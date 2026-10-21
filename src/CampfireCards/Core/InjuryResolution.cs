using System;

namespace CampfireCards.Core
{
    /// <summary>Resolve a hit before committing any shared Injury or deferred payment.</summary>
    public static class InjuryResolution
    {
        public static float Resolve(float injury, float shareFactor, float absorb, float cap, bool fall,
            Func<float, bool> preventFall, Func<float, float> deferFall, Action<float> transfer, out bool prevented)
        {
            prevented = false;
            if (injury <= 0f) return injury;
            float factor = Math.Max(0f, Math.Min(1f, shareFactor));
            float victim = injury * factor;
            float shared = Math.Min(injury * (1f - factor) * Math.Max(0f, absorb), Math.Max(0f, cap));
            if (fall && preventFall != null && preventFall(victim))
            {
                prevented = true;
                return 0f;
            }
            if (shared > 0f) transfer?.Invoke(shared);   // the caller adds small shares up (Hooks.OweShare)
            return fall && deferFall != null ? deferFall(victim) : victim;
        }
    }

    /// <summary>Predict native Injury without applying its multipliers twice or spending a rescue on a blocked hit.</summary>
    public static class NativeInjuryRules
    {
        public static bool Blocks(bool zombie, bool locked, bool scoutmaster, bool airport, bool invincible, bool ignoreInvincibility) =>
            zombie || locked || scoutmaster || airport || (invincible && !ignoreInvincibility);

        public static float Multiplier(bool skeleton, bool ignoreSkeleton, float difficulty) =>
            (skeleton && !ignoreSkeleton ? 8f : 1f) * difficulty;

        /// <summary>
        /// Each native AddStatus floors its pending Injury to 2.5-point steps, discards the remainder after a commit,
        /// and caps Injury separately. Simulate both installments while other current statuses remain unchanged.
        /// </summary>
        public static bool WouldPassOut(float statusSum, float injury, float pending, float injuryCap,
            float nativeMultiplier, float immediate, float deferred = 0f, float passOutThreshold = 1f)
        {
            Commit(ref statusSum, ref injury, ref pending, injuryCap, immediate * nativeMultiplier);
            Commit(ref statusSum, ref injury, ref pending, injuryCap, deferred * nativeMultiplier);
            return statusSum > passOutThreshold - .01f;   // default 1f reproduces vanilla's 0.99
        }

        private static void Commit(ref float total, ref float injury, ref float pending, float cap, float amount)
        {
            if (amount <= 0f) return;
            pending += amount;
            if (pending < .025f) return;
            float added = (float)Math.Floor(pending / .025f) * .025f;
            added = Math.Min(added, Math.Max(0f, 2f - total));
            float updated = Math.Max(0f, Math.Min(cap, injury + added));
            updated = (float)Math.Round(updated * 40f, MidpointRounding.ToEven) / 40f;
            total += updated - injury;
            injury = updated;
            pending = 0f;
        }
    }
}
