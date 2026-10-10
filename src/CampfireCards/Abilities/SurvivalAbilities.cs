using System;
using System.Collections.Generic;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Net;
using Photon.Pun;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>Emergency Purge: once per biome, when Poison, Spores or Drowsy reaches 35, remove 25 of that one status.</summary>
    public sealed class Ability_EmergencyPurge : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: true);
        private readonly Dictionary<CharacterAfflictions.STATUSTYPE, AboveLatch> _latches = new Dictionary<CharacterAfflictions.STATUSTYPE, AboveLatch>
        {
            { CharacterAfflictions.STATUSTYPE.Poison, new AboveLatch() },
            { CharacterAfflictions.STATUSTYPE.Spores, new AboveLatch() },
            { CharacterAfflictions.STATUSTYPE.Drowsy, new AboveLatch() },
        };

        public override void Tick(Character me, float dt)
        {
            if (me.data.dead) return;
            float threshold = P("threshold", 0.35f);
            foreach (var kv in _latches)
            {
                if (!kv.Value.Update(CardStatus.Get(me, kv.Key), threshold - 0.0001f)) continue;
                if (!_charge.TryUse(LocalCards.Biome)) continue;
                CardStatus.Remove(me, kv.Key, P("remove", 0.2f));
                Ui.Hud.Toast(Card.Name + ": " + kv.Key + " cleansed");
                return;   // only the status that triggered
            }
        }

        public override string Status(Character me) => _charge.Available(LocalCards.Biome) ? Card.Name + ": ready" : Card.Name + ": used this biome";
    }

    /// <summary>
    /// Phoenix Clause: once per run, a fall that would knock you out (or a kill plane) is cancelled; you are moved to a
    /// recent safe foothold with 40 Injury and skip your next campfire pick.
    /// </summary>
    public sealed class Ability_PhoenixClause : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: false);
        /// <summary>After triggering, kill planes are ignored until the teleport has landed.</summary>
        private float _shieldUntil;
        public static Ability_PhoenixClause Owned => LocalCards.Get<Ability_PhoenixClause>();

        public bool Available => _charge.Available(0) && !Revival.Busy;

        /// <summary>Called by the fall-damage hook with the Injury about to land. Returns true when the fall was prevented.</summary>
        public bool TryPreventFatalFall(Character me, float injury)
        {
            if (!Available || me.data.dead || me.data.passedOut) return false;
            var aff = me.refs.afflictions;
            float after = aff.statusSum + Mathf.Min(injury, Mathf.Max(0f, aff.GetStatusCap(CharacterAfflictions.STATUSTYPE.Injury) - aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Injury)));
            if (after <= 0.99f) return false;
            return Trigger(me);
        }

        /// <summary>Called every physics step the scout touches a kill plane.</summary>
        public bool TryPreventKillPlane(Character me)
        {
            if (Time.time < _shieldUntil || Revival.Busy) return !me.data.dead;
            return Available && !me.data.dead && Trigger(me);
        }

        private bool Trigger(Character me)
        {
            Vector3 pos;
            if (!LocalCards.RecentFoothold(2f, out pos))
            {
                try
                {
                    var spawn = MapHandler.CurrentBaseCampSpawnPoint;
                    if (spawn == null || !SafeSpot.Near(spawn.position, 0f, 6f, out pos)) return false;
                }
                catch { return false; }
            }
            _charge.TryUse(0);
            _shieldUntil = Time.time + 4f;
            Revival.TeleportTo(me, pos, P("injury", 0.4f));
            NetSend.Raise(new SkipNextPick { actor = NetSend.LocalActor }, Photon.Realtime.ReceiverGroup.All);
            Ui.Hud.Toast(Card.Name.ToUpperInvariant() + "! You skip your next card pick.");
            return true;
        }

        public override string Status(Character me) => _charge.Available(0) ? Card.Name + ": ready" : Card.Name + ": used";
    }

    /// <summary>Delayed Consequences: 75% of each fall's Injury waits 20 s, then 70% of it lands (not as a fall, never deferred again).</summary>
    public sealed class Ability_DelayedConsequences : Ability
    {
        private readonly DelayedInjuryQueue _q = new DelayedInjuryQueue();
        public static Ability_DelayedConsequences Owned => LocalCards.Get<Ability_DelayedConsequences>();

        public float Defer(float fallInjury)
        {
            _q.DeferShare = P("defer", 0.75f);
            _q.Delay = P("delay", 20f);
            _q.Keep = P("keep", 0.8f);
            return _q.Add(fallInjury, Now);
        }

        public override void Tick(Character me, float dt)
        {
            float due = _q.Tick(Now);
            if (due > 0f && !me.data.dead) CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Injury, due);
        }

        public override void OnRemoved(Character me) => _q.Clear();

        public override string Status(Character me) => _q.Pending > 0 ? $"{Card.Name}: {_q.Pending} pending" : null;
    }

    /// <summary>Safety Net: once per biome, press the key to place an anchor on safe ground; going down revives you there with 40 Injury.</summary>
    public sealed class Ability_SafetyNet : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: true);
        private bool _hasAnchor;
        private Vector3 _anchor;
        public override bool IsManual => true;

        public override bool Activate(Character me)
        {
            if (_hasAnchor) { Ui.Hud.Toast(Card.Name + ": anchor already placed"); return false; }
            if (!_charge.Available(LocalCards.Biome)) { Ui.Hud.Toast(Card.Name + ": already used this biome"); return false; }
            var d = me.data;
            Vector3 pos;
            if (!d.isGrounded || d.isClimbingAnything || d.passedOut || me.warping || !SafeSpot.Validate(me.Center, out pos))
            {
                Ui.Hud.Toast(Card.Name + ": stand on solid, flat ground to place the anchor");
                return false;
            }
            _charge.TryUse(LocalCards.Biome);
            _hasAnchor = true;
            _anchor = pos;
            Ui.Hud.Toast(Card.Name + ": anchor placed");
            return true;
        }

        public override bool TryRevive(Character me, int incapEvent)
        {
            if (!_hasAnchor) return false;
            Vector3 pos;
            if (!SafeSpot.Validate(_anchor, out pos)) { _hasAnchor = false; Ui.Hud.Toast(Card.Name + ": the anchor spot is no longer safe"); return false; }
            _hasAnchor = false;
            Plugin.Run(ReviveSoon(me, pos));
            return true;
        }

        private System.Collections.IEnumerator ReviveSoon(Character me, Vector3 pos)
        {
            yield return new WaitForSeconds(1.5f);   // let the pass-out settle, as the game's own revives do
            if (me != null && (me.data.passedOut || me.data.dead)) Revival.ReviveAt(me, pos, P("injury", 0.4f), -1f);
            Ui.Hud.Toast(Card.Name.ToUpperInvariant() + "!");
        }

        public override void OnBiomeStarted(Character me, int biome)
        {
            if (_hasAnchor) Ui.Hud.Toast(Card.Name + ": your unused anchor expired");
            _hasAnchor = false;
        }

        public override string Status(Character me)
        {
            string key = Plugin.SafetyNetKeyLabel;
            if (_hasAnchor) return $"{Card.Name} [{key}]: anchor placed";
            return $"{Card.Name} [{key}]: " + (_charge.Available(LocalCards.Biome) ? "place anchor" : "used this biome");
        }
    }

    /// <summary>
    /// The Eternal Campfire: team aura (modifiers in cards.json) plus one revive per biome per owner for the first scout
    /// to go down inside it. The host arbitrates so overlapping auras never revive twice.
    /// </summary>
    public sealed class Ability_EternalCampfire : Ability
    {
        public override bool TryRevive(Character me, int incapEvent)
        {
            NetSend.RaiseToMaster(new ReviveRequest { ownerActor = NetSend.LocalActor, biome = LocalCards.Biome, incapEvent = incapEvent });
            return true;
        }

        /// <summary>The local scout went down near a teammate who owns The Eternal Campfire.</summary>
        public static bool RequestFromTeammate(Character me, int incapEvent)
        {
            var s = CardSettings.Current;
            int best = int.MinValue; float bestDist = float.MaxValue;
            foreach (var kv in Effects.CardsByActor)
            {
                if (kv.Key == NetSend.LocalActor) continue;
                foreach (var id in kv.Value)
                {
                    var def = s.Card(id);
                    if (def == null || def.Ability != "EternalCampfire") continue;
                    Character owner;
                    if (!PlayerHandler.TryGetCharacter(kv.Key, out owner) || owner == null || owner.data.dead) continue;
                    float dist = Vector3.Distance(owner.Center, me.Center);
                    if (dist <= s.Param(def, "radius", 15f) && dist < bestDist) { bestDist = dist; best = kv.Key; }
                }
            }
            if (best == int.MinValue) return false;
            NetSend.RaiseToMaster(new ReviveRequest { ownerActor = best, biome = LocalCards.Biome, incapEvent = incapEvent });
            return true;
        }

        /// <summary>
        /// Host side: approve at most one revive per owner per biome and per incapacitation. Reset with the run
        /// (RunState.Reset) and told when a player leaves (Hooks.OnPhotonEvent), so nothing carries into the next run.
        /// </summary>
        public static readonly ReviveArbiter Arbiter = new ReviveArbiter();

        public static void HostHandle(ReviveRequest m, int sender)
        {
            var s = CardSettings.Current;
            if (!Effects.HasAbility(m.ownerActor, "EternalCampfire")) return;
            Character owner, victim;
            if (!PlayerHandler.TryGetCharacter(m.ownerActor, out owner) || !PlayerHandler.TryGetCharacter(sender, out victim)) return;
            if (owner == null || victim == null || owner.data.dead) return;
            float radius = 15f;
            foreach (var id in Effects.CardsOf(m.ownerActor)) { var d = s.Card(id); if (d != null && d.Ability == "EternalCampfire") radius = s.Param(d, "radius", 15f); }
            if (sender != m.ownerActor && Vector3.Distance(owner.Center, victim.Center) > radius + 2f) return;
            // the host's own biome counts, never the one in the request (a client cannot refill the charge by naming another)
            if (!Arbiter.TryGrant(m.ownerActor, LocalCards.Biome, sender, m.incapEvent))
            {
                Plugin.Verbose($"Eternal Campfire: refused revive of {sender} (owner {m.ownerActor}, event {m.incapEvent}, biome {LocalCards.Biome})");
                return;
            }
            NetSend.RaiseTo(new ReviveGranted { ownerActor = m.ownerActor, incapEvent = m.incapEvent }, sender);
        }

        /// <summary>Victim side: the host approved; revive at a safe spot nearby.</summary>
        public static void OnGranted(Character me, ReviveGranted m)
        {
            if (me == null || !(me.data.passedOut || me.data.dead)) return;
            var s = CardSettings.Current;
            var def = System.Linq.Enumerable.FirstOrDefault(Generated.CardTable.Cards, c => c.Ability == "EternalCampfire");
            Vector3 origin = me.data.dead ? me.LastLivingPosition : me.Center;
            Vector3 pos;
            if (!SafeSpot.Near(origin, 0f, 6f, out pos) && !LocalCards.RecentFoothold(0f, out pos)) { Ui.Hud.Toast((def != null ? def.Name : "Eternal Campfire") + ": no safe spot nearby"); return; }
            Revival.ReviveAt(me, pos, def != null ? s.Param(def, "injury", 0.4f) : 0.4f, def != null ? s.Param(def, "stamina", 0.5f) : 0.5f);
            Ui.Hud.Toast((def != null ? def.Name : "The Eternal Campfire").ToUpperInvariant() + " brought you back");
        }
    }

    /// <summary>Shared Burden: teammates near the owner take less Injury; the owner absorbs half of what was prevented (capped).</summary>
    public sealed class Ability_SharedBurden : Ability
    {
        /// <summary>Owner side: Injury sent by a protected teammate. Applied raw, so it is never shared or scaled again.</summary>
        public static void Absorb(Character me, float amount)
        {
            if (me == null || me.data.dead || amount <= 0f) return;
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Injury, amount);
        }
    }

    /// <summary>Hero's Arrival: the first teammate you revive each biome gives you both bonus stamina.</summary>
    public sealed class Ability_HerosArrival : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: true);

        /// <summary>Reviver side: the local scout revived the given teammate.</summary>
        public static void OnRevivedTeammate(int victimActor)
        {
            var a = LocalCards.Get<Ability_HerosArrival>();
            var me = Character.localCharacter;
            if (a == null || me == null || victimActor == NetSend.LocalActor) return;
            if (!a._charge.TryUse(LocalCards.Biome)) return;
            float bonus = a.P("bonus", 0.2f);
            me.AddExtraStamina(bonus);
            NetSend.RaiseTo(new BonusStamina { amount = bonus, reason = 1 }, victimActor);
            Ui.Hud.Toast(a.Card.Name.ToUpperInvariant() + "!");
        }

        public override string Status(Character me) => _charge.Available(LocalCards.Biome) ? Card.Name + ": ready" : Card.Name + ": used this biome";
    }
}
