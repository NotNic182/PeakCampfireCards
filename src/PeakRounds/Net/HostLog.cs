using System;
using System.IO;

namespace PeakRounds.Net
{
    /// <summary>
    /// systems.json HostLog: when this player hosts, write the Steam lobby id to %LOCALAPPDATA%/PeakRounds/net.log on a line
    /// "Hosting at <id>" so Melty (recipe multiplayer.connect.address) can show joiners where the game is.
    /// </summary>
    public static class HostLog
    {
        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeakRounds");
        public static string Path_ => Path.Combine(Dir, "net.log");

        public static void WriteHosting(ulong lobbyId)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path_, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] Hosting at {lobbyId}{Environment.NewLine}");
                Plugin.Log.LogInfo("Hosting at " + lobbyId + " (written to " + Path_ + ")");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("HostLog write failed: " + e.Message);
            }
        }
    }
}
