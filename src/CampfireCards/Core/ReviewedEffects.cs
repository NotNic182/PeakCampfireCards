using System;
using System.Collections.Generic;
using CampfireCards.Generated;

namespace CampfireCards.Core
{
    /// <summary>One permanent Double Deal penalty per owner. Replayed messages cannot reroll it.</summary>
    public sealed class PenaltyLedger
    {
        private readonly Dictionary<int, byte> _choices = new Dictionary<int, byte>();
        public static bool Valid(byte choice) => choice >= 1 && choice <= 4;
        public byte Of(int actor) => _choices.TryGetValue(actor, out var v) ? v : (byte)0;
        public bool TrySet(int actor, byte choice)
        {
            if (!Valid(choice) || _choices.ContainsKey(actor)) return false;
            _choices[actor] = choice;
            return true;
        }
        public void Restore(int actor, byte choice) { _choices.Remove(actor); if (Valid(choice)) _choices[actor] = choice; }
        public void Forget(int actor) => _choices.Remove(actor);
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
