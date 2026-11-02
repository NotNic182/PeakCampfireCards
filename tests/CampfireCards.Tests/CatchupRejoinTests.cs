using System.Linq;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Net;
using Photon.Pun;
using Xunit;

namespace CampfireCards.Tests
{
    /// <summary>
    /// Players who load in after a session was dealt (slow chained-run world builds) are owed a catch-up deal, and a
    /// rejoiner (new actor number, same account) gets their old state back instead of a second helping. The dealt
    /// mirror is keyed by published user id so both features survive host migration.
    /// </summary>
    [Collection("Pick sessions")]
    public class CatchupRejoinTests
    {
        private static Character Add(int actor, bool dead = false, string userId = null)
        {
            var c = new Character(); c.data.dead = dead;
            PlayerHandler.Characters[actor] = c;
            PhotonNetwork.CurrentRoom.Players[actor] = new Photon.Realtime.Player { ActorNumber = actor, UserId = userId };
            if (actor == NetSend.LocalActor) Character.localCharacter = c;
            return c;
        }

        /// <summary>A player in the Photon room whose character has not spawned yet (still loading the world).</summary>
        private static void AddRoomOnly(int actor, string userId = null) =>
            PhotonNetwork.CurrentRoom.Players[actor] = new Photon.Realtime.Player { ActorNumber = actor, UserId = userId };

        public CatchupRejoinTests()
        {
            PickSession.Reset();
            LocalCards.Reset(); Effects.Penalties.Reset(); Deck.BonusCards = new byte[0];
            PlayerHandler.Characters.Clear(); Effects.Owned.Clear(); Effects.PickFlags.Clear();
            NetSend.Sent.Clear(); NetSend.IsHost = true; NetSend.LocalActor = 1; NetSend.Deliver = true;
            PhotonNetwork.CurrentRoom = new Photon.Realtime.Room(); PhotonNetwork.Time = 0;
            UnityEngine.Time.unscaledTime = 0; CampfireCards.Ui.CardPicker.Instance = null;
            Plugin.PickTimeoutSeconds = new Setting<float>(60f);
            Plugin.PickModeSetting = new Setting<PickMode>(PickMode.Simultaneous);
            CardSettings.Current = new CardSettings(); Deck.Ready = true;
            Add(1);
        }

        /// <summary>Opening dealt to the host alone (everyone else still loading), host takes a card.</summary>
        private static void DealOpeningToHostAlone()
        {
            PickSession.OnRunStarted();
            PickSession.RecordArrival(1, 255, 0);
            PhotonNetwork.Time = 31;   // past the 30s opening gate
            PickSession.HostTick();
            Assert.Equal(new[] { 1 }, Assert.Single(NetSend.Sent.OfType<PickSessionStart>()).order);
            PickSession.LocalPick(1);
            Assert.False(PickSession.Active);
        }

        private static PickSessionStart[] Starts() => NetSend.Sent.OfType<PickSessionStart>().ToArray();

        // ------------------------------------------------------------------ catch-up deals

        [Fact]
        public void LateLoaderIsCaughtUpOnTheOpening()
        {
            AddRoomOnly(2);
            DealOpeningToHostAlone();

            Add(2);                                        // the friend's world finally built
            PickSession.RecordArrival(2, 255, 31);         // their own opening Arrival (NetHandlers forwards both)
            PickSession.HostNoteReady(2, arrived: true);
            PickSession.HostTick();

            var starts = Starts();
            Assert.Equal(2, starts.Length);
            Assert.Equal(new[] { 2 }, starts[1].order);
            Assert.Equal(255, (int)starts[1].segment);
            Assert.Equal(0, (int)starts[1].reason);
        }

        [Fact]
        public void TwoLateLoadersShareOneCatchupSession()
        {
            AddRoomOnly(2); AddRoomOnly(3);
            DealOpeningToHostAlone();

            Add(2); Add(3);
            PickSession.RecordArrival(2, 255, 31); PickSession.HostNoteReady(2, arrived: true);
            PickSession.RecordArrival(3, 255, 32); PickSession.HostNoteReady(3, arrived: true);
            PickSession.HostTick();

            var starts = Starts();
            Assert.Equal(2, starts.Length);
            Assert.Equal(new[] { 3, 2 }, starts[1].order);   // one session for both, last to arrive first
        }

        [Fact]
        public void CatchupIsDealtOncePerAccountHoweverOftenTheTriggerFires()
        {
            AddRoomOnly(2);
            DealOpeningToHostAlone();
            Add(2);
            PickSession.RecordArrival(2, 255, 31);
            PickSession.HostNoteReady(2, arrived: true);
            PickSession.HostNoteReady(2, arrived: true);
            PickSession.HostTick();
            PickSession.HostOnRequest(new PickRequest { sessionId = PickSession.SessionId, cardId = 1 }, 2);
            Assert.False(PickSession.Active);
            PickSession.HostNoteReady(2, arrived: true);
            PickSession.HostTick();
            Assert.Equal(2, Starts().Length);   // the duplicate triggers never produced a third session
        }

        [Fact]
        public void GhostAtTheFireIsNotCaughtUpButAnUnspawnedPlayerIs()
        {
            var ghost = Add(2, dead: true);
            AddRoomOnly(3);
            PickSession.OnRunStarted();
            PickSession.SnapshotCampfireEligibility(7);
            PickSession.RequestSession(1, 7);
            Assert.Equal(new[] { 1 }, Starts()[0].order);

            ghost.data.dead = false;                        // the fire's RPC revived them
            Add(3);                                         // the slow loader spawned
            PickSession.RecordArrival(2, 255, 1); PickSession.HostNoteReady(2, arrived: true);
            PickSession.RecordArrival(3, 255, 2); PickSession.HostNoteReady(3, arrived: true);
            PickSession.LocalPick(1);

            var starts = Starts();
            Assert.Equal(2, starts.Length);
            Assert.Equal(new[] { 3 }, starts[1].order);     // dead-at-lighting stays excluded
            Assert.Equal(7, (int)starts[1].segment);
            Assert.Equal(1, (int)starts[1].reason);
        }

        [Fact]
        public void PhoenixSkippedPlayerCountsAsDealtAndIsNotCaughtUp()
        {
            Add(2);
            Effects.SetFlag(2, Effects.FlagSkipNext, true);
            PickSession.OnRunStarted();
            PickSession.RecordArrival(1, 255, 0);
            PickSession.RecordArrival(2, 255, 0);
            PickSession.HostTick();
            var first = Starts()[0];
            Assert.Contains(2, first.order);                // in the order, with an empty hand: the pick was consumed
            PickSession.LocalPick(1);

            PickSession.HostNoteReady(2, arrived: true);
            PickSession.HostTick();
            Assert.Single(Starts());                        // no catch-up minted
        }

        [Fact]
        public void ModlessLateLoaderIsCaughtUpAfterTheGrace()
        {
            AddRoomOnly(2);
            DealOpeningToHostAlone();

            UnityEngine.Time.unscaledTime = 100f;
            Add(2);                                         // character registered, but no Arrival ever (no mod)
            PickSession.HostNoteReady(2, arrived: false);
            PickSession.HostTick();
            Assert.Single(Starts());                        // inside the grace: still waiting for their own Arrival

            UnityEngine.Time.unscaledTime = 116f;
            PickSession.HostTick();
            var starts = Starts();
            Assert.Equal(2, starts.Length);
            Assert.Equal(new[] { 2 }, starts[1].order);
        }

        [Fact]
        public void UntimedSessionsNeverMintAModlessCatchup()
        {
            Plugin.PickTimeoutSeconds.Value = 0f;
            AddRoomOnly(2);
            PickSession.OnRunStarted();
            PickSession.RecordArrival(1, 255, 0);
            PhotonNetwork.Time = 31;
            PickSession.HostTick();
            PickSession.LocalPick(1);
            Assert.Single(Starts());

            Add(2);
            PickSession.HostNoteReady(2, arrived: false);   // registration only - they may have no picker at all
            UnityEngine.Time.unscaledTime = 1000f;
            PickSession.HostTick();
            Assert.Single(Starts());                        // a stuck session is worse than a missed pick

            PickSession.RecordArrival(2, 255, 40);          // their Arrival proves the mod is loaded after all
            PickSession.HostNoteReady(2, arrived: true);
            PickSession.HostTick();
            Assert.Equal(2, Starts().Length);
        }

        [Fact]
        public void PromotedHostDealsCatchupsFromTheMirroredOrder()
        {
            NetSend.IsHost = false; NetSend.LocalActor = 2;
            Add(2, userId: "u2"); AddRoomOnly(1, "u1"); AddRoomOnly(3, "u3");
            PickSession.OnRunStarted();
            // the old host dealt itself the opening; this client only mirrors the start and the pick
            PickSession.OnStart(new PickSessionStart { sessionId = 777, reason = 0, order = new[] { 1 }, hands = new byte[] { 5, 6, 7 }, handSize = 3, timeoutSeconds = 60f, mode = 0, segment = 255 });
            PickSession.OnPick(new PickMade { sessionId = 777, actor = 1, cardId = 5 });
            Assert.False(PickSession.Active);

            PhotonNetwork.CurrentRoom.Players.Remove(1);    // the host left; we were promoted
            NetSend.IsHost = true;
            PickSession.RecordArrival(2, 255, 1);           // our own opening arrival, recorded locally at run start
            PickSession.HostPromotedSweep();
            PickSession.HostTick();

            var start = Assert.Single(NetSend.Sent.OfType<PickSessionStart>());
            Assert.Equal(new[] { 2 }, start.order);         // we were never dealt; the loading player 3 is not ready yet
            Assert.Equal(255, (int)start.segment);
        }

        // ------------------------------------------------------------------ rejoin

        [Fact]
        public void RejoinerIsNotRedealtTheOpening()
        {
            Add(2, userId: "steam-2");
            PickSession.OnRunStarted();
            PickSession.RecordArrival(1, 255, 0);
            PickSession.RecordArrival(2, 255, 0);
            PickSession.HostTick();
            Assert.Contains(2, Starts()[0].order);
            PickSession.LocalPick(1);
            PickSession.HostOnRequest(new PickRequest { sessionId = PickSession.SessionId, cardId = 1 }, 2);
            Assert.False(PickSession.Active);

            PhotonNetwork.CurrentRoom.Players.Remove(2);    // they disconnect...
            PlayerHandler.Characters.Remove(2);
            PickSession.OnPlayerLeft(2);
            Add(4, userId: "steam-2");                      // ...and rejoin with a fresh actor number
            PickSession.RecordArrival(4, 255, 60);
            PickSession.HostNoteReady(4, arrived: true);
            PickSession.HostTick();

            Assert.Single(Starts());                        // same account: the opening was already theirs
        }

        [Fact]
        public void TransferMovesCardsFlagsAndPenaltiesAndEmptiesTheSource()
        {
            Effects.CardsOf(2).AddRange(new byte[] { 9, 14 });
            Effects.SetFlag(2, Effects.FlagOfferOne, true);
            Effects.Penalties.TrySet(2, 0, 1);
            Effects.TransferActorState(2, 4);

            Assert.Equal(new byte[] { 9, 14 }, Effects.CardsOf(4));
            Assert.Equal(Effects.FlagOfferOne, Effects.Flags(4));
            Assert.Equal(new byte[] { 1 }, Effects.Penalties.Of(4));
            Assert.True(Effects.Owned.ContainsKey(2));      // the empty key stays: later syncs clear every client's copy
            Assert.Empty(Effects.CardsOf(2));
            Assert.Equal(0, (int)Effects.Flags(2));
            Assert.Empty(Effects.Penalties.Of(2));
        }

        [Fact]
        public void DirectoryFindsDepartedTwinsOldestFirst()
        {
            var dir = new ActorDirectory();
            dir.Record(2, "u"); dir.Record(4, "u"); dir.Record(3, "other");
            dir.Record(5, ""); dir.Record(6, "0");          // unusable ids are never recorded
            Assert.Null(dir.UserIdOf(5));
            Assert.Null(dir.UserIdOf(6));
            Assert.Equal(new[] { 2, 4 }, dir.DepartedTwins(7, "u", new[] { 3, 7 }));
            Assert.Empty(dir.DepartedTwins(7, "u", new[] { 2, 4, 7 }));   // both still in the room
            Assert.Empty(dir.DepartedTwins(7, null, new[] { 3 }));
        }

        [Fact]
        public void ActorReplacementRekeysEligibilityAndArrivals()
        {
            Add(2);
            PickSession.OnRunStarted();
            PickSession.SnapshotCampfireEligibility(9);     // actors 1 and 2 are eligible
            PickSession.RecordArrival(1, 9, 1.0);
            PickSession.RecordArrival(2, 9, 5.0);
            PhotonNetwork.CurrentRoom.Players.Remove(2); PlayerHandler.Characters.Remove(2);
            Add(4);
            PickSession.OnActorReplaced(2, 4);
            PickSession.RequestSession(1, 9);
            var start = Assert.Single(Starts());
            Assert.Contains(4, start.order);                // the rejoiner inherited the old actor's eligibility
            Assert.DoesNotContain(2, start.order);
            Assert.Equal(4, start.order[0]);                // and their arrival time (last to the fire, dealt first)
        }

        [Fact]
        public void ReviveArbiterTransferKeepsTheSpentCharge()
        {
            var arbiter = new ReviveArbiter();
            Assert.True(arbiter.TryGrant(2, 3, 9, 1));      // owner 2 spent their biome-3 charge
            arbiter.Transfer(2, 4);
            Assert.True(arbiter.UsedInBiome(4, 3));
            Assert.False(arbiter.TryGrant(4, 3, 8, 2));     // no refill through leave-and-rejoin
            Assert.False(arbiter.UsedInBiome(2, 3));
        }

        [Fact]
        public void SpawnLedgerTransferBlocksAReclaim()
        {
            var ledger = new SpawnLedger();
            ledger.FireLit(7);
            Assert.Equal(SpawnVerdict.Ok, ledger.TryClaim(2, 40, 7, true));
            ledger.Transfer(2, 4);
            Assert.Equal(SpawnVerdict.AlreadySpawned, ledger.TryClaim(4, 40, 7, true));
        }

        // ------------------------------------------------------------------ the mirror event (review findings)

        [Fact]
        public void MirrorDisarmsTheOpeningAfterARejoinersReset()
        {
            Add(2, userId: "u2");
            PickSession.OnRunStarted();
            PickSession.RecordArrival(1, 255, 0);
            PickSession.RecordArrival(2, 255, 0);
            PickSession.HostTick();
            PickSession.LocalPick(1);
            PickSession.HostOnRequest(new PickRequest { sessionId = PickSession.SessionId, cardId = 1 }, 2);
            var mirror = PickSession.BuildMirror();

            // the rejoiner's own run restart wipes everything; the host's mirror answer restores it
            PickSession.OnRunStarted();
            NetSend.Sent.Clear();
            PickSession.ApplyMirror(mirror);
            PickSession.RecordArrival(1, 255, 40);          // promoted later: own arrival re-recorded
            PhotonNetwork.Time = 100;                       // way past the 30s opening gate
            PickSession.HostTick();

            Assert.Empty(Starts());                         // no second opening for the whole room
        }

        [Fact]
        public void MirrorCarriesDeadAtFireSetsAcrossAReset()
        {
            var ghost = Add(2, dead: true);
            PickSession.OnRunStarted();
            PickSession.CapturePreLitEligibility();         // a chained fire: the host adopts under its minted key
            PickSession.AdoptPreLitEligibility(200);
            PickSession.RequestSession(1, 200);
            PickSession.LocalPick(1);
            var mirror = PickSession.BuildMirror();

            PickSession.OnRunStarted();                     // a promoted host that had reset (or joined late)
            NetSend.Sent.Clear();
            PickSession.ApplyMirror(mirror);
            ghost.data.dead = false;                        // revived since
            PickSession.RecordArrival(2, 255, 1);
            PickSession.HostNoteReady(2, arrived: true);
            PickSession.HostTick();

            Assert.Empty(Starts());                         // dead at that fire stays excluded after migration
        }

        [Fact]
        public void SessionDealtAfterTheTriggerStillOwesTheLateLoader()
        {
            AddRoomOnly(2);
            PickSession.OnRunStarted();
            PickSession.SnapshotCampfireEligibility(5);
            PickSession.RequestSession(1, 5);               // running
            PickSession.SnapshotCampfireEligibility(6);
            PickSession.RequestSession(1, 6);               // queued
            Add(2);                                         // the late loader spawns while 6 is still queued
            PickSession.RecordArrival(2, 255, 1);
            PickSession.HostNoteReady(2, arrived: true);
            PickSession.LocalPick(1);                       // ends 5, deals 6 (snapshot still excludes actor 2)
            PickSession.LocalPick(1);                       // ends 6: the drain owes both to actor 2

            var segs = Starts().Select(s => (int)s.segment).ToArray();
            Assert.Equal(new[] { 5, 6, 5 }, segs.Take(3).ToArray());
            Assert.Equal(new[] { 2 }, Starts()[2].order);
            PickSession.HostOnRequest(new PickRequest { sessionId = PickSession.SessionId, cardId = 1 }, 2);
            Assert.Equal(new[] { 6 }, Starts().Skip(3).Select(s => (int)s.segment).ToArray());
            Assert.Equal(new[] { 2 }, Starts()[3].order);
        }

        [Fact]
        public void DeadAtDrainStaysOwedUntilRevived()
        {
            AddRoomOnly(2);
            DealOpeningToHostAlone();
            var late = Add(2);
            PickSession.RecordArrival(2, 255, 31);
            PickSession.HostNoteReady(2, arrived: true);
            late.data.dead = true;                          // they fell before the drain got to them
            PickSession.HostTick();
            Assert.Single(Starts());                        // owed, not forfeited

            late.data.dead = false;                         // revived at the next fire
            UnityEngine.Time.unscaledTime = 1f;             // past the idle drain's 0.25s back-off
            PickSession.HostTick();
            Assert.Equal(2, Starts().Length);
            Assert.Equal(new[] { 2 }, Starts()[1].order);
        }

        [Fact]
        public void TheOpeningArrivalIsNotGraftedAcrossARejoin()
        {
            Plugin.PickTimeoutSeconds.Value = 0f;           // untimed: only a fresh Arrival may mint a catch-up
            AddRoomOnly(2, "u2");
            PickSession.OnRunStarted();
            PickSession.RecordArrival(1, 255, 0);
            PhotonNetwork.Time = 31;
            PickSession.HostTick();
            PickSession.LocalPick(1);
            Add(2, userId: "u2");
            PickSession.RecordArrival(2, 255, 32);          // their old stint proved the mod...

            PhotonNetwork.CurrentRoom.Players.Remove(2);    // ...but they crash out before being dealt
            PlayerHandler.Characters.Remove(2);
            PickSession.OnPlayerLeft(2);
            Add(4, userId: "u2");                           // and rejoin, possibly with a broken install
            PickSession.OnActorReplaced(2, 4);              // every client's roster tick detects the rejoin
            PickSession.HostNoteReady(4, arrived: false);
            UnityEngine.Time.unscaledTime = 1000f;
            PickSession.HostTick();

            Assert.Single(Starts());                        // no stale arrival, no unanswerable untimed session
        }

        [Fact]
        public void AlivenessAtTheFireButDeathAtTheDealStillEndsInACatchup()
        {
            var friend = Add(2);
            PickSession.OnRunStarted();
            PickSession.SnapshotCampfireEligibility(7);     // alive at lighting
            friend.data.dead = true;                        // dead when the session finally deals
            PickSession.RequestSession(1, 7);
            Assert.Equal(new[] { 1 }, Starts()[0].order);
            friend.data.dead = false;                       // revived; no trigger will ever re-fire for them
            UnityEngine.Time.unscaledTime = 50f;
            PickSession.LocalPick(1);                       // the deal-time audit owed them; the drain seeds the grace
            Assert.Single(Starts());
            UnityEngine.Time.unscaledTime = 66f;            // grace over (no Arrival: maybe modless)
            PickSession.HostTick();
            Assert.Equal(2, Starts().Length);
            Assert.Equal(new[] { 2 }, Starts()[1].order);
            Assert.Equal(7, (int)Starts()[1].segment);
        }

        // ------------------------------------------------------------------ stamina bar ceiling

        [Fact]
        public void BarGeometryIsCappedAtTheKnockoutCeiling()
        {
            Assert.Equal(1.3f, StatComposites.BarFrame(0.3f, 0f), 3);
            Assert.Equal(1.9f, StatComposites.BarFrame(1.4f, 0f), 3);          // stacked past the cap: clamp
            Assert.Equal(2.0f, StatComposites.BarFrame(1.4f, 2.0f), 3);        // statuses may still overflow it
            Assert.Equal(180f, StatComposites.BarSegmentsShift(0.3f, 600f), 2);
            Assert.Equal(540f, StatComposites.BarSegmentsShift(1.4f, 600f), 2);
            Assert.Equal(-570f, StatComposites.BarSegmentsShift(-2f, 600f), 2);
        }
    }
}
