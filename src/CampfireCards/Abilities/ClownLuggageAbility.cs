using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Net;
using CampfireCards.Ui;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>
    /// Double Deal: the host deals two bonus cards; every live acquisition rolls one permanent modifier and syncs it.
    /// </summary>
    public sealed class Ability_DoubleDeal : Ability
    {
        public override void OnAcquired(Character me)
        {
            int actor = NetSend.LocalActor;
            int slot = Effects.Penalties.Count(actor);
            byte variant = (byte)Random.Range(1, 5);
            if (slot > 255 || !Effects.Penalties.TrySet(actor, slot, variant)) return;
            NetSend.Raise(new CardPenalty { actor = actor, slot = (byte)slot, variant = variant }, Photon.Realtime.ReceiverGroup.All);
            Hud.Toast(Status(me).Text);
        }

        public override AbilityStatus Status(Character me)
        {
            var all = Effects.Penalties.Of(NetSend.LocalActor);
            if (all.Count == 0) return AbilityStatus.Wait(Line(null, "syncing drawback"));
            var parts = new System.Collections.Generic.List<string>();
            foreach (var v in all) parts.Add(BuildStats.Say(Effects.PenaltyValue(Card, v), true, null, StatWords.Of(CardPenaltyRules.ChannelFor(v))));
            return AbilityStatus.Wait(Line(null, string.Join("; ", parts)));
        }
    }
}
