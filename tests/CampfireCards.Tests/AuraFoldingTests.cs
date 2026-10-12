using System.Linq;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using UnityEngine;
using Xunit;

namespace CampfireCards.Tests
{
    [Collection("Pick sessions")]
    public class AuraFoldingTests
    {
        private static CardDef Card(string key) => CardTable.Cards.Single(c => c.Key == key);

        public AuraFoldingTests()
        {
            LocalCards.Reset(); Effects.Owned.Clear(); PlayerHandler.Characters.Clear();
            CardSettings.Current = new CardSettings(); NetSend.LocalActor = 1;
            Character.localCharacter = new Character();
            PlayerHandler.Characters[1] = Character.localCharacter;
        }

        private static Character Owner(int actor, float distance, byte card)
        {
            var scout = new Character { Center = new Vector3(distance, 0f, 0f) };
            PlayerHandler.Characters[actor] = scout;
            Effects.CardsOf(actor).Add(card);
            return scout;
        }

        [Fact]
        public void SharedBurdenChoosesNearestOwnerDespiteDictionaryOrderAndUpdatesAsScoutsMove()
        {
            var shared = Card("SharedBurden");
            var farther = Owner(2, 5f, shared.Id); var nearer = Owner(3, 2f, shared.Id);
            Effects.Recompute(Character.localCharacter);
            Assert.Equal(3, Effects.SharedBurdenOwner);
            Assert.Equal(.7f, Effects.SharedBurdenFactor, 5);
            Assert.Equal(1f, Effects.Local[Channel.allInjury]); // applied once separately by the status hook
            farther.Center = new Vector3(1f, 0f, 0f); nearer.Center = new Vector3(4f, 0f, 0f);
            Effects.Recompute(Character.localCharacter);
            Assert.Equal(2, Effects.SharedBurdenOwner); Assert.Equal(.7f, Effects.SharedBurdenFactor, 5);
        }

        [Fact]
        public void SharedBurdenIgnoresDeadOrOutOfRangeOwnersAndClearsWhenNobodyCoversYou()
        {
            var shared = Card("SharedBurden");
            var first = Owner(2, 1f, shared.Id); var second = Owner(3, 3f, shared.Id);
            first.data.dead = true;
            Effects.Recompute(Character.localCharacter); Assert.Equal(3, Effects.SharedBurdenOwner);
            second.Center = new Vector3(50f, 0f, 0f);
            Effects.Recompute(Character.localCharacter);
            Assert.Equal(int.MinValue, Effects.SharedBurdenOwner); Assert.Equal(1f, Effects.SharedBurdenFactor);
        }

        [Fact]
        public void OrdinaryIdenticalAurasStillCountOnceWhileDistinctAurasCombine()
        {
            var eternal = Card("EternalCampfire");
            Owner(2, 1f, eternal.Id); Owner(3, 2f, eternal.Id);
            Effects.Recompute(Character.localCharacter);
            var injury = eternal.Mods.Single(m => m.Channel == Channel.allInjury);
            Assert.Equal(injury.Value, Effects.Local[Channel.allInjury]);
            Effects.CardsOf(2).Add(Card("CampChoir").Id);
            Effects.CardsOf(3).Add(Card("CampChoir").Id);
            Effects.Recompute(Character.localCharacter);
            Assert.Equal(1.12f, Effects.Local[Channel.regenRate], 5);
            Assert.Equal(injury.Value, Effects.Local[Channel.allInjury]);
        }
    }
}
