using System;
using System.Linq;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Net;
using CampfireCards.Ui;
using Photon.Pun;
using Xunit;

namespace CampfireCards.Tests
{
    [CollectionDefinition("Pick sessions", DisableParallelization = true)]
    public class PickSessionCollection { }

    [Collection("Pick sessions")]
    public class PickSessionTests
    {
        private static Character Add(int actor, bool dead = false)
        {
            var c = new Character(); c.data.dead = dead;
            PlayerHandler.Characters[actor] = c;
            PhotonNetwork.CurrentRoom.Players[actor] = new Photon.Realtime.Player { ActorNumber = actor };
            if (actor == NetSend.LocalActor) Character.localCharacter = c;
            return c;
        }

        public PickSessionTests()
        {
            PickSession.Reset();
            LocalCards.Reset(); Effects.Penalties.Reset(); Deck.BonusCards = new byte[0];
            PlayerHandler.Characters.Clear(); Effects.Owned.Clear(); Effects.PickFlags.Clear();
            NetSend.Sent.Clear(); NetSend.IsHost = true; NetSend.LocalActor = 1; NetSend.Deliver = true;
            PhotonNetwork.CurrentRoom = new Photon.Realtime.Room(); PhotonNetwork.Time = 0;
            UnityEngine.Time.unscaledTime = 0; CardPicker.Instance = null;
            Plugin.PickTimeoutSeconds = new Setting<float>(60f);
            Plugin.PickModeSetting = new Setting<PickMode>(PickMode.Simultaneous);
            CardSettings.Current = new CardSettings(); Deck.Ready = true;
            Add(1);
        }

        private static void Start(PickMode mode, float timeout)
        {
            Plugin.PickModeSetting.Value = mode;
            Plugin.PickTimeoutSeconds.Value = timeout;
            PickSession.OnRunStarted();
            PickSession.RequestSession(1, 1);
        }

        [Theory]
        [InlineData(PickMode.Simultaneous)]
        [InlineData(PickMode.Turns)]
        public void GhostAtFireGetsNoOfferEvenIfRevivedBeforeDeal(PickMode mode)
        {
            var ghost = Add(2, dead: true);
            Effects.SetFlag(2, Effects.FlagOfferOne, true);
            Plugin.PickModeSetting.Value = mode;
            PickSession.SnapshotCampfireEligibility(1);
            ghost.data.dead = false;
            PickSession.RequestSession(1, 1);
            var start = Assert.Single(NetSend.Sent.OfType<PickSessionStart>());
            Assert.Equal(new[] { 1 }, start.order);
            Assert.Empty(PickSession.Turns.HandOf(2));
            Assert.Equal(Effects.FlagOfferOne, Effects.Flags(2)); // no pick means no consumed penalty
        }

        [Fact]
        public void QueuedFirePreservesGhostEligibilityAcrossRevival()
        {
            Start(PickMode.Simultaneous, 0);
            var ghost = Add(2, dead: true);
            PickSession.SnapshotCampfireEligibility(2);
            PickSession.RequestSession(1, 2);
            ghost.data.dead = false;
            PickSession.LocalPick(1);
            var starts = NetSend.Sent.OfType<PickSessionStart>().ToArray();
            Assert.Equal(2, starts.Length);
            Assert.Equal(new[] { 1 }, starts[1].order);
        }

        [Theory]
        [InlineData(PickMode.Simultaneous)]
        [InlineData(PickMode.Turns)]
        public void UntimedPickWaitsForManualChoicePastTheOldDeadline(PickMode mode)
        {
            Start(mode, 0f);
            Assert.Equal(0f, CardPicker.Instance.Timeout);
            UnityEngine.Time.unscaledTime = 3600f;
            PickSession.HostTick();
            Assert.True(PickSession.Active);
            Assert.Empty(NetSend.Sent.OfType<PickMade>());
            Assert.Empty(NetSend.Sent.OfType<SessionEnd>());
            PickSession.LocalPick(2);
            Assert.Contains((byte)2, Effects.CardsOf(1));
            Assert.False(PickSession.Active);
        }

        [Fact]
        public void HostZeroTimeoutOverridesClientsPositiveTimeout()
        {
            NetSend.IsHost = false;
            Plugin.PickTimeoutSeconds.Value = 10f;
            Add(2);
            PickSession.OnStart(new PickSessionStart { sessionId = 50, reason = 1, order = new[] { 2 }, hands = new byte[] { 1, 2, 3 }, handSize = 3, timeoutSeconds = 0f });
            Assert.Equal(0f, PickSession.TimeoutSeconds);
            Assert.DoesNotContain("s", Hud.Waiting.Split("Player 2")[1]);
        }

        [Theory]
        [InlineData(PickMode.Simultaneous)]
        [InlineData(PickMode.Turns)]
        public void TimedPickGivesTheFullSixtySeconds(PickMode mode)
        {
            Start(mode, 60f);
            UnityEngine.Time.unscaledTime = 59.99f; PickSession.HostTick();
            Assert.Empty(NetSend.Sent.OfType<PickMade>());
            Assert.True(PickSession.Active);
            UnityEngine.Time.unscaledTime = 60f; PickSession.HostTick();
            Assert.True(Assert.Single(NetSend.Sent.OfType<PickMade>()).auto);
            Assert.Single(Effects.CardsOf(1));
            Assert.False(PickSession.Active);
        }

        [Theory]
        [InlineData(PickMode.Simultaneous, 0f)]
        [InlineData(PickMode.Turns, 0f)]
        [InlineData(PickMode.Simultaneous, 60f)]
        [InlineData(PickMode.Turns, 60f)]
        public void DeathBeforeChoiceSkipsWithoutGrantingOrBlockingTheNextScout(PickMode mode, float timeout)
        {
            var other = Add(2);
            Start(mode, timeout);
            Character.localCharacter.data.dead = true;
            PickSession.HostTick();
            Assert.Equal(1, Assert.Single(NetSend.Sent.OfType<PickSkipped>()).actor);
            Assert.Empty(Effects.CardsOf(1));
            Assert.False(CardPicker.Instance.isOpen);
            Assert.True(PickSession.Turns.CanChoose(2));
            PickSession.HostOnRequest(new PickRequest { sessionId = PickSession.SessionId, cardId = 1 }, 2);
            Assert.Single(Effects.CardsOf(2));
            Assert.False(PickSession.Active);
        }

        [Fact]
        public void HostRejectsGhostClickBeforeTheNextTick()
        {
            var other = Add(2);
            Start(PickMode.Simultaneous, 0f);
            other.data.dead = true;
            PickSession.HostOnRequest(new PickRequest { sessionId = PickSession.SessionId, cardId = 1 }, 2);
            Assert.Empty(NetSend.Sent.OfType<PickMade>());
            Assert.Single(NetSend.Sent.OfType<PickSkipped>());
            Assert.Empty(Effects.CardsOf(2));
        }

        [Fact]
        public void HostSkipAdvancesWithoutWaitingForEchoAndNextTurnGetsItsFullTime()
        {
            Add(2); Start(PickMode.Turns, 60f);
            NetSend.Deliver = false;
            UnityEngine.Time.unscaledTime = 20f;
            Character.localCharacter.data.dead = true;
            PickSession.HostTick();
            Assert.Equal(2, PickSession.CurrentActor);
            Assert.Equal(20f, PickSession.TurnStartTime);
            UnityEngine.Time.unscaledTime = 60f; PickSession.HostTick();
            Assert.Empty(NetSend.Sent.OfType<PickMade>());
            UnityEngine.Time.unscaledTime = 80f; PickSession.HostTick();
            Assert.Equal(2, Assert.Single(NetSend.Sent.OfType<PickMade>()).actor);
        }

        [Fact]
        public void GhostNeverGetsATimeoutCard()
        {
            var other = Add(2);
            Start(PickMode.Simultaneous, 60f);
            other.data.dead = true;
            UnityEngine.Time.unscaledTime = 100f; PickSession.HostTick();
            Assert.Empty(Effects.CardsOf(2));
            Assert.Equal(1, Assert.Single(NetSend.Sent.OfType<PickMade>()).actor);
        }

        [Fact]
        public void AllGhostsEndSessionWithoutCardsEvenWithoutATimer()
        {
            Start(PickMode.Simultaneous, 0f);
            Character.localCharacter.data.dead = true;
            PickSession.HostTick();
            Assert.False(PickSession.Active);
            Assert.Empty(NetSend.Sent.OfType<PickMade>());
        }

        [Fact]
        public void LocalGhostCannotSendAPickAndItsWindowCloses()
        {
            Start(PickMode.Simultaneous, 0f);
            NetSend.IsHost = false;
            Character.localCharacter.data.dead = true;
            PickSession.LocalPick(1);
            Assert.False(CardPicker.Instance.isOpen);
            Assert.Empty(NetSend.Sent.OfType<PickRequest>());
        }

        [Fact]
        public void DownedScoutsAreAliveAndStayEligible()
        {
            Character.localCharacter.data.passedOut = true;
            Assert.True(PickSession.CanReceiveCard(1));
            Add(2, dead: true);
            Assert.False(PickSession.CanReceiveCard(2));
            Assert.False(PickSession.CanReceiveCard(99));
        }

        [Fact]
        public void SkipReplayAndStaleSkipCannotRemoveAnotherSessionsOffer()
        {
            Add(2); Start(PickMode.Turns, 0f);
            int id = PickSession.SessionId;
            PickSession.OnSkipped(new PickSkipped { sessionId = id - 1, actor = 1 });
            Assert.Equal(1, PickSession.CurrentActor);
            var skip = new PickSkipped { sessionId = id, actor = 1 };
            PickSession.OnSkipped(skip); PickSession.OnSkipped(skip);
            Assert.Equal(2, PickSession.CurrentActor);
            Assert.Equal(PickVerdict.NotInSession, PickSession.Turns.CheckRequest(id, 1, 1));
        }

        [Fact]
        public void InvalidTimersAreFiniteAndZeroSurvivesTheWire()
        {
            Assert.Equal(60f, PickTiming.Normalize(float.NaN));
            Assert.Equal(60f, PickTiming.Normalize(float.PositiveInfinity));
            Assert.Equal(0f, PickTiming.Normalize(-1f));
            Assert.Equal(5f, PickTiming.Normalize(1f));
            var m = PickSessionStart.Read(new PickSessionStart { timeoutSeconds = 0f }.Write());
            Assert.Equal(0f, m.timeoutSeconds);
            var skip = PickSkipped.Read(new PickSkipped { sessionId = 22, actor = 2 }.Write());
            Assert.Equal(22, skip.sessionId); Assert.Equal(2, skip.actor);
        }
    }
}
