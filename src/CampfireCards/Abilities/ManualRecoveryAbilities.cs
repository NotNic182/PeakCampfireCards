using CampfireCards.Cards;
using CampfireCards.Core;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>Second Wind: manual recovery below the threshold, with a five-minute cooldown.</summary>
    public sealed class Ability_SecondWind : Ability
    {
        private readonly Cooldown _cooldown = new Cooldown(300f);
        public override bool IsManual => true;

        public override bool Activate(Character me)
        {
            if (me.data.passedOut || me.data.fullyPassedOut || me.data.dead) return false;
            if (!_cooldown.Ready(Now)) { Ui.Hud.Toast(Card.Name + ": cooldown " + CardCopy.Countdown(_cooldown.Remaining(Now))); return false; }
            if (me.data.currentStamina >= P("threshold", 0.2f)) { Ui.Hud.Toast(Card.Name + ": needs stamina below " + CardCopy.Points(P("threshold", .2f))); return false; }
            if (me.GetMaxStamina() <= me.data.currentStamina) { Ui.Hud.Toast(Card.Name + ": statuses leave no room to recover"); return false; }
            float before = me.data.currentStamina;
            me.AddStamina(P("restore", 0.25f));   // AddStamina clamps to the current maximum
            _cooldown.Seconds = Mathf.Max(0f, P("cooldown", 300f));
            _cooldown.Trigger(Now);
            Ui.Hud.Toast(Card.Name + ": recovered " + CardCopy.Points(me.data.currentStamina - before) + " stamina");
            return true;
        }

        public override string Status(Character me)
        {
            string prefix = $"{Card.Name} [{Plugin.SecondWindKeyLabel}]: ";
            if (me.data.dead || me.data.passedOut || me.data.fullyPassedOut) return prefix + "unavailable while down";
            if (!_cooldown.Ready(Now)) return prefix + "cooldown " + CardCopy.Countdown(_cooldown.Remaining(Now));
            if (me.data.currentStamina >= P("threshold", .2f)) return prefix + "needs stamina below " + CardCopy.Points(P("threshold", .2f));
            if (me.GetMaxStamina() <= me.data.currentStamina) return prefix + "no room to recover";
            return prefix + "ready";
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
            if (!_charge.Available(LocalCards.Biome)) { Ui.Hud.Toast(Card.Name + ": already used this biome"); return false; }
            float threshold = P("threshold", 0.35f);
            var chosen = Worst(me, out float worst);
            if (worst < threshold - 0.0001f) { Ui.Hud.Toast(Card.Name + ": " + Requirement()); return false; }
            _charge.TryUse(LocalCards.Biome);
            float before = CardStatus.Get(me, chosen);
            CardStatus.Remove(me, chosen, P("remove", 0.2f));
            Ui.Hud.Toast(Card.Name + ": removed " + CardCopy.Points(before - CardStatus.Get(me, chosen)) + " " + chosen);
            return true;
        }

        public override string Status(Character me)
        {
            string prefix = $"{Card.Name} [{Plugin.EmergencyPurgeKeyLabel}]: ";
            if (me.data.dead || me.data.passedOut || me.data.fullyPassedOut) return prefix + "unavailable while down";
            if (!_charge.Available(LocalCards.Biome)) return prefix + "used this biome";
            Worst(me, out float worst);
            return prefix + (worst < P("threshold", .35f) - .0001f ? Requirement() : "ready");
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
