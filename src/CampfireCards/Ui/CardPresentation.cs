using CampfireCards.Core;
using CampfireCards.Generated;

namespace CampfireCards.Ui
{
    internal static class CardPresentation
    {
        public static string Text(CardDef card, string template)
        {
            string key;
            switch (card.Ability)
            {
                case "SecondWind": key = Plugin.SecondWindKeyLabel; break;
                case "EmergencyPurge": key = Plugin.EmergencyPurgeKeyLabel; break;
                case "BloodBattery": key = Plugin.BloodBatteryKeyLabel; break;
                case "SafetyNet": key = Plugin.SafetyNetKeyLabel; break;
                default: key = "?"; break;
            }
            return CardCopy.WithKey(template, key);
        }

        /// <summary>
        /// The benefit as it plays on this difficulty: its numbers are the scaled ones (Core.DifficultyScale). note is a
        /// short "Benefits at N% on tier." line when the card's benefit is scaled but its text has no number to rewrite.
        /// </summary>
        public static string Benefit(CardDef card, out string note)
        {
            note = null;
            var s = CardSettings.Current;
            string text = DifficultyScale.Benefit(card, s, card.Positive, out bool complete);
            if (!complete)
                note = string.Format(Ui_CardEffects.StrengthNote, (s.BenefitStrength * 100f).ToString("0"), TierName());
            return Text(card, text);
        }

        /// <summary>The current difficulty tier's name ("Tenderfoot–Peak", "Ascent 1–3"...).</summary>
        public static string TierName() => DifficultyScale.TierNames[DifficultyScale.TierOf(Ascents.currentAscent)];
    }
}
