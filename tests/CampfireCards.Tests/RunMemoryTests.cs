using System.Collections.Generic;
using CampfireCards.Core;
using Xunit;

namespace CampfireCards.Tests
{
    public class RunMemoryTests
    {
        private static Dictionary<string, string[]> Entries(params (string name, string[] keys)[] rows)
        {
            var d = new Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var (name, keys) in rows) d[name] = keys;
            return d;
        }

        [Fact]
        public void SerializeAndParseRoundTripWithRunId()
        {
            var text = RunMemory.Serialize(1234, Entries(
                ("NotNic", new[] { "GeckoGrip", "SecondWind" }),
                ("Swipity", new[] { "PackRat" })));
            Assert.Equal(1234, RunMemory.RunIdOf(text));
            var back = RunMemory.Parse(text);
            Assert.Equal(new[] { "GeckoGrip", "SecondWind" }, back["NotNic"]);
            Assert.Equal(new[] { "PackRat" }, back["SWIPITY"]);   // names match ignoring case
        }

        [Fact]
        public void NamesAreCleanedAndEmptyOrBlankLinesAreIgnored()
        {
            Assert.Equal("A  B", RunMemory.CleanName(" A \tB\r\n"));
            // a leading '#' would read back as a comment line, so it never enters the file or the lookup
            Assert.Equal("1 Scout", RunMemory.CleanName("## 1 Scout"));
            var hashed = RunMemory.Parse(RunMemory.Serialize(3, Entries((RunMemory.CleanName("#1 Scout"), new[] { "HOLD" }))));
            Assert.Equal(new[] { "HOLD" }, hashed["1 Scout"]);
            Assert.Empty(RunMemory.Parse(""));
            Assert.Empty(RunMemory.Parse("# run 5\n\n   \n"));
            Assert.Equal(0, RunMemory.RunIdOf(""));
            Assert.Equal(0, RunMemory.RunIdOf("NotAHeader\tGeckoGrip"));
            // a serialized entry with no cards is dropped rather than written as an empty line
            var text = RunMemory.Serialize(7, Entries(("Ghost", new string[0]), ("NotNic", new[] { "HOLD" })));
            var back = RunMemory.Parse(text);
            Assert.Single(back);
            Assert.True(back.ContainsKey("NotNic"));
        }

        [Fact]
        public void LaterLinesForTheSameNameReplaceEarlierOnes()
        {
            var back = RunMemory.Parse("# run 9\nNotNic\tGeckoGrip\nNotNic\tSecondWind, HOLD\n");
            Assert.Equal(new[] { "SecondWind", "HOLD" }, back["NotNic"]);
        }

        [Fact]
        public void MergeUnionsPerNameWithoutDroppingAnyone()
        {
            var lastRun = Entries(("Swipity", new[] { "PackRat", "LoadedDice" }), ("TechTara", new[] { "DeepLungs" }));
            var current = Entries(("Swipity", new[] { "LoadedDice", "GeckoGrip" }), ("NotNic", new[] { "HOLD" }));
            var merged = RunMemory.Merge(lastRun, current);
            Assert.Equal(new[] { "PackRat", "LoadedDice", "GeckoGrip" }, merged["Swipity"]);
            Assert.Equal(new[] { "DeepLungs" }, merged["TechTara"]);
            Assert.Equal(new[] { "HOLD" }, merged["NotNic"]);
        }

        [Fact]
        public void MergeKeepsDuplicateCopiesFromEitherFile()
        {
            // two copies in the newer file beat one in the older: both come back (Missing is a multiset diff)
            var merged = RunMemory.Merge(
                Entries(("NotNic", new[] { "GeckoGrip" })),
                Entries(("NotNic", new[] { "GeckoGrip", "GeckoGrip" })));
            Assert.Equal(new[] { "GeckoGrip", "GeckoGrip" }, merged["NotNic"]);
            // and two copies in the older file are not tripled by the newer one's single copy
            merged = RunMemory.Merge(
                Entries(("NotNic", new[] { "GeckoGrip", "GeckoGrip" })),
                Entries(("NotNic", new[] { "GeckoGrip", "HOLD" })));
            Assert.Equal(new[] { "GeckoGrip", "GeckoGrip", "HOLD" }, merged["NotNic"]);
        }

        [Fact]
        public void MissingCountsCopiesSoOwnedDuplicatesAreNotRegranted()
        {
            // saved two copies, owns one: exactly one copy comes back
            Assert.Equal(new List<string> { "GeckoGrip" },
                RunMemory.Missing(new[] { "GeckoGrip", "GeckoGrip" }, new[] { "geckogrip" }));
            // owns everything saved: nothing to grant
            Assert.Empty(RunMemory.Missing(new[] { "HOLD" }, new[] { "HOLD", "SecondWind" }));
            // fresh rejoin with no cards: everything comes back in saved order
            Assert.Equal(new List<string> { "HOLD", "SecondWind" },
                RunMemory.Missing(new[] { "HOLD", "SecondWind" }, new string[0]));
        }
    }
}
