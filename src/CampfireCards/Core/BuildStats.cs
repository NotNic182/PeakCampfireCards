using System;
using System.Collections.Generic;
using CampfireCards.Generated;

namespace CampfireCards.Core
{
    /// <summary>
    /// How folded channels combine into what the game actually applies. The hooks use these, and so does the Current
    /// Build screen, so the screen always shows the same numbers the game uses. Unity-free.
    /// </summary>
    public static class StatComposites
    {
        /// <summary>Injury multiplier: fall or non-fall channel times allInjury (Hooks.OnAddStatusPrefix).</summary>
        public static float Injury(ModResult r, bool fall) => (fall ? r[Channel.fallInjury] : r[Channel.nonFallInjury]) * r[Channel.allInjury];

        /// <summary>A status build-up channel times the all-negative-status channel.</summary>
        public static float StatusGain(ModResult r, Channel rate) => r[rate] * r[Channel.negativeStatusRate];

        /// <summary>Weight of the item in hand (Hooks.OnUpdateWeight): carried-item weight times held-item weight.</summary>
        public static float HeldItemWeight(ModResult r) => r[Channel.itemWeight] * r[Channel.heldItemWeight];

        public static float Thorns(ModResult r) => StatusGain(r, Channel.thornsRate);

        /// <summary>
        /// Should the game's AddStatus be skipped after card scaling? Only when nothing is left (a Mythic set the status
        /// to zero, or Delayed Consequences / Phoenix Clause took all of it). PEAK adds Hunger (0.0005/s) and night Cold
        /// (0.005/s) in tiny per-frame steps that it accumulates itself, so a small positive amount must always go through.
        /// </summary>
        public static bool CancelsStatus(float scaledAmount) => scaledAmount <= 0f;

        /// <summary>
        /// Width of the stamina bar frame in full bars: the card-adjusted maximum (1 + maxStamina channel), or the status
        /// total when statuses overflow it (vanilla uses max(1, statusSum)).
        /// </summary>
        public static float BarFrame(float maxStaminaBonus, float statusSum) => Math.Max(Math.Max(0.05f, 1f + maxStaminaBonus), statusSum);

        /// <summary>
        /// How far the HUD's right-aligned status segments move (HUD units): by the card bonus times the full bar width,
        /// never so far left that the row would have no width, so they always end where the card-adjusted bar ends.
        /// </summary>
        public static float BarSegmentsShift(float maxStaminaBonus, float fullWidth) => Math.Max(-0.95f, maxStaminaBonus) * fullWidth;
    }

    /// <summary>stats.json in-game wording for one line: "more" when the value goes up, "less" when it goes down, "never" at 0.</summary>
    public struct StatWords
    {
        public string More, Less, Never;
        public bool HigherIsBetter;

        public static StatWords Of(Channel c)
        {
            var d = ChannelTable.Defs[(int)c];
            return new StatWords { More = d.More, Less = d.Less, Never = d.Never, HigherIsBetter = d.HigherIsBetter };
        }
    }

    /// <summary>One line of the combined stats: the value now (conditions and auras as they are) and the always-on value.</summary>
    public struct StatLine
    {
        public string Label;
        public float Now;
        public float Always;
        public bool IsMul;
        /// <summary>Value format for add lines: "stamina" (x100) or "seconds".</summary>
        public string Unit;
        public StatWords Words;
        public bool Conditional => Math.Abs(Now - Always) > 0.0005f;
    }

    /// <summary>
    /// Current Build: the combined effect of a scout's cards. "Now" is the live fold (Effects.Local: conditions that hold
    /// right now, teammates' auras in range, caps applied); "Always" is the fold of only the modifiers that never depend
    /// on a condition, so temporary effects are never presented as permanent. Unity-free.
    /// </summary>
    public static class BuildStats
    {
        private struct Row
        {
            public string Label; public bool IsMul; public string Unit; public StatWords Words; public Func<ModResult, float> Get; public Channel[] Uses;
        }

        /// <summary>A combined multiplier, worded like its first channel.</summary>
        private static Row Mul(string label, Func<ModResult, float> get, params Channel[] uses) =>
            new Row { Label = label, IsMul = true, Words = StatWords.Of(uses[0]), Get = get, Uses = uses };
        private static Row Ch(Channel c, string unit = null) =>
            new Row { Label = ChannelTable.Defs[(int)c].Label, IsMul = ChannelTable.Defs[(int)c].IsMul, Unit = unit, Words = StatWords.Of(c), Get = r => r[c], Uses = new[] { c } };

        /// <summary>The all-status channel on its own covers only the statuses without a line of their own.</summary>
        private static readonly StatWords OtherStatuses = new StatWords
        {
            More = "Other statuses build up {0} faster", Less = "Other statuses build up {0} slower", Never = "Other statuses never build up", HigherIsBetter = false,
        };

        /// <summary>Channels the game cannot apply yet (preflight KNOWN_UNAPPLIED; Pack Rat's slot): never shown as a stat.</summary>
        private static readonly string[] NotApplied = { "invSlots" };

        private static readonly Row[] Rows = BuildRows();

        private static Row[] BuildRows()
        {
            var rows = new List<Row>
            {
                Ch(Channel.sprintSpeed),
                Ch(Channel.jumpHeight),
                Ch(Channel.climbSpeed),
                Ch(Channel.climbStamina),
                Ch(Channel.sprintStamina),
                Ch(Channel.regenRate),
                Ch(Channel.regenDelay, "seconds"),
                Ch(Channel.maxStamina, "stamina"),
                Ch(Channel.bonusStaminaGain),
                Mul("Fall Injury received", r => StatComposites.Injury(r, true), Channel.fallInjury, Channel.allInjury),
                Mul("Other Injury received", r => StatComposites.Injury(r, false), Channel.nonFallInjury, Channel.allInjury),
                Mul("Carried item weight", r => r[Channel.itemWeight], Channel.itemWeight),
                Mul("Item in hand weight", StatComposites.HeldItemWeight, Channel.itemWeight, Channel.heldItemWeight),
                Ch(Channel.carryScoutWeight),
            };
            foreach (var rate in new[] { Channel.hungerRate, Channel.coldRate, Channel.heatRate, Channel.poisonRate, Channel.sporesRate,
                                         Channel.drowsyRate, Channel.thornsRate, Channel.curseRate, Channel.petrifyRate })
            {
                var c = rate;
                rows.Add(Mul(ChannelTable.Defs[(int)c].Label, r => StatComposites.StatusGain(r, c), c, Channel.negativeStatusRate));
            }
            var other = Mul("Other negative statuses", r => r[Channel.negativeStatusRate], Channel.negativeStatusRate);
            other.Words = OtherStatuses;
            rows.Add(other);
            // everything else, channel by channel (decay, food, healing, reach, slipping, huddling)
            var covered = new HashSet<Channel>();
            foreach (var r in rows) foreach (var c in r.Uses) covered.Add(c);
            foreach (var d in ChannelTable.Defs)
                if (!covered.Contains(d.Id) && Array.IndexOf(NotApplied, d.Id.ToString()) < 0) rows.Add(Ch(d.Id));
            return rows.ToArray();
        }


        /// <summary>Lines that differ from an unmodified scout, now or always.</summary>
        public static List<StatLine> Lines(ModResult now, ModResult always)
        {
            var list = new List<StatLine>();
            foreach (var row in Rows)
            {
                float n = row.Get(now), a = row.Get(always);
                float neutral = row.IsMul ? 1f : 0f;
                if (Math.Abs(n - neutral) < 0.0005f && Math.Abs(a - neutral) < 0.0005f) continue;
                list.Add(new StatLine { Label = row.Label, Now = n, Always = a, IsMul = row.IsMul, Unit = row.Unit, Words = row.Words });
            }
            return list;
        }

        /// <summary>Unit of a channel's add value: max stamina is shown x100, the regen delay in seconds.</summary>
        public static string UnitOf(Channel c) => c == Channel.maxStamina ? "stamina" : c == Channel.regenDelay ? "seconds" : null;

        /// <summary>Is this value an unmodified scout's (x1, +0)?</summary>
        public static bool IsNeutral(float v, bool isMul) => Math.Abs(v - (isMul ? 1f : 0f)) < 0.0005f;

        /// <summary>Does this value help the scout (stats.json better)?</summary>
        public static bool Helps(float v, bool isMul, StatWords w) => (v > (isMul ? 1f : 0f)) == w.HigherIsBetter;

        /// <summary>
        /// The value in the cards' own words: "You heat up 30% faster", "Your stamina bar is 25 longer", "Stamina starts
        /// coming back 0.3 seconds sooner", "Climbing never tires you". Null for an unmodified value.
        /// </summary>
        public static string Say(float v, bool isMul, string unit, StatWords w)
        {
            if (IsNeutral(v, isMul)) return null;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            bool up = v > (isMul ? 1f : 0f);
            string amount;
            if (isMul)
            {
                if (v < 0.0005f && !string.IsNullOrEmpty(w.Never)) return w.Never;
                amount = (Math.Abs(v - 1f) * 100f).ToString("0.#", inv) + "%";
            }
            else if (unit == "seconds")
            {
                float sec = Math.Abs(v);
                amount = sec.ToString("0.##", inv) + (Math.Abs(sec - 1f) < 0.005f ? " second" : " seconds");
            }
            else amount = (Math.Abs(v) * (unit == "stamina" ? 100f : 1f)).ToString("0.#", inv);
            return string.Format(up ? w.More : w.Less, amount);
        }

        public static string Say(StatLine l, float v) => Say(v, l.IsMul, l.Unit, l.Words);

        /// <summary>"x1.25", "+20", "-0.5 s"; a multiplier of 0 reads "none".</summary>
        public static string Format(float v, bool isMul, string unit)
        {
            if (isMul) return v < 0.0005f ? "none" : "x" + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            if (unit == "seconds") return (v >= 0 ? "+" : "") + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " s";
            float shown = unit == "stamina" ? v * 100f : v;
            return (shown >= 0 ? "+" : "") + shown.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
