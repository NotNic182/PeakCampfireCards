using System;
using System.Collections.Generic;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using Photon.Pun;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>
    /// Phoenix Clause: once per run, a fall that would knock you out (or a kill plane) is cancelled; you are moved to a
    /// recent safe foothold without extra Injury.
    /// </summary>
    public sealed class Ability_PhoenixClause : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: false);
        /// <summary>After triggering, kill planes are ignored until the teleport has landed.</summary>
        private float _shieldUntil;
        public static Ability_PhoenixClause Owned => LocalCards.Get<Ability_PhoenixClause>();

        public bool Available => _charge.Available(0) && !Revival.Busy;

        /// <summary>Called by the fall-damage hook with the Injury about to land. Returns true when the fall was prevented.</summary>
        public bool TryPreventFatalFall(Character me, float injury, float deferred = 0f, bool ignoreSkeleton = false)
        {
            if (!Available || me.data.dead || me.data.passedOut) return false;
            var aff = me.refs.afflictions;
            var type = CharacterAfflictions.STATUSTYPE.Injury;
            float nativeMultiplier = NativeInjuryRules.Multiplier(me.data.isSkeleton, ignoreSkeleton, Ascents.etcDamageMultiplier);
            // the step leftover the mod keeps back (Hooks.Status, StatusCarry) lands with the next Injury, so count it as pending
            float pending = aff.GetIncrementalStatus(type) + Hooks.PendingInjuryCarry;
            // the threshold follows bonus max stamina (hooks.json passOutThreshold), or the rescue would burn on
            // falls that no longer knock the scout out; GetStatusCap already returns the raised Injury cap
            if (!NativeInjuryRules.WouldPassOut(aff.statusSum, aff.GetCurrentStatus(type), pending,
                aff.GetStatusCap(type), nativeMultiplier, injury, deferred, Hooks.PassOutThreshold())) return false;
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
            Revival.TeleportTo(me, pos, -1f); // prevents this fall; does not add or clear unrelated Injury
            Ui.Hud.Toast(Card.Name.ToUpperInvariant() + "! Back on safe ground.");
            return true;
        }

        public override AbilityStatus Status(Character me) =>
            _charge.Available(0) ? AbilityStatus.Ok(Line(null, Ui_AbilityBar.Ready)) : AbilityStatus.Wait(Line(null, Ui_AbilityBar.UsedThisRun));
    }

    /// <summary>Delayed Consequences: 75% of each fall's Injury waits 20 s, then 80% of it lands (not as a fall, never deferred again).</summary>
    public sealed class Ability_DelayedConsequences : Ability
    {
        private readonly DelayedInjuryQueue _q = new DelayedInjuryQueue();
        public static Ability_DelayedConsequences Owned => LocalCards.Get<Ability_DelayedConsequences>();

        public float Defer(float fallInjury, bool ignoreInvincibility = false, bool ignoreSkeleton = false)
        {
            Configure();
            return _q.Add(fallInjury, Now, ignoreInvincibility, ignoreSkeleton);
        }

        /// <summary>All the Injury a fall will cost once its deferred share lands (what Phoenix Clause judges).</summary>
        public float TotalOf(float fallInjury)
        {
            Configure();
            return _q.TotalOf(fallInjury);
        }

        public void Preview(float fallInjury, out float immediate, out float deferred)
        {
            Configure();
            float share = fallInjury * _q.DeferShare;
            immediate = fallInjury - share;
            deferred = share * _q.Keep;
        }

        private void Configure()
        {
            _q.DeferShare = P("defer", 0.75f);
            _q.Delay = P("delay", 20f);
            _q.Keep = P("keep", 0.8f);
        }

        public override void Tick(Character me, float dt)
        {
            // Being a ghost defers collection, not the debt itself. Unconscious scouts still pay on time.
            _q.CollectDue(Now, canApply: !me.data.dead, (due, ignoreInvincibility, ignoreSkeleton) =>
                CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Injury, due, ignoreInvincibility, ignoreSkeleton));
        }

        /// <summary>
        /// Sacrificed (Fine Print) or otherwise removed mid-run: the debt is still owed, so it lands now instead of being
        /// forgiven. At run end (LocalCards.Resetting) it is simply dropped.
        /// </summary>
        public override void OnRemoved(Character me)
        {
            if (!LocalCards.Resetting && me != null)
                _q.CollectDue(float.MaxValue, canApply: !me.data.dead, (due, ignoreInvincibility, ignoreSkeleton) =>
                    CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Injury, due, ignoreInvincibility, ignoreSkeleton));
            _q.Clear();
        }

        public override AbilityStatus Status(Character me)
        {
            if (_q.Pending == 0) return AbilityStatus.None;
            int next = Mathf.CeilToInt(Mathf.Max(0f, _q.NextDue - Now));
            return AbilityStatus.Wait($"{Card.Name}: {Points(_q.PendingAmount)} Injury from {_q.Pending} {(_q.Pending == 1 ? "fall" : "falls")} still to land, next in {next} s");
        }
    }

    /// <summary>Safety Net: once per biome, press the key to place an anchor on safe ground; going down revives you there with 40 Injury.</summary>
    public sealed class Ability_SafetyNet : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: true);
        private bool _hasAnchor;
        private Vector3 _anchor;
        public override bool IsManual => true;
        public override void OnRemoved(Character me) => _hasAnchor = false;

        public override bool Activate(Character me)
        {
            if (_hasAnchor) { Ui.Hud.Toast(Card.Name + ": anchor already placed"); return false; }
            if (!_charge.Available(LocalCards.Biome)) { Ui.Hud.Toast(AlreadyUsedThisBiome); return false; }
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
            if (me != null && (me.data.passedOut || me.data.dead)) Revival.ReviveAt(me, pos, S.DrawbacksEnabled ? P("injury", 0.4f) : 0f, -1f);
            Ui.Hud.Toast(Card.Name.ToUpperInvariant() + "! You woke up at your anchor.");
        }

        public override void OnBiomeStarted(Character me, int biome)
        {
            if (_hasAnchor) Ui.Hud.Toast(Card.Name + ": your unused anchor expired");
            _hasAnchor = false;
        }

        public override AbilityStatus Status(Character me)
        {
            string key = Plugin.SafetyNetKeyLabel;
            if (_hasAnchor) return AbilityStatus.Wait(Line(key, "revive anchor placed"));
            return BiomeCharge(_charge.Available(LocalCards.Biome), key, "place anchor on flat ground");
        }
    }

    /// <summary>
    /// The Eternal Campfire: team aura (modifiers in cards.json) plus one revive per biome per owner for the first scout
    /// to DIE inside it (a recoverable pass-out never spends the charge). The host arbitrates so overlapping auras never
    /// revive twice.
    /// </summary>
    public sealed class Ability_EternalCampfire : Ability
    {
        public override bool TryRevive(Character me, int incapEvent)
        {
            NetSend.RaiseToMaster(new ReviveRequest { ownerActor = NetSend.LocalActor, biome = LocalCards.Biome, incapEvent = incapEvent });
            return true;
        }

        /// <summary>The local scout died near a teammate who owns The Eternal Campfire.</summary>
        public static bool RequestFromTeammate(Character me, int incapEvent)
        {
            var s = CardSettings.Current;
            // this now runs AFTER RPCA_Die warped the body to DeathPos: measure from where the scout actually fell
            Vector3 myPos = me.data.dead ? me.LastLivingPosition : me.Center;
            int best = int.MinValue; float bestDist = float.MaxValue;
            foreach (var kv in Effects.CardsByActor)
            {
                if (kv.Key == NetSend.LocalActor) continue;
                foreach (var id in kv.Value)
                {
                    var def = s.Card(id);
                    if (def == null || def.Ability != "EternalCampfire") continue;
                    Character owner;
                    // a dead owner's aura is gone, except for the owner itself: its own death is the one it is always inside
                    if (!PlayerHandler.TryGetCharacter(kv.Key, out owner) || owner == null || (owner.data.dead && owner != me)) continue;
                    float dist = Vector3.Distance(owner.Center, myPos);
                    // +2 matches the host's own re-check band, so a victim the host WOULD grant also sends the request
                    if (dist <= s.Param(def, "radius", 15f) + 2f && dist < bestDist) { bestDist = dist; best = kv.Key; }
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
            if (owner == null || victim == null || (owner.data.dead && m.ownerActor != sender)) return;
            float radius = 15f;
            foreach (var id in Effects.CardsOf(m.ownerActor)) { var d = s.Card(id); if (d != null && d.Ability == "EternalCampfire") radius = s.Param(d, "radius", 15f); }
            // a dead victim's body was warped to DeathPos: re-check the radius against where it actually fell
            Vector3 victimPos = victim.data.dead ? victim.LastLivingPosition : victim.Center;
            if (sender != m.ownerActor && Vector3.Distance(owner.Center, victimPos) > radius + 2f) return;
            // the host's own biome counts, never the one in the request (a client cannot refill the charge by naming another)
            int grantedOwner = m.ownerActor;
            if (!Arbiter.TryGrant(m.ownerActor, LocalCards.Biome, sender, m.incapEvent))
            {
                // the named owner's charge is spent: only the host knows that, so fall back to any other living
                // owner whose unspent aura covers the victim before giving up. TryGrant's per-event key makes a
                // double grant for the same incapacitation impossible.
                grantedOwner = int.MinValue;
                foreach (var kv in Effects.CardsByActor)
                {
                    if (kv.Key == m.ownerActor) continue;
                    foreach (var id in kv.Value)
                    {
                        var d2 = s.Card(id);
                        if (d2 == null || d2.Ability != "EternalCampfire") continue;
                        Character other;
                        if (!PlayerHandler.TryGetCharacter(kv.Key, out other) || other == null || (other.data.dead && kv.Key != sender)) continue;
                        if (kv.Key != sender && Vector3.Distance(other.Center, victimPos) > s.Param(d2, "radius", 15f) + 2f) continue;
                        if (Arbiter.TryGrant(kv.Key, LocalCards.Biome, sender, m.incapEvent)) { grantedOwner = kv.Key; break; }
                    }
                    if (grantedOwner != int.MinValue) break;
                }
                if (grantedOwner == int.MinValue)
                {
                    Plugin.Verbose($"Eternal Campfire: refused revive of {sender} (owner {m.ownerActor}, event {m.incapEvent}, biome {LocalCards.Biome})");
                    return;
                }
            }
            NetSend.RaiseTo(new ReviveGranted { ownerActor = grantedOwner, incapEvent = m.incapEvent }, sender);
        }

        /// <summary>
        /// Victim side: the host approved; revive at a safe spot nearby. A still-settling pass-out waits 1.5 s like
        /// Budget Checkpoint Flag; an already-dead scout has nothing left to settle, and waiting would widen the
        /// end-of-run race, so it revives on the next frame.
        /// </summary>
        public static void OnGranted(Character me, ReviveGranted m)
        {
            if (me == null || !(me.data.passedOut || me.data.dead)) return;
            Plugin.Run(ReviveSoon(me));
        }

        private static System.Collections.IEnumerator ReviveSoon(Character me)
        {
            if (me != null && !me.data.dead) yield return new WaitForSeconds(1.5f);
            else yield return null;
            if (me == null || !(me.data.passedOut || me.data.dead)) yield break;
            var s = CardSettings.Current;
            var def = System.Linq.Enumerable.FirstOrDefault(Generated.CardTable.Cards, c => c.Ability == "EternalCampfire");
            Vector3 origin = me.data.dead ? me.LastLivingPosition : me.Center;
            Vector3 pos;
            if (!SafeSpot.Near(origin, 0f, 6f, out pos) && !LocalCards.RecentFoothold(0f, out pos)) { Ui.Hud.Toast((def != null ? def.Name : "The Eternal Campfire") + ": no safe ground nearby, so it could not revive you"); yield break; }
            Revival.ReviveAt(me, pos, def != null ? s.Param(def, "injury", 0.4f) : 0.4f, def != null ? s.Param(def, "stamina", 0.5f) : 0.5f);
            Ui.Hud.Toast((def != null ? def.Name : "The Eternal Campfire").ToUpperInvariant() + " revived you");
        }
    }

    /// <summary>Shared Burden: teammates near the owner take less Injury; the owner absorbs half of what was prevented (capped).</summary>
    public sealed class Ability_SharedBurden : Ability
    {
        /// <summary>Owner side: Injury sent by a protected teammate. Applied raw, so it is never shared or scaled again.</summary>
        public static void Absorb(Character me, float amount)
        {
            if (me == null || me.data.dead || amount <= 0f) return;
            CardStatus.Penalty(me, CharacterAfflictions.STATUSTYPE.Injury, amount);
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
            float bonus = a.P("bonus", 1f);
            me.AddExtraStamina(bonus);
            NetSend.RaiseTo(new BonusStamina { amount = bonus, reason = 1 }, victimActor);
            Ui.Hud.Toast($"{a.Card.Name.ToUpperInvariant()}! You and {NetSend.NameOf(victimActor)} get +{Points(bonus)} extra stamina");
        }

        public override AbilityStatus Status(Character me) => BiomeCharge(_charge.Available(LocalCards.Biome));
    }
}
