using System;
using System.Collections.Generic;
using System.Linq;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using CampfireCards.Ui;
using Photon.Pun;

namespace CampfireCards.Cards
{
    /// <summary>
    /// systems.json Session: host-authoritative picks. The host orders players last-to-the-fire first, deals every hand
    /// and starts the session (net.json PickSessionStart). A player asks for a card (PickRequest); only the host decides,
    /// after checking the session, the turn, the hand and earlier claims, and announces the accepted pick to everyone
    /// (PickMade), which every client checks again before adding the card. An optional timer lets the host pick for anyone
    /// who never answers. Ghosts receive no offer; deaths before choosing are skipped by the host. Campfires queue.
    /// </summary>
    public static class PickSession
    {
        /// <summary>The turn state, identical on every client (Core.PickTurns).</summary>
        public static readonly PickTurns Turns = new PickTurns();
        public static bool Active => Turns.Active;
        public static int SessionId => Turns.SessionId;
        public static byte Reason;
        /// <summary>Time.unscaledTime on this machine when the current turn (turns mode) or the session (simultaneous) began.</summary>
        public static float TurnStartTime;
        public static float TimeoutSeconds = UiTiming.PickTimeoutSeconds;
        public static int CurrentActor => Turns.CurrentActor;

        private static readonly Dictionary<byte, Dictionary<int, double>> Arrivals = new Dictionary<byte, Dictionary<int, double>>();
        private static readonly SessionQueue _queue = new SessionQueue();
        /// <summary>Scouts alive when each campfire was lit, before its RPC can revive anyone. Kept for queued fires.</summary>
        private static readonly Dictionary<byte, HashSet<int>> _campfireEligible = new Dictionary<byte, HashSet<int>>();
        private static readonly HashSet<int> _seenSessions = new HashSet<int>();
        /// <summary>Picks applied from sessions this client never saw start (it joined mid-session): once per player each.</summary>
        private static readonly HashSet<(int session, int actor)> _foreignPicks = new HashSet<(int, int)>();
        private static bool _warnedEmptyDeck;
        private static int _nextSessionId = 1;
        private static bool _openingPending;
        private static double _runStartTime;
        private static int _turnActor = int.MinValue;
        /// <summary>Host: a PickSessionStart was sent and has not come back yet (no second session may start meanwhile).</summary>
        private static int _awaitingStart;
        private static float _awaitingSince;
        /// <summary>Extra seconds after the timeout before the host closes a session that is still stuck.</summary>
        private const float StuckGraceSeconds = 10f;

        public static void Reset()
        {
            Turns.Clear();
            Arrivals.Clear();
            _queue.Reset();
            _campfireEligible.Clear();
            _seenSessions.Clear();
            _foreignPicks.Clear();
            _grants.Clear();
            _devPending.Clear();
            _warnedEmptyDeck = false;
            _openingPending = false;
            _awaitingStart = 0;
            _turnActor = int.MinValue;
            CardPicker.HideIfOpen();
            Hud.HideWaiting();
        }

        public static void OnRunStarted()
        {
            Reset();
            _openingPending = true;
            _runStartTime = PhotonNetwork.Time;
            TimeoutSeconds = PickTiming.Normalize(Plugin.PickTimeoutSeconds != null ? Plugin.PickTimeoutSeconds.Value : UiTiming.PickTimeoutSeconds);
        }

        public static void RecordArrival(int actor, byte segment, double time)
        {
            Dictionary<int, double> d;
            if (!Arrivals.TryGetValue(segment, out d)) { d = new Dictionary<int, double>(); Arrivals[segment] = d; }
            if (!d.ContainsKey(actor)) d[actor] = time;
        }

        private static List<int> ActorsInRoom()
        {
            var list = new List<int>();
            var room = PhotonNetwork.CurrentRoom;
            if (room != null)
            {
                foreach (var p in room.Players.Values) list.Add(p.ActorNumber);
            }
            if (!list.Contains(NetSend.LocalActor)) list.Add(NetSend.LocalActor);
            return list;
        }

        /// <summary>Living scouts can pick. A passed-out scout is still alive; data.dead is PEAK's ghost state.</summary>
        public static bool CanReceiveCard(int actor)
        {
            Character c;
            if (actor == NetSend.LocalActor) c = Character.localCharacter;
            else if (!PlayerHandler.TryGetCharacter(actor, out c)) return false;
            return c != null && c.data != null && !c.isBot && !c.data.dead;
        }

        public static void SnapshotCampfireEligibility(byte segment)
        {
            if (_campfireEligible.ContainsKey(segment)) return;
            _campfireEligible[segment] = new HashSet<int>(ActorsInRoom().Where(CanReceiveCard));
        }

        /// <summary>Host: a session is running or about to (its start has not come back yet).</summary>
        private static bool HostBusy
        {
            get
            {
                if (_awaitingStart != 0 && UnityEngine.Time.unscaledTime - _awaitingSince > 15f) _awaitingStart = 0;   // never came back
                return Turns.Active || _awaitingStart != 0;
            }
        }

        /// <summary>Host: start the opening pick when everyone is awake on the beach (or 30 s passed), and pick for anyone who timed out.</summary>
        public static void HostTick()
        {
            if (!NetSend.IsHost) return;
            if (_openingPending && Deck.Ready)
            {
                Dictionary<int, double> d;
                int arrived = Arrivals.TryGetValue(255, out d) ? d.Count : 0;
                int players = ActorsInRoom().Count;
                bool meReady = d != null && d.ContainsKey(NetSend.LocalActor);
                if ((arrived >= players && meReady) || (meReady && PhotonNetwork.Time - _runStartTime > 30.0))
                {
                    _openingPending = false;
                    RequestSession(0, 255);
                }
            }
            if (!Turns.Active) { HostStartNext(); return; }

            int tickingSession = SessionId;
            foreach (int actor in Turns.Pending.ToList())
            {
                if (!Turns.IsClaimed(actor) && !CanReceiveCard(actor)) HostSkip(actor);
                // Offline delivery can finish this session and start the next one synchronously.
                if (!Turns.Active || SessionId != tickingSession) return;
            }

            // Untimed sessions never auto-pick or close just because someone is still reading.
            if (TimeoutSeconds <= 0f) return;

            float waited = UnityEngine.Time.unscaledTime - TurnStartTime;
            bool picked = false;
            if (PickTiming.HasExpired(TimeoutSeconds, waited))
            {
                // timeout picks are host picks like any other: claimed first, so a late request from the player loses
                var due = Turns.Mode == PickMode.Turns ? new List<int> { Turns.CurrentActor } : Turns.Pending.ToList();
                foreach (int actor in due)
                {
                    if (actor == int.MinValue || Turns.IsClaimed(actor)) continue;
                    var hand = Turns.HandOf(actor);
                    if (hand.Length == 0) continue;
                    Plugin.Verbose($"Pick timeout for actor {actor}: waited {waited:0.0}s of {TimeoutSeconds}s");
                    var rng = DealRandom.Shared;
                    HostAccept(actor, hand[rng.Next(hand.Length)], auto: true);
                    picked = true;
                    if (!Turns.Active || SessionId != tickingSession) return;
                }
            }
            // a pick may have ended this session and (offline, synchronously) started the next one: never judge that
            // one by this session's clock
            if (picked || !Turns.Active) return;
            if (waited > TimeoutSeconds + StuckGraceSeconds)
            {
                // an accepted pick never came back (or the state is otherwise stuck): close the session so the queue moves on
                Plugin.Log.LogWarning($"Pick session {SessionId} stuck; closing it");
                NetSend.Raise(new SessionEnd { sessionId = SessionId }, Photon.Realtime.ReceiverGroup.All);
                TurnStartTime = UnityEngine.Time.unscaledTime;   // one SessionEnd per grace period until it lands
            }
        }

        /// <summary>
        /// Everyone: a session is due. reason 0 = opening (segment 255), 1 = campfire (segment = Campfire.advanceToSegment).
        /// Each segment gets exactly one session per run; while another session runs, it waits in the queue. Every client
        /// keeps the same queue (sessions leave it when their PickSessionStart arrives), and only the host deals.
        /// </summary>
        public static void RequestSession(byte reason, byte segment)
        {
            if (!_queue.Request(reason, segment))
            {
                Plugin.Verbose($"Pick session for segment {segment} already started or queued");
                return;
            }
            if (!NetSend.IsHost) return;
            if (HostBusy) Plugin.Log.LogInfo($"Pick session for segment {segment} queued behind the current one");
            HostStartNext();
        }

        /// <summary>Host: deal the oldest waiting session if nothing is running.</summary>
        private static void HostStartNext()
        {
            if (!NetSend.IsHost || HostBusy || _queue.Count == 0) return;
            if (!Deck.Ready)
            {
                if (!_warnedEmptyDeck) Plugin.Log.LogWarning("No pick session: the deck is empty");
                _warnedEmptyDeck = true;
                return;
            }
            SessionQueue.Entry next;
            if (!_queue.TryNext(out next)) return;
            Deal(next.Reason, next.Segment);
        }

        private static void Deal(byte reason, byte segment)
        {
            HashSet<int> eligible;
            _campfireEligible.TryGetValue(segment, out eligible);
            var actors = ActorsInRoom().Where(a => CanReceiveCard(a) && (reason != 1 || eligible == null || eligible.Contains(a))).ToList();
            Dictionary<int, double> arr;
            Arrivals.TryGetValue(segment, out arr);
            // last to arrive picks first; players with no recorded arrival are treated as the latest
            actors.Sort((a, b) =>
            {
                double ta = arr != null && arr.ContainsKey(a) ? arr[a] : double.MaxValue;
                double tb = arr != null && arr.ContainsKey(b) ? arr[b] : double.MaxValue;
                int cmp = tb.CompareTo(ta);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            int id = _nextSessionId++ + NetSend.LocalActor * 10000;
            int handSize = Math.Max(1, CardSettings.Current.OfferCount);
            bool solo = NetSend.IsSolo;
            var claimedMythics = Effects.ClaimedMythics();
            var hands = new List<byte>();
            foreach (var actor in actors)
            {
                // Phoenix Clause: skip this pick. Scout's Dishonor: a reduced offer (its nextOffer param, at most the
                // normal hand). Both flags are consumed by this session.
                byte flags = Effects.Flags(actor);
                int reduced = Math.Min(handSize, Math.Max(1, (int)Math.Round(CardSettings.Current.Param(Effects.DoubleDownCard(), "nextOffer", 1f))));
                int count = (flags & Effects.FlagSkipNext) != 0 ? 0 : (flags & Effects.FlagOfferOne) != 0 ? reduced : handSize;
                var hand = count > 0 ? Deck.Deal(DealRandom.Shared, Effects.CardsOf(actor), count, solo, claimedMythics) : new byte[0];
                var fixedHand = new byte[handSize];
                for (int i = 0; i < fixedHand.Length; i++) fixedHand[i] = i < hand.Length ? hand[i] : (byte)0;
                hands.AddRange(fixedHand);
            }
            _awaitingStart = id;
            _awaitingSince = UnityEngine.Time.unscaledTime;
            var mode = Plugin.PickModeSetting != null ? Plugin.PickModeSetting.Value : PickMode.Simultaneous;
            NetSend.Raise(new PickSessionStart
            {
                sessionId = id, reason = reason, order = actors.ToArray(), hands = hands.ToArray(),
                handSize = (byte)handSize, timeoutSeconds = TimeoutSeconds, mode = (byte)mode, segment = segment
            }, Photon.Realtime.ReceiverGroup.All);
        }

        public static void OnStart(PickSessionStart m)
        {
            if (m.sessionId == _awaitingStart) _awaitingStart = 0;
            // a replayed or out-of-date start never restarts a session
            if (!_seenSessions.Add(m.sessionId)) { Plugin.Verbose($"Ignored repeated session start {m.sessionId}"); return; }
            _queue.MarkStarted(m.segment);
            if (m.segment == 255) _openingPending = false;   // a later host never deals the opening again
            if (m.order == null || m.hands == null || m.handSize == 0 || m.handSize > CardSettings.MaxOfferCount || m.hands.Length < m.order.Length * m.handSize)
            {
                Plugin.Log.LogWarning($"Ignored malformed session start {m.sessionId}");
                return;
            }
            if (Turns.Active) Plugin.Log.LogWarning($"Session {m.sessionId} replaces unfinished session {Turns.SessionId}");

            var hands = new List<byte[]>();
            for (int i = 0; i < m.order.Length; i++)
            {
                var hand = new List<byte>();
                for (int k = 0; k < m.handSize; k++)
                {
                    byte id = m.hands[i * m.handSize + k];
                    if (id != 0 && CardSettings.Current.Card(id) != null && !hand.Contains(id)) hand.Add(id);
                }
                hands.Add(hand.ToArray());
            }
            var mode = m.mode == (byte)PickMode.Turns ? PickMode.Turns : PickMode.Simultaneous;
            Turns.Start(m.sessionId, m.order, hands, mode);
            Reason = m.reason;
            TimeoutSeconds = PickTiming.Normalize(m.timeoutSeconds);
            _turnActor = int.MinValue;
            TurnStartTime = UnityEngine.Time.unscaledTime;
            // the skip / one-card flags were used to deal this session: consume them everywhere
            foreach (var actor in m.order)
            {
                if ((Effects.Flags(actor) & Effects.FlagSkipNext) != 0)
                    Hud.Toast(actor == NetSend.LocalActor
                        ? string.Format(Ui_Toast.SkipYouTemplate, Deck.NameOfAbility("PhoenixClause"))
                        : string.Format(Ui_Toast.SkipTemplate, NetSend.NameOf(actor), Deck.NameOfAbility("PhoenixClause")));
                Effects.SetFlag(actor, (byte)(Effects.FlagSkipNext | Effects.FlagOfferOne), false);
            }
            Plugin.Verbose($"Pick session {SessionId} reason {Reason} mode {mode}: order {string.Join(",", Turns.Order)}");
            Refresh(sessionStarted: true);
        }

        /// <summary>Show the picker to whoever may choose now, the status banner to everyone else; end when everyone chose.</summary>
        private static void Refresh(bool sessionStarted = false)
        {
            if (!Turns.Active) return;
            if (Turns.AllDone) { End(); return; }
            int me = NetSend.LocalActor;
            if (Turns.Mode == PickMode.Turns)
            {
                int current = Turns.CurrentActor;
                if (current != _turnActor)
                {
                    _turnActor = current;
                    TurnStartTime = UnityEngine.Time.unscaledTime;
                    Plugin.Verbose($"Turn: actor {current} picks (hand {string.Join(",", Turns.HandOf(current))}), timeout {TimeoutSeconds}s");
                    if (current == me) ShowMyHand();
                    else HidePickFan();
                }
            }
            else if (sessionStarted && Turns.CanChoose(me))
            {
                ShowMyHand();
            }
            UpdateBanner();
        }

        private static void ShowMyHand()
        {
            if (!CanReceiveCard(NetSend.LocalActor)) { HidePickFan(); return; }
            Hud.HideWaiting();
            float left = TimeoutSeconds <= 0f ? 0f : Math.Max(1f, TimeoutSeconds - (UnityEngine.Time.unscaledTime - TurnStartTime));
            CardPicker.Ensure().Show(Reason, Turns.HandOf(NetSend.LocalActor), left, CardPicker.Mode.Pick);
        }

        /// <summary>The banner for everyone not choosing right now: who is choosing, the time left, and the pick order.</summary>
        public static void UpdateBanner()
        {
            if (!Turns.Active || Turns.AllDone) { Hud.HideWaiting(); return; }
            int me = NetSend.LocalActor;
            if (!CanReceiveCard(me)) HidePickFan();
            bool choosingNow = Turns.CanChoose(me) && !Turns.IsClaimed(me) && CardPicker.Instance != null && CardPicker.Instance.isOpen
                               && CardPicker.Instance.Current == CardPicker.Mode.Pick;
            if (choosingNow) { Hud.HideWaiting(); return; }
            string status;
            if (Turns.Mode == PickMode.Turns)
                status = string.Format(Ui_WaitingBanner.Template, NetSend.NameOf(Turns.CurrentActor));
            else
                status = string.Format(Ui_WaitingBanner.SimultaneousTemplate, string.Join(", ", Turns.Pending.Select(NetSend.NameOf)));
            int left = Math.Max(0, (int)Math.Ceiling(TimeoutSeconds - (UnityEngine.Time.unscaledTime - TurnStartTime)));
            // Turns: the order players pick in. Simultaneous: everyone picks at once, so it is the deal order (first claim on a Mythic).
            string label = Turns.Mode == PickMode.Turns ? Ui_WaitingBanner.OrderLabel : Ui_WaitingBanner.DealOrderLabel;
            var order = new System.Text.StringBuilder(label);
            for (int i = 0; i < Turns.Order.Count; i++)
            {
                int a = Turns.Order[i];
                if (Turns.HandOf(a).Length == 0) continue;   // skipped this pick (Phoenix Clause)
                bool done = Turns.HasPicked(a);
                bool picking = !done && Turns.CanChoose(a);
                var color = done ? Ui_WaitingBanner.DoneColor : picking ? Ui_WaitingBanner.PickingColor : Ui_WaitingBanner.QueuedColor;
                if (order.Length > label.Length) order.Append(Ui_WaitingBanner.Separator);
                order.Append(UiKit.ColorTag(color)).Append(a == me ? Ui_Toast.You : NetSend.NameOf(a)).Append("</color>");
            }
            Hud.ShowWaiting(TimeoutSeconds > 0f ? string.Format(Ui_WaitingBanner.StatusTemplate, status, left) : status, order.ToString());
        }

        // ------------------------------------------------------------------ picks
        /// <summary>
        /// The local player clicked a card: ask the host (the host itself decides directly). False when nothing was sent
        /// (not this player's turn, or their pick is already claimed); the picker then stays choosable.
        /// </summary>
        public static bool LocalPick(byte cardId)
        {
            int me = NetSend.LocalActor;
            if (!CanReceiveCard(me)) { HidePickFan(); return false; }
            if (!Turns.CanChoose(me) || Turns.IsClaimed(me)) return false;
            Art.Sfx.Play("cardPick");
            if (NetSend.IsHost) HostOnRequest(new PickRequest { sessionId = SessionId, cardId = cardId }, me);
            else NetSend.RaiseToMaster(new PickRequest { sessionId = SessionId, cardId = cardId });
            return true;
        }

        /// <summary>Host: a player's request. Everything is checked here; the sender can only ever pick for themselves.</summary>
        public static void HostOnRequest(PickRequest m, int sender)
        {
            if (!NetSend.IsHost) return;
            var verdict = Turns.CheckRequest(m.sessionId, sender, m.cardId);
            if (verdict != PickVerdict.Ok)
            {
                Plugin.Log.LogWarning($"Refused pick from actor {sender}: card {m.cardId}, session {m.sessionId} (current {SessionId}): {verdict}");
                return;
            }
            if (!CanReceiveCard(sender)) { HostSkip(sender); return; }
            HostAccept(sender, m.cardId, auto: false);
        }

        /// <summary>Host: claim the turn and announce the accepted pick to everyone (including itself).</summary>
        private static void HostAccept(int actor, byte cardId, bool auto)
        {
            if (!CanReceiveCard(actor)) { HostSkip(actor); return; }
            Turns.Claim(actor);
            NetSend.Raise(new PickMade { sessionId = SessionId, actor = actor, cardId = cardId, auto = auto }, Photon.Realtime.ReceiverGroup.All);
        }

        /// <summary>Everyone: the host announced a pick. Checked again (stale, replayed or impossible picks change nothing).</summary>
        public static void OnPick(PickMade m)
        {
            var verdict = Turns.CheckPick(m.sessionId, m.actor, m.cardId);
            if (verdict != PickVerdict.Ok)
            {
                // joined during a session: its start never reached us, but the host's pick (checked as from the host)
                // still belongs in our copy of that player's cards; once per player per session
                if ((verdict == PickVerdict.NoSession || verdict == PickVerdict.StaleSession)
                    && !_seenSessions.Contains(m.sessionId) && _foreignPicks.Add((m.sessionId, m.actor)))
                {
                    Plugin.Verbose($"Pick from a session this client did not see: actor {m.actor} card {m.cardId}");
                    Effects.AddCard(m.actor, m.cardId, live: true);
                    return;
                }
                Plugin.Verbose($"Ignored pick: actor {m.actor} card {m.cardId} session {m.sessionId}: {verdict}");
                return;
            }
            Turns.Apply(m.actor);
            Plugin.Verbose($"Pick: actor {m.actor} card {m.cardId} auto={m.auto} session {m.sessionId}");
            bool added = Effects.AddCard(m.actor, m.cardId, live: true);
            if (added && NetSend.IsHost) HostGrantBonus(m.actor, m.cardId, m.sessionId);
            string who = m.actor == NetSend.LocalActor ? Ui_Toast.You : NetSend.NameOf(m.actor);
            Hud.Toast(string.Format(m.auto ? Ui_Toast.AutoTookTemplate : Ui_Toast.TookTemplate, who, Deck.NameOf(m.cardId)));
            if (m.actor == NetSend.LocalActor) HidePickFan();
            Refresh();
        }

        /// <summary>
        /// Host: a card-draw card (Double Deal, Double Down) was taken; deal its bonus cards right away. Bonus cards are
        /// never card-draw cards themselves, so draws cannot chain, and never a Mythic still on offer to someone else.
        /// One grant per accepted pick (the pick is applied once).
        /// </summary>
        private static void HostGrantBonus(int actor, byte cardId, int sessionId)
        {
            var def = CardSettings.Current.Card(cardId);
            if (def == null || !def.DrawsCards) return;
            int count = Math.Max(0, (int)Math.Round(CardSettings.Current.Param(def, "bonusCards", 1f)));
            if (count == 0) return;
            var maxRarity = def.Ability == "DoubleDown" ? CardRarity.Rare : CardRarity.Mythic;
            var cards = Deck.DealBonus(DealRandom.Shared, Effects.CardsOf(actor), count, NetSend.IsSolo, maxRarity, Turns.CardsStillOffered());
            if (cards.Length == 0) return;
            NetSend.Raise(new CardsGranted { sessionId = sessionId, actor = actor, cards = cards, reason = 1 }, Photon.Realtime.ReceiverGroup.All);
        }

        /// <summary>Tester grants raised but not echoed back yet: a second click on the same card must wait, or a
        /// card-draw card would deal its bonus cards twice (online, the echo takes a round trip).</summary>
        private static readonly HashSet<byte> _devPending = new HashSet<byte>();

        /// <summary>
        /// Host dev tester (F6 card menu): grant the local player one card WITH its acquisition effects (reason 10, so
        /// clicking again after a remove is never dropped as a repeated grant). A card-draw card also deals its bonus
        /// cards right away, counting the just-granted card as owned (its own grant may not have echoed back yet).
        /// Refused with a toast: a card already in the build, a Mythic a teammate owns, and a card still on offer in the
        /// current pick session (granting it would let AddCard refuse that player's accepted pick, costing them the pick).
        /// </summary>
        public static void DevGrantSelf(byte id)
        {
            if (!NetSend.IsHost) return;
            int actor = NetSend.LocalActor;
            var def = CardSettings.Current.Card(id);
            if (def == null) return;
            if (Effects.CardsOf(actor).Contains(id) && (!CardSettings.Current.AllowDuplicates || def.Rarity == CardRarity.Mythic))
            {
                Hud.Toast(def.Name + " is already in your build");
                return;
            }
            if (def.Rarity == CardRarity.Mythic)
            {
                int owner = Effects.MythicOwner(id);
                if (owner != int.MinValue) { Hud.Toast(def.Name + " is already owned by " + NetSend.NameOf(owner)); return; }
            }
            if (Turns.Active && Turns.CardsStillOffered().Contains(id))
            {
                Hud.Toast(def.Name + " is still on offer in the current card pick");
                return;
            }
            if (!_devPending.Add(id)) return;   // clicked again before the first grant came back
            NetSend.Raise(new CardsGranted { sessionId = SessionId, actor = actor, cards = new[] { id }, reason = 10 }, Photon.Realtime.ReceiverGroup.All);
            if (!def.DrawsCards) return;
            int count = Math.Max(0, (int)Math.Round(CardSettings.Current.Param(def, "bonusCards", 1f)));
            if (count == 0) return;
            var owned = new List<byte>(Effects.CardsOf(actor)) { id };
            var maxRarity = def.Ability == "DoubleDown" ? CardRarity.Rare : CardRarity.Mythic;
            var bonus = Deck.DealBonus(DealRandom.Shared, owned, count, NetSend.IsSolo, maxRarity, Turns.CardsStillOffered());
            if (bonus.Length > 0)
                NetSend.Raise(new CardsGranted { sessionId = SessionId, actor = actor, cards = bonus, reason = 10 }, Photon.Realtime.ReceiverGroup.All);
        }

        private static readonly HashSet<(int session, int actor, byte reason)> _grants = new HashSet<(int, int, byte)>();

        /// <summary>Everyone: bonus cards from the host. A repeated grant (same session, player and reason) adds nothing.</summary>
        public static void OnCardsGranted(CardsGranted m)
        {
            if (m.cards == null || m.cards.Length == 0) return;
            // the tester grant is back (accepted or not): the card may be clicked again
            if (m.reason == 10 && m.actor == NetSend.LocalActor)
                foreach (var id in m.cards) _devPending.Remove(id);
            if (m.reason != 9 && m.reason != 10 && !_grants.Add((m.sessionId, m.actor, m.reason)))   // 9/10 = dev keys, may repeat
            {
                Plugin.Log.LogWarning($"Ignored repeated card grant for actor {m.actor} in session {m.sessionId}");
                return;
            }
            var names = new List<string>();
            var got = new List<byte>();
            foreach (var id in m.cards)
            {
                // reason 9 = the host's F8 restore: the card comes back WITHOUT its pick-time effects (no Curse, no new
                // Clown penalty, no sacrifice screen, no one-card flag) and may return a Mythic the deck counts as taken
                if (!Effects.AddCard(m.actor, id, live: m.reason != 9)) continue;
                got.Add(id);
                names.Add(Deck.NameOf(id));
            }
            if (got.Count == 0) return;
            if (m.actor == NetSend.LocalActor)
            {
                Hud.Toast(string.Format(Ui_Toast.BonusTemplate, Ui_Toast.You, string.Join(", ", names)));
                CardPicker.Ensure().ShowReveal(got.ToArray());
            }
            else Hud.Toast(string.Format(Ui_Toast.BonusTemplate, NetSend.NameOf(m.actor), string.Join(", ", names)));
        }

        public static void OnSessionEnd(SessionEnd m)
        {
            if (Turns.Active && m.sessionId == SessionId) End();
        }

        /// <summary>Remove an ineligible pending scout on every client so a ghost cannot hold up an untimed session.</summary>
        private static void HostSkip(int actor)
        {
            if (!NetSend.IsHost || !Turns.IsPending(actor) || Turns.IsClaimed(actor)) return;
            var m = new PickSkipped { sessionId = SessionId, actor = actor };
            NetSend.Raise(m, Photon.Realtime.ReceiverGroup.All);
            OnSkipped(m);   // update host state now; the echoed event is harmless
        }

        public static void OnSkipped(PickSkipped m)
        {
            if (!Turns.Skip(m.sessionId, m.actor)) return;
            Plugin.Verbose($"Actor {m.actor} no longer eligible for pick session {m.sessionId}");
            if (m.actor == NetSend.LocalActor) HidePickFan();
            Refresh();
        }

        /// <summary>A player left the room: they no longer hold up the session (and the host's queue moves on).</summary>
        public static void OnPlayerLeft(int actor)
        {
            if (!Turns.Active || !Turns.RemoveActor(actor)) return;
            Plugin.Verbose($"Actor {actor} left during pick session {SessionId}");
            Refresh();
        }

        private static void End()
        {
            Turns.Clear();
            _turnActor = int.MinValue;
            Hud.HideWaiting();
            HidePickFan();
            if (NetSend.IsHost) HostStartNext();
        }

        /// <summary>Close the picker only if it shows an offer (a bonus reveal or a sacrifice stays open).</summary>
        private static void HidePickFan()
        {
            var p = CardPicker.Instance;
            if (p != null && p.isOpen && p.Current == CardPicker.Mode.Pick) CardPicker.HideIfOpen();
        }
    }
}
