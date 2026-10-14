using System.Linq;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Net;
using CampfireCards.Ui;

namespace CampfireCards.Abilities
{
    /// <summary>
    /// Double Down: the host deals the bonus Common-to-Rare cards (never a card-draw card, see PickSession); here: the
    /// Curse now. The reduced next offer (nextOffer param) is a pick flag set for everyone when the card is added
    /// (Effects.AddCard); PickSession deals min(offer size, nextOffer).
    /// </summary>
    public sealed class Ability_DoubleDown : Ability
    {
        public override void OnAcquired(Character me)
        {
            CardStatus.Penalty(me, CharacterAfflictions.STATUSTYPE.Curse, P("curse", 0.05f));
            if (!S.DrawbacksEnabled) return;
            int offer = System.Math.Max(1, (int)System.Math.Round(P("nextOffer", 2f)));
            Hud.Toast($"{Card.Name}: only {offer} {(offer == 1 ? "card" : "cards")} to choose from at the next campfire");
        }
    }

    /// <summary>
    /// Mountain's Bargain: on acquisition the owner picks one non-Mythic card to give up (real card-choice UI);
    /// the +35 max stamina is a modifier on the card; the 5 Curse is charged once, when the sacrifice is committed.
    /// </summary>
    public sealed class Ability_MountainsBargain : Ability
    {
        public override void OnAcquired(Character me)
        {
            var targets = CardDraw.SacrificeTargets(S, Effects.CardsOf(NetSend.LocalActor)).ToArray();
            if (targets.Length == 0) { Hud.Toast(Card.Name + ": you have no card to give up"); return; }
            CardPicker.Ensure().ShowSacrifice(targets, id =>
            {
                NetSend.Raise(new CardSacrificed { actor = NetSend.LocalActor, cardId = id }, Photon.Realtime.ReceiverGroup.All);
                var owner = Character.localCharacter;
                if (owner != null) CardStatus.Penalty(owner, CharacterAfflictions.STATUSTYPE.Curse, P("curse", 0.05f));
            });
        }
    }
}
