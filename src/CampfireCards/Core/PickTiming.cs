using System;

namespace CampfireCards.Core
{
    /// <summary>Pick timeout rules shared by the host and picker. Zero means the player chooses in their own time.</summary>
    public static class PickTiming
    {
        public const float DefaultSeconds = 60f;

        public static float Normalize(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return DefaultSeconds;
            return seconds <= 0f ? 0f : Math.Max(5f, seconds);
        }

        public static bool HasExpired(float seconds, float elapsed) => seconds > 0f && elapsed >= seconds;

        /// <summary>Only the old shipped default is migrated; custom durations and an untimed pick are preserved.</summary>
        public static float UpgradeLegacyDefault(float seconds, int settingsVersion) =>
            settingsVersion < 1 && seconds == 30f ? DefaultSeconds : seconds;
    }
}
