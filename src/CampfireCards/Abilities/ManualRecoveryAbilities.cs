using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>Second Wind: manual stamina recovery on a five-minute cooldown.</summary>
    public sealed class Ability_SecondWind : Ability
    {
        private readonly Cooldown _cooldown = new Cooldown(300f);
        public override bool IsManual => true;

        public override bool Activate(Character me)
        {
            if (me.data.passedOut || me.data.fullyPassedOut || me.data.dead) return false;
            if (!_cooldown.Ready(Now)) { Ui.Hud.Toast(Card.Name + ": cooldown " + CardCopy.Countdown(_cooldown.Remaining(Now))); return false; }
            if (me.GetMaxStamina() <= me.data.currentStamina) { Ui.Hud.Toast(Card.Name + ": statuses leave no room to recover"); return false; }
            float before = me.data.currentStamina;
            me.AddStamina(P("restore", 0.25f));   // AddStamina clamps to the current maximum
            _cooldown.Seconds = Mathf.Max(0f, P("cooldown", 300f));
            _cooldown.Trigger(Now);
            Ui.Hud.Toast(Card.Name + ": recovered " + CardCopy.Points(me.data.currentStamina - before) + " stamina");
            return true;
        }

        public override AbilityStatus Status(Character me)
        {
            string key = Plugin.SecondWindKeyLabel;
            if (me.data.dead || me.data.passedOut || me.data.fullyPassedOut) return AbilityStatus.Wait(Line(key, "unavailable while down"));
            if (!_cooldown.Ready(Now)) return AbilityStatus.Wait(Line(key, "cooldown " + CardCopy.Countdown(_cooldown.Remaining(Now))));
            if (me.GetMaxStamina() <= me.data.currentStamina) return AbilityStatus.Wait(Line(key, "no room to recover"));
            return AbilityStatus.Ok(Line(key, Ui_AbilityBar.Ready));
        }
    }

    /// <summary>Emergency Purge: manually clear 20 from the worst eligible status, once per biome.</summary>
    public sealed class Ability_EmergencyPurge : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: true);
        private static readonly CharacterAfflictions.STATUSTYPE[] Candidates =
        {
            CharacterAfflictions.STATUSTYPE.Poison, CharacterAfflictions.STATUSTYPE.Spores, CharacterAfflictions.STATUSTYPE.Drowsy,
        };
        public override bool IsManual => true;

        public override bool Activate(Character me)
        {
            if (me.data.dead || me.data.passedOut || me.data.fullyPassedOut) return false;
            if (!_charge.Available(LocalCards.Biome)) { Ui.Hud.Toast(AlreadyUsedThisBiome); return false; }
            float threshold = P("threshold", 0.35f);
            var chosen = Worst(me, out float worst);
            if (worst < threshold - 0.0001f) { Ui.Hud.Toast(Card.Name + ": " + Requirement()); return false; }
            _charge.TryUse(LocalCards.Biome);
            float before = CardStatus.Get(me, chosen);
            CardStatus.Remove(me, chosen, P("remove", 0.2f));
            Ui.Hud.Toast(Card.Name + ": removed " + CardCopy.Points(before - CardStatus.Get(me, chosen)) + " " + StatusName(chosen));
            return true;
        }

        public override AbilityStatus Status(Character me)
        {
            string key = Plugin.EmergencyPurgeKeyLabel;
            if (me.data.dead || me.data.passedOut || me.data.fullyPassedOut) return AbilityStatus.Wait(Line(key, "unavailable while down"));
            if (!_charge.Available(LocalCards.Biome)) return AbilityStatus.Wait(Line(key, Ui_AbilityBar.UsedThisBiome));
            Worst(me, out float worst);
            return worst < P("threshold", .35f) - .0001f ? AbilityStatus.Wait(Line(key, Requirement())) : AbilityStatus.Ok(Line(key, Ui_AbilityBar.Ready));
        }

        private string Requirement() => "needs " + CardCopy.Points(P("threshold", .35f)) + "+ Poison, Spores or Drowsy";

        private static CharacterAfflictions.STATUSTYPE Worst(Character me, out float amount)
        {
            amount = 0f;
            var chosen = Candidates[0];
            foreach (var type in Candidates)
            {
                float value = CardStatus.Get(me, type);
                if (value > amount) { amount = value; chosen = type; }
            }
            return chosen;
        }
    }

}
