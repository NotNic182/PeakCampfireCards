using System;
using CampfireCards.Core;
using Xunit;

namespace CampfireCards.Tests
{
    /// <summary>
    /// Simulates PEAK's AddStatus / SubtractStatus accumulator (whole 0.025 steps, leftover thrown away after a step
    /// lands) with and without StatusCarry, using the audit's real cases.
    /// </summary>
    public class StatusCarryTests
    {
        private const float Step = 0.025f;

        /// <summary>The game: pending += amount; once it reaches a step, whole steps land and pending resets to 0.</summary>
        private static float Native(ref float pending, float amount)
        {
            pending += amount;
            if (pending < Step) return 0f;
            float landed = (float)Math.Floor(pending / Step) * Step;
            pending = 0f;
            return landed;
        }

        /// <summary>The mod in front of the game, as Hooks.Status does it.</summary>
        private static float WithCarry(StatusCarry carry, ref float pending, float amount)
        {
            float toGame = carry.Route(0, pending, amount, out bool reset);
            if (reset) pending = 0f;
            return Native(ref pending, toGame);
        }

        [Fact]
        public void WithoutCarryAScaledFiveInjuryFallRoundsBackToFive()
        {
            float pending = 0f;
            Assert.Equal(0.05f, Native(ref pending, 0.05f * 1.3f), 5);   // the bug: x1.3 lost
        }

        [Fact]
        public void MiniScoutCannonsThirtyPercentHoldsOverManySmallFalls()
        {
            var carry = new StatusCarry(1);
            float pending = 0f, total = 0f;
            for (int i = 0; i < 20; i++) total += WithCarry(carry, ref pending, 0.05f * 1.3f);
            Assert.InRange(total, 20 * 0.065f - Step, 20 * 0.065f + 0.0001f);   // within one step of exactly +30%
        }

        [Fact]
        public void ToTheBoneWarmingIsTenPercentSlowerNotTwiceAsSlow()
        {
            // body heat removes 5 Cold per tick; x0.909 must average 4.545, not round down to 2.5
            var carry = new StatusCarry(1);
            float pending = 0f, removed = 0f;
            for (int i = 0; i < 40; i++) removed += WithCarry(carry, ref pending, 0.05f / 1.1f);
            Assert.InRange(removed, 40 * 0.05f / 1.1f - Step, 40 * 0.05f / 1.1f + 0.0001f);
            float native = 0f, nativePending = 0f;
            for (int i = 0; i < 40; i++) native += Native(ref nativePending, 0.05f / 1.1f);
            Assert.Equal(40 * 0.025f, native, 4);   // what the game alone would do: half the warming
        }

        [Fact]
        public void CrownsTwoCursePerCampfireAveragesTwo()
        {
            var carry = new StatusCarry(1);
            float pending = 0f, total = 0f;
            for (int i = 0; i < 10; i++) total += WithCarry(carry, ref pending, 0.02f);
            Assert.InRange(total, 0.2f - Step, 0.2f + 0.0001f);
        }

        [Fact]
        public void CardRemovalFractionsAreBankedAndDeliveredLater()
        {
            // Medicinal Munchies at 60% strength removes 4.5 Injury per snack. The amount reaches the game UNTOUCHED
            // (its exact-amount cure rules read it), so each use lands 2.5 and Bank keeps the 2 the game discards;
            // the next routed removal (an item heal, natural decay) delivers the banked rest.
            var carry = new StatusCarry(1);
            float pending = 0f, removed = 0f;
            for (int i = 0; i < 10; i++)
            {
                carry.Bank(0, pending, 0.045f);
                removed += Native(ref pending, 0.045f);
            }
            Assert.Equal(0.25f, removed, 4);                    // what lands immediately: 2.5 per use, as in vanilla
            removed += WithCarry(carry, ref pending, 0.001f);   // the next routed removal delivers the bank
            Assert.InRange(removed, 0.45f - Step, 0.45f + 0.001f);
        }

        [Fact]
        public void BankKeepsNothingFromWholeStepAmounts()
        {
            // a 7.5-point removal at full strength lands exactly; Bank must not invent a leftover
            var carry = new StatusCarry(1);
            float pending = 0f;
            carry.Bank(0, pending, 0.075f);
            Native(ref pending, 0.075f);
            Assert.True(carry.Of(0) < 0.0005f);
        }

        [Fact]
        public void SmallPerFrameAmountsStillGoToTheGamesOwnAccumulator()
        {
            var carry = new StatusCarry(1);
            float pending = 0.01f;
            float toGame = carry.Route(0, pending, 0.001f, out bool reset);
            Assert.False(reset);                 // below a step: vanilla keeps accumulating (and timing) as usual
            Assert.Equal(0.001f, toGame, 6);
            Assert.Equal(0f, carry.Of(0));
        }

        [Fact]
        public void ExactStepsLandWithoutLeavingAnything()
        {
            var carry = new StatusCarry(1);
            float pending = 0f;
            Assert.Equal(0.075f, WithCarry(carry, ref pending, 0.075f), 5);
            Assert.True(carry.Of(0) < 0.0001f);
        }

        [Fact]
        public void PetrifyFractionsCarryToTheNextGain()
        {
            float carry = 0f;
            int total = 0;
            for (int i = 0; i < 20; i++) total += StatusCarry.Whole(ref carry, 1 * 0.65f);   // +1 gains, 35% resistance
            Assert.InRange(total, 12, 13);   // 13 of 20, not 20 of 20 as with per-gain rounding
        }

        [Fact]
        public void ResetForgetsEveryLeftover()
        {
            var carry = new StatusCarry(3);
            carry.Route(1, 0f, 0.06f, out _);
            Assert.True(carry.Of(1) > 0f);
            carry.Reset();
            Assert.Equal(0f, carry.Of(1));
        }
    }
}
