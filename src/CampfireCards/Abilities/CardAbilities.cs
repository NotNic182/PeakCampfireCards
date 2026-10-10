using System.Linq;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Net;
using CampfireCards.Ui;

namespace CampfireCards.Abilities
{
    /// <summary>
    /// Double Down: the host deals 1 bonus Common-to-Rare card (never a card-draw card, see PickSession); here: +10 Curse now.
    /// The one-card next offer is a pick flag set for everyone when the card is added (Effects.AddCard).
    /// </summary>
    public sealed class Ability_DoubleDown : Ability
    {
        public override void OnAcquired(Character me)
        {
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Curse, P("curse", 0.1f));
            Hud.Toast("Double Down: only 1 card to choose at the next campfire");
        }
    }

    /// <summary>
    /// Mountain's Bargain: on acquisition the owner picks one non-Mythic card to give up (real card-choice UI);
    /// the +35 max stamina is a modifier on the card; +5 Curse at every later campfire.
    /// </summary>
    public sealed class Ability_MountainsBargain : Ability
    {
        public override void OnAcquired(Character me)
        {
            var targets = CardDraw.SacrificeTargets(S, Effects.CardsOf(NetSend.LocalActor)).ToArray();
            if (targets.Length == 0) { Hud.Toast("Mountain's Bargain: nothing to sacrifice"); return; }
            CardPicker.Ensure().ShowSacrifice(targets, id =>
            {
                NetSend.Raise(new CardSacrificed { actor = NetSend.LocalActor, cardId = id }, Photon.Realtime.ReceiverGroup.All);
            });
        }

        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (acquiredThisFire || me.data.dead) return;
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Curse, P("curseFire", 0.05f));
        }
    }
}
