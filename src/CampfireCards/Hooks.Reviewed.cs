using System;
using System.Collections.Generic;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using UnityEngine;

namespace CampfireCards
{
    /// <summary>Local provenance tag; every client receives the dynamite RPC and tags its own AOE instance.</summary>
    public sealed class DynamiteCardBlast : MonoBehaviour { }
    public sealed class DynamiteSpawnState
    {
        public int PreviousDepth;
        public HashSet<int> Existing;
    }

    public static partial class Hooks
    {
        [ThreadStatic] private static int _dynamiteSpawnDepth;
        [ThreadStatic] private static bool _dynamiteBlast;

        public static void OnDynamiteSourcePrefix(out DynamiteSpawnState __state)
        {
            __state = null;
            if (!RunState.RunStarted) return;
            __state = new DynamiteSpawnState { PreviousDepth = _dynamiteSpawnDepth, Existing = new HashSet<int>() };
            foreach (var aoe in UnityEngine.Object.FindObjectsOfType<AOE>(true)) __state.Existing.Add(aoe.GetInstanceID());
            _dynamiteSpawnDepth++;
        }

        public static Exception OnDynamiteSourceFinalizer(Exception __exception, DynamiteSpawnState __state)
        {
            if (__state == null) return __exception;
            _dynamiteSpawnDepth = __state.PreviousDepth;
            foreach (var aoe in UnityEngine.Object.FindObjectsOfType<AOE>(true))
                if (!__state.Existing.Contains(aoe.GetInstanceID()) && aoe.GetComponent<DynamiteCardBlast>() == null)
                    aoe.gameObject.AddComponent<DynamiteCardBlast>();
            return __exception;
        }

        public static void OnBlastContextPrefix(AOE __instance, out bool __state)
        {
            __state = _dynamiteBlast;
            _dynamiteBlast = _dynamiteSpawnDepth > 0 || __instance.GetComponent<DynamiteCardBlast>() != null;
        }
        public static Exception OnBlastContextFinalizer(Exception __exception, bool __state)
        {
            _dynamiteBlast = __state;
            return __exception;
        }

        public static void OnThornInteractTime(ThornOnMe __instance, ref float __result) => ScaleThornTime(__instance, ref __result);
        public static void OnThornPopOutTime(ThornOnMe __instance, ref float __result) => ScaleThornTime(__instance, ref __result);
        private static void ScaleThornTime(ThornOnMe thorn, ref float time)
        {
            if (!RunState.RunStarted || thorn == null || !thorn.isThorn || thorn.character == null || time <= 0f) return;
            time *= Effects.ActorChannel(NetSend.ActorOf(thorn.character), Channel.thornRemovalTime);
        }

        public static void OnPostReviveStatusPrefix(CharacterAfflictions afflictions, out float __state)
        {
            __state = -1f;
            if (afflictions != null && Mine(afflictions.character)) __state = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Curse);
        }
        public static void OnPostReviveStatus(CharacterAfflictions afflictions, float __state)
        {
            if (__state < 0f || afflictions == null || !Mine(afflictions.character)) return;
            float multiplier = Effects.ActorChannel(NetSend.ActorOf(afflictions.character), Channel.reviveCurse);
            float current = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Curse);
            float adjusted = ReviewedEffectMath.RevivalCurse(__state, current, multiplier);
            if (Mathf.Abs(adjusted - current) > .00001f) afflictions.SetStatus(CharacterAfflictions.STATUSTYPE.Curse, adjusted);
        }

        /// <summary>
        /// hooks.json carriedDeath (Character.UpdateVariablesFixed, where the game advances deathTimer). Only a fully
        /// passed-out scout's timer runs in vanilla, so only that state is measured.
        /// </summary>
        public static void OnCarriedDeathPrefix(Character __instance, out float __state)
        {
            __state = -1f;
            if (Mine(__instance) && !__instance.data.dead && __instance.data.fullyPassedOut)
                __state = __instance.data.deathTimer;
        }
        public static void OnCarriedDeath(Character __instance, float __state)
        {
            if (__state < 0f || !Mine(__instance) || __instance.data.dead || !__instance.data.fullyPassedOut) return;
            var carrier = __instance.data.carrier;
            if (carrier == null || carrier.data.dead) return;
            float multiplier = Effects.ActorChannel(NetSend.ActorOf(carrier), Channel.carriedDeathRate);
            // Carrying normally pauses this timer. Charge the excess 15% of a 60-second normal timer while paused;
            // if native rules (e.g. spores) still advance it, scale that actual progression instead.
            __instance.data.deathTimer = ReviewedEffectMath.CarriedDeathTimer(__state, __instance.data.deathTimer, multiplier, Time.fixedDeltaTime / 60f);
        }
    }
}
