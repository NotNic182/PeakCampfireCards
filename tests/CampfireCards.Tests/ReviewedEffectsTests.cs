using System;
using System.Linq;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using Xunit;

namespace CampfireCards.Tests
{
    [Collection("Pick sessions")]
    public class ManualRecoveryTests
    {
        public ManualRecoveryTests()
        {
            CardSettings.Current = new CardSettings();
            LocalCards.Biome = 0;
            UnityEngine.Time.time = 0f;
        }

        private static Ability Recovery(string key) => AbilityRegistry.Create(CardTable.Cards.Single(c => c.Key == key));

        [Fact]
        public void NicOfTimeWaitsForInputAndHasAThreeMinuteCooldownAcrossBiomes()
        {
            var c = new Character(); c.data.currentStamina = .1f;
            var a = Recovery("SecondWind");
            Assert.True(a.IsManual);
            a.Tick(c, 60f);
            Assert.Equal(.1f, c.data.currentStamina);
            Assert.True(a.Activate(c));
            Assert.Equal(.4f, c.data.currentStamina, 5);
            c.data.currentStamina = .1f;
            LocalCards.Biome = 1;
            a.OnBiomeStarted(c, 1);
            UnityEngine.Time.time = 179.9f;
            Assert.False(a.Activate(c));
            UnityEngine.Time.time = 180f;
            Assert.True(a.Activate(c));
        }

        [Fact]
        public void FailedNicOfTimeAttemptsDoNotStartCooldownAndRecoveryClamps()
        {
            var c = new Character(); c.data.currentStamina = .2f;
            var a = Recovery("SecondWind");
            Assert.False(a.Activate(c)); // below 20, not 20 or below
            c.data.currentStamina = .19f; c.MaxStamina = .19f;
            Assert.False(a.Activate(c)); // afflictions have left no recoverable space
            c.MaxStamina = .3f;
            Assert.True(a.Activate(c));
            Assert.Equal(.3f, c.data.currentStamina);
        }

        [Theory]
        [InlineData("SecondWind")]
        [InlineData("EmergencyPurge")]
        public void GhostsAndPassedOutScoutsCannotActivateRecovery(string key)
        {
            var c = new Character(); c.data.currentStamina = .1f;
            c.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .5f;
            var a = Recovery(key);
            c.data.dead = true; Assert.False(a.Activate(c));
            c.data.dead = false; c.data.passedOut = true; Assert.False(a.Activate(c));
            c.data.passedOut = false; c.data.fullyPassedOut = true; Assert.False(a.Activate(c));
            c.data.fullyPassedOut = false; Assert.True(a.Activate(c));
        }

        [Fact]
        public void CureAllIsManualAndCleansOnlyTheWorstEligibleStatusOncePerBiome()
        {
            var c = new Character();
            c.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .35f;
            c.Statuses[CharacterAfflictions.STATUSTYPE.Spores] = .5f;
            c.Statuses[CharacterAfflictions.STATUSTYPE.Drowsy] = .4f;
            var a = Recovery("EmergencyPurge");
            a.Tick(c, 10f);
            Assert.Equal(.5f, CardStatus.Get(c, CharacterAfflictions.STATUSTYPE.Spores));
            Assert.True(a.Activate(c));
            Assert.Equal(.25f, CardStatus.Get(c, CharacterAfflictions.STATUSTYPE.Spores), 5);
            Assert.Equal(.35f, CardStatus.Get(c, CharacterAfflictions.STATUSTYPE.Poison));
            Assert.False(a.Activate(c));
            LocalCards.Biome++;
            Assert.True(a.Activate(c));
            Assert.Equal(.15f, CardStatus.Get(c, CharacterAfflictions.STATUSTYPE.Drowsy), 5);
        }

        [Fact]
        public void CureAllFailedAttemptPreservesChargeAndAcceptsExactly25()
        {
            var c = new Character(); var a = Recovery("EmergencyPurge");
            c.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .24f;
            Assert.False(a.Activate(c));
            c.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .25f;
            Assert.True(a.Activate(c));
            Assert.Equal(0f, CardStatus.Get(c, CharacterAfflictions.STATUSTYPE.Poison), 5);
        }
    }

    [Collection("Pick sessions")]
    public class ReviewedEffectsTests
    {
        [Fact]
        public void RandomPenaltySurvivesReplayAndStateRestoreWithoutRerolling()
        {
            var ledger = new PenaltyLedger();
            Assert.False(ledger.TrySet(2, 0, 0)); Assert.False(ledger.TrySet(2, 0, 5));
            Assert.True(ledger.TrySet(2, 0, 3));
            Assert.False(ledger.TrySet(2, 0, 1));    // replayed slot 0 cannot reroll
            Assert.False(ledger.TrySet(2, 2, 1));    // a skipped slot is ignored
            var snapshot = StateSync.Read(new StateSync
            {
                actors = new[] { 2 }, counts = new byte[] { 1 }, cards = new byte[] { 40 },
                pickFlags = new byte[] { 4 }, penaltyCounts = new byte[] { 1 }, penaltyVariants = ledger.Of(2).ToArray(), sessionId = 5
            }.Write());
            var restored = new PenaltyLedger(); restored.Restore(snapshot.actors[0], snapshot.penaltyVariants.Take(snapshot.penaltyCounts[0]));
            Assert.Equal(new byte[] { 3 }, restored.Of(2)); Assert.Equal(4, snapshot.pickFlags[0]);
            Assert.False(restored.TrySet(2, 0, 4));
            Assert.Equal(Channel.allInjury, CardPenaltyRules.ChannelFor(restored.Of(2)[0]));
            restored.Reset(); Assert.Empty(restored.Of(2));
        }

        [Fact]
        public void EachClownLuggageAcquisitionKeepsItsOwnPermanentPenalty()
        {
            var ledger = new PenaltyLedger();
            Assert.True(ledger.TrySet(7, 0, 1));
            // the card is sacrificed and later picked again: the old penalty stays and the new pick adds a second one
            Assert.True(ledger.TrySet(7, ledger.Count(7), 4));
            Assert.Equal(new byte[] { 1, 4 }, ledger.Of(7));
            Assert.False(ledger.TrySet(7, 1, 2));    // replay of the second message
            Assert.Equal(2, ledger.Count(7));
        }

        [Fact]
        public void NewPenaltyAndRewardMessagesRoundTripAndDispatch()
        {
            var penalty = new CardPenalty { actor = 2, slot = 1, variant = 4 };
            var reward = new CardRewardUsed { actor = 2 };
            Assert.Equal(4, CardPenalty.Read(penalty.Write()).variant);
            Assert.Equal(1, CardPenalty.Read(penalty.Write()).slot);
            Assert.Equal(2, CardRewardUsed.Read(reward.Write()).actor);
            NetHandlers.Last.Clear();
            Assert.True(NetDispatcher.Dispatch(108, penalty.Write(), 2));
            Assert.True(NetDispatcher.Dispatch(109, reward.Write(), 1));
            Assert.Equal(2, NetHandlers.Last.Count);
        }

        [Fact]
        public void MushroomClockPausesForGhostsAndKeepsAPendingPower()
        {
            var clock = new PeriodicPowerClock();
            clock.Advance(590f, true); clock.Advance(600f, false);
            Assert.False(clock.Ready(600f));
            clock.Advance(10f, true); Assert.True(clock.Ready(600f));
            clock.Advance(50f, true); Assert.True(clock.Ready(600f));
            clock.Consume(); Assert.False(clock.Ready(600f));
            Assert.Equal(600f, clock.Remaining(600f));
        }

        [Fact]
        public void WeightDiscountIncludesScoutButBackpackPenaltyOnlyChangesContents()
        {
            var s = new CardSettings(); var r = new ModResult();
            var defs = CardTable.Cards.Where(c => c.Key == "PackRat" || c.Key == "Ultralight");
            r.Fold(defs.SelectMany(c => c.Mods.Select((m, i) => new ActiveMod
            { CardId = c.Id, ModIndex = i, Channel = m.Channel, Op = m.Op, Value = m.Value, Rarity = c.Rarity })), s);
            // Four ordinary weight units, two held (including a bag shell), ten inside the bag, eight scout, one stuck item.
            Assert.Equal((4f + 2f * .55f + 10f * 1.05f + 8f + 1f) * .5f,
                StatComposites.CarriedWeight(r, 4f, 2f, 10f, 8f, 1f), 5);
        }

        [Fact]
        public void WeightCardsNeverShrinkNegativeWeightLift()
        {
            var r = new ModResult();
            r.Fold(new[] { new ActiveMod { CardId = 33, ModIndex = 0, Channel = Channel.totalWeight, Op = ModOp.mul, Value = .5f, Rarity = CardRarity.Rare } }, new CardSettings());
            // 20 units of gear halved, a Balloon Bunch's -15 lift kept whole
            Assert.Equal(20f * .5f - 15f, StatComposites.CarriedWeight(r, 20f, 0f, 0f, 0f, 0f, -15f), 5);
            Assert.Equal(10f, StatComposites.CarriedWeight(r, 20f, 0f, 0f, 0f, 0f), 5);
        }

        [Fact]
        public void PhoenixClauseJudgesTheWholeDeferredFall()
        {
            var q = new DelayedInjuryQueue { DeferShare = .75f, Keep = .8f, Delay = 20f };
            // 25% now + 75% x 0.8 later = 85% of the fall in total
            Assert.Equal(.85f, q.TotalOf(1f), 5);
            Assert.Equal(0, q.Pending);   // judging a fall queues nothing
        }

        [Theory]
        [InlineData(.2f, .3f, 1.15f, .315f)]
        [InlineData(.2f, .2f, 1.15f, .2f)]
        [InlineData(.2f, 0f, 1.15f, 0f)]
        public void CarryPenaltyScalesDeathProgressWithoutScalingItsExistingValue(float before, float after, float multiplier, float expected) =>
            Assert.Equal(expected, ReviewedEffectMath.CarriedDeathTimer(before, after, multiplier), 5);

        [Fact]
        public void CarryPenaltyConsumesFifteenPercentNormalTimeDuringVanillaPause()
        {
            float nativeNormalTick = 1f / 60f;
            Assert.Equal(.2f + .15f / 60f, ReviewedEffectMath.CarriedDeathTimer(.2f, .2f, 1.15f, nativeNormalTick), 5);
            Assert.Equal(.2f, ReviewedEffectMath.CarriedDeathTimer(.2f, .2f, 1f, nativeNormalTick));
            Assert.Equal(0f, ReviewedEffectMath.CarriedDeathTimer(.2f, 0f, 1.15f, nativeNormalTick));
        }

        [Fact]
        public void ReviveCurseProtectionPreservesExistingCurseAndNativeClears()
        {
            Assert.Equal(.2f, ReviewedEffectMath.RevivalCurse(.2f, .5f, 0f));
            Assert.Equal(.1f, ReviewedEffectMath.RevivalCurse(.2f, .1f, 0f));
            var card = CardTable.Cards.Single(c => c.Key == "CurseBargain");
            Assert.DoesNotContain(card.Mods, m => m.Channel == Channel.allInjury);   // the rebalance removed its Injury tax
        }
    }
}
