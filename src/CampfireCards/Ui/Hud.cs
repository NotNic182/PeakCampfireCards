using System.Collections.Generic;
using System.Text;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Generated;
using CampfireCards.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CampfireCards.Ui
{
    /// <summary>ui.json WaitingBanner, MyCardsHud, Toast, AbilityBar, Prompt and Scavenger markers: overlays that never block input.</summary>
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
        private static TextMeshProUGUI _myCards; private static Image _myCardsPanel;
        private static Image _toastPanel; private static TextMeshProUGUI _toastText; private static float _toastUntil;
        private static TextMeshProUGUI _abilityBar; private static Image _abilityPanel;
        /// <summary>The input device the hint labels were last written for (they name a key or a controller button).</summary>
        private static bool _labelsForGamepad;
        private static Image _promptPanel; private static TextMeshProUGUI _promptText;
        private static RectTransform _markerLayer;
        private static readonly List<TextMeshProUGUI> _markerPool = new List<TextMeshProUGUI>();
        private static readonly List<Marker> _markers = new List<Marker>();

        /// <summary>Where YOUR CARDS starts above the bottom edge (ui.json MyCardsHud).</summary>
        private static readonly float MyCardsBottom = Ui_MyCardsHud.Y;

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

            // the corner lists sit straight on the world: a dark panel fitted to the text keeps them readable on snow
            _myCardsPanel = UiKit.Panel(_root.transform, "MyCardsPanel", Vector2.zero, Ui_MyCardsHud.Panel, Art.Shapes.Panel);
            _myCardsPanel.type = Image.Type.Sliced;
            _myCards = UiKit.Text(_root.transform, "MyCardsHud", Ui_MyCardsHud.Size, Ui_MyCardsHud.FontSize, Ui_MyCardsHud.Color, TextAlignmentOptions.BottomLeft);
            UiKit.Place(_myCards.rectTransform, new Vector2(0f, 0f), new Vector2(Ui_MyCardsHud.X + Ui_MyCardsHud.Padding, MyCardsBottom + Ui_MyCardsHud.Padding));   // panel corner stays above the status icons over the bar
            UiKit.LineHeight(_myCards, Ui_MyCardsHud.LineHeight);
            UiKit.AutoSize(_myCards, Ui_MyCardsHud.MinFontSize);
            ShowCorner(_myCards, _myCardsPanel, false);

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
            if (show) UiKit.FitBacking(panel, text, text == _myCards ? Ui_MyCardsHud.Padding : Ui_AbilityBar.Padding);
            else panel.gameObject.SetActive(false);
        }

        /// <summary>"YOUR CARDS": one line per card with a dot in its rarity colour.</summary>
        public static void RefreshMyCards()
        {
            if (_myCards == null) return;
            _labelsForGamepad = Controls.UsingGamepad;
            var cards = Effects.CardsOf(NetSend.LocalActor);
            if (cards.Count == 0) { ShowCorner(_myCards, _myCardsPanel, false); return; }
            // on a short canvas (big UI scale, 32:9) stay below the prompt: the list shrinks, then cuts, rather than overlap
            float room = UiKit.CanvasSize.y - MyCardsBottom - (PromptTop + Ui_Prompt.Size.y);
            _myCards.rectTransform.sizeDelta = new Vector2(Ui_MyCardsHud.Size.x, Mathf.Clamp(room, Ui_MyCardsHud.FontSize * 6f, Ui_MyCardsHud.Size.y));
            var sb = new StringBuilder();
            sb.Append("<b>").Append(Ui_MyCardsHud.Header).Append("</b>\n");
            foreach (var id in cards)
            {
                CardDef def;
                var color = Deck.TryGet(id, out def) ? Deck.RarityColor(def.Rarity) : Color.white;
                sb.Append(UiKit.ColorTag(color)).Append("■</color> ").Append(Deck.NameOf(id)).Append('\n');
            }
            sb.Append("<size=80%>").Append(UiKit.ColorTag(Ui_MyCardsHud.HintColor))
              .Append(string.Format(Ui_MyCardsHud.HintTemplate, Plugin.BuildScreenKeyLabel)).Append("</color></size>");
            _myCards.text = sb.ToString().TrimEnd();
            ShowCorner(_myCards, _myCardsPanel, RunState.OnIsland);
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
            BuildScreen.Hide();
            ClearMarkers();
            if (_toastPanel != null) _toastPanel.gameObject.SetActive(false);
            if (_myCards != null) ShowCorner(_myCards, _myCardsPanel, false);
            if (_abilityBar != null) ShowCorner(_abilityBar, _abilityPanel, false);
        }

        internal static void TickTimers()
        {
            if (_toastPanel != null && _toastPanel.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toastPanel.gameObject.SetActive(false);
            if (!RunState.OnIsland)
            {
                if (_myCards != null && _myCards.gameObject.activeSelf) ShowCorner(_myCards, _myCardsPanel, false);
                if (_abilityBar != null && _abilityBar.gameObject.activeSelf) ShowCorner(_abilityBar, _abilityPanel, false);
            }
            else if (_myCards != null && Controls.UsingGamepad != _labelsForGamepad) RefreshMyCards();   // the hint names the other device's button
            DrawMarkers();
        }
    }

    public class HudTicker : MonoBehaviour
    {
        private void LateUpdate() { Hud.TickTimers(); }
    }
}
