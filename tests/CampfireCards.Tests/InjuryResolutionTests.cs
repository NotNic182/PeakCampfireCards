using System;
using System.Collections.Generic;
using CampfireCards.Core;
using Xunit;

namespace CampfireCards.Tests
{
    public class InjuryResolutionTests
    {
        [Fact]
        public void FatalDeferredFallRescueDoesNotChargeHelperOrQueueAnyOfThatFall()
        {
            var queue = new DelayedInjuryQueue { DeferShare = .75f, Keep = .8f, Delay = 20f };
            float transferred = 0f;
            float immediate = InjuryResolution.Resolve(.8f, .7f, .5f, .1f, true,
                victim => NativeInjuryRules.WouldPassOut(.6f, .1f, 0f, 1f, 1f, victim * .25f, victim * .75f * .8f),
                victim => queue.Add(victim, 0f), share => transferred += share, out bool rescued);
            Assert.True(rescued);
            Assert.Equal(0f, immediate);
            Assert.Equal(0f, transferred);
            Assert.Equal(0, queue.Pending);
            Assert.Equal(0f, queue.Tick(20f));
        }

        [Fact]
        public void NonfatalSharedFallCommitsTransferThenQueuesOnlyTheVictimsShare()
        {
            var queue = new DelayedInjuryQueue { DeferShare = .75f, Keep = .8f, Delay = 20f };
            var events = new List<string>();
            float transferred = 0f;
            float immediate = InjuryResolution.Resolve(.5f, .7f, .5f, .1f, true,
                victim => { events.Add("rescue-check"); Assert.Equal(.35f, victim, 5); return false; },
                victim => { events.Add("defer"); return queue.Add(victim, 0f); },
                share => { events.Add("transfer"); transferred = share; }, out bool rescued);
            Assert.False(rescued);
            Assert.Equal(new[] { "rescue-check", "transfer", "defer" }, events);
            Assert.Equal(.075f, transferred, 5);
            Assert.Equal(.0875f, immediate, 5);
            Assert.Equal(.21f, queue.Tick(20f), 5);
        }

        [Fact]
        public void RescueDoesNotForgiveAnOlderFallsDebt()
        {
            var queue = new DelayedInjuryQueue { DeferShare = .75f, Keep = .8f, Delay = 20f };
            queue.Add(.1f, 0f);
            InjuryResolution.Resolve(1f, 1f, .5f, .1f, true, _ => true,
                victim => queue.Add(victim, 10f), _ => throw new Exception("No transfer"), out bool rescued);
            Assert.True(rescued);
            Assert.Equal(1, queue.Pending);
            Assert.Equal(.06f, queue.Tick(20f), 5);
        }

        [Fact]
        public void NonfallInjuryNeverSpendsFallInsuranceOrDefers()
        {
            float transfer = 0f;
            float immediate = InjuryResolution.Resolve(.4f, .7f, .5f, .1f, false,
                _ => throw new Exception("Not a fall"), _ => throw new Exception("Not a fall"),
                share => transfer = share, out bool rescued);
            Assert.False(rescued);
            Assert.Equal(.28f, immediate, 5);
            Assert.Equal(.06f, transfer, 5);
        }

        [Fact]
        public void InvincibilityBlocksCardSideEffectsUnlessTheNativeHitBypassesIt()
        {
            Assert.True(NativeInjuryRules.Blocks(false, false, false, false, true, false));
            Assert.False(NativeInjuryRules.Blocks(false, false, false, false, true, true));
            Assert.True(NativeInjuryRules.Blocks(false, true, false, false, true, true));
            Assert.True(NativeInjuryRules.Blocks(true, false, false, false, false, false));
            Assert.True(NativeInjuryRules.Blocks(false, false, true, false, false, false));
            Assert.True(NativeInjuryRules.Blocks(false, false, false, true, false, false));
        }

        [Fact]
        public void NativeInstallmentRoundingDoesNotSpendInsuranceOnASafeDeferredFall()
        {
            Assert.False(NativeInjuryRules.WouldPassOut(.9f, .1f, 0f, 1f, 1f, .0275f, .066f));
            // A slightly larger installment crosses a native step and now is fatal.
            Assert.True(NativeInjuryRules.WouldPassOut(.9f, .1f, 0f, 1f, 1f, .05f, .075f));
        }

        [Fact]
        public void PredictionIncludesPendingInjuryAndTheNativeInjuryCap()
        {
            Assert.True(NativeInjuryRules.WouldPassOut(.975f, .1f, .01f, 1f, 1f, .02f));
            Assert.False(NativeInjuryRules.WouldPassOut(.7f, .1f, 0f, .1f, 1f, 2f));
        }

        [Fact]
        public void SkeletonAndDifficultyAffectPredictionWithoutChangingInputDamage()
        {
            float skeleton = NativeInjuryRules.Multiplier(true, false, 1f);
            float exempt = NativeInjuryRules.Multiplier(true, true, 1f);
            Assert.True(NativeInjuryRules.WouldPassOut(.7f, .1f, 0f, 1f, skeleton, .04f));
            Assert.False(NativeInjuryRules.WouldPassOut(.7f, .1f, 0f, 1f, exempt, .04f));
            Assert.True(NativeInjuryRules.WouldPassOut(.8f, .1f, 0f, 1f,
                NativeInjuryRules.Multiplier(false, false, 2f), .1f));
        }
    }
}
