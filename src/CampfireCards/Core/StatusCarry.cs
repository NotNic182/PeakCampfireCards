using System;

namespace CampfireCards.Core
{
    /// <summary>
    /// PEAK changes a status only in whole 0.025 steps and throws away whatever is left over each time a step lands
    /// (CharacterAfflictions.AddStatus / SubtractStatus reset their accumulator to 0). A card that scales one chunky
    /// tick (a 5-point fall ×1.3 = 6.5, body heat 5 ×0.909 = 4.5) would lose its whole effect to that rounding. When a
    /// card changed an amount, the leftover is kept here and handed back on the next change of the same status, so the
    /// card's percentage holds on average. Amounts below a step still go to the game's own accumulator untouched, so
    /// vanilla timing (last-added time, decay cooldowns) is unchanged. Unity-free so it can be unit tested.
    /// </summary>
    public sealed class StatusCarry
    {
        public const float Step = 0.025f;
        private const float Epsilon = 0.00001f;
        private readonly float[] _carry;

        public StatusCarry(int statuses) { _carry = new float[statuses]; }

        public float Of(int status) => status >= 0 && status < _carry.Length ? _carry[status] : 0f;
        public void Clear(int status) { if (status >= 0 && status < _carry.Length) _carry[status] = 0f; }
        public void Reset() => Array.Clear(_carry, 0, _carry.Length);

        /// <summary>
        /// amount: what the game will accumulate for this change, after its own multipliers. pending: the game's own
        /// accumulator for this status. Returns what to hand the game. resetPending: clear the game's accumulator first
        /// (the returned amount already includes it), so exactly whole steps land and the remainder stays here.
        /// </summary>
        public float Route(int status, float pending, float amount, out bool resetPending)
        {
            float carried = _carry[status];
            float total = Math.Max(0f, pending) + carried + amount;
            if (total < Step - Epsilon)
            {
                // below a step: the game keeps it in its own accumulator, so nothing is lost yet
                _carry[status] = 0f;
                resetPending = false;
                return amount + carried;
            }
            float landed = (float)Math.Floor((total + Epsilon) / Step) * Step;
            _carry[status] = Math.Max(0f, total - landed);
            resetPending = true;
            return landed + Epsilon;   // the game floors pending / 0.025: a hair above the step count keeps float error out
        }

        /// <summary>
        /// The same idea for a whole-number amount (Petrify is an int 0..100): returns the whole part to apply now and
        /// keeps the fraction for the next gain, instead of rounding each gain on its own.
        /// </summary>
        public static int Whole(ref float carry, float scaled)
        {
            float total = Math.Max(0f, carry + scaled);
            int whole = (int)Math.Floor(total + 0.0001f);
            carry = Math.Max(0f, total - whole);
            return whole;
        }
    }
}
