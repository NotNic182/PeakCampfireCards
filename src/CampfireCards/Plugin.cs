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
    /// cards change how they climb, eat, fall and recover. 64 cards, five rarities, all values from the design sheets.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "notnic.campfirecards";
        public const string Name = "Campfire Cards";
        public const string Version = "0.7.4";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static string PluginDir { get; private set; }

        public static ConfigEntry<bool> SoundsEnabled;
        public static ConfigEntry<bool> CustomFont;
        public static ConfigEntry<float> UiScale;
        public static ConfigEntry<float> PickTimeoutSeconds;
        public static ConfigEntry<PickMode> PickModeSetting;
        public static ConfigEntry<bool> VerboseLog;
        public static ConfigEntry<bool> DevKeys;
        public static ConfigEntry<string> DevGrantCard;
        public static ConfigEntry<Key> BloodBatteryKey;
        public static ConfigEntry<Key> SecondWindKey;
        public static ConfigEntry<Key> EmergencyPurgeKey;
        public static ConfigEntry<Key> SafetyNetKey;
        public static ConfigEntry<Key> BuildScreenKey;
        public static ConfigEntry<string> BloodBatteryPad;
        public static ConfigEntry<string> SecondWindPad;
        public static ConfigEntry<string> EmergencyPurgePad;
        public static ConfigEntry<string> SafetyNetPad;
        public static ConfigEntry<string> BuildScreenPad;

        /// <summary>The key, or the controller button while a controller is in use (Controls).</summary>
        public static string BloodBatteryKeyLabel => Controls.Label(Controls.Act.BloodBattery);
        public static string SecondWindKeyLabel => Controls.Label(Controls.Act.SecondWind);
        public static string EmergencyPurgeKeyLabel => Controls.Label(Controls.Act.EmergencyPurge);
        public static string SafetyNetKeyLabel => Controls.Label(Controls.Act.SafetyNet);
        public static string BuildScreenKeyLabel => Controls.Label(Controls.Act.BuildScreen);

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);

            SoundsEnabled = Config.Bind("General", "Sounds", true, "Play the card sounds.");
            CustomFont = Config.Bind("General", "CustomFont", true,
                "Show all Campfire Cards text in the bundled Darumadrop One font. Off: the game's own font. Takes effect on the next launch.");
            UiScale = Config.Bind("General", "UiScale", 0f,
                new ConfigDescription("Size of all Campfire Cards UI. 0 = automatic (a little larger on small screens such as a Steam Deck); " +
                    "otherwise " + UiTiming.MinUiScale + " to " + UiTiming.MaxUiScale + ", where 1 is the size at 1080p. Applies right away.",
                    new AcceptableValueRange<float>(0f, UiTiming.MaxUiScale)));
            PickTimeoutSeconds = Config.Bind("General", "PickTimeoutSeconds", UiTiming.PickTimeoutSeconds,
                "Seconds to choose a card (default 60). Set to 0 for unlimited time, with no automatic pick. Positive values have a minimum of 5. The host's value is used for everyone.");
            PickModeSetting = Config.Bind("General", "PickMode", PickMode.Simultaneous,
                "Simultaneous: everyone chooses at the same time (the last to reach the fire is still dealt first and listed first). " +
                "Turns: one player at a time, last to the fire first. The host's choice is used for everyone.");
            BloodBatteryKey = Config.Bind("Keys", "BloodBattery", Key.B, "Activates Bootleg Scoutmaster Bugle (once per biome).");
            SecondWindKey = Config.Bind("Keys", "SecondWind", Key.V, "Activates Nic of Time below its stamina threshold (five-minute cooldown).");
            EmergencyPurgeKey = Config.Bind("Keys", "EmergencyPurge", Key.K, "Activates Discount Cure-All at 35+ Poison, Spores or Drowsy (once per biome).");
            SafetyNetKey = Config.Bind("Keys", "SafetyNet", Key.N, "Places the Budget Checkpoint Flag anchor (when you own it).");
            BuildScreenKey = Config.Bind("Keys", "BuildScreen", Key.J, "Opens and closes the Current Build screen (your cards and their combined effects).");
            const string padNote = " auto: the first of D-pad up, down, left, right, View/Select, left or right stick click that no PEAK action uses. " +
                                   "none: no controller button. Or an Input System path, for example <Gamepad>/dpad/left.";
            BloodBatteryPad = Config.Bind("Keys", "BloodBatteryGamepad", Controls.Auto, "Controller button for Bootleg Scoutmaster Bugle." + padNote);
            SecondWindPad = Config.Bind("Keys", "SecondWindGamepad", Controls.Auto, "Controller button for Nic of Time." + padNote);
            EmergencyPurgePad = Config.Bind("Keys", "EmergencyPurgeGamepad", Controls.Auto, "Controller button for Discount Cure-All." + padNote);
            SafetyNetPad = Config.Bind("Keys", "SafetyNetGamepad", Controls.Auto, "Controller button for the Budget Checkpoint Flag anchor." + padNote);
            BuildScreenPad = Config.Bind("Keys", "BuildScreenGamepad", Controls.Auto, "Controller button for the Current Build screen." + padNote);
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Log every card event.");
            DevKeys = Config.Bind("Debug", "DevKeys", false,
                "Testing keys: F9 lights the current campfire, F10 warps you next to it, F11 shows a sample offer, F8 (host) grants the cards listed in GrantCard, F7 logs the active card effects, F6 (host) opens the card tester: every card one rarity at a time, click one to add it to your build with its effects (press F6 again for the next rarity).");
            DevGrantCard = Config.Bind("Debug", "GrantCard", "", "Cards the host grants with F8 (DevKeys on), by card key, comma separated: \"GeckoGrip, BloodBattery\" gives them to the host. Add \"; PlayerName: Key, Key\" (in-game name or player number) to give another player cards, e.g. after a rejoin. Restored cards arrive without their pick-time effects (no Curse, no new Clown penalty, no bonus draws); charges and cooldowns are whatever that player's own game remembers (fresh after a rejoin, kept when the card left and returned on the same PC). A Mythic can be returned even when the deck still counts its old owner. Re-read on every press.");

            CardSettings.Current = LoadSettings(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Hooks.Subscribe();

            Deck.Build();
            Log.LogInfo($"{Name} {Version} loaded.");
        }

        /// <summary>
        /// The balance revision the design sheets carry (tools/card_sheet.py bumps it when a sheet edit changes numbers).
        /// Card values are not configurable: the sheets compiled into the mod are the single source of truth, so this is
        /// a revision marker, not a config key.
        /// 1 = 0.3.0-0.4.0, 2 = 0.4.1, 3 = 0.6.0 reviewed workbook redesign, 4 = 0.6.4 (BingBong Low Gravity 3), 5 = 0.6.5 (Bing BOING 15 s, BingBong every 5 min), 6 = 0.6.6 (Daddy reach x1.4), 7 = 0.7.1 full rebalance, 8 = 0.7.3 (regen benefits reworked, rarity weights 40/40).
        /// </summary>
        public const int CardBalanceVersion = 8;

        /// <summary>
        /// The lobby options. Card numbers, rarity weights and the balance internals (cost cap, stamina floor, difficulty
        /// strengths) are deliberately NOT configurable: they come from the design sheets compiled into the mod, so every
        /// player on the same version plays the same, intended cards. Entries older versions wrote into the file (the 64
        /// per-card sections, Rarity Weights, the balance knobs) are removed once below.
        /// </summary>
        public static CardSettings LoadSettings(ConfigFile cfg)
        {
            var s = new CardSettings();
            s.OfferCount = Mathf.Clamp(cfg.Bind("Cards", "OfferCount", CardTable.OfferCount,
                "Cards offered at each pick (choose 1), from 1 to 10. The host's value is used for everyone.").Value, 1, CardSettings.MaxOfferCount);
            s.DrawbacksEnabled = cfg.Bind("Cards", "Drawbacks", true,
                "false = cards lose their drawbacks for the whole lobby: drawback stats do not apply and there are no Curse, Drowsy, Poison or Injury costs, no Clown Luggage penalty, no skipped pick, no pull cost. Benefits are untouched.").Value;
            s.AllowDuplicates = cfg.Bind("Cards", "AllowDuplicates", false, "Allow a player to own the same card more than once (Mythics never repeat).").Value;
            s.MythicsEnabled = cfg.Bind("Cards", "MythicsEnabled", true, "Mythic cards can appear (each at most once per run, for the whole team).").Value;
            var disabled = cfg.Bind("Cards", "DisabledCards", "",
                "Cards that are never offered, comma separated, by the name on the card. Example: \"BingBong, Fine Print\". " +
                "For a name that itself contains a comma, use the card key from design/editable/card-editing.csv instead.");

            // Everything this build reads is bound above or in Awake (LoadSettings runs last), so any other entry in the
            // file was written by an older version and would sit in config editors as a setting this build never reads.
            // A card someone had turned off in the old per-card sections stays off: it moves into DisabledCards.
            try
            {
                var orphans = Traverse.Create(cfg).Property("OrphanedEntries").GetValue() as Dictionary<ConfigDefinition, string>;
                if (orphans != null && orphans.Count > 0)
                {
                    var kept = new List<string>();
                    foreach (var kv in orphans)
                        if (kv.Key.Section.StartsWith("Card - ") && kv.Key.Key == "Enabled" && string.Equals(kv.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                            kept.Add(kv.Key.Section.Substring("Card - ".Length));
                    if (kept.Count > 0)
                    {
                        if (disabled.Value.Trim().Length > 0) kept.Insert(0, disabled.Value.Trim());
                        disabled.Value = string.Join(", ", kept);
                    }
                    Log.LogInfo($"Config: removed {orphans.Count} setting(s) written by older versions" +
                        (kept.Count > 0 ? $"; kept {kept.Count} disabled card(s) in DisabledCards." : "."));
                    orphans.Clear();
                    cfg.Save();
                }
            }
            catch (Exception e) { Log.LogWarning("Stale config cleanup: " + e.Message); }

            foreach (var token in disabled.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = token.Trim();
                if (t.Length == 0) continue;
                int idx = Array.FindIndex(CardTable.Cards, c =>
                    string.Equals(c.Key, t, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Name, t, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) s.Enabled[idx] = false;
                else Log.LogWarning($"DisabledCards: no card called \"{t}\"");
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

        private void OnDestroy() => Art.CustomCardImages.Clear();

        public static Coroutine Run(IEnumerator routine) => Instance.StartCoroutine(routine);

        public static void Verbose(string msg)
        {
            if (VerboseLog != null && VerboseLog.Value) Log.LogInfo(msg);
        }
    }
}
