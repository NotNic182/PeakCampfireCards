using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using CampfireCards.Cards;
using CampfireCards.Generated;
using CampfireCards.Art;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CampfireCards.Ui
{
    /// <summary>Hover feedback for one card in the fan (ui.json Card: hoverScale, frame boost, cardSwitch sound).</summary>
    public class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Frame;
        public Color FrameColor;
        public void OnPointerEnter(PointerEventData e)
        {
            transform.localScale = Vector3.one * UiTiming.HoverScale;
            if (Frame != null) Frame.color = Color.Lerp(FrameColor, Color.white, Ui_Card.HoverFrameBoost);
            Sfx.Play(Ui_Card.SoundOn, 0.8f);
        }
        public void OnPointerExit(PointerEventData e)
        {
            transform.localScale = Vector3.one;
            if (Frame != null) Frame.color = FrameColor;
        }
    }

    /// <summary>
    /// ui.json PickerWindow: the card fan. A MenuWindow so PEAK itself blocks movement and shows the cursor
    /// while it is open (GUIManager.windowBlockingInput / CursorHandler).
    /// </summary>
    public class CardPicker : MenuWindow
    {
        public static CardPicker Instance { get; private set; }
        private static readonly MethodInfo OpenM = AccessTools.Method(typeof(MenuWindow), "Open");
        private static readonly MethodInfo CloseM = AccessTools.Method(typeof(MenuWindow), "Close");

        public override bool openOnStart => false;
        public override bool selectOnOpen => false;
        public override bool closeOnPause => false;
        public override bool closeOnUICancel => false;
        public override bool blocksPlayerInput => true;
        public override bool showCursorWhileOpen => true;
        public override bool autoHideOnClose => true;
        public override GameObject panel => gameObject;

        /// <summary>
        /// Pick = the player's campfire choice; Test = dev sample (click closes); Sacrifice = Mountain's Bargain, choose a
        /// card to give up; Reveal = bonus cards just granted (click or wait to close).
        /// </summary>
        public enum Mode { Pick, Test, Sacrifice, Reveal }

        private TextMeshProUGUI _title, _subtitle;
        private RectTransform _fan;
        private GridLayoutGroup _grid;
        private const int MaxPerRow = 6;
        private readonly List<GameObject> _cards = new List<GameObject>();
        private float _deadline;
        private bool _picked;
        private byte[] _hand;
        public Mode Current { get; private set; }
        private System.Action<byte> _onSacrifice;
        /// <summary>A sacrifice waiting because the player's own pick has the window.</summary>
        private byte[] _pendingSacrifice;
        private System.Action<byte> _pendingSacrificeCallback;

        public static CardPicker Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PickerWindow", typeof(RectTransform));
            go.transform.SetParent(UiKit.Canvas.transform, false);
            UiKit.Stretch(UiKit.Rect(go));
            go.SetActive(false);
            Instance = go.AddComponent<CardPicker>();
            Instance.Build();
            return Instance;
        }

        /// <summary>Close a pick, sample or reveal fan (a pending sacrifice then opens). An open sacrifice stays.</summary>
        public static void HideIfOpen()
        {
            if (Instance == null || !Instance.isOpen) return;
            if (Instance.Current == Mode.Sacrifice) return;
            Instance.HideNow();
        }

        /// <summary>Close everything (run end, leaving the island). A pending sacrifice is dropped.</summary>
        public static void HideAll()
        {
            if (Instance == null) return;
            Instance._pendingSacrifice = null;
            Instance._pendingSacrificeCallback = null;
            Instance._onSacrifice = null;
            if (Instance.isOpen) CloseM.Invoke(Instance, null);
        }

        private void Build()
        {
            var backdrop = UiKit.Panel(transform, "Backdrop", Vector2.zero, Ui_PickerWindow.Backdrop);
            UiKit.Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            _title = UiKit.Text(transform, "Title", Ui_Title.Size, Ui_Title.FontSize, Ui_Title.Color, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.UpperCase);
            UiKit.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f));

            _subtitle = UiKit.Text(transform, "Subtitle", Ui_Subtitle.Size, Ui_Subtitle.FontSize, Ui_Subtitle.Color, TextAlignmentOptions.Center);
            UiKit.Place(_subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -130f));

            var fanGo = UiKit.Node(transform, "Fan", Ui_Fan.Size);
            _fan = UiKit.Rect(fanGo);
            _fan.anchoredPosition = new Vector2(0f, -20f);
            _grid = fanGo.AddComponent<GridLayoutGroup>();
            _grid.cellSize = Ui_Card.Size;
            _grid.spacing = new Vector2(Ui_Fan.Spacing, Ui_Fan.Spacing);
            _grid.childAlignment = TextAnchor.MiddleCenter;
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        }

        /// <summary>A pick offer (Mode.Pick) or a dev sample (Mode.Test).</summary>
        public void Show(byte reason, byte[] hand, float timeoutSeconds, Mode mode)
        {
            if (isOpen && Current == Mode.Sacrifice)
            {
                // the player's own pick comes first; the sacrifice opens again right after it
                _pendingSacrifice = _hand;
                _pendingSacrificeCallback = _onSacrifice;
                _onSacrifice = null;
            }
            _title.text = reason == 0 ? Ui_Title.OpeningTitle : Ui_Title.CampfireTitle;
            Open(hand, timeoutSeconds, mode);
        }

        /// <summary>Mountain's Bargain: the player chooses one of their cards to give up. A random one is taken on timeout.</summary>
        public void ShowSacrifice(byte[] cards, System.Action<byte> onChosen)
        {
            if (cards == null || cards.Length == 0 || onChosen == null) return;
            if (isOpen && Current == Mode.Pick)
            {
                _pendingSacrifice = cards;
                _pendingSacrificeCallback = onChosen;
                Hud.ShowPrompt(Deck.NameOfAbility("MountainsBargain") + ": choose a card to sacrifice after this pick");
                return;
            }
            _onSacrifice = onChosen;
            _title.text = Ui_Title.SacrificeTitle;
            float timeout = Plugin.PickTimeoutSeconds != null ? Plugin.PickTimeoutSeconds.Value : UiTiming.PickTimeoutSeconds;
            Open(cards, timeout, Mode.Sacrifice);
            Hud.HidePrompt();
        }

        /// <summary>Bonus cards were just added to the collection: show them (nothing to choose).</summary>
        public void ShowReveal(byte[] cards)
        {
            if (cards == null || cards.Length == 0) return;
            if (isOpen && (Current == Mode.Pick || Current == Mode.Sacrifice)) return;   // the toast already named them
            _title.text = Ui_Title.BonusTitle;
            Open(cards, 8f, Mode.Reveal);
        }

        private void Open(byte[] hand, float timeoutSeconds, Mode mode)
        {
            Current = mode;
            _hand = hand;
            _picked = false;
            _deadline = Time.unscaledTime + timeoutSeconds;
            foreach (var c in _cards) Destroy(c);
            _cards.Clear();
            foreach (var id in hand) _cards.Add(BuildCard(id));
            LayoutFan(hand.Length);
            if (!isOpen) OpenM.Invoke(this, null);
            Sfx.Play(Ui_PickerWindow.SoundOn);
            StartCoroutine(SlideIn());
        }

        /// <summary>Up to 6 cards per row; more rows (a sacrifice can list many cards), scaled down to fit the screen.</summary>
        private void LayoutFan(int n)
        {
            n = Mathf.Max(1, n);
            int rows = (n + MaxPerRow - 1) / MaxPerRow;
            int cols = (n + rows - 1) / rows;
            _grid.constraintCount = cols;
            float w = cols * Ui_Card.Size.x + (cols - 1) * Ui_Fan.Spacing;
            float h = rows * Ui_Card.Size.y + (rows - 1) * Ui_Fan.Spacing;
            _fan.sizeDelta = new Vector2(w, h);
            float scale = Mathf.Min(1f, 1840f / w, 800f / h);
            _fan.localScale = Vector3.one * scale;
            _fan.anchoredPosition = new Vector2(0f, rows > 1 ? -90f : -20f);
        }

        public void HideNow()
        {
            if (!isOpen) return;
            CloseM.Invoke(this, null);
            if (_pendingSacrifice != null)
            {
                var cards = _pendingSacrifice; var cb = _pendingSacrificeCallback;
                _pendingSacrifice = null; _pendingSacrificeCallback = null;
                ShowSacrifice(cards, cb);
            }
        }

        private void Choose(byte id)
        {
            if (_picked) return;
            Plugin.Verbose($"Card clicked: {id} ({Deck.NameOf(id)}) mode={Current}");
            switch (Current)
            {
                case Mode.Pick:
                    _picked = true;
                    PickSession.LocalPick(id);
                    break;
                case Mode.Sacrifice:
                    _picked = true;
                    var cb = _onSacrifice;
                    _onSacrifice = null;
                    Current = Mode.Reveal;
                    HideNow();
                    Sfx.Play("cardPick");
                    if (cb != null) cb(id);
                    break;
                default:
                    HideNow();
                    break;
            }
        }

        private IEnumerator SlideIn()
        {
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < UiTiming.FanSlideInSeconds + UiTiming.CardStaggerSeconds * _cards.Count)
            {
                for (int i = 0; i < _cards.Count; i++)
                {
                    if (_cards[i] == null) continue;
                    float t = Mathf.Clamp01((Time.unscaledTime - start - i * UiTiming.CardStaggerSeconds) / UiTiming.FanSlideInSeconds);
                    float eased = 1f - (1f - t) * (1f - t);
                    var inner = _cards[i].transform.GetChild(0) as RectTransform;
                    if (inner != null) inner.anchoredPosition = new Vector2(0f, Mathf.Lerp(Ui_Fan.SlideFromY, 0f, eased));
                }
                yield return null;
            }
            foreach (var c in _cards)
            {
                var inner = c != null ? c.transform.GetChild(0) as RectTransform : null;
                if (inner != null) inner.anchoredPosition = Vector2.zero;
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!isOpen || _picked) return;
            float left = Mathf.Max(0f, _deadline - Time.unscaledTime);
            switch (Current)
            {
                case Mode.Sacrifice:
                    _subtitle.text = "Choose a card to give up. " + Mathf.CeilToInt(left) + "s";
                    if (left <= 0f && _hand != null && _hand.Length > 0) Choose(_hand[Random.Range(0, _hand.Length)]);
                    break;
                case Mode.Reveal:
                    _subtitle.text = "Added to your cards. Click to continue.";
                    if (left <= 0f) HideNow();
                    break;
                default:
                    _subtitle.text = Ui_Subtitle.Text.Replace("{timeLeft}", Mathf.CeilToInt(left).ToString());
                    break;
            }
        }

        private GameObject BuildCard(byte id)
        {
            CardDef def; Deck.TryGet(id, out def);
            var theme = Deck.ThemeOf(def);
            Color rarity = Deck.RarityColor(def.Rarity);
            Color bgColor = theme.Bg.ToColor();
            Color accent = theme.Accent.ToColor();

            // slot keeps the layout stable; inner moves for the slide-in
            var slot = UiKit.Node(_fan, "CardSlot", Ui_Card.Size);
            var inner = UiKit.Node(slot.transform, "Card", Ui_Card.Size);

            // frame in the rarity colour around the theme background (ui.json Card)
            var frame = inner.AddComponent<Image>();
            frame.sprite = Shapes.Panel; frame.type = Image.Type.Sliced;
            frame.color = rarity;
            frame.raycastTarget = true;
            var bg = UiKit.Panel(inner.transform, "Background", Ui_Card.Size - Vector2.one * (Ui_Card.FrameWidth * 2f), bgColor, Shapes.Panel);
            bg.type = Image.Type.Sliced;

            var strip = UiKit.Panel(inner.transform, "RarityStrip", new Vector2(Ui_Card.Size.x - Ui_Card.CornerRadius * 2f, Ui_Card.RarityStripHeight), rarity, Shapes.Panel);
            strip.type = Image.Type.Sliced;
            UiKit.Place(strip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -Ui_Card.FrameWidth - 4f));

            // art window with the card's animated motif (ui.json CardArt, art.json)
            var window = UiKit.Panel(inner.transform, "ArtWindow", Ui_CardArt.Size, Color.Lerp(bgColor, Color.black, Ui_CardArt.WindowDarken), Shapes.Panel);
            window.type = Image.Type.Sliced;
            UiKit.Place(window.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardArt.Y));
            window.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var glow = UiKit.Panel(window.transform, "Glow", Vector2.one * Ui_CardArt.GlowSize, new Color(accent.r, accent.g, accent.b, Ui_CardArt.GlowAlpha), Shapes.Get(ArtShape.circle));
            UiKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            CardArt.Build(window.rectTransform, def.Art, theme);

            // rarity name in the rarity colour (ui.json CardRarity)
            var rarityText = UiKit.Text(inner.transform, "RarityName", Ui_CardRarity.Size, Ui_CardRarity.FontSize, rarity, TextAlignmentOptions.Center, FontStyles.Bold);
            UiKit.Place(rarityText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardRarity.Y));
            rarityText.text = Deck.RarityName(def.Rarity);

            // name (ui.json CardName)
            var name = UiKit.Text(inner.transform, "Name", Ui_CardName.Size, Ui_CardName.FontSize, Ui_CardName.Color, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.UpperCase);
            UiKit.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardName.Y));
            name.enableAutoSizing = true; name.fontSizeMin = Ui_CardName.MinFontSize; name.fontSizeMax = Ui_CardName.FontSize;
            name.text = def.Name;

            // benefit in green, drawback in red (ui.json CardEffects); Mythics have no drawback
            var effects = UiKit.Text(inner.transform, "Effects", Ui_CardEffects.Size, Ui_CardEffects.FontSize, Color.white, TextAlignmentOptions.Top);
            UiKit.Place(effects.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardEffects.Y));
            effects.enableAutoSizing = true; effects.fontSizeMin = Ui_CardEffects.MinFontSize; effects.fontSizeMax = Ui_CardEffects.FontSize;
            effects.text = EffectText(def);

            // flavour (ui.json CardFlavor)
            var flavor = UiKit.Text(inner.transform, "Flavor", Ui_CardFlavor.Size, Ui_CardFlavor.FontSize, Ui_CardFlavor.Color, TextAlignmentOptions.Center, FontStyles.Italic);
            UiKit.Place(flavor.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, Ui_CardFlavor.Y));
            flavor.enableAutoSizing = true; flavor.fontSizeMin = Ui_CardFlavor.MinFontSize; flavor.fontSizeMax = Ui_CardFlavor.FontSize;
            flavor.text = def.Flavor ?? "";

            var button = inner.AddComponent<Button>();
            button.targetGraphic = frame;
            byte captured = id;
            button.onClick.AddListener(() => Choose(captured));
            var hover = inner.AddComponent<CardHover>();
            hover.Frame = frame; hover.FrameColor = rarity;
            return slot;
        }

        /// <summary>The card's benefit and drawback lines exactly as written in cards.json.</summary>
        public static string EffectText(CardDef def)
        {
            var sb = new StringBuilder();
            sb.Append("<color=#").Append(UiKit.Hex(Ui_CardEffects.GoodColor)).Append('>')
              .Append(Prefixed(Ui_CardEffects.GoodPrefix, def.Positive)).Append("</color>\n");
            bool none = string.IsNullOrEmpty(def.Negative);
            string bad = none ? Ui_CardEffects.NoDrawback : Prefixed(Ui_CardEffects.BadPrefix, def.Negative);
            sb.Append("<color=#").Append(UiKit.Hex(none ? Ui_CardFlavor.Color : Ui_CardEffects.BadColor)).Append('>').Append(bad).Append("</color>");
            return sb.ToString();
        }

        /// <summary>The +/- marker, unless the text already starts with a sign ("+20 bonus stamina", "+10 Curse").</summary>
        private static string Prefixed(string prefix, string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            char c = text[0];
            return c == '+' || c == '-' ? text : prefix + text;
        }
    }
}
