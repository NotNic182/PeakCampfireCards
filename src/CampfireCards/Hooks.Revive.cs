using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Net;
using Photon.Pun;
using UnityEngine;

namespace CampfireCards
{
    /// <summary>Going down, dying, waking up and who revived whom (Safety Net, Eternal Campfire, Phoenix Clause, Hero's Arrival).</summary>
    public static partial class Hooks
    {
        private static int _lastFeederActor = int.MinValue;
        private static float _lastFedTime;

        /// <summary>hooks.json passOut: the local scout went down. Revival cards get their one chance for this incapacitation.</summary>
        public static void OnPassedOut(Character __instance)
        {
            if (!Mine(__instance) || __instance.isBot) return;   // a host-owned bot is IsLocal on the host, but never a card holder
            LocalCards.OnDown(__instance);
        }

        public static void OnDiedPrefix(Character __instance, out bool __state)
        {
            __state = __instance.data != null && (__instance.data.passedOut || __instance.data.fullyPassedOut);
            // the master runs CheckEndGame synchronously inside RPCA_Die, before the victim's ReviveRequest can make
            // the round trip: when an Eternal Campfire revive is plausible for this death, hold the end-of-run check
            // (hooks.json endGameCheck) so the last living scout - and every solo owner - can still be brought back.
            // The prefix runs before RPCA_Die warps the body away, so __instance.Center is still the death spot.
            if (NetSend.IsHost && EternalReviveLikely(__instance))
            {
                _endGameHoldUntil = Time.time + 8f;
                _endGameRecheck = __instance;
            }
        }

        /// <summary>
        /// hooks.json died and zombifyDied: an instant death (no pass-out first) still gives the anchor its chance;
        /// The Eternal Campfire's shared charge is spent here, on real death, never on a recoverable pass-out.
        /// </summary>
        public static void OnDied(Character __instance, bool __state)
        {
            if (!Mine(__instance) || __instance.isBot) return;   // a host-owned bot is IsLocal on the host, but never a card holder
            if (!__state) LocalCards.OnDown(__instance);
            LocalCards.OnDeath(__instance);
        }

        // ------------------------------------------------------------------ end-of-run hold (Eternal Campfire)
        private static float _endGameHoldUntil;
        private static Character _endGameRecheck;

        /// <summary>RunState.Reset: Time.time is monotonic across scene loads, so a leftover hold must not leak into the next run.</summary>
        internal static void ResetEndGameHold()
        {
            _endGameHoldUntil = 0f;
            _endGameRecheck = null;
        }

        /// <summary>hooks.json endGameCheck prefix: skip the game-over check while a death-revive may be in flight.</summary>
        public static bool OnCheckEndGamePrefix(Character __instance)
        {
            if (Time.time >= _endGameHoldUntil) return true;
            _endGameRecheck = __instance;
            return false;
        }

        /// <summary>From Hooks.Tick: a held end-of-run check runs once the window closes (a failed revive still ends the run, late).</summary>
        internal static void TickEndGameHold()
        {
            if (_endGameRecheck == null || Time.time < _endGameHoldUntil) return;
            var ch = _endGameRecheck;
            _endGameRecheck = null;
            try { ch.CheckEndGame(); } catch (System.Exception e) { Plugin.Log.LogWarning("End-game recheck: " + e.Message); }
        }

        /// <summary>An unspent Eternal Campfire whose owner is the dying scout, or a living owner in aura range of it.</summary>
        private static bool EternalReviveLikely(Character victim)
        {
            try
            {
                if (victim == null || victim.isBot) return false;   // a host-owned bot shares the host's actor number
                int victimActor = NetSend.ActorOf(victim);
                var s = Core.CardSettings.Current;
                foreach (var kv in Effects.CardsByActor)
                {
                    foreach (var id in kv.Value)
                    {
                        var def = s.Card(id);
                        if (def == null || def.Ability != "EternalCampfire") continue;
                        if (Ability_EternalCampfire.Arbiter.UsedInBiome(kv.Key, LocalCards.Biome)) continue;
                        if (kv.Key == victimActor) return true;   // an owner's own death is always inside its aura
                        Character owner;
                        if (!PlayerHandler.TryGetCharacter(kv.Key, out owner) || owner == null || owner.data == null || owner.data.dead) continue;
                        if (Vector3.Distance(owner.Center, victim.Center) <= s.Param(def, "radius", 15f) + 2f) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>hooks.json fedItem: remember who fed the downed local scout.</summary>
        public static bool OnFedItemPrefix(Character __instance, int itemPhotonID)
        {
            if (!RunState.RunStarted) return true;
            try
            {
                var view = PhotonView.Find(itemPhotonID);
                var item = view != null ? view.GetComponent<Item>() : null;
                if (!Mine(__instance) || !(__instance.data.passedOut || __instance.data.fullyPassedOut)) return true;
                var feeder = item != null ? item.trueHolderCharacter : null;
                if (feeder == null || feeder == __instance) return true;
                _lastFeederActor = NetSend.ActorOf(feeder);
                _lastFedTime = Time.time;
            }
            catch { }
            return true;
        }

        /// <summary>hooks.json unPassOut: the local scout woke up; if a teammate's feeding did it, tell that teammate.</summary>
        public static void OnUnPassedOut(Character __instance)
        {
            if (!Mine(__instance)) return;
            LocalCards.OnRecovered();
            if (_lastFeederActor != int.MinValue && Time.time - _lastFedTime < 20f)
                NetSend.RaiseTo(new RevivedBy { victimActor = NetSend.LocalActor }, _lastFeederActor);
            _lastFeederActor = int.MinValue;
        }

        /// <summary>hooks.json statueRevive: the local scout opened a revive statue while a teammate was down or dead.</summary>
        public static void OnRespawnChestUsedPrefix(Character interactor)
        {
            if (!Mine(interactor)) return;
            bool canRevive = true;
            try { canRevive = Ascents.canReviveDead; } catch { }
            if (!canRevive) return;
            foreach (var c in Character.AllCharacters)
            {
                if (c == null || c == interactor || c.isBot || c.data == null) continue;
                if (!(c.data.dead || c.data.fullyPassedOut)) continue;
                Ability_HerosArrival.OnRevivedTeammate(NetSend.ActorOf(c));
                return;
            }
        }

        /// <summary>hooks.json killPlane: Phoenix Clause cancels the kill plane once per run (false skips the game's death).</summary>
        public static bool OnKillPlanePrefix(Collision other)
        {
            if (other == null || other.collider == null) return true;
            Character c;
            if (!CharacterRagdoll.TryGetCharacterFromCollider(other.collider, out c) || !Mine(c)) return true;
            var phoenix = Ability_PhoenixClause.Owned;
            return phoenix == null || !phoenix.TryPreventKillPlane(c);
        }
    }
}
