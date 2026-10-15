using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CampfireCards.Generated;

namespace CampfireCards.Core
{
    /// <summary>
    /// Card strength by difficulty. The cards are balanced for Ascent 7-8; on easier difficulties PEAK itself is
    /// gentler (climbing costs x1.0 on Peak and x0.7 on Tenderfoot instead of x1.4), so each card's benefits shrink
    /// toward "no change" by the tier's strength (cards.json difficultyStrength); a strength above 1 amplifies them
    /// past the written values instead. Drawbacks and Mythics never scale.
    /// Unity-free so it can be unit tested.
    /// </summary>
    public static class DifficultyScale
    {
        public static readonly string[] TierNames = { "Tenderfoot–Peak", "Ascent 1–3", "Ascent 4–6", "Ascent 7–8" };

        /// <summary>Ascents.currentAscent: -1 Tenderfoot, 0 Peak, 1..8 Ascents.</summary>
        public static int TierOf(int ascent) => ascent <= 0 ? 0 : ascent <= 3 ? 1 : ascent <= 6 ? 2 : 3;

        /// <summary>Does this modifier work against the player? ('set' mods are Mythic benefits and never count.)</summary>
        public static bool Hurts(float value, ModOp op, bool isMul, bool higherIsBetter)
        {
            if (op == ModOp.set) return false;
            if (isMul) return value > 0f && (higherIsBetter ? value < 1f : value > 1f);
            return higherIsBetter ? value < 0f : value > 0f;
        }

        /// <summary>1 within a rounding hair: the strength that changes nothing.</summary>
        public static bool Unscaled(float strength) => Math.Abs(strength - 1f) < 0.0001f;

        /// <summary>
        /// A card modifier: a benefit moves toward no change (x1, or +0) below strength 1 and past its written value
        /// above it (never past "free": a cost multiplier stops at 0); a drawback stays as it is.
        /// </summary>
        public static float Mod(float value, ModOp op, bool isMul, bool higherIsBetter, float strength)
        {
            if (Unscaled(strength) || op == ModOp.set) return value;
            if (isMul)
            {
                if (value <= 0f) return value;   // "none at all" (no revival Curse) is all or nothing, not a percentage
                bool helps = higherIsBetter ? value > 1f : value < 1f;
                return helps ? Math.Max(0f, 1f + (value - 1f) * strength) : value;
            }
            bool helpsFlat = higherIsBetter ? value > 0f : value < 0f;
            return helpsFlat ? value * strength : value;
        }

        public enum ParamKind
        {
            /// <summary>Not a benefit amount (ranges, thresholds, chances, costs, penalties, items): unchanged.</summary>
            Fixed,
            /// <summary>How much the card gives (stamina, healing, power length): x strength (above 1: more).</summary>
            Amount,
            /// <summary>How long you wait for it (cooldowns, intervals, build-up): / strength, so longer below 1 and shorter above.</summary>
            Wait,
            /// <summary>The share of a cost that still lands (Piton Payment Plan's keep): the saving scales with the strength, never past free.</summary>
            Reduction
        }

        private static readonly Dictionary<string, ParamKind> ByName = new Dictionary<string, ParamKind>
        {
            { "restore", ParamKind.Amount }, { "bonus", ParamKind.Amount }, { "bonusNow", ParamKind.Amount },
            { "bonusFire", ParamKind.Amount }, { "regenPerSecond", ParamKind.Amount }, { "heal", ParamKind.Amount },
            { "remove", ParamKind.Amount }, { "duration", ParamKind.Amount }, { "boost", ParamKind.Amount },
            { "cooldown", ParamKind.Wait }, { "interval", ParamKind.Wait }, { "buildup", ParamKind.Wait },
            // Meditation's "hold" is a requirement, not a pacing factor (the bonus is once per biome regardless), and
            // HOLD's "refund" is the benefit share itself: the default Fixed and Amount do the right thing.
            { "refund", ParamKind.Amount },
            { "keep", ParamKind.Reduction },
        };

        /// <summary>Card-specific exceptions: Big Lollipop Crash's cooldown spaces out its drawback, not a benefit, and
        /// HOLD's full hang refund is all-or-nothing ("costs no stamina" stays true on every difficulty).</summary>
        private static readonly Dictionary<string, ParamKind> ByCard = new Dictionary<string, ParamKind>
        {
            { "MomentumFiend/cooldown", ParamKind.Fixed },
            { "QuickRecovery/refund", ParamKind.Fixed },
        };

        public static ParamKind KindOf(string cardKey, string param)
        {
            ParamKind k;
            if (ByCard.TryGetValue(cardKey + "/" + param, out k)) return k;
            return ByName.TryGetValue(param, out k) ? k : ParamKind.Fixed;
        }

        /// <summary>
        /// The strength for each of a card's numbers. When several numbers multiply into one benefit (Endurance Badge:
        /// discount x boost length x how often; Nic of Time: amount x cooldown), scaling each by the full strength would
        /// compound (60% x 60% x 60% = 22%), so the strength is split between them: strength^(1/n), n = the payload (benefit
        /// modifiers and amounts) + each length (duration, boost) + each wait (cooldown, interval, build-up).
        /// </summary>
        public static float CardStrength(CardDef card, float strength)
        {
            if (Unscaled(strength) || strength <= 0f || card == null) return strength;
            bool payload = false;
            int factors = 0;
            for (int i = 0; i < card.Mods.Length && !payload; i++)
            {
                var m = card.Mods[i];
                var ch = ChannelTable.Defs[(int)m.Channel];
                if (Math.Abs(Mod(m.Value, m.Op, ch.IsMul, ch.HigherIsBetter, 0.5f) - m.Value) > 1e-6f) payload = true;
            }
            foreach (var kv in card.Params)
            {
                switch (KindOf(card.Key, kv.Key))
                {
                    case ParamKind.Amount:
                        if (kv.Key == "duration" || kv.Key == "boost") factors++; else payload = true;
                        break;
                    case ParamKind.Reduction: payload = true; break;
                    case ParamKind.Wait: factors++; break;
                }
            }
            int n = factors + (payload ? 1 : 0);
            return n <= 1 ? strength : (float)Math.Pow(strength, 1.0 / n);
        }

        public static float Param(string cardKey, string param, float value, float strength)
        {
            if (Unscaled(strength) || strength <= 0f) return value;
            switch (KindOf(cardKey, param))
            {
                case ParamKind.Amount: return value * strength;
                case ParamKind.Wait: return value / strength;
                case ParamKind.Reduction: return Math.Max(0f, 1f - (1f - value) * strength);
                default: return value;
            }
        }

        // ------------------------------------------------------------------ card text
        /// <summary>
        /// The card's benefit text with every scaled number replaced by the number this difficulty really uses
        /// ("Grab walls from 40% farther away" -> "24%" at 60%). complete = every scaled number was found in the text;
        /// when not, the caller adds a short "benefits at N%" line so the card never overstates itself.
        /// </summary>
        public static string Benefit(CardDef card, CardSettings s, string text, out bool complete)
        {
            complete = true;
            if (card == null || string.IsNullOrEmpty(text) || card.Rarity == CardRarity.Mythic || Unscaled(s.BenefitStrength)) return text;
            // each scaled number, with the ways a card may write it (300 s is "5 min")
            var pairs = new List<KeyValuePair<float, float>[]>();
            for (int i = 0; i < card.Mods.Length; i++)
            {
                var m = card.Mods[i];
                float raw = s.RawModValue(card, i), now = s.ModValue(card, i);
                if (Math.Abs(raw - now) < 1e-6f) continue;
                var def = ChannelTable.Defs[(int)m.Channel];
                // a drawback dropped by the toggle is not a benefit number to rewrite (its text is hidden anyway)
                if (!s.DrawbacksEnabled && Hurts(raw, m.Op, def.IsMul, def.HigherIsBetter)) continue;
                if (def.IsMul) pairs.Add(One(Math.Abs(raw - 1f) * 100f, Math.Abs(now - 1f) * 100f));
                else if (m.Channel == Channel.regenDelay) pairs.Add(One(Math.Abs(raw), Math.Abs(now)));
                else pairs.Add(One(Math.Abs(raw) * 100f, Math.Abs(now) * 100f));
            }
            foreach (var kv in card.Params)
            {
                float raw = s.RawParam(card, kv.Key, kv.Value), now = s.Param(card, kv.Key, kv.Value);
                if (Math.Abs(raw - now) < 1e-6f) continue;
                switch (KindOf(card.Key, kv.Key))
                {
                    case ParamKind.Wait:
                        pairs.Add(raw >= 60f && Math.Abs(raw % 60f) < 1e-3f
                            ? new[] { Pair(raw, now), Pair(raw / 60f, now / 60f) }    // "300s" or "5 min"
                            : One(raw, now));
                        break;
                    case ParamKind.Reduction:
                        // Piton Payment Plan says "60% after 20s: 15% less in total"; both follow from defer and keep
                        float defer = s.RawParam(card, "defer", 0.75f);
                        pairs.Add(One(defer * raw * 100f, defer * now * 100f));
                        pairs.Add(One((defer - defer * raw) * 100f, (defer - defer * now) * 100f));
                        break;
                    default:
                        bool seconds = kv.Key == "duration" || kv.Key == "boost";
                        pairs.Add(seconds ? One(raw, now) : One(raw * 100f, now * 100f));
                        break;
                }
            }
            if (pairs.Count == 0) return text;

            // find every number in the original text first, then replace, so a new number never gets replaced again;
            // the same number scaled the same way (Rule Zero's 15 and 15) is replaced everywhere it appears
            var swaps = new Dictionary<string, string>();
            foreach (var forms in pairs)
            {
                bool found = false;
                foreach (var p in forms)
                {
                    string from = Number(p.Key);
                    if (!Rx(from).IsMatch(text)) continue;
                    found = true;
                    if (!swaps.ContainsKey(from)) swaps[from] = Number(p.Value);
                }
                if (!found) complete = false;
            }
            if (swaps.Count == 0) return text;
            var all = new Regex(@"(?<![\d.])(" + string.Join("|", OrderByLength(swaps.Keys)) + @")(?![\d])");
            return all.Replace(text, m => swaps[m.Value]);
        }

        private static Regex Rx(string number) => new Regex(@"(?<![\d.])" + Regex.Escape(number) + @"(?![\d])");

        private static IEnumerable<string> OrderByLength(IEnumerable<string> numbers)
        {
            var list = new List<string>(numbers);
            list.Sort((a, b) => b.Length.CompareTo(a.Length));   // "7.5" before "7"
            for (int i = 0; i < list.Count; i++) list[i] = Regex.Escape(list[i]);
            return list;
        }

        private static KeyValuePair<float, float> Pair(float from, float to) => new KeyValuePair<float, float>(from, to);
        private static KeyValuePair<float, float>[] One(float from, float to) => new[] { Pair(from, to) };

        /// <summary>Numbers as the cards write them: whole numbers plain, otherwise at most one or two decimals.</summary>
        public static string Number(float v)
        {
            double r = Math.Round(v, v < 1f ? 2 : 1);
            return r.ToString(r == Math.Floor(r) ? "0" : (v < 1f ? "0.##" : "0.#"), CultureInfo.InvariantCulture);
        }
    }
}
