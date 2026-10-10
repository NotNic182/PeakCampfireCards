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

        private TextMeshProUGUI _title, _subtitle;
        private RectTransform _fan;
        private readonly List<GameObject> _cards = new List<GameObject>();
        private float _deadline;
        private bool _picked;
        private byte[] _hand;
        /// <summary>Dev: the fan is shown without a session; clicking just closes it.</summary>
        public bool TestMode;

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

        public static void HideIfOpen()
        {
            if (Instance != null && Instance.isOpen) Instance.HideNow();
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
            var layout = fanGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = Ui_Fan.Spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false; layout.childControlHeight = false;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
        }

        public void Show(byte reason, byte[] hand, float timeoutSeconds, bool testMode = false)
        {
            TestMode = testMode;
            _hand = hand;
            _picked = false;
            _deadline = Time.unscaledTime + timeoutSeconds;
            _title.text = reason == 0 ? Ui_Title.OpeningTitle : Ui_Title.CampfireTitle;
            foreach (var c in _cards) Destroy(c);
            _cards.Clear();
            foreach (var id in hand) _cards.Add(BuildCard(id));
            OpenM.Invoke(this, null);
            Sfx.Play(Ui_PickerWindow.SoundOn);
            StartCoroutine(SlideIn());
        }

        public void HideNow()
        {
            if (!isOpen) return;
            CloseM.Invoke(this, null);
        }

        private IEnumerator SlideIn()
        {
            float start = Time.unscaledTime;
            var targets = new List<Vector3>();
            foreach (var c in _cards) targets.Add(Vector3.zero);
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
            _subtitle.text = Ui_Subtitle.Text.Replace("{timeLeft}", Mathf.CeilToInt(left).ToString());
        }

        private GameObject BuildCard(byte id)
        {
            CardDef def; Deck.TryGet(id, out def);
            var theme = Deck.ThemeOf(def);

            // slot keeps the layout stable; inner moves for the slide-in
            var slot = UiKit.Node(_fan, "CardSlot", Ui_Card.Size);
            var inner = UiKit.Node(slot.transform, "Card", Ui_Card.Size);

            // frame and background (ui.json Card)
            var frame = inner.AddComponent<Image>();
            frame.sprite = Shapes.Panel; frame.type = Image.Type.Sliced;
            frame.color = theme.Frame;
            frame.raycastTarget = true;
            var bg = UiKit.Panel(inner.transform, "Background", Ui_Card.Size - Vector2.one * (Ui_Card.FrameWidth * 2f), theme.Bg, Shapes.Panel);
            bg.type = Image.Type.Sliced;

            if (def.Rarity > 0)
            {
                var strip = UiKit.Panel(inner.transform, "Rarity", new Vector2(Ui_Card.Size.x - Ui_Card.CornerRadius * 2f, Ui_Card.RarityStripHeight), def.Rarity == 2 ? Ui_Card.RareColor : Ui_Card.UncommonColor, Shapes.Panel);
                strip.type = Image.Type.Sliced;
                UiKit.Place(strip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -Ui_Card.FrameWidth - 4f));
            }

            // art window with the card's animated motif (ui.json CardArt, art.json)
            var window = UiKit.Panel(inner.transform, "ArtWindow", Ui_CardArt.Size, Color.Lerp(theme.Bg, Color.black, Ui_CardArt.WindowDarken), Shapes.Panel);
            window.type = Image.Type.Sliced;
            UiKit.Place(window.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardArt.Y));
            window.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var glow = UiKit.Panel(window.transform, "Glow", Vector2.one * Ui_CardArt.GlowSize, new Color(theme.Accent.r, theme.Accent.g, theme.Accent.b, Ui_CardArt.GlowAlpha), Shapes.Get(ArtShape.circle));
            UiKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            CardArt.Build(window.rectTransform, def.Art, theme);

            // name (ui.json CardName)
            var name = UiKit.Text(inner.transform, "Name", Ui_CardName.Size, Ui_CardName.FontSize, Ui_CardName.Color, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.UpperCase);
            UiKit.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardName.Y));
            name.enableAutoSizing = true; name.fontSizeMin = Ui_CardName.MinFontSize; name.fontSizeMax = Ui_CardName.FontSize;
            name.text = def.Name;

            // effect lines, good in green and drawbacks in red (ui.json CardEffects)
            var effects = UiKit.Text(inner.transform, "Effects", Ui_CardEffects.Size, Ui_CardEffects.FontSize, Color.white, TextAlignmentOptions.Top);
            UiKit.Place(effects.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardEffects.Y));
            effects.enableAutoSizing = true; effects.fontSizeMin = Ui_CardEffects.MinFontSize; effects.fontSizeMax = Ui_CardEffects.FontSize;
            var sb = new StringBuilder();
            foreach (var line in Deck.EffectLines(def))
            {
                string col = UiKit.Hex(line.Value ? Ui_CardEffects.GoodColor : Ui_CardEffects.BadColor);
                sb.Append("<color=#").Append(col).Append(">").Append(line.Key).Append("</color>\n");
            }
            effects.text = sb.ToString().TrimEnd();

            // flavour (ui.json CardFlavor)
            var flavor = UiKit.Text(inner.transform, "Flavor", Ui_CardFlavor.Size, Ui_CardFlavor.FontSize, Ui_CardFlavor.Color, TextAlignmentOptions.Center, FontStyles.Italic);
            UiKit.Place(flavor.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, Ui_CardFlavor.Y));
            flavor.enableAutoSizing = true; flavor.fontSizeMin = Ui_CardFlavor.MinFontSize; flavor.fontSizeMax = Ui_CardFlavor.FontSize;
            flavor.text = def.Flavor;

            var button = inner.AddComponent<Button>();
            button.targetGraphic = frame;
            byte captured = id;
            button.onClick.AddListener(() =>
            {
                if (_picked) return;
                Plugin.Verbose($"Card clicked: {captured} ({Deck.NameOf(captured)}) testMode={TestMode}");
                if (TestMode) { HideNow(); return; }
                _picked = true;
                PickSession.LocalPick(captured);
            });
            var hover = inner.AddComponent<CardHover>();
            hover.Frame = frame; hover.FrameColor = theme.Frame;
            return slot;
        }
    }
}
