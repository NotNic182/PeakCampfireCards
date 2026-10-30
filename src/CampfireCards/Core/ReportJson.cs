using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CampfireCards.Core
{
    /// <summary>
    /// One in-game bug report (ui.json BugReport): what the tester wrote plus the run context that makes it actionable.
    /// Deliberately free of anything personal - no names, no account ids, no machine info; the log arrives pre-scrubbed
    /// (LogScrub). Unity-free so the tests can pin the wire format.
    /// </summary>
    public sealed class Report
    {
        public string ModVersion;
        public int Balance;
        public string GameVersion;
        public string Scene;
        public string UtcTime;
        /// <summary>A random per-install id (config Reporting.InstallId): groups one tester's reports, names nobody.</summary>
        public string InstallId;
        public int Players;
        public bool IsHost;
        public bool DrawbacksEnabled;
        public float BenefitStrength;
        public List<string> Categories = new List<string>();
        public string Comment = "";
        /// <summary>The reporter's deck as card ids (names are in the public card list).</summary>
        public List<int> Deck = new List<int>();
        /// <summary>Teammates' decks as card ids, in no particular order and with no identity attached.</summary>
        public List<List<int>> TeamDecks = new List<List<int>>();
        /// <summary>The scrubbed log tail, or null when the tester left the checkbox off.</summary>
        public string Log;
    }

    /// <summary>Writes the report as JSON by hand: the mod ships no JSON library, and the shape is five fields deep.</summary>
    public static class ReportJson
    {
        public static string Build(Report r)
        {
            var sb = new StringBuilder(r.Log != null ? r.Log.Length + 2048 : 2048);
            sb.Append("{\"v\":1");
            Str(sb, "mod", r.ModVersion);
            sb.Append(",\"balance\":").Append(r.Balance.ToString(CultureInfo.InvariantCulture));
            Str(sb, "game", r.GameVersion);
            Str(sb, "scene", r.Scene);
            Str(sb, "time", r.UtcTime);
            Str(sb, "install", r.InstallId);
            sb.Append(",\"players\":").Append(r.Players.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"host\":").Append(r.IsHost ? "true" : "false");
            sb.Append(",\"drawbacks\":").Append(r.DrawbacksEnabled ? "true" : "false");
            sb.Append(",\"strength\":").Append(r.BenefitStrength.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"categories\":[");
            for (int i = 0; i < r.Categories.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Escape(r.Categories[i])).Append('"');
            }
            sb.Append(']');
            Str(sb, "comment", r.Comment ?? "");
            sb.Append(",\"deck\":");
            Ints(sb, r.Deck);
            sb.Append(",\"teamDecks\":[");
            for (int i = 0; i < r.TeamDecks.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Ints(sb, r.TeamDecks[i]);
            }
            sb.Append(']');
            if (r.Log == null) sb.Append(",\"log\":null");
            else Str(sb, "log", r.Log);
            sb.Append('}');
            return sb.ToString();
        }

        private static void Ints(StringBuilder sb, List<int> list)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(list[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(']');
        }

        private static void Str(StringBuilder sb, string name, string value)
        {
            sb.Append(",\"").Append(name).Append("\":");
            if (value == null) { sb.Append("null"); return; }
            sb.Append('"').Append(Escape(value)).Append('"');
        }

        /// <summary>JSON string escaping: quotes, backslashes, and every control character as \uXXXX (or the short form).</summary>
        public static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
