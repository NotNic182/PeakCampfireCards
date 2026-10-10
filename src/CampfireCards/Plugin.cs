using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CampfireCards
{
    /// <summary>
    /// Campfire Cards: a BepInEx 5 plugin for PEAK. Every climber picks a card on the beach and at every lit campfire;
    /// cards change how they climb, eat, fall and recover. 64 cards, five rarities, all values in the config.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "notnic.campfirecards";
        public const string Name = "Campfire Cards";
        public const string Version = "0.3.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static string PluginDir { get; private set; }

        public static ConfigEntry<bool> SoundsEnabled;
        public static ConfigEntry<float> PickTimeoutSeconds;
        public static ConfigEntry<bool> VerboseLog;
        public static ConfigEntry<bool> DevKeys;
        public static ConfigEntry<string> DevGrantCard;
        public static ConfigEntry<Key> BloodBatteryKey;
        public static ConfigEntry<Key> SafetyNetKey;

        /// <summary>Cards that ship disabled because part of their effect cannot be built on PEAK's current code (see README).</summary>
        private static readonly HashSet<string> DisabledByDefault = new HashSet<string> { "PackRat" };

        public static string BloodBatteryKeyLabel => BloodBatteryKey != null ? BloodBatteryKey.Value.ToString() : "?";
        public static string SafetyNetKeyLabel => SafetyNetKey != null ? SafetyNetKey.Value.ToString() : "?";

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);

            SoundsEnabled = Config.Bind("General", "Sounds", true, "Play the card sounds.");
            PickTimeoutSeconds = Config.Bind("General", "PickTimeoutSeconds", UiTiming.PickTimeoutSeconds,
                "Safety timer: if a player has not chosen after this many seconds, a random card of their offer is taken.");
            BloodBatteryKey = Config.Bind("Keys", "BloodBattery", Key.B, "Activates Blood Battery (when you own it).");
            SafetyNetKey = Config.Bind("Keys", "SafetyNet", Key.N, "Places the Safety Net anchor (when you own it).");
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Log every card event.");
            DevKeys = Config.Bind("Debug", "DevKeys", false,
                "Testing keys: F9 lights the current campfire, F10 warps you next to it, F11 shows a sample offer, F8 (host) grants the cards listed in GrantCard, F7 logs the active card effects.");
            DevGrantCard = Config.Bind("Debug", "GrantCard", "", "Card keys granted by F8 with DevKeys on, comma separated (for example GeckoGrip, BloodBattery). Re-read on every press.");

            CardSettings.Current = LoadSettings(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Hooks.Subscribe();

            Deck.Build();
            Log.LogInfo($"{Name} {Version} loaded.");
        }

        /// <summary>Every tunable number of the card system, from the config (defaults come from design/cards.json).</summary>
        public static CardSettings LoadSettings(ConfigFile cfg)
        {
            var s = new CardSettings();
            s.OfferCount = Mathf.Clamp(cfg.Bind("Cards", "OfferCount", CardTable.OfferCount, "Cards offered at each pick (choose 1).").Value, 1, 5);
            s.AllowDuplicates = cfg.Bind("Cards", "AllowDuplicates", false, "Allow a player to own the same card more than once (Mythics never repeat).").Value;
            s.MythicsEnabled = cfg.Bind("Cards", "MythicsEnabled", true, "Mythic cards can appear (each at most once per run, for the whole team).").Value;
            s.StaminaCostCap = Mathf.Clamp01(cfg.Bind("Cards", "StaminaCostReductionCapPercent", 60f,
                "Normal cards combined can lower a stamina cost by at most this many percent. Mythics ignore the cap.").Value / 100f);
            s.MaxStaminaFloor = Mathf.Clamp01(cfg.Bind("Cards", "MaxStaminaFloor", 50f,
                "Normal cards combined cannot pull maximum stamina below this (100 = a full bar).").Value / 100f);

            var names = Enum.GetNames(typeof(CardRarity));
            for (int r = 0; r < names.Length && r < s.RarityWeights.Length; r++)
                s.RarityWeights[r] = Mathf.Max(0f, cfg.Bind("Rarity Weights", names[r], CardTable.RarityWeights[r],
                    "Relative chance that an offered card is " + names[r] + ".").Value);

            var cards = CardTable.Cards;
            for (int i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                string section = "Card - " + c.Key;
                bool defaultOn = !DisabledByDefault.Contains(c.Key);
                string note = defaultOn ? "" : " Off by default: its extra inventory slot cannot be added to PEAK yet, only the weight drawback works.";
                s.Enabled[i] = cfg.Bind(section, "Enabled", defaultOn, $"{c.Name} ({c.Rarity}) can be offered.{note}").Value;
                var seen = new Dictionary<string, int>();
                for (int m = 0; m < c.Mods.Length; m++)
                {
                    var mod = c.Mods[m];
                    string key = mod.Channel.ToString();
                    int n;
                    seen[key] = seen.TryGetValue(key, out n) ? n + 1 : 1;
                    if (seen[key] > 1) key += "_" + seen[key];
                    string desc = $"{mod.Op} {mod.Channel} when {mod.When} for {mod.Target}. Multipliers: 1 = no change; flat stamina: 0.1 = 10 stamina.";
                    s.ModValues[i][m] = cfg.Bind(section, key, mod.Value, desc).Value;
                }
                foreach (var kv in c.Params)
                    s.Params[i][kv.Key] = cfg.Bind(section, "Param_" + kv.Key, kv.Value, "Card parameter (statuses and stamina: 0.1 = 10).").Value;
            }
            return s;
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
