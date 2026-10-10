using System;
using System.Collections.Generic;
using CampfireCards.Generated;
using UnityEngine;

namespace CampfireCards.Art
{
    /// <summary>
    /// systems.json Art: draws every art.json shape into a white, anti-aliased sprite (alpha only) at runtime and caches it.
    /// Layers tint these sprites with their theme colour, so the mod ships no image files.
    /// </summary>
    public static class Shapes
    {
        private const int Size = 128;
        private const int Samples = 4; // 4x4 supersampling per pixel for smooth edges
        private static readonly Dictionary<ArtShape, Sprite> Cache = new Dictionary<ArtShape, Sprite>();
        private static Sprite _panel;

        public static Sprite Get(ArtShape shape)
        {
            Sprite s;
            if (Cache.TryGetValue(shape, out s) && s != null) return s;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "CampfireCards/" + shape, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            float step = 2f / Size, sub = step / Samples;
            for (int j = 0; j < Size; j++)
            {
                for (int i = 0; i < Size; i++)
                {
                    int hits = 0;
                    for (int sj = 0; sj < Samples; sj++)
                        for (int si = 0; si < Samples; si++)
                        {
                            float x = -1f + i * step + (si + 0.5f) * sub;
                            float y = -1f + j * step + (sj + 0.5f) * sub;
                            if (Inside(shape, x, y)) hits++;
                        }
                    px[j * Size + i] = new Color32(255, 255, 255, (byte)(255 * hits / (Samples * Samples)));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            s.name = tex.name;
            Cache[shape] = s;
            return s;
        }

        /// <summary>A 64x64 rounded rectangle with 9-slice borders, for card frames and windows.</summary>
        public static Sprite Panel
        {
            get
            {
                if (_panel != null) return _panel;
                const int n = 64; const float r = 20f;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "CampfireCards/Panel", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int hits = 0;
                        for (int sj = 0; sj < Samples; sj++)
                            for (int si = 0; si < Samples; si++)
                            {
                                float x = i + (si + 0.5f) / Samples, y = j + (sj + 0.5f) / Samples;
                                float qx = Mathf.Max(Mathf.Max(r - x, x - (n - r)), 0f);
                                float qy = Mathf.Max(Mathf.Max(r - y, y - (n - r)), 0f);
                                if (qx * qx + qy * qy <= r * r) hits++;
                            }
                        px[j * n + i] = new Color32(255, 255, 255, (byte)(255 * hits / (Samples * Samples)));
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                _panel = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
                return _panel;
            }
        }

        /// <summary>Is the point (x, y) in [-1, 1], y up, inside the shape?</summary>
        public static bool Inside(ArtShape shape, float x, float y)
        {
            float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
            switch (shape)
            {
                case ArtShape.circle: return x * x + y * y <= 0.95f * 0.95f;
                case ArtShape.ring: { float d = Mathf.Sqrt(x * x + y * y); return d <= 0.95f && d >= 0.66f; }
                case ArtShape.triangle: return y >= -0.95f && y <= 0.95f && ax <= 0.95f * (0.95f - y) / 1.9f;
                case ArtShape.diamond: return ax + ay <= 0.95f;
                case ArtShape.star: return Star(x, y, 5, 0.95f, 0.42f);
                case ArtShape.spark: return Mathf.Sqrt(ax) + Mathf.Sqrt(ay) <= 0.97f;
                case ArtShape.drop: return Drop(x, y);
                case ArtShape.flame:
                {
                    float lift = Mathf.Max(0f, y + 0.3f);
                    return Drop(x - 0.22f * Mathf.Sin(lift * 2.4f) * lift, y);
                }
                case ArtShape.chevron:
                    return SegDist(x, y, -0.9f, -0.5f, 0f, 0.45f) <= 0.3f || SegDist(x, y, 0f, 0.45f, 0.9f, -0.5f) <= 0.3f;
                case ArtShape.crescent:
                {
                    float dx = x - 0.38f, dy = y - 0.28f;
                    return x * x + y * y <= 0.95f * 0.95f && dx * dx + dy * dy > 0.78f * 0.78f;
                }
                case ArtShape.wave: return ax <= 0.97f && Mathf.Abs(y - 0.45f * Mathf.Sin(x * Mathf.PI * 1.25f)) <= 0.32f;
                case ArtShape.rounded:
                {
                    const float r = 0.35f, h = 0.95f - r;
                    float qx = Mathf.Max(ax - h, 0f), qy = Mathf.Max(ay - h, 0f);
                    return ax <= 0.95f && ay <= 0.95f && qx * qx + qy * qy <= r * r;
                }
                case ArtShape.plus: return (ax <= 0.3f && ay <= 0.95f) || (ay <= 0.3f && ax <= 0.95f);
                case ArtShape.shield:
                    if (y > 0.9f || y < -0.95f) return false;
                    if (y >= 0f) return ax <= 0.85f;
                    return ax <= 0.85f * (1f - Mathf.Pow(-y / 0.95f, 1.8f));
                default: return false;
            }
        }

        private static bool Drop(float x, float y)
        {
            float cy = y + 0.32f;
            if (x * x + cy * cy <= 0.62f * 0.62f) return true;
            return y >= -0.32f && y <= 0.95f && Mathf.Abs(x) <= 0.62f * (0.95f - y) / 1.27f;
        }

        private static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
            float t = Mathf.Clamp01((wx * vx + wy * vy) / (vx * vx + vy * vy));
            float dx = wx - vx * t, dy = wy - vy * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static bool Star(float x, float y, int points, float outer, float inner)
        {
            float rho = Mathf.Sqrt(x * x + y * y);
            if (rho > outer) return false;
            if (rho <= inner) return true;
            float seg = 2f * Mathf.PI / points;
            float a = Mathf.Atan2(x, y);          // 0 = straight up
            a = Mathf.Repeat(a + seg / 2f, seg) - seg / 2f;
            a = Mathf.Abs(a);
            // edge from the tip (outer, angle 0) to the valley (inner, angle seg/2)
            float px = rho * Mathf.Cos(a), py = rho * Mathf.Sin(a);
            float tx = outer, ty = 0f;
            float vx = inner * Mathf.Cos(seg / 2f), vy = inner * Mathf.Sin(seg / 2f);
            float cross = (vx - tx) * (py - ty) - (vy - ty) * (px - tx);
            float originCross = (vx - tx) * (0f - ty) - (vy - ty) * (0f - tx);
            return Math.Sign(cross) == Math.Sign(originCross) || cross == 0f;
        }
    }
}
