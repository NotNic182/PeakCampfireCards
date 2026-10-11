using System.Collections.Generic;
using CampfireCards.Cards;
using CampfireCards.Generated;
using UnityEngine;
using UnityEngine.UI;

namespace CampfireCards.Art
{
    /// <summary>
    /// systems.json Art: builds one art.json motif inside a card's art window and animates its layers on unscaled time
    /// (so the art keeps moving while the picker is open, even when the game is paused).
    /// </summary>
    public class CardArt : MonoBehaviour
    {
        private struct Live
        {
            public RectTransform Rt;
            public Image Img;
            public ArtLayer Def;
            public Color Color;
        }

        private readonly List<Live> _layers = new List<Live>();

        public static void Build(RectTransform window, int motifIndex, ThemeDef theme, string cardKey)
        {
            var custom = CustomCardImages.Get(cardKey);
            if (custom != null)
            {
                var go = new GameObject("CustomArt", typeof(RectTransform));
                go.transform.SetParent(window, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                var image = go.AddComponent<Image>();
                image.sprite = custom;
                image.color = Color.white;
                image.preserveAspect = true;
                image.raycastTarget = false;
                return;
            }
            var art = window.gameObject.AddComponent<CardArt>();
            if (motifIndex < 0 || motifIndex >= ArtTable.Motifs.Length) return;
            foreach (var l in ArtTable.Motifs[motifIndex].Layers)
            {
                var go = new GameObject("Layer_" + l.Shape, typeof(RectTransform));
                go.transform.SetParent(window, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(l.W, l.H);
                rt.anchoredPosition = new Vector2(l.X, l.Y);
                rt.localEulerAngles = new Vector3(0f, 0f, l.Rot);
                var img = go.AddComponent<Image>();
                img.sprite = Shapes.Get(l.Shape);
                img.raycastTarget = false;
                var c = ColorFor(l.Color, theme);
                c.a *= l.Alpha;
                img.color = c;
                art._layers.Add(new Live { Rt = rt, Img = img, Def = l, Color = c });
            }
            art.Tick();
        }

        public static Color ColorFor(ArtColor role, ThemeDef theme)
        {
            switch (role)
            {
                case ArtColor.frame: return theme.Frame.ToColor();
                case ArtColor.accent: return theme.Accent.ToColor();
                case ArtColor.bg: return theme.Bg.ToColor();
                case ArtColor.white: return Color.white;
                case ArtColor.dark: return Color.Lerp(theme.Frame.ToColor(), theme.Bg.ToColor(), 0.6f);
                default: return Color.white;
            }
        }

        private void Update() => Tick();

        private void Tick()
        {
            float time = Time.unscaledTime;
            foreach (var l in _layers)
            {
                if (l.Rt == null) continue;
                var d = l.Def;
                float cyc = time * d.Speed + d.Phase;
                float wave = Mathf.Sin(cyc * 2f * Mathf.PI);
                Vector2 pos = new Vector2(d.X, d.Y);
                float rot = d.Rot, sx = 1f, sy = 1f, alpha = 1f;
                switch (d.Anim)
                {
                    case ArtAnim.bob: pos.y += d.Amp * wave; break;
                    case ArtAnim.slide: pos.x += d.Amp * wave; break;
                    case ArtAnim.spin: rot += d.Speed * time + d.Phase * 360f; break;
                    case ArtAnim.pulse: sx = sy = 1f + d.Amp * wave; break;
                    case ArtAnim.drift:
                    {
                        float f = Mathf.Repeat(time * d.Speed + d.Phase, 1f);
                        pos.y += d.Amp * f;
                        alpha = (1f - f) * Mathf.Clamp01(f * 6f);
                        break;
                    }
                    case ArtAnim.flicker:
                    {
                        float n1 = Mathf.Sin(cyc * 2f * Mathf.PI) * 0.6f + Mathf.Sin((cyc * 2.3f + 0.4f) * 2f * Mathf.PI) * 0.4f;
                        float n2 = Mathf.Sin((cyc * 1.7f + 0.2f) * 2f * Mathf.PI);
                        sx = 1f + d.Amp * n2;
                        sy = 1f + d.Amp * n1;
                        alpha = 0.85f + 0.15f * n1;
                        break;
                    }
                    case ArtAnim.orbit:
                    {
                        float a = cyc * 2f * Mathf.PI;
                        pos += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d.Amp;
                        break;
                    }
                    case ArtAnim.sway: rot += d.Amp * wave; break;
                }
                l.Rt.anchoredPosition = pos;
                l.Rt.localEulerAngles = new Vector3(0f, 0f, rot);
                l.Rt.localScale = new Vector3(sx, sy, 1f);
                var c = l.Color; c.a *= alpha;
                l.Img.color = c;
            }
        }
    }
}
