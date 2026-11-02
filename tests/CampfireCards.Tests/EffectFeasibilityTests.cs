using CampfireCards.Core;
using CampfireCards.Generated;
using Xunit;

namespace CampfireCards.Tests
{
    /// <summary>Sandbox calculations for new designs. Nothing here adds a card to the generated deck.</summary>
    public class EffectFeasibilityTests
    {
        [Fact]
        public void DoubleCleanStaminaAndSevereNonFallFragilityCanCoexist()
        {
            var result = new ModResult();
            result.Fold(new[]
            {
                new ActiveMod { Channel = Channel.maxStamina, Op = ModOp.add, Value = 1f, Rarity = CardRarity.Rare },
                new ActiveMod { Channel = Channel.nonFallInjury, Op = ModOp.mul, Value = 8f, Rarity = CardRarity.Rare }
            }, new CardSettings());
            Assert.Equal(1f, result[Channel.maxStamina]);
            // the drawn bar (and the real maximum with it) stops at the knockout ceiling: a 2.0 bar never matched
            // the 1.9-capped pass-out threshold anyway, so a double-stamina build plays as +90, not +100
            Assert.Equal(StatComposites.MaxBarFrame, StatComposites.BarFrame(result[Channel.maxStamina], 0f));
            Assert.Equal(8f, StatComposites.Injury(result, fall: false));
            Assert.Equal(1f, StatComposites.Injury(result, fall: true));
            // This affects ALL non-fall Injury. Falling-object-only fragility needs a new source-aware hook.
        }

        [Fact]
        public void HalfStaminaCostsRemainValidNormalModifiersButStackingRespectsTheCap()
        {
            var result = new ModResult(); var settings = new CardSettings();
            var halfCost = new ActiveMod { Channel = Channel.climbStamina, Op = ModOp.mul, Value = 0.5f, Rarity = CardRarity.Rare };
            result.Fold(new[] { halfCost }, settings);
            Assert.Equal(0.5f, result[Channel.climbStamina]);
            result.Fold(new[] { halfCost, halfCost }, settings);
            Assert.Equal(ModResult.SoftCostCap(0.25f, 0.4f), result[Channel.climbStamina], precision: 5); // normal-card soft cap past 60% off
            Assert.True(result[Channel.climbStamina] < 0.4f && result[Channel.climbStamina] > 0.25f);
        }
    }
}
