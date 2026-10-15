using System.Linq;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using CampfireCards.Ui;
using Photon.Pun;
using Xunit;

namespace CampfireCards.Tests
{
    [Collection("Pick sessions")]
    public class CardAcquisitionTests
    {
        private static CardDef Card(string key) => CardTable.Cards.Single(c => c.Key == key);

        public CardAcquisitionTests()
        {
            PickSession.Reset(); LocalCards.Reset(); Effects.Penalties.Reset();
            Effects.Owned.Clear(); Effects.PickFlags.Clear(); PlayerHandler.Characters.Clear();
            NetSend.Sent.Clear(); NetSend.LocalActor = 1; NetSend.IsHost = true; NetSend.Deliver = true;
            CardSettings.Current = new CardSettings();
            Character.localCharacter = new Character();
            PlayerHandler.Characters[1] = Character.localCharacter;
            PhotonNetwork.CurrentRoom = new Photon.Realtime.Room();
            PhotonNetwork.CurrentRoom.Players[1] = new Photon.Realtime.Player { ActorNumber = 1 };
            UnityEngine.Time.time = 0f; UnityEngine.Time.unscaledTime = 0f;
            UnityEngine.Random.Next = 1; LocalCards.Biome = 0;
            Deck.BonusCards = new byte[] { 1, 2 }; Deck.Ready = true; CardPicker.Instance = null;
        }

        private static PickMade Choose(int session, byte card)
        {
            PickSession.OnStart(new PickSessionStart
            {
                sessionId = session, order = new[] { 1 }, hands = new[] { card }, handSize = 1,
                mode = (byte)PickMode.Simultaneous, timeoutSeconds = 0f, segment = 255
            });
            var accepted = new PickMade { sessionId = session, actor = 1, cardId = card };
            PickSession.OnPick(accepted);
            return accepted;
        }

        [Fact]
        public void DuplicateClownPicksEachPayOnceThroughTheRealAcquisitionAndBonusDealPath()
        {
            CardSettings.Current.AllowDuplicates = true;
            var clown = Card("DoubleDeal");
            var first = Choose(101, clown.Id);
            var instance = LocalCards.Get<Ability_DoubleDeal>();
            UnityEngine.Random.Next = 4;
            var second = Choose(102, clown.Id);

            Assert.Equal(2, Effects.CardsOf(1).Count(id => id == clown.Id));
            Assert.Same(instance, LocalCards.Get<Ability_DoubleDeal>());
            Assert.Equal(new byte[] { 1, 4 }, Effects.Penalties.Of(1));
            Assert.Equal(new byte[] { 0, 1 }, NetSend.Sent.OfType<CardPenalty>().Select(p => p.slot));
            Assert.Equal(2, NetSend.Sent.OfType<CardsGranted>().Count());
            Assert.Equal(6, Effects.CardsOf(1).Count); // two accepted Clowns, two bonus cards per pick

            PickSession.OnPick(first); PickSession.OnPick(second);
            foreach (var grant in NetSend.Sent.OfType<CardsGranted>().ToArray()) PickSession.OnCardsGranted(grant);
            foreach (var penalty in NetSend.Sent.OfType<CardPenalty>().ToArray())
                Effects.Penalties.TrySet(penalty.actor, penalty.slot, penalty.variant);
            Assert.Equal(6, Effects.CardsOf(1).Count);
            Assert.Equal(new byte[] { 1, 4 }, Effects.Penalties.Of(1));
            Assert.Equal(2, NetSend.Sent.OfType<CardPenalty>().Count());
        }

        [Fact]
        public void DefaultDuplicateRuleRefusesAnotherClownWithoutChargingIt()
        {
            var clown = Card("DoubleDeal");
            Assert.False(CardSettings.Current.AllowDuplicates);
            Assert.True(Effects.AddCard(1, clown.Id, true));
            Assert.False(Effects.AddCard(1, clown.Id, true));
            Assert.Single(Effects.CardsOf(1)); Assert.Single(Effects.Penalties.Of(1));
            Assert.Single(NetSend.Sent.OfType<CardPenalty>());
        }

        [Fact]
        public void SacrificeThenReacquireClownKeepsOldPenaltyAndPaysAnother()
        {
            var clown = Card("DoubleDeal");
            Choose(101, clown.Id);
            var instance = LocalCards.Get<Ability_DoubleDeal>();
            Effects.RemoveCard(1, clown.Id);
            Assert.Null(LocalCards.Get<Ability_DoubleDeal>());
            Assert.Equal(new byte[] { 1 }, Effects.Penalties.Of(1));
            UnityEngine.Random.Next = 3;
            Choose(102, clown.Id);
            Assert.Same(instance, LocalCards.Get<Ability_DoubleDeal>());
            Assert.Equal(new byte[] { 1, 3 }, Effects.Penalties.Of(1));
            Assert.Equal(2, NetSend.Sent.OfType<CardsGranted>().Count());
        }

        [Fact]
        public void RemovingOneDuplicateRetainsAbilityUntilTheLastCopyLeaves()
        {
            CardSettings.Current.AllowDuplicates = true;
            var clown = Card("DoubleDeal");
            Effects.AddCard(1, clown.Id, true); Effects.AddCard(1, clown.Id, true);
            var instance = LocalCards.Get<Ability_DoubleDeal>();
            Effects.RemoveCard(1, clown.Id);
            Assert.Same(instance, LocalCards.Get<Ability_DoubleDeal>());
            Assert.Single(Effects.CardsOf(1));
            Effects.RemoveCard(1, clown.Id);
            Assert.Null(LocalCards.Get<Ability_DoubleDeal>());
            Assert.Equal(2, Effects.Penalties.Count(1));
        }

        [Fact]
        public void DuplicateDishonorPaysCurseOnEachAcceptedBonusDeal()
        {
            CardSettings.Current.AllowDuplicates = true;
            var dishonor = Card("DoubleDown");
            Choose(101, dishonor.Id); var instance = LocalCards.Get<Ability_DoubleDown>();
            Choose(102, dishonor.Id);
            Assert.Same(instance, LocalCards.Get<Ability_DoubleDown>());
            Assert.Equal(.1f, CardStatus.Get(Character.localCharacter, CharacterAfflictions.STATUSTYPE.Curse), 5);
            Assert.Equal(2, NetSend.Sent.OfType<CardsGranted>().Count());
            Assert.Equal(Effects.FlagOfferOne, Effects.Flags(1));
            Effects.SetAll(1, Effects.CardsOf(1), Effects.Flags(1));
            Assert.Equal(.1f, CardStatus.Get(Character.localCharacter, CharacterAfflictions.STATUSTYPE.Curse), 5);
        }

        [Fact]
        public void DevTesterGrantRunsAcquisitionEffectsDealsBonusCardsAndMayRepeatAfterARemove()
        {
            var dishonor = Card("DoubleDown");

            PickSession.DevGrantSelf(dishonor.Id);
            Assert.Contains(dishonor.Id, Effects.CardsOf(1));
            Assert.NotNull(LocalCards.Get<Ability_DoubleDown>());
            Assert.Equal(.05f, CardStatus.Get(Character.localCharacter, CharacterAfflictions.STATUSTYPE.Curse), 5);
            // the card itself and its bonus deal, both as reason 10 (exempt from the repeated-grant check)
            Assert.Equal(2, NetSend.Sent.OfType<CardsGranted>().Count());
            Assert.All(NetSend.Sent.OfType<CardsGranted>(), g => Assert.Equal((byte)10, g.reason));
            Assert.Equal(3, Effects.CardsOf(1).Count);   // Double Down plus its two bonus cards

            // already owned (duplicates off): nothing is raised
            PickSession.DevGrantSelf(dishonor.Id);
            Assert.Equal(2, NetSend.Sent.OfType<CardsGranted>().Count());

            // removed and granted again in the same session: the repeated-grant check never eats a tester grant
            Effects.RemoveCard(1, dishonor.Id);
            Assert.DoesNotContain(dishonor.Id, Effects.CardsOf(1));
            PickSession.DevGrantSelf(dishonor.Id);
            Assert.Contains(dishonor.Id, Effects.CardsOf(1));
        }

        [Fact]
        public void DevTesterRefusesMythicsATeammateOwnsAndCardsStillOnOffer()
        {
            // a teammate owns the Mythic: nothing is raised (AddCard would refuse it on every client anyway)
            var mythic = Card("MountainsHeir");
            Effects.CardsOf(2).Add(mythic.Id);
            PickSession.DevGrantSelf(mythic.Id);
            Assert.Empty(NetSend.Sent.OfType<CardsGranted>());

            // a card in a teammate's dealt hand: granting it would consume that player's accepted pick
            var gecko = Card("GeckoGrip"); var sure = Card("Surefooted");
            PickSession.OnStart(new PickSessionStart
            {
                sessionId = 201, order = new[] { 1, 2 }, hands = new[] { gecko.Id, sure.Id }, handSize = 1,
                mode = (byte)PickMode.Simultaneous, timeoutSeconds = 0f, segment = 255
            });
            PickSession.DevGrantSelf(sure.Id);
            Assert.Empty(NetSend.Sent.OfType<CardsGranted>());

            // a card nobody is being offered is still grantable mid-session
            var ledge = Card("LedgeDancer");
            PickSession.DevGrantSelf(ledge.Id);
            Assert.Contains(ledge.Id, Effects.CardsOf(1));
        }

        [Fact]
        public void DevTesterIgnoresASecondClickUntilTheGrantEchoReturns()
        {
            NetSend.Deliver = false;   // online: the raised grant only lands after a server round trip
            var dishonor = Card("DoubleDown");
            PickSession.DevGrantSelf(dishonor.Id);
            PickSession.DevGrantSelf(dishonor.Id);   // double click inside the echo window
            // one main grant and one bonus deal, not two of each
            Assert.Equal(2, NetSend.Sent.OfType<CardsGranted>().Count());

            foreach (var g in NetSend.Sent.OfType<CardsGranted>().ToArray()) PickSession.OnCardsGranted(g);
            Assert.Contains(dishonor.Id, Effects.CardsOf(1));

            // after the echo, a re-click reports the duplicate instead of granting again
            PickSession.DevGrantSelf(dishonor.Id);
            Assert.Equal(2, NetSend.Sent.OfType<CardsGranted>().Count());
        }

        [Fact]
        public void DuplicateRecoveryAcquisitionDoesNotResetItsCooldown()
        {
            CardSettings.Current.AllowDuplicates = true;
            var wind = Card("SecondWind"); var me = Character.localCharacter;
            Effects.AddCard(1, wind.Id, true);
            var instance = LocalCards.Get<Ability_SecondWind>(); me.data.currentStamina = .1f;
            Assert.True(instance.Activate(me)); me.data.currentStamina = .1f;
            Effects.AddCard(1, wind.Id, true);
            Assert.Same(instance, LocalCards.Get<Ability_SecondWind>());
            Assert.False(instance.Activate(me));
            Effects.RemoveCard(1, wind.Id);
            Assert.Same(instance, LocalCards.Get<Ability_SecondWind>());
            Assert.False(instance.Activate(me));
        }

        [Fact]
        public void SyncReconcilesChangedCollectionWithoutRefillingOwnedChargesOrRerollingPenalties()
        {
            var wind = Card("SecondWind"); var purge = Card("EmergencyPurge"); var clown = Card("DoubleDeal");
            var me = Character.localCharacter;
            Effects.AddCard(1, wind.Id, true); Effects.AddCard(1, purge.Id, true); Effects.AddCard(1, clown.Id, true);
            var windAbility = LocalCards.Get<Ability_SecondWind>(); var purgeAbility = LocalCards.Get<Ability_EmergencyPurge>();
            var clownAbility = LocalCards.Get<Ability_DoubleDeal>();
            me.data.currentStamina = .1f; Assert.True(windAbility.Activate(me)); me.data.currentStamina = .1f;
            me.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .5f; Assert.True(purgeAbility.Activate(me));
            me.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .5f;
            Effects.SetAll(1, new[] { clown.Id, wind.Id, purge.Id, (byte)1 }, 1);
            Effects.SetAll(1, Effects.CardsOf(1), 1); // aliases current collection as a real replay could
            Assert.Same(windAbility, LocalCards.Get<Ability_SecondWind>());
            Assert.Same(purgeAbility, LocalCards.Get<Ability_EmergencyPurge>());
            Assert.Same(clownAbility, LocalCards.Get<Ability_DoubleDeal>());
            Assert.False(windAbility.Activate(me)); Assert.False(purgeAbility.Activate(me));
            Assert.Single(Effects.Penalties.Of(1)); Assert.Single(NetSend.Sent.OfType<CardPenalty>());
            Assert.Equal(1, Effects.Flags(1));
            Effects.SetAll(1, new[] { clown.Id }, 0);
            Assert.Null(LocalCards.Get<Ability_SecondWind>()); Assert.Null(LocalCards.Get<Ability_EmergencyPurge>());
            Assert.Same(clownAbility, LocalCards.Get<Ability_DoubleDeal>());
        }

        [Fact]
        public void RestoreCanCreateADrawAbilityWithoutReplayingItsAcquisitionCost()
        {
            var clown = Card("DoubleDeal"); var dishonor = Card("DoubleDown");
            Effects.SetAll(1, new[] { clown.Id, dishonor.Id }, 0);
            Assert.NotNull(LocalCards.Get<Ability_DoubleDeal>()); Assert.NotNull(LocalCards.Get<Ability_DoubleDown>());
            Assert.Empty(Effects.Penalties.Of(1)); Assert.Empty(NetSend.Sent);
            Assert.Equal(0f, CardStatus.Get(Character.localCharacter, CharacterAfflictions.STATUSTYPE.Curse));
            Effects.RemoveCard(1, dishonor.Id); Effects.RemoveCard(1, clown.Id);
            Assert.Empty(LocalCards.All);
        }

        [Fact]
        public void SacrificeAndReacquirePreserveRealRecoveryCooldownAndBiomeCharge()
        {
            var wind = Card("SecondWind"); var purge = Card("EmergencyPurge"); var me = Character.localCharacter;
            Effects.AddCard(1, wind.Id, true); Effects.AddCard(1, purge.Id, true);
            var windAbility = LocalCards.Get<Ability_SecondWind>(); var purgeAbility = LocalCards.Get<Ability_EmergencyPurge>();
            me.data.currentStamina = .1f; Assert.True(windAbility.Activate(me)); me.data.currentStamina = .1f;
            me.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .5f; Assert.True(purgeAbility.Activate(me));
            me.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .5f;
            Effects.RemoveCard(1, wind.Id); Effects.RemoveCard(1, purge.Id);
            Assert.Empty(LocalCards.Manual);
            UnityEngine.Time.time = 100f;
            Effects.AddCard(1, wind.Id, true); Effects.AddCard(1, purge.Id, true);
            Assert.Same(windAbility, LocalCards.Get<Ability_SecondWind>());
            Assert.Same(purgeAbility, LocalCards.Get<Ability_EmergencyPurge>());
            Assert.False(windAbility.Activate(me)); Assert.False(purgeAbility.Activate(me));
            LocalCards.OnBiomeStarted(me, 1);
            Assert.True(purgeAbility.Activate(me));
            UnityEngine.Time.time = 179.9f; Assert.False(windAbility.Activate(me));
            UnityEngine.Time.time = 180f; Assert.True(windAbility.Activate(me));
        }

        [Fact]
        public void RemovedAbilitiesStopTickingButKeepRunChargeUntilReset()
        {
            var probe = new CardDef { Id = 250, Ability = "RunChargeProbe", Name = "Run charge probe" };
            LocalCards.OnAcquired(probe, true);
            var first = LocalCards.Get<Ability_RunChargeProbe>();
            Assert.True(first.Activate(Character.localCharacter));
            LocalCards.Tick(Character.localCharacter, 1f); Assert.Equal(1, first.Ticks);
            LocalCards.OnRemoved(probe);
            Assert.Null(LocalCards.Get<Ability_RunChargeProbe>()); Assert.Equal(1, first.Cleanups);
            LocalCards.Tick(Character.localCharacter, 1f); Assert.Equal(1, first.Ticks);
            LocalCards.OnAcquired(probe, true);
            Assert.Same(first, LocalCards.Get<Ability_RunChargeProbe>());
            Assert.Equal(2, first.Acquisitions); Assert.False(first.Activate(Character.localCharacter));
            LocalCards.OnRemoved(probe); LocalCards.Reset(); LocalCards.OnAcquired(probe, true);
            var nextRun = LocalCards.Get<Ability_RunChargeProbe>();
            Assert.NotSame(first, nextRun); Assert.True(nextRun.Activate(Character.localCharacter));
        }

        [Fact]
        public void SyncReturningAPreviouslyRemovedCardPreservesItsSpentCharge()
        {
            var purge = Card("EmergencyPurge"); var me = Character.localCharacter;
            Effects.AddCard(1, purge.Id, true);
            var ability = LocalCards.Get<Ability_EmergencyPurge>();
            me.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .5f; Assert.True(ability.Activate(me));
            Effects.SetAll(1, new byte[0], 0);
            Assert.Null(LocalCards.Get<Ability_EmergencyPurge>());
            Effects.SetAll(1, new[] { purge.Id }, 0);
            Assert.Same(ability, LocalCards.Get<Ability_EmergencyPurge>());
            me.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .5f;
            Assert.False(ability.Activate(me));
        }
    }

    // Runs against the real LocalCards lifecycle and Charge, without depending on the Parachute's teleport boundary.
    public sealed class Ability_RunChargeProbe : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: false);
        public int Acquisitions, Cleanups, Ticks;
        public override void OnAcquired(Character me) => Acquisitions++;
        public override void OnRemoved(Character me) => Cleanups++;
        public override void Tick(Character me, float dt) => Ticks++;
        public override bool Activate(Character me) => _charge.TryUse(0);
    }
}
