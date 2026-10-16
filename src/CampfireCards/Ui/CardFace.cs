using System.Text;
using CampfireCards.Art;
using CampfireCards.Cards;
using CampfireCards.Generated;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CampfireCards.Ui
{
    /// <summary>A built card face: the layout slot, the inner card that moves and scales, and (interactive) its button and hover.</summary>
    public struct CardFaceHandle
    {
        public GameObject Slot;
        public RectTransform Inner;
        public Button Button;
        public CardHover Hover;
        public byte Id;
    }

    /// <summary>
    /// One card face exactly as the pick fan draws it (ui.json Card, CardArt, CardRarity, CardName, CardEffects,
    /// CardFlavor): rarity frame around the theme background, rarity strip, animated art window, rarity badge, name,
    /// benefit and drawback, flavour. Shared by the pick fan (CardPicker) and the deck (DeckScreen); interactive
    /// faces get the Button and the hover/selection feedback, static ones never catch the pointer.
    /// </summary>
    public static class CardFace
    {
        public static CardFaceHandle Build(Transform parent, byte id, bool interactive)
        {
            CardDef def; Deck.TryGet(id, out def);
            var theme = Deck.ThemeOf(def);
            Color rarity = Deck.RarityColor(def.Rarity);
            Color bgColor = theme.Bg.ToColor();
            Color accent = theme.Accent.ToColor();

            // slot keeps the layout stable; inner moves for slide-in and expand animations
            var slot = UiKit.Node(parent, "CardSlot", Ui_Card.Size);
            var inner = UiKit.Node(slot.transform, "Card", Ui_Card.Size);

            // frame in the rarity colour around the theme background (ui.json Card)
            var frame = inner.AddComponent<Image>();
            frame.sprite = Shapes.Panel; frame.type = Image.Type.Sliced;
            frame.color = rarity;
            frame.raycastTarget = interactive;
            var bg = UiKit.Panel(inner.transform, "Background", Ui_Card.Size - Vector2.one * (Ui_Card.FrameWidth * 2f), bgColor, Shapes.Panel);
            bg.type = Image.Type.Sliced;

            var strip = UiKit.Panel(inner.transform, "RarityStrip", new Vector2(Ui_Card.Size.x - Ui_Card.CornerRadius * 2f, Ui_Card.RarityStripHeight), rarity, Shapes.Panel);
            strip.type = Image.Type.Sliced;
            UiKit.Place(strip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -Ui_Card.FrameWidth - Ui_Card.StripInset));

            // art window with the card's animated motif (ui.json CardArt, art.json)
            var window = UiKit.Panel(inner.transform, "ArtWindow", Ui_CardArt.Size, Color.Lerp(bgColor, Color.black, Ui_CardArt.WindowDarken), Shapes.Panel);
            window.type = Image.Type.Sliced;
            UiKit.Place(window.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardArt.Y));
            window.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var glow = UiKit.Panel(window.transform, "Glow", Vector2.one * Ui_CardArt.GlowSize, new Color(accent.r, accent.g, accent.b, Ui_CardArt.GlowAlpha), Shapes.Get(ArtShape.circle));
            UiKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            CardArt.Build(window.rectTransform, def.Art, theme, def.Key);

            // rarity badge: the rarity name in dark capitals on a chip filled with the rarity colour, straddling the art
            // window's bottom edge, so the rarity reads as a word and never rests on colour alone (ui.json CardRarity)
            string rarityName = Deck.RarityName(def.Rarity);
            var badge = UiKit.Panel(inner.transform, "RarityBadge", new Vector2(Ui_CardRarity.Size.x, Ui_CardRarity.BadgeHeight), rarity, Shapes.Panel);
            badge.type = Image.Type.Sliced;
            badge.pixelsPerUnitMultiplier = 2f;   // halve the panel sprite's corners for a chip this small
            badge.raycastTarget = false;
            UiKit.Place(badge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardRarity.Y));
            var rarityText = UiKit.Text(badge.transform, "RarityName", new Vector2(Ui_CardRarity.Size.x, Ui_CardRarity.BadgeHeight), Ui_CardRarity.FontSize,
                Ui_CardRarity.TextColor, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.UpperCase);
            UiKit.Place(rarityText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            rarityText.characterSpacing = Ui_CardRarity.CharacterSpacing;
            rarityText.raycastTarget = false;
            rarityText.text = rarityName;
            float badgeWidth = Mathf.Min(Ui_CardRarity.Size.x, rarityText.GetPreferredValues(rarityName).x + 2f * Ui_CardRarity.PadX);
            badge.rectTransform.sizeDelta = new Vector2(badgeWidth, Ui_CardRarity.BadgeHeight);
            rarityText.rectTransform.sizeDelta = new Vector2(badgeWidth, Ui_CardRarity.BadgeHeight);

            // name: up to two lines at full size so every card's name carries the same weight (ui.json CardName)
            var name = UiKit.Text(inner.transform, "Name", Ui_CardName.Size, Ui_CardName.FontSize, Ui_CardName.Color, TextAlignmentOptions.Top, FontStyles.Bold | FontStyles.UpperCase);
            UiKit.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardName.Y));
            UiKit.LineHeight(name, Ui_CardName.LineHeight);
            UiKit.AutoSize(name, Ui_CardName.MinFontSize);
            name.text = def.Name;
            name.ForceMeshUpdate(true);
            float nameHeight = Mathf.Max(1, name.textInfo.lineCount) * name.fontSize * Ui_CardName.LineHeight;

            // flavour, one line at the bottom, only when the card has one (ui.json CardFlavor)
            bool hasFlavor = !string.IsNullOrEmpty(def.Flavor);
            if (hasFlavor)
            {
                var flavor = UiKit.Text(inner.transform, "Flavor", Ui_CardFlavor.Size, Ui_CardFlavor.FontSize, Soft(accent), TextAlignmentOptions.Center, FontStyles.Italic);
                UiKit.Place(flavor.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, Ui_CardFlavor.Y));
                UiKit.AutoSize(flavor, Ui_CardFlavor.MinFontSize);
                flavor.text = def.Flavor;
            }

            // benefit in green, drawback in red, in the room between the name and the flavour (ui.json CardEffects)
            float effectsTop = -Ui_CardName.Y + nameHeight + Ui_CardEffects.Gap;
            float effectsFloor = hasFlavor ? Ui_CardFlavor.Y + Ui_CardFlavor.Size.y + Ui_CardFlavor.Gap : Ui_CardEffects.BottomPad;
            var effectsSize = new Vector2(Ui_CardEffects.Size.x, Mathf.Max(Ui_CardEffects.MinFontSize * 2f, Ui_Card.Size.y - effectsTop - effectsFloor));
            var effects = UiKit.Text(inner.transform, "Effects", effectsSize, Ui_CardEffects.FontSize, UiPalette.Text, TextAlignmentOptions.Top);
            UiKit.Place(effects.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -effectsTop));
            UiKit.LineHeight(effects, Ui_CardEffects.LineHeight);
            UiKit.AutoSize(effects, Ui_CardEffects.MinFontSize);
            effects.text = EffectText(def);

            var handle = new CardFaceHandle { Slot = slot, Inner = UiKit.Rect(inner), Id = id };
            if (interactive)
            {
                handle.Button = inner.AddComponent<Button>();
                handle.Button.targetGraphic = frame;
                handle.Hover = inner.AddComponent<CardHover>();
                handle.Hover.Frame = frame; handle.Hover.FrameColor = rarity; handle.Hover.CardId = id;
            }
            return handle;
        }

        /// <summary>Secondary text on a card face: the theme's accent, slightly softened, so it belongs to the card's own hue.</summary>
        private static Color Soft(Color accent) => new Color(accent.r, accent.g, accent.b, Ui_CardFlavor.AccentAlpha);

        /// <summary>The card's benefit (green) and drawback (red) exactly as written, unlabelled, with live key bindings filled in.</summary>
        public static string EffectText(CardDef def)
        {
            var sb = new StringBuilder();
            // the benefit's numbers are the ones this difficulty uses (Core.DifficultyScale); the drawback is never scaled
            sb.Append(UiKit.ColorTag(Ui_CardEffects.GoodColor)).Append(CardPresentation.Benefit(def, out string note)).Append("</color>");
            if (!string.IsNullOrEmpty(def.Negative) && Core.CardSettings.Current.DrawbacksEnabled)
                sb.Append('\n').Append(UiKit.ColorTag(Ui_CardEffects.BadColor)).Append(CardPresentation.Text(def, def.Negative)).Append("</color>");
            if (note != null)
                sb.Append('\n').Append("<size=80%>").Append(UiKit.ColorTag(UiPalette.Faint)).Append(note).Append("</color></size>");
            return sb.ToString();
        }
    }
}
