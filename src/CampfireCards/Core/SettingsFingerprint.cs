using System;
using System.Collections.Generic;
using System.Linq;
using CampfireCards.Generated;

namespace CampfireCards.Core
{
    /// <summary>
    /// Every card setting that changes shared gameplay, reduced to numbers that hash the same on every machine: the
    /// global rules (offer count, duplicates, Mythics, stamina-cost cap, max-stamina floor), the rarity weights, and per
    /// card its id, key, enabled state, modifier values and parameters (cooldowns, radii, amounts). Floats are rounded to
    /// 1/10000 before hashing, so config text that parses to the same value always matches. Local-only settings (sounds,
    /// keys, logging, dev keys) are not part of it; the pick timer and pick mode are sent by the host with each session.
    /// Players exchange this in Hello and are told exactly which parts differ. Unity-free.
    /// </summary>
    public sealed class SettingsFingerprint
    {
        public static readonly string[] RuleNames = { "offer count", "duplicate cards", "Mythics enabled", "stamina-cost reduction cap", "max-stamina floor" };

        public int[] Rules;
        public int[] Weights;
        /// <summary>One hash per card, in CardTable order.</summary>
        public int[] Cards;
        public int Hash;

        public static int Q(float v) => (int)Math.Round((double)v * 10000.0);

        public static SettingsFingerprint Of(CardSettings s)
        {
            var f = new SettingsFingerprint
            {
                Rules = new[] { s.OfferCount, s.AllowDuplicates ? 1 : 0, s.MythicsEnabled ? 1 : 0, Q(s.StaminaCostCap), Q(s.MaxStaminaFloor) },
                Weights = s.RarityWeights.Select(Q).ToArray(),
                Cards = new int[CardTable.Cards.Length]
            };
            for (int i = 0; i < CardTable.Cards.Length; i++) f.Cards[i] = CardHash(s, CardTable.Cards[i]);
            unchecked
            {
                int h = (int)2166136261;
                h = Mix(h, 2);   // fingerprint format
                foreach (var v in f.Rules) h = Mix(h, v);
                foreach (var v in f.Weights) h = Mix(h, v);
                foreach (var v in f.Cards) h = Mix(h, v);
                f.Hash = h;
            }
            return f;
        }

        private static int CardHash(CardSettings s, CardDef def)
        {
            unchecked
            {
                int h = (int)2166136261;
                h = Mix(h, def.Id);
                foreach (var ch in def.Key) h = Mix(h, ch);
                h = Mix(h, s.IsEnabled(def) ? 1 : 0);
                h = Mix(h, (int)def.Rarity);
                for (int m = 0; m < def.Mods.Length; m++) h = Mix(h, Q(s.ModValue(def, m)));
                int idx = s.IndexOf(def.Id);
                var names = idx >= 0 ? s.Params[idx].Keys.OrderBy(k => k, StringComparer.Ordinal) : Enumerable.Empty<string>();
                foreach (var name in names)
                {
                    foreach (var ch in name) h = Mix(h, ch);
                    h = Mix(h, Q(s.Param(def, name)));
                }
                return h;
            }
        }

        private static int Mix(int h, int v) { unchecked { return (h ^ v) * 16777619; } }

        /// <summary>Human-readable list of what differs between two fingerprints (empty = compatible).</summary>
        public static List<string> Differences(SettingsFingerprint mine, int[] rules, int[] weights, int[] cards)
        {
            var diff = new List<string>();
            if (rules == null || weights == null || cards == null) { diff.Add("card settings (no details received)"); return diff; }
            for (int i = 0; i < RuleNames.Length; i++)
                if (i >= rules.Length || i >= mine.Rules.Length || rules[i] != mine.Rules[i]) diff.Add(RuleNames[i]);
            if (!weights.SequenceEqual(mine.Weights)) diff.Add("rarity weights");
            if (cards.Length != mine.Cards.Length) { diff.Add("card list"); return diff; }
            var names = new List<string>();
            for (int i = 0; i < cards.Length; i++)
                if (cards[i] != mine.Cards[i]) names.Add(CardTable.Cards[i].Name);
            if (names.Count > 0)
                diff.Add(names.Count <= 3 ? "cards: " + string.Join(", ", names) : $"{names.Count} cards ({string.Join(", ", names.Take(3))}, ...)");
            return diff;
        }
    }
}
