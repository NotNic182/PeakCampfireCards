using CampfireCards.Cards;
using CampfireCards.Core;
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
                float bonus = P("bonus", 0.25f);
                me.AddExtraStamina(bonus);
                Ui.Hud.Toast($"{Card.Name}: lucky roll, +{Points(bonus)} extra stamina");
            }
            else
            {
                float injury = P("injury", 0.2f);
                CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Injury, injury);
                Ui.Hud.Toast($"{Card.Name}: unlucky roll, +{Points(injury)} Injury");
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
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Injury, injury);
            Ui.Hud.Toast($"{Card.Name.ToUpperInvariant()}! +{Points(bonus)} extra stamina, +{Points(injury)} Injury");
            return true;
        }

        public override AbilityStatus Status(Character me) => BiomeCharge(_charge.Available(LocalCards.Biome), Plugin.BloodBatteryKeyLabel);
    }
}
