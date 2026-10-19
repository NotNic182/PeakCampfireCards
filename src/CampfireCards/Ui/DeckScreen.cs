using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CampfireCards.Ui
{
    /// <summary>
    /// ui.json DeckScreen: your deck as real cards. The Deck key (or a click on the HUD pile) opens a MenuWindow where
    /// the deck expands into a fan of card faces with ALL CARDS ADDED UP (the live combined stats) beneath it; clicking
    /// a card pulls it out for a close look (benefit, drawback, each conditional modifier on or off right now, charges,
    /// aura reach, pick order). The key cycles on: TEAM DECKS, one clickable pile of card backs per teammate, and a
    /// third press closes. Clicking a pile fans that teammate's cards out; Esc backs out one step (hooks.json
    /// pausePressed keeps the pause menu from opening on top). It never computes a modifier itself (Effects and
    /// Core.BuildStats do).
    /// </summary>
    public class DeckScreen : MenuWindow
    {
        public static DeckScreen Instance { get; private set; }
        private static readonly MethodInfo OpenM = AccessTools.Method(typeof(MenuWindow), "Open");
        private static readonly MethodInfo CloseM = AccessTools.Method(typeof(MenuWindow), "Close");

        public override bool openOnStart => false;
        public override bool selectOnOpen => true;
        public override Selectable objectToSelectOnOpen => _buttons.Count > 0 ? _buttons[0] : null;
        public override bool closeOnPause => false;
        public override bool closeOnUICancel => false;
        public override bool blocksPlayerInput => true;
        public override bool showCursorWhileOpen => true;
        public override bool autoHideOnClose => true;
        public override GameObject panel => gameObject;

        private enum View { Fan, Pulled, Team, TeamDeck }
        private View _view;
        /// <summary>The teammate whose fanned-out cards the TeamDeck view shows.</summary>
        private int _teamFocus = int.MinValue;
        /// <summary>The frame a back input was handled: Esc can arrive through the pause press and action_cancel at once.</summary>
        private int _backFrame = -1;

        private TextMeshProUGUI _title, _subtitle;
        private RectTransform _fan;
        private GridLayoutGroup _grid;
        private readonly List<GameObject> _cards = new List<GameObject>();
        private readonly List<Button> _buttons = new List<Button>();
        private Image _totalsPanel;
        private TextMeshProUGUI _totalsLeft, _totalsRight;
        private RectTransform _pulledRoot, _pulledSlot;
        private TextMeshProUGUI _detail;
        private Button _prev, _next;
        private RectTransform _teamRoot;
        private readonly List<Button> _teamButtons = new List<Button>();
        private byte[] _deck = new byte[0];
        private int _pulledIndex;
        private float _nextRefresh;
        /// <summary>The frame OpenFan ran: Hooks opened the window on this key press, so Update must not step on it too.</summary>
        private int _openedFrame;
        private string _teamPrint;
        private static readonly ModResult _always = new ModResult();
        private static readonly int MaxPerRow = Mathf.RoundToInt(Ui_Fan.MaxPerRow);

        // ------------------------------------------------------------------ entry points (Hooks and the HUD pile)
        /// <summary>The Deck key: open the fan, step to Team Decks (with teammates), then close. A pulled card steps back to the fan.</summary>
        public static void Toggle(Character me)
        {
            if (Instance != null && Instance.isOpen) { Instance.Step(); return; }
            if (!RunState.OnIsland || me == null) return;
            if (CardPicker.Instance != null && CardPicker.Instance.isOpen) return;   // never under a real choice
            // the pause menu (or any other blocking window) has the screen: the HUD pile click must not open under it
            bool blocked = false;
            try { blocked = GUIManager.instance != null && (GUIManager.instance.windowBlockingInput || GUIManager.instance.wheelActive); } catch { }
            if (blocked) return;
            Ensure().OpenFan();
        }

        /// <summary>A click on the HUD pile (possible whenever a cursor is visible).</summary>
        public static void ToggleFromHud() => Toggle(Character.localCharacter);

        public static void HideIfOpen()
        {
            if (Instance != null && Instance.isOpen) Instance.HideNow();
        }

        /// <summary>Per frame from Hooks.Tick: closes for a pick or off the island, refreshes the live numbers 4 times a second.</summary>
        public static void Tick(Character me)
        {
            if (Instance == null || !Instance.isOpen) return;
            if (!RunState.OnIsland || me == null || (CardPicker.Instance != null && CardPicker.Instance.isOpen)) { HideIfOpen(); return; }
            if (Time.unscaledTime < Instance._nextRefresh) return;
            Instance._nextRefresh = Time.unscaledTime + UiTiming.BuildRefreshSeconds;
            // the deck can grow while the screen is open (a bonus reveal, the dev tester): rebuild the fan to match
            if (Instance._view == View.Fan && Effects.CardsOf(NetSend.LocalActor).Count != Instance._deck.Length) { Instance.ShowFan(); return; }
            Instance.RefreshTotals();
            if (Instance._view == View.Pulled) Instance.RefreshDetail(me);
            if (Instance._view == View.Team || Instance._view == View.TeamDeck)
            {
                string print = TeamFingerprint();
                if (print != Instance._teamPrint)
                {
                    Instance._teamPrint = print;
                    Instance.ClearTeam();
                    if (Instance._view == View.Team) Instance.BuildTeam();
                    else if (Teammates().Contains(Instance._teamFocus)) Instance.BuildTeamDeck(Instance._teamFocus);
                    else Instance.ShowTeam();   // that teammate left the room: back to the deck row
                }
            }
        }

        /// <summary>What the team view shows, cheap enough to compare 4 times a second: who, which cards, state, penalties.</summary>
        private static string TeamFingerprint()
        {
            var sb = new StringBuilder();
            foreach (int actor in Teammates())
            {
                sb.Append(actor).Append(':');
                // the card ids, not just the count: a swap (Fine Print's sacrifice beside a pick) must redraw too
                foreach (var id in Effects.CardsOf(actor)) sb.Append(id).Append('.');
                sb.Append(':');
                Character ch;
                if (PlayerHandler.TryGetCharacter(actor, out ch) && ch != null && ch.data != null)
                    sb.Append(ch.data.dead ? 'g' : (ch.data.passedOut || ch.data.fullyPassedOut) ? 'd' : 'u');
                sb.Append(':').Append(Effects.Penalties.Of(actor).Count).Append('|');
            }
            return sb.ToString();
        }

        private static DeckScreen Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("DeckScreen", typeof(RectTransform));
            go.transform.SetParent(UiKit.Canvas.transform, false);
            UiKit.Stretch(UiKit.Rect(go));
            go.SetActive(false);
            Instance = go.AddComponent<DeckScreen>();
            Instance.Build();
            return Instance;
        }

        private void Build()
        {
            var backdrop = UiKit.Panel(transform, "Backdrop", Vector2.zero, Ui_DeckScreen.Backdrop);
            UiKit.Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;
            backdrop.gameObject.AddComponent<BackdropClick>().OnClick = () => { if (isOpen) BackFromInput(); };

            _title = UiKit.Text(transform, "Title", new Vector2(1400f, 90f), Ui_DeckScreen.TitleFontSize, Ui_DeckScreen.TitleColor, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.UpperCase);
            _title.richText = false;   // the TeamDeck title is a raw player name; a name with TMP tags must not restyle the screen
            UiKit.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_DeckScreen.TitleY));
            UiKit.AutoSize(_title, Ui_DeckScreen.TitleMinFontSize);

            _subtitle = UiKit.Text(transform, "Subtitle", new Vector2(1400f, 40f), Ui_DeckScreen.SubtitleFontSize, Ui_DeckScreen.SubtitleColor, TextAlignmentOptions.Center);
            UiKit.Place(_subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, Ui_DeckScreen.SubtitleY));

            var fanGo = UiKit.Node(transform, "Fan", Vector2.zero);
            _fan = UiKit.Rect(fanGo);
            _grid = fanGo.AddComponent<GridLayoutGroup>();
            _grid.cellSize = Ui_Card.Size;
            _grid.spacing = new Vector2(Ui_Fan.Spacing, Ui_Fan.Spacing);
            _grid.childAlignment = TextAnchor.MiddleCenter;
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;

            _totalsPanel = UiKit.Panel(transform, "Totals", new Vector2(Ui_DeckScreen.TotalsWidth, Ui_DeckScreen.TotalsHeight), Ui_DeckScreen.TotalsPanel, Art.Shapes.Panel);
            _totalsPanel.type = Image.Type.Sliced;
            UiKit.Place(_totalsPanel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, Ui_DeckScreen.TotalsY));
            float colW = (Ui_DeckScreen.TotalsWidth - 2f * Ui_DeckScreen.TotalsPad - Ui_DeckScreen.TotalsColGap) * 0.5f;
            float colH = Ui_DeckScreen.TotalsHeight - 2f * Ui_DeckScreen.TotalsPad;
            _totalsLeft = TotalsColumn("Left", colW, colH, Ui_DeckScreen.TotalsPad);
            _totalsRight = TotalsColumn("Right", colW, colH, Ui_DeckScreen.TotalsPad + colW + Ui_DeckScreen.TotalsColGap);

            BuildPulled();

            var teamGo = UiKit.Node(transform, "Team", Vector2.zero);
            _teamRoot = UiKit.Rect(teamGo);

            gameObject.SetActive(false);
        }

        private TextMeshProUGUI TotalsColumn(string name, float w, float h, float x)
        {
            var t = UiKit.Text(_totalsPanel.transform, name, new Vector2(w, h), Ui_DeckScreen.FontSize, UiPalette.Text, TextAlignmentOptions.TopLeft);
            UiKit.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(x, -Ui_DeckScreen.TotalsPad));
            UiKit.LineHeight(t, Ui_DeckScreen.LineHeight);
            t.overflowMode = TextOverflowModes.Ellipsis;
            UiKit.AutoSize(t, Ui_DeckScreen.MinFontSize);
            return t;
        }

        private void BuildPulled()
        {
            var go = UiKit.Node(transform, "Pulled", Vector2.zero);
            _pulledRoot = UiKit.Rect(go);
            UiKit.Stretch(_pulledRoot);

            _pulledSlot = UiKit.Rect(UiKit.Node(go.transform, "PulledCard", Ui_Card.Size));
            UiKit.Place(_pulledSlot, new Vector2(0.5f, 0.5f), new Vector2(Ui_DeckScreen.PulledX, Ui_DeckScreen.PulledY));
            _pulledSlot.localScale = Vector3.one * Ui_DeckScreen.PulledScale;

            var panel = UiKit.Panel(go.transform, "DetailPanel", new Vector2(Ui_DeckScreen.DetailWidth, Ui_DeckScreen.DetailHeight), Ui_DeckScreen.DetailPanel, Art.Shapes.Panel);
            panel.type = Image.Type.Sliced;
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(Ui_DeckScreen.DetailX + Ui_DeckScreen.DetailWidth * 0.5f, Ui_DeckScreen.PulledY));
            _detail = UiKit.Text(panel.transform, "Detail", new Vector2(Ui_DeckScreen.DetailWidth - 2f * Ui_DeckScreen.DetailPad, Ui_DeckScreen.DetailHeight - 2f * Ui_DeckScreen.DetailPad),
                Ui_DeckScreen.DetailFontSize, UiPalette.Text, TextAlignmentOptions.TopLeft);
            UiKit.Place(_detail.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            UiKit.LineHeight(_detail, Ui_DeckScreen.LineHeight);
            _detail.overflowMode = TextOverflowModes.Ellipsis;
            UiKit.AutoSize(_detail, Ui_DeckScreen.DetailMinFontSize);

            _prev = Arrow(go.transform, "<", new Vector2(Ui_DeckScreen.PulledX - (Ui_Card.Size.x * Ui_DeckScreen.PulledScale) * 0.5f - Ui_DeckScreen.ArrowGap - Ui_DeckScreen.ArrowSize * 0.5f, Ui_DeckScreen.PulledY), -1);
            _next = Arrow(go.transform, ">", new Vector2(Ui_DeckScreen.DetailX + Ui_DeckScreen.DetailWidth + Ui_DeckScreen.ArrowGap + Ui_DeckScreen.ArrowSize * 0.5f, Ui_DeckScreen.PulledY), +1);
            go.SetActive(false);
        }

        private Button Arrow(Transform parent, string glyph, Vector2 pos, int dir)
        {
            var panel = UiKit.Panel(parent, "Arrow" + glyph, Vector2.one * Ui_DeckScreen.ArrowSize, Ui_DeckScreen.TotalsPanel, Art.Shapes.Get(ArtShape.circle));
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), pos);
            panel.raycastTarget = true;
            var label = UiKit.Text(panel.transform, "Glyph", Vector2.one * Ui_DeckScreen.ArrowSize, Ui_DeckScreen.ArrowFontSize, Ui_DeckScreen.ArrowColor, TextAlignmentOptions.Center, FontStyles.Bold);
            UiKit.Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f));
            label.text = glyph;
            var b = panel.gameObject.AddComponent<Button>();
            b.targetGraphic = panel;
            b.onClick.AddListener(() => Flip(dir));
            return b;
        }

        // ------------------------------------------------------------------ views
        private void OpenFan()
        {
            if (!isOpen) OpenM.Invoke(this, null);
            _openedFrame = Time.frameCount;
            _title.rectTransform.sizeDelta = new Vector2(UiKit.FitWidth(1400f), 90f);
            _subtitle.rectTransform.sizeDelta = new Vector2(UiKit.FitWidth(1400f), 40f);
            Art.Sfx.Play(Ui_DeckScreen.SoundOn);
            ShowFan(expand: true);
        }

        /// <summary>The fan of your cards, expanding from the pile when the screen has just opened.</summary>
        private void ShowFan(bool expand = false, int select = 0)
        {
            StopAllCoroutines();   // a running Expand must never animate a rebuilt fan
            _view = View.Fan;
            _pulledRoot.gameObject.SetActive(false);
            _teamRoot.gameObject.SetActive(false);
            ClearTeam();
            _fan.gameObject.SetActive(true);
            _totalsPanel.gameObject.SetActive(true);
            FitTotals();
            _title.text = Ui_DeckScreen.Title;

            var mine = Effects.CardsOf(NetSend.LocalActor);
            _deck = mine.ToArray();
            // detach before the deferred Destroy, so the grid's immediate layout pass never counts dying cards
            foreach (var c in _cards) { if (c != null) { c.transform.SetParent(null, false); Destroy(c); } }
            _cards.Clear();
            _buttons.Clear();
            foreach (var id in _deck)
            {
                var face = CardFace.Build(_fan, id, interactive: true);
                int index = _cards.Count;
                face.Button.onClick.AddListener(() => Pull(index));
                _cards.Add(face.Slot);
                _buttons.Add(face.Button);
            }
            WireNavigation(LayoutFan(_deck.Length));
            Reselect(select);
            RefreshTotals();
            RefreshSubtitle();
            if (expand && _cards.Count > 0) StartCoroutine(Expand());
        }

        /// <summary>The totals panel and its two columns, refitted to the canvas every open (UiScale and resolution change).</summary>
        private void FitTotals()
        {
            float w = UiKit.FitWidth(Ui_DeckScreen.TotalsWidth);
            _totalsPanel.rectTransform.sizeDelta = new Vector2(w, Ui_DeckScreen.TotalsHeight);
            float colW = (w - 2f * Ui_DeckScreen.TotalsPad - Ui_DeckScreen.TotalsColGap) * 0.5f;
            float colH = Ui_DeckScreen.TotalsHeight - 2f * Ui_DeckScreen.TotalsPad;
            _totalsLeft.rectTransform.sizeDelta = new Vector2(colW, colH);
            _totalsRight.rectTransform.sizeDelta = new Vector2(colW, colH);
            _totalsRight.rectTransform.anchoredPosition = new Vector2(Ui_DeckScreen.TotalsPad + colW + Ui_DeckScreen.TotalsColGap, -Ui_DeckScreen.TotalsPad);
        }

        /// <summary>One card out of the deck: the face enlarged, everything it does beside it.</summary>
        private void Pull(int index)
        {
            if (_deck.Length == 0) return;
            bool flip = _view == View.Pulled;
            _pulledIndex = (index % _deck.Length + _deck.Length) % _deck.Length;
            _view = View.Pulled;
            _fan.gameObject.SetActive(false);
            _totalsPanel.gameObject.SetActive(false);
            _teamRoot.gameObject.SetActive(false);
            _pulledRoot.gameObject.SetActive(true);
            _title.text = Ui_DeckScreen.Title;

            FitPulled();
            for (int i = _pulledSlot.childCount - 1; i >= 0; i--)
            {
                var child = _pulledSlot.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
            var face = CardFace.Build(_pulledSlot, _deck[_pulledIndex], interactive: false);
            UiKit.Place(UiKit.Rect(face.Slot), new Vector2(0.5f, 0.5f), Vector2.zero);
            RefreshDetail(Character.localCharacter);
            RefreshSubtitle();
            if (!flip)
            {
                Art.Sfx.Play(Ui_DeckScreen.PullSound);
                try { UIInputHandler.SetSelectedObject(_next.gameObject); }
                catch (System.Exception e) { Plugin.Verbose("controller selection skipped: " + e.Message); }
            }
        }

        /// <summary>The pulled view at fixed reference offsets, scaled as one so card, panel and arrows always fit the canvas.</summary>
        private void FitPulled()
        {
            var canvas = UiKit.CanvasSize;
            float halfW = canvas.x * 0.5f - UiTiming.SafeMargin;
            float leftSpan = -(Ui_DeckScreen.PulledX - Ui_Card.Size.x * Ui_DeckScreen.PulledScale * 0.5f - Ui_DeckScreen.ArrowGap - Ui_DeckScreen.ArrowSize);
            float rightSpan = Ui_DeckScreen.DetailX + Ui_DeckScreen.DetailWidth + Ui_DeckScreen.ArrowGap + Ui_DeckScreen.ArrowSize;
            float bandTop = canvas.y * 0.5f - Ui_DeckScreen.FanBandTop;
            float bandBottom = -canvas.y * 0.5f + UiTiming.SafeMargin;
            float cTop = Ui_DeckScreen.PulledY + Ui_DeckScreen.DetailHeight * 0.5f;
            float cBottom = Ui_DeckScreen.PulledY - Ui_DeckScreen.DetailHeight * 0.5f;
            float s = Mathf.Min(1f, halfW / Mathf.Max(leftSpan, rightSpan));
            if (cTop > 0f) s = Mathf.Min(s, bandTop / cTop);
            if (cBottom < 0f) s = Mathf.Min(s, bandBottom / cBottom);
            _pulledRoot.localScale = Vector3.one * Mathf.Clamp(s, 0.3f, 1f);
        }

        private void Flip(int dir)
        {
            if (_view != View.Pulled || _deck.Length == 0) return;
            Art.Sfx.Play(Ui_DeckScreen.FlipSound);
            Pull(_pulledIndex + dir);
        }

        /// <summary>Every teammate as a deck pile; clicking one fans that deck out.</summary>
        private void ShowTeam()
        {
            if (Teammates().Count == 0) { ShowFan(); return; }   // the last teammate left: never a blank TEAM DECKS page
            _view = View.Team;
            _fan.gameObject.SetActive(false);
            _totalsPanel.gameObject.SetActive(false);
            _pulledRoot.gameObject.SetActive(false);
            _teamRoot.gameObject.SetActive(true);
            _title.text = Ui_DeckScreen.TeamTitle;
            _teamPrint = TeamFingerprint();
            ClearTeam();
            BuildTeam();
            RefreshSubtitle();
        }

        /// <summary>One teammate's cards, fanned out from their pile in the team view.</summary>
        private void ShowTeamDeck(int actor)
        {
            _view = View.TeamDeck;
            _teamFocus = actor;
            _fan.gameObject.SetActive(false);
            _totalsPanel.gameObject.SetActive(false);
            _pulledRoot.gameObject.SetActive(false);
            _teamRoot.gameObject.SetActive(true);
            _title.text = NetSend.NameOf(actor);
            _teamPrint = TeamFingerprint();
            ClearTeam();
            BuildTeamDeck(actor);
            RefreshSubtitle();
            Art.Sfx.Play(Ui_DeckScreen.FlipSound);
        }

        /// <summary>The Deck key cycles: your fan, the team decks, closed. A pulled card steps back into the fan first.</summary>
        private void Step()
        {
            switch (_view)
            {
                case View.Pulled: ShowFan(select: _pulledIndex); break;
                case View.Fan:
                    if (Teammates().Count > 0) ShowTeam(); else HideNow();
                    break;
                default: HideNow(); break;   // Team and TeamDeck: the third press closes
            }
        }

        /// <summary>Esc, controller cancel or a click outside, once per frame however many input paths deliver it.</summary>
        public void BackFromInput()
        {
            if (Time.frameCount == _backFrame) return;
            _backFrame = Time.frameCount;
            // Esc raises Pause and UICancel together. This runs in the Update phase; if Back() closes the window,
            // GUIManager.UpdatePaused (LateUpdate) would find it closed and open the pause menu off the same press.
            try
            {
                var local = Character.localCharacter;
                if (local != null && local.input != null) local.input.pauseWasPressed = false;
            }
            catch { }
            Back();
        }

        /// <summary>One step out: a pulled card to the fan, a teammate's cards to the team decks, anything else closes.</summary>
        private void Back()
        {
            if (_view == View.Pulled) { ShowFan(select: _pulledIndex); return; }
            if (_view == View.TeamDeck) { ShowTeam(); return; }
            HideNow();
        }

        public void HideNow()
        {
            if (!isOpen) return;
            StopAllCoroutines();
            CloseM.Invoke(this, null);
        }

        // ------------------------------------------------------------------ fan layout and animation (CardPicker's rules)
        /// <summary>Up to 6 cards per row, scaled down to fit between the subtitle band and the totals panel. Returns the column count.</summary>
        private int LayoutFan(int n)
        {
            n = Mathf.Max(1, n);
            int rows = (n + MaxPerRow - 1) / MaxPerRow;
            int cols = (n + rows - 1) / rows;
            _grid.constraintCount = cols;
            float w = cols * Ui_Card.Size.x + (cols - 1) * Ui_Fan.Spacing;
            float h = rows * Ui_Card.Size.y + (rows - 1) * Ui_Fan.Spacing;
            _fan.sizeDelta = new Vector2(w, h);

            var canvas = UiKit.CanvasSize;
            float bandTop = canvas.y * 0.5f - Ui_DeckScreen.FanBandTop;
            float bandBottom = -canvas.y * 0.5f + Ui_DeckScreen.TotalsY + Ui_DeckScreen.TotalsHeight + UiTiming.SafeMargin * 0.5f;
            float hoverRoom = UiTiming.HoverScale;
            float scale = Mathf.Min(1f, (canvas.x - 2f * UiTiming.SafeMargin) / (w * hoverRoom), (bandTop - bandBottom) / Mathf.Max(1f, h * hoverRoom));
            _fan.localScale = Vector3.one * Mathf.Max(0.05f, scale);
            _fan.anchoredPosition = new Vector2(0f, (bandTop + bandBottom) * 0.5f);
            return cols;
        }

        /// <summary>Controller navigation across the grid (CardPicker's rules).</summary>
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

        private void Reselect(int index = 0)
        {
            if (_buttons.Count == 0) return;
            index = Mathf.Clamp(index, 0, _buttons.Count - 1);
            try { UIInputHandler.SetSelectedObject(_buttons[index].gameObject); }
            catch (System.Exception e) { Plugin.Verbose("controller selection skipped: " + e.Message); }
        }

        /// <summary>The deck expands: every card flies from the pile point (the fan's centre) out to its slot.</summary>
        private IEnumerator Expand()
        {
            // the grid must have placed the slots before their positions can anchor the animation
            LayoutRebuilder.ForceRebuildLayoutImmediate(_fan);
            var starts = new Vector2[_cards.Count];
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] == null) continue;
                starts[i] = -(Vector2)_cards[i].transform.localPosition;
                var inner = _cards[i].transform.GetChild(0) as RectTransform;
                if (inner != null) { inner.anchoredPosition = starts[i]; inner.localScale = Vector3.one * 0.7f; }
            }
            float begin = Time.unscaledTime;
            float total = Ui_DeckScreen.ExpandSeconds + UiTiming.CardStaggerSeconds * _cards.Count;
            while (Time.unscaledTime - begin < total)
            {
                for (int i = 0; i < _cards.Count && i < starts.Length; i++)
                {
                    if (_cards[i] == null) continue;
                    float t = Mathf.Clamp01((Time.unscaledTime - begin - i * UiTiming.CardStaggerSeconds) / Ui_DeckScreen.ExpandSeconds);
                    float eased = 1f - (1f - t) * (1f - t);
                    var inner = _cards[i].transform.GetChild(0) as RectTransform;
                    if (inner == null) continue;
                    inner.anchoredPosition = Vector2.Lerp(starts[i], Vector2.zero, eased);
                    inner.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, eased);
                }
                yield return null;
            }
            foreach (var c in _cards)
            {
                var inner = c != null ? c.transform.GetChild(0) as RectTransform : null;
                if (inner != null) { inner.anchoredPosition = Vector2.zero; inner.localScale = Vector3.one; }
            }
        }

        // ------------------------------------------------------------------ input
        protected override void Update()
        {
            base.Update();
            if (!isOpen) return;
            // Hooks.AbilityKeys is silenced by the game's windowBlockingInput while this MenuWindow is open, so the
            // Deck key steps views from here; never on the very frame Hooks opened the window with the same press.
            if (Time.frameCount != _openedFrame && Controls.WasPressed(Controls.Act.BuildScreen)) { Step(); return; }
            if (CancelPressed()) { BackFromInput(); return; }
            if (_view == View.Pulled)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.leftArrowKey.wasPressedThisFrame) Flip(-1);
                    else if (kb.rightArrowKey.wasPressedThisFrame) Flip(+1);
                }
            }
        }

        /// <summary>PEAK's UI cancel (Escape, or B / Circle on a controller) this frame.</summary>
        private static bool CancelPressed()
        {
            try { return UIInputHandler.action_cancel != null && UIInputHandler.action_cancel.WasPressedThisFrame(); }
            catch { return false; }
        }

        private void RefreshSubtitle()
        {
            string key = Plugin.BuildScreenKeyLabel;
            switch (_view)
            {
                case View.Pulled:
                    _subtitle.text = string.Format(Ui_DeckScreen.PulledTemplate, _pulledIndex + 1, _deck.Length);
                    break;
                case View.Team:
                    _subtitle.text = string.Format(Ui_DeckScreen.TeamHint, key);
                    break;
                case View.TeamDeck:
                    _subtitle.text = string.Format(Ui_DeckScreen.TeamDeckHint, key);
                    break;
                default:
                    string hint = _deck.Length == 0 ? Ui_DeckScreen.NoCards
                        : Controls.UsingGamepad ? Ui_DeckScreen.FanHintPad : Ui_DeckScreen.FanHint;
                    string nav = Teammates().Count > 0 ? string.Format(Ui_DeckScreen.NavTeamHint, key) : Ui_DeckScreen.NavCloseHint;
                    _subtitle.text = hint + "  " + nav;
                    break;
            }
        }

        // ------------------------------------------------------------------ ALL CARDS ADDED UP (live combined stats)
        private void RefreshTotals()
        {
            if (_view != View.Fan || !_totalsPanel.gameObject.activeSelf) return;
            var lines = new List<string>();
            var s = CardSettings.Current;
            string tier = CardPresentation.TierName();
            string strengthLine = !DifficultyScale.Unscaled(s.BenefitStrength)
                ? string.Format(s.DrawbacksEnabled ? Ui_DeckScreen.StrengthLine : Ui_DeckScreen.StrengthLineNoDrawbacks, (s.BenefitStrength * 100f).ToString("0"), tier)
                : string.Format(Ui_DeckScreen.FullStrengthLine, tier);
            lines.Add(Meta + UiKit.ColorTag(Ui_DeckScreen.InactiveColor) + strengthLine + "</color></size>");
            if (!s.DrawbacksEnabled)
                lines.Add(Meta + UiKit.ColorTag(Ui_DeckScreen.InactiveColor) + Ui_DeckScreen.DrawbacksOffLine + "</color></size>");

            Effects.FoldAlways(_always);
            var stats = BuildStats.Lines(Effects.Local, _always);
            if (stats.Count == 0)
                lines.Add(UiKit.ColorTag(Ui_DeckScreen.InactiveColor) + Ui_DeckScreen.NothingNow + "</color>");
            foreach (var l in stats)
            {
                string now = BuildStats.Say(l, l.Now), always = BuildStats.Say(l, l.Always);
                if (!l.Conditional)
                    lines.Add(UiKit.ColorTag(GoodOrBad(l, l.Now)) + Mark(l, l.Now) + now + ".</color>");
                else if (now == null)
                    lines.Add(UiKit.ColorTag(Ui_DeckScreen.InactiveColor) + Mark(l, l.Always) + always + ". " + Meta + Ui_DeckScreen.OffNow + "</size></color>");
                else
                {
                    lines.Add(UiKit.ColorTag(GoodOrBad(l, l.Now)) + Mark(l, l.Now) + now + ".</color> " + Meta + UiKit.ColorTag(Ui_DeckScreen.InactiveColor) + Ui_DeckScreen.OnNow + "</color></size>");
                    lines.Add(Meta + UiKit.ColorTag(Ui_DeckScreen.InactiveColor) + "   " + Ui_DeckScreen.Otherwise + (always != null ? LowerFirst(always) : Ui_DeckScreen.NoChange) + ".</color></size>");
                }
            }
            var auras = Auras();
            if (auras.Count > 0)
            {
                lines.Add(Header + "<b>" + UiKit.ColorTag(Ui_DeckScreen.HeaderColor) + Ui_DeckScreen.AurasHeader + "</color></b></size>");
                lines.AddRange(auras);
            }
            int cap = Mathf.RoundToInt(Ui_DeckScreen.MaxTotalLines);
            if (lines.Count > cap)
            {
                int dropped = lines.Count - (cap - 1);
                lines.RemoveRange(cap - 1, dropped);
                lines.Add(Meta + UiKit.ColorTag(Ui_DeckScreen.InactiveColor) + string.Format(Ui_DeckScreen.MoreTotalsTemplate, dropped) + "</color></size>");
            }

            // two balanced columns under one header
            int half = (lines.Count + 1) / 2;
            var sb = new StringBuilder();
            sb.Append(Header).Append("<b>").Append(UiKit.ColorTag(Ui_DeckScreen.HeaderColor)).Append(Ui_DeckScreen.StatsHeader).Append("</color></b></size>\n");
            for (int i = 0; i < half; i++) sb.Append(lines[i]).Append('\n');
            _totalsLeft.text = sb.ToString().TrimEnd();
            sb.Length = 0;
            sb.Append('\n');   // the right column starts level with the left column's first line
            for (int i = half; i < lines.Count; i++) sb.Append(lines[i]).Append('\n');
            _totalsRight.text = sb.ToString().TrimEnd('\n', ' ');
        }

        /// <summary>Teammates' cards whose aura applies to the local scout right now (from the live fold).</summary>
        private static List<string> Auras()
        {
            var list = new List<string>();
            int me = NetSend.LocalActor;
            var mine = Effects.CardsOf(me);
            var seen = new HashSet<byte>();
            foreach (var m in Effects.ActiveMods)
            {
                if (mine.Contains(m.CardId) || !seen.Add(m.CardId)) continue;
                var owners = Effects.CardsByActor.Where(kv => kv.Key != me && kv.Value.Contains(m.CardId)).Select(kv => NetSend.NameOf(kv.Key));
                list.Add(UiKit.ColorTag(Ui_DeckScreen.ActiveColor) + Deck.NameOf(m.CardId) + "</color> (" + string.Join(", ", owners) + ")");
            }
            if (Effects.SharedBurdenOwner != int.MinValue && Effects.SharedBurdenOwner != me)
                list.Add(UiKit.ColorTag(Ui_DeckScreen.ActiveColor) + Deck.NameOfAbility("SharedBurden") + "</color> (" + NetSend.NameOf(Effects.SharedBurdenOwner) + "): "
                         + LowerFirst(BuildStats.Say(Effects.SharedBurdenFactor, true, null, StatWords.Of(Channel.allInjury)) ?? Ui_DeckScreen.NoChange));
            return list;
        }

        // ------------------------------------------------------------------ the pulled card's detail panel
        private void RefreshDetail(Character me)
        {
            if (_deck.Length == 0) return;
            CardDef def;
            if (!Deck.TryGet(_deck[_pulledIndex], out def)) return;
            var sb = new StringBuilder();
            Color rarity = Deck.RarityColor(def.Rarity);
            sb.Append(Header).Append(UiKit.ColorTag(rarity)).Append("■ <b>").Append(def.Name).Append("</b>  <cspace=0.08em><uppercase>")
              .Append(Deck.RarityName(def.Rarity)).Append("</uppercase></cspace></color></size>\n\n");
            sb.Append(UiKit.ColorTag(Ui_CardEffects.GoodColor)).Append(CardPresentation.Benefit(def, out string note)).Append("</color>\n");
            if (!string.IsNullOrEmpty(def.Negative) && CardSettings.Current.DrawbacksEnabled)
                sb.Append(UiKit.ColorTag(Ui_CardEffects.BadColor)).Append(CardPresentation.Text(def, def.Negative)).Append("</color>\n");
            if (note != null)
                sb.Append(Meta).Append(UiKit.ColorTag(Ui_DeckScreen.InactiveColor)).Append(note).Append("</color></size>\n");

            var conditional = ConditionalLines(def);
            if (conditional.Count > 0)
            {
                sb.Append('\n');
                foreach (var line in conditional) sb.Append(line).Append('\n');
            }

            bool anyStatus = false;
            foreach (var a in LocalCards.All)
            {
                if (a.Card == null || a.Card.Id != def.Id) continue;
                AbilityStatus status;
                try { status = a.Status(me); } catch { status = AbilityStatus.None; }
                if (status.IsEmpty) continue;
                if (!anyStatus) { sb.Append('\n'); anyStatus = true; }
                sb.Append(UiKit.ColorTag(Ui_DeckScreen.HeaderColor)).Append(status.Text).Append("</color>\n");
            }

            sb.Append('\n').Append(Meta).Append(UiKit.ColorTag(Ui_DeckScreen.InactiveColor))
              .Append(string.Format(Ui_DeckScreen.PickedTemplate, _pulledIndex + 1, _deck.Length)).Append("</color></size>");
            if (!string.IsNullOrEmpty(def.Flavor))
                sb.Append('\n').Append(Meta).Append("<i>").Append(UiKit.ColorTag(Ui_DeckScreen.InactiveColor)).Append(def.Flavor).Append("</color></i></size>");
            _detail.text = sb.ToString();
        }

        /// <summary>Each modifier with a condition or a teammate target, and whether the live fold applies it right now.</summary>
        private List<string> ConditionalLines(CardDef def)
        {
            var list = new List<string>();
            var s = CardSettings.Current;
            for (int i = 0; i < def.Mods.Length; i++)
            {
                var m = def.Mods[i];
                bool forTeam = m.Target == ModTarget.team;
                if (m.When == Generated.Condition.always && !forTeam) continue;
                var ch = ChannelTable.Defs[(int)m.Channel];
                string effect = BuildStats.Say(s.ModValue(def, i), ch.IsMul, BuildStats.UnitOf(m.Channel), StatWords.Of(m.Channel));
                if (effect == null) continue;
                string line;
                bool active;
                if (forTeam)
                {
                    line = string.Format(Ui_DeckScreen.TeammatesTemplate, s.Param(def, "radius", 6f).ToString("0.#"), LowerFirst(effect));
                    active = false;
                }
                else
                {
                    int c = (int)m.When;
                    line = effect + " while " + (c < ConditionTable.Shown.Length ? ConditionTable.Shown[c] : m.When.ToString());
                    active = IsActive(def.Id, i);
                }
                string state = forTeam ? "" : "  " + (active ? Ui_DeckScreen.OnNow : Ui_DeckScreen.OffNow);
                var color = active ? Ui_DeckScreen.ActiveColor : Ui_DeckScreen.InactiveColor;
                list.Add(Detail + UiKit.ColorTag(color) + "<indent=" + Ui_DeckScreen.DetailIndent.ToString("0") + ">· " + line + state + "</indent></color></size>");
            }
            return list;
        }

        private static bool IsActive(byte cardId, int modIndex)
        {
            foreach (var a in Effects.ActiveMods) if (a.CardId == cardId && a.ModIndex == modIndex) return true;
            return false;
        }

        // ------------------------------------------------------------------ TEAM CARDS
        /// <summary>Every other player in the room, in join order (actor number).</summary>
        private static List<int> Teammates()
        {
            var list = new List<int>();
            int me = NetSend.LocalActor;
            var players = Photon.Pun.PhotonNetwork.PlayerList;
            if (players == null) return list;
            foreach (var p in players)
                if (p != null && p.ActorNumber != me) list.Add(p.ActorNumber);
            list.Sort();
            return list;
        }

        private void ClearTeam()
        {
            _teamButtons.Clear();
            for (int i = _teamRoot.childCount - 1; i >= 0; i--)
            {
                var child = _teamRoot.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        /// <summary>One deck pile per teammate (name, count and state, Clown Luggage penalties above it), in rows of TeamPerRow.</summary>
        private void BuildTeam()
        {
            var team = Teammates();
            float scale = Ui_DeckScreen.TeamPileScale;
            float cardW = Ui_DeckHud.CardW * scale, cardH = Ui_DeckHud.CardH * scale;
            float offX = Ui_DeckHud.OffsetX * scale, offY = Ui_DeckHud.OffsetY * scale;
            int maxShown = Mathf.RoundToInt(Ui_DeckHud.MaxShown);
            int perRow = Mathf.Max(1, Mathf.RoundToInt(Ui_DeckScreen.TeamPerRow));
            float blockW = Ui_DeckScreen.TeamBlockW;
            float headerH = Ui_DeckScreen.TeamNameFontSize * 1.5f;
            float pileH = cardH + (maxShown - 1) * offY;
            float blockH = headerH + Ui_DeckScreen.TeamPileGap + pileH;
            int rows = (Mathf.Max(1, team.Count) + perRow - 1) / perRow;

            for (int i = 0; i < team.Count; i++)
            {
                int actor = team[i];
                var cards = Effects.CardsOf(actor);
                int row = i / perRow;
                int inRow = Mathf.Min(perRow, team.Count - row * perRow);
                float x = (i % perRow - (inRow - 1) * 0.5f) * (blockW + Ui_Fan.Spacing);
                float top = -row * (blockH + Ui_DeckScreen.TeamRowGap);

                var header = UiKit.Text(_teamRoot, "Name", new Vector2(blockW, headerH), Ui_DeckScreen.TeamNameFontSize, Ui_DeckScreen.HeaderColor, TextAlignmentOptions.Center, FontStyles.Bold);
                UiKit.Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(x, top));
                UiKit.AutoSize(header, 14f);
                header.text = TeamHeader(actor, cards.Count);

                if (cards.Count == 0)
                {
                    var none = UiKit.Text(_teamRoot, "None", new Vector2(blockW, 30f), Ui_DeckScreen.FontSize, Ui_DeckScreen.InactiveColor, TextAlignmentOptions.Center);
                    UiKit.Place(none.rectTransform, new Vector2(0.5f, 1f), new Vector2(x, top - headerH - Ui_DeckScreen.TeamPileGap));
                    none.text = Ui_DeckScreen.TeamNoCards;
                    continue;
                }
                // the name is the most natural click target and visually part of the block: it must open the deck,
                // not fall through to the backdrop (which closes the whole screen)
                header.raycastTarget = true;
                header.gameObject.AddComponent<BackdropClick>().OnClick = () => ShowTeamDeck(actor);
                _teamButtons.Add(Pile(actor, cards, x, top - headerH - Ui_DeckScreen.TeamPileGap, cardW, cardH, offX, offY, maxShown));
            }

            FitTeam(rows * blockH + (rows - 1) * Ui_DeckScreen.TeamRowGap,
                    Mathf.Min(Mathf.Max(1, team.Count), perRow) * (blockW + Ui_Fan.Spacing));
            if (_teamButtons.Count > 0)
            {
                try { UIInputHandler.SetSelectedObject(_teamButtons[0].gameObject); }
                catch (System.Exception e) { Plugin.Verbose("controller selection skipped: " + e.Message); }
            }
        }

        /// <summary>A teammate's deck as a pile of card backs (the HUD pile's look, larger): one button that fans it out.</summary>
        private Button Pile(int actor, List<byte> cards, float x, float top, float cardW, float cardH, float offX, float offY, int maxShown)
        {
            int shown = Mathf.Min(cards.Count, maxShown);
            float w = cardW + (shown - 1) * offX, h = cardH + (shown - 1) * offY;
            var pile = UiKit.Node(_teamRoot, "Pile", new Vector2(w, h));
            UiKit.Place(UiKit.Rect(pile), new Vector2(0.5f, 1f), new Vector2(x, top));

            // the hit area doubles as hover/selection feedback: invisible until the tint fades a faint wash in
            var hit = UiKit.Panel(pile.transform, "Hit", new Vector2(w, h) + Vector2.one * Ui_DeckScreen.TeamPileGap * 2f, Color.white, Art.Shapes.Panel);
            hit.type = Image.Type.Sliced;
            hit.raycastTarget = true;
            var b = hit.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            var colors = b.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = colors.selectedColor = new Color(1f, 1f, 1f, 0.15f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.25f);
            b.colors = colors;
            b.onClick.AddListener(() => ShowTeamDeck(actor));

            float frame = Ui_DeckHud.FrameWidth * Ui_DeckScreen.TeamPileScale;
            for (int i = 0; i < shown; i++)
            {
                byte id = cards[cards.Count - shown + i];
                CardDef def;
                var rarity = Deck.TryGet(id, out def) ? Deck.RarityColor(def.Rarity) : Color.white;
                var mini = UiKit.Panel(pile.transform, "Back", new Vector2(cardW, cardH), rarity, Art.Shapes.Panel);
                mini.type = Image.Type.Sliced;
                mini.pixelsPerUnitMultiplier = Ui_DeckHud.BackPpu;
                var rt = mini.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.anchoredPosition = new Vector2(i * offX + cardW * 0.5f, i * offY + cardH * 0.5f);
                rt.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 1f : -1f) * Ui_DeckHud.TiltDegrees * ((i % 3) + 1) / 3f);
                var back = UiKit.Panel(mini.transform, "Face", new Vector2(cardW, cardH) - Vector2.one * (frame * 2f), Ui_DeckHud.BackColor, Art.Shapes.Panel);
                back.type = Image.Type.Sliced;
                back.pixelsPerUnitMultiplier = Ui_DeckHud.BackPpu;
            }

            float chip = Ui_DeckHud.CountChipSize * Ui_DeckScreen.TeamPileScale;
            var chipImg = UiKit.Panel(pile.transform, "CountChip", Vector2.one * chip, Ui_DeckHud.CountChipColor, Art.Shapes.Get(ArtShape.circle));
            chipImg.rectTransform.anchorMin = chipImg.rectTransform.anchorMax = Vector2.zero;
            chipImg.rectTransform.anchoredPosition = new Vector2(w - 4f * Ui_DeckScreen.TeamPileScale, h - 4f * Ui_DeckScreen.TeamPileScale);
            var count = UiKit.Text(chipImg.transform, "Count", Vector2.one * chip, Ui_DeckHud.CountFontSize * Ui_DeckScreen.TeamPileScale, Ui_DeckHud.CountTextColor, TextAlignmentOptions.Center, FontStyles.Bold);
            UiKit.Place(count.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            count.text = cards.Count.ToString();
            return b;
        }

        /// <summary>One teammate's deck fanned out: their header line, then full card faces in pick order.</summary>
        private void BuildTeamDeck(int actor)
        {
            var cards = Effects.CardsOf(actor);
            float headerH = Ui_DeckScreen.TeamNameFontSize * 1.5f;
            var header = UiKit.Text(_teamRoot, "Name", new Vector2(UiKit.FitWidth(1400f), headerH), Ui_DeckScreen.TeamNameFontSize, Ui_DeckScreen.HeaderColor, TextAlignmentOptions.Center, FontStyles.Bold);
            UiKit.Place(header.rectTransform, new Vector2(0.5f, 1f), Vector2.zero);
            UiKit.AutoSize(header, 14f);
            header.text = TeamHeader(actor, cards.Count);
            float y = -headerH - Ui_DeckScreen.TeamPileGap;
            float width = 0f;

            if (cards.Count == 0)
            {
                var none = UiKit.Text(_teamRoot, "None", new Vector2(1400f, 30f), Ui_DeckScreen.FontSize, Ui_DeckScreen.InactiveColor, TextAlignmentOptions.Center);
                UiKit.Place(none.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y));
                none.text = Ui_DeckScreen.TeamNoCards;
                y -= 34f;
            }
            else
            {
                float scale = Ui_DeckScreen.TeamDeckScale;
                float cardH = Ui_Card.Size.y * scale, cardW = Ui_Card.Size.x * scale, gap = Ui_Fan.Spacing * scale;
                int cols = Mathf.Min(cards.Count, MaxPerRow);
                int rows = (cards.Count + cols - 1) / cols;
                float blockW = cols * cardW + (cols - 1) * gap;
                float blockH = rows * cardH + (rows - 1) * gap;
                width = blockW;
                var holder = UiKit.Node(_teamRoot, "Cards", new Vector2(blockW / scale, blockH / scale));
                var rect = UiKit.Rect(holder);
                rect.anchoredPosition = new Vector2(0f, y - blockH * 0.5f);
                rect.localScale = Vector3.one * scale;
                var grid = holder.AddComponent<GridLayoutGroup>();
                grid.cellSize = Ui_Card.Size;
                grid.spacing = new Vector2(Ui_Fan.Spacing, Ui_Fan.Spacing);
                grid.childAlignment = TextAnchor.MiddleCenter;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = cols;
                foreach (var id in cards) CardFace.Build(holder.transform, id, interactive: false);
                y -= blockH;
            }
            FitTeam(-y, width);
        }

        /// <summary>The team content (hanging below the root's origin) scaled to the band under the subtitle, centred in it.</summary>
        private void FitTeam(float totalH, float width)
        {
            totalH = Mathf.Max(1f, totalH);
            var canvas = UiKit.CanvasSize;
            float bandTop = canvas.y * 0.5f - Ui_DeckScreen.TeamBandTop, bandBottom = -canvas.y * 0.5f + UiTiming.SafeMargin;
            float fit = Mathf.Min(1f, (bandTop - bandBottom) / totalH, (canvas.x - 2f * UiTiming.SafeMargin) / Mathf.Max(1f, width));
            _teamRoot.localScale = Vector3.one * fit;
            _teamRoot.anchoredPosition = new Vector2(0f, bandTop - (bandTop - bandBottom - totalH * fit) * 0.5f);
        }

        private static string TeamHeader(int actor, int count)
        {
            string countText = count == 1 ? Ui_DeckScreen.CardCountOne : string.Format(Ui_DeckScreen.CardCountMany, count);
            string state = null;
            Character ch;
            if (PlayerHandler.TryGetCharacter(actor, out ch) && ch != null && ch.data != null)
                state = ch.data.dead ? Ui_DeckScreen.Ghost : (ch.data.passedOut || ch.data.fullyPassedOut) ? Ui_DeckScreen.Down : null;
            var sb = new StringBuilder();
            sb.Append(UiKit.ColorTag(Ui_DeckScreen.HeaderColor)).Append("<b>").Append(NetSend.NameOf(actor)).Append("</b></color>  ")
              .Append(Meta).Append(UiKit.ColorTag(Ui_DeckScreen.InactiveColor)).Append(countText);
            if (state != null) sb.Append(" · ").Append(state);
            var clown = Effects.DoubleDealCard();
            if (clown != null)
                foreach (var v in Effects.Penalties.Of(actor))
                {
                    string said = BuildStats.Say(Effects.PenaltyValue(clown, v), true, null, StatWords.Of(CardPenaltyRules.ChannelFor(v)));
                    if (said != null) sb.Append(" · ").Append(UiKit.ColorTag(Ui_CardEffects.BadColor)).Append(Ui_DeckScreen.PenaltyLabel).Append(LowerFirst(said)).Append("</color>");
                }
            sb.Append("</color></size>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ small text helpers (ui.json DeckScreen type roles)
        private static readonly string Header = "<size=" + Ui_DeckScreen.HeaderSizePercent.ToString("0") + "%>";
        private static readonly string Meta = "<size=" + Ui_DeckScreen.MetaSizePercent.ToString("0") + "%>";
        private static readonly string Detail = "<size=" + Ui_DeckScreen.DetailSizePercent.ToString("0") + "%>";

        private static Color GoodOrBad(StatLine l, float v) => BuildStats.Helps(v, l.IsMul, l.Words) ? Ui_CardEffects.GoodColor : Ui_CardEffects.BadColor;

        /// <summary>A + / - marker on combined stat totals, so help and harm never rest on green and red alone.</summary>
        private static string Mark(StatLine l, float v) => BuildStats.Helps(v, l.IsMul, l.Words) ? Ui_DeckScreen.GoodMark : Ui_DeckScreen.BadMark;

        private static string LowerFirst(string t) => string.IsNullOrEmpty(t) ? t : char.ToLowerInvariant(t[0]) + t.Substring(1);
    }
}
