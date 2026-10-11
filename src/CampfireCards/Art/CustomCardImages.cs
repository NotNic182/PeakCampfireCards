using System;
using System.Collections.Generic;
using System.IO;
using CampfireCards.Core;
using UnityEngine;

namespace CampfireCards.Art
{
    internal static class CustomCardImages
    {
        // Cache missing files as well; restart PEAK to pick up image edits.
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Get(string cardKey)
        {
            if (string.IsNullOrEmpty(cardKey) || string.IsNullOrEmpty(Plugin.PluginDir)) return null;
            if (Cache.TryGetValue(cardKey, out var cached)) return cached;
            string path = Path.Combine(Plugin.PluginDir, "art", "cards", cardKey + ".png");
            Texture2D texture = null;
            Sprite sprite = null;
            try
            {
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > PngImageInfo.MaxBytes) throw new InvalidDataException("PNG must be 8 MiB or smaller");
                    byte[] bytes = File.ReadAllBytes(path);
                    if (!PngImageInfo.Supported(bytes)) throw new InvalidDataException("use a PNG with dimensions between 1 and 2048 pixels");
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(texture, bytes, true)) throw new InvalidDataException("Unity could not decode this PNG");
                    texture.name = "CardArt_" + cardKey;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
                    sprite.name = texture.name;
                }
            }
            catch (Exception e)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                Plugin.Log.LogWarning("Card image " + cardKey + ": " + e.Message + ". Using the original art. See docs/card-images.md.");
            }
            Cache[cardKey] = sprite;
            return sprite;
        }

        public static void Clear()
        {
            foreach (var sprite in Cache.Values)
            {
                if (sprite == null) continue;
                UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }
            Cache.Clear();
        }
    }
}
