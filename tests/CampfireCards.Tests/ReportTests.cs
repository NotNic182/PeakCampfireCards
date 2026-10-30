using System.Collections.Generic;
using System.Text.Json;
using CampfireCards.Core;
using Xunit;

namespace CampfireCards.Tests
{
    /// <summary>The bug report leaves the machine clean (LogScrub) and in a shape the server can parse (ReportJson).</summary>
    public class ReportTests
    {
        // ------------------------------------------------------------------ scrubbing
        [Fact]
        public void UserFolderPathsLoseTheAccountName()
        {
            string log = @"[Info] Loaded C:\Users\Nicholas\AppData\Roaming\Gale\mod.dll and c:/users/Nicholas/saves/run.json";
            string outp = LogScrub.Scrub(log, null, null);
            Assert.DoesNotContain("Nicholas", outp);
            Assert.Contains(@"C:\Users\user\AppData", outp);
            Assert.Contains("c:/users/user/saves", outp);
        }

        [Fact]
        public void UnixHomePathsLoseTheAccountName()
        {
            string outp = LogScrub.Scrub("path /home/nicholas/.config and /Users/nicholas/Library", null, null);
            Assert.DoesNotContain("nicholas", outp);
            Assert.Contains("/home/user/.config", outp);
            Assert.Contains("/Users/user/Library", outp);
        }

        [Fact]
        public void OsUserNameIsScrubbedCaseInsensitively()
        {
            string outp = LogScrub.Scrub("profile of NICHOLAS loaded; nicholas wrote a file", "Nicholas", null);
            Assert.DoesNotContain("NICHOLAS", outp);
            Assert.DoesNotContain("nicholas", outp);
        }

        [Fact]
        public void PlayerNamesBecomeNumberedPlayersLongestFirst()
        {
            // the longer name must be replaced before the shorter one that is its prefix
            var names = new List<string> { "Nic", "Nic the Second" };
            string outp = LogScrub.Scrub("Nic the Second helped Nic up the wall", null, names);
            Assert.Equal("Player2 helped Player1 up the wall", outp);
        }

        [Fact]
        public void SteamIdsAndEmailsAreScrubbed()
        {
            string outp = LogScrub.Scrub("user 76561198000000001 wrote to someone@example.com", null, null);
            Assert.Contains("[steamid]", outp);
            Assert.Contains("[email]", outp);
            Assert.DoesNotContain("7656119", outp);
            Assert.DoesNotContain("example.com", outp);
        }

        [Fact]
        public void IpAddressesAreScrubbedButDottedVersionsSurvive()
        {
            string outp = LogScrub.Scrub("BepInEx 5.4.23.2 connected via 192.168.1.23 and 25.14.3.9", null, null);
            Assert.Contains("BepInEx 5.4.23.2", outp);   // the loader banner is parked and restored around the IP scrub
            Assert.DoesNotContain("192.168.1.23", outp);
            Assert.DoesNotContain("25.14.3.9", outp);
        }

        [Fact]
        public void LowFirstOctetAddressesAreScrubbedToo()
        {
            // 1.0.0.0/8 - 9.0.0.0/8 are real allocated ranges; only the known loader banners are exempt
            string outp = LogScrub.Scrub("peer at 5.146.3.9, dns 1.1.1.1, assembly 1.0.0.0", null, null);
            Assert.DoesNotContain("5.146.3.9", outp);
            Assert.DoesNotContain("1.1.1.1", outp);
            Assert.DoesNotContain("1.0.0.0", outp);   // over-scrubbing a bare dotted quad is the accepted cost
        }

        [Fact]
        public void SteamLobbyIdsAreScrubbed()
        {
            string outp = LogScrub.Scrub("[Info :HostLog] Hosting at 109775242744719551", null, null);
            Assert.DoesNotContain("109775242744719551", outp);
            Assert.Contains("[lobbyid]", outp);
        }

        [Fact]
        public void TagDefusedNicknamesStillMatchTheRawName()
        {
            // NetSend.NoTags inserts U+200B after each '<' before a name is logged; the scrub normalizes both sides
            var names = new List<string> { "<b>Shadow</b>" };
            string logged = "<​b>Shadow</​b> picked a card";
            string outp = LogScrub.Scrub(logged, null, names);
            Assert.DoesNotContain("Shadow", outp);
            Assert.StartsWith("Player1", outp);
        }

        [Fact]
        public void TailKeepsWholeLinesWithinTheBudget()
        {
            string text = "first line\nsecond line\nthird line";
            string tail = LogScrub.Tail(text, 15);
            Assert.True(tail.Length <= 15);
            Assert.Equal("third line", tail);
            Assert.Equal(text, LogScrub.Tail(text, 1000));
        }

        // ------------------------------------------------------------------ wire format
        private static Report Sample(string log = null)
        {
            return new Report
            {
                ModVersion = "0.9.3",
                Balance = 17,
                GameVersion = "1.0",
                Scene = "Level_2",
                UtcTime = "2026-10-29T12:00:00.0000000Z",
                InstallId = "abc123",
                Players = 3,
                IsHost = true,
                DrawbacksEnabled = true,
                BenefitStrength = 0.85f,
                Categories = new List<string> { "effect", "lag" },
                Comment = "line one\nwith \"quotes\" and a \\ backslash",
                Deck = new List<int> { 2, 49 },
                TeamDecks = new List<List<int>> { new List<int> { 7 }, new List<int>() },
                Log = log,
            };
        }

        [Fact]
        public void ReportJsonParsesBackWithEveryField()
        {
            using var doc = JsonDocument.Parse(ReportJson.Build(Sample(log: "[Info] something\thappened")));
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("v").GetInt32());
            Assert.Equal("0.9.3", root.GetProperty("mod").GetString());
            Assert.Equal(17, root.GetProperty("balance").GetInt32());
            Assert.Equal("Level_2", root.GetProperty("scene").GetString());
            Assert.Equal(3, root.GetProperty("players").GetInt32());
            Assert.True(root.GetProperty("host").GetBoolean());
            Assert.True(root.GetProperty("drawbacks").GetBoolean());
            Assert.Equal(0.85, root.GetProperty("strength").GetDouble(), 3);
            Assert.Equal(2, root.GetProperty("categories").GetArrayLength());
            Assert.Equal("line one\nwith \"quotes\" and a \\ backslash", root.GetProperty("comment").GetString());
            Assert.Equal(2, root.GetProperty("deck").GetArrayLength());
            Assert.Equal(49, root.GetProperty("deck")[1].GetInt32());
            Assert.Equal(2, root.GetProperty("teamDecks").GetArrayLength());
            Assert.Equal(7, root.GetProperty("teamDecks")[0][0].GetInt32());
            Assert.Equal("[Info] something\thappened", root.GetProperty("log").GetString());
        }

        [Fact]
        public void ReportJsonWritesNullLogWhenNotAttached()
        {
            using var doc = JsonDocument.Parse(ReportJson.Build(Sample(log: null)));
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("log").ValueKind);
        }

        [Fact]
        public void ControlCharactersAreEscaped()
        {
            var r = Sample();
            r.Comment = "beep\u0007boop";
            using var doc = JsonDocument.Parse(ReportJson.Build(r));
            Assert.Equal("beep\u0007boop", doc.RootElement.GetProperty("comment").GetString());
        }
    }
}
