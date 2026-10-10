using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using CampfireCards.Cards;
using CampfireCards.Ui;
using UnityEngine;

namespace CampfireCards
{
    /// <summary>
    /// Campfire Cards: a BepInEx 5 plugin for PEAK. Every climber picks a card on the beach and at every lit campfire;
    /// cards change how they climb, eat, fall and recover.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "notnic.campfirecards";
        public const string Name = "Campfire Cards";
        public const string Version = "0.2.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static string PluginDir { get; private set; }

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

            SoundsEnabled = Config.Bind("Cards", "Sounds", true, "Play the card sounds.");
            PickTimeoutSeconds = Config.Bind("Cards", "PickTimeoutSeconds", Generated.UiTiming.PickTimeoutSeconds,
                "Safety timer: if a player has not picked after this many seconds, a random card of their hand is taken.");
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Log every card event.");
            DevKeys = Config.Bind("Debug", "DevKeys", false, "Testing keys (host only): F9 lights the current campfire, F10 warps you next to it, F11 shows a sample card fan.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Hooks.Subscribe();

            Deck.Build();
            Log.LogInfo($"{Name} {Version} loaded.");
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
