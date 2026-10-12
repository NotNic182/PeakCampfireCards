using System;
using System.Collections.Generic;
using CampfireCards.Generated;

namespace CampfireCards.Core
{
    /// <summary>
    /// Permanent Double Deal penalties: one per acquisition, kept for the run even if the card is sacrificed. Each
    /// penalty fills the next slot, so a replayed message (same slot) cannot reroll or duplicate one.
    /// </summary>
    public sealed class PenaltyLedger
    {
        private static readonly byte[] None = new byte[0];
        private readonly Dictionary<int, List<byte>> _choices = new Dictionary<int, List<byte>>();
        public static bool Valid(byte choice) => choice >= 1 && choice <= 4;
        public IReadOnlyList<byte> Of(int actor) => _choices.TryGetValue(actor, out var v) ? (IReadOnlyList<byte>)v : None;
        public int Count(int actor) => Of(actor).Count;
        /// <summary>slot = how many penalties the owner had before this one; any other slot is ignored.</summary>
        public bool TrySet(int actor, int slot, byte choice)
        {
            if (!Valid(choice)) return false;
            if (!_choices.TryGetValue(actor, out var list)) _choices[actor] = list = new List<byte>();
            if (slot != list.Count) return false;
            list.Add(choice);
            return true;
        }
        public void Restore(int actor, IEnumerable<byte> choices)
        {
            _choices.Remove(actor);
            var list = new List<byte>();
            foreach (var c in choices) if (Valid(c)) list.Add(c);
            if (list.Count > 0) _choices[actor] = list;
        }
        public void Reset() => _choices.Clear();
    }

    public static class CardPenaltyRules
    {
        public static Channel ChannelFor(byte choice)
        {
            switch (choice)
            {
                case 1: return Channel.climbStamina;
                case 2: return Channel.sprintStamina;
                case 3: return Channel.allInjury;
                case 4: return Channel.hungerRate;
                default: throw new ArgumentOutOfRangeException(nameof(choice));
            }
        }
        public static string Parameter(byte choice)
        {
            switch (choice)
            {
                case 1: return "climbPenalty";
                case 2: return "sprintPenalty";
                case 3: return "injuryPenalty";
                case 4: return "hungerPenalty";
                default: throw new ArgumentOutOfRangeException(nameof(choice));
            }
        }
    }

    /// <summary>Counts alive play time; a pending power waits until it can actually be applied.</summary>
    public sealed class PeriodicPowerClock
    {
        private float _elapsed;
        public void Advance(float dt, bool alive) { if (alive && dt > 0f && !float.IsInfinity(dt)) _elapsed += dt; }
        public float Remaining(float interval) => Math.Max(0f, interval - _elapsed);
        public bool Ready(float interval) => interval > 0f && _elapsed >= interval;
        public void Consume() => _elapsed = 0f;
    }

    public static class ReviewedEffectMath
    {
        /// <summary>Scale active progress; when carrying pauses it, add only the excess fraction of a normal tick.</summary>
        public static float CarriedDeathTimer(float before, float after, float multiplier, float normalTick = 0f)
        {
            if (before < 0f || after < before || multiplier <= 0f) return after;
            if (after == before) return after + Math.Max(0f, multiplier - 1f) * Math.Max(0f, normalTick);
            return before + (after - before) * multiplier;
        }
        public static float RevivalCurse(float before, float after, float multiplier) =>
            after > before ? before + (after - before) * Math.Max(0f, multiplier) : after;
    }
}
