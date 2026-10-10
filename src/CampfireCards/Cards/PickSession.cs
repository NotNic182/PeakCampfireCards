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
    /// systems.json Session: sequential picks. The host orders players last-to-the-fire first, deals every hand
    /// and starts the session (net.json PickSessionStart); each player picks on their turn (PickMade) while the others see
    /// "X is picking"; a safety timer auto-picks for a player who never answers.
    /// </summary>
    public static class PickSession
    {
        public static bool Active;
        public static int SessionId;
        public static byte Reason;
        public static readonly List<int> Order = new List<int>();
        public static readonly Dictionary<int, byte[]> Hands = new Dictionary<int, byte[]>();
        public static int TurnIndex;
        public static float TurnStartTime;   // Time.unscaledTime on this machine (the host times out on its own clock)
        public static float TimeoutSeconds = UiTiming.PickTimeoutSeconds;

        private static readonly Dictionary<byte, Dictionary<int, double>> Arrivals = new Dictionary<byte, Dictionary<int, double>>();
        private static int _nextSessionId = 1;
        private static bool _openingPending, _openingDone;
        private static double _runStartTime;
        private static readonly HashSet<byte> _firesDone = new HashSet<byte>();

        public static int CurrentActor => Active && TurnIndex < Order.Count ? Order[TurnIndex] : int.MinValue;

        public static void Reset()
        {
            Active = false; Order.Clear(); Hands.Clear(); TurnIndex = 0;
            Arrivals.Clear(); _firesDone.Clear();
            _openingPending = false; _openingDone = false;
            CardPicker.HideIfOpen();
            Hud.HideWaiting();
        }

        public static void OnRunStarted()
        {
            Reset();
            _openingPending = true;
            _runStartTime = PhotonNetwork.Time;
            TimeoutSeconds = Plugin.PickTimeoutSeconds != null ? Plugin.PickTimeoutSeconds.Value : UiTiming.PickTimeoutSeconds;
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

        /// <summary>Host: start the opening pick when everyone is awake on the beach (or 30 s passed), and auto-pick on timeout.</summary>
        public static void HostTick()
        {
            if (!NetSend.IsHost) return;
            if (_openingPending && !_openingDone && !Active && Deck.Ready)
            {
                Dictionary<int, double> d;
                int arrived = Arrivals.TryGetValue(255, out d) ? d.Count : 0;
                int players = ActorsInRoom().Count;
                bool meReady = d != null && d.ContainsKey(NetSend.LocalActor);
                if ((arrived >= players && meReady) || (meReady && PhotonNetwork.Time - _runStartTime > 30.0))
                {
                    _openingDone = true; _openingPending = false;
                    HostStart(0, 255);
                }
            }
            if (Active && UnityEngine.Time.unscaledTime - TurnStartTime > TimeoutSeconds)
            {
                int actor = CurrentActor;
                byte[] hand;
                if (actor != int.MinValue && Hands.TryGetValue(actor, out hand) && hand.Length > 0)
                {
                    Plugin.Verbose($"Pick timeout for actor {actor}: now={UnityEngine.Time.unscaledTime:0.0} turnStart={TurnStartTime:0.0} timeout={TimeoutSeconds}");
                    var rng = new Random(SessionId * 31 + TurnIndex);
                    NetSend.Raise(new PickMade { sessionId = SessionId, actor = actor, cardId = hand[rng.Next(hand.Length)], auto = true }, Photon.Realtime.ReceiverGroup.All);
                    TurnStartTime = UnityEngine.Time.unscaledTime; // avoid double auto-picks before the event comes back
                }
                else
                {
                    NetSend.Raise(new SessionEnd { sessionId = SessionId }, Photon.Realtime.ReceiverGroup.All);
                }
            }
        }

        /// <summary>Host: deal and announce a session. reason 0 = opening, 1 = campfire (segment = Campfire.advanceToSegment).</summary>
        public static void HostStart(byte reason, byte segment)
        {
            if (!NetSend.IsHost) return;
            if (!Deck.Ready) { Plugin.Log.LogWarning("No pick session: the deck is empty"); return; }
            if (reason == 1 && _firesDone.Contains(segment)) return;
            if (reason == 1) _firesDone.Add(segment);
            if (Active) { Plugin.Log.LogWarning("Pick session already active; skipping"); return; }

            var actors = ActorsInRoom();
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
                // Phoenix Clause: skip this pick. Double Down: a one-card offer. Both flags are consumed by this session.
                byte flags = Effects.Flags(actor);
                int count = (flags & Effects.FlagSkipNext) != 0 ? 0 : (flags & Effects.FlagOfferOne) != 0 ? 1 : handSize;
                var hand = count > 0 ? Deck.Deal(new Random(id * 7919 + actor), Effects.CardsOf(actor), count, solo, claimedMythics) : new byte[0];
                var fixedHand = new byte[handSize];
                for (int i = 0; i < fixedHand.Length; i++) fixedHand[i] = i < hand.Length ? hand[i] : (byte)0;
                hands.AddRange(fixedHand);
            }
            NetSend.Raise(new PickSessionStart
            {
                sessionId = id, reason = reason, order = actors.ToArray(), hands = hands.ToArray(),
                handSize = (byte)handSize, timeoutSeconds = TimeoutSeconds
            }, Photon.Realtime.ReceiverGroup.All);
        }

        public static void OnStart(PickSessionStart m)
        {
            Active = true; SessionId = m.sessionId; Reason = m.reason;
            Order.Clear(); Order.AddRange(m.order);
            Hands.Clear();
            for (int i = 0; i < m.order.Length; i++)
            {
                var hand = new List<byte>();
                for (int k = 0; k < m.handSize; k++)
                {
                    int idx = i * m.handSize + k;
                    if (idx < m.hands.Length && m.hands[idx] != 0) hand.Add(m.hands[idx]);
                }
                Hands[m.order[i]] = hand.ToArray();
            }
            TimeoutSeconds = m.timeoutSeconds > 0 ? m.timeoutSeconds : TimeoutSeconds;
            TurnIndex = 0;
            // the skip / one-card flags were used to deal this session: consume them everywhere
            foreach (var actor in m.order)
            {
                if ((Effects.Flags(actor) & Effects.FlagSkipNext) != 0)
                    Hud.Toast((actor == NetSend.LocalActor ? "You skip" : NetSend.NameOf(actor) + " skips") + " this pick (Phoenix Clause)");
                Effects.SetFlag(actor, (byte)(Effects.FlagSkipNext | Effects.FlagOfferOne), false);
            }
            Plugin.Verbose($"Pick session {SessionId} reason {Reason}: order {string.Join(",", Order)}");
            BeginTurn();
        }

        private static void BeginTurn()
        {
            while (TurnIndex < Order.Count)
            {
                int actor = Order[TurnIndex];
                byte[] hand;
                if (!Hands.TryGetValue(actor, out hand) || hand.Length == 0) { TurnIndex++; continue; }
                TurnStartTime = UnityEngine.Time.unscaledTime;
                Plugin.Verbose($"Turn {TurnIndex}: actor {actor} picks (hand {string.Join(",", hand)}), timeout {TimeoutSeconds}s");
                if (actor == NetSend.LocalActor)
                {
                    Hud.HideWaiting();
                    CardPicker.Ensure().Show(Reason, hand, TimeoutSeconds, CardPicker.Mode.Pick);
                }
                else
                {
                    CardPicker.HideIfOpen();
                    Hud.ShowWaiting(NetSend.NameOf(actor));
                }
                return;
            }
            End();
        }

        public static void OnPick(PickMade m)
        {
            if (Active && m.sessionId == SessionId && CurrentActor != m.actor)
            {
                Plugin.Verbose($"Ignored out-of-turn pick: actor {m.actor} card {m.cardId} (current {CurrentActor})");
                return;
            }
            Plugin.Verbose($"Pick: actor {m.actor} card {m.cardId} auto={m.auto} session {m.sessionId}");
            bool added = Effects.AddCard(m.actor, m.cardId, live: true);
            if (added && NetSend.IsHost) HostGrantBonus(m.actor, m.cardId, m.sessionId);
            string who = m.actor == NetSend.LocalActor ? "You" : NetSend.NameOf(m.actor);
            Hud.Toast(string.Format(Ui_Toast.TookTemplate, who, Deck.NameOf(m.cardId)) + (m.auto ? " (time ran out)" : ""));
            if (m.actor == NetSend.LocalActor) CardPicker.HideIfOpen();
            if (Active && m.sessionId == SessionId && TurnIndex < Order.Count && Order[TurnIndex] == m.actor)
            {
                TurnIndex++;
                BeginTurn();
            }
        }

        /// <summary>
        /// Host: a card-draw card (Double Deal, Double Down) was taken; deal its bonus cards right away. Bonus cards are
        /// never card-draw cards themselves, so draws cannot chain.
        /// </summary>
        private static void HostGrantBonus(int actor, byte cardId, int sessionId)
        {
            var def = CardSettings.Current.Card(cardId);
            if (def == null || !def.DrawsCards) return;
            int count = Math.Max(0, (int)Math.Round(CardSettings.Current.Param(def, "bonusCards", 1f)));
            if (count == 0) return;
            var maxRarity = def.Ability == "DoubleDown" ? CardRarity.Rare : CardRarity.Mythic;
            var cards = Deck.DealBonus(new Random(sessionId * 104729 + actor * 31 + cardId), Effects.CardsOf(actor), count, NetSend.IsSolo, maxRarity);
            if (cards.Length == 0) return;
            NetSend.Raise(new CardsGranted { sessionId = sessionId, actor = actor, cards = cards, reason = 1 }, Photon.Realtime.ReceiverGroup.All);
        }

        /// <summary>Everyone: bonus cards from the host. The local player sees them revealed.</summary>
        public static void OnCardsGranted(CardsGranted m)
        {
            var names = new List<string>();
            var got = new List<byte>();
            foreach (var id in m.cards)
            {
                if (!Effects.AddCard(m.actor, id, live: true)) continue;
                got.Add(id);
                names.Add(Deck.NameOf(id));
            }
            if (got.Count == 0) return;
            if (m.actor == NetSend.LocalActor)
            {
                Hud.Toast("Bonus: " + string.Join(", ", names));
                CardPicker.Ensure().ShowReveal(got.ToArray());
            }
            else Hud.Toast(NetSend.NameOf(m.actor) + " gained " + string.Join(", ", names));
        }

        public static void LocalPick(byte cardId)
        {
            if (!Active || CurrentActor != NetSend.LocalActor) return;
            Art.Sfx.Play("cardPick");
            NetSend.Raise(new PickMade { sessionId = SessionId, actor = NetSend.LocalActor, cardId = cardId, auto = false }, Photon.Realtime.ReceiverGroup.All);
        }

        public static void OnSessionEnd(SessionEnd m)
        {
            if (m.sessionId == SessionId) End();
        }

        public static void OnPlayerLeft(int actor)
        {
            if (!Active) return;
            bool wasCurrent = CurrentActor == actor;
            int idx = Order.IndexOf(actor);
            if (idx < 0) return;
            Order.RemoveAt(idx);
            if (idx < TurnIndex) TurnIndex--;
            if (wasCurrent) BeginTurn();
        }

        private static void End()
        {
            Active = false;
            Hud.HideWaiting();
            CardPicker.HideIfOpen();
        }
    }
}
