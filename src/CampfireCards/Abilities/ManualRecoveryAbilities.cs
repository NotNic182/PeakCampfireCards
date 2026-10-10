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
            if (!_cooldown.Ready(Now)) { Ui.Hud.Toast(Card.Name + ": still cooling down"); return false; }
            if (me.data.currentStamina >= P("threshold", 0.2f)) { Ui.Hud.Toast(Card.Name + ": stamina must be below 20"); return false; }
            if (me.GetMaxStamina() <= me.data.currentStamina) { Ui.Hud.Toast(Card.Name + ": statuses leave no room to recover"); return false; }
            me.AddStamina(P("restore", 0.25f));   // AddStamina clamps to the current maximum
            _cooldown.Seconds = Mathf.Max(0f, P("cooldown", 300f));
            _cooldown.Trigger(Now);
            Ui.Hud.Toast(Card.Name.ToUpperInvariant() + "!");
            return true;
        }

        public override string Status(Character me) => $"{Card.Name} [{Plugin.SecondWindKeyLabel}]: " +
            (_cooldown.Ready(Now) ? (me.data.currentStamina < P("threshold", .2f) ? "ready" : "needs stamina below 20") : $"{Mathf.CeilToInt(_cooldown.Remaining(Now))}s");
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
            float worst = 0f;
            var chosen = Candidates[0];
            foreach (var t in Candidates)
            {
                float value = CardStatus.Get(me, t);
                if (value > worst) { worst = value; chosen = t; }
            }
            if (worst < threshold - 0.0001f) { Ui.Hud.Toast(Card.Name + ": needs 35+ Poison, Spores or Drowsy"); return false; }
            _charge.TryUse(LocalCards.Biome);
            CardStatus.Remove(me, chosen, P("remove", 0.2f));
            Ui.Hud.Toast(Card.Name + ": " + chosen + " cleansed");
            return true;
        }

        public override string Status(Character me) => $"{Card.Name} [{Plugin.EmergencyPurgeKeyLabel}]: " + (_charge.Available(LocalCards.Biome) ? "ready at 35+ status" : "used this biome");
    }

}
