using CampfireCards.Core;
using Xunit;

namespace CampfireCards.Tests
{
    public class MovementTimerTests
    {
        [Fact]
        public void ClearingMomentumDiscardsOldBoostWithoutChargingAnOldCrash()
        {
            var momentum = new MomentumMachine();
            momentum.Update(2f, true, 2f);
            Assert.True(momentum.Active);
            momentum.ClearMotion(); // the card leaves the collection
            Assert.False(momentum.Active);
            Assert.False(momentum.Update(0f, false, 10f)); // reacquisition while standing does not crash
            momentum.Update(1.5f, true, 11.5f);
            Assert.False(momentum.Active); // must build a fresh continuous sprint
            momentum.Update(.5f, true, 12f);
            Assert.True(momentum.Active);
        }

        [Fact]
        public void ClearingMomentumPreservesTheExistingThirtySecondCrashCooldown()
        {
            var momentum = new MomentumMachine();
            momentum.Update(2f, true, 2f);
            Assert.True(momentum.Update(0f, false, 3f)); // cooldown lasts until 33
            momentum.Update(2f, true, 5f);
            momentum.ClearMotion(); // removal during another boost must not reset the cooldown
            momentum.Update(2f, true, 7f);
            Assert.False(momentum.Update(0f, false, 8f));
            momentum.Update(2f, true, 32f);
            Assert.False(momentum.Update(0f, false, 32.99f));
            momentum.Update(2f, true, 33f);
            Assert.True(momentum.Update(0f, false, 33f));
        }

        [Fact]
        public void ResettingPulseDiscardsBothActiveBoostAndPartialClimbBuildup()
        {
            var pulse = new PulseMachine();
            pulse.Update(3f, true); Assert.True(pulse.Active);
            pulse.Reset(); Assert.False(pulse.Active);
            pulse.Update(2f, true); Assert.False(pulse.Active);
            pulse.Reset(); // sacrificing during buildup also removes its progress
            pulse.Update(2f, true); Assert.False(pulse.Active);
            pulse.Update(1f, true); Assert.True(pulse.Active);
        }
    }
}
