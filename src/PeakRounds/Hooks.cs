using System;
using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using HarmonyLib;
using PeakRounds.Cards;
using PeakRounds.Generated;
using PeakRounds.Net;
using PeakRounds.Rounds;
using PeakRounds.Ui;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRounds
{
    /// <summary>Per-run state shared by the hooks.</summary>
    public static class RunState
    {
        public static bool OnIsland;
        public static bool RunStarted;
        public static bool SentOpeningArrival;
        public static float GroundedSince = -1f;
        public static readonly HashSet<byte> ArrivedSegments = new HashSet<byte>();
        public static bool MissingNoticeShown;
        public static bool DeckBuilt;

        public static void Reset()
        {
            RunStarted = false;
            SentOpeningArrival = false;
            GroundedSince = -1f;
            ArrivedSegments.Clear();
            MissingNoticeShown = false;
            Effects.Reset();
            PickSession.Reset();
        }

        public static bool IsGameplayScene(string name) => name != null && (name.Contains("Island") || name.Contains("Level_"));
    }

    /// <summary>The handler bodies behind design/hooks.json. Generated/HarmonyHooks.cs forwards into these.</summary>
    public static class Hooks
    {
        // ---- reflection into private game members (names verified in design/hooks.json) ----
        private static readonly Func<Character, bool> CanRegenStamina =
            AccessTools.MethodDelegate<Func<Character, bool>>(AccessTools.Method(typeof(Character), "CanRegenStamina"));
        private static readonly AccessTools.FieldRef<CharacterMovement, Character> MovementCharacter =
            AccessTools.FieldRefAccess<CharacterMovement, Character>("character");
        private static readonly System.Reflection.MethodInfo AddForceM =
            AccessTools.Method(typeof(Character), "AddForce", new[] { typeof(Vector3), typeof(float), typeof(float) });

        /// <summary>Set while CharacterMovement.CheckFallDamage runs, so the AddStatus prefix knows the injury is a fall.</summary>
        private static Character _fallDamageCharacter;
        private static float _nextArrivalCheck;
        private static bool _photonSubscribed;

        // ------------------------------------------------------------------ subscriptions (event / unity rows)
        public static void Subscribe()
        {
            PlayerHandler.CharacterRegistered += OnCharacterRegistered;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SubscribePhoton();
            SubscribeGlobalEvents();
        }

        private static void SubscribePhoton()
        {
            if (_photonSubscribed) return;
            var client = PhotonNetwork.NetworkingClient;
            if (client == null) return;
            client.EventReceived += OnPhotonEvent;
            _photonSubscribed = true;
        }

        private static void SubscribeGlobalEvents()
        {
            GlobalEvents.OnItemConsumed -= OnItemConsumed;
            GlobalEvents.OnItemConsumed += OnItemConsumed;
        }

        private static IEnumerator ResubscribeNextFrame()
        {
            yield return null;
            SubscribeGlobalEvents();
            SubscribePhoton();
        }

        // ------------------------------------------------------------------ harmony handlers
        /// <summary>hooks.json runStart: RunManager.StartRun postfix.</summary>
        public static void OnRunStarted()
        {
            RunState.Reset();
            RunState.RunStarted = true;
            RunState.OnIsland = true;
            PickSession.OnRunStarted();
            Hud.Ensure();
            if (!NetSend.IsHost) NetSend.Raise(new Hello { modVersion = Plugin.Version, hasRounds = Deck.Ready, cardCount = Deck.Playable.Count }, Photon.Realtime.ReceiverGroup.Others);
            Plugin.Log.LogInfo($"Run started. Host={NetSend.IsHost} actor={NetSend.LocalActor} deckReady={Deck.Ready} ({Deck.Playable.Count} cards)");
        }

        /// <summary>hooks.json campfireLit prefix: remember the state before the RPC.</summary>
        public static void OnCampfireLitPrefix(Campfire __instance, out Campfire.FireState __state)
        {
            __state = __instance.state;
        }

        /// <summary>hooks.json campfireLit postfix: Off -> Lit with updateSegment is a real lighting.</summary>
        public static void OnCampfireLit(Campfire __instance, bool updateSegment, float burningFor, Campfire.FireState __state)
        {
            if (__state == Campfire.FireState.Lit) return;
            if (!updateSegment) return; // late-join sync or debug jump
            Plugin.Verbose($"Campfire lit -> {__instance.advanceToSegment}");
            var local = Character.localCharacter;
            if (local != null) Effects.OnCampfire(local);
            PickSession.HostStart(1, (byte)__instance.advanceToSegment);
        }

        /// <summary>hooks.json addStatus prefix: scale incoming status by toughness, resists and fall damage.</summary>
        public static void ScaleIncomingStatusPrefix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount)
        {
            if (amount <= 0f) return;
            var ch = __instance.character;
            if (ch == null) return;
            var stats = Effects.StatsFor(ch);
            if (stats == null) return;
            amount = Effects.ScaleStatus(stats, statusType, amount, _fallDamageCharacter == ch);
        }

        /// <summary>hooks.json fallDamage prefix: returns false to skip the game's fall damage (bouncy landing).</summary>
        public static bool OnCheckFallDamagePrefix(CharacterMovement __instance)
        {
            var ch = MovementCharacter(__instance);
            _fallDamageCharacter = ch;
            if (ch == null) return true;
            var stats = Effects.StatsFor(ch);
            if (stats == null) return true;
            if (stats[(int)ClimbStat.bounceOnLand] > 0.5f && __instance.FallTime() > 1.5f)
            {
                _fallDamageCharacter = null;
                if (ch.IsLocal && AddForceM != null)
                {
                    float impulse = Mathf.Clamp(Mathf.Abs(ch.data.avarageLastFrameVelocity.y), 8f, 30f) * 0.9f;
                    AddForceM.Invoke(ch, new object[] { Vector3.up * impulse, 1f, 1f });
                    Hud.Toast("BOUNCY!");
                }
                return false;
            }
            return true;
        }

        /// <summary>hooks.json fallDamage postfix.</summary>
        public static void OnCheckFallDamage(CharacterMovement __instance)
        {
            _fallDamageCharacter = null;
        }

        /// <summary>hooks.json staminaTick: extra regen on top of the game's 0.2/s.</summary>
        public static void OnStaminaTick(Character __instance)
        {
            if (!__instance.IsLocal) return;
            var stats = Effects.StatsFor(__instance);
            if (stats == null) return;
            float bonus = stats[(int)ClimbStat.staminaRegen];
            if (bonus == 0f) return;
            if (!CanRegenStamina(__instance)) return;
            __instance.AddStamina(Time.fixedDeltaTime * 0.2f * bonus);
        }

        /// <summary>hooks.json extraJumps.</summary>
        public static void OnUpdateExtraJumps(CharacterAfflictions __instance)
        {
            var ch = __instance.character;
            if (ch == null) return;
            var stats = Effects.StatsFor(ch);
            if (stats == null) return;
            __instance.totalExtraJumps += Mathf.RoundToInt(stats[(int)ClimbStat.extraJumps]);
        }

        /// <summary>hooks.json revive.</summary>
        public static void OnRevived(Character __instance)
        {
            Effects.Apply(__instance);
        }

        /// <summary>hooks.json lobbyCreated: tell Melty where this host is.</summary>
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

        /// <summary>hooks.json endGame.</summary>
        public static void OnRunEnded()
        {
            Hud.CloseAll();
            PickSession.Reset();
        }

        // ------------------------------------------------------------------ event handlers
        /// <summary>hooks.json characterRegistered.</summary>
        public static void OnCharacterRegistered(Character c)
        {
            if (c == null) return;
            try
            {
                Effects.Apply(c);
                int actor = NetSend.ActorOf(c);
                if (c.IsLocal)
                {
                    if (!NetSend.IsHost && !Effects.HasAnyState)
                        NetSend.RaiseToMaster(new StateRequest { modVersion = Plugin.Version });
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

        /// <summary>hooks.json itemConsumed: lifesteal.</summary>
        public static void OnItemConsumed(Item item, Character c)
        {
            if (c == null || !c.IsLocal) return;
            var stats = Effects.StatsFor(c);
            if (stats == null) return;
            float ls = stats[(int)ClimbStat.lifesteal];
            if (ls <= 0f) return;
            c.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Injury, ls * 0.1f);
            Hud.Toast($"Lifesteal: eating healed {Mathf.RoundToInt(ls * 10f)}% injury");
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
                    return;
                }
                if (e.Code < 90 || e.Code > 99) return;
                var data = e.CustomData as byte[];
                if (data == null) return;
                NetDispatcher.Dispatch(e.Code, data, e.Sender);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Net event {e.Code}: {ex}");
            }
        }

        private static readonly System.Reflection.MethodInfo WarpM =
            AccessTools.Method(typeof(Character), "WarpPlayer", new[] { typeof(Vector3), typeof(bool) });

        /// <summary>Testing keys (config Debug.DevKeys): F9 light the current campfire, F10 warp next to it, F11 sample fan.</summary>
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
                if (fire != null && WarpM != null)
                {
                    Vector3 pos = fire.transform.position + fire.transform.forward * 3f + Vector3.up * 1.5f;
                    Plugin.Log.LogInfo("DevKeys: warping to " + fire.name + " at " + pos);
                    WarpM.Invoke(local, new object[] { pos, true });
                }
            }
            if (kb.f11Key.wasPressedThisFrame && Deck.Ready)
            {
                var hand = Deck.Deal(new System.Random(), Effects.CardsOf(NetSend.LocalActor), Generated.CardTable.HandSize);
                CardPicker.Ensure().Show(1, hand, 60f, true);
                Plugin.Log.LogInfo("DevKeys: sample fan " + string.Join(",", hand));
            }
        }

        // ------------------------------------------------------------------ per frame
        public static void Tick()
        {
            if (RoundsReader.Ready && !RunState.DeckBuilt)
            {
                RunState.DeckBuilt = true;
                Deck.Build(RoundsReader.Data);
            }

            if (!RunState.OnIsland) return;
            var local = Character.localCharacter;
            if (local == null) return;

            Hud.Ensure();
            if (RunState.DeckBuilt && !RunState.MissingNoticeShown && !Deck.Ready)
            {
                RunState.MissingNoticeShown = true;
                var d = RoundsReader.Data;
                Hud.ShowMissing(d != null && !d.NotInstalled ? string.Format(Ui_RoundsMissingNotice.ReadFailedText, d.Error) : Ui_RoundsMissingNotice.Text);
            }

            if (!RunState.RunStarted) return;

            if (Plugin.DevKeys != null && Plugin.DevKeys.Value) DevKeys(local);
            Effects.Tick(local);
            PickSession.HostTick();

            if (Time.unscaledTime < _nextArrivalCheck) return;
            _nextArrivalCheck = Time.unscaledTime + 0.25f;

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
                    }
                }
            }
        }
    }
}
