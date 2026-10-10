using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PeakRounds.Generated;

namespace PeakRounds.Rounds
{
    /// <summary>systems.json RoundsLocator: finds the player's ROUNDS install (config override, Steam manifest, Steam library scan).</summary>
    public static class RoundsLocator
    {
        public static string Find(string overridePath, out string how)
        {
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                string p = overridePath.Trim().Trim('"');
                if (HasRounds(p)) { how = "config RoundsPath"; return p; }
                how = "config RoundsPath set but ROUNDS.exe not found there; falling back to Steam";
            }
            else how = "";

            var libs = SteamLibraries();
            foreach (var lib in libs)
            {
                string manifest = Path.Combine(lib, "steamapps", $"appmanifest_{RoundsInstall.SteamAppId}.acf");
                if (!File.Exists(manifest)) continue;
                string installdir = null;
                try
                {
                    var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (m.Success) installdir = m.Groups[1].Value;
                }
                catch { }
                if (installdir != null)
                {
                    string p = Path.Combine(lib, "steamapps", "common", installdir);
                    if (HasRounds(p)) { how += (how.Length > 0 ? "; " : "") + "Steam manifest in " + lib; return p; }
                }
            }
            foreach (var lib in libs)
            {
                string p = Path.Combine(lib, "steamapps", "common", "ROUNDS");
                if (HasRounds(p)) { how += (how.Length > 0 ? "; " : "") + "Steam library folder scan in " + lib; return p; }
            }
            how += (how.Length > 0 ? "; " : "") + $"not found in {libs.Count} Steam library folder(s)";
            return null;
        }

        public static bool HasRounds(string folder)
        {
            return !string.IsNullOrEmpty(folder) && Directory.Exists(folder)
                && File.Exists(Path.Combine(folder, RoundsInstall.Exe))
                && Directory.Exists(Path.Combine(folder, RoundsInstall.DataFolder));
        }

        /// <summary>Steam root plus every extra library listed in libraryfolders.vdf. Also the library PEAK itself sits in.</summary>
        public static List<string> SteamLibraries()
        {
            var result = new List<string>();
            void Add(string p)
            {
                if (string.IsNullOrEmpty(p)) return;
                p = p.Replace('/', '\\').TrimEnd('\\');
                if (Directory.Exists(p) && !result.Contains(p)) result.Add(p);
            }
            // the library PEAK is installed in (…/steamapps/common/PEAK)
            try
            {
                string gameRoot = BepInEx.Paths.GameRootPath;
                var common = Directory.GetParent(gameRoot);
                var steamapps = common?.Parent;
                var lib = steamapps?.Parent;
                if (lib != null && string.Equals(steamapps.Name, "steamapps", StringComparison.OrdinalIgnoreCase)) Add(lib.FullName);
            }
            catch { }
            Add(RegistryString(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"));
            Add(RegistryString(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            Add(@"C:\Program Files (x86)\Steam");

            foreach (var root in result.ToArray())
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
                catch { }
            }
            return result;
        }

        /// <summary>Reads a registry string through reflection (Microsoft.Win32.Registry is not part of .NET Standard, but Mono has it).</summary>
        private static string RegistryString(string keyPath, string valueName)
        {
            try
            {
                var t = Type.GetType("Microsoft.Win32.Registry, mscorlib") ?? Type.GetType("Microsoft.Win32.Registry");
                var m = t?.GetMethod("GetValue", new[] { typeof(string), typeof(string), typeof(object) });
                return m?.Invoke(null, new object[] { keyPath, valueName, null }) as string;
            }
            catch { return null; }
        }
    }
}
