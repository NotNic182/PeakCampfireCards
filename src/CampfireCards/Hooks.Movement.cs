using System;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Generated;
using CampfireCards.Net;
using HarmonyLib;
using UnityEngine;

namespace CampfireCards
{
    /// <summary>Climbing cost, reach, slipping and jump hooks. All run for the local scout only (owner-authoritative physics).</summary>
    public static partial class Hooks
    {
        private static readonly AccessTools.FieldRef<CharacterClimbing, Character> ClimbingCharacter =
            AccessTools.FieldRefAccess<CharacterClimbing, Character>("character");
        private static readonly AccessTools.FieldRef<CharacterRopeHandling, Character> RopeCharacter =
            AccessTools.FieldRefAccess<CharacterRopeHandling, Character>("character");
        private static readonly AccessTools.FieldRef<CharacterVineClimbing, Character> VineCharacter =
            AccessTools.FieldRefAccess<CharacterVineClimbing, Character>("character");
        private static readonly AccessTools.FieldRef<CharacterGrabbing, Character> GrabbingCharacter =
            AccessTools.FieldRefAccess<CharacterGrabbing, Character>("character");

        /// <summary>Inside a climbing method of this scout: its UseStamina calls are climbing costs.</summary>
        [ThreadStatic] private static Character _climbCtx;
        [ThreadStatic] private static int _climbDepth;
        /// <summary>playerSlide.y when CharacterClimbing.Climbing started this physics step.</summary>
        [ThreadStatic] private static float _slideBefore;
        [ThreadStatic] private static bool _slideTracked;

        private static void EnterClimb(Character c) { if (c == null) return; _climbCtx = c; _climbDepth++; }
        private static void ExitClimb() { if (--_climbDepth <= 0) { _climbDepth = 0; _climbCtx = null; } }

        // ------------------------------------------------------------------ climbing cost contexts
        public static void OnWallClimbContextPrefix(CharacterClimbing __instance) => EnterClimb(ClimbingCharacter(__instance));
        public static void OnWallClimbContext(CharacterClimbing __instance) => ExitClimb();
        public static void OnRopeClimbContextPrefix(CharacterRopeHandling __instance) => EnterClimb(RopeCharacter(__instance));
        public static void OnRopeClimbContext(CharacterRopeHandling __instance) => ExitClimb();
        public static void OnVineClimbContextPrefix(CharacterVineClimbing __instance) => EnterClimb(VineCharacter(__instance));
        public static void OnVineClimbContext(CharacterVineClimbing __instance) => ExitClimb();
        public static void OnStartClimbPrefix(CharacterClimbing __instance) => EnterClimb(ClimbingCharacter(__instance));

        /// <summary>hooks.json startClimb postfix: leave the context, then scale the slide a grab starts with (slipForce).</summary>
        public static void OnStartClimb(CharacterClimbing __instance)
        {
            ExitClimb();
            var ch = ClimbingCharacter(__instance);
            if (!Mine(ch)) return;
            float k = Effects.Local[Channel.slipForce];
            var s = __instance.playerSlide;
            if (Mathf.Abs(k - 1f) > 0.0001f && s.y < 0f) __instance.playerSlide = new Vector2(s.x, s.y * k);
        }

        /// <summary>
        /// hooks.json useStamina: inside a climbing context the cost is multiplied by climbStamina. A cost that ends at 0
        /// (The Mountain's Heir) returns success without calling the game, because UseStamina(0) returns false and drops
        /// the scout off ropes and vines.
        /// </summary>
        public static bool OnUseStaminaPrefix(Character __instance, ref float usage, ref bool __result)
        {
            if (usage <= 0f || !Mine(__instance)) return true;
            bool climbing = _climbCtx == __instance && _climbDepth > 0;
            usage *= Core.StatComposites.StaminaCost(Effects.Local, climbing);
            if (climbing) { Ability_OverhangRegen.NoteClimbCost(usage); Ability_HoldRefund.NoteClimbCost(usage); }
            if (usage <= 0f)
            {
                __result = true;
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ reach
        /// <summary>
        /// An extended pull is only allowed when its geometry matches the intended hoist. For a climber the drag must be
        /// upward-dominant (~29 degrees or steeper): DragTowards slides the climb ANCHOR toward the drag point, and a
        /// shallow extended pull sweeps it sideways off climbable geometry, force-detaching the climber - the "the reach
        /// card pulled me down" beta reports. For a stuck/webbed scout, mirror the vanilla climbing height rule so an
        /// extended pull never yanks someone down from above. Inside the vanilla 4 m the game pulls regardless, as ever.
        /// </summary>
        private static bool ExtendedPullSafe(Character helper, Character target, Vector3 dragPoint)
        {
            Vector3 d = dragPoint - target.Center;
            float horiz = new Vector2(d.x, d.z).magnitude;
            if (target.data.isClimbing) return d.y > 0f && d.y >= 0.55f * horiz;
            return target.Center.y < helper.Center.y + 1f;
        }

        /// <summary>A started extended pull carries through to the vanilla handover (the gate is a start condition).</summary>
        private static Character _pullLatchHelper;
        private static float _pullLatchExpire;

        public static void OnReachPrefix(CharacterGrabbing __instance)
        {
            var helper = GrabbingCharacter(__instance);
            if (!Mine(helper) || !helper.data.isReaching || helper.data.dead || helper.data.passedOut) return;
            var cost = LocalCards.Get<Ability_HelpCost>();
            if (cost == null) return;
            float reach = 4f * Effects.Local[Channel.helpReach];
            Vector3 dragPoint = helper.Center + Util.ZeroY(helper.data.lookDirection * __instance.extendReachDistance);
            foreach (var target in Character.AllCharacters)
            {
                if (target == null || target == helper || target.isBot || target.data.dead) continue;
                float dist = Vector3.Distance(helper.Center, target.Center);
                if (dist > reach) continue;
                if (Vector3.Angle(helper.data.lookDirection, target.Center - helper.Center) > 60f) continue;
                if (!CharacterGrabbing.TargetCanBeHelped(target, helper)) continue;
                // never charge the drawback for an extended pull the pulled client will refuse (geometry gate below)
                if (dist > 4f && !ExtendedPullSafe(helper, target, dragPoint)) continue;
                cost.OnPull(helper);
                return;
            }
        }

        /// <summary>hooks.json wallReach: grabReach lengthens the wall-attach ray (vanilla 1.25 m from the camera).</summary>
        public static void OnTryWallClimbPrefix(CharacterClimbing __instance, ref float raycastDistance)
        {
            if (!Mine(ClimbingCharacter(__instance))) return;
            raycastDistance *= Effects.Local[Channel.grabReach];
        }

        private static readonly Action<Character, Vector3, float> DragTowards =
            AccessTools.MethodDelegate<Action<Character, Vector3, float>>(AccessTools.Method(typeof(Character), "DragTowards"));
        private static readonly Action<Character> LimitFalling =
            AccessTools.MethodDelegate<Action<Character>>(AccessTools.Method(typeof(Character), "LimitFalling"));
        private static readonly Action<Character> UnStick =
            AccessTools.MethodDelegate<Action<Character>>(AccessTools.Method(typeof(Character), "UnStick"));

        /// <summary>
        /// hooks.json helpReach postfix. The game pulls a teammate within a literal 4 m, evaluated on the pulled scout's
        /// client. When the helper owns Long Arm, the local scout is also pulled from 4 m up to 4 m x helpReach, with the
        /// same angle and "can be helped" rules and the same force.
        /// </summary>
        public static void OnReach(CharacterGrabbing __instance)
        {
            var helper = GrabbingCharacter(__instance);
            var me = Character.localCharacter;
            if (helper == null || !Mine(me) || helper == me || me.data.dead) return;
            float k = Effects.ActorChannel(NetSend.ActorOf(helper), Channel.helpReach);
            if (k <= 1.0001f) return;
            float dist = Vector3.Distance(helper.Center, me.Center);
            if (dist <= 4f || dist > 4f * k) return;
            if (Vector3.Angle(helper.data.lookDirection, me.Center - helper.Center) > 60f) return;
            if (!CharacterGrabbing.TargetCanBeHelped(me, helper)) return;
            Vector3 target = helper.Center + Util.ZeroY(helper.data.lookDirection * __instance.extendReachDistance);
            // the geometry gate must run before the web/stuck strips, or a refused pull would still free the target.
            // It is a START condition only: as the hoist lifts the climber, d.y shrinks against a floored horizontal
            // gap and would re-veto the pull mid-air before the 4 m vanilla handover (a dead band), so a started pull
            // is latched through until the reach ends.
            bool latched = _pullLatchHelper == helper && Time.time <= _pullLatchExpire;
            if (!latched && !ExtendedPullSafe(helper, me, target)) return;
            _pullLatchHelper = helper;
            _pullLatchExpire = Time.time + 0.25f;
            if (me.IsStuck()) UnStick(me);
            me.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Web, 1f);
            me.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.FlyTrap, 1f);
            // taper the force across the extended band: a ragdoll dragged from 6+ m arrives slower, so it no longer
            // bowls the helper off the ledge on arrival (vanilla's full 70 resumes once inside 4 m)
            DragTowards(me, target, Mathf.Lerp(70f, 45f, Mathf.InverseLerp(4f, 4f * k, dist)));
            LimitFalling(me);
            me.data.sinceGrabFriend = 0f;
            try { GUIManager.instance.Grasp(); } catch { }
        }

        // ------------------------------------------------------------------ slipping
        /// <summary>hooks.json wallSlide prefix: remember the slide before Climbing() adds the out-of-stamina slide.</summary>
        public static void OnClimbingSlidePrefix(CharacterClimbing __instance, out float __state)
        {
            __state = __instance.playerSlide.y;
            _slideBefore = __state;
            _slideTracked = true;
        }

        public static void OnClimbingSlide(CharacterClimbing __instance, float __state)
        {
            _slideTracked = false;
        }

        /// <summary>
        /// hooks.json windSlip prefix (runs inside Climbing, after the slide was added and before the climb drag decays it):
        /// scale this step's added slide and the wind slipperiness by slipForce. The slipperiness is restored after.
        /// </summary>
        public static void OnRequestedPositionPrefix(CharacterClimbing __instance, out float __state)
        {
            var ch = ClimbingCharacter(__instance);
            __state = ch != null && ch.data != null ? ch.data.slippy : 0f;
            if (!Mine(ch)) return;
            float k = Effects.Local[Channel.slipForce];
            if (Mathf.Abs(k - 1f) < 0.0001f) return;
            if (_slideTracked)
            {
                var s = __instance.playerSlide;
                if (s.y < _slideBefore) __instance.playerSlide = new Vector2(s.x, _slideBefore + (s.y - _slideBefore) * k);
                _slideTracked = false;
            }
            ch.data.slippy = __state * k;
        }

        public static void OnRequestedPosition(CharacterClimbing __instance, float __state)
        {
            var ch = ClimbingCharacter(__instance);
            if (Mine(ch)) ch.data.slippy = __state;
        }

        // ------------------------------------------------------------------ jumps
        /// <summary>hooks.json jumped: Second Spring notices its extra midair jump being used.</summary>
        public static void OnCharacterJumped(Character character, bool doubleJump)
        {
            if (!doubleJump || !Mine(character)) return;
            var spring = Ability_SecondSpring.Active;
            if (spring != null) spring.OnExtraJumpUsed(character);
        }
    }
}
