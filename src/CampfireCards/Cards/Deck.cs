using System;
using System.Collections.Generic;
using CampfireCards.Generated;

namespace CampfireCards.Cards
{
    /// <summary>
    /// systems.json Deck: every cards.json row is in the deck. Deals hands with the rarity weights and the unique rule,
    /// and writes each card's effect lines from the stats.json display strings.
    /// </summary>
    public static class Deck
    {
        public static readonly List<CardDef> Playable = new List<CardDef>();
        private static readonly Dictionary<byte, CardDef> ById = new Dictionary<byte, CardDef>();
        public static bool Ready { get; private set; }
        /// <summary>Hash of the whole deck; players on different card lists see a warning.</summary>
        public static int Hash { get; private set; }

        public static void Build()
        {
            Playable.Clear(); ById.Clear();
            unchecked
            {
                int h = (int)2166136261;
                foreach (var def in CardTable.Cards)
                {
                    ById[def.Id] = def;
                    Playable.Add(def);
                    h = (h ^ def.Id) * 16777619;
                    foreach (var ch in def.Name) h = (h ^ ch) * 16777619;
                    foreach (var e in def.Effects) h = (h ^ ((int)e.Stat * 1000 + (int)(e.Value * 1000f))) * 16777619;
                }
                Hash = h;
            }
            Ready = Playable.Count >= CardTable.HandSize;
            Plugin.Log.LogInfo($"Deck: {Playable.Count} cards, hand of {CardTable.HandSize}, hash {Hash:X8}");
        }

        public static bool TryGet(byte id, out CardDef def) => ById.TryGetValue(id, out def);

        public static string NameOf(byte id)
        {
            CardDef d;
            return TryGet(id, out d) ? d.Name : ("card " + id);
        }

        public static ThemeDef ThemeOf(CardDef def)
        {
            var themes = CardTable.Themes;
            return def.Theme >= 0 && def.Theme < themes.Length ? themes[def.Theme] : themes[0];
        }

        /// <summary>
        /// Weighted draw: pick a random card, keep it with probability = its rarity weight; never twice in one hand,
        /// never a unique card the player already holds.
        /// </summary>
        public static byte[] Deal(Random rng, ICollection<byte> owned, int count)
        {
            var hand = new List<byte>(count);
            if (Playable.Count == 0) return hand.ToArray();
            bool Allowed(CardDef def) => !hand.Contains(def.Id) && !(def.Unique && owned != null && owned.Contains(def.Id));
            int attempts = 0;
            while (hand.Count < count && attempts++ < 4000)
            {
                var def = Playable[rng.Next(Playable.Count)];
                if (!Allowed(def)) continue;
                if (rng.NextDouble() > CardTable.RarityWeight(def.Rarity)) continue;
                hand.Add(def.Id);
            }
            attempts = 0;
            while (hand.Count < count && attempts++ < 4000)
            {
                var def = Playable[rng.Next(Playable.Count)];
                if (Allowed(def)) hand.Add(def.Id);
            }
            return hand.ToArray();
        }

        /// <summary>One line per effect, each marked good or bad, from the stats.json display strings.</summary>
        public static List<KeyValuePair<string, bool>> EffectLines(CardDef def)
        {
            var lines = new List<KeyValuePair<string, bool>>();
            foreach (var e in def.Effects)
            {
                var sd = ClimbStatTable.Defs[(int)e.Stat];
                bool good;
                string line = Describe(sd, e.Value, out good);
                if (!string.IsNullOrEmpty(line)) lines.Add(new KeyValuePair<string, bool>(line, good));
            }
            return lines;
        }

        public static string Describe(ClimbStatDef sd, float v, out bool good)
        {
            good = true;
            if (sd.IsFlag) return v > 0.5f ? sd.Display : "";
            if (Math.Abs(v) < 0.0005f) return "";
            good = sd.InvertSign ? v < 0 : v > 0;
            string fmt = good ? sd.Display : sd.DisplayNegative;
            if (string.IsNullOrEmpty(fmt)) fmt = sd.Display;
            string val;
            switch (sd.Unit)
            {
                case "count": val = Math.Abs(v).ToString("0"); break;
                case "perSecond": val = (Math.Abs(v) * 100f).ToString("0.0") + "%/s"; break;
                case "bool": val = ""; break;
                default: val = Math.Round(Math.Abs(v) * 100f).ToString("0") + "%"; break;
            }
            return fmt.Replace("{v}", val).Replace("{abs}", val);
        }
    }
}
