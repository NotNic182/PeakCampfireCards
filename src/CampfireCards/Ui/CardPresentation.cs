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
    }
}
