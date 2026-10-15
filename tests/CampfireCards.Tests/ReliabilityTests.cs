using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using Xunit;

namespace CampfireCards.Tests
{
    /// <summary>Regression tests for the v0.4.0 reliability pass: pick validation, the session queue, revivals, item rewards,
    /// Lucky Lunch, Thorns, the settings fingerprint, the build summary and the hardened wire format.</summary>
    public class PickValidationTests
    {
        private const int Session = 10001;
        private static readonly byte[] HandA = { 1, 2, 3 }, HandB = { 4, 5, 6 }, HandC = { 7, 8, 9 };

        private static PickTurns Start(PickMode mode, params int[] order)
        {
            var t = new PickTurns();
            var hands = new List<byte[]> { HandA, HandB, HandC, new byte[0] };
            t.Start(Session, order, hands.Take(order.Length).ToList(), mode);
            return t;
        }

        [Fact]
        public void RegularPickIsAcceptedOnceAndCompletesTheSession()
        {
            var t = Start(PickMode.Turns, 1);
            Assert.Equal(PickVerdict.Ok, t.CheckRequest(Session, 1, 2));
            t.Claim(1);
            Assert.Equal(PickVerdict.Ok, t.CheckPick(Session, 1, 2));   // the host's own accepted pick comes back
            t.Apply(1);
            Assert.True(t.AllDone);
        }

        [Fact]
        public void NoSessionAndStaleSessionAreRefused()
        {
            var t = new PickTurns();
            Assert.Equal(PickVerdict.NoSession, t.CheckRequest(Session, 1, 1));
            Assert.Equal(PickVerdict.NoSession, t.CheckPick(Session, 1, 1));
            t = Start(PickMode.Simultaneous, 1, 2);
            Assert.Equal(PickVerdict.StaleSession, t.CheckRequest(Session - 1, 1, 1));
            Assert.Equal(PickVerdict.StaleSession, t.CheckPick(Session + 1, 1, 1));
        }

        [Fact]
        public void OutOfTurnPickIsRefusedInTurnsMode()
        {
            var t = Start(PickMode.Turns, 1, 2);
            Assert.Equal(1, t.CurrentActor);
            Assert.Equal(PickVerdict.NotYourTurn, t.CheckRequest(Session, 2, 4));
            Assert.Equal(PickVerdict.NotYourTurn, t.CheckPick(Session, 2, 4));
            t.Claim(1); t.Apply(1);
            Assert.Equal(2, t.CurrentActor);
            Assert.Equal(PickVerdict.Ok, t.CheckRequest(Session, 2, 4));
        }

        [Fact]
        public void CardsOutsideTheOwnHandAreRefused()
        {
            var t = Start(PickMode.Simultaneous, 1, 2);
            Assert.Equal(PickVerdict.NotInHand, t.CheckRequest(Session, 1, 4));    // player 2's card
            Assert.Equal(PickVerdict.NotInHand, t.CheckRequest(Session, 1, 0));    // padding id
            Assert.Equal(PickVerdict.NotInHand, t.CheckRequest(Session, 1, 200));  // unknown id
            Assert.Equal(PickVerdict.NotInSession, t.CheckRequest(Session, 99, 1)); // not dealt in
        }

        [Fact]
        public void ReplayedOrDuplicatedPicksGrantNothingMore()
        {
            var t = Start(PickMode.Simultaneous, 1, 2);
            Assert.Equal(PickVerdict.Ok, t.CheckRequest(Session, 1, 1));
            t.Claim(1);
            Assert.Equal(PickVerdict.AlreadyClaimed, t.CheckRequest(Session, 1, 2));   // second click before the echo
            t.Apply(1);
            Assert.Equal(PickVerdict.AlreadyPicked, t.CheckRequest(Session, 1, 2));
            Assert.Equal(PickVerdict.AlreadyPicked, t.CheckPick(Session, 1, 1));        // the same PickMade replayed
            Assert.False(t.AllDone);
        }

        [Fact]
        public void TimeoutPickWinsOverALateRequest()
        {
            var t = Start(PickMode.Turns, 1, 2);
            t.Claim(1);   // host timeout pick for player 1
            Assert.Equal(PickVerdict.AlreadyClaimed, t.CheckRequest(Session, 1, 3));
            Assert.Equal(PickVerdict.Ok, t.CheckPick(Session, 1, 2));   // the host's auto pick still applies
            t.Apply(1);
            Assert.Equal(2, t.CurrentActor);
        }

        [Fact]
        public void SimultaneousPlayersChooseIndependently()
        {
            var t = Start(PickMode.Simultaneous, 3, 1, 2);
            Assert.True(t.CanChoose(1) && t.CanChoose(2) && t.CanChoose(3));
            Assert.Equal(int.MinValue, t.CurrentActor);
            foreach (var (actor, card) in new[] { (2, (byte)8), (3, (byte)2), (1, (byte)5) })   // any order
            {
                Assert.Equal(PickVerdict.Ok, t.CheckRequest(Session, actor, card));
                t.Claim(actor); t.Apply(actor);
            }
            Assert.True(t.AllDone);
            Assert.Equal(new[] { 3, 1, 2 }, t.Order);   // arrival order kept for display and dealing
        }

        [Fact]
        public void PlayersWithoutCardsAreNeverWaitedFor()
        {
            var t = Start(PickMode.Turns, 1, 2, 3, 4);   // player 4 skips (Phoenix Clause)
            Assert.False(t.IsPending(4));
            foreach (var a in new[] { 1, 2, 3 }) { Assert.Equal(a, t.CurrentActor); t.Claim(a); t.Apply(a); }
            Assert.True(t.AllDone);
        }

        [Fact]
        public void LeavingPlayerNoLongerHoldsUpTheSession()
        {
            var t = Start(PickMode.Turns, 1, 2);
            Assert.True(t.RemoveActor(1));
            Assert.Equal(2, t.CurrentActor);
            Assert.Equal(PickVerdict.NotInSession, t.CheckRequest(Session, 1, 1));
            Assert.True(t.RemoveActor(2));
            Assert.True(t.AllDone);
            Assert.False(t.RemoveActor(5));
        }

        [Fact]
        public void MalformedOrderNeverListsAPlayerTwice()
        {
            var t = new PickTurns();
            t.Start(Session, new[] { 1, 1, 2 }, new List<byte[]> { HandA, HandB, HandC }, PickMode.Turns);
            Assert.Equal(new[] { 1, 2 }, t.Order);
            Assert.Equal(HandA, t.HandOf(1));
        }

        [Fact]
        public void NewSessionForgetsTheOldOne()
        {
            var t = Start(PickMode.Simultaneous, 1);
            t.Claim(1); t.Apply(1);
            t.Start(Session + 1, new[] { 1 }, new List<byte[]> { HandB }, PickMode.Simultaneous);
            Assert.Equal(PickVerdict.StaleSession, t.CheckPick(Session, 1, 1));
            Assert.Equal(PickVerdict.Ok, t.CheckRequest(Session + 1, 1, 4));
        }

        [Fact]
        public void MythicsStillOfferedArePassedToBonusDeals()
        {
            var t = Start(PickMode.Simultaneous, 1, 2);
            Assert.Contains((byte)4, t.CardsStillOffered());
            t.Claim(2); t.Apply(2);
            Assert.DoesNotContain((byte)4, t.CardsStillOffered());   // player 2 already chose: their other cards are free
            Assert.Contains((byte)1, t.CardsStillOffered());
        }
    }

    public class SessionQueueTests
    {
        [Fact]
        public void RapidCampfiresAreQueuedInOrderAndNeverSkipped()
        {
            var q = new SessionQueue();
            Assert.True(q.Request(0, 255));
            Assert.True(q.TryNext(out var opening));                 // opening session starts
            Assert.True(q.Request(1, 1));                            // fire 1 lit while the opening still runs
            Assert.True(q.Request(1, 2));                            // fire 2 lit right after
            Assert.False(q.HasStarted(1));                           // not marked done while it waits
            Assert.True(q.TryNext(out var a)); Assert.Equal(1, a.Segment);
            Assert.True(q.TryNext(out var b)); Assert.Equal(2, b.Segment);
            Assert.False(q.TryNext(out _));
            Assert.Equal(255, opening.Segment);
        }

        [Fact]
        public void TheSameFireNeverGetsTwoSessions()
        {
            var q = new SessionQueue();
            Assert.True(q.Request(1, 3));
            Assert.False(q.Request(1, 3));    // duplicate event while waiting
            q.TryNext(out _);
            Assert.False(q.Request(1, 3));    // duplicate event after it started
            Assert.Equal(0, q.Count);
        }

        [Fact]
        public void ClientMirrorLetsANewHostContinueTheQueue()
        {
            var host = new SessionQueue(); var client = new SessionQueue();
            foreach (var q in new[] { host, client }) { q.Request(1, 1); q.Request(1, 2); }   // both fires lit, one session at a time
            host.TryNext(out var first);
            client.MarkStarted(first.Segment);           // the client sees PickSessionStart for fire 1
            // the host leaves: the client becomes host and deals what is still waiting, and only that
            Assert.True(client.TryNext(out var next));
            Assert.Equal(2, next.Segment);
            Assert.False(client.TryNext(out _));
        }

        [Fact]
        public void StartSeenBeforeTheLightingEventStillCountsOnce()
        {
            var client = new SessionQueue();
            client.MarkStarted(4);                       // the session start overtook the lighting RPC
            Assert.False(client.Request(1, 4));
            Assert.Equal(0, client.Count);
        }

        [Fact]
        public void ResetClearsPendingSessions()
        {
            var q = new SessionQueue();
            q.Request(1, 1); q.Request(1, 2); q.TryNext(out _);
            q.Reset();
            Assert.Equal(0, q.Count);
            Assert.True(q.Request(1, 1));     // a new run lights segment 1 again
        }
    }

    public class RevivalTests
    {
        [Fact]
        public void ConsecutiveRunsStartWithFreshRevivals()
        {
            var a = new ReviveArbiter();
            Assert.True(a.TryGrant(1, 0, 2, 1));       // run 1: Shore, victim 2's first incapacitation
            a.Reset();                                  // run end / run start
            Assert.True(a.TryGrant(1, 0, 2, 1));       // run 2: same actors, same biome, same event number
            a.Reset();
            Assert.False(a.UsedInBiome(1, 0));
        }

        [Fact]
        public void OverlappingRequestsReviveOnce()
        {
            var a = new ReviveArbiter();
            int granted = 0;
            foreach (var owner in new[] { 1, 3, 1, 3 }) if (a.TryGrant(owner, 2, 4, 9)) granted++;
            Assert.Equal(1, granted);
            Assert.True(a.UsedInBiome(1, 2));
            Assert.False(a.UsedInBiome(3, 2));
        }

        [Fact]
        public void LimitsResetBetweenBiomes()
        {
            var a = new ReviveArbiter();
            Assert.True(a.TryGrant(1, 0, 2, 1));
            Assert.False(a.TryGrant(1, 0, 3, 1));
            Assert.True(a.TryGrant(1, 1, 3, 1));
        }

        [Fact]
        public void RejoiningVictimIsNotBlockedButTheOwnerChargeStaysSpent()
        {
            var a = new ReviveArbiter();
            Assert.True(a.TryGrant(1, 0, 2, 1));
            a.ForgetVictim(2);                          // player 2 left; back with the same actor number, counter restarted
            Assert.False(a.TryGrant(1, 0, 2, 1));      // owner 1 already used this biome
            Assert.True(a.TryGrant(5, 0, 2, 1));       // another owner may revive the rejoined player's event 1
            a.ForgetVictim(99);                         // unknown player: harmless
        }
    }

    public class SpawnLedgerTests
    {
        [Fact]
        public void EachRewardSpawnsOncePerFire()
        {
            var l = new SpawnLedger();
            l.FireLit(3);
            Assert.Equal(SpawnVerdict.Ok, l.TryClaim(2, 0, 3, true));
            for (int i = 0; i < 50; i++) Assert.Equal(SpawnVerdict.AlreadySpawned, l.TryClaim(2, 0, 3, true));   // spammed requests
            Assert.Equal(SpawnVerdict.Ok, l.TryClaim(2, 1, 3, true));   // Supply Scout and Rope Baron are separate cards
            Assert.Equal(SpawnVerdict.Ok, l.TryClaim(5, 0, 3, true));   // another owner of Rope Baron (duplicates allowed)
        }

        [Fact]
        public void RequestsNeedTheCardAndALitFire()
        {
            var l = new SpawnLedger();
            Assert.Equal(SpawnVerdict.UnknownFire, l.TryClaim(2, 0, 4, true));   // a fire that was never lit
            l.FireLit(4);
            Assert.Equal(SpawnVerdict.NotOwned, l.TryClaim(2, 0, 4, false));
            Assert.Equal(SpawnVerdict.Ok, l.TryClaim(2, 0, 4, true));   // a refused request did not use it up
        }

        [Fact]
        public void NewFireAndNewRunRefill()
        {
            var l = new SpawnLedger();
            l.FireLit(1);
            Assert.Equal(SpawnVerdict.Ok, l.TryClaim(2, 0, 1, true));
            l.FireLit(2);
            Assert.Equal(SpawnVerdict.Ok, l.TryClaim(2, 0, 2, true));
            l.Reset();
            Assert.Equal(SpawnVerdict.UnknownFire, l.TryClaim(2, 0, 2, true));
            l.FireLit(1);
            Assert.Equal(SpawnVerdict.Ok, l.TryClaim(2, 0, 1, true));
        }
    }

    public class LuckyLunchTransactionTests
    {
        /// <summary>The Lucky Lunch decision as the hooks make it: eligible food spends the per-biome charge.</summary>
        private static bool Saves(ItemTransaction tx, Charge charge, int item, int frame, int biome, ref int evaluations, bool cookedFood = true)
        {
            int n = evaluations;
            bool result = tx.Decide(item, frame, () => { n++; return cookedFood && charge.TryUse(biome); });
            evaluations = n;
            return result;
        }

        [Fact]
        public void AllHooksOfOneUseShareOneDecisionAndOneCharge()
        {
            var tx = new ItemTransaction(); var charge = new Charge(perBiome: true); int evals = 0;
            // reduceUses, consumeAndSpawn and consumeDelayed all fire for the same use, in any order
            Assert.True(Saves(tx, charge, item: 42, frame: 100, biome: 0, ref evals));
            Assert.True(Saves(tx, charge, item: 42, frame: 100, biome: 0, ref evals));
            Assert.True(Saves(tx, charge, item: 42, frame: 100, biome: 0, ref evals));
            Assert.Equal(1, evals);
            Assert.False(charge.Available(0));          // spent exactly once
        }

        [Fact]
        public void OnConsumedEffectsRunOncePerUse()
        {
            var tx = new ItemTransaction(); var charge = new Charge(perBiome: true); int evals = 0;
            Saves(tx, charge, 42, 100, 0, ref evals);
            Assert.True(tx.TryFireEffects(42, 100));
            Assert.False(tx.TryFireEffects(42, 100));   // a second keeping path does not repeat them
            Assert.False(tx.TryFireEffects(43, 100));   // another item has no decision yet
        }

        [Fact]
        public void LaterUsesInTheSameBiomeAreNormalAndANewBiomeRefills()
        {
            var tx = new ItemTransaction(); var charge = new Charge(perBiome: true); int evals = 0;
            Assert.True(Saves(tx, charge, 42, 100, 0, ref evals));   // first cooked food: kept
            Assert.False(Saves(tx, charge, 42, 160, 0, ref evals));  // same multi-use item, next use: used up normally
            Assert.False(Saves(tx, charge, 77, 200, 0, ref evals));  // other food, same biome
            Assert.True(Saves(tx, charge, 77, 900, 1, ref evals));   // new biome
        }

        [Fact]
        public void UncookedFoodNeverSpendsTheCharge()
        {
            var tx = new ItemTransaction(); var charge = new Charge(perBiome: true); int evals = 0;
            Assert.False(Saves(tx, charge, 42, 100, 0, ref evals, cookedFood: false));
            Assert.False(Saves(tx, charge, 42, 100, 0, ref evals, cookedFood: true));   // same use: same answer, no late charge
            Assert.True(charge.Available(0));
        }
    }

    public class ThornsTests
    {
        [Fact]
        public void IncomingThornsAreHalvedWithThornbreaker()
        {
            var l = new ThornsLedger();
            l.Update(4, 0.5f);
            Assert.Equal(2, l.ScaledSteps);
            l.Update(6, 0.5f);
            Assert.Equal(3, l.ScaledSteps);
        }

        [Fact]
        public void StrongMultipliersNeverEraseAStuckThornOutright()
        {
            // thornsRate 0.2 (the 0.7.6 Thornbreaker): one thorn scales to 0.2, which must still show as one unit
            var l = new ThornsLedger();
            l.Update(1, 0.2f);
            Assert.Equal(1, l.ScaledSteps);
            l.Update(2, 0.2f);
            Assert.Equal(1, l.ScaledSteps);
            l.Update(10, 0.2f);                // 10 x 0.2 = 2 units: the 80% discount holds in bulk
            Assert.Equal(2, l.ScaledSteps);
            l.Update(0, 0.2f);                 // all pulled out: nothing lingers
            Assert.Equal(0, l.ScaledSteps);
        }

        [Fact]
        public void WeightRecalculationsNeverReduceThornsAgain()
        {
            var l = new ThornsLedger();
            l.Update(6, 0.5f);
            for (int i = 0; i < 100; i++) l.Update(6, 0.5f);   // every inventory change recomputes the same count
            Assert.Equal(3, l.ScaledSteps);
            Assert.Equal(6, l.Raw);
        }

        [Fact]
        public void ThornsAlreadyStuckKeepTheirValueWhenTheMultiplierChanges()
        {
            var l = new ThornsLedger();
            l.Update(4, 1f);                 // stuck before the card
            l.Update(4, 0.5f);               // card picked: nothing changes for these
            Assert.Equal(4, l.ScaledSteps);
            l.Update(6, 0.5f);               // two new thorns count half
            Assert.Equal(5, l.ScaledSteps);
            l.Update(6, 1f);                 // card sacrificed / aura left: still nothing changes
            Assert.Equal(5, l.ScaledSteps);
            l.Update(8, 1f);                 // new thorns count fully again
            Assert.Equal(7, l.ScaledSteps);
        }

        [Fact]
        public void PulledThornsRemoveTheirShareAndZeroResets()
        {
            var l = new ThornsLedger();
            l.Update(8, 0.5f);
            l.Update(4, 0.5f);
            Assert.Equal(2, l.ScaledSteps);
            l.Update(0, 0.5f);
            Assert.Equal(0, l.ScaledSteps);
            l.Update(2, 1f);
            Assert.Equal(2, l.ScaledSteps);
        }

        [Fact]
        public void ASingleThornIsNeverErasedByRounding()
        {
            var l = new ThornsLedger();
            l.Update(1, 0.5f);
            Assert.Equal(1, l.ScaledSteps);
            l.Update(1, 0.4f * 0.5f);
            Assert.Equal(1, l.ScaledSteps);
        }
    }

    public class ShuffleTests
    {
        private static readonly CardSettings S = new CardSettings();

        private static string Opening(Random rng) =>
            string.Join(",", CardDraw.Offer(rng, S, new HashSet<byte>(), OfferRules.Campfire(3, true, false)).OrderBy(i => i));

        [Fact]
        public void OldSeedingReplayedTheSameOpeningEveryLaunch()
        {
            // what 0.3.0/0.4.0 did: first session of every launch = id 10001, solo actor 1
            Assert.Equal(Opening(new Random(10001 * 7919 + 1)), Opening(new Random(10001 * 7919 + 1)));
        }

        [Fact]
        public void EveryLaunchDealsDifferentOpenings()
        {
            var openings = Enumerable.Range(0, 40).Select(_ => Opening(DealRandom.Create())).ToList();
            Assert.True(openings.Distinct().Count() >= 30, $"only {openings.Distinct().Count()} distinct openings in 40 launches");
        }

        [Fact]
        public void OffersWithinARunAreShuffled()
        {
            var rng = DealRandom.Create();
            var seen = new HashSet<byte>();
            for (int session = 0; session < 30; session++)
                foreach (var id in CardDraw.Offer(rng, S, new HashSet<byte>(), OfferRules.Campfire(3, true, false))) seen.Add(id);
            // 90 cards drawn from the 56 solo cards: a shuffled deck shows most of them
            Assert.True(seen.Count >= 35, $"only {seen.Count} different cards in 30 offers");
        }
    }

    public class StatusPassThroughTests
    {
        [Theory]
        [InlineData(0.0005f * (1f / 60f))]          // PEAK Hunger per frame at 60 fps (CharacterAfflictions.UpdateNormalStatuses)
        [InlineData(0.0005f * 1.5f * (1f / 144f))]  // Hunger with an ascent multiplier at 144 fps
        [InlineData(0.005f * (1f / 60f))]           // Ascents.nightColdRate per frame
        [InlineData(0.0001f)]                       // the old cut-off itself
        public void TinyPerFrameStatusesReachTheGame(float perFrame)
        {
            // regression: amounts at or under 0.0001 used to be swallowed, so Hunger never built up during a run
            var r = new ModResult();
            r.Fold(new ActiveMod[0], new CardSettings());
            Assert.False(StatComposites.CancelsStatus(perFrame * StatComposites.StatusGain(r, Channel.hungerRate)));
        }

        [Fact]
        public void OnlyAStatusScaledToNothingIsCancelled()
        {
            var s = new CardSettings();
            var heir = CardTable.Cards.First(c => c.Key == "MountainsHeir");
            var r = new ModResult();
            r.Fold(heir.Mods.Select((m, i) => new ActiveMod { CardId = heir.Id, ModIndex = i, Channel = m.Channel, Op = m.Op, Value = m.Value, Rarity = heir.Rarity }), s);
            Assert.True(StatComposites.CancelsStatus(0.3f * StatComposites.Injury(r, fall: true)));
            Assert.False(StatComposites.CancelsStatus(0.3f * StatComposites.Injury(r, fall: false)));
        }

        [Fact]
        public void StaminaBarFrameFollowsTheCardMaximum()
        {
            Assert.Equal(0.9f, StatComposites.BarFrame(-0.1f, 0.2f), 4);   // Supply Scout: the bar is visibly shorter
            Assert.Equal(1.2f, StatComposites.BarFrame(0.2f, 0.1f), 4);    // Deep Lungs: drawn inside the frame
            Assert.Equal(1.1f, StatComposites.BarFrame(-0.1f, 1.1f), 4);   // statuses overflowing grow it, as in vanilla
            Assert.Equal(0.05f, StatComposites.BarFrame(-2f, 0f), 4);
        }

        [Fact]
        public void StatusSegmentsEndWhereTheCardAdjustedBarEnds()
        {
            // HUD: segments are right-aligned in a row spanning the full 600-unit bar; MaxStamina hangs from the left
            const float w = 600f;
            foreach (var (bonus, statusSum) in new[] { (-0.15f, 0.08f), (-0.2f, 0.5f), (0.2f, 0.1f) })
            {
                float maxStaminaEnd = (1f + bonus - statusSum) * w;          // GetMaxStamina() x width (mod postfix)
                float segmentsStart = w + StatComposites.BarSegmentsShift(bonus, w) - statusSum * w;
                Assert.Equal(maxStaminaEnd, segmentsStart, 2);                // no gap, no overlap
                Assert.Equal(StatComposites.BarFrame(bonus, statusSum) * w, w + StatComposites.BarSegmentsShift(bonus, w), 2);
            }
        }
    }

    public class FingerprintTests
    {
        [Fact]
        public void FingerprintIsDeterministic()
        {
            var a = SettingsFingerprint.Of(new CardSettings());
            var b = SettingsFingerprint.Of(new CardSettings());
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(a.Cards, b.Cards);
            Assert.Empty(SettingsFingerprint.Differences(a, b.Rules, b.Weights, b.Cards));
        }

        [Fact]
        public void ReviewedBalanceChangesTheLobbyFingerprint()
        {
            // A lobby with the previous deck must not report this redesigned deck as compatible.
            Assert.NotEqual(PinnedDefaultHash, SettingsFingerprint.Of(new CardSettings()).Hash);
        }

        internal const int PinnedDefaultHash = -560646409;   // 0.4.1 balance (34 drawback-free cards); 0.4.0 was 2036217628

        public static IEnumerable<object[]> Changes()
        {
            yield return new object[] { "offer count", (Action<CardSettings>)(s => s.OfferCount = 4) };
            yield return new object[] { "duplicate cards", (Action<CardSettings>)(s => s.AllowDuplicates = true) };
            yield return new object[] { "Mythics enabled", (Action<CardSettings>)(s => s.MythicsEnabled = false) };
            yield return new object[] { "stamina-cost reduction cap", (Action<CardSettings>)(s => s.StaminaCostCap = 0.5f) };
            yield return new object[] { "drawbacks", (Action<CardSettings>)(s => s.DrawbacksEnabled = false) };
            yield return new object[] { "max-stamina floor", (Action<CardSettings>)(s => s.MaxStaminaFloor = 0.4f) };
            yield return new object[] { "rarity weights", (Action<CardSettings>)(s => s.RarityWeights[4] = 1f) };
            yield return new object[] { "cards: Piton Fingers", (Action<CardSettings>)(s => s.ModValues[0][0] = 0.8f) };
            yield return new object[] { "cards: Bing BOING", (Action<CardSettings>)(s => s.Params[4]["cooldown"] = 30f) };
            yield return new object[] { "cards: Mini Scout Cannon", (Action<CardSettings>)(s => s.Enabled[1] = false) };
        }

        [Theory]
        [MemberData(nameof(Changes))]
        public void EveryGameplaySettingIsCoveredAndNamed(string expected, Action<CardSettings> change)
        {
            var mine = SettingsFingerprint.Of(new CardSettings());
            var s = new CardSettings();
            change(s);
            var theirs = SettingsFingerprint.Of(s);
            Assert.NotEqual(mine.Hash, theirs.Hash);
            Assert.Contains(expected, SettingsFingerprint.Differences(mine, theirs.Rules, theirs.Weights, theirs.Cards));
        }

        [Fact]
        public void SameValueFromConfigTextHashesTheSame()
        {
            var a = new CardSettings(); var b = new CardSettings();
            a.ModValues[0][0] = 0.82f;
            b.ModValues[0][0] = float.Parse("0.8200", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(SettingsFingerprint.Of(a).Hash, SettingsFingerprint.Of(b).Hash);
        }

        [Fact]
        public void MissingDetailsAreNeverReportedAsCompatible()
        {
            var mine = SettingsFingerprint.Of(new CardSettings());
            Assert.NotEmpty(SettingsFingerprint.Differences(mine, null, null, null));
            Assert.Contains("card list", SettingsFingerprint.Differences(mine, mine.Rules, mine.Weights, new int[3]));
        }

        [Fact]
        public void SecondSpringHasACooldownParameter()
        {
            // the fingerprint test above edits it; make sure the card still has it
            Assert.True(CardTable.Cards[4].Params.ContainsKey("cooldown"), CardTable.Cards[4].Key);
        }
    }

    public class BuildStatsTests
    {
        private static ModResult Fold(CardSettings s, params (string key, Func<ModDef, bool> include)[] cards)
        {
            var mods = new List<ActiveMod>();
            foreach (var (key, include) in cards)
            {
                var def = CardTable.Cards.First(c => c.Key == key);
                for (int i = 0; i < def.Mods.Length; i++)
                    if (include(def.Mods[i]))
                        mods.Add(new ActiveMod { CardId = def.Id, ModIndex = i, Channel = def.Mods[i].Channel, Op = def.Mods[i].Op, Value = s.ModValue(def, i), Rarity = def.Rarity });
            }
            var r = new ModResult();
            r.Fold(mods, s);
            return r;
        }

        private static bool Always(ModDef m) => m.When == Condition.always && m.Target != ModTarget.team;

        [Fact]
        public void CombinedStatsMatchTheHookComposites()
        {
            var s = new CardSettings();
            var r = Fold(s, ("RocketAnkles", Always), ("GlassHeart", Always));
            var lines = BuildStats.Lines(r, r).ToDictionary(l => l.Label);
            Assert.Equal(1.35f, lines["Sprint speed"].Now, 4);
            Assert.Equal(1.25f, lines["Jump height"].Now, 4);
            Assert.Equal(1.25f * 1.25f, lines["Fall Injury received"].Now, 4);
            Assert.Equal(1.25f, lines["Other Injury received"].Now, 4);
            Assert.Equal(StatComposites.Injury(r, true), lines["Fall Injury received"].Now, 5);
            Assert.False(lines["Sprint speed"].Conditional);
        }

        [Fact]
        public void ConditionalEffectsAreNotPresentedAsPermanent()
        {
            var s = new CardSettings();
            var now = Fold(s, ("MomentumFiend", m => true));        // momentum active right now
            var always = Fold(s, ("MomentumFiend", Always));
            var line = BuildStats.Lines(now, always).Single(l => l.Label == "Sprint speed");
            Assert.True(line.Conditional);
            Assert.Equal(1.3f, line.Now, 4);
            Assert.Equal(1f, line.Always, 4);
        }

        [Fact]
        public void HeldWeightAndStatusRatesCombineLikeTheGame()
        {
            var s = new CardSettings();
            var r = Fold(s, ("OneHandedScout", Always), ("Ultralight", Always), ("EternalCampfire", Always), ("Thornbreaker", Always));
            var lines = BuildStats.Lines(r, r).ToDictionary(l => l.Label);
            Assert.Equal(0.55f * 0.35f, lines["Item in hand weight"].Now, 4);
            Assert.DoesNotContain("Carried item weight", lines.Keys); // ordinary pocket items have no Carry-On Only discount
            Assert.Equal(0.2f * 0.4f, lines["Thorns build-up"].Now, 4);
            Assert.Equal(StatComposites.Thorns(r), lines["Thorns build-up"].Now, 5);
            Assert.Equal("+35", BuildStats.Format(r[Channel.maxStamina], false, "stamina"));
        }

        [Fact]
        public void MythicZeroCostReadsNoneAndUnbuiltSlotsAreHidden()
        {
            var s = new CardSettings();
            var r = Fold(s, ("MountainsHeir", Always), ("PackRat", Always));
            var lines = BuildStats.Lines(r, r);
            Assert.Equal("none", BuildStats.Format(lines.Single(l => l.Label == "Climbing stamina cost").Now, true, null));
            Assert.DoesNotContain(lines, l => l.Label == ChannelTable.Defs[(int)Channel.invSlots].Label);
        }

        [Fact]
        public void TwoModsOnOneChannelFromOneCardMultiplyInTheFold()
        {
            // Surefooted (0.7.6) is the first card stacking an always and a conditional mod on the same channel for
            // the same target: on blue ice both apply and the fold must multiply them, not keep one
            var s = new CardSettings();
            var r = Fold(s, ("Surefooted", m => true));
            Assert.Equal(0.7f * 0.5f, r[Channel.coldRate], 4);
        }

        [Fact]
        public void CombinedEffectsReadLikeTheCards()
        {
            var s = new CardSettings();
            var r = Fold(s, ("WinterCoat", Always), ("LedgeDancer", Always), ("DeepLungs", Always), ("QuickRecovery", Always), ("RocketAnkles", Always));
            var lines = BuildStats.Lines(r, r).ToDictionary(l => l.Label);
            Assert.Equal("You get cold 70% slower", BuildStats.Say(lines["Cold build-up"], lines["Cold build-up"].Now));
            Assert.Equal("You heat up 10% faster", BuildStats.Say(lines["Heat build-up"], lines["Heat build-up"].Now));
            Assert.Equal("Grab walls from 40% farther away", BuildStats.Say(lines["Wall-grab reach"], lines["Wall-grab reach"].Now));
            Assert.Equal("Your stamina bar is 25 longer", BuildStats.Say(lines["Max stamina"], lines["Max stamina"].Now));
            Assert.Equal("Falls hurt 25% more", BuildStats.Say(lines["Fall Injury received"], lines["Fall Injury received"].Now));
            Assert.True(BuildStats.Helps(lines["Cold build-up"].Now, true, lines["Cold build-up"].Words));
            Assert.False(BuildStats.Helps(lines["Heat build-up"].Now, true, lines["Heat build-up"].Words));
            Assert.False(BuildStats.Helps(lines["Fall Injury received"].Now, true, lines["Fall Injury received"].Words));
        }

        [Fact]
        public void MythicZeroAndNeutralValuesHaveTheirOwnWords()
        {
            var s = new CardSettings();
            var r = Fold(s, ("MountainsHeir", Always));
            var line = BuildStats.Lines(r, r).Single(l => l.Label == "Climbing stamina cost");
            Assert.Equal("Climbing never tires you", BuildStats.Say(line, line.Now));
            Assert.Null(BuildStats.Say(1f, true, null, StatWords.Of(Channel.sprintSpeed)));
            Assert.Equal("Stamina starts coming back 1 second later", BuildStats.Say(1f, false, "seconds", StatWords.Of(Channel.regenDelay)));
        }

        [Fact]
        public void EveryChannelAndConditionHasInGameWords()
        {
            foreach (var d in ChannelTable.Defs)
            {
                Assert.Contains("{0}", d.More);
                Assert.Contains("{0}", d.Less);
            }
            Assert.Equal(System.Enum.GetValues(typeof(Condition)).Length, ConditionTable.Shown.Length);
            Assert.All(ConditionTable.Shown, w => Assert.False(string.IsNullOrWhiteSpace(w)));
        }

        [Fact]
        public void NoCardsMeansNoLines()
        {
            var r = new ModResult();
            Assert.Empty(BuildStats.Lines(r, r));
            Assert.Equal("x1.25", BuildStats.Format(1.25f, true, null));
            Assert.Equal("-0.5 s", BuildStats.Format(-0.5f, false, "seconds"));
        }
    }

    public class WireFormatTests
    {
        [Fact]
        public void NewEventsRoundTrip()
        {
            var req = PickRequest.Read(new PickRequest { sessionId = 10003, cardId = 17 }.Write());
            Assert.Equal(10003, req.sessionId); Assert.Equal(17, req.cardId);
            var spawn = SpawnItem.Read(new SpawnItem { item = 1, segment = 4 }.Write());
            Assert.Equal(1, spawn.item); Assert.Equal(4, spawn.segment);
            var f = SettingsFingerprint.Of(new CardSettings());
            var hello = Hello.Read(new Hello { modVersion = "0.4.0", cardCount = 64, deckHash = f.Hash, rules = f.Rules, weights = f.Weights, cards = f.Cards }.Write());
            Assert.Equal(f.Cards, hello.cards);
            var start = PickSessionStart.Read(new PickSessionStart { sessionId = 1, order = new[] { 2, 1 }, hands = new byte[] { 1, 2, 3, 4, 5, 6 }, handSize = 3, timeoutSeconds = 30f, mode = 1 }.Write());
            Assert.Equal(1, start.mode); Assert.Equal(new[] { 2, 1 }, start.order);
        }

        [Fact]
        public void HostileArrayLengthsAreRefusedBeforeAllocating()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(1);                 // sessionId
                w.Write((byte)0);           // reason
                w.Write(int.MaxValue);      // order length: far more than the payload holds
                w.Flush();
                Assert.Throws<InvalidDataException>(() => PickSessionStart.Read(ms.ToArray()));
            }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(1); w.Write(2);     // sessionId, actor
                w.Write(-5);                // negative byte[] length
                w.Flush();
                Assert.Throws<InvalidDataException>(() => CardsGranted.Read(ms.ToArray()));
            }
        }

        [Fact]
        public void TruncatedOldFormatHelloFailsLoudly()
        {
            // a 0.3.0 Hello (version, count, hash only) cannot be read by 0.4.0: the receiver reports the mismatch
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write("0.3.0"); w.Write(64); w.Write(123);
                w.Flush();
                Assert.ThrowsAny<IOException>(() => Hello.Read(ms.ToArray()));
            }
        }

        [Fact]
        public void DispatchRoutesThePickRequestToTheHost()
        {
            NetHandlers.Last.Clear();
            Assert.True(NetDispatcher.Dispatch(NetCodes.PickRequest, new PickRequest { sessionId = 5, cardId = 9 }.Write(), 3));
            Assert.Equal((NetCodes.PickRequest, 3), (NetHandlers.Last[0].code, NetHandlers.Last[0].sender));
            Assert.False(NetDispatcher.Dispatch(119, new byte[0], 3));
        }
    }
}
