using System.Collections.Generic;
using CampfireCards.Core;
using Xunit;

namespace CampfireCards.Tests
{
    public class DeferredDebtTests
    {
        private static DelayedInjuryQueue PaymentPlan() =>
            new DelayedInjuryQueue { DeferShare = .75f, Keep = .8f, Delay = 20f };

        [Fact]
        public void OverdueDebtSurvivesDeadTicksAndIsCollectedOnceAfterRevival()
        {
            var q = PaymentPlan();
            Assert.Equal(.1f, q.Add(.4f, 100f), 5);

            Assert.Equal(0f, q.Tick(120f, canApply: false));
            Assert.Equal(0f, q.Tick(200f, canApply: false));
            Assert.Equal(1, q.Pending);

            Assert.Equal(.24f, q.Tick(200f, canApply: true), 5);
            Assert.Equal(0, q.Pending);
            Assert.Equal(0f, q.Tick(200f));
            Assert.Equal(0f, q.Tick(201f));
        }

        [Fact]
        public void TwoOverdueFallsAreCollectedTogetherAfterRevival()
        {
            var q = PaymentPlan();
            q.Add(.4f, 0f);
            q.Add(.2f, 5f);

            Assert.Equal(0f, q.Tick(20f, canApply: false));
            Assert.Equal(0f, q.Tick(50f, canApply: false));
            Assert.Equal(2, q.Pending);

            Assert.Equal(.36f, q.Tick(50f), 5);
            Assert.Equal(0, q.Pending);
            Assert.Equal(0f, q.Tick(50f));
        }

        [Fact]
        public void RevivalCollectsOnlyOverdueFallsWithoutRestartingFutureTimers()
        {
            var q = PaymentPlan();
            q.Add(.4f, 0f);
            q.Add(.2f, 25f);

            Assert.Equal(0f, q.Tick(30f, canApply: false));
            Assert.Equal(.24f, q.Tick(30f), 5);
            Assert.Equal(1, q.Pending);
            Assert.Equal(0f, q.Tick(44.99f));
            Assert.Equal(.12f, q.Tick(45f), 5);
            Assert.Equal(0, q.Pending);
            Assert.Equal(0f, q.Tick(45f));
        }

        [Fact]
        public void OrdinaryCollectionKeepsTheOriginalTwentySecondDeadline()
        {
            var q = PaymentPlan();
            q.Add(.4f, 10f);

            Assert.Equal(0f, q.Tick(29.99f));
            Assert.Equal(1, q.Pending);
            Assert.Equal(.24f, q.Tick(30f), 5);
            Assert.Equal(0, q.Pending);
            Assert.Equal(0f, q.Tick(31f));
        }

        [Fact]
        public void ExplicitClearStillForgivesDebtThatWasHeldWhileDead()
        {
            var q = PaymentPlan();
            q.Add(.4f, 0f);
            Assert.Equal(0f, q.Tick(25f, canApply: false));
            Assert.Equal(1, q.Pending);

            q.Clear();

            Assert.Equal(0, q.Pending);
            Assert.Equal(0f, q.Tick(30f));
        }

        [Fact]
        public void DueFallsRemainSeparateWithTheirOriginalNativeExemptions()
        {
            var q = PaymentPlan();
            q.Add(.4f, 0f, ignoreInvincibility: true);
            q.Add(.2f, 1f, ignoreSkeleton: true);
            q.Add(.1f, 2f);
            var payments = new List<(float amount, bool ignoreInvincibility, bool ignoreSkeleton)>();

            q.CollectDue(22f, true, (amount, invincible, skeleton) => payments.Add((amount, invincible, skeleton)));

            Assert.Equal(3, payments.Count);
            Assert.Equal(.24f, payments[0].amount, 5);
            Assert.True(payments[0].ignoreInvincibility);
            Assert.False(payments[0].ignoreSkeleton);
            Assert.Equal(.12f, payments[1].amount, 5);
            Assert.False(payments[1].ignoreInvincibility);
            Assert.True(payments[1].ignoreSkeleton);
            Assert.Equal(.06f, payments[2].amount, 5);
            Assert.False(payments[2].ignoreInvincibility);
            Assert.False(payments[2].ignoreSkeleton);
            Assert.Equal(0, q.Pending);
        }

        [Fact]
        public void DeadTicksRetainPaymentExemptionsUntilRevival()
        {
            var q = PaymentPlan();
            q.Add(.4f, 0f, ignoreInvincibility: true, ignoreSkeleton: true);
            var payments = new List<(float amount, bool ignoreInvincibility, bool ignoreSkeleton)>();
            void Apply(float amount, bool invincible, bool skeleton) => payments.Add((amount, invincible, skeleton));

            q.CollectDue(20f, false, Apply);
            q.CollectDue(50f, false, Apply);
            Assert.Empty(payments);
            Assert.Equal(1, q.Pending);

            q.CollectDue(50f, true, Apply);
            Assert.Single(payments);
            Assert.Equal(.24f, payments[0].amount, 5);
            Assert.True(payments[0].ignoreInvincibility);
            Assert.True(payments[0].ignoreSkeleton);
            Assert.Equal(0, q.Pending);
            q.CollectDue(51f, true, Apply);
            Assert.Single(payments);
        }

        [Fact]
        public void SeparateCollectionKeepsFallOrderDeadlinesAndSinglePayment()
        {
            var q = PaymentPlan();
            q.Add(.1f, 0f);
            q.Add(.2f, 5f);
            q.Add(.3f, 10f);
            var payments = new List<float>();
            void Apply(float amount, bool invincible, bool skeleton) => payments.Add(amount);

            q.CollectDue(19.99f, true, Apply);
            Assert.Empty(payments);
            q.CollectDue(25f, true, Apply);
            Assert.Equal(2, payments.Count);
            Assert.Equal(.06f, payments[0], 5);
            Assert.Equal(.12f, payments[1], 5);
            Assert.Equal(1, q.Pending);
            q.CollectDue(25f, true, Apply);
            q.CollectDue(29.99f, true, Apply);
            Assert.Equal(2, payments.Count);
            q.CollectDue(30f, true, Apply);
            Assert.Equal(3, payments.Count);
            Assert.Equal(.18f, payments[2], 5);
            Assert.Equal(0, q.Pending);
        }

        [Fact]
        public void InvincibleRevivalCanBlockOrdinaryDebtWithoutBlockingExemptDebt()
        {
            var q = PaymentPlan();
            q.Add(.4f, 0f);
            q.Add(.2f, 1f, ignoreInvincibility: true);
            q.CollectDue(30f, false, (amount, invincible, skeleton) => Assert.Fail("Dead scouts cannot receive payments."));
            float applied = 0f;

            // Native AddStatus rejects ordinary Injury while invincible, but honors the original ignore flag.
            q.CollectDue(30f, true, (amount, ignoreInvincibility, ignoreSkeleton) =>
            {
                if (ignoreInvincibility) applied += amount;
            });

            Assert.Equal(.12f, applied, 5);
            Assert.Equal(0, q.Pending);
        }
    }
}
