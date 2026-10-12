using System.Collections.Generic;
using System.Linq;
using CampfireCards.Abilities;
using CampfireCards.Core;
using CampfireCards.Generated;
using UnityEngine;

namespace CampfireCards.Cards
{
    /// <summary>
    /// The local player's card abilities and per-run bookkeeping: current biome, run seed, recent safe footholds and
    /// incapacitation events. Everything here is cleared at run end.
    /// </summary>
    public static class LocalCards
    {
        private static readonly Dictionary<byte, Ability> _abilities = new Dictionary<byte, Ability>();
        // A removed card stops ticking and cleans up its live effects, but its spent charges/cooldowns belong to the run.
        private static readonly Dictionary<byte, Ability> _retired = new Dictionary<byte, Ability>();
        public static int Biome = -1;
        public static int RunSeed;
        public static int IncapEvent;
        /// <summary>The campfire most recently lit (for item spawns next to it).</summary>
        public static Campfire LastCampfire;
        /// <summary>true while the local scout is down from an incapacitation that already had its revival chance.</summary>
        private static bool _downHandled;
        private static readonly System.Func<Character, bool> IsSliding =
            HarmonyLib.AccessTools.MethodDelegate<System.Func<Character, bool>>(HarmonyLib.AccessTools.Method(typeof(Character), "IsSliding"));
        private static readonly List<KeyValuePair<float, Vector3>> _footholds = new List<KeyValuePair<float, Vector3>>();
        private static float _nextFoothold;

        public static IEnumerable<Ability> All => _abilities.Values;

        public static T Get<T>() where T : Ability
        {
            foreach (var a in _abilities.Values) if (a is T t) return t;
            return null;
        }

        public static void OnAcquired(CardDef def, bool live)
        {
            Ability a;
            if (_abilities.TryGetValue(def.Id, out a))
            {
                // The bonus deal belongs to each accepted pick, so its acquisition price must too. Reuse the
                // ability rather than resetting any timers, and leave other cards' duplicate behaviour unchanged.
                if (!live || !def.DrawsCards) return;
            }
            else
            {
                if (_retired.TryGetValue(def.Id, out a)) _retired.Remove(def.Id);
                else a = AbilityRegistry.Create(def);
                if (a == null) return;
                _abilities[def.Id] = a;
            }
            var me = Character.localCharacter;
            if (live && me != null)
            {
                try { a.OnAcquired(me); }
                catch (System.Exception e) { Plugin.Log.LogError($"{def.Name} OnAcquired: {e}"); }
            }
        }

        public static void OnRemoved(CardDef def)
        {
            Ability a;
            if (!_abilities.TryGetValue(def.Id, out a)) return;
            _abilities.Remove(def.Id);
            _retired[def.Id] = a;
            var me = Character.localCharacter;
            try { if (me != null) a.OnRemoved(me); }
            catch (System.Exception e) { Plugin.Log.LogError($"{def.Name} OnRemoved: {e}"); }
        }

        public static void Reset()
        {
            var me = Character.localCharacter;
            foreach (var a in _abilities.Values.ToList())
            {
                try { if (me != null) a.OnRemoved(me); } catch { }
            }
            _abilities.Clear();
            _retired.Clear();
            Biome = -1;
            IncapEvent = 0;
            _downHandled = false;
            LastCampfire = null;
            _footholds.Clear();
        }

        public static void OnRunStarted(int seed)
        {
            RunSeed = seed;
            Biome = -1;
            _footholds.Clear();
        }

        public static void OnBiomeStarted(Character me, int biome)
        {
            if (biome == Biome) return;
            Biome = biome;
            Plugin.Verbose("Biome started: " + biome);
            foreach (var a in _abilities.Values.ToList())
            {
                try { a.OnBiomeStarted(me, biome); }
                catch (System.Exception e) { Plugin.Log.LogError($"{a.Card.Name} OnBiomeStarted: {e}"); }
            }
        }

        public static void OnCampfireLit(Character me, Campfire fire)
        {
            LastCampfire = fire;
            foreach (var a in _abilities.Values.ToList())
            {
                try { a.OnCampfireLit(me, false); }
                catch (System.Exception e) { Plugin.Log.LogError($"{a.Card.Name} OnCampfireLit: {e}"); }
            }
        }

        public static void Tick(Character me, float dt)
        {
            if (_downHandled && !me.data.passedOut && !me.data.fullyPassedOut && !me.data.dead) _downHandled = false;   // revived
            TrackFoothold(me);
            foreach (var a in _abilities.Values.ToList())
            {
                try { a.Tick(me, dt); }
                catch (System.Exception e) { if (Time.frameCount % 600 == 0) Plugin.Log.LogError($"{a.Card.Name} Tick: {e}"); }
            }
        }

        public static void OnFood(Character me, FoodEvent food)
        {
            foreach (var a in _abilities.Values.ToList())
            {
                try { a.OnFoodEaten(me, food); }
                catch (System.Exception e) { Plugin.Log.LogError($"{a.Card.Name} OnFoodEaten: {e}"); }
            }
        }

        public static void OnCurative(Character me, CurativeEvent cure)
        {
            foreach (var a in _abilities.Values.ToList())
            {
                try { a.OnCurativeUsed(me, cure); }
                catch (System.Exception e) { Plugin.Log.LogError($"{a.Card.Name} OnCurativeUsed: {e}"); }
            }
        }

        public static bool Flag(Condition c)
        {
            foreach (var a in _abilities.Values) if (a.ConditionFlag(c)) return true;
            return false;
        }

        /// <summary>
        /// The local scout went down (passed out, or died without passing out). One incapacitation = one revival chance:
        /// a pass-out followed by death is the same event, so it never gets a second revival.
        /// </summary>
        public static void OnDown(Character me)
        {
            if (_downHandled) return;
            _downHandled = true;
            bool revived = TryRevive(me);
            Plugin.Verbose($"Incapacitation {IncapEvent}: revival {(revived ? "started" : "none")}");
        }

        /// <summary>The local scout is up again (woke up or was revived): the next time it goes down is a new event.</summary>
        public static void OnRecovered() { _downHandled = false; }

        /// <summary>Give each revival card a chance, in a fixed order, and stop at the first.</summary>
        private static bool TryRevive(Character me)
        {
            IncapEvent++;
            foreach (var name in new[] { "SafetyNet", "EternalCampfire" })
                foreach (var a in _abilities.Values)
                    if (a.Card.Ability == name && a.TryRevive(me, IncapEvent)) return true;
            // auras from teammates (Eternal Campfire owned by someone else)
            return Ability_EternalCampfire.RequestFromTeammate(me, IncapEvent);
        }

        public static IEnumerable<Ability> Manual => _abilities.Values.Where(a => a.IsManual);

        // ------------------------------------------------------------------ safe footholds (Phoenix Clause)
        private static void TrackFoothold(Character me)
        {
            if (Time.time < _nextFoothold) return;
            _nextFoothold = Time.time + 0.5f;
            var d = me.data;
            if (!d.isGrounded || d.groundedFor < 0.75f || d.passedOut || d.dead || me.warping || IsSliding(me)) return;
            if (d.avarageVelocity.magnitude > 6f) return;
            _footholds.Add(new KeyValuePair<float, Vector3>(Time.time, me.Center));
            if (_footholds.Count > 120) _footholds.RemoveAt(0);
        }

        /// <summary>A recently occupied, grounded position at least minAge seconds old (newest first), checked again for ground.</summary>
        public static bool RecentFoothold(float minAge, out Vector3 pos)
        {
            for (int i = _footholds.Count - 1; i >= 0; i--)
            {
                if (Time.time - _footholds[i].Key < minAge) continue;
                if (SafeSpot.Validate(_footholds[i].Value, out pos)) return true;
            }
            pos = default(Vector3);
            return false;
        }
    }
}
