using System;
using System.Linq;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using Xunit;

namespace CampfireCards.Tests
{
    public class PresentationTests
    {
        [Theory]
        [InlineData("V", "Press V to recover.")]
        [InlineData("F6", "Press F6 to recover.")]
        [InlineData("None", "Press [unbound] to recover.")]
        [InlineData("", "Press [unbound] to recover.")]
        public void DescriptionsUseTheConfiguredKey(string key, string expected) =>
            Assert.Equal(expected, CardCopy.WithKey("Press {key} to recover.", key));

        [Theory]
        [InlineData(300f, "5:00")]
        [InlineData(60.01f, "1:01")]
        [InlineData(.1f, "0:01")]
        [InlineData(-1f, "0:00")]
        public void CooldownsShowRoundedUpMinutesAndSeconds(float value, string expected) =>
            Assert.Equal(expected, CardCopy.Countdown(value));

        private static byte[] Header(uint width, uint height)
        {
            var bytes = new byte[33];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 }.CopyTo(bytes, 0);
            for (int i = 0; i < 4; i++)
            {
                bytes[16 + i] = (byte)(width >> (24 - 8 * i));
                bytes[20 + i] = (byte)(height >> (24 - 8 * i));
            }
            return bytes;
        }

        [Theory]
        [InlineData(1024u, 576u, true)]
        [InlineData(2048u, 2048u, true)]
        [InlineData(0u, 576u, false)]
        [InlineData(1024u, 2049u, false)]
        [InlineData(uint.MaxValue, 1u, false)]
        public void ImageDimensionsAreCheckedBeforeDecoding(uint width, uint height, bool supported) =>
            Assert.Equal(supported, PngImageInfo.Supported(Header(width, height)));

        [Fact]
        public void NonPngTruncatedAndOversizedImagesAreRejected()
        {
            Assert.False(PngImageInfo.Supported(null));
            Assert.False(PngImageInfo.Supported(new byte[32]));
            var bytes = Header(1024, 576);
            bytes[0] = 0;
            Assert.False(PngImageInfo.Supported(bytes));
            bytes = Header(1024, 576); bytes[12] = 0;
            Assert.False(PngImageInfo.Supported(bytes));
            bytes = Header(1024, 576); bytes[11] = 14;
            Assert.False(PngImageInfo.Supported(bytes));
            bytes = Header(1024, 576);
            Array.Resize(ref bytes, PngImageInfo.MaxBytes + 1);
            Assert.False(PngImageInfo.Supported(bytes));
        }
    }

    [Collection("Pick sessions")]
    public class RecoveryPresentationTests
    {
        public RecoveryPresentationTests()
        {
            CardSettings.Current = new CardSettings();
            LocalCards.Biome = 0;
            UnityEngine.Time.time = 0f;
        }

        private static Ability Recovery(string key) => AbilityRegistry.Create(CardTable.Cards.Single(c => c.Key == key));

        [Fact]
        public void NicReadinessMatchesActivationAndShowsRealCooldown()
        {
            var c = new Character();
            var a = Recovery("SecondWind");
            c.data.currentStamina = .2f;
            Assert.Contains("needs stamina below 20", a.Status(c).Text);
            c.data.currentStamina = .1f; c.MaxStamina = .1f;
            Assert.Contains("no room to recover", a.Status(c).Text);
            c.MaxStamina = 1f;
            Assert.EndsWith("ready", a.Status(c).Text); Assert.True(a.Status(c).Ready);
            Assert.True(a.Activate(c));
            Assert.Contains("cooldown 5:00", a.Status(c).Text);
            UnityEngine.Time.time = 299.5f;
            Assert.Contains("cooldown 0:01", a.Status(c).Text);
            UnityEngine.Time.time = 300f; c.data.currentStamina = .1f;
            Assert.EndsWith("ready", a.Status(c).Text); Assert.True(a.Status(c).Ready);
            c.data.dead = true;
            Assert.Contains("unavailable while down", a.Status(c).Text);
        }

        [Fact]
        public void CureAllShowsTriggerUntilUsableAndSpentUntilTheNextBiome()
        {
            var c = new Character();
            var a = Recovery("EmergencyPurge");
            c.Statuses[CharacterAfflictions.STATUSTYPE.Spores] = .34f;
            Assert.Contains("needs 35+ Poison, Spores or Drowsy", a.Status(c).Text);
            Assert.False(a.Activate(c));
            c.Statuses[CharacterAfflictions.STATUSTYPE.Spores] = .35f;
            Assert.EndsWith("ready", a.Status(c).Text); Assert.True(a.Status(c).Ready);
            Assert.True(a.Activate(c));
            Assert.Contains("used this biome", a.Status(c).Text);
            LocalCards.Biome++;
            Assert.Contains("needs 35+", a.Status(c).Text);
            c.Statuses[CharacterAfflictions.STATUSTYPE.Drowsy] = .5f;
            Assert.EndsWith("ready", a.Status(c).Text); Assert.True(a.Status(c).Ready);
        }

        [Fact]
        public void StatusRequirementsReflectCustomizedThresholds()
        {
            var nic = Recovery("SecondWind");
            var cure = Recovery("EmergencyPurge");
            int nicIndex = Array.FindIndex(CardTable.Cards, c => c.Key == "SecondWind");
            int cureIndex = Array.FindIndex(CardTable.Cards, c => c.Key == "EmergencyPurge");
            CardSettings.Current.Params[nicIndex]["threshold"] = .1f;
            CardSettings.Current.Params[cureIndex]["threshold"] = .4f;
            var c = new Character(); c.data.currentStamina = .15f;
            c.Statuses[CharacterAfflictions.STATUSTYPE.Poison] = .35f;
            Assert.Contains("below 10", nic.Status(c).Text);
            Assert.Contains("needs 40+", cure.Status(c).Text);
            Assert.False(nic.Activate(c));
            Assert.False(cure.Activate(c));
        }
    }
}
