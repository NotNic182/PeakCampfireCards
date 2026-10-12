using System.Collections.Generic;
using CampfireCards.Core;
using Xunit;

namespace CampfireCards.Tests
{
    public class MiniTenacityCureTests
    {
        private enum Status { Injury, Poison, Cold, Hot, Drowsy, Spores, Hunger, Curse, Weight, Petrify }

        private static readonly Status[] Eligible =
            { Status.Injury, Status.Poison, Status.Cold, Status.Hot, Status.Drowsy, Status.Spores };

        private static bool Record(HashSet<Status> cured, Status type, bool isMedical = true) =>
            CurativeSelection.RecordMedicalCure(cured, type, Status.Poison, Status.Spores, isMedical);

        private static bool Pick(HashSet<Status> cured, Dictionary<Status, float> current, out Status pick) =>
            CurativeSelection.TryHighestOther(Eligible, cured, t => current.TryGetValue(t, out var amount) ? amount : 0f, out pick);

        [Fact]
        public void PoisonMedicineTreatsSporesAndBonusChoosesRemainingInjury()
        {
            var cured = new HashSet<Status>();
            Assert.True(Record(cured, Status.Poison));
            var remaining = new Dictionary<Status, float>
                { [Status.Poison] = .2f, [Status.Spores] = .6f, [Status.Injury] = .3f };

            Assert.True(Pick(cured, remaining, out var pick));
            Assert.Equal(Status.Injury, pick);
            Assert.Contains(Status.Poison, cured);
            Assert.Contains(Status.Spores, cured);
        }

        [Fact]
        public void RemainingSporesAloneDoNotReceiveASecondCleanseFromPoisonMedicine()
        {
            var cured = new HashSet<Status>();
            Record(cured, Status.Poison);
            // Native Poison subtraction still treats Spores even when the current Poison value is zero.
            var remaining = new Dictionary<Status, float> { [Status.Spores] = .5f };

            Assert.False(Pick(cured, remaining, out _));
        }

        [Fact]
        public void PoisonMedicineStillAllowsTheHighestOtherStatus()
        {
            var cured = new HashSet<Status>();
            Record(cured, Status.Poison);
            var remaining = new Dictionary<Status, float>
                { [Status.Injury] = .2f, [Status.Cold] = .35f, [Status.Hot] = .1f, [Status.Drowsy] = .1f };

            Assert.True(Pick(cured, remaining, out var pick));
            Assert.Equal(Status.Cold, pick);
            Assert.Equal(2, cured.Count);
        }

        [Fact]
        public void MultipleMedicalActionsPreserveAllTreatedChannelsForOneUse()
        {
            var cured = new HashSet<Status>();
            Record(cured, Status.Injury);
            Record(cured, Status.Poison);
            var remaining = new Dictionary<Status, float>
                { [Status.Injury] = .4f, [Status.Spores] = .8f, [Status.Cold] = .2f };

            Assert.True(Pick(cured, remaining, out var pick));
            Assert.Equal(Status.Cold, pick);
            Assert.Equal(3, cured.Count);
        }

        [Fact]
        public void SporesOnlyMedicineDoesNotMarkPoisonAsAlreadyTreated()
        {
            var cured = new HashSet<Status>();
            Record(cured, Status.Spores);
            var remaining = new Dictionary<Status, float> { [Status.Poison] = .4f, [Status.Injury] = .3f };

            Assert.True(Pick(cured, remaining, out var pick));
            Assert.Equal(Status.Poison, pick);
            Assert.DoesNotContain(Status.Poison, cured);
        }

        [Fact]
        public void OrdinaryFoodDoesNotCreateMedicalCureMetadata()
        {
            var cured = new HashSet<Status>();

            Assert.False(Record(cured, Status.Poison, isMedical: false));
            Assert.False(Record(cured, Status.Injury, isMedical: false));
            Assert.Empty(cured);
        }

        [Fact]
        public void BonusPreservesTheAllowedStatusesAndOriginalTieOrder()
        {
            var remaining = new Dictionary<Status, float>
            {
                [Status.Curse] = 2f, [Status.Hunger] = 1f, [Status.Weight] = 1f, [Status.Petrify] = 1f,
                [Status.Injury] = .3f, [Status.Cold] = .3f
            };

            Assert.True(Pick(new HashSet<Status>(), remaining, out var pick));
            Assert.Equal(Status.Injury, pick);
        }

        [Fact]
        public void MissingCureMetadataStillAllowsAnEligibleHighestStatus()
        {
            var remaining = new Dictionary<Status, float> { [Status.Drowsy] = .4f, [Status.Injury] = .2f };

            Assert.True(Pick(null, remaining, out var pick));
            Assert.Equal(Status.Drowsy, pick);
        }
    }
}
