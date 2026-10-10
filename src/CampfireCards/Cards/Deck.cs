using System;
using System.Collections.Generic;
using System.Linq;
using CampfireCards.Core;
using CampfireCards.Generated;
using UnityEngine;

namespace CampfireCards.Cards
{
    /// <summary>
    /// systems.json Deck: the 64-card registry (cards.json) plus the live settings. Offers are dealt by
    /// Core.CardDraw with the configured rarity weights and rules; this class only adds game-side context.
    /// </summary>
    public static class Deck
    {
        public static bool Ready { get; private set; }
        /// <summary>Hash of every shared gameplay setting (Core.SettingsFingerprint); players with different settings see a warning.</summary>
        public static int Hash => Fingerprint != null ? Fingerprint.Hash : 0;
        /// <summary>The settings fingerprint exchanged in Hello, so a mismatch can name what differs.</summary>
        public static SettingsFingerprint Fingerprint { get; private set; }
        public static int Count => CardTable.Cards.Length;

        public static void Build()
        {
            Fingerprint = SettingsFingerprint.Of(CardSettings.Current);
            Ready = Count > 0;
            Plugin.Log.LogInfo($"Deck: {Count} cards, offer {CardSettings.Current.OfferCount}, settings hash {Hash:X8}");
        }

        public static bool TryGet(byte id, out CardDef def)
        {
            def = CardSettings.Current.Card(id);
            return def != null;
        }

        public static string NameOf(byte id)
        {
            CardDef d;
            return TryGet(id, out d) ? d.Name : ("card " + id);
        }

        /// <summary>The name of the card with this ability (cards can be renamed in the sheet; messages follow).</summary>
        public static string NameOfAbility(string ability)
        {
            var def = Array.Find(CardTable.Cards, c => c.Ability == ability);
            return def != null ? def.Name : ability;
        }

        public static ThemeDef ThemeOf(CardDef def)
        {
            var themes = CardTable.Themes;
            return def.Theme >= 0 && def.Theme < themes.Length ? themes[def.Theme] : themes[0];
        }

        public static Color RarityColor(CardRarity r) => CardTable.RarityColors[(int)r].ToColor();

        public static string RarityName(CardRarity r) => r.ToString().ToUpperInvariant();

        /// <summary>
        /// Deal a campfire offer for one player (count may be reduced by Double Down). claimedMythics = Mythics owned by
        /// anyone or already offered this session; the Mythics dealt here are added to it.
        /// </summary>
        public static byte[] Deal(System.Random rng, ICollection<byte> owned, int count, bool solo, HashSet<byte> claimedMythics = null)
        {
            bool sacrifice = CardDraw.SacrificeTargets(CardSettings.Current, owned).Count > 0;
            var rules = OfferRules.Campfire(count, solo, sacrifice);
            rules.ClaimedMythics = claimedMythics ?? Effects.ClaimedMythics();
            var hand = CardDraw.Offer(rng, CardSettings.Current, owned, rules).ToArray();
            NoteMythics(hand, claimedMythics);
            return hand;
        }

        /// <summary>
        /// Bonus cards (Double Deal, Double Down): never card-draw cards, optionally capped at Rare, and never a Mythic that
        /// is still on offer to another player this session (stillOffered), so that player's pick cannot be refused later.
        /// </summary>
        public static byte[] DealBonus(System.Random rng, ICollection<byte> owned, int count, bool solo, CardRarity maxRarity, ICollection<byte> stillOffered = null)
        {
            bool sacrifice = CardDraw.SacrificeTargets(CardSettings.Current, owned).Count > 0;
            var rules = OfferRules.Campfire(count, solo, sacrifice);
            rules.ExcludeDrawCards = true;
            rules.MaxRarity = maxRarity;
            var claimed = Effects.ClaimedMythics();
            if (stillOffered != null) claimed.UnionWith(stillOffered.Where(id => { var d = CardSettings.Current.Card(id); return d != null && d.Rarity == CardRarity.Mythic; }));
            rules.ClaimedMythics = claimed;
            return CardDraw.Offer(rng, CardSettings.Current, owned, rules).ToArray();
        }

        private static void NoteMythics(byte[] hand, HashSet<byte> claimed)
        {
            if (claimed == null) return;
            foreach (var id in hand)
            {
                var d = CardSettings.Current.Card(id);
                if (d != null && d.Rarity == CardRarity.Mythic) claimed.Add(id);
            }
        }
    }

    public static class RgbaExtensions
    {
        public static Color ToColor(this Rgba c) => new Color(c.R, c.G, c.B, c.A);
    }
}
