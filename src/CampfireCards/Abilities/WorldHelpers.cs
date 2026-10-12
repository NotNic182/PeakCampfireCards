using System;
using System.Collections;
using CampfireCards.Cards;
using CampfireCards.Net;
using Photon.Pun;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>Validated positions for teleports, revivals and item spawns. Never returns a spot inside terrain.</summary>
    public static class SafeSpot
    {
        private const float MaxSlope = 50f;   // the game's own walkable limit (CharacterMovement.maxAngle)

        /// <summary>Is there static, walkable ground right below this point with room for a scout above it? pos = torso target.</summary>
        public static bool Validate(Vector3 candidate, out Vector3 pos)
        {
            pos = candidate;
            RaycastHit hit = HelperFunctions.LineCheck(candidate + Vector3.up * 1.5f, candidate + Vector3.down * 4f, HelperFunctions.LayerType.TerrainMap);
            if (hit.transform == null) return false;
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic) return false;
            if (Vector3.Angle(hit.normal, Vector3.up) >= MaxSlope) return false;
            // headroom: nothing solid in the body's space
            if (Physics.CheckSphere(hit.point + Vector3.up * 0.9f, 0.35f, HelperFunctions.AllPhysicalExceptCharacter)) return false;
            if (Physics.CheckSphere(hit.point + Vector3.up * 1.6f, 0.35f, HelperFunctions.AllPhysicalExceptCharacter)) return false;
            pos = hit.point + Vector3.up * 1.0f;
            return true;
        }

        /// <summary>A safe spot on rings around an origin (first ring that works).</summary>
        public static bool Near(Vector3 origin, float minRadius, float maxRadius, out Vector3 pos)
        {
            if (minRadius <= 0.01f && Validate(origin, out pos)) return true;
            for (float r = Mathf.Max(minRadius, 0.75f); r <= maxRadius; r += 0.75f)
            {
                foreach (var dir in HelperFunctions.GetCircularDirections(12))
                {
                    var p = origin + new Vector3(dir.x, 0f, dir.z).normalized * r;
                    if (Validate(p, out pos)) return true;
                }
            }
            pos = origin;
            return false;
        }

        /// <summary>
        /// An item spot near a campfire: out of the fire, on static walkable ground, in sight of the fire. salt rotates
        /// the starting direction so several players' items do not land on the same spot.
        /// </summary>
        public static bool NearCampfire(Campfire fire, int salt, out Vector3 point, out Vector3 normal)
        {
            Vector3 c = fire.transform.position;
            var dirs = HelperFunctions.GetCircularDirections(10);
            foreach (float r in new[] { 2.75f, 3.25f, 3.75f, 4.5f })
            {
                for (int k = 0; k < dirs.Length; k++)
                {
                    var dir = dirs[(k + (salt & 0x7fffffff)) % dirs.Length];
                    Vector3 p = c + new Vector3(dir.x, 0f, dir.z).normalized * r;
                    RaycastHit h = HelperFunctions.LineCheck(p + Vector3.up * 5f, p + Vector3.down * 10f, HelperFunctions.LayerType.TerrainMap);
                    if (h.transform == null || h.rigidbody != null) continue;
                    if (Vector3.Angle(h.normal, Vector3.up) >= MaxSlope || Mathf.Abs(h.point.y - c.y) > 2f) continue;
                    if (HelperFunctions.LineCheck(c + Vector3.up, h.point + Vector3.up * 0.5f, HelperFunctions.LayerType.TerrainMap).transform != null) continue;
                    if (Physics.CheckSphere(h.point + Vector3.up * 0.45f, 0.3f, HelperFunctions.AllPhysicalExceptCharacter)) continue;
                    point = h.point + h.normal * 0.1f;
                    normal = h.normal;
                    return true;
                }
            }
            point = c; normal = Vector3.up;
            return false;
        }
    }

    /// <summary>Card-applied statuses: exact amounts, not rescaled by the card multipliers and never shared or deferred.</summary>
    public static class CardStatus
    {
        [ThreadStatic] public static bool Raw;

        /// <summary>
        /// A card's own penalty or cost lands exactly as written: not scaled by other cards, not dodged by being invincible
        /// (Milk, mushroom) and not multiplied x8 as a skeleton. Piton Payment Plan passes its fall's original flags.
        /// </summary>
        public static void Add(Character me, CharacterAfflictions.STATUSTYPE type, float amount, bool ignoreInvincibility = true, bool ignoreSkeleton = true)
        {
            if (me == null || amount <= 0f) return;
            Raw = true;
            try { me.refs.afflictions.AddStatus(type, amount, ignoreInvincibility: ignoreInvincibility, ignoreSkeleton: ignoreSkeleton); }
            finally { Raw = false; }
        }

        /// <summary>A card's own removal (Mini Tenacity, Medicinal Munchies...) removes exactly what it says.</summary>
        public static void Remove(Character me, CharacterAfflictions.STATUSTYPE type, float amount)
        {
            if (me == null || amount <= 0f) return;
            Raw = true;
            try { me.refs.afflictions.SubtractStatus(type, amount); }
            finally { Raw = false; }
        }

        public static float Get(Character me, CharacterAfflictions.STATUSTYPE type) => me.refs.afflictions.GetCurrentStatus(type);
    }

    /// <summary>Teleports and revivals of the local scout, using the game's own RPCs.</summary>
    public static class Revival
    {
        public static bool Busy { get; private set; }

        /// <summary>Revive the local scout at a position, then set its Injury and a minimum of usable stamina.</summary>
        public static void ReviveAt(Character me, Vector3 pos, float injury, float minStamina, Action after = null)
        {
            if (Busy || me == null) return;
            Busy = true;
            me.photonView.RPC("RPCA_ReviveAtPosition", RpcTarget.All, pos, false, -1);
            Plugin.Run(AfterWarp(me, injury, minStamina, after));
        }

        /// <summary>Teleport the local scout (not downed) to a position, then set its Injury.</summary>
        public static void TeleportTo(Character me, Vector3 pos, float injury, Action after = null)
        {
            if (Busy || me == null) return;
            Busy = true;
            me.photonView.RPC("WarpPlayerRPC", RpcTarget.All, pos, true);
            Plugin.Run(AfterWarp(me, injury, -1f, after));
        }

        private static IEnumerator AfterWarp(Character me, float injury, float minStamina, Action after)
        {
            float start = Time.time;
            yield return null;
            while (me != null && (me.warping || me.data.passedOut || me.data.dead) && Time.time - start < 8f) yield return null;
            if (me != null)
            {
                if (injury >= 0f) me.refs.afflictions.SetStatus(CharacterAfflictions.STATUSTYPE.Injury, injury);
                if (minStamina > 0f && me.data.currentStamina < minStamina)
                {
                    me.data.currentStamina = Mathf.Min(minStamina, me.GetMaxStamina());
                    GUIManager.instance.bar.ChangeBar();
                }
                me.data.sinceGrounded = 0f;
                Effects.Recompute(me);
            }
            Busy = false;
            after?.Invoke();
        }
    }
}
