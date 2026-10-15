using System;
using System.Collections.Generic;
using System.Text;

namespace CampfireCards.Core
{
    /// <summary>
    /// The run-memory text format: every player's cards by player NAME, so the host can give cards back after a crash
    /// or a rejoin (names survive reconnects; actor numbers do not). One header line names the run, then one line per
    /// player: name, a tab, the card keys comma separated. Pure string logic; Cards/RunMemoryFile does the game side.
    /// </summary>
    public static class RunMemory
    {
        private const string HeaderPrefix = "# run ";

        /// <summary>A name as stored and compared: trimmed, tabs and line breaks turned into spaces, and leading '#'
        /// removed (a stored line starting with '#' would read back as a comment and the player could never be restored).</summary>
        public static string CleanName(string name)
        {
            if (name == null) return "";
            var clean = name.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (clean.Length > 0 && clean[0] == '#') clean = clean.Substring(1).TrimStart();
            return clean;
        }

        /// <summary>The run the file's state belongs to (0 when the text has no header).</summary>
        public static int RunIdOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                int id;
                if (t.StartsWith(HeaderPrefix, StringComparison.Ordinal) && int.TryParse(t.Substring(HeaderPrefix.Length).Trim(), out id)) return id;
                return 0;   // the first non-empty line is not a header
            }
            return 0;
        }

        /// <summary>name -> card keys. Later lines for the same name replace earlier ones.</summary>
        public static Dictionary<string, string[]> Parse(string text)
        {
            var entries = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return entries;
            foreach (var line in text.Split('\n'))
            {
                var t = line.TrimEnd('\r').Trim();
                if (t.Length == 0 || t[0] == '#') continue;
                int tab = t.IndexOf('\t');
                if (tab <= 0) continue;
                string name = CleanName(t.Substring(0, tab));
                if (name.Length == 0) continue;
                var keys = new List<string>();
                foreach (var part in t.Substring(tab + 1).Split(','))
                {
                    var key = part.Trim();
                    if (key.Length > 0) keys.Add(key);
                }
                entries[name] = keys.ToArray();
            }
            return entries;
        }

        public static string Serialize(int runId, Dictionary<string, string[]> entries)
        {
            var sb = new StringBuilder();
            sb.Append(HeaderPrefix).Append(runId).Append('\n');
            foreach (var kv in entries)
            {
                if (kv.Key.Length == 0 || kv.Value == null || kv.Value.Length == 0) continue;
                sb.Append(kv.Key).Append('\t').Append(string.Join(", ", kv.Value)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Both files seen as one: per name, the older entry's cards followed by whatever the newer entry adds. Copies
        /// count (Missing is a multiset diff): a name with two copies of a card in either file keeps two in the merge.
        /// Names only ever gain entries; nobody's saved cards are dropped by a merge.
        /// </summary>
        public static Dictionary<string, string[]> Merge(Dictionary<string, string[]> older, Dictionary<string, string[]> newer)
        {
            var merged = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in older) merged[kv.Key] = kv.Value;
            foreach (var kv in newer)
            {
                string[] have;
                if (!merged.TryGetValue(kv.Key, out have)) { merged[kv.Key] = kv.Value; continue; }
                var union = new List<string>(have);
                foreach (var key in Missing(kv.Value, have)) union.Add(key);
                merged[kv.Key] = union.ToArray();
            }
            return merged;
        }

        /// <summary>The saved cards a player does not hold right now (each owned copy covers one saved copy).</summary>
        public static List<string> Missing(string[] saved, IEnumerable<string> ownedKeys)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in ownedKeys)
            {
                int n;
                counts[key] = counts.TryGetValue(key, out n) ? n + 1 : 1;
            }
            var missing = new List<string>();
            foreach (var key in saved)
            {
                int n;
                if (counts.TryGetValue(key, out n) && n > 0) { counts[key] = n - 1; continue; }
                missing.Add(key);
            }
            return missing;
        }
    }
}
