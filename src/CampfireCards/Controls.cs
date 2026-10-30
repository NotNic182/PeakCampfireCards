using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine.InputSystem;
using Zorro.ControllerSupport;

namespace CampfireCards
{
    /// <summary>
    /// systems.json Controls: the mod's own buttons. Each action has a keyboard key and a controller button (config Keys).
    /// A controller button set to "auto" takes the first candidate that no PEAK gameplay action is bound to (player
    /// rebinds included), so pressing it never also jumps, drops or uses an item. Labels follow the device in use.
    /// </summary>
    public static class Controls
    {
        public enum Act { BloodBattery, SecondWind, EmergencyPurge, SafetyNet, BuildScreen, ScavengerFood, ScavengerLuggage, ScavengerMisc, BugReport }

        /// <summary>The action a manual card's ability is pressed with (cards.json ability), or null for a passive card.</summary>
        public static Act? ActOf(string ability)
        {
            switch (ability)
            {
                case "BloodBattery": return Act.BloodBattery;
                case "SecondWind": return Act.SecondWind;
                case "EmergencyPurge": return Act.EmergencyPurge;
                case "SafetyNet": return Act.SafetyNet;
                default: return null;
            }
        }

        /// <summary>Two actions answer to the same key or controller button, so one press would be ambiguous.</summary>
        public static bool SharesButton(Act a, Act b)
        {
            if (a == b) return true;
            var ka = KeyOf(a); var kb = KeyOf(b);
            if (ka != null && kb != null && ka.Value != Key.None && ka.Value == kb.Value) return true;
            var pa = PadAction(a); var pb = PadAction(b);
            return pa != null && pb != null && Control(pa.bindings[0].effectivePath) == Control(pb.bindings[0].effectivePath);
        }

        public const string Auto = "auto";
        public const string None = "none";

        /// <summary>Tried in this order for "auto"; the D-pad first because it is the easiest to reach while climbing.</summary>
        private static readonly string[] Candidates =
        {
            "<Gamepad>/dpad/up", "<Gamepad>/dpad/down", "<Gamepad>/dpad/left", "<Gamepad>/dpad/right",
            "<Gamepad>/select", "<Gamepad>/leftStickPress", "<Gamepad>/rightStickPress",
        };

        /// <summary>Device layouts whose bindings never collide with a controller button.</summary>
        private static readonly string[] NotGamepad = { "<keyboard>", "<mouse>", "<pointer>", "<touchscreen>", "<pen>" };

        private static InputAction[] _pad;
        private static bool _resolved;

        /// <summary>The player is on a controller right now (PEAK's own input scheme switch).</summary>
        public static bool UsingGamepad
        {
            get
            {
                try { return InputHandler.GetCurrentUsedInputScheme() == InputScheme.Gamepad; }
                catch { return false; }
            }
        }

        /// <summary>The key or the controller button of the action went down this frame.</summary>
        public static bool WasPressed(Act act)
        {
            if (KeyPressed(KeyOf(act))) return true;
            var pad = PadAction(act);
            return pad != null && pad.WasPressedThisFrame();
        }

        /// <summary>
        /// The action's keyboard key would type a character into a focused text field (letters, digits, punctuation,
        /// space, Enter, numpad). Such a key must not double as "close the window" while the player is typing.
        /// </summary>
        public static bool ProducesText(Act act)
        {
            var key = KeyOf(act);
            if (key == null || key.Value == Key.None) return false;
            var v = key.Value;
            return (v >= Key.Space && v <= Key.Digit0) || (v >= Key.NumpadEnter && v <= Key.Numpad9);
        }

        /// <summary>The key or the controller button of the action is held down right now (TSA's reveal keys).</summary>
        public static bool IsHeld(Act act)
        {
            if (KeyHeld(KeyOf(act))) return true;
            var pad = PadAction(act);
            return pad != null && pad.IsPressed();
        }

        /// <summary>What to print for the action: the controller button while a controller is in use, else the key.</summary>
        public static string Label(Act act)
        {
            if (UsingGamepad)
            {
                var pad = PadAction(act);
                if (pad != null)
                {
                    try { return InputControlPath.ToHumanReadableString(pad.bindings[0].effectivePath, InputControlPath.HumanReadableStringOptions.OmitDevice); }
                    catch { }
                }
            }
            var key = KeyOf(act);
            return key != null && key.Value != Key.None ? key.Value.ToString() : "?";
        }

        private static ConfigEntry<Key> KeyOf(Act act)
        {
            switch (act)
            {
                case Act.BloodBattery: return Plugin.BloodBatteryKey;
                case Act.SecondWind: return Plugin.SecondWindKey;
                case Act.EmergencyPurge: return Plugin.EmergencyPurgeKey;
                case Act.SafetyNet: return Plugin.SafetyNetKey;
                case Act.ScavengerFood: return Plugin.ScavengerFoodKey;
                case Act.ScavengerLuggage: return Plugin.ScavengerLuggageKey;
                case Act.ScavengerMisc: return Plugin.ScavengerMiscKey;
                case Act.BugReport: return Plugin.BugReportKey;
                default: return Plugin.BuildScreenKey;
            }
        }

        private static ConfigEntry<string> PadOf(Act act)
        {
            switch (act)
            {
                case Act.BloodBattery: return Plugin.BloodBatteryPad;
                case Act.SecondWind: return Plugin.SecondWindPad;
                case Act.EmergencyPurge: return Plugin.EmergencyPurgePad;
                case Act.SafetyNet: return Plugin.SafetyNetPad;
                case Act.ScavengerFood: return Plugin.ScavengerFoodPad;
                case Act.ScavengerLuggage: return Plugin.ScavengerLuggagePad;
                case Act.ScavengerMisc: return Plugin.ScavengerMiscPad;
                case Act.BugReport: return Plugin.BugReportPad;
                default: return Plugin.BuildScreenPad;
            }
        }

        private static bool KeyPressed(ConfigEntry<Key> key)
        {
            var kb = Keyboard.current;
            if (kb == null || key == null || key.Value == Key.None) return false;
            try { var control = kb[key.Value]; return control != null && control.wasPressedThisFrame; }
            catch { return false; }
        }

        private static bool KeyHeld(ConfigEntry<Key> key)
        {
            var kb = Keyboard.current;
            if (kb == null || key == null || key.Value == Key.None) return false;
            try { var control = kb[key.Value]; return control != null && control.isPressed; }
            catch { return false; }
        }

        private static InputAction PadAction(Act act)
        {
            Resolve();
            return _pad != null ? _pad[(int)act] : null;
        }

        /// <summary>
        /// Once PEAK's actions exist: bind every controller button. Buttons set by hand are reserved first, so an auto
        /// pick never lands on one of them; auto picks also skip buttons the game uses. Bound again when PEAK's own
        /// bindings change (a rebind in its controls menu, a controller connected).
        /// </summary>
        private static void Resolve()
        {
            if (_resolved) return;
            InputActionAsset game = null;
            try { game = InputSystem.actions; } catch { }
            if (game == null) return;   // not loaded yet; try again next call
            _resolved = true;
            WatchRebinds();
            Release();

            var acts = (Act[])Enum.GetValues(typeof(Act));
            var want = new string[acts.Length];
            var gameUsed = GameplayControls(game);
            var taken = new HashSet<string>(gameUsed);
            foreach (var act in acts)
            {
                var entry = PadOf(act);
                string w = entry != null && entry.Value != null ? entry.Value.Trim() : Auto;
                want[(int)act] = w;
                if (!IsAuto(w) && !IsNone(w)) taken.Add(Control(w));   // reserved before any auto pick
            }

            _pad = new InputAction[acts.Length];
            foreach (var act in acts)
            {
                string w = want[(int)act], path = null;
                if (IsNone(w)) continue;
                if (IsAuto(w))
                {
                    foreach (var c in Candidates)
                        if (!IsUsed(taken, c)) { path = c; break; }
                    if (path == null)
                    {
                        Plugin.Log.LogWarning($"No free controller button for {act}: every candidate is used. Set Keys.{act}Gamepad to a button path, for example <Gamepad>/dpad/left.");
                        continue;
                    }
                    taken.Add(Control(path));
                }
                else
                {
                    path = w;
                    if (IsUsed(gameUsed, path)) Plugin.Log.LogWarning($"Keys.{act}Gamepad ({path}) is also bound to a PEAK action; both will fire.");
                }

                try
                {
                    var action = new InputAction("CampfireCards_" + act, InputActionType.Button, path);
                    action.Enable();
                    // a typo in a path is accepted silently and simply never fires: say so while a controller is there to match
                    if (action.controls.Count == 0 && Gamepad.all.Count > 0)
                        Plugin.Log.LogWarning($"Keys.{act}Gamepad ({path}) matches no button on the connected controller. Check the path, for example <Gamepad>/dpad/left.");
                    _pad[(int)act] = action;
                    Plugin.Log.LogInfo($"Controller button for {act}: {path}");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Keys.{act}Gamepad ({path}) is not a valid button path: {e.Message}");
                }
            }
        }

        private static bool IsAuto(string w) => w.Equals(Auto, StringComparison.OrdinalIgnoreCase);
        private static bool IsNone(string w) => w.Length == 0 || w.Equals(None, StringComparison.OrdinalIgnoreCase);

        /// <summary>Drop the mod's own actions before binding again.</summary>
        private static void Release()
        {
            if (_pad == null) return;
            foreach (var a in _pad)
            {
                if (a == null) continue;
                a.Disable();
                a.Dispose();
            }
            _pad = null;
        }

        private static bool _watching;

        /// <summary>Bind again on the next press check whenever PEAK's own action bindings change.</summary>
        private static void WatchRebinds()
        {
            if (_watching) return;
            _watching = true;
            InputSystem.onActionChange += (obj, change) =>
            {
                if (change != InputActionChange.BoundControlsChanged) return;
                InputActionAsset game = null;
                try { game = InputSystem.actions; } catch { }
                if (game == null) return;
                bool peaks = ReferenceEquals(obj, game)
                             || (obj is InputActionMap map && map.asset == game)
                             || (obj is InputAction action && action.actionMap != null && action.actionMap.asset == game);
                if (peaks) _resolved = false;   // the mod's own actions are not in PEAK's asset, so they never trigger this
            };
        }

        /// <summary>
        /// The controller controls PEAK's gameplay actions are bound to. The UI map is skipped: it only acts while a menu
        /// is open, and the mod's buttons are ignored then.
        /// </summary>
        private static HashSet<string> GameplayControls(InputActionAsset asset)
        {
            var used = new HashSet<string>();
            foreach (var map in asset.actionMaps)
            {
                if (string.Equals(map.name, "UI", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var action in map.actions)
                {
                    if (action.name.StartsWith("UI", StringComparison.Ordinal) || action.name.StartsWith("Navigate", StringComparison.Ordinal)) continue;
                    foreach (var b in action.bindings)
                    {
                        if (b.isComposite) continue;
                        string p = b.effectivePath;
                        if (string.IsNullOrEmpty(p) || p[0] != '<') continue;
                        int close = p.IndexOf('>');
                        if (close < 0 || Array.IndexOf(NotGamepad, p.Substring(0, close + 1).ToLowerInvariant()) >= 0) continue;
                        used.Add(Control(p));
                    }
                }
            }
            return used;
        }

        /// <summary>A button counts as used when it or a control containing it is bound ("dpad" covers "dpad/up").</summary>
        private static bool IsUsed(HashSet<string> used, string path)
        {
            string c = Control(path);
            for (int i = c.Length; i > 0; i = c.LastIndexOf('/', i - 1))
                if (used.Contains(c.Substring(0, i))) return true;
            return false;
        }

        /// <summary>"&lt;Gamepad&gt;/dpad/up" -> "dpad/up" (device-independent, lower case).</summary>
        private static string Control(string path)
        {
            int i = path.IndexOf('/');
            return (i >= 0 ? path.Substring(i + 1) : path).ToLowerInvariant();
        }
    }
}
