using System.Linq;
using PeakRounds.Generated;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakRounds.Ui
{
    /// <summary>Small helpers for building the runtime uGUI described in design/ui.json.</summary>
    public static class UiKit
    {
        private static Canvas _canvas;
        private static TMP_FontAsset _font;

        public static Canvas Canvas
        {
            get
            {
                if (_canvas != null) return _canvas;
                var go = new GameObject("PeakRoundsCanvas");
                Object.DontDestroyOnLoad(go);
                _canvas = go.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = UiTiming.SortingOrder;
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = UiTiming.ReferenceResolution;
                scaler.matchWidthOrHeight = 0.5f;
                go.AddComponent<GraphicRaycaster>();
                return _canvas;
            }
        }

        public static TMP_FontAsset Font
        {
            get
            {
                if (_font != null) return _font;
                try { _font = TMP_Settings.defaultFontAsset; } catch { }
                if (_font == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                    _font = all.FirstOrDefault(f => f != null && f.name.IndexOf("SDF", System.StringComparison.OrdinalIgnoreCase) >= 0) ?? all.FirstOrDefault();
                }
                Plugin.Log.LogInfo("UI font: " + (_font != null ? _font.name : "none"));
                return _font;
            }
        }

        public static RectTransform Rect(GameObject go) => go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();

        public static GameObject Node(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = Rect(go);
            rt.sizeDelta = size;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        /// <summary>anchor: (0..1, 0..1) point on the parent; pos: offset from it.</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
        }

        public static Image Panel(Transform parent, string name, Vector2 size, Color color, Sprite sprite = null)
        {
            var go = Node(parent, name, size);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, Vector2 size, float fontSize, Color color, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
        {
            var go = Node(parent, name, size);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (Font != null) t.font = Font;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        /// <summary>TMP outlines need an instanced font material; skip quietly when the font has none yet.</summary>
        public static void TryOutline(TextMeshProUGUI t, float width)
        {
            try
            {
                if (t == null || t.font == null || t.font.material == null) return;
                t.outlineWidth = width;
                t.outlineColor = Color.black;
            }
            catch (System.Exception e) { Plugin.Verbose("outline skipped: " + e.Message); }
        }
    }
}
