using CampfireCards.Cards;

namespace CampfireCards.Abilities
{
    /// <summary>
    /// Clown Luggage Special: the host deals two bonus cards on acquisition (PickSession reads the bonusCards param).
    /// The 0.8.0 rebalance removed its permanent random penalty; the penalty plumbing (Effects.Penalties, CardPenalty)
    /// stays for compatibility and simply never rolls for cards that no longer use it.
    /// </summary>
    public sealed class Ability_DoubleDeal : Ability
    {
    }
}
