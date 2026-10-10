using System;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using HarmonyLib;
using UnityEngine;

namespace CampfireCards
{
    /// <summary>
    /// Status, stamina and weight hooks. Every multiplier is read from Effects.Local (folded fresh every frame), and only
    /// the local scout's own values are changed: PEAK computes statuses and stamina on the owner and syncs them.
    /// </summary>
    public static partial class Hooks
    {
        private static readonly AccessTools.FieldRef<CharacterHeatEmission, Character> HeatEmissionCharacter =
            AccessTools.FieldRefAccess<CharacterHeatEmission, Character>("character");
        private static readonly AccessTools.FieldRef<CharacterHeatEmission, float> HeatEmissionCounter =
            AccessTools.FieldRefAccess<CharacterHeatEmission, float>("counter");

        /// <summary>Inside CharacterMovement/CharacterClimbing.CheckFallDamage: the Injury being added is fall damage.</summary>
        [ThreadStatic] private static int _fallDepth;
        /// <summary>Inside CharacterHeatEmission.Update: the Cold being removed is body heat from this emitter's scout.</summary>
        [ThreadStatic] private static Character _heatEmitter;
        /// <summary>Inside Character.UpdateVariablesFixed: the AddStamina call is natural regeneration.</summary>
        [ThreadStatic] private static Character _regenChar;

        private static bool Mine(Character c) => c != null && c.IsLocal && RunState.RunStarted;

        // ------------------------------------------------------------------ fall flag
        public static void OnFallDamagePrefix(CharacterMovement __instance) { _fallDepth++; }
        public static void OnFallDamage(CharacterMovement __instance) { if (_fallDepth > 0) _fallDepth--; }
        public static void OnClimbFallDamagePrefix(CharacterClimbing __instance) { _fallDepth++; }
        public static void OnClimbFallDamage(CharacterClimbing __instance) { if (_fallDepth > 0) _fallDepth--; }

        // ------------------------------------------------------------------ AddStatus
        /// <summary>
        /// hooks.json addStatus. Order: card multipliers (fall or non-fall Injury, build-up rates), Shared Burden split,
        /// Delayed Consequences (fall only), Phoenix Clause (fall only). Card-applied penalties (CardStatus.Raw) pass untouched.
        /// </summary>
        public static bool OnAddStatusPrefix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount, bool fromRPC, ref bool __result, out bool __state)
        {
            __state = false;
            if (amount <= 0f || CardStatus.Raw) return true;
            var ch = __instance.character;
            if (!Mine(ch)) return true;
            if (statusType == CharacterAfflictions.STATUSTYPE.Petrify) return true;   // scaled once, in AddPetrify

            float before = amount;
            bool fall = statusType == CharacterAfflictions.STATUSTYPE.Injury && _fallDepth > 0;
            amount *= Effects.StatusGainMultiplier(statusType, fall);

            if (statusType == CharacterAfflictions.STATUSTYPE.Injury && amount > 0f)
            {
                amount = SharedBurdenSplit(amount);
                if (fall)
                {
                    var delayed = Ability_DelayedConsequences.Owned;
                    if (delayed != null) amount = delayed.Defer(amount);
                    var phoenix = Ability_PhoenixClause.Owned;
                    if (phoenix != null && phoenix.TryPreventFatalFall(ch, amount))
                    {
                        __result = false;
                        return false;
                    }
                }
            }

            // only a status scaled to nothing is cancelled (The Mountain's Heir: no fall Injury); tiny per-frame Hunger and
            // night Cold must reach the game, which accumulates them in 0.025 steps itself
            if (StatComposites.CancelsStatus(amount)) { __result = false; return false; }
            __state = fromRPC && Mathf.Abs(amount - before) > 0.00001f;
            return true;
        }

        /// <summary>A rescaled fromRPC add is not pushed by the game; push so other clients see the owner's value promptly.</summary>
        public static void OnAddStatus(CharacterAfflictions __instance, bool __state)
        {
            if (__state) __instance.PushStatuses();
        }

        /// <summary>Shared Burden: a nearby teammate's aura cuts the Injury; half of what it prevents (capped) goes to that teammate.</summary>
        private static float SharedBurdenSplit(float amount)
        {
            int owner = Effects.SharedBurdenOwner;
            if (owner == int.MinValue || owner == NetSend.LocalActor) return amount;
            float factor = Mathf.Clamp01(Effects.SharedBurdenFactor);
            float prevented = amount * (1f - factor);
            var def = Array.Find(CardTable.Cards, c => c.Ability == "SharedBurden");
            float absorb = def != null ? CardSettings.Current.Param(def, "absorb", 0.5f) : 0.5f;
            float cap = def != null ? CardSettings.Current.Param(def, "cap", 0.1f) : 0.1f;
            float share = Mathf.Min(prevented * absorb, cap);
            if (share >= 0.0125f) NetSend.RaiseTo(new InjuryShare { amount = share }, owner);
            return amount * factor;
        }

        // ------------------------------------------------------------------ SubtractStatus / Petrify
        /// <summary>hooks.json subtractStatus: natural decay rates, and body-heat Cold removal.</summary>
        public static void OnSubtractStatusPrefix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount, bool decreasedNaturally)
        {
            if (amount <= 0f) return;
            var ch = __instance.character;
            if (!Mine(ch)) return;
            if (decreasedNaturally)
            {
                amount *= Effects.NaturalDecayMultiplier(statusType);
            }
            else if (statusType == CharacterAfflictions.STATUSTYPE.Cold && _heatEmitter != null)
            {
                float k = Effects.Local[Channel.coldDecay];
                if (_heatEmitter != ch) k *= Effects.Local[Channel.huddleStrength];   // huddling = a teammate's body heat
                amount *= k;
            }
        }

        /// <summary>hooks.json addPetrify: Petrify is stored as 0..100 outside the status array.</summary>
        public static void OnAddPetrifyPrefix(CharacterAfflictions __instance, ref int petrify)
        {
            if (petrify <= 0 || CardStatus.Raw) return;
            if (!Mine(__instance.character)) return;
            petrify = Mathf.RoundToInt(petrify * StatComposites.StatusGain(Effects.Local, Channel.petrifyRate));
        }

        // ------------------------------------------------------------------ body heat (Heat Huddle, Frostwalker)
        /// <summary>hooks.json heatEmission prefix: mark the emitter, and note whether it releases heat this frame.</summary>
        public static void OnHeatEmissionPrefix(CharacterHeatEmission __instance, out bool __state)
        {
            __state = false;
            var ch = HeatEmissionCharacter(__instance);
            _heatEmitter = ch;
            if (ch == null || ch.data == null || ch.data.sinceAddedCold < 3f) return;
            __state = HeatEmissionCounter(__instance) + Time.deltaTime >= __instance.rate;
        }

        /// <summary>
        /// hooks.json heatEmission postfix: with Heat Huddle the local scout also gets a teammate's heat from up to
        /// radius x huddleStrength away (vanilla only reaches within radius, 1 m).
        /// </summary>
        public static void OnHeatEmission(CharacterHeatEmission __instance, bool __state)
        {
            var emitter = _heatEmitter;
            _heatEmitter = null;
            if (!__state || emitter == null) return;
            var me = Character.localCharacter;
            if (!Mine(me) || emitter == me || me.data.dead) return;
            float k = Effects.Local[Channel.huddleStrength];
            if (k <= 1.0001f) return;
            float d = Vector3.Distance(__instance.transform.position, me.Center);
            if (d < __instance.radius || d >= __instance.radius * k) return;
            _heatEmitter = emitter;
            try { me.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Cold, __instance.heatAmount); }
            finally { _heatEmitter = null; }
        }

        // ------------------------------------------------------------------ stamina
        public static void OnRegenContextPrefix(Character __instance) { _regenChar = __instance; }
        public static void OnRegenContext(Character __instance) { _regenChar = null; }

        /// <summary>hooks.json addStamina: regenRate applies to natural regeneration only (the one AddStamina in UpdateVariablesFixed).</summary>
        public static void OnAddStaminaPrefix(Character __instance, ref float add)
        {
            if (add <= 0f || _regenChar != __instance || !Mine(__instance)) return;
            add *= Effects.Local[Channel.regenRate];
        }

        /// <summary>hooks.json canRegen: regenDelay shifts the vanilla 1 s (2 s after emptying the bar) wait.</summary>
        public static void OnCanRegenStamina(Character __instance, ref bool __result)
        {
            if (!Mine(__instance)) return;
            float delay = Effects.Local[Channel.regenDelay];
            if (Mathf.Abs(delay) < 0.001f) return;
            var d = __instance.data;
            if (d.currentClimbHandle != null) return;   // vanilla always regenerates on a piton
            bool airborne = d.sinceGrounded > 0.2f && !__instance.refs.afflictions.isWebbed;
            float need = Mathf.Max(0f, (d.currentStamina > 0f ? 1f : 2f) + delay);
            if (!__result)
            {
                if (!airborne && d.sinceUseStamina >= need) __result = true;
            }
            else if (delay > 0f && !__instance.IsStuck() && d.sinceUseStamina < need)
            {
                __result = false;
            }
        }

        /// <summary>hooks.json maxStamina: flat maximum-stamina modifiers (already floored for normal cards in the fold).</summary>
        public static void OnGetMaxStamina(Character __instance, ref float __result)
        {
            if (!Mine(__instance)) return;
            float bonus = Effects.Local[Channel.maxStamina];
            if (Mathf.Abs(bonus) < 0.0001f) return;
            __result = Mathf.Max(1f + bonus - __instance.refs.afflictions.statusSum, 0f);
        }

        /// <summary>The HUD's status-segment row (Bar/LayoutGroup) and its prefab right edge, while the mod has moved it.</summary>
        private static RectTransform _barSegments;
        private static int _barSegmentsOwner;
        private static float _barSegmentsRight;
        private static bool _barSegmentsMoved;

        /// <summary>
        /// hooks.json staminaBarFrame. In PEAK's HUD prefab, MaxStamina ignores the layout and is sized from the left to
        /// GetMaxStamina(), while the status segments sit in a HorizontalLayoutGroup aligned MiddleRight across the full
        /// bar, so in vanilla the two always meet (max stamina + statuses = 100%). A card's max-stamina change resizes only
        /// MaxStamina, which left a gap (penalty) or an overlap (bonus). Here the segment row's right edge and the frame
        /// follow the card-adjusted maximum instead: a penalty visibly shortens the whole bar with Hunger and the other
        /// statuses right after the stamina, and a bonus lengthens it. Only for the local scout; otherwise vanilla.
        /// </summary>
        public static void OnStaminaBarUpdate(StaminaBar __instance)
        {
            if (__instance.staminaBarOutline == null || __instance.fullBar == null) return;
            var c = Character.observedCharacter;
            float bonus = c != null && Mine(c) ? Effects.Local[Channel.maxStamina] : 0f;
            var segments = BarSegments(__instance);
            float width = __instance.fullBar.sizeDelta.x;
            if (Mathf.Abs(bonus) < 0.0001f)
            {
                if (segments != null && _barSegmentsMoved) SetRightEdge(segments, _barSegmentsRight);
                _barSegmentsMoved = false;
                return;
            }
            if (segments == null) return;   // unknown HUD layout: leave the game's bar alone rather than half-move it
            float frame = StatComposites.BarFrame(bonus, c.refs.afflictions.statusSum);
            var o = __instance.staminaBarOutline;
            o.sizeDelta = new Vector2(14f + frame * width, o.sizeDelta.y);
            if (!_barSegmentsMoved) { _barSegmentsRight = segments.offsetMax.x; _barSegmentsMoved = true; }
            SetRightEdge(segments, _barSegmentsRight + StatComposites.BarSegmentsShift(bonus, width));
        }

        private static void SetRightEdge(RectTransform rt, float right)
        {
            if (Mathf.Abs(rt.offsetMax.x - right) > 0.01f) rt.offsetMax = new Vector2(right, rt.offsetMax.y);
        }

        /// <summary>The parent of the status segments, if it is the right-aligned HorizontalLayoutGroup the prefab has.</summary>
        private static RectTransform BarSegments(StaminaBar bar)
        {
            int id = bar.GetInstanceID();
            if (_barSegmentsOwner == id) return _barSegments;   // a new HUD (scene load) has a new id
            _barSegments = null; _barSegmentsOwner = id; _barSegmentsMoved = false;
            // StaminaBar.Start fills afflictions from the whole bar group (Petrify lives elsewhere): take the segments'
            // HorizontalLayoutGroup. MaxStamina is also its child, but ignores the layout and hangs from the left edge.
            if (bar.afflictions == null || bar.afflictions.Length == 0) { _barSegmentsOwner = 0; return null; }   // Start has not run yet
            foreach (var a in bar.afflictions)
            {
                var parent = a != null && a.rtf != null ? a.rtf.parent as RectTransform : null;
                if (parent != null && parent.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() != null) { _barSegments = parent; break; }
            }
            Plugin.Verbose("Stamina bar segments: " + (_barSegments != null ? _barSegments.name : "not found (frame only)"));
            return _barSegments;
        }

        /// <summary>hooks.json bonusStamina: every bonus (yellow) stamina grant, including the cards' own.</summary>
        public static void OnAddExtraStaminaPrefix(Character __instance, ref float add)
        {
            if (add <= 0f || !Mine(__instance)) return;
            add *= Effects.Local[Channel.bonusStaminaGain];
        }

        // ------------------------------------------------------------------ weight and thorns
        /// <summary>Thorns the local scout carries: the game's count and the card-scaled value (only new thorns are scaled).</summary>
        private static readonly ThornsLedger _thorns = new ThornsLedger();

        /// <summary>
        /// hooks.json updateWeight prefix: the game compares its freshly counted Thorns with the stored value to decide
        /// whether to play the thorn sound, particles and screen effect. Put the unscaled count back first, so a scaled
        /// value never makes every inventory change sting again; the postfix stores the scaled value afterwards.
        /// </summary>
        public static void OnUpdateWeightPrefix(CharacterAfflictions __instance, out float __state)
        {
            __state = -1f;
            if (!Mine(__instance.character) || __instance.currentStatuses == null) return;
            __state = __instance.character.data.currentStamina;
            int i = (int)CharacterAfflictions.STATUSTYPE.Thorns;
            if (i < __instance.currentStatuses.Length) __instance.currentStatuses[i] = _thorns.Raw * CharacterAfflictions.STATUS_INCREMENT;
        }

        /// <summary>
        /// hooks.json updateWeight postfix: rebuild Weight from the same parts the game counts, with the card multipliers
        /// (carried items, the item in hand, a carried scout), recomputed from scratch every time; and scale Thorns. PEAK
        /// sets Thorns here (0.025 per stuck thorn), never through AddStatus, so Core.ThornsLedger scales only thorns that
        /// arrived since the last update: weight-only updates change nothing, thorns already stuck keep their value, and
        /// without the card new thorns count fully again.
        /// </summary>
        public static void OnUpdateWeight(CharacterAfflictions __instance, float __state)
        {
            var ch = __instance.character;
            if (!Mine(ch) || ch.player == null) return;
            float kItem = Effects.Local[Channel.itemWeight];
            float kHeld = Effects.Local[Channel.heldItemWeight];
            float kScout = Effects.Local[Channel.carryScoutWeight];
            bool weightChanged = Mathf.Abs(kItem - 1f) > 0.0001f || Mathf.Abs(kHeld - 1f) > 0.0001f || Mathf.Abs(kScout - 1f) > 0.0001f;

            if (weightChanged)
            {
                var p = ch.player;
                var sel = ch.refs.items.currentSelectedSlot;
                int carried = 0, held = 0, sticky = 0;
                for (int i = 0; i < p.itemSlots.Length; i++)
                {
                    var slot = p.itemSlots[i];
                    if (slot == null || slot.prefab == null) continue;
                    if (sel.IsSome && sel.Value == i) held += slot.prefab.CarryWeight; else carried += slot.prefab.CarryWeight;
                }
                var bp = p.backpackSlot;
                if (bp != null && !bp.IsEmpty() && bp.data != null)
                {
                    BackpackData bd;
                    if (bp.data.TryGetDataEntry<BackpackData>(DataEntryKey.BackpackData, out bd))
                    {
                        int pack = 0;
                        foreach (var s in bd.itemSlots) if (s != null && !s.IsEmpty()) pack += s.prefab.CarryWeight;
                        Item packItem;
                        if (ItemDatabase.TryGetItem(bp.GetPrefabName(), out packItem)) pack += packItem.CarryWeight;
                        if (sel.IsSome && sel.Value == Player.BACKPACKSLOTINDEX) held += pack; else carried += pack;
                    }
                }
                var temp = p.GetItemSlot(TemporaryItemSlot.SLOT_INDEX);
                if (temp != null && !temp.IsEmpty()) held += temp.prefab.CarryWeight;   // the hands-full slot is always in hand
                int scout = ch.data.carriedPlayer != null ? 8 : 0;
                foreach (var s in StickyItemComponent.ALL_STUCK_ITEMS)
                    if (s != null && s.stuckToCharacter == ch) sticky += s.addWeightToStuckPlayer;

                float units = carried * kItem + held * StatComposites.HeldItemWeight(Effects.Local) + scout * kScout + sticky;
                __instance.SetStatus(CharacterAfflictions.STATUSTYPE.Weight, 0.025f * units);
            }

            // the game just stored the unscaled count (0.025 per thorn)
            float raw = __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Thorns);
            int count = Mathf.RoundToInt(raw / CharacterAfflictions.STATUS_INCREMENT);
            _thorns.Update(count, StatComposites.Thorns(Effects.Local));
            int steps = _thorns.ScaledSteps;
            if (steps != count) __instance.SetStatus(CharacterAfflictions.STATUSTYPE.Thorns, steps * CharacterAfflictions.STATUS_INCREMENT);

            // the game set the unscaled Weight and Thorns first and clamped stamina to them; give back what that took,
            // up to the final maximum (vanilla clamps the same way, only against the values that are now in place)
            if (__state >= 0f && ch.data.currentStamina < __state - 0.00001f)
            {
                ch.data.currentStamina = Mathf.Clamp(__state, 0f, ch.GetMaxStamina());
                GUIManager.instance.bar.ChangeBar();
            }
        }
    }
}
