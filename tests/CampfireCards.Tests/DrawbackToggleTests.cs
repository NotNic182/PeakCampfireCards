using System.Linq;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using Photon.Pun;
using Xunit;

namespace CampfireCards.Tests
{
    /// <summary>The host option that strips every card of its drawback (Cards.Drawbacks = false).</summary>
    [Collection("Pick sessions")]
    public class DrawbackToggleTests
    {
        private static CardDef Card(string key) => CardTable.Cards.Single(c => c.Key == key);
        private static int Mod(CardDef c, Channel ch) => System.Array.FindIndex(c.Mods, m => m.Channel == ch);

        public DrawbackToggleTests()
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
        }

        [Fact]
        public void HurtingModifiersBecomeNoChangeAndBenefitsStay()
        {
            var s = new CardSettings { DrawbacksEnabled = false };
            var piton = Card("GeckoGrip");
            Assert.Equal(0.82f, s.ModValue(piton, Mod(piton, Channel.climbStamina)), 4);   // benefit untouched
            Assert.Equal(1f, s.ModValue(piton, Mod(piton, Channel.sprintStamina)), 4);     // drawback gone
            var tax = Card("CardioTax");
            Assert.Equal(-1, Mod(tax, Channel.maxStamina));                                // 0.8.0 removed the max-stamina drawback from the card itself
            Assert.Equal(1.35f, s.ModValue(tax, Mod(tax, Channel.sprintSpeed)), 4);        // its benefit stays
            var goat = Card("MountainGoat");
            Assert.Equal(1f, s.ModValue(goat, Mod(goat, Channel.fallInjury)), 4);
            Assert.Empty(Card("CurseBargain").Mods);                                       // 0.8.0: the Book of Bones is an item grant, nothing to toggle
            var heir = Card("MountainsHeir");
            for (int i = 0; i < heir.Mods.Length; i++) Assert.Equal(s.RawModValue(heir, i), s.ModValue(heir, i));
        }

        [Fact]
        public void DrawbackDropComposesWithDifficultyScaling()
        {
            var s = new CardSettings { DrawbacksEnabled = false, BenefitStrength = 0.6f };
            var daddy = Card("LedgeDancer");
            Assert.Equal(1.24f, s.ModValue(daddy, Mod(daddy, Channel.grabReach)), 4);      // benefit still scaled
            Assert.Equal(1f, s.ModValue(daddy, Mod(daddy, Channel.staminaUsage)), 4);      // drawback gone, not scaled
        }

        [Fact]
        public void PenaltyHelperRespectsTheToggle()
        {
            var me = Character.localCharacter;
            CardSettings.Current.DrawbacksEnabled = false;
            CardStatus.Penalty(me, CharacterAfflictions.STATUSTYPE.Curse, 0.1f);
            Assert.Equal(0f, CardStatus.Get(me, CharacterAfflictions.STATUSTYPE.Curse));
            CardSettings.Current.DrawbacksEnabled = true;
            CardStatus.Penalty(me, CharacterAfflictions.STATUSTYPE.Curse, 0.1f);
            Assert.Equal(0.1f, CardStatus.Get(me, CharacterAfflictions.STATUSTYPE.Curse), 4);
        }

        [Fact]
        public void DoubleDownAndClownLoseTheirPenaltiesButKeepTheirDraws()
        {
            CardSettings.Current.DrawbacksEnabled = false;
            Assert.True(Effects.AddCard(1, Card("DoubleDown").Id, live: true));
            Assert.Equal(0, Effects.Flags(1) & Effects.FlagOfferOne);                      // no one-card pick
            Assert.True(Effects.AddCard(1, Card("DoubleDeal").Id, live: true));
            Assert.Empty(Effects.Penalties.Of(1));                                         // no permanent penalty rolled
        }

        [Fact]
        public void TenCardOffersDealAndOpenEndToEnd()
        {
            // the draw gives 10 distinct cards, and the receive side accepts a 10-card session (it rejected > 5 once)
            var s = new CardSettings { OfferCount = 10 };
            var hand = CardDraw.Offer(new System.Random(7), s, new byte[0], Core.OfferRules.Campfire(10, solo: false, hasSacrificeTarget: false));
            Assert.Equal(10, hand.Count);
            Assert.Equal(10, hand.Distinct().Count());
            PickSession.OnStart(new PickSessionStart
            {
                sessionId = 70, reason = 1, order = new[] { 1 }, hands = hand.ToArray(), handSize = 10, timeoutSeconds = 0f,
            });
            Assert.True(PickSession.Active);
            PickSession.OnStart(new PickSessionStart
            {
                sessionId = 71, reason = 1, order = new[] { 1 }, hands = new byte[11], handSize = 11, timeoutSeconds = 0f,
            });
            Assert.NotEqual(71, PickSession.SessionId);   // past the shared cap stays malformed
        }

        [Fact]
        public void TheToggleIsPartOfTheLobbySettingsCheck()
        {
            var a = SettingsFingerprint.Of(new CardSettings { DrawbacksEnabled = true });
            var b = SettingsFingerprint.Of(new CardSettings { DrawbacksEnabled = false });
            Assert.NotEqual(a.Hash, b.Hash);
        }
    }
}
