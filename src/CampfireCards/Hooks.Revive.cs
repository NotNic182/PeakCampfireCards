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
            if (!Mine(__instance)) return;
            LocalCards.OnDown(__instance);
        }

        public static void OnDiedPrefix(Character __instance, out bool __state)
        {
            __state = __instance.data != null && (__instance.data.passedOut || __instance.data.fullyPassedOut);
        }

        /// <summary>hooks.json died: a death without a pass-out first (instant death) is its own incapacitation.</summary>
        public static void OnDied(Character __instance, bool __state)
        {
            if (!Mine(__instance) || __state) return;
            LocalCards.OnDown(__instance);
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
