using System;
using System.Globalization;

namespace CampfireCards.Core
{
    /// <summary>Shared display formatting; no card mechanics or settings are changed.</summary>
    public static class CardCopy
    {
        public static string WithKey(string text, string key) =>
            (text ?? "").Replace("{key}", string.IsNullOrEmpty(key) || key == "None" ? "[unbound]" : key);

        public static string Points(float amount) => (amount * 100f).ToString("0.#", CultureInfo.InvariantCulture);

        public static string Countdown(float seconds)
        {
            int whole = (int)Math.Ceiling(Math.Max(0, seconds));
            return (whole / 60).ToString(CultureInfo.InvariantCulture) + ":" + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
