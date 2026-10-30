using System.Collections.Generic;
using System.Text;
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
    /// <summary>ui.json WaitingBanner, DeckHud, Toast, AbilityBar, Prompt and Scavenger markers: overlays that never block input.</summary>
    public static class Hud
    {
        /// <summary>A world position marked on screen (Scavenger's Instinct).</summary>
        public struct Marker
        {
            public Vector3 World;
            public string Label;
            public Color Color;
        }

        private static GameObject _root;
        private static RectTransform _rootRect;
        private static Image _waitingPanel; private static TextMeshProUGUI _waitingText, _waitingOrder;
        /// <summary>The deck pile (mini card backs), its count chip, and the deck-key hint underneath (ui.json DeckHud).</summary>
        private static RectTransform _pile, _pileCards;
        private static Image _deckChip; private static TextMeshProUGUI _deckCount;
        private static TextMeshProUGUI _hint; private static Image _hintPanel;
        private static Image _toastPanel; private static TextMeshProUGUI _toastText; private static float _toastUntil;
        private static TextMeshProUGUI _abilityBar; private static Image _abilityPanel;
        /// <summary>The input device the hint labels were last written for (they name a key or a controller button).</summary>
        private static bool _labelsForGamepad;
        private static Image _promptPanel; private static TextMeshProUGUI _promptText;
        private static RectTransform _markerLayer;
        private static readonly List<TextMeshProUGUI> _markerPool = new List<TextMeshProUGUI>();
        private static readonly List<Marker> _markers = new List<Marker>();

        /// <summary>Where the deck HUD starts above the bottom edge (ui.json DeckHud).</summary>
        private static readonly float DeckBottom = Ui_DeckHud.Y;

        /// <summary>The top stack, each slot StackGap under the one above, so the three never touch (ui.json WaitingBanner).</summary>
        private static readonly float BannerTop = Ui_WaitingBanner.Top;
        private static readonly float ToastTop = BannerTop + Ui_WaitingBanner.Size.y + Ui_WaitingBanner.StackGap;
        private static readonly float PromptTop = ToastTop + Ui_Toast.Size.y + Ui_WaitingBanner.StackGap;

        public static void Ensure()
        {
            if (_root != null) return;
            _root = new GameObject("CampfireCardsHud", typeof(RectTransform));
            _root.transform.SetParent(UiKit.Canvas.transform, false);
            _rootRect = UiKit.Rect(_root);
            UiKit.Stretch(_rootRect);

            // markers sit underneath every panel
            var layer = new GameObject("Markers", typeof(RectTransform));
            layer.transform.SetParent(_root.transform, false);
            _markerLayer = UiKit.Rect(layer);
            UiKit.Stretch(_markerLayer);

            _waitingPanel = UiKit.Panel(_root.transform, "WaitingBanner", Ui_WaitingBanner.Size, Ui_WaitingBanner.Panel);
            UiKit.Place(_waitingPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -BannerTop));
            var line = new Vector2(Ui_WaitingBanner.Size.x - 24f, Ui_WaitingBanner.Size.y * 0.5f);
            _waitingText = UiKit.Text(_waitingPanel.transform, "Text", line, Ui_WaitingBanner.FontSize, Ui_WaitingBanner.Color, TextAlignmentOptions.Center, FontStyles.Bold);
            UiKit.Place(_waitingText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -2f));
            UiKit.AutoSize(_waitingText, Ui_WaitingBanner.MinFontSize);   // long player names shrink before they are cut
            _waitingOrder = UiKit.Text(_waitingPanel.transform, "Order", line, Ui_WaitingBanner.OrderFontSize, Ui_WaitingBanner.Color, TextAlignmentOptions.Center);
            UiKit.Place(_waitingOrder.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 2f));
            UiKit.AutoSize(_waitingOrder, Mathf.Min(Ui_WaitingBanner.MinFontSize, Ui_WaitingBanner.OrderFontSize));
            _waitingPanel.gameObject.SetActive(false);

            // the deck corner sits straight on the world: one hint line on a dark panel fitted to the text (readable on
            // snow), the pile of card backs above it, both above the status icons over the stamina bar (ui.json DeckHud).
            // The combined stats live in the DeckScreen - a corner list could only ever show a slice and read as broken.
            _hintPanel = UiKit.Panel(_root.transform, "DeckHintPanel", Vector2.zero, Ui_DeckHud.Panel, Art.Shapes.Panel);
            _hintPanel.type = Image.Type.Sliced;
            _hint = UiKit.Text(_root.transform, "DeckHint", new Vector2(Ui_DeckHud.Width, Ui_DeckHud.FontSize * 1.5f), Ui_DeckHud.FontSize, Ui_DeckHud.HintColor, TextAlignmentOptions.BottomLeft);
            UiKit.Place(_hint.rectTransform, new Vector2(0f, 0f), new Vector2(Ui_DeckHud.X + Ui_DeckHud.Padding, DeckBottom + Ui_DeckHud.Padding));
            UiKit.AutoSize(_hint, Ui_DeckHud.MinFontSize);

            var pileGo = new GameObject("DeckPile", typeof(RectTransform));
            pileGo.transform.SetParent(_root.transform, false);
            _pile = UiKit.Rect(pileGo);
            _pile.anchorMin = _pile.anchorMax = _pile.pivot = Vector2.zero;
            var cardsGo = new GameObject("Cards", typeof(RectTransform));
            cardsGo.transform.SetParent(_pile, false);
            _pileCards = UiKit.Rect(cardsGo);
            UiKit.Stretch(_pileCards);
            // an invisible stretched image makes the whole pile one button: a click (whenever a cursor is visible) opens the deck
            var hit = UiKit.Panel(_pile, "Hit", Vector2.zero, new Color(1f, 1f, 1f, 0f));
            UiKit.Stretch(hit.rectTransform);
            hit.raycastTarget = true;
            var pileButton = hit.gameObject.AddComponent<Button>();
            pileButton.transition = Selectable.Transition.None;
            // cursor-click affordance only: out of the gamepad navigation graph, or stick-down from the DeckScreen's
            // team piles lands on this invisible button and confirm closes the open screen
            pileButton.navigation = new Navigation { mode = Navigation.Mode.None };
            pileButton.onClick.AddListener(DeckScreen.ToggleFromHud);
            _deckChip = UiKit.Panel(_pile, "CountChip", Vector2.one * Ui_DeckHud.CountChipSize, Ui_DeckHud.CountChipColor, Art.Shapes.Get(ArtShape.circle));
            _deckChip.rectTransform.anchorMin = _deckChip.rectTransform.anchorMax = Vector2.zero;
            _deckCount = UiKit.Text(_deckChip.transform, "Count", Vector2.one * Ui_DeckHud.CountChipSize, Ui_DeckHud.CountFontSize, Ui_DeckHud.CountTextColor, TextAlignmentOptions.Center, FontStyles.Bold);
            UiKit.Place(_deckCount.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            ShowDeck(false);

            _abilityPanel = UiKit.Panel(_root.transform, "AbilityPanel", Vector2.zero, Ui_AbilityBar.Panel, Art.Shapes.Panel);
            _abilityPanel.type = Image.Type.Sliced;
            _abilityBar = UiKit.Text(_root.transform, "AbilityBar", Ui_AbilityBar.Size, Ui_AbilityBar.FontSize, Ui_AbilityBar.Color, TextAlignmentOptions.BottomRight);
            UiKit.Place(_abilityBar.rectTransform, new Vector2(1f, 0f), new Vector2(Ui_AbilityBar.X - Ui_AbilityBar.Padding, Ui_AbilityBar.Y + Ui_AbilityBar.Padding));
            UiKit.LineHeight(_abilityBar, Ui_AbilityBar.LineHeight);
            UiKit.AutoSize(_abilityBar, Ui_AbilityBar.MinFontSize);
            ShowCorner(_abilityBar, _abilityPanel, false);

            _toastPanel = UiKit.Panel(_root.transform, "Toast", Ui_Toast.Size, Ui_Toast.Panel);
            UiKit.Place(_toastPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -ToastTop));
            _toastText = UiKit.Text(_toastPanel.transform, "Text", Ui_Toast.Size, Ui_Toast.FontSize, Ui_Toast.Color, TextAlignmentOptions.Center);
            _toastText.margin = new Vector4(Ui_Toast.Padding, 0f, Ui_Toast.Padding, 0f);
            UiKit.AutoSize(_toastText, Ui_Toast.MinFontSize);
            _toastPanel.gameObject.SetActive(false);

            _promptPanel = UiKit.Panel(_root.transform, "Prompt", Ui_Prompt.Size, Ui_Prompt.Panel);
            UiKit.Place(_promptPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -PromptTop));
            _promptText = UiKit.Text(_promptPanel.transform, "Text", Ui_Prompt.Size, Ui_Prompt.FontSize, Ui_Prompt.Color, TextAlignmentOptions.Center);
            _promptText.margin = new Vector4(Ui_Prompt.Padding, 0f, Ui_Prompt.Padding, 0f);
            UiKit.AutoSize(_promptText, Ui_Prompt.MinFontSize);
            _promptPanel.gameObject.SetActive(false);

            _root.AddComponent<HudTicker>();
            RefreshMyCards();
        }

        /// <summary>The pick status banner: who is choosing (and the time left), and below it the pick order.</summary>
        public static void ShowWaiting(string status, string order)
        {
            Ensure();
            UiKit.FitBanner(_waitingPanel, Ui_WaitingBanner.Size.x, _waitingText, _waitingOrder);
            _waitingText.text = status;
            _waitingOrder.text = order ?? "";
            _waitingPanel.gameObject.SetActive(true);
        }

        public static void HideWaiting()
        {
            if (_waitingPanel != null) _waitingPanel.gameObject.SetActive(false);
        }

        /// <summary>A notice for toastSeconds, or longer (warningToastSeconds) when it asks the players to act.</summary>
        public static void Toast(string message, bool warning = false)
        {
            Ensure();
            UiKit.FitBanner(_toastPanel, Ui_Toast.Size.x, _toastText);
            _toastText.text = message;
            _toastPanel.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + (warning ? UiTiming.WarningToastSeconds : UiTiming.ToastSeconds);
            Plugin.Verbose("Toast: " + message);
        }

        public static void ShowPrompt(string message)
        {
            Ensure();
            UiKit.FitBanner(_promptPanel, Ui_Prompt.Size.x, _promptText);
            _promptText.text = message;
            _promptPanel.gameObject.SetActive(true);
        }

        public static void HidePrompt()
        {
            if (_promptPanel != null) _promptPanel.gameObject.SetActive(false);
        }

        /// <summary>A corner list and its backing panel: shown together, the panel fitted around the text.</summary>
        private static void ShowCorner(TextMeshProUGUI text, Image panel, bool show)
        {
            text.gameObject.SetActive(show);
            if (show) UiKit.FitBacking(panel, text, Ui_AbilityBar.Padding);
            else panel.gameObject.SetActive(false);
        }

        /// <summary>The deck corner shown or hidden as one: the hint line (panel fitted around it) and the pile above.</summary>
        private static void ShowDeck(bool show)
        {
            if (_hint == null) return;
            _hint.gameObject.SetActive(show);
            if (show) UiKit.FitBacking(_hintPanel, _hint, Ui_DeckHud.Padding);
            else _hintPanel.gameObject.SetActive(false);
            _pile.gameObject.SetActive(show);
        }

        /// <summary>The deck pile (newest card on top) and the deck-key hint under it. Rebuilt when the collection changes.</summary>
        public static void RefreshMyCards()
        {
            if (_hint == null) return;
            _labelsForGamepad = Controls.UsingGamepad;
            var cards = Effects.CardsOf(NetSend.LocalActor);
            if (cards.Count == 0) { ShowDeck(false); return; }

            _hint.text = string.Format(Ui_DeckHud.HintTemplate, Plugin.BuildScreenKeyLabel);
            ShowDeck(RunState.OnIsland);

            // the pile: up to MaxShown card backs cascading up-right, framed in their rarity colour, newest on top
            for (int i = _pileCards.childCount - 1; i >= 0; i--) Object.Destroy(_pileCards.GetChild(i).gameObject);
            int shown = Mathf.Min(cards.Count, Mathf.RoundToInt(Ui_DeckHud.MaxShown));
            float w = Ui_DeckHud.CardW + (shown - 1) * Ui_DeckHud.OffsetX;
            float h = Ui_DeckHud.CardH + (shown - 1) * Ui_DeckHud.OffsetY;
            _pile.sizeDelta = new Vector2(w, h);
            float panelH = _hintPanel.gameObject.activeSelf ? _hintPanel.rectTransform.sizeDelta.y : 0f;
            _pile.anchoredPosition = new Vector2(Ui_DeckHud.X, DeckBottom + panelH + Ui_DeckHud.PileGap);
            for (int i = 0; i < shown; i++)
            {
                byte id = cards[cards.Count - shown + i];
                CardDef def;
                var rarity = Deck.TryGet(id, out def) ? Deck.RarityColor(def.Rarity) : Color.white;
                var mini = UiKit.Panel(_pileCards, "Back", new Vector2(Ui_DeckHud.CardW, Ui_DeckHud.CardH), rarity, Art.Shapes.Panel);
                mini.type = Image.Type.Sliced;
                mini.pixelsPerUnitMultiplier = Ui_DeckHud.BackPpu;
                var rt = mini.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.anchoredPosition = new Vector2(i * Ui_DeckHud.OffsetX + Ui_DeckHud.CardW * 0.5f, i * Ui_DeckHud.OffsetY + Ui_DeckHud.CardH * 0.5f);
                rt.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 1f : -1f) * Ui_DeckHud.TiltDegrees * ((i % 3) + 1) / 3f);
                var back = UiKit.Panel(mini.transform, "Face", new Vector2(Ui_DeckHud.CardW, Ui_DeckHud.CardH) - Vector2.one * (Ui_DeckHud.FrameWidth * 2f), Ui_DeckHud.BackColor, Art.Shapes.Panel);
                back.type = Image.Type.Sliced;
                back.pixelsPerUnitMultiplier = Ui_DeckHud.BackPpu;
                var logo = Art.Logo.Sprite;
                if (logo != null)
                {
                    var stamp = UiKit.Panel(mini.transform, "Logo", Vector2.one * (Ui_DeckHud.CardW * Ui_DeckHud.LogoFrac), Color.white, logo);
                    stamp.preserveAspect = true;
                }
            }
            _deckChip.rectTransform.anchoredPosition = new Vector2(w - 4f, h - 4f);
            _deckChip.transform.SetAsLastSibling();
            _deckCount.text = cards.Count.ToString();
        }

        /// <summary>One line per owned card that has a state to show (charges, cooldowns, activation keys).</summary>
        public static void RefreshAbilityBar(Character me)
        {
            if (_abilityBar == null) return;
            var sb = new StringBuilder();
            foreach (var a in LocalCards.All)
            {
                AbilityStatus status;
                try { status = a.Status(me); } catch { status = AbilityStatus.None; }
                if (status.IsEmpty) continue;
                sb.Append(UiKit.ColorTag(status.Ready ? Ui_AbilityBar.ReadyColor : Ui_AbilityBar.WaitColor)).Append(status.Text).Append("</color>\n");
            }
            string text = sb.ToString().TrimEnd();
            bool show = text.Length > 0 && RunState.OnIsland;
            if (text == _abilityBar.text && show == _abilityBar.gameObject.activeSelf) return;   // unchanged: no re-layout
            _abilityBar.text = text;
            // grows with its lines (up to MaxHeight) before the text shrinks, then the panel is fitted around it
            float height = Mathf.Clamp(_abilityBar.GetPreferredValues(text, Ui_AbilityBar.Size.x, Mathf.Infinity).y + 8f, Ui_AbilityBar.Size.y, Ui_AbilityBar.MaxHeight);
            _abilityBar.rectTransform.sizeDelta = new Vector2(Ui_AbilityBar.Size.x, height);
            ShowCorner(_abilityBar, _abilityPanel, show);
        }

        // ------------------------------------------------------------------ markers
        public static void SetMarkers(List<Marker> markers)
        {
            _markers.Clear();
            if (markers != null) _markers.AddRange(markers);
        }

        public static void ClearMarkers()
        {
            _markers.Clear();
            foreach (var t in _markerPool) if (t != null) t.gameObject.SetActive(false);
        }

        private static void DrawMarkers()
        {
            if (_markerLayer == null) return;
            Camera cam = null;
            try { cam = MainCamera.instance != null ? MainCamera.instance.GetComponent<Camera>() : null; } catch { }
            if (cam == null) cam = Camera.main;
            var me = Character.localCharacter;
            int used = 0;
            if (cam != null && me != null && RunState.OnIsland)
            {
                foreach (var m in _markers)
                {
                    Vector3 sp = cam.WorldToScreenPoint(m.World);
                    if (sp.z <= 0.1f) continue;
                    Vector2 local;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_markerLayer, sp, null, out local)) continue;
                    var t = MarkerText(used++);
                    t.rectTransform.anchoredPosition = local;
                    int meters = Mathf.RoundToInt(Vector3.Distance(m.World, me.Center));
                    t.text = UiKit.ColorTag(m.Color) + string.Format(Ui_Marker.Template, m.Label, meters) + "</color>";
                    t.gameObject.SetActive(true);
                }
            }
            for (int i = used; i < _markerPool.Count; i++) if (_markerPool[i] != null) _markerPool[i].gameObject.SetActive(false);
        }

        private static TextMeshProUGUI MarkerText(int i)
        {
            while (_markerPool.Count <= i)
            {
                var t = UiKit.Text(_markerLayer, "Marker", Ui_Marker.Size, Ui_Marker.FontSize, Ui_Marker.Color, TextAlignmentOptions.Center, FontStyles.Bold);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                t.raycastTarget = false;
                UiKit.TryOutline(t, Ui_Marker.Outline);   // markers float over any terrain, snow and sky included
                _markerPool.Add(t);
            }
            return _markerPool[i];
        }

        public static void CloseAll()
        {
            HideWaiting();
            HidePrompt();
            CardPicker.HideAll();
            DeckScreen.HideIfOpen();
            BugReportScreen.HideIfOpen();
            ClearMarkers();
            if (_toastPanel != null) _toastPanel.gameObject.SetActive(false);
            ShowDeck(false);
            if (_abilityBar != null) ShowCorner(_abilityBar, _abilityPanel, false);
        }

        internal static void TickTimers()
        {
            if (_toastPanel != null && _toastPanel.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toastPanel.gameObject.SetActive(false);
            if (!RunState.OnIsland)
            {
                if (_hint != null && _hint.gameObject.activeSelf) ShowDeck(false);
                if (_abilityBar != null && _abilityBar.gameObject.activeSelf) ShowCorner(_abilityBar, _abilityPanel, false);
            }
            else if (_hint != null && Controls.UsingGamepad != _labelsForGamepad) RefreshMyCards();   // the hint names the other device's button
            DrawMarkers();
        }
    }

    public class HudTicker : MonoBehaviour
    {
        private void LateUpdate() { Hud.TickTimers(); }
    }
}
