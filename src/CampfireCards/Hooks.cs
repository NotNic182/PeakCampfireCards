using System;
using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using CampfireCards.Abilities;
using CampfireCards.Cards;
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
        public static readonly HashSet<byte> ArrivedSegments = new HashSet<byte>();

        /// <summary>Everything the mod changed goes back to vanilla: cards, flags, timers, stat changes, UI.</summary>
        public static void Reset()
        {
            RunStarted = false;
            SentOpeningArrival = false;
            GroundedSince = -1f;
            ArrivedSegments.Clear();
            Effects.Reset();
            PickSession.Reset();
            Ability_EternalCampfire.Arbiter.Reset();
            NetHandlers.Reset();
            Hooks.ResetRunTracking();
            Hud.ClearMarkers();
            BuildScreen.Hide();
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
            PickSession.OnRunStarted();
            Hud.Ensure();
            if (!NetSend.IsHost) NetSend.Raise(NetHandlers.MakeHello(), Photon.Realtime.ReceiverGroup.Others);
            Plugin.Log.LogInfo($"Run started. Host={NetSend.IsHost} actor={NetSend.LocalActor} solo={NetSend.IsSolo} deck={Deck.Count} cards");
        }

        /// <summary>hooks.json endGame: the run is over (end screen). Cards, timers and stat changes are cleared.</summary>
        public static void OnRunEnded()
        {
            RunState.Reset();
            Hud.CloseAll();
        }

        /// <summary>hooks.json campfireLit prefix: remember the state before the RPC.</summary>
        public static void OnCampfireLitPrefix(Campfire __instance, out Campfire.FireState __state)
        {
            __state = __instance.state;
        }

        /// <summary>hooks.json campfireLit postfix: Off -> Lit with updateSegment is a real lighting.</summary>
        public static void OnCampfireLit(Campfire __instance, bool updateSegment, float burningFor, Campfire.FireState __state)
        {
            if (__state == Campfire.FireState.Lit || !updateSegment) return;   // late-join sync, quicksave or debug
            if (!RunState.RunStarted) return;
            Plugin.Verbose($"Campfire lit -> {__instance.advanceToSegment}");
            if (NetSend.IsHost) HostCampfireLit(__instance);
            var local = Character.localCharacter;
            // card effects first, so cards picked at this fire do not fire for it
            if (local != null) LocalCards.OnCampfireLit(local, __instance);
            PickSession.RequestSession(1, (byte)__instance.advanceToSegment);   // every client queues it; the host deals
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

            if (!RunState.OnIsland) return;
            var local = Character.localCharacter;
            if (local == null) return;

            Hud.Ensure();
            if (!RunState.RunStarted) return;

            if (Plugin.DevKeys != null && Plugin.DevKeys.Value) DevKeys(local);

            PollBiome(local);
            Effects.Recompute(local);
            LocalCards.Tick(local, Time.deltaTime);
            FlushItemUses(local);
            AbilityKeys(local);
            PickSession.HostTick();
            BuildScreen.Tick(local);

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

        private static void PollBiome(Character local)
        {
            int seg;
            try { seg = (int)MapHandler.CurrentSegmentNumber; }
            catch { return; }
            if (seg != LocalCards.Biome) LocalCards.OnBiomeStarted(local, seg);
        }

        /// <summary>Blood Battery, Safety Net and Current Build buttons (config Keys, keyboard or controller). Ignored while a menu or the card picker has focus.</summary>
        private static void AbilityKeys(Character local)
        {
            bool blocked = false;
            try { blocked = GUIManager.instance != null && (GUIManager.instance.windowBlockingInput || GUIManager.instance.wheelActive); } catch { }
            if (blocked) return;
            TryKey(Controls.Act.BloodBattery, "BloodBattery", local);
            TryKey(Controls.Act.SafetyNet, "SafetyNet", local);
            if (Controls.WasPressed(Controls.Act.BuildScreen)) BuildScreen.Toggle(local);
        }

        private static void TryKey(Controls.Act act, string ability, Character local)
        {
            if (!Controls.WasPressed(act)) return;
            foreach (var a in LocalCards.Manual)
            {
                if (a.Card.Ability != ability) continue;
                try { a.Activate(local); }
                catch (Exception e) { Plugin.Log.LogError($"{a.Card.Name} Activate: {e}"); }
                return;
            }
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
            if (fire != null && !fire.Lit)
            {
                byte seg = (byte)fire.advanceToSegment;
                if (!RunState.ArrivedSegments.Contains(seg))
                {
                    float radius = fire.moraleBoostRadius > 0f ? fire.moraleBoostRadius : 15f;
                    if (Vector3.Distance(local.Center, fire.transform.position) <= radius)
                    {
                        RunState.ArrivedSegments.Add(seg);
                        NetSend.RaiseToMaster(new Arrival { segment = seg, time = PhotonNetwork.Time });
                        PickSession.RecordArrival(NetSend.LocalActor, seg, PhotonNetwork.Time);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ dev keys
        /// <summary>Testing keys (config Debug.DevKeys): F9 light the current campfire, F10 warp next to it, F11 sample offer, F8 grant a card.</summary>
        private static void DevKeys(Character local)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.f9Key.wasPressedThisFrame)
            {
                Campfire fire = null;
                try { fire = MapHandler.CurrentCampfire; } catch { }
                if (fire != null && !fire.Lit) { Plugin.Log.LogInfo("DevKeys: lighting " + fire.name); fire.DebugLight(); }
                else Plugin.Log.LogInfo("DevKeys: no unlit current campfire");
            }
            if (kb.f10Key.wasPressedThisFrame)
            {
                Campfire fire = null;
                try { fire = MapHandler.CurrentCampfire; } catch { }
                if (fire != null)
                {
                    Vector3 pos = fire.transform.position + fire.transform.forward * 3f + Vector3.up * 1.5f;
                    Plugin.Log.LogInfo("DevKeys: warping to " + fire.name + " at " + pos);
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, pos, true);
                }
            }
            if (kb.f11Key.wasPressedThisFrame && Deck.Ready)
            {
                var hand = Deck.Deal(new System.Random(), Effects.CardsOf(NetSend.LocalActor), Core.CardSettings.Current.OfferCount, NetSend.IsSolo);
                CardPicker.Ensure().Show(1, hand, 60f, CardPicker.Mode.Test);
                Plugin.Log.LogInfo("DevKeys: sample offer " + string.Join(",", hand));
            }
            if (kb.f8Key.wasPressedThisFrame && NetSend.IsHost)
            {
                // re-read the file so GrantCard can be edited while the game runs
                try { Plugin.Instance.Config.Reload(); } catch (Exception e) { Plugin.Log.LogWarning("Config reload: " + e.Message); }
                var wanted = (Plugin.DevGrantCard?.Value ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var ids = new List<byte>();
                foreach (var key in wanted)
                {
                    var def = Array.Find(CardTable.Cards, c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (def != null) ids.Add(def.Id); else Plugin.Log.LogWarning("DevKeys: no card " + key);
                }
                if (ids.Count > 0)
                {
                    Plugin.Log.LogInfo("DevKeys: granting " + string.Join(", ", ids.ConvertAll(Deck.NameOf)));
                    NetSend.Raise(new CardsGranted { sessionId = PickSession.SessionId, actor = NetSend.LocalActor, cards = ids.ToArray(), reason = 9 }, Photon.Realtime.ReceiverGroup.All);
                }
            }
            if (kb.f7Key.wasPressedThisFrame) DumpEffects(local);
        }

        /// <summary>Dev: log every channel that differs from neutral and the scout fields the cards drive.</summary>
        private static void DumpEffects(Character local)
        {
            var sb = new System.Text.StringBuilder("DevKeys: effects");
            foreach (Channel ch in Enum.GetValues(typeof(Channel)))
            {
                float v = Effects.Local[ch];
                float neutral = ChannelTable.Defs[(int)ch].IsMul ? 1f : 0f;
                if (Mathf.Abs(v - neutral) > 0.0001f) sb.Append($" {ch}={v:0.###}");
            }
            var mv = local.refs.movement;
            sb.Append($" | sprintMultiplier={mv.sprintMultiplier:0.###} jumpImpulse={mv.jumpImpulse:0.###} sprintStaminaUsage={mv.sprintStaminaUsage:0.####}");
            sb.Append($" maxStamina={local.GetMaxStamina():0.###} statusSum={local.refs.afflictions.statusSum:0.###} stamina={local.data.currentStamina:0.###} extra={local.data.extraStamina:0.###}");
            sb.Append($" weight={local.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Weight):0.###} biome={LocalCards.Biome}");
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}
