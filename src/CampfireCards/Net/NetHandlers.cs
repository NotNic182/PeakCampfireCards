using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Ui;
using Photon.Pun;
using UnityEngine;

namespace CampfireCards.Net
{
    /// <summary>systems.json Net: the receive side of design/net.json (Generated/NetEvents.cs dispatches here).</summary>
    public static class NetHandlers
    {
        /// <summary>Events only the host may send (deals, grants, approvals).</summary>
        private static bool FromHost(int sender)
        {
            if (PhotonNetwork.OfflineMode || !PhotonNetwork.InRoom) return true;
            var master = PhotonNetwork.MasterClient;
            return master != null && master.ActorNumber == sender;
        }

        public static void OnHello(Hello m, int sender)
        {
            Plugin.Log.LogInfo($"Player {sender} runs {Plugin.Name} {m.modVersion} ({m.cardCount} cards, deck {m.deckHash:X8})");
            if (m.modVersion != Plugin.Version || m.deckHash != Deck.Hash)
                Hud.Toast($"{NetSend.NameOf(sender)} has a different version or card settings of Campfire Cards ({m.modVersion})");
            if (NetSend.IsHost && Effects.HasAnyState) NetSend.RaiseTo(Effects.BuildStateSync(PickSession.SessionId), sender);
        }

        public static void OnPickSessionStart(PickSessionStart m, int sender)
        {
            if (!FromHost(sender)) return;
            PickSession.OnStart(m);
        }

        public static void OnPickMade(PickMade m, int sender)
        {
            // a player picks for themselves; the host may auto-pick for anyone on timeout
            if (m.actor != sender && !FromHost(sender)) return;
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

        /// <summary>Mountain's Bargain: the owner gave up a card (never a Mythic, never the Bargain itself).</summary>
        public static void OnCardSacrificed(CardSacrificed m, int sender)
        {
            if (m.actor != sender || !Effects.HasAbility(m.actor, "MountainsBargain")) return;
            var def = CardSettings.Current.Card(m.cardId);
            if (def == null || def.Rarity == CardRarity.Mythic || def.Ability == "MountainsBargain") return;
            Effects.RemoveCard(m.actor, m.cardId);
            Hud.Toast((m.actor == NetSend.LocalActor ? "You" : NetSend.NameOf(m.actor)) + " sacrificed " + def.Name + " to the mountain");
        }

        /// <summary>Phoenix Clause fired for that player: they skip their next campfire pick.</summary>
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

        /// <summary>Shared Burden owner: Injury a protected teammate did not take.</summary>
        public static void OnInjuryShare(InjuryShare m, int sender)
        {
            if (!Effects.HasAbility(NetSend.LocalActor, "SharedBurden")) return;
            Ability_SharedBurden.Absorb(Character.localCharacter, Mathf.Clamp(m.amount, 0f, 0.25f));
        }

        /// <summary>Hero's Arrival: the reviver's bonus stamina for the revived scout.</summary>
        public static void OnBonusStamina(BonusStamina m, int sender)
        {
            var me = Character.localCharacter;
            if (me == null || m.amount <= 0f || !Effects.HasAbility(sender, "HerosArrival")) return;
            me.AddExtraStamina(Mathf.Min(m.amount, 1f));
            Hud.Toast($"HERO'S ARRIVAL! {NetSend.NameOf(sender)} brought you back strong");
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
