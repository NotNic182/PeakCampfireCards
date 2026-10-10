using System;
using System.Collections.Generic;
using System.Linq;
using PeakRounds.Generated;
using PeakRounds.Net;
using PeakRounds.Ui;
using UnityEngine;

namespace PeakRounds.Cards
{
    /// <summary>Remembers the game's own values on a Character so card effects can be re-applied or undone cleanly.</summary>
    public class CharacterBaseline : MonoBehaviour
    {
        public float BaseJump, BaseMinThrow, BaseMaxThrow, BaseHunger;
        public float AppliedMove, AppliedSprint, AppliedClimb;
        public bool Captured;
    }

    /// <summary>
    /// systems.json Effects: per-player card lists -> summed, clamped ClimbStats (stats.json) -> applied to the character's
    /// public fields on every client; the Harmony hooks read the same totals on the owner.
    /// </summary>
    public static class Effects
    {
        public static readonly Dictionary<int, List<byte>> CardsByActor = new Dictionary<int, List<byte>>();
        public static readonly HashSet<int> PhoenixUsed = new HashSet<int>();
        private static readonly Dictionary<int, float[]> Cache = new Dictionary<int, float[]>();

        public static bool HasAnyState => CardsByActor.Count > 0;

        public static void Reset()
        {
            foreach (var kv in CardsByActor.ToList())
            {
                Character c;
                if (PlayerHandler.TryGetCharacter(kv.Key, out c) && c != null) { CardsByActor[kv.Key] = new List<byte>(); Cache.Remove(kv.Key); Apply(c); }
            }
            CardsByActor.Clear();
            PhoenixUsed.Clear();
            Cache.Clear();
            Hud.RefreshMyCards();
        }

        public static List<byte> CardsOf(int actor)
        {
            List<byte> l;
            if (!CardsByActor.TryGetValue(actor, out l)) { l = new List<byte>(); CardsByActor[actor] = l; }
            return l;
        }

        public static void AddCard(int actor, byte cardId)
        {
            CardsOf(actor).Add(cardId);
            Cache.Remove(actor);
            Character c;
            if (PlayerHandler.TryGetCharacter(actor, out c) && c != null) Apply(c);
            if (actor == NetSend.LocalActor) Hud.RefreshMyCards();
        }

        public static void SetAll(int actor, IEnumerable<byte> cards)
        {
            CardsByActor[actor] = new List<byte>(cards);
            Cache.Remove(actor);
            Character c;
            if (PlayerHandler.TryGetCharacter(actor, out c) && c != null) Apply(c);
            if (actor == NetSend.LocalActor) Hud.RefreshMyCards();
        }

        /// <summary>Summed and clamped stats for an actor (index = ClimbStat). Null when the actor holds no cards.</summary>
        public static float[] Compute(int actor)
        {
            float[] cached;
            if (Cache.TryGetValue(actor, out cached)) return cached;
            List<byte> cards;
            if (!CardsByActor.TryGetValue(actor, out cards) || cards.Count == 0) return null;
            var sum = new float[ClimbStatTable.Count];
            foreach (var id in cards)
            {
                CardDef def;
                if (!Deck.TryGet(id, out def)) continue;
                foreach (var e in def.Effects)
                {
                    var sd = ClimbStatTable.Defs[(int)e.Stat];
                    if (sd.IsFlag) sum[(int)e.Stat] = 1f;
                    else sum[(int)e.Stat] += e.Value;
                }
            }
            for (int i = 0; i < sum.Length; i++)
            {
                var sd = ClimbStatTable.Defs[i];
                if (!sd.IsFlag) sum[i] = Mathf.Clamp(sum[i], sd.Min, sd.Max);
            }
            Cache[actor] = sum;
            return sum;
        }

        public static float[] StatsFor(Character c)
        {
            if (c == null) return null;
            int actor = NetSend.ActorOf(c);
            return actor == int.MinValue ? null : Compute(actor);
        }

        /// <summary>stats.json appliesVia "effects": push the totals into the character's public fields (runs on every client).</summary>
        public static void Apply(Character c)
        {
            if (c == null) return;
            if (c.refs == null || c.refs.movement == null || c.refs.items == null || c.refs.afflictions == null)
            {
                // CharacterRegistered fires in Character.Awake, before the refs exist: try again next frame
                Plugin.Run(ApplyLater(c));
                return;
            }
            var bl = c.GetComponent<CharacterBaseline>() ?? c.gameObject.AddComponent<CharacterBaseline>();
            var mv = c.refs.movement; var items = c.refs.items; var aff = c.refs.afflictions;
            if (!bl.Captured)
            {
                bl.BaseJump = mv.jumpImpulse;
                bl.BaseMinThrow = items.minThrowForce;
                bl.BaseMaxThrow = items.maxThrowForce;
                bl.BaseHunger = aff.hungerPerSecond;
                bl.Captured = true;
            }
            var s = StatsFor(c);
            float move = s == null ? 0f : s[(int)ClimbStat.moveSpeed];
            float sprint = s == null ? 0f : s[(int)ClimbStat.sprintSpeed];
            float climb = s == null ? 0f : s[(int)ClimbStat.climbSpeed];
            float jump = s == null ? 0f : s[(int)ClimbStat.jumpHeight];
            float throwF = s == null ? 0f : s[(int)ClimbStat.throwForce];
            float hunger = s == null ? 0f : s[(int)ClimbStat.hungerRate];

            mv.movementModifier += move - bl.AppliedMove; bl.AppliedMove = move;
            mv.sprintMultiplier += sprint - bl.AppliedSprint; bl.AppliedSprint = sprint;
            float dClimb = climb - bl.AppliedClimb; bl.AppliedClimb = climb;
            if (c.refs.climbing != null) c.refs.climbing.climbSpeedMod += dClimb;
            if (c.refs.ropeHandling != null) c.refs.ropeHandling.climbSpeedMod += dClimb;
            if (c.refs.vineClimbing != null) c.refs.vineClimbing.climbSpeedMod += dClimb;
            mv.jumpImpulse = bl.BaseJump * (1f + jump);
            items.minThrowForce = bl.BaseMinThrow * (1f + throwF);
            items.maxThrowForce = bl.BaseMaxThrow * (1f + throwF);
            aff.hungerPerSecond = bl.BaseHunger * (1f + hunger);
            try { aff.UpdateExtraJumps(); } catch { }
            Plugin.Verbose($"Effects on {c.characterName} (actor {NetSend.ActorOf(c)}): move {mv.movementModifier:0.00} sprint {mv.sprintMultiplier:0.00} climb {c.refs.climbing.climbSpeedMod:0.00} jump {mv.jumpImpulse:0.0} (base {bl.BaseJump:0.0}) throw {items.minThrowForce:0.0}-{items.maxThrowForce:0.0} hunger {aff.hungerPerSecond:0.00000} extraJumps {aff.totalExtraJumps}");
        }

        private static System.Collections.IEnumerator ApplyLater(Character c)
        {
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                if (c == null) yield break;
                if (c.refs != null && c.refs.movement != null && c.refs.items != null && c.refs.afflictions != null) { Apply(c); yield break; }
            }
        }

        /// <summary>stats.json toughness / resists / fallDamage, used by the AddStatus prefix on the owner.</summary>
        public static float ScaleStatus(float[] s, CharacterAfflictions.STATUSTYPE type, float amount, bool isFall)
        {
            float t = s[(int)ClimbStat.toughness];
            switch (type)
            {
                case CharacterAfflictions.STATUSTYPE.Injury:
                case CharacterAfflictions.STATUSTYPE.Hunger:
                case CharacterAfflictions.STATUSTYPE.Cold:
                case CharacterAfflictions.STATUSTYPE.Poison:
                case CharacterAfflictions.STATUSTYPE.Hot:
                case CharacterAfflictions.STATUSTYPE.Drowsy:
                case CharacterAfflictions.STATUSTYPE.Spores:
                case CharacterAfflictions.STATUSTYPE.Thorns:
                    amount *= t >= 0f ? 1f / (1f + t) : 1f - t;
                    break;
            }
            if (type == CharacterAfflictions.STATUSTYPE.Cold) amount *= 1f - s[(int)ClimbStat.coldResist];
            if (type == CharacterAfflictions.STATUSTYPE.Poison || type == CharacterAfflictions.STATUSTYPE.Spores) amount *= 1f - s[(int)ClimbStat.poisonResist];
            if (type == CharacterAfflictions.STATUSTYPE.Hot) amount *= 1f - s[(int)ClimbStat.heatResist];
            if (isFall && type == CharacterAfflictions.STATUSTYPE.Injury) amount *= Mathf.Max(0f, 1f - s[(int)ClimbStat.fallDamage]);
            return amount;
        }

        /// <summary>stats.json bonusStamina: at a lit campfire the local climber gets extra (yellow) stamina.</summary>
        public static void OnCampfire(Character local)
        {
            var s = StatsFor(local);
            if (s == null) return;
            float b = s[(int)ClimbStat.bonusStamina];
            if (Math.Abs(b) > 0.001f) local.AddExtraStamina(b);
        }

        /// <summary>stats.json appliesVia "tick": phoenix and injuryRegen on the owner, every frame.</summary>
        public static void Tick(Character local)
        {
            if (local == null || local.data == null || local.data.dead) return;
            var s = StatsFor(local);
            if (s == null) return;
            var aff = local.refs.afflictions;
            float regen = s[(int)ClimbStat.injuryRegen];
            if (regen > 0f && aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Injury) > 0f)
                aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Injury, regen * Time.deltaTime);
            int me = NetSend.LocalActor;
            if (s[(int)ClimbStat.phoenix] > 0.5f && !PhoenixUsed.Contains(me) && aff.shouldPassOut && !local.data.passedOut)
            {
                PhoenixUsed.Add(me);
                aff.ClearAllStatus(false, true);
                local.AddExtraStamina(0.5f);
                NetSend.Raise(new PhoenixUsed { actor = me }, Photon.Realtime.ReceiverGroup.All);
            }
        }

        public static StateSync BuildStateSync(int sessionId)
        {
            var actors = new List<int>(); var counts = new List<byte>(); var cards = new List<byte>(); var phoenix = new List<byte>();
            foreach (var kv in CardsByActor)
            {
                actors.Add(kv.Key);
                counts.Add((byte)Math.Min(255, kv.Value.Count));
                cards.AddRange(kv.Value.Take(255));
                phoenix.Add((byte)(PhoenixUsed.Contains(kv.Key) ? 1 : 0));
            }
            return new StateSync { actors = actors.ToArray(), counts = counts.ToArray(), cards = cards.ToArray(), phoenixUsed = phoenix.ToArray(), sessionId = sessionId };
        }

        public static void ApplyStateSync(StateSync m)
        {
            int pos = 0;
            for (int i = 0; i < m.actors.Length; i++)
            {
                int n = i < m.counts.Length ? m.counts[i] : 0;
                var list = new List<byte>();
                for (int k = 0; k < n && pos < m.cards.Length; k++) list.Add(m.cards[pos++]);
                SetAll(m.actors[i], list);
                if (i < m.phoenixUsed.Length && m.phoenixUsed[i] != 0) PhoenixUsed.Add(m.actors[i]);
            }
        }
    }
}
