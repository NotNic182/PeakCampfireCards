using System;
using System.Collections.Generic;

namespace CampfireCards.Core
{
    /// <summary>A cooldown on a monotonically increasing clock (seconds).</summary>
    public sealed class Cooldown
    {
        public float Seconds;
        private float _readyAt = float.NegativeInfinity;
        public Cooldown(float seconds) { Seconds = seconds; }
        public bool Ready(float now) => now >= _readyAt;
        public float Remaining(float now) => Math.Max(0f, _readyAt - now);
        public void Trigger(float now) => _readyAt = now + Seconds;
        public void Reset() => _readyAt = float.NegativeInfinity;
    }

    /// <summary>A charge that refills when the biome changes (per-biome) or never (per-run).</summary>
    public sealed class Charge
    {
        private readonly bool _perBiome;
        private int _usedInBiome = int.MinValue;
        private bool _usedInRun;
        public Charge(bool perBiome) { _perBiome = perBiome; }
        public bool Available(int biome) => _perBiome ? _usedInBiome != biome : !_usedInRun;
        public bool TryUse(int biome)
        {
            if (!Available(biome)) return false;
            if (_perBiome) _usedInBiome = biome; else _usedInRun = true;
            return true;
        }
        public void Reset() { _usedInBiome = int.MinValue; _usedInRun = false; }
    }

    /// <summary>Fires once when a value drops below a threshold, then waits until it is back above before it can fire again.</summary>
    public sealed class BelowLatch
    {
        private bool _armed = true;
        public bool Update(float value, float threshold)
        {
            if (value >= threshold) { _armed = true; return false; }
            if (!_armed) return false;
            _armed = false;
            return true;
        }
        public void Reset() => _armed = true;
    }

    /// <summary>Fires once when a value reaches a threshold, re-arming when it falls back below.</summary>
    public sealed class AboveLatch
    {
        private bool _armed = true;
        public bool Update(float value, float threshold)
        {
            if (value < threshold) { _armed = true; return false; }
            if (!_armed) return false;
            _armed = false;
            return true;
        }
        public void Reset() => _armed = true;
    }

    /// <summary>
    /// Momentum Fiend: sprint continuously for Buildup seconds to activate; stopping after activation is a penalty event,
    /// at most once per PenaltyCooldown. The buildup restarts whenever sprinting stops.
    /// </summary>
    public sealed class MomentumMachine
    {
        public float Buildup = 2f, PenaltyCooldown = 30f;
        private float _sprinted;
        private float _lastPenalty = float.NegativeInfinity;
        public bool Active { get; private set; }

        /// <returns>true when a stop after activation should apply the penalty.</returns>
        public bool Update(float dt, bool sprinting, float now)
        {
            if (sprinting)
            {
                _sprinted += dt;
                if (_sprinted >= Buildup) Active = true;
                return false;
            }
            bool penalty = false;
            if (Active && now - _lastPenalty >= PenaltyCooldown)
            {
                penalty = true;
                _lastPenalty = now;
            }
            Active = false;
            _sprinted = 0f;
            return penalty;
        }

        public void Reset() { _sprinted = 0f; Active = false; _lastPenalty = float.NegativeInfinity; }
    }

    /// <summary>
    /// Pulse Climber: after Buildup seconds of continuous climbing the boost is on for Boost seconds, then the buildup
    /// starts again from zero. Stopping climbing resets the buildup.
    /// </summary>
    public sealed class PulseMachine
    {
        public float Buildup = 3f, Boost = 1f;
        private float _climbed, _boostLeft;
        public bool Active => _boostLeft > 0f;

        public void Update(float dt, bool climbing)
        {
            if (_boostLeft > 0f)
            {
                _boostLeft -= dt;
                if (_boostLeft <= 0f) { _boostLeft = 0f; _climbed = 0f; }
                return;
            }
            if (!climbing) { _climbed = 0f; return; }
            _climbed += dt;
            if (_climbed >= Buildup) { _boostLeft = Boost; _climbed = 0f; }
        }

        public void Reset() { _climbed = 0f; _boostLeft = 0f; }
    }

    /// <summary>
    /// Delayed Consequences: part of each fall's Injury is queued and lands later, reduced. Each fall keeps its own timer;
    /// the delayed Injury is not a fall, so it is never deferred again.
    /// </summary>
    public sealed class DelayedInjuryQueue
    {
        private struct Entry { public float Due; public float Amount; }
        private readonly List<Entry> _entries = new List<Entry>();
        public float DeferShare = 0.75f, Delay = 20f, Keep = 0.70f;

        public int Pending => _entries.Count;

        /// <returns>the Injury to apply right now.</returns>
        public float Add(float fallInjury, float now)
        {
            float deferred = fallInjury * DeferShare;
            _entries.Add(new Entry { Due = now + Delay, Amount = deferred * Keep });
            return fallInjury - deferred;
        }

        /// <returns>the total delayed Injury due at this time.</returns>
        public float Tick(float now)
        {
            float due = 0f;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Due <= now)
                {
                    due += _entries[i].Amount;
                    _entries.RemoveAt(i);
                }
            }
            return due;
        }

        public void Clear() => _entries.Clear();
    }

    /// <summary>
    /// Host-side bookkeeping for team revivals: each owner's per-biome revive charge is spent once, and each
    /// incapacitation event is revived at most once even if several auras overlap.
    /// </summary>
    public sealed class ReviveArbiter
    {
        private readonly Dictionary<int, int> _ownerBiome = new Dictionary<int, int>();
        private readonly HashSet<long> _events = new HashSet<long>();

        public bool TryGrant(int ownerActor, int biome, int victimActor, int incapEvent)
        {
            long key = ((long)victimActor << 32) | (uint)incapEvent;
            if (_events.Contains(key)) return false;
            int used;
            if (_ownerBiome.TryGetValue(ownerActor, out used) && used == biome) return false;
            _ownerBiome[ownerActor] = biome;
            _events.Add(key);
            return true;
        }

        public void Reset() { _ownerBiome.Clear(); _events.Clear(); }
    }

    public static class Rolls
    {
        /// <summary>Loaded Dice: one deterministic roll per player per biome per run.</summary>
        public static bool LoadedDice(int runSeed, int biome, int actor, float chance)
        {
            unchecked
            {
                int seed = runSeed * 397 ^ biome * 7919 ^ actor * 104729;
                return new Random(seed).NextDouble() < chance;
            }
        }
    }
}
