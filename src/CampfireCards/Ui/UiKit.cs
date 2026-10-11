using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampfireCards.Generated;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace CampfireCards.Ui
{
    /// <summary>Keeps the canvas scaler on the current UI scale when the resolution or the config changes.</summary>
    public class CanvasFit : MonoBehaviour
    {
        private Vector2 _applied;

        public void Apply()
        {
            var scaler = GetComponent<CanvasScaler>();
            var want = UiKit.ScaledReference;
            if (scaler == null || want == _applied) return;
            scaler.referenceResolution = want;
            _applied = want;
        }

        private void Update() => Apply();
    }

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
                var go = new GameObject("CampfireCardsCanvas");
                Object.DontDestroyOnLoad(go);
                _canvas = go.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = UiTiming.SortingOrder;
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.matchWidthOrHeight = 0.5f;
                go.AddComponent<GraphicRaycaster>();
                go.AddComponent<CanvasFit>().Apply();
                return _canvas;
            }
        }

        /// <summary>
        /// The UI scale in use: config General.UiScale, or for 0 (auto) 1 and smallScreenScale on screens at most
        /// smallScreenHeight pixels tall (ui.json canvas).
        /// </summary>
        public static float Scale
        {
            get
            {
                float s = Plugin.UiScale != null ? Plugin.UiScale.Value : 0f;
                if (s <= 0f) return Screen.height <= UiTiming.SmallScreenHeight ? UiTiming.SmallScreenScale : 1f;
                return Mathf.Clamp(s, UiTiming.MinUiScale, UiTiming.MaxUiScale);
            }
        }

        /// <summary>The reference resolution the scaler works against: 1920x1080 shrunk by the UI scale, so everything grows.</summary>
        public static Vector2 ScaledReference => UiTiming.ReferenceResolution / Scale;

        /// <summary>
        /// The canvas in reference units right now (what fits on screen). Computed like CanvasScaler (match 0.5, the
        /// geometric mean of the width and height ratios), so it is right even before the scaler's first update.
        /// </summary>
        public static Vector2 CanvasSize
        {
            get
            {
                var r = ScaledReference;
                float factor = Mathf.Sqrt((Screen.width / r.x) * (Screen.height / r.y));
                return factor > 0f ? new Vector2(Screen.width, Screen.height) / factor : r;
            }
        }

        /// <summary>
        /// Every mod text uses ui.json's font (UiTiming.FontFile, next to the DLL), with the game's font as the fallback
        /// for any glyph it lacks (player names). Without the file, or with General.CustomFont off, the game's font.
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (_font != null) return _font;
                var game = GameFont();
                if (!_customTried && Plugin.CustomFont != null && Plugin.CustomFont.Value)
                {
                    _customTried = true;
                    _custom = LoadFont(Path.Combine(Plugin.PluginDir ?? "", UiTiming.FontFile), game);
                }
                _font = _custom ?? game;
                Plugin.Log.LogInfo("UI font: " + (_font != null ? _font.name : "none"));
                return _font;
            }
        }

        private static TMP_FontAsset _custom;
        private static bool _customTried;

        private static TMP_FontAsset GameFont()
        {
            TMP_FontAsset f = null;
            try { f = TMP_Settings.defaultFontAsset; } catch { }
            if (f == null)
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                f = all.FirstOrDefault(a => a != null && a.name.IndexOf("SDF", System.StringComparison.OrdinalIgnoreCase) >= 0) ?? all.FirstOrDefault();
            }
            return f;
        }

        /// <summary>A dynamic SDF font asset built from a font file; null (logged) when the file is missing or unreadable.</summary>
        private static TMP_FontAsset LoadFont(string path, TMP_FontAsset fallback)
        {
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("UI font file not found (" + path + "); using the game's font.");
                return null;
            }
            try
            {
                var asset = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                if (asset == null)
                {
                    Plugin.Log.LogWarning("UI font could not be read (" + path + "); using the game's font.");
                    return null;
                }
                asset.name = Path.GetFileNameWithoutExtension(path);
                asset.isMultiAtlasTexturesEnabled = true;
                // one heavy weight: keep the synthesised bold light so counters stay open (ui.json canvas)
                asset.boldStyle = UiTiming.FontBoldStyle;
                asset.boldSpacing = UiTiming.FontBoldSpacing;
                if (fallback != null) asset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
                // built at runtime: keep it (and its atlas and material) through scene changes
                asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (asset.material != null) asset.material.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (asset.atlasTexture != null) asset.atlasTexture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return asset;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("UI font failed to load (" + e.Message + "); using the game's font.");
                return null;
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

        /// <summary>
        /// Line advance as a multiple of the font size (ui.json LineHeight), whatever the font's own line height: the
        /// custom font and the game's fallback differ. TMP lineSpacing is in hundredths of an em.
        /// </summary>
        public static void LineHeight(TextMeshProUGUI t, float em)
        {
            var f = t.font;
            if (f == null || f.faceInfo.pointSize <= 0f) return;
            t.lineSpacing = (em - f.faceInfo.lineHeight / f.faceInfo.pointSize) * 100f;
        }

        /// <summary>A width that fits the canvas: wanted, or the canvas less safeMargin on both sides when that is narrower.</summary>
        public static float FitWidth(float wanted) => Mathf.Min(wanted, CanvasSize.x - 2f * UiTiming.SafeMargin);

        /// <summary>Narrow a top banner (panel plus its text lines) to the canvas; each text keeps its inset from the panel edge.</summary>
        public static void FitBanner(Image panel, float wanted, params TextMeshProUGUI[] lines)
        {
            float w = FitWidth(wanted);
            float shrink = panel.rectTransform.sizeDelta.x - w;
            if (Mathf.Abs(shrink) < 0.5f) return;
            panel.rectTransform.sizeDelta = new Vector2(w, panel.rectTransform.sizeDelta.y);
            foreach (var t in lines) t.rectTransform.sizeDelta = new Vector2(t.rectTransform.sizeDelta.x - shrink, t.rectTransform.sizeDelta.y);
        }

        /// <summary>Shrink to fit (down to min) before the text is cut with an ellipsis.</summary>
        public static void AutoSize(TextMeshProUGUI t, float min)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = Mathf.Min(min, t.fontSize);
        }

        /// <summary>
        /// Size and place a backing panel (a sibling drawn before the text, same parent and anchors) around the text as
        /// it is laid out now, with pad on every side. Hidden when the text is empty.
        /// </summary>
        public static void FitBacking(Image panel, TextMeshProUGUI text, float pad)
        {
            text.ForceMeshUpdate(true);
            var b = text.textBounds;
            bool any = !string.IsNullOrEmpty(text.text) && b.size.x > 0f && b.size.y > 0f;
            panel.gameObject.SetActive(any);
            if (!any) return;
            var rt = panel.rectTransform;
            var tr = text.rectTransform;
            rt.anchorMin = tr.anchorMin; rt.anchorMax = tr.anchorMax;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(b.size.x + pad * 2f, b.size.y + pad * 2f);
            rt.anchoredPosition = tr.anchoredPosition + new Vector2(b.min.x - pad, b.min.y - pad);
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        /// <summary>The rich-text tag that opens a span in this colour (close it with &lt;/color&gt;).</summary>
        public static string ColorTag(Color c) => "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">";

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
