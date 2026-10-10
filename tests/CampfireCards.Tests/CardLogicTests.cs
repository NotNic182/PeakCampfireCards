using System;
using System.Collections.Generic;
using System.Linq;
using CampfireCards.Core;
using CampfireCards.Generated;
using Xunit;

namespace CampfireCards.Tests
{
    public class RegistryTests
    {
        [Fact]
        public void Has64CardsWithUniqueIdsAndKeys()
        {
            Assert.Equal(64, CardTable.Cards.Length);
            Assert.Equal(64, CardTable.Cards.Select(c => c.Id).Distinct().Count());
            Assert.Equal(64, CardTable.Cards.Select(c => c.Key).Distinct().Count());
            Assert.Equal(Enumerable.Range(1, 64).Select(i => (byte)i), CardTable.Cards.Select(c => c.Id).OrderBy(i => i));
        }

        [Theory]
        [InlineData(CardRarity.Common, 15)]
        [InlineData(CardRarity.Uncommon, 24)]
        [InlineData(CardRarity.Rare, 16)]
        [InlineData(CardRarity.Legendary, 7)]
        [InlineData(CardRarity.Mythic, 2)]
        public void RarityCounts(CardRarity r, int count) => Assert.Equal(count, CardTable.Cards.Count(c => c.Rarity == r));

        [Fact]
        public void MythicsHaveNoDrawbacks()
        {
            foreach (var c in CardTable.Cards.Where(c => c.Rarity == CardRarity.Mythic))
            {
                Assert.True(string.IsNullOrEmpty(c.Negative), c.Name);
                foreach (var m in c.Mods)
                {
                    // every Mythic modifier must help: costs and injury down, speed and stamina up
                    bool harmful = (m.Op == ModOp.mul && m.Value > 1f && (m.Channel == Channel.climbStamina || m.Channel == Channel.allInjury || m.Channel == Channel.fallInjury || m.Channel == Channel.negativeStatusRate))
                                   || (m.Op == ModOp.mul && m.Value < 1f && m.Channel == Channel.sprintSpeed)
                                   || (m.Op == ModOp.add && m.Value < 0f && m.Channel == Channel.maxStamina);
                    Assert.False(harmful, c.Name + " " + m.Channel);
                }
            }
        }

        [Fact]
        public void EveryNormalCardHasBenefitAndDrawback()
        {
            foreach (var c in CardTable.Cards.Where(c => c.Rarity != CardRarity.Mythic))
            {
                Assert.False(string.IsNullOrEmpty(c.Positive), c.Name);
                Assert.False(string.IsNullOrEmpty(c.Negative), c.Name);
                Assert.True(c.Mods.Length > 0 || !string.IsNullOrEmpty(c.Ability), c.Name + " has no effect");
            }
        }

        [Fact]
        public void TeamCategoryIsMultiplayerOnly()
        {
            var team = CardTable.Cards.Where(c => c.Category == "team").ToList();
            Assert.Equal(8, team.Count);
            Assert.All(team, c => Assert.True(c.MultiplayerOnly));
            Assert.Equal(8, CardTable.Cards.Count(c => c.MultiplayerOnly));
        }

        [Fact]
        public void DefaultRarityWeightsMatchSpec()
        {
            Assert.Equal(new[] { 48f, 32f, 16.5f, 3f, 0.5f }, CardTable.RarityWeights);
            Assert.Equal(3, CardTable.OfferCount);
        }
    }

    public class DrawTests
    {
        private static readonly CardSettings S = new CardSettings();

        [Fact]
        public void OfferIsDistinctAndHasRequestedCount()
        {
            var rng = new Random(1);
            for (int i = 0; i < 2000; i++)
            {
                var hand = CardDraw.Offer(rng, S, new HashSet<byte>(), OfferRules.Campfire(3, false, false));
                Assert.Equal(3, hand.Count);
                Assert.Equal(3, hand.Distinct().Count());
            }
        }

        [Fact]
        public void SoloNeverOffersMultiplayerCards()
        {
            var rng = new Random(2);
            for (int i = 0; i < 5000; i++)
                foreach (var id in CardDraw.Offer(rng, S, null, OfferRules.Campfire(3, true, true)))
                    Assert.False(S.Card(id).MultiplayerOnly);
        }

        [Fact]
        public void OwnedCardsAreNeverOfferedAgain()
        {
            var rng = new Random(3);
            var owned = new HashSet<byte>(CardTable.Cards.Where(c => c.Rarity == CardRarity.Common).Select(c => c.Id));
            for (int i = 0; i < 3000; i++)
                foreach (var id in CardDraw.Offer(rng, S, owned, OfferRules.Campfire(3, false, true)))
                    Assert.DoesNotContain(id, owned);
        }

        [Fact]
        public void BonusDrawsNeverIncludeDrawCards()
        {
            var rng = new Random(4);
            var rules = OfferRules.Campfire(2, false, true); rules.ExcludeDrawCards = true;
            for (int i = 0; i < 5000; i++)
                foreach (var id in CardDraw.Offer(rng, S, null, rules))
                    Assert.False(S.Card(id).DrawsCards);
        }

        [Fact]
        public void DoubleDownBonusIsCommonToRare()
        {
            var rng = new Random(5);
            var rules = OfferRules.Campfire(1, false, true); rules.ExcludeDrawCards = true; rules.MaxRarity = CardRarity.Rare;
            for (int i = 0; i < 5000; i++)
                Assert.True(S.Card(CardDraw.Offer(rng, S, null, rules)[0]).Rarity <= CardRarity.Rare);
        }

        [Fact]
        public void ClaimedMythicsAreNeverOfferedAgain()
        {
            var rng = new Random(11);
            var mythics = CardTable.Cards.Where(c => c.Rarity == CardRarity.Mythic).Select(c => c.Id).ToList();
            var weights = new CardSettings();
            weights.RarityWeights[(int)CardRarity.Mythic] = 1000f;   // make Mythics the likely roll
            var rules = OfferRules.Campfire(3, false, true);
            rules.ClaimedMythics = new HashSet<byte> { mythics[0] };
            bool sawOther = false;
            for (int i = 0; i < 2000; i++)
            {
                var hand = CardDraw.Offer(rng, weights, null, rules);
                Assert.DoesNotContain(mythics[0], hand);
                sawOther |= hand.Contains(mythics[1]);
            }
            Assert.True(sawOther);
        }

        [Fact]
        public void CampfireSpawnCardsPointAtRealItems()
        {
            Assert.Equal(new[] { "RopeSpool", "Bandages" }, CardTable.SpawnItems);
            foreach (var c in CardTable.Cards.Where(c => c.Ability == "CampfireSpawn"))
            {
                int item = (int)Math.Round(c.Param("item", -1f));
                Assert.InRange(item, 0, CardTable.SpawnItems.Length - 1);
            }
        }

        [Fact]
        public void BargainNeedsASacrificeTarget()
        {
            var rng = new Random(6);
            byte bargain = CardTable.Cards.First(c => c.Ability == "MountainsBargain").Id;
            for (int i = 0; i < 20000; i++)
                Assert.DoesNotContain(bargain, CardDraw.Offer(rng, S, null, OfferRules.Campfire(3, false, false)));
            Assert.Empty(CardDraw.SacrificeTargets(S, new byte[] { 63, 64, bargain }));
            Assert.Single(CardDraw.SacrificeTargets(S, new byte[] { 1, 63 }));
        }

        [Fact]
        public void MythicsCanBeDisabled()
        {
            var s = new CardSettings { MythicsEnabled = false };
            var rng = new Random(7);
            for (int i = 0; i < 20000; i++)
                foreach (var id in CardDraw.Offer(rng, s, null, OfferRules.Campfire(3, false, true)))
                    Assert.NotEqual(CardRarity.Mythic, s.Card(id).Rarity);
        }

        [Fact]
        public void DisabledCardsAreNeverOffered()
        {
            var s = new CardSettings();
            s.Enabled[0] = false;
            var rng = new Random(8);
            for (int i = 0; i < 5000; i++)
                Assert.DoesNotContain(CardTable.Cards[0].Id, CardDraw.Offer(rng, s, null, OfferRules.Campfire(3, false, true)));
        }

        [Fact]
        public void RarityRollFollowsWeights()
        {
            var rng = new Random(9);
            var all = new List<CardRarity> { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare, CardRarity.Legendary, CardRarity.Mythic };
            var counts = new int[5];
            const int n = 400000;
            for (int i = 0; i < n; i++) counts[(int)CardDraw.RollRarity(rng, S, all)]++;
            Assert.InRange(counts[0] / (double)n, 0.475, 0.485);
            Assert.InRange(counts[1] / (double)n, 0.315, 0.325);
            Assert.InRange(counts[2] / (double)n, 0.160, 0.170);
            Assert.InRange(counts[3] / (double)n, 0.027, 0.033);
            Assert.InRange(counts[4] / (double)n, 0.004, 0.006);
        }

        [Fact]
        public void SmallPoolStillFillsWhatItCan()
        {
            var s = new CardSettings();
            for (int i = 0; i < s.Enabled.Length; i++) s.Enabled[i] = i < 2;
            var hand = CardDraw.Offer(new Random(10), s, null, OfferRules.Campfire(3, false, true));
            Assert.Equal(2, hand.Count);
        }
    }

    public class ModStackTests
    {
        private static ActiveMod M(Channel c, ModOp op, float v, CardRarity r = CardRarity.Common, byte card = 1) =>
            new ActiveMod { Channel = c, Op = op, Value = v, Rarity = r, CardId = card };

        [Fact]
        public void PercentagesStackMultiplicatively()
        {
            var r = new ModResult();
            r.Fold(new[] { M(Channel.sprintSpeed, ModOp.mul, 1.2f), M(Channel.sprintSpeed, ModOp.mul, 0.9f) }, new CardSettings());
            Assert.Equal(1.08f, r[Channel.sprintSpeed], 4);
        }

        [Fact]
        public void StaminaCostReductionIsCappedAt60Percent()
        {
            var r = new ModResult();
            r.Fold(new[] { M(Channel.climbStamina, ModOp.mul, 0.5f), M(Channel.climbStamina, ModOp.mul, 0.5f) }, new CardSettings());
            Assert.Equal(0.4f, r[Channel.climbStamina], 4);
        }

        [Fact]
        public void MythicZeroClimbCostOverridesEverything()
        {
            var r = new ModResult();
            r.Fold(new[] { M(Channel.climbStamina, ModOp.mul, 1.2f), M(Channel.climbStamina, ModOp.set, 0f, CardRarity.Mythic) }, new CardSettings());
            Assert.Equal(0f, r[Channel.climbStamina]);
        }

        [Fact]
        public void MaxStaminaFlatsAddAndRespectTheFloor()
        {
            var r = new ModResult();
            r.Fold(new[] { M(Channel.maxStamina, ModOp.add, 0.2f), M(Channel.maxStamina, ModOp.add, -0.1f) }, new CardSettings());
            Assert.Equal(0.1f, r[Channel.maxStamina], 4);
            r.Fold(new[] { M(Channel.maxStamina, ModOp.add, -0.2f), M(Channel.maxStamina, ModOp.add, -0.2f), M(Channel.maxStamina, ModOp.add, -0.2f) }, new CardSettings());
            Assert.Equal(-0.5f, r[Channel.maxStamina], 4);
        }

        [Fact]
        public void MythicMaxStaminaGoesBeyondTheFloor()
        {
            var r = new ModResult();
            r.Fold(new[] { M(Channel.maxStamina, ModOp.add, -0.6f), M(Channel.maxStamina, ModOp.add, 0.35f, CardRarity.Mythic) }, new CardSettings());
            Assert.Equal(-0.15f, r[Channel.maxStamina], 4);
        }

        [Fact]
        public void RefoldingDoesNotDrift()
        {
            var r = new ModResult();
            var mods = new[] { M(Channel.jumpHeight, ModOp.mul, 1.25f), M(Channel.maxStamina, ModOp.add, 0.2f) };
            for (int i = 0; i < 1000; i++) r.Fold(mods, new CardSettings());
            Assert.Equal(1.25f, r[Channel.jumpHeight], 5);
            Assert.Equal(0.2f, r[Channel.maxStamina], 5);
            r.Fold(new ActiveMod[0], new CardSettings());
            Assert.Equal(1f, r[Channel.jumpHeight]);
            Assert.Equal(0f, r[Channel.maxStamina]);
        }
    }

    public class TimerTests
    {
        [Fact]
        public void DelayedConsequencesMatchesSpecExample()
        {
            var q = new DelayedInjuryQueue();
            Assert.Equal(25f, q.Add(100f, 0f), 3);
            Assert.Equal(0f, q.Tick(19.9f));
            Assert.Equal(52.5f, q.Tick(20f), 3);
            Assert.Equal(0, q.Pending);
        }

        [Fact]
        public void DelayedFallsKeepSeparateTimers()
        {
            var q = new DelayedInjuryQueue();
            q.Add(100f, 0f);
            q.Add(40f, 10f);
            Assert.Equal(52.5f, q.Tick(20f), 3);
            Assert.Equal(21f, q.Tick(30f), 3);
        }

        [Fact]
        public void SecondWindFiresOnceBelowThreshold()
        {
            var l = new BelowLatch();
            Assert.False(l.Update(0.5f, 0.2f));
            Assert.True(l.Update(0.15f, 0.2f));
            Assert.False(l.Update(0.10f, 0.2f));
            Assert.False(l.Update(0.25f, 0.2f));
            Assert.True(l.Update(0.19f, 0.2f));
        }

        [Fact]
        public void PerBiomeChargeRefillsOnNewBiomeOnly()
        {
            var c = new Charge(perBiome: true);
            Assert.True(c.TryUse(0));
            Assert.False(c.TryUse(0));
            Assert.True(c.TryUse(1));
            var run = new Charge(perBiome: false);
            Assert.True(run.TryUse(0));
            Assert.False(run.TryUse(1));
        }

        [Fact]
        public void CooldownBlocksUntilElapsed()
        {
            var cd = new Cooldown(45f);
            Assert.True(cd.Ready(0f));
            cd.Trigger(0f);
            Assert.False(cd.Ready(44.9f));
            Assert.True(cd.Ready(45f));
        }

        [Fact]
        public void MomentumBuildsResetsAndRateLimitsThePenalty()
        {
            var m = new MomentumMachine();
            Assert.False(m.Update(1.5f, true, 1.5f)); Assert.False(m.Active);
            Assert.False(m.Update(0.6f, true, 2.1f)); Assert.True(m.Active);
            Assert.True(m.Update(0.1f, false, 2.2f)); Assert.False(m.Active);
            m.Update(2.5f, true, 4.7f);
            Assert.False(m.Update(0.1f, false, 4.8f)); // within 30 s of the last penalty
            m.Update(1.0f, true, 40f);
            Assert.False(m.Active);                       // buildup restarted
        }

        [Fact]
        public void PulseClimberCycles()
        {
            var p = new PulseMachine();
            p.Update(2.9f, true); Assert.False(p.Active);
            p.Update(0.2f, true); Assert.True(p.Active);
            p.Update(1.0f, true); Assert.False(p.Active);
            p.Update(2.0f, true); Assert.False(p.Active);
            p.Update(0.5f, false); p.Update(2.9f, true); Assert.False(p.Active); // stopping resets the buildup
        }

        [Fact]
        public void ReviveArbiterGrantsOncePerOwnerPerBiomeAndPerEvent()
        {
            var a = new ReviveArbiter();
            Assert.True(a.TryGrant(ownerActor: 1, biome: 0, victimActor: 2, incapEvent: 7));
            Assert.False(a.TryGrant(ownerActor: 3, biome: 0, victimActor: 2, incapEvent: 7)); // overlapping aura, same event
            Assert.False(a.TryGrant(ownerActor: 1, biome: 0, victimActor: 4, incapEvent: 1)); // owner already used this biome
            Assert.True(a.TryGrant(ownerActor: 1, biome: 1, victimActor: 4, incapEvent: 1));
        }

        [Fact]
        public void LoadedDiceIsDeterministicAndNearSixtyPercent()
        {
            Assert.Equal(Rolls.LoadedDice(5, 2, 1, 0.6f), Rolls.LoadedDice(5, 2, 1, 0.6f));
            int wins = 0;
            for (int i = 0; i < 20000; i++) if (Rolls.LoadedDice(i, i % 7, i % 4, 0.6f)) wins++;
            Assert.InRange(wins / 20000.0, 0.57, 0.63);
        }
    }
}
