using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using PeakRounds.Cards;
using PeakRounds.Generated;
using PeakRounds.Rounds;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeakRounds.Ui
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
            RoundsAudio.Play("cardSwitch", 0.8f);
        }
        public void OnPointerExit(PointerEventData e)
        {
            transform.localScale = Vector3.one;
            if (Frame != null) Frame.color = FrameColor;
        }
    }

    /// <summary>
    /// ui.json PickerWindow: the ROUNDS-style card fan. A MenuWindow so PEAK itself blocks movement and shows the cursor
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
            RoundsAudio.Play(Ui_PickerWindow.SoundOn);
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
            var rc = Deck.RoundsFor(id);
            var data = RoundsReader.Data;
            var theme = data != null ? data.Theme(rc != null ? rc.ColorTheme : 3) : RoundsData.DefaultThemes()[3];
            var bg = theme.Bg; bg.a = 1f;
            float bright = bg.r * 0.299f + bg.g * 0.587f + bg.b * 0.114f;
            if (bright < Ui_Card.BgMinBrightness) bg = Color.Lerp(bg, theme.Target, 0.25f);

            // slot keeps the layout stable; inner moves for the slide-in
            var slot = UiKit.Node(_fan, "CardSlot", Ui_Card.Size);
            var inner = UiKit.Node(slot.transform, "Card", Ui_Card.Size);

            var frame = inner.AddComponent<Image>();
            var squareTex = data != null ? data.Texture(RA_SquareSprite.Name) : null;
            if (squareTex != null && squareTex.Sprite != null) frame.sprite = squareTex.Sprite;
            frame.color = theme.Target;
            frame.raycastTarget = true;

            var bgImg = UiKit.Panel(inner.transform, "Background", Ui_Card.Size - Vector2.one * (Ui_Card.FrameWidth * 2f), bg);
            var bgTex = data != null ? data.Texture(RA_CardBG.Name) : null;
            if (bgTex != null && bgTex.Sprite != null) { bgImg.sprite = bgTex.Sprite; bgImg.color = Color.Lerp(bg, Color.white, 0.15f); }

            if (rc != null && rc.Rarity > 0)
            {
                var strip = UiKit.Panel(inner.transform, "Rarity", new Vector2(Ui_Card.Size.x - Ui_Card.FrameWidth * 2f, Ui_Card.RarityStripHeight), rc.Rarity == 2 ? Ui_Card.RareColor : Ui_Card.UncommonColor);
                UiKit.Place(strip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -Ui_Card.FrameWidth));
            }

            // icon (ui.json CardIcon): ROUNDS's own sprite for the card; cards without one get a monogram (ui.json CardMonogram)
            var iconTex = data != null && rc != null ? data.Texture(rc.IconTextureName) : null;
            if (iconTex != null && iconTex.Sprite != null)
            {
                var icon = UiKit.Panel(inner.transform, "Icon", Ui_CardIcon.Size, new Color(theme.Target.r, theme.Target.g, theme.Target.b, Ui_CardIcon.Alpha), iconTex.Sprite);
                icon.preserveAspect = true;
                UiKit.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardIcon.Y));
            }
            else
            {
                string nm = rc != null ? rc.Name : def.Rounds;
                var mono = UiKit.Text(inner.transform, "Monogram", Ui_CardMonogram.Size, Ui_CardMonogram.FontSize, new Color(theme.Target.r, theme.Target.g, theme.Target.b, Ui_CardMonogram.Alpha), TextAlignmentOptions.Center, FontStyles.Bold);
                UiKit.Place(mono.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardMonogram.Y));
                mono.text = Ui_CardMonogram.Text.Replace("{firstLetter}", string.IsNullOrEmpty(nm) ? "?" : nm.Substring(0, 1).ToUpperInvariant());
            }

            // name (ui.json CardName)
            var name = UiKit.Text(inner.transform, "Name", Ui_CardName.Size, Ui_CardName.FontSize, Ui_CardName.Color, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.UpperCase);
            UiKit.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardName.Y));
            name.enableAutoSizing = true; name.fontSizeMin = Ui_CardName.MinFontSize; name.fontSizeMax = Ui_CardName.FontSize;
            name.text = rc != null ? rc.Name : def.Rounds;

            // ROUNDS stats (ui.json CardStats)
            var stats = UiKit.Text(inner.transform, "Stats", Ui_CardStats.Size, Ui_CardStats.FontSize, Ui_CardStats.LabelColor, TextAlignmentOptions.Center);
            UiKit.Place(stats.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardStats.Y));
            stats.overflowMode = TextOverflowModes.Truncate;
            var sb = new StringBuilder();
            if (rc != null)
            {
                foreach (var st in rc.Stats)
                {
                    string col = UiKit.Hex(st.Positive ? Ui_CardStats.PositiveColor : Ui_CardStats.NegativeColor);
                    sb.Append(st.Label).Append("  <color=#").Append(col).Append(">").Append(st.Amount).Append("</color>\n");
                }
            }
            stats.text = sb.ToString().TrimEnd();

            // ROUNDS description (ui.json CardDescription)
            string desc = rc != null ? rc.Description : null;
            if (!string.IsNullOrWhiteSpace(desc))
            {
                var d = UiKit.Text(inner.transform, "Description", Ui_CardDescription.Size, Ui_CardDescription.FontSize, Ui_CardDescription.Color, TextAlignmentOptions.Center, FontStyles.Italic);
                UiKit.Place(d.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardDescription.Y));
                d.enableAutoSizing = true; d.fontSizeMin = 10f; d.fontSizeMax = Ui_CardDescription.FontSize;
                d.text = desc.Replace("\n", " ");
            }

            // what it does on the mountain (ui.json CardMountain)
            var mPanel = UiKit.Panel(inner.transform, "Mountain", Ui_CardMountain.Size, Ui_CardMountain.Panel);
            UiKit.Place(mPanel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, Ui_CardMountain.Y));
            var mText = UiKit.Text(mPanel.transform, "MountainText", Ui_CardMountain.Size - new Vector2(12f, 8f), Ui_CardMountain.FontSize, Ui_CardMountain.Color, TextAlignmentOptions.Center);
            mText.enableAutoSizing = true; mText.fontSizeMin = 10f; mText.fontSizeMax = Ui_CardMountain.FontSize;
            var lines = Deck.MountainLines(def);
            mText.text = "<b>" + Ui_CardMountain.Header + "</b>\n" + string.Join("\n", lines);

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
            hover.Frame = frame; hover.FrameColor = theme.Target;
            return slot;
        }
    }
}
