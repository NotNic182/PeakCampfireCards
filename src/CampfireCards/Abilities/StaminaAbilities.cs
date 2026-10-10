using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Net;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>Second Wind: once per biome, when usable stamina drops below 20, restore 25 (up to the current ceiling).</summary>
    public sealed class Ability_SecondWind : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: true);
        private readonly BelowLatch _latch = new BelowLatch();

        public override void Tick(Character me, float dt)
        {
            if (me.data.passedOut || me.data.dead) return;
            if (!_latch.Update(me.data.currentStamina, P("threshold", 0.2f))) return;
            if (!_charge.TryUse(LocalCards.Biome)) return;
            me.AddStamina(P("restore", 0.25f));   // AddStamina clamps to the current maximum
            Ui.Hud.Toast(Card.Name.ToUpperInvariant() + "!");
        }

        public override string Status(Character me) => _charge.Available(LocalCards.Biome) ? Card.Name + ": ready" : Card.Name + ": used this biome";
    }

    /// <summary>Half Tank: bonus stamina whenever a new campfire is lit (on top of the vanilla campfire boost).</summary>
    public sealed class Ability_CampfireBonus : Ability
    {
        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (me.data.dead) return;
            me.AddExtraStamina(P("bonus", 0.15f));
        }
    }

    /// <summary>Cursed Crown: bonus stamina and Curse now, and smaller amounts at every later campfire.</summary>
    public sealed class Ability_CursedCrown : Ability
    {
        public override void OnAcquired(Character me)
        {
            me.AddExtraStamina(P("bonusNow", 0.3f));
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Curse, P("curseNow", 0.1f));
        }

        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (acquiredThisFire || me.data.dead) return;
            me.AddExtraStamina(P("bonusFire", 0.2f));
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Curse, P("curseFire", 0.05f));
        }
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

        public override void OnBiomeStarted(Character me, int biome)
        {
            if (biome == _rolledBiome || me.data.dead) return;
            _rolledBiome = biome;
            bool win = Rolls.LoadedDice(LocalCards.RunSeed, biome, NetSend.LocalActor, P("chance", 0.65f));
            if (win)
            {
                me.AddExtraStamina(P("bonus", 0.25f));
                Ui.Hud.Toast(Card.Name + ": lucky roll, extra stamina");
            }
            else
            {
                CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Injury, P("injury", 0.2f));
                Ui.Hud.Toast(Card.Name + ": bad roll, Injury");
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
            if (!_charge.TryUse(LocalCards.Biome)) { Ui.Hud.Toast(Card.Name + ": already used this biome"); return false; }
            me.AddExtraStamina(P("bonus", 0.6f));
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Injury, P("injury", 0.25f));
            Ui.Hud.Toast(Card.Name + "!");
            return true;
        }

        public override string Status(Character me) =>
            $"{Card.Name} [{Plugin.BloodBatteryKeyLabel}]: " + (_charge.Available(LocalCards.Biome) ? "ready" : "used this biome");
    }
}
