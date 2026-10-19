using System;
using System.Collections.Generic;
using System.Linq;
using CampfireCards.Core;
using CampfireCards.Generated;
using Xunit;

namespace CampfireCards.Tests
{
    public class DifficultyScaleTests
    {
        private static CardDef Card(string key) => CardTable.Cards.First(c => c.Key == key);
        private static int Mod(CardDef c, Channel ch) => Array.FindIndex(c.Mods, m => m.Channel == ch);

        private static CardSettings At(float strength) => new CardSettings { BenefitStrength = strength };

        [Theory]
        [InlineData(-1, 0)] [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(3, 1)]
        [InlineData(4, 2)] [InlineData(6, 2)] [InlineData(7, 3)] [InlineData(8, 3)]
        public void AscentsMapToTheFourTiers(int ascent, int tier) => Assert.Equal(tier, DifficultyScale.TierOf(ascent));

        [Fact]
        public void DefaultTiersAreFullStrengthEverywhere()
        {
            // the 0.7.1 rebalance tunes the cards themselves; the tiers stay available but default to 100%
            var s = new CardSettings();
            Assert.Equal(1f, s.StrengthFor(0), 4);
            Assert.Equal(1f, s.StrengthFor(-1), 4);
            Assert.Equal(1f, s.StrengthFor(2), 4);
            Assert.Equal(1f, s.StrengthFor(5), 4);
            Assert.Equal(1f, s.StrengthFor(8), 4);
        }

        [Fact]
        public void BenefitsShrinkAndDrawbacksStay()
        {
            var s = At(0.6f);
            var daddy = Card("LedgeDancer");
            Assert.Equal(1.24f, s.ModValue(daddy, Mod(daddy, Channel.grabReach)), 4);        // +40% -> +24%
            Assert.Equal(1.05f, s.ModValue(daddy, Mod(daddy, Channel.staminaUsage)), 4);     // drawback unchanged
            Assert.Equal(1.4f, s.RawModValue(daddy, Mod(daddy, Channel.grabReach)), 4);
            var piton = Card("GeckoGrip");
            Assert.Equal(1f - 0.18f * 0.6f, s.ModValue(piton, Mod(piton, Channel.climbStamina)), 4);   // -18% -> -10.8%
            Assert.Equal(1.1f, s.ModValue(piton, Mod(piton, Channel.sprintStamina)), 4);
            var lungs = Card("DeepLungs");
            Assert.Equal(0.18f, s.ModValue(lungs, Mod(lungs, Channel.maxStamina)), 4);       // +30 -> +18
            var tax = Card("CardioTax");
            Assert.Equal(-1, Mod(tax, Channel.maxStamina));                                  // 0.8.0 removed the -2.5 max-stamina drawback outright
            Assert.Equal(1.21f, s.ModValue(tax, Mod(tax, Channel.sprintSpeed)), 4);          // its +35% sprint benefit still scales: -> +21%
        }

        [Fact]
        public void MythicsAndAllOrNothingBenefitsNeverScale()
        {
            var s = At(0.6f);
            var heir = Card("MountainsHeir");
            for (int i = 0; i < heir.Mods.Length; i++) Assert.Equal(s.RawModValue(heir, i), s.ModValue(heir, i));
            var eternal = Card("EternalCampfire");
            Assert.Equal(15f, s.Param(eternal, "radius"), 4);
            var gas = Card("ToxicAdaptation");
            Assert.Equal(0.02f, s.Param(gas, "gasThreshold"), 4);   // gas immunity is all-or-nothing on every difficulty
            Assert.Equal(1.2f, s.Param(gas, "hit"), 4);             // and its poison-hit drawback never scales
        }

        [Fact]
        public void AmountsShrinkWaitsGrowAndRangesStay()
        {
            var s = At(0.6f);
            Assert.Equal(25f, s.Param(Card("SecondSpring"), "cooldown"), 3);          // 15 s -> 25 s
            float half = (float)Math.Sqrt(0.6);                                         // amount x cooldown share the 60%
            Assert.Equal(0.3f * half, s.Param(Card("SecondWind"), "restore"), 4);     // 30 -> 23.2
            Assert.Equal(300f / half, s.Param(Card("SecondWind"), "cooldown"), 1);    // 5 min -> 6.5 min
            Assert.False(Card("SecondWind").Params.ContainsKey("threshold"));         // 0.8.0: no low-stamina gate any more
            Assert.Equal(300f / half, s.Param(Card("BonePlating"), "interval"), 1);   // 5 min -> 6.5 min
            Assert.False(Card("MomentumFiend").Params.ContainsKey("cooldown"));       // 0.8.0: the stop-Drowsy penalty (and its spacing) is gone
            Assert.Equal(0.88f, s.Param(Card("DelayedConsequences"), "keep"), 4);     // saves 12% of the delayed share, not 20%
            Assert.Equal(8f, s.Param(Card("BelayBuddy"), "radius"), 4);
            Assert.False(Card("SecondSpring").Params.ContainsKey("drowsy"));          // 0.8.0: the double-jump Drowsy drawback is gone
        }

        [Fact]
        public void NumbersThatMultiplyIntoOneBenefitShareTheStrength()
        {
            // Endurance Badge: discount x boost length / build-up time; each number gets 0.6^(1/3), so the duty-cycle
            // math lands the whole card's value near, not exactly at, 60% (with the 3.5s boost: ~74%)
            var s = At(0.6f);
            var c = Card("PulseClimber");
            float discount = 1f - s.ModValue(c, Mod(c, Channel.climbStamina)), boost = s.Param(c, "boost"), buildup = s.Param(c, "buildup");
            float full = 0.4f * 3.5f / (2f + 3.5f), now = discount * boost / (buildup + boost);
            Assert.InRange(now / full, 0.6f, 0.76f);
            Assert.Equal(1.24f, s.ModValue(Card("LedgeDancer"), 0), 4);   // a single benefit still gets exactly 60%
        }

        [Fact]
        public void StrengthsAbove100AmplifyBenefits()
        {
            var s = At(1.5f);
            var daddy = Card("LedgeDancer");
            Assert.Equal(1.6f, s.ModValue(daddy, Mod(daddy, Channel.grabReach)), 4);        // +40% -> +60%
            Assert.Equal(1.05f, s.ModValue(daddy, Mod(daddy, Channel.staminaUsage)), 4);    // drawback still untouched
            var lungs = Card("DeepLungs");
            Assert.Equal(0.45f, s.ModValue(lungs, Mod(lungs, Channel.maxStamina)), 4);      // +30 -> +45
            Assert.Equal(10f, s.Param(Card("SecondSpring"), "cooldown"), 3);                // waits shrink: 15 s -> 10 s
            var heir = Card("MountainsHeir");
            for (int i = 0; i < heir.Mods.Length; i++) Assert.Equal(s.RawModValue(heir, i), s.ModValue(heir, i));   // Mythics never scale
            Assert.Equal("Grab walls from 60% farther away.", DifficultyScale.Benefit(daddy, s, daddy.Positive, out bool complete));
            Assert.True(complete);
        }

        [Fact]
        public void AmplificationNeverCrossesIntoFreeOrNegative()
        {
            // a cost multiplier amplified past "free" stops at 0, a Reduction param at 0: never a refund
            Assert.Equal(0f, DifficultyScale.Mod(0.2f, ModOp.mul, true, false, 3f));
            Assert.Equal(0f, DifficultyScale.Param("DelayedConsequences", "keep", 0.1f, 3f), 4);
            Assert.Equal(0.7f, DifficultyScale.Param("DelayedConsequences", "keep", 0.8f, 1.5f), 4);
        }

        [Fact]
        public void FullStrengthChangesNothing()
        {
            var s = At(1f);
            foreach (var c in CardTable.Cards)
            {
                for (int i = 0; i < c.Mods.Length; i++) Assert.Equal(s.RawModValue(c, i), s.ModValue(c, i));
                Assert.Equal(c.Positive, DifficultyScale.Benefit(c, s, c.Positive, out bool complete));
                Assert.True(complete);
            }
        }

        [Theory]
        [InlineData("LedgeDancer", "Grab walls from 24% farther away.")]
        [InlineData("SecondSpring", "Jump again in mid-air for a double jump. Cooldown: 25s.")]
        [InlineData("SecondWind", "Press {key} to recover 23.2 stamina. Cooldown: 6.5 min.")]
        [InlineData("BonePlating", "Every 6.5 min alive: a random 15.5s burst of Infinite Stamina, Invincibility or Low Gravity.")]
        [InlineData("DelayedConsequences", "Take 25% of a fall's Injury now and 66% after 20s: 9% less in total.")]
        [InlineData("BelayBuddy", "You and teammates within 8 m use 12% less climbing stamina.")]
        [InlineData("HeatHuddle", "Huddling warms you 45% more, from farther away, and you get cold 12% slower.")]
        [InlineData("OverhangArtist", "Hang still on a wall to recover 4.5 stamina per second.")]
        [InlineData("GeckoGrip", "Climbing uses 10.8% less stamina.")]
        public void CardTextShowsTheNumbersThisDifficultyUses(string key, string expected)
        {
            var c = Card(key);
            Assert.Equal(expected, DifficultyScale.Benefit(c, At(0.6f), c.Positive, out bool complete));
            Assert.True(complete);
        }

        [Fact]
        public void EveryScaledCardTextIsRewrittenOrFlagged()
        {
            // a card whose benefit scales but whose text has no number to rewrite gets the "Benefits at N%" note;
            // the 0.8.0 text pass deliberately sends the "full bar" and no-number cards down that note path
            var s = At(0.6f);
            var flagged = new List<string>();
            foreach (var c in CardTable.Cards.Where(c => c.Rarity != CardRarity.Mythic))
            {
                string text = DifficultyScale.Benefit(c, s, c.Positive, out bool complete);
                if (!complete) flagged.Add(c.Key);
                else Assert.DoesNotContain("NaN", text);
            }
            Assert.Equal(new[] { "QuietBreathing", "CleansingRitual", "HerosArrival", "CursedCrown", "BloodBattery" }, flagged);   // full-bar texts carry no number to rewrite
        }
    }
}
