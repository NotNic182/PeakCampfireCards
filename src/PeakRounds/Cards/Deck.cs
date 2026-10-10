using System;
using System.Collections.Generic;
using System.Text;
using PeakRounds.Generated;
using PeakRounds.Rounds;

namespace PeakRounds.Cards
{
    /// <summary>
    /// systems.json Deck: the playable deck is every cards.json row that is inDeck AND whose ROUNDS card was read from the
    /// player's install. Deals hands with ROUNDS's rarity weights and allowMultiple rule.
    /// </summary>
    public static class Deck
    {
        public static readonly List<CardDef> Playable = new List<CardDef>();
        private static readonly Dictionary<byte, CardDef> ById = new Dictionary<byte, CardDef>();
        private static readonly Dictionary<byte, RoundsCard> RoundsById = new Dictionary<byte, RoundsCard>();
        public static bool Ready { get; private set; }
        public static string Status { get; private set; } = "waiting for ROUNDS";

        public static void Build(RoundsData data)
        {
            Playable.Clear(); ById.Clear(); RoundsById.Clear();
            Ready = false;
            if (data == null || data.Error != null || data.Cards.Count == 0)
            {
                Status = data == null ? "no data" : (data.Error ?? "no cards");
                return;
            }
            int missing = 0;
            foreach (var def in CardTable.Cards)
            {
                ById[def.Id] = def;
                RoundsCard rc;
                if (!data.Cards.TryGetValue(def.Rounds, out rc)) { missing++; continue; }
                RoundsById[def.Id] = rc;
                if (def.InDeck && data.PoolNames.Contains(def.Rounds)) Playable.Add(def);
            }
            Ready = Playable.Count >= CardTable.HandSize;
            Status = Ready ? $"{Playable.Count} playable cards" : $"only {Playable.Count} playable cards";
            Plugin.Log.LogInfo($"Deck: {Status}; {missing} sheet rows had no ROUNDS card in this install.");
        }

        public static bool TryGet(byte id, out CardDef def) => ById.TryGetValue(id, out def);

        public static RoundsCard RoundsFor(byte id)
        {
            RoundsCard rc;
            return RoundsById.TryGetValue(id, out rc) ? rc : null;
        }

        public static string NameOf(byte id)
        {
            var rc = RoundsFor(id);
            if (rc != null) return rc.Name;
            CardDef d;
            return TryGet(id, out d) ? d.Rounds : ("card " + id);
        }

        /// <summary>
        /// ROUNDS-style draw: pick a random candidate, keep it with probability = rarity weight, never twice in one hand,
        /// never a card the player already holds when ROUNDS marks it allowMultiple = false.
        /// </summary>
        public static byte[] Deal(Random rng, ICollection<byte> owned, int count)
        {
            var hand = new List<byte>(count);
            if (Playable.Count == 0) return hand.ToArray();
            int attempts = 0;
            while (hand.Count < count && attempts++ < 4000)
            {
                var def = Playable[rng.Next(Playable.Count)];
                if (hand.Contains(def.Id)) continue;
                var rc = RoundsFor(def.Id);
                if (rc != null && !rc.AllowMultiple && owned != null && owned.Contains(def.Id)) continue;
                int rarity = rc != null ? rc.Rarity : 0;
                if (rng.NextDouble() > CardTable.RarityWeight(rarity)) continue;
                hand.Add(def.Id);
            }
            // fill up if the rarity rolls were unlucky
            attempts = 0;
            while (hand.Count < count && hand.Count < Playable.Count && attempts++ < 4000)
            {
                var def = Playable[rng.Next(Playable.Count)];
                if (!hand.Contains(def.Id)) hand.Add(def.Id);
            }
            return hand.ToArray();
        }

        /// <summary>The "on the mountain" lines for a card, built from stats.json display strings.</summary>
        public static string[] MountainLines(CardDef def)
        {
            var lines = new List<string>();
            foreach (var e in def.Effects)
            {
                var sd = ClimbStatTable.Defs[(int)e.Stat];
                string line = Describe(sd, e.Value);
                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
            return lines.ToArray();
        }

        public static string Describe(ClimbStatDef sd, float v)
        {
            if (sd.IsFlag) return v > 0.5f ? sd.Display : "";
            if (Math.Abs(v) < 0.0005f) return "";
            bool good = sd.InvertSign ? v < 0 : v > 0;
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
