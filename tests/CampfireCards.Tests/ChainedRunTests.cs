using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Net;
using Xunit;

namespace CampfireCards.Tests
{
    /// <summary>
    /// Chained multi-biome runs (True Final Ascent) revisit vanilla segment numbers and cross biome boundaries
    /// without moving the map's segment counter. The queue exposes what it already used so the campfire hook can
    /// re-key repeats, and LocalCards.Biome is a monotonic ordinal that chain boundaries can push past the segment.
    /// </summary>
    [Collection("Pick sessions")]
    public class ChainedRunTests
    {
        public ChainedRunTests()
        {
            PickSession.Reset();
            LocalCards.Reset();
            PlayerHandler.Characters.Clear();
            Character.localCharacter = new Character();
            PlayerHandler.Characters[1] = Character.localCharacter;
            NetSend.IsHost = false; NetSend.LocalActor = 1; NetSend.Deliver = false;
        }

        // ------------------------------------------------------------------ SessionQueue.Used

        [Fact]
        public void UsedSeesWaitingAndStartedSegments()
        {
            var q = new SessionQueue();
            Assert.False(q.Used(1));
            Assert.True(q.Request(1, 1));
            Assert.True(q.Used(1));                       // waiting counts
            Assert.False(q.Request(1, 1));                // and blocks a repeat
            SessionQueue.Entry e;
            Assert.True(q.TryNext(out e));
            Assert.Equal(1, (int)e.Segment);
            Assert.True(q.Used(1));                       // started counts too
            Assert.False(q.Used(2));
            q.Reset();
            Assert.False(q.Used(1));
        }

        [Fact]
        public void RepeatedSegmentIsRefusedButAChainKeyIsNot()
        {
            var q = new SessionQueue();
            Assert.True(q.Request(1, 1));
            SessionQueue.Entry e; q.TryNext(out e);
            Assert.False(q.Request(1, 1));                // the second fire on the same number: refused
            Assert.True(q.Request(1, 200));               // the chain key the hook assigns instead: accepted
            Assert.True(q.Used(200));
        }

        [Fact]
        public void MarkStartedBlocksTheSegmentLikeADeal()
        {
            var q = new SessionQueue();
            q.MarkStarted(3);                             // a client mirroring the host's PickSessionStart
            Assert.True(q.Used(3));
            Assert.False(q.Request(1, 3));
        }

        // ------------------------------------------------------------------ biome ordinal

        [Fact]
        public void VanillaSegmentsProduceTheSameBiomeNumbersAsBefore()
        {
            var me = Character.localCharacter;
            Assert.Equal(-1, LocalCards.Biome);
            LocalCards.OnSegmentNumber(me, 0);            // beach
            Assert.Equal(0, LocalCards.Biome);
            LocalCards.OnSegmentNumber(me, 1);
            Assert.Equal(1, LocalCards.Biome);
            LocalCards.OnSegmentNumber(me, 2);
            Assert.Equal(2, LocalCards.Biome);
        }

        [Fact]
        public void ChainBoundariesPushTheBiomePastTheSegmentNumber()
        {
            var me = Character.localCharacter;
            LocalCards.OnSegmentNumber(me, 0);
            LocalCards.OnSegmentNumber(me, 1);            // Shore -> Tropics fire
            Assert.Equal(1, LocalCards.Biome);
            LocalCards.OnChainBoundary(me);               // Tropics -> Roots stage fire: no segment move
            Assert.Equal(2, LocalCards.Biome);
            LocalCards.OnSegmentNumber(me, 2);            // Roots -> Alpine fire: segment catches up
            Assert.Equal(3, LocalCards.Biome);            // still a NEW biome, not a repeat of 2
            LocalCards.OnChainBoundary(me);               // Alpine -> Mesa stage fire
            Assert.Equal(4, LocalCards.Biome);
        }

        [Fact]
        public void LateJoinJumpsStraightToTheCurrentSegment()
        {
            var me = Character.localCharacter;
            LocalCards.OnSegmentNumber(me, 3);            // joined mid-run at the Caldera
            Assert.Equal(3, LocalCards.Biome);
        }

        [Fact]
        public void BackwardsSegmentStillCountsAsANewBiome()
        {
            var me = Character.localCharacter;
            LocalCards.OnSegmentNumber(me, 4);
            Assert.Equal(4, LocalCards.Biome);
            LocalCards.OnSegmentNumber(me, 1);            // a custom chain goes back down the mountain
            Assert.Equal(5, LocalCards.Biome);            // new biome, so once-per-biome charges refill
        }

        // ------------------------------------------------------------------ pre-lighting eligibility

        private static Character Add(int actor, bool dead = false)
        {
            var c = new Character(); c.data.dead = dead;
            PlayerHandler.Characters[actor] = c;
            Photon.Pun.PhotonNetwork.CurrentRoom.Players[actor] = new Photon.Realtime.Player { ActorNumber = actor };
            if (actor == NetSend.LocalActor) Character.localCharacter = c;
            return c;
        }

        [Fact]
        public void AdoptedSnapshotExcludesScoutsTheFireRevived()
        {
            Photon.Pun.PhotonNetwork.CurrentRoom = new Photon.Realtime.Room();
            PlayerHandler.Characters.Clear();
            Add(1);
            var ghost = Add(2, dead: true);
            PickSession.CapturePreLitEligibility();       // the lighting prefix runs while scout 2 is dead
            ghost.data.dead = false;                      // the fire's RPC revives them
            PickSession.AdoptPreLitEligibility(200);
            Assert.True(PickSession.EligibleAtCampfire(200, 1));
            Assert.False(PickSession.EligibleAtCampfire(200, 2));
        }

        [Fact]
        public void AdoptWithoutACaptureFallsBackToTheCurrentRoom()
        {
            Photon.Pun.PhotonNetwork.CurrentRoom = new Photon.Realtime.Room();
            PlayerHandler.Characters.Clear();
            Add(1); Add(2);
            PickSession.AdoptPreLitEligibility(201);      // no prefix capture happened
            Assert.True(PickSession.EligibleAtCampfire(201, 1));
            Assert.True(PickSession.EligibleAtCampfire(201, 2));
        }

        [Fact]
        public void PerBiomeChargeRefillsAtAChainBoundary()
        {
            var charge = new Charge(perBiome: true);
            var me = Character.localCharacter;
            LocalCards.OnSegmentNumber(me, 1);
            Assert.True(charge.TryUse(LocalCards.Biome));
            Assert.False(charge.Available(LocalCards.Biome));   // spent in Tropics
            LocalCards.OnChainBoundary(me);                     // stage fire into Roots
            Assert.True(charge.Available(LocalCards.Biome));    // refilled without a segment move
        }
    }
}
