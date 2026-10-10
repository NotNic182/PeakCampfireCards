using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PeakRounds.Cards;
using PeakRounds.Rounds;
using PeakRounds.Ui;
using UnityEngine;

namespace PeakRounds
{
    /// <summary>
    /// PEAK x ROUNDS: Campfire Cards. BepInEx 5 plugin for PEAK that reads the real ROUNDS cards from the
    /// player's own ROUNDS install and deals them at every campfire.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "thejokehive.peakrounds";
        public const string Name = "PEAK x ROUNDS: Campfire Cards";
        public const string Version = "0.1.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static string PluginDir { get; private set; }

        public static ConfigEntry<string> RoundsPath;
        public static ConfigEntry<bool> SoundsEnabled;
        public static ConfigEntry<float> PickTimeoutSeconds;
        public static ConfigEntry<bool> VerboseLog;
        public static ConfigEntry<bool> DevKeys;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);

            RoundsPath = Config.Bind("ROUNDS", "RoundsPath", "",
                "Folder of your ROUNDS install (the one with ROUNDS.exe). Leave empty to find it through Steam.");
            SoundsEnabled = Config.Bind("ROUNDS", "Sounds", true,
                "Play ROUNDS's own card sounds (read from your ROUNDS install).");
            PickTimeoutSeconds = Config.Bind("Cards", "PickTimeoutSeconds", Generated.UiTiming.PickTimeoutSeconds,
                "Safety timer: if a player has not picked after this many seconds, a random card of their hand is taken.");
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Log every card event.");
            DevKeys = Config.Bind("Debug", "DevKeys", false, "Testing keys (host only): F9 lights the current campfire, F10 warps you next to it, F11 shows a sample card fan.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Hooks.Subscribe();

            RoundsReader.BeginLoad(PluginDir, RoundsPath.Value);
            Log.LogInfo($"{Name} {Version} loaded. Reading ROUNDS in the background...");
        }

        private void Update()
        {
            try
            {
                Hooks.Tick();
            }
            catch (Exception e)
            {
                if (Time.frameCount % 300 == 0) Log.LogError("Tick: " + e);
            }
        }

        public static Coroutine Run(IEnumerator routine) => Instance.StartCoroutine(routine);

        public static void Verbose(string msg)
        {
            if (VerboseLog != null && VerboseLog.Value) Log.LogInfo(msg);
        }
    }
}
