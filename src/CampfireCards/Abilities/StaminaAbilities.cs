using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>Half Tank: bonus stamina whenever a new campfire is lit (on top of the vanilla campfire boost).</summary>
    public sealed class Ability_CampfireBonus : Ability
    {
        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (me.data.dead) return;
            me.AddExtraStamina(P("bonus", 0.15f));
        }
    }

    /// <summary>
    /// Cursed Crown: bonus stamina and Curse now, smaller amounts at every later campfire — but the Curse stops once the
    /// card has charged its lifetime budget. The budget counts what was charged, so cleansing cannot reopen it; the
    /// counter lives in this instance, which survives removal and reacquisition on the same PC (LocalCards._retired).
    /// </summary>
    public sealed class Ability_CursedCrown : Ability
    {
        private float _charged;

        public override void OnAcquired(Character me)
        {
            me.AddExtraStamina(P("bonusNow", 0.3f));
            Charge(me, P("curseNow", 0.05f));
        }

        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (acquiredThisFire || me.data.dead) return;
            me.AddExtraStamina(P("bonusFire", 0.2f));
            Charge(me, P("curseFire", 0.01f));
        }

        private void Charge(Character me, float amount)
        {
            if (!S.DrawbacksEnabled) return;   // nothing charged, nothing counted
            float room = P("curseBudget", 0.1f) - _charged;
            amount = UnityEngine.Mathf.Min(amount, room);
            if (amount <= 0f) return;
            _charged += amount;
            CardStatus.Penalty(me, CharacterAfflictions.STATUSTYPE.Curse, amount);
        }

        public override AbilityStatus Status(Character me)
        {
            if (!S.DrawbacksEnabled) return AbilityStatus.None;
            float budget = P("curseBudget", 0.1f);
            return _charged >= budget - 0.0001f
                ? AbilityStatus.Ok(Line(null, "Curse budget spent: later fires are free"))
                : AbilityStatus.Wait(Line(null, $"Curse charged so far: {Points(_charged)} of {Points(budget)}"));
        }
    }

    /// <summary>Meditation: crouch and stay still for a few seconds to gain yellow stamina, once per biome.</summary>
    public sealed class Ability_Meditation : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: true);
        private float _held;

        public override void Tick(Character me, float dt)
        {
            var d = me.data;
            // the same crouch-and-still reading the card conditions use (Effects.Condition crouchStill)
            bool still = !d.dead && !d.passedOut && d.isCrouching && me.input.movementInput.sqrMagnitude < 0.01f;
            if (!still || !_charge.Available(LocalCards.Biome)) { _held = 0f; return; }
            _held += dt;
            float hold = P("hold", 3f);
            if (_held < hold) return;
            // the game clamps yellow stamina to a full bar: a grant that would vanish keeps the charge (and stays
            // primed, so it fires the moment there is room again)
            float before = d.extraStamina;
            me.AddExtraStamina(P("bonus", 0.25f));
            float gained = d.extraStamina - before;
            if (gained <= 0.0001f) { _held = hold; return; }
            _held = 0f;
            _charge.TryUse(LocalCards.Biome);
            Ui.Hud.Toast($"{Card.Name}: +{Points(gained)} extra stamina");
        }

        public override AbilityStatus Status(Character me) =>
            BiomeCharge(_charge.Available(LocalCards.Biome), ready: Ui_AbilityBar.Ready + " (crouch and stay still)");
    }

    /// <summary>Loaded Dice: one roll at the start of each biome (60% bonus stamina, else Injury). The weight cut is a modifier.</summary>
    public sealed class Ability_LoadedDice : Ability
    {
        private int _rolledBiome = int.MinValue;

        /// <summary>Picked mid-biome: that biome still gets its roll.</summary>
        public override void OnAcquired(Character me)
        {
            if (LocalCards.Biome >= 0) OnBiomeStarted(me, LocalCards.Biome);
        }

        /// <summary>A scout who was a ghost when the biome started rolls once revived, instead of losing that biome's roll.</summary>
        public override void Tick(Character me, float dt)
        {
            if (LocalCards.Biome >= 0 && LocalCards.Biome != _rolledBiome && !me.data.dead) OnBiomeStarted(me, LocalCards.Biome);
        }

        public override void OnBiomeStarted(Character me, int biome)
        {
            if (biome == _rolledBiome || me.data.dead) return;
            _rolledBiome = biome;
            bool win = Rolls.LoadedDice(LocalCards.RunSeed, biome, NetSend.LocalActor, P("chance", 0.65f));
            if (win)
            {
                float bonus = P("bonus", 0.25f);
                me.AddExtraStamina(bonus);
                Ui.Hud.Toast($"{Card.Name}: lucky roll, +{Points(bonus)} extra stamina");
            }
            else
            {
                float injury = P("injury", 0.2f);
                CardStatus.Penalty(me, CharacterAfflictions.STATUSTYPE.Injury, injury);
                Ui.Hud.Toast(S.DrawbacksEnabled ? $"{Card.Name}: unlucky roll, +{Points(injury)} Injury" : $"{Card.Name}: unlucky roll, no bonus this biome");
            }
        }
    }

    /// <summary>Blood Battery: press the ability key for bonus stamina at the cost of Injury, once per biome.</summary>
    public sealed class Ability_BloodBattery : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: true);
        public override bool IsManual => true;

        public override bool Activate(Character me)
        {
            if (me.data.dead || me.data.passedOut) return false;
            if (!_charge.TryUse(LocalCards.Biome)) { Ui.Hud.Toast(AlreadyUsedThisBiome); return false; }
            float bonus = P("bonus", 0.6f), injury = P("injury", 0.15f);
            me.AddExtraStamina(bonus);
            CardStatus.Penalty(me, CharacterAfflictions.STATUSTYPE.Injury, injury);
            Ui.Hud.Toast($"{Card.Name.ToUpperInvariant()}! +{Points(bonus)} extra stamina" + (S.DrawbacksEnabled ? $", +{Points(injury)} Injury" : ""));
            return true;
        }

        public override AbilityStatus Status(Character me) => BiomeCharge(_charge.Available(LocalCards.Biome), Plugin.BloodBatteryKeyLabel);
    }
}
