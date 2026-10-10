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
                scaler.referenceResolution = UiTiming.ReferenceResolution;
                scaler.matchWidthOrHeight = 0.5f;
                go.AddComponent<GraphicRaycaster>();
                return _canvas;
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
