using System;
using System.Collections.Generic;

namespace CampfireCards.Core
{
    /// <summary>Why the host refused a campfire item (Ok = spawn it).</summary>
    public enum SpawnVerdict { Ok, UnknownFire, AlreadySpawned, NotOwned }

    /// <summary>
    /// Host: campfire item rewards (Rope Baron, Supply Scout). Each owner gets each card's item once per campfire lit
    /// this run, whatever the client sends; repeated or replayed requests spawn nothing. Unity-free.
    /// </summary>
    public sealed class SpawnLedger
    {
        private readonly HashSet<byte> _litFires = new HashSet<byte>();
        private readonly HashSet<long> _spawned = new HashSet<long>();

        /// <summary>The host saw this campfire being lit (its segment) during the current run.</summary>
        public void FireLit(byte segment) => _litFires.Add(segment);

        public bool IsLit(byte segment) => _litFires.Contains(segment);

        /// <param name="ownsCardForItem">the host checked that the sender owns a card whose reward is this item</param>
        public SpawnVerdict TryClaim(int actor, byte item, byte segment, bool ownsCardForItem)
        {
            if (!ownsCardForItem) return SpawnVerdict.NotOwned;
            if (!_litFires.Contains(segment)) return SpawnVerdict.UnknownFire;
            long key = ((long)actor << 16) | ((long)item << 8) | segment;
            return _spawned.Add(key) ? SpawnVerdict.Ok : SpawnVerdict.AlreadySpawned;
        }

        public void Reset() { _litFires.Clear(); _spawned.Clear(); }
    }

    /// <summary>Something allowed once per key per biome (Hero's Arrival bonus stamina received from one teammate).</summary>
    public sealed class PerKeyBiomeCharge
    {
        private readonly Dictionary<int, int> _usedIn = new Dictionary<int, int>();

        public bool TryUse(int key, int biome)
        {
            int b;
            if (_usedIn.TryGetValue(key, out b) && b == biome) return false;
            _usedIn[key] = biome;
            return true;
        }

        public void Reset() => _usedIn.Clear();
    }

    /// <summary>
    /// One decision per item-consumption transaction. Several game hooks can fire for one use of one item (ReduceUses,
    /// ConsumeAndSpawn and ConsumeDelayed, in whatever order the item's actions run); they all share the first answer,
    /// so a once-per-biome charge is spent exactly once and every path agrees on keeping or using up the item.
    /// A transaction = one item (key) during one frame: every action of a use runs inside the same FinishCast call.
    /// </summary>
    public sealed class ItemTransaction
    {
        private bool _has;
        private int _key, _frame;
        private bool _result, _effectsFired;

        /// <summary>The decision for this item use; evaluate runs only for the first hook of the transaction.</summary>
        public bool Decide(int key, int frame, Func<bool> evaluate)
        {
            if (_has && _key == key && _frame == frame) return _result;
            _has = true; _key = key; _frame = frame; _effectsFired = false;
            _result = evaluate();
            return _result;
        }

        /// <summary>True only the first time per transaction: the item's on-consumed effects must run exactly once.</summary>
        public bool TryFireEffects(int key, int frame)
        {
            if (!_has || _key != key || _frame != frame || _effectsFired) return false;
            _effectsFired = true;
            return true;
        }

        public void Reset() { _has = false; _effectsFired = false; }
    }

    /// <summary>
    /// Thornbreaker (thornsRate) and the Eternal Campfire aura (negativeStatusRate) on Thorns. PEAK does not add Thorns
    /// through AddStatus: UpdateWeight recomputes the status from the number of thorns stuck in the scout (0.025 each)
    /// on every inventory or weight change. This ledger turns that count into "incoming accumulation": each newly stuck
    /// thorn is scaled by the multiplier active when it arrived, thorns already stuck keep their value when the multiplier
    /// changes (card removed, aura entered or left), recomputing without a new thorn changes nothing, and pulled thorns
    /// remove their proportional share. Values are in thorn units (1 = 0.025 status). Unity-free.
    /// </summary>
    public sealed class ThornsLedger
    {
        private int _raw;
        private float _scaled;

        public int Raw => _raw;
        public float Scaled => _scaled;

        /// <summary>Feed the game's freshly computed thorn count; returns the scaled thorn units to show.</summary>
        public float Update(int rawCount, float multiplier)
        {
            if (rawCount <= 0) { _raw = 0; _scaled = 0f; return 0f; }
            if (rawCount > _raw) _scaled += (rawCount - _raw) * Math.Max(0f, multiplier);
            else if (rawCount < _raw) _scaled = _raw > 0 ? _scaled * rawCount / _raw : 0f;
            _raw = rawCount;
            return _scaled;
        }

        /// <summary>Whole thorn units for the game's 0.025 status steps (a half is rounded up, so one thorn is never erased).</summary>
        public int ScaledSteps => (int)Math.Floor(_scaled + 0.5f + 1e-4f);

        public void Reset() { _raw = 0; _scaled = 0f; }
    }
}
