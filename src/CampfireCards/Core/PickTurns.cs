using System.Collections.Generic;

namespace CampfireCards.Core
{
    /// <summary>How a pick session runs. The host's choice is sent with every session, so clients always follow it.</summary>
    public enum PickMode : byte
    {
        /// <summary>Everyone chooses at the same time; the arrival order still decides who is dealt first and is shown.</summary>
        Simultaneous = 0,
        /// <summary>One player at a time, last to the fire first.</summary>
        Turns = 1
    }

    /// <summary>Why a pick was refused (Ok = accepted).</summary>
    public enum PickVerdict { Ok, NoSession, StaleSession, NotInSession, NotYourTurn, NotInHand, AlreadyPicked, AlreadyClaimed }

    /// <summary>
    /// The turn state of one pick session. Every client builds the same state from the host's PickSessionStart and
    /// advances it only with the host's PickMade, so any client can take over as host. The host additionally "claims" a
    /// player's turn the moment it accepts a request, so a duplicate or replayed request is refused before the
    /// accepted pick has come back. Unity-free so it can be unit tested.
    /// </summary>
    public sealed class PickTurns
    {
        public bool Active { get; private set; }
        public int SessionId { get; private set; }
        public PickMode Mode { get; private set; }
        /// <summary>Pick order: last to the fire first (also the dealing order, which decides who may see a Mythic).</summary>
        public readonly List<int> Order = new List<int>();

        private readonly Dictionary<int, byte[]> _hands = new Dictionary<int, byte[]>();
        private readonly HashSet<int> _picked = new HashSet<int>();
        private readonly HashSet<int> _claimed = new HashSet<int>();

        public void Start(int sessionId, IList<int> order, IList<byte[]> hands, PickMode mode)
        {
            Clear();
            Active = true;
            SessionId = sessionId;
            Mode = mode;
            for (int i = 0; i < order.Count; i++)
            {
                if (_hands.ContainsKey(order[i])) continue;   // a malformed order never lists a player twice
                Order.Add(order[i]);
                _hands[order[i]] = i < hands.Count && hands[i] != null ? hands[i] : new byte[0];
            }
        }

        /// <summary>The session is over; the id is kept so state syncs and stale events can still refer to it.</summary>
        public void Clear()
        {
            Active = false;
            Order.Clear();
            _hands.Clear();
            _picked.Clear();
            _claimed.Clear();
        }

        public byte[] HandOf(int actor)
        {
            byte[] h;
            return _hands.TryGetValue(actor, out h) ? h : new byte[0];
        }

        public bool HasPicked(int actor) => _picked.Contains(actor);

        /// <summary>Still has to choose: in the session, dealt at least one card, no accepted pick yet.</summary>
        public bool IsPending(int actor) => Active && _hands.ContainsKey(actor) && HandOf(actor).Length > 0 && !_picked.Contains(actor);

        public IEnumerable<int> Pending
        {
            get { foreach (var a in Order) if (IsPending(a)) yield return a; }
        }

        public int PendingCount
        {
            get { int n = 0; foreach (var a in Order) if (IsPending(a)) n++; return n; }
        }

        public bool AllDone => Active && PendingCount == 0;

        /// <summary>Turns mode: the first player in order who still has to choose. Simultaneous: nobody in particular.</summary>
        public int CurrentActor
        {
            get
            {
                if (!Active || Mode != PickMode.Turns) return int.MinValue;
                foreach (var a in Order) if (IsPending(a)) return a;
                return int.MinValue;
            }
        }

        /// <summary>May this player choose right now?</summary>
        public bool CanChoose(int actor) => IsPending(actor) && (Mode == PickMode.Simultaneous || CurrentActor == actor);

        /// <summary>Host: a player asks to take a card. Checked against the session, the turn, the hand and earlier claims.</summary>
        public PickVerdict CheckRequest(int sessionId, int actor, byte cardId)
        {
            var v = CheckPick(sessionId, actor, cardId);
            if (v != PickVerdict.Ok) return v;
            return _claimed.Contains(actor) ? PickVerdict.AlreadyClaimed : PickVerdict.Ok;
        }

        /// <summary>Everyone: the host announced an accepted pick. Stale, replayed or impossible picks are refused.</summary>
        public PickVerdict CheckPick(int sessionId, int actor, byte cardId)
        {
            if (!Active) return PickVerdict.NoSession;
            if (sessionId != SessionId) return PickVerdict.StaleSession;
            if (!_hands.ContainsKey(actor)) return PickVerdict.NotInSession;
            if (_picked.Contains(actor)) return PickVerdict.AlreadyPicked;
            if (System.Array.IndexOf(HandOf(actor), cardId) < 0) return PickVerdict.NotInHand;
            if (Mode == PickMode.Turns && CurrentActor != actor) return PickVerdict.NotYourTurn;
            return PickVerdict.Ok;
        }

        /// <summary>Host: the request was accepted and its PickMade is on the way; refuse any second request.</summary>
        public void Claim(int actor) { if (Active) _claimed.Add(actor); }

        public bool IsClaimed(int actor) => _claimed.Contains(actor);

        /// <summary>A host-approved skip (death before choosing). Replays and skips from an older session do nothing.</summary>
        public bool Skip(int sessionId, int actor)
        {
            if (!Active || sessionId != SessionId || !IsPending(actor) || IsClaimed(actor)) return false;
            return RemoveActor(actor);
        }

        /// <summary>The accepted pick arrived: the player is done.</summary>
        public void Apply(int actor)
        {
            if (!Active) return;
            _picked.Add(actor);
            _claimed.Remove(actor);
        }

        /// <summary>A player left the room: they no longer hold up the session. Returns true if they were in it.</summary>
        public bool RemoveActor(int actor)
        {
            if (!_hands.ContainsKey(actor)) return false;
            Order.Remove(actor);
            _hands.Remove(actor);
            _picked.Remove(actor);
            _claimed.Remove(actor);
            return true;
        }

        /// <summary>Cards still on offer to players who have not chosen yet (a bonus deal must not give one of their Mythics away).</summary>
        public HashSet<byte> CardsStillOffered()
        {
            var set = new HashSet<byte>();
            if (!Active) return set;
            foreach (var kv in _hands)
                if (!_picked.Contains(kv.Key))
                    foreach (var id in kv.Value) set.Add(id);
            return set;
        }
    }

    /// <summary>
    /// Host: pick sessions waiting to be dealt, in the order they were asked for. A campfire (segment) gets exactly one
    /// session per run: a second event from the same fire is ignored, and a fire lit while another session is still
    /// running waits here instead of being skipped. A fire only counts as done once its session really starts.
    /// </summary>
    public sealed class SessionQueue
    {
        public struct Entry { public byte Reason; public byte Segment; }

        private readonly HashSet<byte> _started = new HashSet<byte>();
        private readonly List<Entry> _waiting = new List<Entry>();

        public int Count => _waiting.Count;

        /// <summary>Ask for a session. False when this segment already had or is already waiting for one.</summary>
        public bool Request(byte reason, byte segment)
        {
            if (_started.Contains(segment)) return false;
            foreach (var e in _waiting) if (e.Segment == segment) return false;
            _waiting.Add(new Entry { Reason = reason, Segment = segment });
            return true;
        }

        /// <summary>The next session to deal (oldest first); its segment is marked done now.</summary>
        public bool TryNext(out Entry next)
        {
            if (_waiting.Count == 0) { next = default(Entry); return false; }
            next = _waiting[0];
            _waiting.RemoveAt(0);
            _started.Add(next.Segment);
            return true;
        }

        public bool HasStarted(byte segment) => _started.Contains(segment);

        /// <summary>
        /// A session for this segment started (seen by a client in PickSessionStart). Every client mirrors the host's queue
        /// this way, so after a host migration the new host continues with exactly the sessions still waiting.
        /// </summary>
        public void MarkStarted(byte segment)
        {
            _waiting.RemoveAll(e => e.Segment == segment);
            _started.Add(segment);
        }

        public void Reset() { _started.Clear(); _waiting.Clear(); }
    }
}
