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
    /// <summary>
    /// Hover and controller-selection feedback for one card in the fan (ui.json Card: hoverScale, frame boost,
    /// cardSwitch sound). A chosen card is locked in the raised look while its pick is pending.
    /// </summary>
    public class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public Image Frame;
        public Color FrameColor;
        public byte CardId;
        private bool _locked;

        private Color Raised => Color.Lerp(FrameColor, Color.white, Ui_Card.HoverFrameBoost);

        public void OnPointerEnter(PointerEventData e) => Raise(true);
        public void OnPointerExit(PointerEventData e) => Lower();
        public void OnSelect(BaseEventData e) => Raise(true);
        public void OnDeselect(BaseEventData e) => Lower();

        private void Raise(bool sound)
        {
            if (_locked) return;
            transform.localScale = Vector3.one * UiTiming.HoverScale;
            if (Frame != null) Frame.color = Raised;
            if (sound) Sfx.Play(Ui_Card.SoundOn, Ui_Card.HoverVolume);
        }

        private void Lower()
        {
            if (_locked) return;
            transform.localScale = Vector3.one;
            if (Frame != null) Frame.color = FrameColor;
        }

        /// <summary>Chosen: flash the frame from white to the raised colour (selectedFlashSeconds) and hold it there.</summary>
        public void Lock()
        {
            Raise(false);
            _locked = true;
            StopAllCoroutines();
            if (isActiveAndEnabled) StartCoroutine(Flash());
        }

        /// <summary>The pick got no answer: back to normal, raised again if the pointer or the selection is still on it.</summary>
        public void Unlock(bool stillHere)
        {
            StopAllCoroutines();
            _locked = false;
            if (stillHere) Raise(false); else Lower();
        }

        private IEnumerator Flash()
        {
            float start = Time.unscaledTime;
            while (Frame != null && Time.unscaledTime - start < UiTiming.SelectedFlashSeconds)
            {
                float t = (Time.unscaledTime - start) / UiTiming.SelectedFlashSeconds;
                Frame.color = Color.Lerp(Color.white, Raised, 1f - (1f - t) * (1f - t) * (1f - t));
                yield return null;
            }
            if (Frame != null) Frame.color = Raised;
        }
    }

    /// <summary>The picker's backdrop: a click outside the cards (closes reveal and sample fans).</summary>
    public class BackdropClick : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnClick;
        public void OnPointerClick(PointerEventData e) { if (OnClick != null) OnClick(); }
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
        /// <summary>With a controller PEAK selects objectToSelectOnOpen (and falls back to it when the selection is lost).</summary>
        public override bool selectOnOpen => true;
        public override Selectable objectToSelectOnOpen => _buttons.Count > 0 ? _buttons[0] : null;
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
        private static readonly int MaxPerRow = Mathf.RoundToInt(Ui_Fan.MaxPerRow);
        private readonly List<GameObject> _cards = new List<GameObject>();
        private readonly List<Button> _buttons = new List<Button>();
        private float _deadline;
        private bool _hasTimer;
        private bool _picked;
        /// <summary>Pick mode: when a sent pick with no answer yet makes the cards choosable again.</summary>
        private float _pendingUntil;
        /// <summary>Pick mode: a sent pick got no answer; the subtitle says so until the next pick.</summary>
        private bool _retry;
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
            backdrop.gameObject.AddComponent<BackdropClick>().OnClick = () =>
            {
                if (isOpen && (Current == Mode.Reveal || Current == Mode.Test)) HideNow();
            };

            _title = UiKit.Text(transform, "Title", Ui_Title.Size, Ui_Title.FontSize, Ui_Title.Color, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.UpperCase);
            UiKit.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_Title.Y));
            UiKit.AutoSize(_title, Ui_Title.MinFontSize);   // the long sacrifice title on a narrow screen

            _subtitle = UiKit.Text(transform, "Subtitle", Ui_Subtitle.Size, Ui_Subtitle.FontSize, Ui_Subtitle.Color, TextAlignmentOptions.Center);
            UiKit.Place(_subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_Subtitle.Y));

            var fanGo = UiKit.Node(transform, "Fan", Ui_Fan.Size);
            _fan = UiKit.Rect(fanGo);
            _fan.anchoredPosition = new Vector2(0f, Ui_Fan.OneRowY);
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
                Hud.ShowPrompt(string.Format(Ui_Prompt.GiveUpLaterTemplate, Deck.NameOfAbility("MountainsBargain")));
                return;
            }
            _onSacrifice = onChosen;
            _title.text = string.Format(Ui_Title.SacrificeTitle, Deck.NameOfAbility("MountainsBargain"));
            float timeout = PickSession.TimeoutSeconds;
            Open(cards, timeout, Mode.Sacrifice);
            Hud.HidePrompt();
        }

        /// <summary>Bonus cards were just added to the collection: show them (nothing to choose).</summary>
        public void ShowReveal(byte[] cards)
        {
            if (cards == null || cards.Length == 0) return;
            if (isOpen && (Current == Mode.Pick || Current == Mode.Sacrifice)) return;   // the toast already named them
            _title.text = Ui_Title.BonusTitle;
            Open(cards, UiTiming.RevealSeconds, Mode.Reveal);
        }

        private void Open(byte[] hand, float timeoutSeconds, Mode mode)
        {
            Current = mode;
            _hand = hand;
            _picked = false;
            _retry = false;
            _hasTimer = timeoutSeconds > 0f;
            _title.rectTransform.sizeDelta = new Vector2(UiKit.FitWidth(Ui_Title.Size.x), Ui_Title.Size.y);
            _subtitle.rectTransform.sizeDelta = new Vector2(UiKit.FitWidth(Ui_Subtitle.Size.x), Ui_Subtitle.Size.y);
            _deadline = Time.unscaledTime + timeoutSeconds;
            foreach (var c in _cards) Destroy(c);
            _cards.Clear();
            _buttons.Clear();
            // open (active) before building: a card's name is measured as it is laid out, and text under an inactive
            // window has never woken up, so it would always measure as one line
            if (!isOpen) OpenM.Invoke(this, null);
            foreach (var id in hand) _cards.Add(BuildCard(id));
            WireNavigation(LayoutFan(hand.Length));
            // select the first new card (MenuWindow selected before the cards existed; a reopen destroyed the old one)
            if (_buttons.Count > 0)
            {
                try { UIInputHandler.SetSelectedObject(_buttons[0].gameObject); }
                catch (System.Exception e) { Plugin.Verbose("controller selection skipped: " + e.Message); }
            }
            Sfx.Play(Ui_PickerWindow.SoundOn);
            StartCoroutine(SlideIn());
        }

        /// <summary>Controller navigation across the grid: left/right within a row, up/down between rows (a short last row is reachable from every column).</summary>
        private void WireNavigation(int cols)
        {
            int n = _buttons.Count;
            for (int i = 0; i < n; i++)
            {
                int row = i / cols, col = i % cols;
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnLeft = col > 0 ? _buttons[i - 1] : null;
                nav.selectOnRight = col < cols - 1 && i + 1 < n ? _buttons[i + 1] : null;
                nav.selectOnUp = row > 0 ? _buttons[i - cols] : null;
                nav.selectOnDown = i + cols < n ? _buttons[i + cols] : (row + 1) * cols < n ? _buttons[n - 1] : null;
                _buttons[i].navigation = nav;
            }
        }

        /// <summary>Up to 6 cards per row; more rows (a sacrifice can list many cards), scaled down to fit the screen. Returns the column count.</summary>
        private int LayoutFan(int n)
        {
            n = Mathf.Max(1, n);
            int rows = (n + MaxPerRow - 1) / MaxPerRow;
            int cols = (n + rows - 1) / rows;
            _grid.constraintCount = cols;
            float w = cols * Ui_Card.Size.x + (cols - 1) * Ui_Fan.Spacing;
            float h = rows * Ui_Card.Size.y + (rows - 1) * Ui_Fan.Spacing;
            _fan.sizeDelta = new Vector2(w, h);

            // the band the fan may use: below the title and subtitle, safeMargin from the other edges (ui.json Fan)
            var canvas = UiKit.CanvasSize;
            float bandTop = canvas.y * 0.5f - Ui_Fan.TitleBand, bandBottom = -canvas.y * 0.5f + UiTiming.SafeMargin;
            float hoverRoom = UiTiming.HoverScale;   // a raised card must not touch the edge either
            float scale = Mathf.Min(1f, (canvas.x - 2f * UiTiming.SafeMargin) / (w * hoverRoom), (bandTop - bandBottom) / (h * hoverRoom));
            _fan.localScale = Vector3.one * scale;

            // keep the usual height, moved only as far as needed to stay inside the band
            float half = h * scale * 0.5f;
            float y = rows > 1 ? Ui_Fan.RowsY : Ui_Fan.OneRowY;
            y = bandTop - bandBottom < 2f * half ? (bandTop + bandBottom) * 0.5f : Mathf.Clamp(y, bandBottom + half, bandTop - half);
            _fan.anchoredPosition = new Vector2(0f, y);
            return cols;
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
                {
                    var offer = _hand;
                    if (!PickSession.LocalPick(id)) break;   // nothing sent: the cards stay choosable
                    // offline (solo) the host's answer runs inside LocalPick: the pick may already be accepted and this
                    // window closed or reopened for something else (the next offer, a sacrifice). Only a pick still
                    // waiting on this same offer shows as pending.
                    if (!isOpen || Current != Mode.Pick || !ReferenceEquals(_hand, offer)) break;
                    _picked = true;
                    _retry = false;
                    _pendingUntil = Time.unscaledTime + UiTiming.PickPendingRetrySeconds;
                    SetPending(id);
                    _subtitle.text = string.Format(Ui_Subtitle.PendingTemplate, Deck.NameOf(id));
                    break;
                }
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

        /// <summary>A pick was sent: the chosen card stays raised (with a flash), the others dim. null = all back to normal.</summary>
        private void SetPending(byte? chosen)
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            foreach (var slot in _cards)
            {
                if (slot == null) continue;
                var hover = slot.GetComponentInChildren<CardHover>();
                var group = slot.GetComponent<CanvasGroup>();
                if (group == null) group = slot.AddComponent<CanvasGroup>();
                bool isChosen = hover != null && chosen.HasValue && hover.CardId == chosen.Value;
                group.alpha = chosen.HasValue && !isChosen ? Ui_PickerWindow.DimAlpha : 1f;
                if (hover == null) continue;
                if (isChosen) hover.Lock();
                else hover.Unlock(stillHere: !chosen.HasValue && (hover.gameObject == selected || PointerOver(hover.gameObject)));
            }
        }

        private static bool PointerOver(GameObject go)
        {
            if (Controls.UsingGamepad) return false;
            var rt = go.transform as RectTransform;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return rt != null && mouse != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mouse.position.ReadValue(), null);
        }

        protected override void Update()
        {
            base.Update();
            if (!isOpen) return;
            if (_picked)
            {
                // a sent pick with no answer: let the player choose again (the host refuses a second claim, so a late
                // answer to the first one still wins)
                if (Current != Mode.Pick || Time.unscaledTime < _pendingUntil) return;
                _picked = false;
                _retry = true;
                SetPending(null);
                Plugin.Log.LogWarning("No answer from the host to the card pick; the cards can be chosen again.");
            }
            float left = Mathf.Max(0f, _deadline - Time.unscaledTime);
            string seconds = Mathf.CeilToInt(left).ToString();
            switch (Current)
            {
                case Mode.Sacrifice:
                    _subtitle.text = _hasTimer ? string.Format(Ui_Subtitle.SacrificeTemplate, seconds) : Ui_Subtitle.SacrificeUntimedText;
                    if (_hasTimer && left <= 0f && _hand != null && _hand.Length > 0) Choose(_hand[Random.Range(0, _hand.Length)]);
                    break;
                case Mode.Reveal:
                    _subtitle.text = Controls.UsingGamepad ? Ui_Subtitle.RevealPadText : Ui_Subtitle.RevealText;
                    if (left <= 0f || CancelPressed()) HideNow();
                    break;
                case Mode.Test:
                    _subtitle.text = Ui_Subtitle.Text.Replace("{timeLeft}", seconds);
                    if (CancelPressed()) HideNow();
                    break;
                default:
                    _subtitle.text = _hasTimer
                        ? (_retry ? string.Format(Ui_Subtitle.RetryTemplate, seconds) : Ui_Subtitle.Text.Replace("{timeLeft}", seconds))
                        : (_retry ? Ui_Subtitle.RetryUntimedText : Ui_Subtitle.UntimedText);
                    break;
            }
        }

        /// <summary>PEAK's UI cancel (Escape, or B / Circle on a controller) this frame.</summary>
        private static bool CancelPressed()
        {
            try { return UIInputHandler.action_cancel != null && UIInputHandler.action_cancel.WasPressedThisFrame(); }
            catch { return false; }
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
            UiKit.Place(strip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -Ui_Card.FrameWidth - Ui_Card.StripInset));

            // art window with the card's animated motif (ui.json CardArt, art.json)
            var window = UiKit.Panel(inner.transform, "ArtWindow", Ui_CardArt.Size, Color.Lerp(bgColor, Color.black, Ui_CardArt.WindowDarken), Shapes.Panel);
            window.type = Image.Type.Sliced;
            UiKit.Place(window.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardArt.Y));
            window.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var glow = UiKit.Panel(window.transform, "Glow", Vector2.one * Ui_CardArt.GlowSize, new Color(accent.r, accent.g, accent.b, Ui_CardArt.GlowAlpha), Shapes.Get(ArtShape.circle));
            UiKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            CardArt.Build(window.rectTransform, def.Art, theme, def.Key);

            // rarity name in the rarity colour: a spaced uppercase label, not a second name (ui.json CardRarity)
            var rarityText = UiKit.Text(inner.transform, "RarityName", Ui_CardRarity.Size, Ui_CardRarity.FontSize, rarity, TextAlignmentOptions.Center, FontStyles.UpperCase);
            UiKit.Place(rarityText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_CardRarity.Y));
            rarityText.characterSpacing = Ui_CardRarity.CharacterSpacing;
            rarityText.text = Deck.RarityName(def.Rarity);

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
            effects.text = EffectText(def, Soft(accent));

            var button = inner.AddComponent<Button>();
            button.targetGraphic = frame;
            byte captured = id;
            button.onClick.AddListener(() => Choose(captured));
            _buttons.Add(button);
            var hover = inner.AddComponent<CardHover>();
            hover.Frame = frame; hover.FrameColor = rarity; hover.CardId = id;
            return slot;
        }

        /// <summary>Secondary text on a card face: the theme's accent, slightly softened, so it belongs to the card's own hue.</summary>
        private static Color Soft(Color accent) => new Color(accent.r, accent.g, accent.b, Ui_CardFlavor.AccentAlpha);

        /// <summary>The card's named benefit and drawback lines, with live key bindings filled in. noDrawback colours "No drawback".</summary>
        public static string EffectText(CardDef def, Color noDrawback)
        {
            var sb = new StringBuilder();
            sb.Append(UiKit.ColorTag(Ui_CardEffects.GoodColor))
              .Append("<b>").Append(Ui_CardEffects.GoodPrefix).Append("</b>").Append(CardPresentation.Text(def, def.Positive)).Append("</color>\n");
            bool none = string.IsNullOrEmpty(def.Negative);
            string bad = none ? Ui_CardEffects.NoDrawback : "<b>" + Ui_CardEffects.BadPrefix + "</b>" + CardPresentation.Text(def, def.Negative);
            sb.Append(UiKit.ColorTag(none ? noDrawback : Ui_CardEffects.BadColor)).Append(bad).Append("</color>");
            return sb.ToString();
        }

    }
}
