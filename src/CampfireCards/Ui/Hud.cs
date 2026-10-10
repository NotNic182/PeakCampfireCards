using System.Collections.Generic;
using System.Text;
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
        private static Image _waitingPanel; private static TextMeshProUGUI _waitingText;
        private static TextMeshProUGUI _myCards;
        private static Image _toastPanel; private static TextMeshProUGUI _toastText; private static float _toastUntil;
        private static TextMeshProUGUI _abilityBar;
        private static Image _promptPanel; private static TextMeshProUGUI _promptText;
        private static RectTransform _markerLayer;
        private static readonly List<TextMeshProUGUI> _markerPool = new List<TextMeshProUGUI>();
        private static readonly List<Marker> _markers = new List<Marker>();

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
            UiKit.Place(_waitingPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -120f));
            _waitingText = UiKit.Text(_waitingPanel.transform, "Text", Ui_WaitingBanner.Size, Ui_WaitingBanner.FontSize, Ui_WaitingBanner.Color, TextAlignmentOptions.Center, FontStyles.Bold);
            _waitingPanel.gameObject.SetActive(false);

            _myCards = UiKit.Text(_root.transform, "MyCardsHud", Ui_MyCardsHud.Size, Ui_MyCardsHud.FontSize, Ui_MyCardsHud.Color, TextAlignmentOptions.BottomLeft);
            UiKit.Place(_myCards.rectTransform, new Vector2(0f, 0f), new Vector2(20f, 160f));
            _myCards.enableAutoSizing = true; _myCards.fontSizeMin = 10f; _myCards.fontSizeMax = Ui_MyCardsHud.FontSize;
            _myCards.gameObject.SetActive(false);

            _abilityBar = UiKit.Text(_root.transform, "AbilityBar", Ui_AbilityBar.Size, Ui_AbilityBar.FontSize, Ui_AbilityBar.Color, TextAlignmentOptions.BottomRight);
            UiKit.Place(_abilityBar.rectTransform, new Vector2(1f, 0f), new Vector2(-24f, 150f));
            _abilityBar.gameObject.SetActive(false);

            _toastPanel = UiKit.Panel(_root.transform, "Toast", Ui_Toast.Size, Ui_Toast.Panel);
            UiKit.Place(_toastPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f));
            _toastText = UiKit.Text(_toastPanel.transform, "Text", Ui_Toast.Size, Ui_Toast.FontSize, Ui_Toast.Color, TextAlignmentOptions.Center);
            _toastPanel.gameObject.SetActive(false);

            _promptPanel = UiKit.Panel(_root.transform, "Prompt", Ui_Prompt.Size, Ui_Prompt.Panel);
            UiKit.Place(_promptPanel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -260f));
            _promptText = UiKit.Text(_promptPanel.transform, "Text", Ui_Prompt.Size, Ui_Prompt.FontSize, Ui_Prompt.Color, TextAlignmentOptions.Center);
            _promptPanel.gameObject.SetActive(false);

            _root.AddComponent<HudTicker>();
            RefreshMyCards();
        }

        public static void ShowWaiting(string playerName)
        {
            Ensure();
            _waitingText.text = string.Format(Ui_WaitingBanner.Template, playerName);
            _waitingPanel.gameObject.SetActive(true);
        }

        public static void HideWaiting()
        {
            if (_waitingPanel != null) _waitingPanel.gameObject.SetActive(false);
        }

        public static void Toast(string message)
        {
            Ensure();
            _toastText.text = message;
            _toastPanel.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + UiTiming.ToastSeconds;
            Plugin.Verbose("Toast: " + message);
        }

        public static void ShowPrompt(string message)
        {
            Ensure();
            _promptText.text = message;
            _promptPanel.gameObject.SetActive(true);
        }

        public static void HidePrompt()
        {
            if (_promptPanel != null) _promptPanel.gameObject.SetActive(false);
        }

        /// <summary>"YOUR CARDS": one line per card with a dot in its rarity colour.</summary>
        public static void RefreshMyCards()
        {
            if (_myCards == null) return;
            var cards = Effects.CardsOf(NetSend.LocalActor);
            if (cards.Count == 0) { _myCards.gameObject.SetActive(false); return; }
            var sb = new StringBuilder();
            sb.Append("<b>").Append(Ui_MyCardsHud.Header).Append("</b>\n");
            foreach (var id in cards)
            {
                CardDef def;
                var color = Deck.TryGet(id, out def) ? Deck.RarityColor(def.Rarity) : Color.white;
                sb.Append("<color=#").Append(UiKit.Hex(color)).Append(">■</color> ").Append(Deck.NameOf(id)).Append('\n');
            }
            _myCards.text = sb.ToString().TrimEnd();
            _myCards.gameObject.SetActive(RunState.OnIsland);
        }

        /// <summary>One line per owned card that has a state to show (charges, cooldowns, activation keys).</summary>
        public static void RefreshAbilityBar(Character me)
        {
            if (_abilityBar == null) return;
            var sb = new StringBuilder();
            foreach (var a in LocalCards.All)
            {
                string line;
                try { line = a.Status(me); } catch { line = null; }
                if (string.IsNullOrEmpty(line)) continue;
                bool ready = line.EndsWith("ready") || line.EndsWith("place anchor") || line.EndsWith("boosting");
                sb.Append("<color=#").Append(UiKit.Hex(ready ? Ui_AbilityBar.ReadyColor : Ui_AbilityBar.WaitColor)).Append('>').Append(line).Append("</color>\n");
            }
            string text = sb.ToString().TrimEnd();
            _abilityBar.text = text;
            _abilityBar.gameObject.SetActive(text.Length > 0 && RunState.OnIsland);
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
                    t.text = "<color=#" + UiKit.Hex(m.Color) + ">■ " + m.Label + "  " + meters + "m</color>";
                    t.gameObject.SetActive(true);
                }
            }
            for (int i = used; i < _markerPool.Count; i++) if (_markerPool[i] != null) _markerPool[i].gameObject.SetActive(false);
        }

        private static TextMeshProUGUI MarkerText(int i)
        {
            while (_markerPool.Count <= i)
            {
                var t = UiKit.Text(_markerLayer, "Marker", new Vector2(320f, 28f), 17f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                t.raycastTarget = false;
                _markerPool.Add(t);
            }
            return _markerPool[i];
        }

        public static void CloseAll()
        {
            HideWaiting();
            HidePrompt();
            CardPicker.HideAll();
            ClearMarkers();
            if (_toastPanel != null) _toastPanel.gameObject.SetActive(false);
            if (_myCards != null) _myCards.gameObject.SetActive(false);
            if (_abilityBar != null) _abilityBar.gameObject.SetActive(false);
        }

        internal static void TickTimers()
        {
            if (_toastPanel != null && _toastPanel.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toastPanel.gameObject.SetActive(false);
            if (!RunState.OnIsland)
            {
                if (_myCards != null && _myCards.gameObject.activeSelf) _myCards.gameObject.SetActive(false);
                if (_abilityBar != null && _abilityBar.gameObject.activeSelf) _abilityBar.gameObject.SetActive(false);
            }
            DrawMarkers();
        }
    }

    public class HudTicker : MonoBehaviour
    {
        private void LateUpdate() { Hud.TickTimers(); }
    }
}
