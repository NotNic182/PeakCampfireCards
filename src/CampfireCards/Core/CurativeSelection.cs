using System;
using System.Collections.Generic;

namespace CampfireCards.Core
{
    /// <summary>Tracks all channels treated by a medical item and selects an eligible, untreated bonus cleanse.</summary>
    public static class CurativeSelection
    {
        /// <summary>Vanilla medical Poison subtraction also treats Spores. Ordinary food is not a medical use.</summary>
        public static bool RecordMedicalCure<T>(ISet<T> cured, T status, T poison, T spores, bool isMedical)
        {
            if (!isMedical) return false;
            cured.Add(status);
            if (EqualityComparer<T>.Default.Equals(status, poison)) cured.Add(spores);
            return true;
        }

        /// <summary>Candidate order determines ties; the caller supplies the existing allowed-status whitelist.</summary>
        public static bool TryHighestOther<T>(IEnumerable<T> candidates, ISet<T> cured, Func<T, float> currentStatus, out T pick)
        {
            float worst = 0f;
            pick = default(T);
            foreach (var status in candidates)
            {
                if (cured != null && cured.Contains(status)) continue;
                float amount = currentStatus(status);
                if (amount > worst + 0.0001f) { worst = amount; pick = status; }
            }
            return worst > 0f;
        }
    }
}
