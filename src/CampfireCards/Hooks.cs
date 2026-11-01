using System;
using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using CampfireCards.Ui;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CampfireCards
{
    /// <summary>Per-run state shared by the hooks.</summary>
    public static class RunState
    {
        public static bool OnIsland;
        public static bool RunStarted;
        public static bool SentOpeningArrival;
        public static float GroundedSince = -1f;
        /// <summary>Campfires this scout already sent an Arrival for - by object, since chained runs reuse segment numbers.</summary>
        public static readonly HashSet<Campfire> ArrivedFires = new HashSet<Campfire>();

        /// <summary>Everything the mod changed goes back to vanilla: cards, flags, timers, stat changes, UI.</summary>
        public static void Reset()
        {
            RunStarted = false;
            SentOpeningArrival = false;
            GroundedSince = -1f;
            ArrivedFires.Clear();
            Effects.Reset();
            Hooks.ResetStatusCarries();
            Hooks.ResetEndGameHold();
            PickSession.Reset();
            Ability_EternalCampfire.Arbiter.Reset();
            NetHandlers.Reset();
            Hooks.ResetRunTracking();
            Hud.ClearMarkers();
            DeckScreen.HideIfOpen();
            BugReportScreen.HideIfOpen();   // Hooks.Tick is gated off-island, so its Tick cannot do this close itself
        }

        public static bool IsGameplayScene(string name) => name != null && (name.Contains("Island") || name.Contains("Level_"));
    }

    /// <summary>
    /// The handler bodies behind design/hooks.json (Generated/HarmonyHooks.cs forwards into these). This file holds the
    /// run lifecycle, the per-frame tick and the network entry point; the stat, item and revival hooks live in the
    /// other Hooks.*.cs files.
    /// </summary>
    public static partial class Hooks
    {
        private static float _nextArrivalCheck, _nextAbilityBar;
        private static bool _photonSubscribed;

        // ------------------------------------------------------------------ subscriptions (event / unity rows)
        public static void Subscribe()
        {
            PlayerHandler.CharacterRegistered += OnCharacterRegistered;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SubscribePhoton();
        }

        private static void SubscribePhoton()
        {
            if (_photonSubscribed) return;
            var client = PhotonNetwork.NetworkingClient;
            if (client == null) return;
            client.EventReceived += OnPhotonEvent;
            _photonSubscribed = true;
        }

        private static IEnumerator ResubscribeNextFrame()
        {
            yield return null;
            SubscribePhoton();
        }

        /// <summary>Per-run bookkeeping inside the hooks (feeders, contexts, item-use batches, thorns, campfire rewards).</summary>
        public static void ResetRunTracking()
        {
            _lastFeederActor = int.MinValue;
            _fallDepth = 0;
            _climbDepth = 0; _climbCtx = null;
            _batches.Clear();
            _thorns.Reset();
            _lunch.Reset();
            _dynamiteSpawnDepth = 0; _dynamiteBlast = false;
            _lastSegmentNumber = int.MinValue;
            _fireKeys.Clear();
            _chainKeysTaken = 0;
            ResetCampfireSpawns();
        }

        // ------------------------------------------------------------------ run lifecycle
        /// <summary>hooks.json runStart: RunManager.StartRun postfix.</summary>
        public static void OnRunStarted()
        {
            RunState.Reset();
            RunState.RunStarted = true;
            RunState.OnIsland = true;
            LocalCards.OnRunStarted(Environment.TickCount ^ (NetSend.LocalActor * 7919));
            RunMemoryFile.OnRunStarted(LocalCards.RunSeed);
            PickSession.OnRunStarted();
            Hud.Ensure();
            if (!NetSend.IsHost) NetSend.Raise(NetHandlers.MakeHello(), Photon.Realtime.ReceiverGroup.Others);
            Plugin.Log.LogInfo($"Run started. Host={NetSend.IsHost} actor={NetSend.LocalActor} solo={NetSend.IsSolo} deck={Deck.Count} cards");
        }

        /// <summary>hooks.json endGame: the run is over (end screen). Cards, timers and stat changes are cleared.</summary>
        public static void OnRunEnded()
        {
            RunState.Reset();
            RunMemoryFile.OnRunEnded();
            Hud.CloseAll();
        }

        /// <summary>hooks.json campfireLit prefix: remember the state before the RPC.</summary>
        public static void OnCampfireLitPrefix(Campfire __instance, bool updateSegment, out Campfire.FireState __state)
        {
            __state = __instance.state;
            if (__state == Campfire.FireState.Lit || !RunState.RunStarted) return;
            if (TfaPresent) PickSession.CapturePreLitEligibility();   // a chained fire's key exists only in the postfix
            if (updateSegment) PickSession.SnapshotCampfireEligibility(FireKey(__instance));
        }

        /// <summary>
        /// hooks.json campfireLit postfix: Off -> Lit with a segment advance is a real lighting. A lighting WITHOUT a
        /// segment advance is normally a late-join sync or quicksave relight (burningFor carries the fire's age) - but
        /// True Final Ascent lights its mid-chain biome fires exactly that way, fresh, so with that mod present a fresh
        /// one counts as a biome boundary and deals cards too. Chained runs also revisit vanilla segment numbers, so a
        /// fire whose number already had its session needs a key of its own. Chain keys live only on the HOST: a local
        /// counter would drift on late joins and rejoins, and the host's key reaches every client (fixed or not) in
        /// PickSessionStart, so the mirrored queues stay identical. Biome boundaries and card effects stay per-client.
        /// </summary>
        public static void OnCampfireLit(Campfire __instance, bool updateSegment, float burningFor, Campfire.FireState __state)
        {
            if (__state == Campfire.FireState.Lit || !RunState.RunStarted) return;
            var local = Character.localCharacter;
            byte raw = FireKey(__instance);
            byte key = raw;
            bool chained = false;
            if (!updateSegment)
            {
                if (!TfaPresent) return;   // late-join sync, quicksave or debug
                bool fresh = burningFor <= 0.01f && PhotonNetwork.Time - PickSession.RunStartTime > 20.0;
                Plugin.Log.LogInfo($"Campfire lit without a segment advance (burningFor {burningFor:0.##}): "
                                   + (fresh ? "chained-run biome fire" : "sync relight, ignored"));
                if (!fresh) return;
                chained = true;
            }
            else if (TfaPresent && PickSession.SegmentUsed(raw))
            {
                chained = true;   // the chain revisits this segment number; the fire still needs a session of its own
            }
            bool session = true;
            if (chained)
            {
                if (!PollBiomeNow(local) && local != null) LocalCards.OnChainBoundary(local);
                if (NetSend.IsHost)
                {
                    int chainKey = TakeFreeChainKey();
                    if (chainKey < 0) { session = false; Plugin.Log.LogWarning("Chain keys exhausted; this fire deals no cards"); }
                    else
                    {
                        key = (byte)chainKey;
                        PickSession.AdoptPreLitEligibility(key);
                        _fireKeys[__instance] = key;
                    }
                }
            }
            else _fireKeys[__instance] = key;
            Plugin.Log.LogInfo($"Campfire lit -> {__instance.advanceToSegment} (key {key})");
            if (NetSend.IsHost) HostCampfireLit(__instance, raw);
            // card effects first, so cards picked at this fire do not fire for it
            if (local != null) LocalCards.OnCampfireLit(local, __instance);
            // vanilla runs: every client queues it and the host deals. Chained runs: ONLY the host's queue takes keys -
            // a guest's view of what a fire should be keyed can diverge (late join, rejoin), and a wrongly queued entry
            // survives to deal a phantom session after host migration. Guests mirror the host via PickSessionStart.
            if (session && (!TfaPresent || NetSend.IsHost)) PickSession.RequestSession(1, key);
        }

        // ------------------------------------------------------------------ chained-run campfires (True Final Ascent)
        private static bool? _tfaPresent;
        private static readonly Dictionary<Campfire, byte> _fireKeys = new Dictionary<Campfire, byte>();
        private static int _chainKeysTaken;
        /// <summary>First session key for chained-run fires: above every vanilla segment number, below the opening's 255.</summary>
        internal const byte ChainKeyFirst = 200;
        internal const int ChainKeyRange = 50;

        /// <summary>True Final Ascent chains many biomes into one run; several of its campfires reuse segment numbers.</summary>
        internal static bool TfaPresent
        {
            get
            {
                if (_tfaPresent == null)
                {
                    try { _tfaPresent = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.CptPICHU.peak.truefinalascent"); }
                    catch { _tfaPresent = false; }
                }
                return _tfaPresent.Value;
            }
        }

        /// <summary>
        /// Session/ledger key for a campfire: its segment number, unless this client keyed the fire onto the chain
        /// range at lighting time (the host does, for chained fires). Guests keep the segment number; the host's
        /// ledger carries both, so their claims still resolve to the right fire.
        /// </summary>
        internal static byte FireKey(Campfire fire)
        {
            byte key;
            if (fire != null && _fireKeys.TryGetValue(fire, out key)) return key;
            int seg = fire != null ? (int)fire.advanceToSegment : 0;
            return (byte)(seg >= 255 ? 254 : seg);   // 255 is the opening pick's sentinel
        }

        /// <summary>
        /// Host: the next chain key no session used yet, or -1 when all 50 are spent (a promoted host's counter may
        /// trail its mirrored queue, so keys from before its join can be re-minted; session ids keep that harmless).
        /// </summary>
        private static int TakeFreeChainKey()
        {
            for (int i = 0; i < ChainKeyRange; i++)
            {
                byte key = (byte)(ChainKeyFirst + (_chainKeysTaken++ % ChainKeyRange));
                if (!PickSession.SegmentUsed(key)) return key;
            }
            return -1;
        }

        /// <summary>hooks.json lobbyCreated: log where this host is (net.log), for launchers that list open games.</summary>
        public static void OnLobbyCreated(SteamLobbyHandler __instance)
        {
            try
            {
                var id = __instance.LobbySteamId;
                if (id.IsValid()) HostLog.WriteHosting(id.m_SteamID);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("HostLog: " + e.Message);
            }
        }

        /// <summary>hooks.json characterRegistered.</summary>
        public static void OnCharacterRegistered(Character c)
        {
            if (c == null) return;
            try
            {
                int actor = NetSend.ActorOf(c);
                if (c.IsLocal)
                {
                    if (!NetSend.IsHost && !Effects.HasAnyState)
                    {
                        // a late joiner may never see the run start: introduce ourselves here too (the host answers in kind)
                        NetSend.RaiseToMaster(NetHandlers.MakeHello());
                        NetSend.RaiseToMaster(new StateRequest { modVersion = Plugin.Version });
                    }
                }
                else if (NetSend.IsHost && Effects.HasAnyState)
                {
                    NetSend.RaiseTo(Effects.BuildStateSync(PickSession.SessionId), actor);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("OnCharacterRegistered: " + e);
            }
        }

        /// <summary>hooks.json sceneLoaded.</summary>
        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            bool gameplay = RunState.IsGameplayScene(scene.name);
            RunState.OnIsland = gameplay;
            if (!gameplay)
            {
                RunState.Reset();
                Hud.CloseAll();
            }
            Plugin.Run(ResubscribeNextFrame());
        }

        /// <summary>hooks.json photonEvent.</summary>
        public static void OnPhotonEvent(EventData e)
        {
            try
            {
                if (e.Code == Photon.Realtime.EventCode.Leave)
                {
                    PickSession.OnPlayerLeft(e.Sender);
                    if (NetSend.IsHost) Ability_EternalCampfire.Arbiter.ForgetVictim(e.Sender);
                    _unreadableFrom.Remove(e.Sender);
                    return;
                }
                if (e.Code < 90 || e.Code > 119) return;
                var data = e.CustomData as byte[];
                if (data == null) return;
                try
                {
                    NetDispatcher.Dispatch(e.Code, data, e.Sender);
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is FormatException)
                {
                    // EndOfStream / InvalidData: a malformed message, or another version of the mod
                    Plugin.Log.LogWarning($"Net event {e.Code} from actor {e.Sender} could not be read: {ex.Message}");
                    if (_unreadableFrom.Add(e.Sender))
                        Hud.Toast(string.Format(Ui_Toast.UnreadableTemplate, NetSend.NameOf(e.Sender)), warning: true);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Net event {e.Code}: {ex}");
            }
        }

        /// <summary>Players already reported for sending unreadable mod messages (once per player per session).</summary>
        private static readonly HashSet<int> _unreadableFrom = new HashSet<int>();

        // ------------------------------------------------------------------ per frame
        public static void Tick()
        {
            // contexts are set and cleared inside game methods; nothing legitimately spans frames
            _fallDepth = 0; _climbDepth = 0; _climbCtx = null;
            _itemActionDepth = 0; _itemScaled = false;
            // card benefits scale with the run's difficulty (the game syncs the Ascent to every player)
            CardSettings.Current.BenefitStrength = RunState.OnIsland ? CardSettings.Current.StrengthFor(Ascents.currentAscent) : 1f;

            if (!RunState.OnIsland) return;
            var local = Character.localCharacter;
            if (local == null) return;

            Hud.Ensure();
            if (!RunState.RunStarted) return;

            RestoreKey(local);

            PollBiome(local);
            Effects.Recompute(local);
            SampleInjuryCapFloor(local);   // the Injury cap may shrink with a lost aura, never below committed points
            LocalCards.Tick(local, Time.deltaTime);
            FlushItemUses(local);
            AbilityKeys(local);
            PickSession.HostTick();
            DeckScreen.Tick(local);
            BugReportScreen.Tick();
            TickEndGameHold();

            if (Time.unscaledTime >= _nextAbilityBar)
            {
                _nextAbilityBar = Time.unscaledTime + UiTiming.HudRefreshSeconds;
                Hud.RefreshAbilityBar(local);
                PickSession.UpdateBanner();
            }

            if (Time.unscaledTime < _nextArrivalCheck) return;
            _nextArrivalCheck = Time.unscaledTime + 0.25f;
            CheckArrivals(local);
        }

        private static int _lastSegmentNumber = int.MinValue;

        private static void PollBiome(Character local) { PollBiomeNow(local); }

        /// <summary>Checks the map's segment number right now; true when it moved and a biome boundary was raised.</summary>
        private static bool PollBiomeNow(Character local)
        {
            if (local == null) return false;
            int seg;
            try { seg = (int)MapHandler.CurrentSegmentNumber; }
            catch { return false; }
            if (seg == _lastSegmentNumber) return false;
            _lastSegmentNumber = seg;
            LocalCards.OnSegmentNumber(local, seg);
            return true;
        }

        /// <summary>
        /// The manual ability and Current Build buttons (config Keys, keyboard or controller), one activation per press.
        /// Overlapping bindings ask the player to assign separate keys. Ignored while a menu or the card picker has focus.
        /// </summary>
        private static void AbilityKeys(Character local)
        {
            bool blocked = false;
            try { blocked = GUIManager.instance != null && (GUIManager.instance.windowBlockingInput || GUIManager.instance.wheelActive); } catch { }
            if (blocked) return;
            if (!local.data.dead && !local.data.passedOut && !local.data.fullyPassedOut)
            {
                var handled = new HashSet<Controls.Act>();
                foreach (var a in LocalCards.Manual)
                {
                    var act = Controls.ActOf(a.Card.Ability);
                    if (act == null || !handled.Add(act.Value) || !Controls.WasPressed(act.Value)) continue;
                    var choices = new List<Ability>();
                    foreach (var other in LocalCards.Manual)
                    {
                        var otherAct = Controls.ActOf(other.Card.Ability);
                        if (otherAct != null && Controls.SharesButton(act.Value, otherAct.Value)) choices.Add(other);
                    }
                    if (choices.Count > 1) { Hud.Toast(Ui_Toast.SharedKeyText, warning: true); break; }
                    try { a.Activate(local); }
                    catch (Exception e) { Plugin.Log.LogError($"{a.Card.Name} Activate: {e}"); }
                }
            }
            if (Controls.WasPressed(Controls.Act.BuildScreen)) DeckScreen.Toggle(local);
            if (Controls.WasPressed(Controls.Act.BugReport)) BugReportScreen.Toggle();
        }

        /// <summary>
        /// hooks.json pausePressed: the pause press belongs to whichever mod window is open - the deck screen backs
        /// out one step, the mythic chooser postpones - so the pause menu never opens on top of them. Keyboard Esc
        /// raises Pause and UICancel together, and the windows consume both in their own Update; this prefix is the
        /// net under them for inputs that raise Pause without UICancel (a controller's Start) and for any press that
        /// still finds the window open at LateUpdate. Any other frame vanilla pause runs untouched.
        /// </summary>
        public static bool OnUpdatePausedPrefix(GUIManager __instance)
        {
            var local = Character.localCharacter;
            if (local == null || local.input == null || !local.input.pauseWasPressed) return true;
            if (DeckScreen.Instance != null && DeckScreen.Instance.isOpen)
            {
                local.input.pauseWasPressed = false;
                DeckScreen.Instance.BackFromInput();
                return false;
            }
            if (ItemChooser.IsOpen)
            {
                local.input.pauseWasPressed = false;
                ItemChooser.Instance.HideNow();
                return false;
            }
            if (BugReportScreen.Instance != null && BugReportScreen.Instance.isOpen)
            {
                local.input.pauseWasPressed = false;   // a controller Start raises Pause without UICancel
                BugReportScreen.Instance.CloseFromInput();   // the draft is static and survives
                return false;
            }
            return true;
        }

        private static void CheckArrivals(Character local)
        {
            // opening pick readiness: the beach wake-up is over and we have stood on the ground for a moment
            if (!RunState.SentOpeningArrival)
            {
                bool awake = local.data.passedOutOnTheBeach <= 0f && !local.warping && local.data.isGrounded && !local.data.passedOut;
                if (awake)
                {
                    if (RunState.GroundedSince < 0f) RunState.GroundedSince = Time.unscaledTime;
                    if (Time.unscaledTime - RunState.GroundedSince >= UiTiming.OpeningPickDelaySeconds)
                    {
                        RunState.SentOpeningArrival = true;
                        NetSend.RaiseToMaster(new Arrival { segment = 255, time = PhotonNetwork.Time });
                        PickSession.RecordArrival(NetSend.LocalActor, 255, PhotonNetwork.Time);   // known here too if we become host
                    }
                }
                else RunState.GroundedSince = -1f;
            }

            // campfire arrival: first time we are within the fire's rest radius
            Campfire fire = null;
            try { fire = MapHandler.CurrentCampfire; } catch { }
            if (fire != null && !fire.Lit && !RunState.ArrivedFires.Contains(fire))
            {
                float radius = fire.moraleBoostRadius > 0f ? fire.moraleBoostRadius : 15f;
                if (Vector3.Distance(local.Center, fire.transform.position) <= radius)
                {
                    byte seg = FireKey(fire);
                    RunState.ArrivedFires.Add(fire);
                    NetSend.RaiseToMaster(new Arrival { segment = seg, time = PhotonNetwork.Time });
                    PickSession.RecordArrival(NetSend.LocalActor, seg, PhotonNetwork.Time);
                }
            }
        }

        // ------------------------------------------------------------------ run restore
        /// <summary>
        /// Keys.RestoreRun (F8): the host gives everyone in the room their cards from the saved run back
        /// (Cards/RunMemoryFile) after a crash, a rejoin or a relaunch. A guest's press points them at the host.
        /// </summary>
        private static void RestoreKey(Character local)
        {
            var key = Plugin.RestoreRunKey;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || key == null || key.Value == UnityEngine.InputSystem.Key.None) return;
            // same gate as AbilityKeys: never while a menu has input focus - PEAK's rebind screen accepts F8 as a
            // binding, and that press must not also fire a lobby-wide restore
            bool blocked = false;
            try { blocked = GUIManager.instance != null && (GUIManager.instance.windowBlockingInput || GUIManager.instance.wheelActive); } catch { }
            if (blocked) return;
            UnityEngine.InputSystem.Controls.KeyControl control = null;
            try { control = kb[key.Value]; } catch { }
            if (control == null || !control.wasPressedThisFrame) return;
            if (!NetSend.IsHost) { Ui.Hud.Toast("Card restore: only the host can restore the run"); return; }
            RunMemoryFile.RestoreAll();
        }
    }
}
