using System;
using System.Collections.Generic;
using CampfireCards.Core;
using CampfireCards.Generated;

namespace CampfireCards.Sim
{
    /// <summary>
    /// The stamina-economy model of one PEAK biome at Ascent 7-8. Everything a card does is converted into one
    /// currency: stamina-bar POINTS PER BIOME (100 points = one full bar), using the game's own constants
    /// (decompiled 2.6.b): the bar is 1.0 minus the status sum, regen is 20 pts/s on the ground only, wall climbing
    /// drains 2..20 pts/s by input and slope, a climb jump costs 20, sprinting 2.5 pts/s, hunger builds 0.05 pts/s.
    /// Model assumptions (activity mix, status pressure, condition uptimes) are the named constants below - they are
    /// the debatable part, and the per-card formulas read the REAL card numbers from the sheets at runtime.
    /// </summary>
    public static class Profile
    {
        // ---- activity mix per biome (seconds); a biome is roughly 8 minutes of mountain
        public const float BiomeSeconds = 480f;
        public const float ClimbSeconds = 150f;    // active wall climbing
        public const float HangSeconds = 25f;      // hanging still mid-wall (rest points)
        public const float RopeVineSeconds = 40f;
        public const float SprintSeconds = 90f;
        public const float ClimbJumps = 10f;

        // ---- game constants (decompiled), in pts (1 bar = 100) per second
        public const float ClimbDrain = 9f;        // avg of min 2 and max 20 by input/slope
        public const float HangDrain = 2f;         // minStaminaUsage 0.02/s
        public const float RopeDrain = 1f;
        public const float SprintDrain = 2.5f;     // sprintStaminaUsage 0.025/s
        public const float JumpCost = 20f;         // UseStamina(0.2) per climb jump
        public const float Regen = 20f;            // AddStamina(0.2/s) grounded

        /// <summary>Gross stamina spent per biome before any cards (the budget a % discount works on).</summary>
        public static float GrossSpend =>
            ClimbSeconds * ClimbDrain + HangSeconds * HangDrain + RopeVineSeconds * RopeDrain
            + SprintSeconds * SprintDrain + ClimbJumps * JumpCost;

        // ---- what one freed BAR POINT is worth per biome: the bar gates the ~6 long walls a biome where you
        // drain it fully, and running dry on a wall risks a fall, hence the safety factor.
        public const float HardWallsPerBiome = 6f;
        public const float WallSafetyFactor = 1.5f;
        public const float BarValue = HardWallsPerBiome * WallSafetyFactor;   // 9 pts of budget per bar point

        // yellow (extra) stamina does not regen but spends like bar: slight premium for burst timing
        public const float YellowFactor = 1.1f;

        // ---- average status load on the bar (pts, steady state) and incoming pressure (pts gained per biome).
        // A rate modifier changes INCOMING; roughly half of incoming becomes standing LOAD (decay/meals/heals
        // absorb the rest), so dLoad = dIncoming * IncomingToLoad, valued at BarValue.
        public const float IncomingToLoad = 0.5f;
        public static readonly Dictionary<string, (float load, float incoming)> Status = new Dictionary<string, (float, float)>
        {
            { "hunger", (10f, 24f) },   // 0.05 pts/s x 480s, eaten back down
            { "cold",   (5f, 12f) },
            { "hot",    (4f, 10f) },
            { "poison", (5f, 9f) },
            { "spores", (2f, 4f) },
            { "drowsy", (2f, 5f) },
            { "thorns", (2.5f, 5f) },
            { "injury", (14f, 32f) },   // 2 falls x 12 + 6 non-fall + 2 dynamite, healed slowly
            { "curse",  (7f, 10f) },    // revives and curse cards
            { "weight", (9f, 0f) },     // carried gear; not a rate
        };

        public const float FallsPerBiome = 2f;
        public const float MeanFallInjury = 12f;
        public const float MealsPerBiome = 1.5f;
        public const float HungerPerMeal = 25f;
        public const float CookedMealShare = 0.6f;
        public const float HealsPerBiome = 0.8f;
        public const float InjuryPerHeal = 25f;
        public const float CurativesPerBiome = 0.5f;
        public const float RevivesPerBiomePerPlayer = 0.15f;
        public const float CursePerRevive = 15f;    // Ascents.revivalCurse at Ascent 7-8 (0.15)

        // dying mid-run costs the team a rescue and you your momentum
        public const float DeathCost = 350f;
        public const float FatalRiskPerBiome = 0.18f;

        /// <summary>Weight composite inputs (game units): items carried, the one in hand, backpack contents.</summary>
        public const float CarriedWeightLoad = 9f;    // avg weight pts on the bar from gear
        public const float HeldShare = 0.35f, BackpackShare = 0.35f, LooseShare = 0.30f;

        // drain is charged per second of the activity, and a fixed route takes route/speed seconds, so total drain
        // scales exactly 1/speed; speed also buys tempo (keeping up, exposure time), the pace utilities below
        public const float SpeedExponent = 1f;
        public const float ClimbPaceUtilityPer100 = 50f;
        public const float SprintPaceUtilityPer100 = 60f;

        // reach/jump utility per +100%: +40% grab reach skips roughly a tenth of all climbing (~150 pts) and
        // prevents a share of falls (~20 pts) - 170 per +40% = ~425 per +100%; jumps skip smaller moves
        public const float GrabReachUtilityPer100 = 425f;
        public const float JumpHeightUtilityPer100 = 180f;
        public const float HelpReachTeamUtilityPer100 = 40f;
        public const float SlipUtilityPer100 = 40f;

        /// <summary>How much of a teammate-facing effect counts toward the card's headline value.</summary>
        public const float TeamWeight = 0.5f;
        public const int Teammates = 3;

        /// <summary>Fraction of time each card condition holds (of the activity it gates).</summary>
        public static readonly Dictionary<Condition, float> Uptime = new Dictionary<Condition, float>
        {
            { Condition.always, 1f },
            { Condition.hangingStill, 1f },          // handled as its own activity segment
            { Condition.crouchStill, 0.5f },         // of grounded rest time
            { Condition.grounded, 0.45f },
            { Condition.injury20, 0.45f },
            { Condition.poisonOrSpores20, 0.15f },
            { Condition.poisonAbove20, 0.12f },
            { Condition.climbingWithItem, 0.25f },
            { Condition.noBackpack, 0.75f },     // backpacks are scarce loot, and this card's owner stays packless
            { Condition.wearingBackpack, 0.25f },
            { Condition.teammateNear, 0.55f },
            { Condition.noTeammateNear, 0.45f },
            { Condition.separatedFromTeam, 0.25f },
            { Condition.momentumActive, 0.35f },     // of sprint time, after the 2s buildup
            { Condition.pulseActive, 0.45f },        // 2s boost per 2s buildup = 50% duty on unbroken walls, damped for resets
            { Condition.climbingColdSurface, 0.12f },
        };

        public static float UptimeOf(Condition c) => Uptime.TryGetValue(c, out var u) ? u : 0.2f;
    }
}
