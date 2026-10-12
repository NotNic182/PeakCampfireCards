using System;
using System.Collections.Generic;
using System.Linq;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using CampfireCards.Ui;
using UnityEngine;

namespace CampfireCards.Cards
{
    /// <summary>The game's own values on a scout, captured once, so card effects are always recomputed from base (no stat drift).</summary>
    public class CharacterBaseline : MonoBehaviour
    {
        public bool Captured;
        public float BaseSprintMult, BaseJump, BaseSprintStamina;
        public float AppliedClimbDelta;
        public int LastWeightKey = int.MinValue;
    }

    /// <summary>
    /// systems.json Effects: every player's card collection (synced), and for the local scout the folded modifiers
    /// from its own cards (with their conditions) plus nearby teammates' auras. The Harmony hooks read Local.
    /// </summary>
    public static partial class Effects
    {
        public const byte FlagSkipNext = 1, FlagOfferOne = 2, FlagBalloonUsed = 4;
        public static readonly PenaltyLedger Penalties = new PenaltyLedger();

        public static readonly Dictionary<int, List<byte>> CardsByActor = new Dictionary<int, List<byte>>();
        /// <summary>Per player: bits 1 skip next pick, 2 next offer is one card, 4 one-time Balloon Bunch already spawned.</summary>
        public static readonly Dictionary<int, byte> PickFlags = new Dictionary<int, byte>();

        /// <summary>Folded modifiers for the local scout, refreshed every frame.</summary>
        public static readonly ModResult Local = new ModResult();
        /// <summary>The nearest teammate whose Shared Burden aura covers the local scout (int.MinValue when none).</summary>
        public static int SharedBurdenOwner = int.MinValue;
        public static float SharedBurdenFactor = 1f;

        private static readonly Action<CharacterAfflictions> UpdateWeight =
            HarmonyLib.AccessTools.MethodDelegate<Action<CharacterAfflictions>>(HarmonyLib.AccessTools.Method(typeof(CharacterAfflictions), "UpdateWeight"));

        public static bool HasAnyState => CardsByActor.Count > 0;

        /// <summary>The modifiers applied to the local scout in the last fold (conditions held, auras in range).</summary>
        public static IReadOnlyList<ActiveMod> ActiveMods => _mods;

        /// <summary>
        /// The local scout's always-on fold: its own modifiers that need no condition (self, or its own team-and-self
        /// auras), without any teammate's aura. The Current Build screen shows it next to Local, so a temporary effect
        /// is never presented as permanent.
        /// </summary>
        public static void FoldAlways(ModResult into)
        {
            var mods = new List<ActiveMod>();
            var s = CardSettings.Current;
            List<byte> list;
            if (CardsByActor.TryGetValue(NetSend.LocalActor, out list))
            {
                foreach (var id in list)
                {
                    var def = s.Card(id);
                    if (def == null) continue;
                    for (int i = 0; i < def.Mods.Length; i++)
                    {
                        var m = def.Mods[i];
                        if (m.Target == ModTarget.team || m.When != Generated.Condition.always) continue;
                        mods.Add(new ActiveMod { CardId = def.Id, ModIndex = i, Channel = m.Channel, Op = m.Op, Value = s.ModValue(def, i), Rarity = def.Rarity });
                    }
                }
            }
            AddPenalties(mods, NetSend.LocalActor);
            into.Fold(mods, s);
        }

        public static List<byte> CardsOf(int actor)
        {
            List<byte> l;
            if (!CardsByActor.TryGetValue(actor, out l)) { l = new List<byte>(); CardsByActor[actor] = l; }
            return l;
        }

        public static bool HasAbility(int actor, string ability)
        {
            List<byte> l;
            if (!CardsByActor.TryGetValue(actor, out l)) return false;
            foreach (var id in l)
            {
                var c = CardSettings.Current.Card(id);
                if (c != null && c.Ability == ability) return true;
            }
            return false;
        }

        public static byte Flags(int actor) { byte f; return PickFlags.TryGetValue(actor, out f) ? f : (byte)0; }
        public static void SetFlag(int actor, byte flag, bool on)
        {
            byte f = Flags(actor);
            PickFlags[actor] = on ? (byte)(f | flag) : (byte)(f & ~flag);
        }

        /// <summary>Who owns this card (int.MinValue = nobody). Used for Mythics, which exist once per run.</summary>
        public static int MythicOwner(byte cardId)
        {
            foreach (var kv in CardsByActor) if (kv.Value.Contains(cardId)) return kv.Key;
            return int.MinValue;
        }

        /// <summary>Mythic cards anyone already owns this run.</summary>
        public static HashSet<byte> ClaimedMythics()
        {
            var set = new HashSet<byte>();
            foreach (var kv in CardsByActor)
                foreach (var id in kv.Value)
                {
                    var d = CardSettings.Current.Card(id);
                    if (d != null && d.Rarity == CardRarity.Mythic) set.Add(id);
                }
            return set;
        }

        /// <summary>
        /// One channel for any player from their unconditional self modifiers (synced collection), for effects evaluated on
        /// another client: Long Arm's reach is checked on the pulled scout's client, Field Medic's heal on the patient's.
        /// </summary>
        public static float ActorChannel(int actor, Channel channel)
        {
            List<byte> list;
            if (!CardsByActor.TryGetValue(actor, out list)) return ChannelTable.Defs[(int)channel].IsMul ? 1f : 0f;
            var mods = new List<ActiveMod>();
            var s = CardSettings.Current;
            foreach (var id in list)
            {
                var def = s.Card(id);
                if (def == null) continue;
                for (int i = 0; i < def.Mods.Length; i++)
                {
                    var m = def.Mods[i];
                    if (m.Channel != channel || m.Target != ModTarget.self || m.When != Generated.Condition.always) continue;
                    mods.Add(new ActiveMod { CardId = def.Id, ModIndex = i, Channel = m.Channel, Op = m.Op, Value = s.ModValue(def, i), Rarity = def.Rarity });
                }
            }
            AddPenalties(mods, actor, channel);
            _scratch.Fold(mods, s);
            return _scratch[channel];
        }

        private static readonly ModResult _scratch = new ModResult();

        /// <summary>Run over: every card, flag, aura, timer and stat change is cleared.</summary>
        public static void Reset()
        {
            LocalCards.Reset();
            CardsByActor.Clear();
            PickFlags.Clear();
            Penalties.Reset();
            Local.Reset();
            SharedBurdenOwner = int.MinValue;
            var me = Character.localCharacter;
            if (me != null) ApplyFields(me);
            Hud.RefreshMyCards();
        }

        private static bool Condition(Condition c, Character me, CardDef def)
        {
            var d = me.data;
            var aff = me.refs.afflictions;
            switch (c)
            {
                case Generated.Condition.always: return true;
                case Generated.Condition.hangingStill: return d.isClimbing && me.input.movementInput.magnitude < 0.1f;
                case Generated.Condition.crouchStill: return d.isCrouching && me.input.movementInput.sqrMagnitude < 0.01f;
                case Generated.Condition.grounded: return d.isGrounded;
                case Generated.Condition.injury20: return aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Injury) >= 0.2f - 0.0001f;
                case Generated.Condition.poisonOrSpores20:
                    return aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Poison) >= 0.2f - 0.0001f
                        || aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Spores) >= 0.2f - 0.0001f;
                case Generated.Condition.poisonAbove20: return aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Poison) > 0.2f;
                case Generated.Condition.climbingColdSurface:
                    return d.isClimbing && d.climbMod != null && d.climbMod.applyStatus && d.climbMod.statusType == CharacterAfflictions.STATUSTYPE.Cold;
                case Generated.Condition.climbingWithItem: return d.isClimbingAnything && d.currentItem != null;
                case Generated.Condition.noBackpack: return !WearingBackpack(me);
                case Generated.Condition.wearingBackpack: return WearingBackpack(me);
                case Generated.Condition.teammateNear: return TeammatesWithin(me, CardSettings.Current.Param(def, "radius", 6f)) > 0;
                case Generated.Condition.noTeammateNear: return TeammatesWithin(me, CardSettings.Current.Param(def, "radius", 6f)) == 0;
                case Generated.Condition.separatedFromTeam: return TeammatesWithin(me, CardSettings.Current.Param(def, "separation", 8f)) == 0;
                case Generated.Condition.momentumActive: return LocalCards.Flag(Generated.Condition.momentumActive);
                case Generated.Condition.pulseActive: return LocalCards.Flag(Generated.Condition.pulseActive);
                default: return false;
            }
        }

        public static bool WearingBackpack(Character c)
        {
            try { return c.player != null && c.player.backpackSlot != null && c.player.backpackSlot.backpackType == BackpackSlot.BackpackType.Backpack; }
            catch { return false; }
        }

        public static int TeammatesWithin(Character me, float radius)
        {
            int n = 0;
            foreach (var c in Character.AllCharacters)
            {
                if (c == null || c == me || c.isBot || c.data == null || c.data.dead) continue;
                if (Vector3.Distance(c.Center, me.Center) <= radius) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ applying to the scout (owner)
        public static void ApplyFields(Character me)
        {
            if (me == null || me.refs == null || me.refs.movement == null) return;
            var bl = me.GetComponent<CharacterBaseline>() ?? me.gameObject.AddComponent<CharacterBaseline>();
            var mv = me.refs.movement;
            if (!bl.Captured)
            {
                bl.BaseSprintMult = mv.sprintMultiplier;
                bl.BaseJump = mv.jumpImpulse;
                bl.BaseSprintStamina = mv.sprintStaminaUsage;
                bl.Captured = true;
            }
            mv.sprintMultiplier = bl.BaseSprintMult * Local[Channel.sprintSpeed];
            mv.jumpImpulse = bl.BaseJump * Local[Channel.jumpHeight];
            mv.sprintStaminaUsage = bl.BaseSprintStamina * Local[Channel.sprintStamina];

            // climbSpeedMod is shared with the game's own buffs (energy drink), so apply as a tracked delta
            float climbDelta = Local[Channel.climbSpeed] - 1f;
            float d = climbDelta - bl.AppliedClimbDelta;
            if (Mathf.Abs(d) > 0.00001f)
            {
                if (me.refs.climbing != null) me.refs.climbing.climbSpeedMod += d;
                if (me.refs.ropeHandling != null) me.refs.ropeHandling.climbSpeedMod += d;
                if (me.refs.vineClimbing != null) me.refs.vineClimbing.climbSpeedMod += d;
                bl.AppliedClimbDelta = climbDelta;
            }

            // weight only recomputes on inventory events, so nudge it when the weight multipliers change (a Thorns
            // multiplier change needs no nudge: it only applies to thorns that arrive later, Core.ThornsLedger)
            int key = (int)(Local[Channel.itemWeight] * 1000) * 31 + (int)(Local[Channel.heldItemWeight] * 1000) * 17
                      + (int)(Local[Channel.carryScoutWeight] * 1000) * 7;
            key = unchecked(key * 31 + (int)(Local[Channel.totalWeight] * 1000) * 13 + (int)(Local[Channel.backpackItemWeight] * 1000));
            if (key != bl.LastWeightKey && me.refs.afflictions != null)
            {
                bl.LastWeightKey = key;
                try { UpdateWeight(me.refs.afflictions); } catch { }
            }
        }

        /// <summary>The multiplier for a status this scout is gaining, before any Shared Burden split.</summary>
        public static float StatusGainMultiplier(CharacterAfflictions.STATUSTYPE type, bool isFall)
        {
            switch (type)
            {
                case CharacterAfflictions.STATUSTYPE.Injury: return StatComposites.Injury(Local, isFall);
                case CharacterAfflictions.STATUSTYPE.Hunger: return StatComposites.StatusGain(Local, Channel.hungerRate);
                case CharacterAfflictions.STATUSTYPE.Cold: return StatComposites.StatusGain(Local, Channel.coldRate);
                case CharacterAfflictions.STATUSTYPE.Hot: return StatComposites.StatusGain(Local, Channel.heatRate);
                case CharacterAfflictions.STATUSTYPE.Poison: return StatComposites.StatusGain(Local, Channel.poisonRate);
                case CharacterAfflictions.STATUSTYPE.Spores: return StatComposites.StatusGain(Local, Channel.sporesRate);
                case CharacterAfflictions.STATUSTYPE.Drowsy: return StatComposites.StatusGain(Local, Channel.drowsyRate);
                case CharacterAfflictions.STATUSTYPE.Curse: return StatComposites.StatusGain(Local, Channel.curseRate);
                case CharacterAfflictions.STATUSTYPE.Crab:
                case CharacterAfflictions.STATUSTYPE.Web:
                case CharacterAfflictions.STATUSTYPE.FlyTrap:
                    return Local[Channel.negativeStatusRate];
                default: return 1f;
            }
        }

        public static float NaturalDecayMultiplier(CharacterAfflictions.STATUSTYPE type)
        {
            switch (type)
            {
                case CharacterAfflictions.STATUSTYPE.Cold: return Local[Channel.coldDecay];
                case CharacterAfflictions.STATUSTYPE.Hot: return Local[Channel.heatDecay];
                case CharacterAfflictions.STATUSTYPE.Poison: return Local[Channel.poisonDecay];
                case CharacterAfflictions.STATUSTYPE.Spores: return Local[Channel.sporesDecay];
                default: return 1f;
            }
        }

        // ------------------------------------------------------------------ state sync
        public static StateSync BuildStateSync(int sessionId)
        {
            var actors = new List<int>(); var counts = new List<byte>(); var cards = new List<byte>(); var flags = new List<byte>();
            var penaltyCounts = new List<byte>(); var penalties = new List<byte>();
            foreach (var kv in CardsByActor)
            {
                actors.Add(kv.Key);
                counts.Add((byte)Math.Min(255, kv.Value.Count));
                cards.AddRange(kv.Value.Take(255));
                flags.Add(Flags(kv.Key));
                var own = Penalties.Of(kv.Key).Take(255).ToList();
                penaltyCounts.Add((byte)own.Count);
                penalties.AddRange(own);
            }
            return new StateSync { actors = actors.ToArray(), counts = counts.ToArray(), cards = cards.ToArray(), pickFlags = flags.ToArray(), sessionId = sessionId,
                penaltyCounts = penaltyCounts.ToArray(), penaltyVariants = penalties.ToArray() };
        }

        public static void ApplyStateSync(StateSync m)
        {
            int pos = 0, penaltyPos = 0;
            for (int i = 0; i < m.actors.Length; i++)
            {
                int n = i < m.counts.Length ? m.counts[i] : 0;
                var list = new List<byte>();
                for (int k = 0; k < n && pos < m.cards.Length; k++) list.Add(m.cards[pos++]);
                SetAll(m.actors[i], list, i < m.pickFlags.Length ? m.pickFlags[i] : (byte)0);
                // Clown Luggage penalties are permanent: they stay even after the card itself was sacrificed
                int pn = m.penaltyCounts != null && i < m.penaltyCounts.Length ? m.penaltyCounts[i] : 0;
                var own = new List<byte>();
                for (int k = 0; k < pn && m.penaltyVariants != null && penaltyPos < m.penaltyVariants.Length; k++) own.Add(m.penaltyVariants[penaltyPos++]);
                Penalties.Restore(m.actors[i], own);
            }
        }

        /// <summary>
        /// Every Clown Luggage penalty the player has rolled (one per acquisition). They come from the ledger, not the
        /// owned cards, so sacrificing the card keeps them. channel = only that channel (ActorChannel).
        /// </summary>
        private static void AddPenalties(List<ActiveMod> mods, int actor, Channel? channel = null)
        {
            var choices = Penalties.Of(actor);
            if (choices.Count == 0) return;
            var def = DoubleDealCard();
            if (def == null) return;
            foreach (var variant in choices)
            {
                var c = CardPenaltyRules.ChannelFor(variant);
                if (channel.HasValue && c != channel.Value) continue;
                mods.Add(new ActiveMod { CardId = def.Id, ModIndex = -1, Channel = c, Op = ModOp.mul, Value = PenaltyValue(def, variant), Rarity = def.Rarity });
            }
        }

        public static float PenaltyValue(CardDef doubleDeal, byte variant) =>
            CardSettings.Current.Param(doubleDeal, CardPenaltyRules.Parameter(variant), variant <= 2 ? 1.15f : 1.2f);

        public static CardDef DoubleDealCard()
        {
            var row = Array.Find(CardTable.Cards, c => c.Ability == "DoubleDeal");
            return row != null ? CardSettings.Current.Card(row.Id) : null;
        }
    }
}
