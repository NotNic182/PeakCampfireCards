using System;
using System.Collections.Generic;
using System.Linq;
using CampfireCards.Generated;

namespace CampfireCards.Core
{
    /// <summary>What an offer is for: how many cards, and which cards are allowed.</summary>
    public struct OfferRules
    {
        public int Count;
        public bool Solo;
        /// <summary>Bonus draws (Double Deal, Double Down) never include card-draw cards, so draws cannot chain.</summary>
        public bool ExcludeDrawCards;
        /// <summary>Highest rarity allowed (Double Down's bonus is Common to Rare). Mythic = no limit.</summary>
        public CardRarity MaxRarity;
        /// <summary>Mountain's Bargain is only offered when the player owns a non-Mythic card to sacrifice.</summary>
        public bool HasSacrificeTarget;
        /// <summary>Mythics already owned (or already offered this session) by anyone: a Mythic exists once per run.</summary>
        public ICollection<byte> ClaimedMythics;

        public static OfferRules Campfire(int count, bool solo, bool hasSacrificeTarget) =>
            new OfferRules { Count = count, Solo = solo, MaxRarity = CardRarity.Mythic, HasSacrificeTarget = hasSacrificeTarget };
    }

    /// <summary>
    /// Card offers: each offered card first rolls a rarity by the configured weights (only rarities that still have an
    /// eligible card take part), then picks uniformly inside that rarity. Unity-free so it can be unit tested.
    /// </summary>
    public static class CardDraw
    {
        public static bool Eligible(CardDef c, CardSettings s, ICollection<byte> owned, OfferRules rules)
        {
            if (!s.IsEnabled(c)) return false;
            if (c.Rarity > rules.MaxRarity) return false;
            if (rules.Solo && c.MultiplayerOnly) return false;
            if (rules.ExcludeDrawCards && c.DrawsCards) return false;
            if (owned != null && owned.Contains(c.Id) && (!s.AllowDuplicates || c.Rarity == CardRarity.Mythic)) return false;
            if (c.Ability == "MountainsBargain" && !rules.HasSacrificeTarget) return false;
            if (c.Rarity == CardRarity.Mythic && rules.ClaimedMythics != null && rules.ClaimedMythics.Contains(c.Id)) return false;
            return true;
        }

        public static List<byte> Offer(Random rng, CardSettings s, ICollection<byte> owned, OfferRules rules)
        {
            var pool = CardTable.Cards.Where(c => Eligible(c, s, owned, rules)).ToList();
            var hand = new List<byte>();
            while (hand.Count < rules.Count)
            {
                var left = pool.Where(c => !hand.Contains(c.Id)).ToList();
                if (left.Count == 0) break;
                var rarities = left.Select(c => c.Rarity).Distinct().Where(r => s.Weight(r) > 0f).ToList();
                if (rarities.Count == 0) break;
                CardRarity picked = RollRarity(rng, s, rarities);
                var bucket = left.Where(c => c.Rarity == picked).ToList();
                hand.Add(bucket[rng.Next(bucket.Count)].Id);
            }
            return hand;
        }

        public static CardRarity RollRarity(Random rng, CardSettings s, IList<CardRarity> available)
        {
            float total = 0f;
            foreach (var r in available) total += s.Weight(r);
            double roll = rng.NextDouble() * total;
            foreach (var r in available)
            {
                roll -= s.Weight(r);
                if (roll < 0) return r;
            }
            return available[available.Count - 1];
        }

        /// <summary>Cards a player may sacrifice to Mountain's Bargain: anything they own except Mythics and the Bargain itself.</summary>
        public static List<byte> SacrificeTargets(CardSettings s, IEnumerable<byte> owned)
        {
            var list = new List<byte>();
            foreach (var id in owned)
            {
                var c = s.Card(id);
                if (c != null && c.Rarity != CardRarity.Mythic && c.Ability != "MountainsBargain") list.Add(id);
            }
            return list;
        }
    }
}
