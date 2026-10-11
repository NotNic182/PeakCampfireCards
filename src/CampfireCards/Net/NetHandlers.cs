using System.Collections.Generic;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Ui;
using Photon.Pun;
using UnityEngine;

namespace CampfireCards.Net
{
    /// <summary>
    /// systems.json Net: the receive side of design/net.json (Generated/NetEvents.cs dispatches here). Every handler
    /// checks who may send it: host-only events come from the master client; player events only ever act for the sender.
    /// </summary>
    public static class NetHandlers
    {
        /// <summary>Hero's Arrival bonus stamina received: once per giving teammate per biome.</summary>
        private static readonly PerKeyBiomeCharge _bonusFrom = new PerKeyBiomeCharge();
        /// <summary>Settings mismatches already reported (sender, their hash), and players the host already answered.</summary>
        private static readonly HashSet<(int, int)> _warned = new HashSet<(int, int)>();
        private static readonly HashSet<(int, int)> _answered = new HashSet<(int, int)>();

        /// <summary>Run over: per-run receive-side bookkeeping is cleared.</summary>
        public static void Reset()
        {
            _bonusFrom.Reset();
            _warned.Clear();
            _answered.Clear();
        }

        /// <summary>Events only the host may send (deals, grants, approvals).</summary>
        private static bool FromHost(int sender)
        {
            if (PhotonNetwork.OfflineMode || !PhotonNetwork.InRoom) return true;
            var master = PhotonNetwork.MasterClient;
            return master != null && master.ActorNumber == sender;
        }

        /// <summary>This player's version and settings fingerprint (Core.SettingsFingerprint).</summary>
        public static Hello MakeHello()
        {
            var f = Deck.Fingerprint;
            return new Hello
            {
                modVersion = Plugin.Version, cardCount = Deck.Count, deckHash = Deck.Hash,
                rules = f != null ? f.Rules : new int[0], weights = f != null ? f.Weights : new int[0], cards = f != null ? f.Cards : new int[0]
            };
        }

        /// <summary>
        /// A player announced their version and settings. Any difference is reported once, naming what differs; the host
        /// answers a player with its own Hello so both sides are told (never silently assumed identical).
        /// </summary>
        public static void OnHello(Hello m, int sender)
        {
            if (sender == NetSend.LocalActor) return;
            Plugin.Log.LogInfo($"Player {sender} runs {Plugin.Name} {m.modVersion} ({m.cardCount} cards, settings {m.deckHash:X8})");
            var diffs = new List<string>();
            if (m.modVersion != Plugin.Version) diffs.Add("mod version " + m.modVersion + " (yours " + Plugin.Version + ")");
            else if (m.deckHash != Deck.Hash && Deck.Fingerprint != null) diffs.AddRange(SettingsFingerprint.Differences(Deck.Fingerprint, m.rules, m.weights, m.cards));
            if (m.modVersion == Plugin.Version && m.deckHash != Deck.Hash && diffs.Count == 0) diffs.Add("card settings");
            if (diffs.Count > 0 && _warned.Add((sender, m.deckHash)))
            {
                string who = NetSend.NameOf(sender);
                Plugin.Log.LogWarning($"Campfire Cards settings differ from {who} (actor {sender}): {string.Join("; ", diffs)}. " +
                                      "The host deals the cards, but each player's card values apply on their own scout: use the same config.");
                Hud.Toast(string.Format(Ui_Toast.SettingsTemplate, who, string.Join("; ", diffs)), warning: true);
            }
            if (NetSend.IsHost && !FromHost(sender))
            {
                if (_answered.Add((sender, m.deckHash))) NetSend.RaiseTo(MakeHello(), sender);
                if (Effects.HasAnyState) NetSend.RaiseTo(Effects.BuildStateSync(PickSession.SessionId), sender);
            }
        }

        public static void OnPickSessionStart(PickSessionStart m, int sender)
        {
            if (!FromHost(sender)) return;
            PickSession.OnStart(m);
        }

        /// <summary>Host: a player asks for a card. They can only ever pick for themselves; PickSession validates the rest.</summary>
        public static void OnPickRequest(PickRequest m, int sender)
        {
            if (!NetSend.IsHost) return;
            PickSession.HostOnRequest(m, sender);
        }

        /// <summary>An accepted pick. Only the host announces picks (its own validated decision or a timeout pick).</summary>
        public static void OnPickMade(PickMade m, int sender)
        {
            if (!FromHost(sender))
            {
                Plugin.Log.LogWarning($"Ignored PickMade from non-host actor {sender}");
                return;
            }
            PickSession.OnPick(m);
        }

        public static void OnStateSync(StateSync m, int sender)
        {
            if (!FromHost(sender)) return;
            Effects.ApplyStateSync(m);
            Plugin.Verbose($"State sync from {sender}: {m.actors.Length} players");
        }

        public static void OnArrival(Arrival m, int sender)
        {
            PickSession.RecordArrival(sender, m.segment, m.time);
        }

        public static void OnStateRequest(StateRequest m, int sender)
        {
            if (!NetSend.IsHost) return;
            NetSend.RaiseTo(Effects.BuildStateSync(PickSession.SessionId), sender);
        }

        public static void OnCardsGranted(CardsGranted m, int sender)
        {
            if (!FromHost(sender)) return;
            PickSession.OnCardsGranted(m);
        }

        public static void OnSessionEnd(SessionEnd m, int sender)
        {
            if (!FromHost(sender)) return;
            PickSession.OnSessionEnd(m);
        }

        public static void OnPickSkipped(PickSkipped m, int sender)
        {
            if (!FromHost(sender)) return;
            PickSession.OnSkipped(m);
        }

        public static void OnCardPenalty(CardPenalty m, int sender)
        {
            if (m.actor != sender || !Effects.HasAbility(sender, "DoubleDeal")) return;
            Effects.Penalties.TrySet(sender, m.variant);
        }

        public static void OnCardRewardUsed(CardRewardUsed m, int sender)
        {
            if (FromHost(sender)) Effects.SetFlag(m.actor, Effects.FlagBalloonUsed, true);
        }

        /// <summary>
        /// Mountain's Bargain: the owner gave up a card (never a Mythic, never the Bargain itself). A player can only
        /// remove their own cards, so a repeated message can only cost the sender.
        /// </summary>
        public static void OnCardSacrificed(CardSacrificed m, int sender)
        {
            if (m.actor != sender || !Effects.HasAbility(m.actor, "MountainsBargain")) return;
            var def = CardSettings.Current.Card(m.cardId);
            if (def == null || def.Rarity == CardRarity.Mythic || def.Ability == "MountainsBargain") return;
            if (!Effects.CardsOf(m.actor).Contains(m.cardId)) return;
            Effects.RemoveCard(m.actor, m.cardId);
            Hud.Toast(string.Format(Ui_Toast.GaveUpTemplate, m.actor == NetSend.LocalActor ? Ui_Toast.You : NetSend.NameOf(m.actor), def.Name, Deck.NameOfAbility("MountainsBargain")));
        }

        /// <summary>Phoenix Clause fired for that player: they skip their next campfire pick (only ever their own).</summary>
        public static void OnSkipNextPick(SkipNextPick m, int sender)
        {
            if (m.actor != sender) return;
            Effects.SetFlag(m.actor, Effects.FlagSkipNext, true);
        }

        public static void OnReviveRequest(ReviveRequest m, int sender)
        {
            if (!NetSend.IsHost) return;
            Ability_EternalCampfire.HostHandle(m, sender);
        }

        public static void OnReviveGranted(ReviveGranted m, int sender)
        {
            if (!FromHost(sender)) return;
            Ability_EternalCampfire.OnGranted(Character.localCharacter, m);
        }

        /// <summary>
        /// Shared Burden owner: Injury a protected teammate did not take. Only from a teammate inside this owner's aura,
        /// and never more than the card's per-hit cap.
        /// </summary>
        public static void OnInjuryShare(InjuryShare m, int sender)
        {
            if (sender == NetSend.LocalActor || !Effects.HasAbility(NetSend.LocalActor, "SharedBurden")) return;
            var me = Character.localCharacter;
            Character victim;
            if (me == null || !PlayerHandler.TryGetCharacter(sender, out victim) || victim == null) return;
            var def = System.Array.Find(CardTable.Cards, c => c.Ability == "SharedBurden");
            float radius = def != null ? CardSettings.Current.Param(def, "radius", 6f) : 6f;
            float cap = def != null ? CardSettings.Current.Param(def, "cap", 0.1f) : 0.1f;
            if (Vector3.Distance(victim.Center, me.Center) > radius + 3f)
            {
                Plugin.Verbose($"Ignored Injury share from {sender}: outside the aura");
                return;
            }
            Ability_SharedBurden.Absorb(me, Mathf.Clamp(m.amount, 0f, cap));
        }

        /// <summary>
        /// Hero's Arrival: the reviver's bonus stamina for the revived scout. Only from an owner of the card, at most the
        /// card's bonus, and once per giving teammate per biome (the card's own limit).
        /// </summary>
        public static void OnBonusStamina(BonusStamina m, int sender)
        {
            var me = Character.localCharacter;
            if (me == null || sender == NetSend.LocalActor || m.amount <= 0f || !Effects.HasAbility(sender, "HerosArrival")) return;
            if (!_bonusFrom.TryUse(sender, LocalCards.Biome)) { Plugin.Verbose($"Ignored repeated bonus stamina from {sender}"); return; }
            var def = System.Array.Find(CardTable.Cards, c => c.Ability == "HerosArrival");
            float max = def != null ? CardSettings.Current.Param(def, "bonus", 1f) : 1f;
            float got = Mathf.Min(m.amount, max);
            me.AddExtraStamina(got);
            Hud.Toast($"{Deck.NameOfAbility("HerosArrival").ToUpperInvariant()}! {NetSend.NameOf(sender)} revived you: +{Ability.Points(got)} extra stamina");
        }

        public static void OnSpawnItem(SpawnItem m, int sender)
        {
            Hooks.HostSpawnItem(m, sender);
        }

        /// <summary>The sender woke up because the local scout fed them (Hero's Arrival).</summary>
        public static void OnRevivedBy(RevivedBy m, int sender)
        {
            if (m.victimActor != sender) return;
            Ability_HerosArrival.OnRevivedTeammate(m.victimActor);
        }
    }
}
