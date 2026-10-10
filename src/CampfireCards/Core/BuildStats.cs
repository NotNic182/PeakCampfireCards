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
            public string Label; public bool IsMul; public string Unit; public Func<ModResult, float> Get; public Channel[] Uses;
        }

        private static Row Mul(string label, Func<ModResult, float> get, params Channel[] uses) => new Row { Label = label, IsMul = true, Get = get, Uses = uses };
        private static Row Ch(Channel c) => new Row { Label = ChannelTable.Defs[(int)c].Label, IsMul = ChannelTable.Defs[(int)c].IsMul, Get = r => r[c], Uses = new[] { c } };

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
                new Row { Label = ChannelTable.Defs[(int)Channel.regenDelay].Label, IsMul = false, Unit = "seconds", Get = r => r[Channel.regenDelay], Uses = new[] { Channel.regenDelay } },
                new Row { Label = ChannelTable.Defs[(int)Channel.maxStamina].Label, IsMul = false, Unit = "stamina", Get = r => r[Channel.maxStamina], Uses = new[] { Channel.maxStamina } },
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
            rows.Add(Mul("Other negative statuses", r => r[Channel.negativeStatusRate], Channel.negativeStatusRate));
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
                list.Add(new StatLine { Label = row.Label, Now = n, Always = a, IsMul = row.IsMul, Unit = row.Unit });
            }
            return list;
        }

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
