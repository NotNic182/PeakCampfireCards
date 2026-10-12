using System;
using CampfireCards.Core;
using CampfireCards.Generated;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>Overhang Artist: regenerate stamina while gripping a wall (not a rope or vine) without moving.</summary>
    public sealed class Ability_OverhangRegen : Ability
    {
        public override void Tick(Character me, float dt)
        {
            var d = me.data;
            if (!d.isClimbing || d.isRopeClimbing || d.isVineClimbing) return;   // wall only
            if (me.input.movementInput.magnitude >= 0.1f) return;
            if (me.refs.climbing.playerSlide.sqrMagnitude > 0.01f) return;      // sliding is not hanging still
            me.AddStamina(P("regenPerSecond", 0.04f) * dt);
        }
    }

    /// <summary>
    /// Second Spring: one extra midair jump on a cooldown. The game consumes the grounded jump first, so a normal jump
    /// never uses it. Using it adds Drowsy and starts the cooldown; it cannot refresh itself.
    /// </summary>
    public sealed class Ability_SecondSpring : Ability
    {
        private Cooldown _cd;
        private bool _granted;
        public static Ability_SecondSpring Active;

        public override void OnAcquired(Character me) { Ensure(); }

        private void Ensure()
        {
            if (_cd == null) _cd = new Cooldown(P("cooldown", 35f));
            Active = this;
        }

        public override void Tick(Character me, float dt)
        {
            Ensure();
            var d = me.data;
            int baseExtra = me.refs.afflictions.totalExtraJumps;
            if (_cd.Ready(Now))
            {
                if (d.extraJumps < baseExtra + 1) d.extraJumps = baseExtra + 1;
                _granted = true;
            }
        }

        /// <summary>Called when the local scout used an extra (midair) jump.</summary>
        public void OnExtraJumpUsed(Character me)
        {
            if (!_granted) return;
            int baseExtra = me.refs.afflictions.totalExtraJumps;
            if (me.data.extraJumps >= baseExtra + 1) return;   // an amulet jump was used, ours is still there
            _granted = false;
            _cd.Trigger(Now);
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Drowsy, P("drowsy", 0.075f));
        }

        public override void OnRemoved(Character me)
        {
            if (_granted && me != null) me.data.extraJumps = Math.Min(me.data.extraJumps, me.refs.afflictions.totalExtraJumps);
            _granted = false;
            if (Active == this) Active = null;
        }

        public override AbilityStatus Status(Character me)
        {
            Ensure();
            return _cd.Ready(Now) ? AbilityStatus.Ok(Card.Name + " (double jump): " + Ui_AbilityBar.Ready)
                                  : AbilityStatus.Wait(Line(null, "cooldown " + CardCopy.Countdown(_cd.Remaining(Now))));
        }
    }

    /// <summary>Pulse Climber: 3 s of continuous climbing turns on a 1 s climbing-cost cut, then the buildup starts again.</summary>
    public sealed class Ability_PulseClimber : Ability
    {
        private readonly PulseMachine _m = new PulseMachine();

        public override void Tick(Character me, float dt)
        {
            _m.Buildup = P("buildup", 3f);
            _m.Boost = P("boost", 1f);
            _m.Update(dt, me.data.isClimbingAnything);
        }

        public override bool ConditionFlag(Condition c) => c == Condition.pulseActive && _m.Active;
        public override void OnRemoved(Character me) => _m.Reset();
    }

    /// <summary>Momentum Fiend: sprint 2 s to boost sprint speed until you stop; stopping after the boost adds Drowsy (30 s cooldown).</summary>
    public sealed class Ability_MomentumFiend : Ability
    {
        private readonly MomentumMachine _m = new MomentumMachine();

        public override void Tick(Character me, float dt)
        {
            _m.Buildup = P("buildup", 2f);
            _m.PenaltyCooldown = P("cooldown", 30f);
            bool sprinting = me.data.isSprinting && me.input.movementInput.y > 0.01f;
            if (_m.Update(dt, sprinting, Now))
                CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Drowsy, P("drowsy", 0.075f));
        }

        public override bool ConditionFlag(Condition c) => c == Condition.momentumActive && _m.Active;
        public override void OnRemoved(Character me) => _m.ClearMotion();

        public override AbilityStatus Status(Character me) => _m.Active ? AbilityStatus.Ok(Line(null, "sprint boost active")) : AbilityStatus.None;
    }
}
