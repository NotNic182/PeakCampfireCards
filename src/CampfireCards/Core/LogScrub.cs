using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CampfireCards.Core
{
    /// <summary>
    /// Removes personal traces from a log before it leaves the machine (ui.json BugReport). User-folder paths, the OS
    /// user name, player nicknames, Steam ids, IP addresses and e-mail addresses become neutral placeholders. Unity-free
    /// so the tests can pin every rule.
    /// </summary>
    public static class LogScrub
    {
        private static readonly Regex UserPath = new Regex(@"([A-Za-z]:[\\/](?:Users|home)[\\/]+)([^\\/\r\n""<>|]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HomePath = new Regex(@"(/(?:home|Users)/)([^/\r\n""<>|]+)", RegexOptions.Compiled);
        private static readonly Regex SteamId = new Regex(@"\b7656\d{13}\b", RegexOptions.Compiled);
        // an 18-digit Steam LOBBY id ("Hosting at 109775242744719551") would let a report reader join the live game
        private static readonly Regex LobbyId = new Regex(@"\b1097\d{14}\b", RegexOptions.Compiled);
        private static readonly Regex Ip = new Regex(@"\b(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]\d?)\.(?:25[0-5]|2[0-4]\d|1?\d?\d)\.(?:25[0-5]|2[0-4]\d|1?\d?\d)\.(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled);
        // loader banners are the one dotted quad worth keeping; everything else IP-shaped is scrubbed (privacy wins)
        private static readonly Regex KnownVersion = new Regex(@"(?i)\b(?:BepInEx|0Harmony|HarmonyX|MonoMod)[ /][vV]?\d{1,4}(?:\.\d{1,4}){1,3}", RegexOptions.Compiled);
        private static readonly Regex Email = new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

        /// <summary>
        /// The scrubbed text: paths keep their shape (C:\Users\user\...), the OS user becomes "user", and each player
        /// nickname becomes PlayerN - longest names first, so "NotNic the Second" is gone before "NotNic" matches inside it.
        /// The text and the names are first stripped of the zero-width spaces NetSend.NoTags inserts, so a nickname the
        /// mod logged tag-defused still matches its raw Photon form.
        /// </summary>
        public static string Scrub(string text, string osUserName, IEnumerable<string> playerNames)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = text.Replace("\u200B", "");
            text = UserPath.Replace(text, "$1user");
            text = HomePath.Replace(text, "$1user");
            if (!string.IsNullOrWhiteSpace(osUserName) && osUserName.Trim().Length >= 2)
                text = Regex.Replace(text, Regex.Escape(osUserName.Trim()), "user", RegexOptions.IgnoreCase);
            if (playerNames != null)
            {
                var names = new List<string>();
                foreach (var raw in playerNames)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var n = raw.Replace("\u200B", "").Trim();
                    if (n.Length >= 2 && !names.Contains(n)) names.Add(n);
                }
                var order = new Dictionary<string, int>();   // numbered by arrival so Player1 is stable per report
                foreach (var n in names) order[n] = order.Count + 1;
                names.Sort((a, b) => b.Length.CompareTo(a.Length));
                foreach (var n in names)
                    text = Regex.Replace(text, Regex.Escape(n), "Player" + order[n], RegexOptions.IgnoreCase);
            }
            text = SteamId.Replace(text, "[steamid]");
            text = LobbyId.Replace(text, "[lobbyid]");
            // keep the loader banner readable: park the known version strings, scrub every other dotted quad, put them back
            var parked = new List<string>();
            text = KnownVersion.Replace(text, m => { parked.Add(m.Value); return "\uE000" + (parked.Count - 1) + "\uE001"; });
            text = Ip.Replace(text, "[ip]");
            for (int i = 0; i < parked.Count; i++) text = text.Replace("\uE000" + i + "\uE001", parked[i]);
            text = Email.Replace(text, "[email]");
            return text;
        }

        /// <summary>The last maxChars characters, cut at the next line break so the first kept line is whole.</summary>
        public static string Tail(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
            int start = text.Length - maxChars;
            int nl = text.IndexOf('\n', start);
            if (nl >= 0 && nl + 1 < text.Length) start = nl + 1;
            return text.Substring(start);
        }
    }
}
