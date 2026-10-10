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
        public void NicOfTimeWaitsForInputAndHasAFiveMinuteCooldownAcrossBiomes()
        {
            var c = new Character(); c.data.currentStamina = .1f;
            var a = Recovery("SecondWind");
            Assert.True(a.IsManual);
            a.Tick(c, 60f);
            Assert.Equal(.1f, c.data.currentStamina);
            Assert.True(a.Activate(c));
            Assert.Equal(.35f, c.data.currentStamina, 5);
            c.data.currentStamina = .1f;
            LocalCards.Biome = 1;
            a.OnBiomeStarted(c, 1);
            UnityEngine.Time.time = 299.9f;
            Assert.False(a.Activate(c));
            UnityEngine.Time.time = 300f;
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
            Assert.Equal(.3f, CardStatus.Get(c, CharacterAfflictions.STATUSTYPE.Spores), 5);
            Assert.Equal(.35f, CardStatus.Get(c, CharacterAfflictions.STATUSTYPE.Poison));
            Assert.False(a.Activate(c));
            LocalCards.Biome++;
            Assert.True(a.Activate(c));
            Assert.Equal(.2f, CardStatus.Get(c, CharacterAfflictions.STATUSTYPE.Drowsy), 5);
        }

        [Fact]
        public void CureAllFailedAttemptPreservesChargeAndAcceptsExactly35()
        {
            var c = new Character(); var a = Recovery("EmergencyPurge");
            c.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .34f;
            Assert.False(a.Activate(c));
            c.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .35f;
            Assert.True(a.Activate(c));
            Assert.Equal(.15f, CardStatus.Get(c, CharacterAfflictions.STATUSTYPE.Poison), 5);
        }
    }

    [Collection("Pick sessions")]
    public class ReviewedEffectsTests
    {
        [Fact]
        public void RandomPenaltySurvivesReplayAndStateRestoreWithoutRerolling()
        {
            var ledger = new PenaltyLedger();
            Assert.False(ledger.TrySet(2, 0)); Assert.False(ledger.TrySet(2, 5));
            Assert.True(ledger.TrySet(2, 3));
            Assert.False(ledger.TrySet(2, 1));
            var snapshot = StateSync.Read(new StateSync
            {
                actors = new[] { 2 }, counts = new byte[] { 1 }, cards = new byte[] { 40 },
                pickFlags = new byte[] { 4 }, penaltyVariants = new[] { ledger.Of(2) }, sessionId = 5
            }.Write());
            var restored = new PenaltyLedger(); restored.Restore(snapshot.actors[0], snapshot.penaltyVariants[0]);
            Assert.Equal(3, restored.Of(2)); Assert.Equal(4, snapshot.pickFlags[0]);
            Assert.False(restored.TrySet(2, 4));
            Assert.Equal(Channel.allInjury, CardPenaltyRules.ChannelFor(restored.Of(2)));
            restored.Reset(); Assert.Equal(0, restored.Of(2));
        }

        [Fact]
        public void NewPenaltyAndRewardMessagesRoundTripAndDispatch()
        {
            var penalty = new CardPenalty { actor = 2, variant = 4 };
            var reward = new CardRewardUsed { actor = 2 };
            Assert.Equal(4, CardPenalty.Read(penalty.Write()).variant);
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
            Assert.Equal((4f + 2f * .75f + 10f * 1.1f + 8f + 1f) * .5f,
                StatComposites.CarriedWeight(r, 4f, 2f, 10f, 8f, 1f), 5);
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
            Assert.Contains(card.Mods, m => m.Channel == Channel.allInjury && m.Value == 1.25f);
        }
    }
}
