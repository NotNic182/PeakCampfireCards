using System;

namespace CampfireCards.Core
{
    /// <summary>
    /// Where the host's card deals get their randomness: one generator per game launch, seeded from fresh entropy, that
    /// every offer, bonus deal and timeout pick draws from in turn. (Seeding a new System.Random from the session number
    /// replayed the very same offers after every launch, and nearby seeds gave look-alike hands.) Clients never deal:
    /// they receive the host's hands, so nothing here has to match between machines. Unity-free.
    /// </summary>
    public static class DealRandom
    {
        public static readonly Random Shared = Create();

        public static Random Create() => new Random(Guid.NewGuid().GetHashCode());
    }
}
