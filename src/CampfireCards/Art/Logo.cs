using System.IO;
using UnityEngine;

namespace CampfireCards.Art
{
    /// <summary>
    /// The campfire logo (art/CampfireLogo.png beside the DLL): stamped on card backs and in the picker's branding
    /// strip (ui.json Branding). The one image the mod ships - everything else is drawn (Shapes) - so a missing or
    /// unreadable file means "no logo", never an error.
    /// </summary>
    public static class Logo
    {
        private static Sprite _sprite;
        private static bool _tried;

        public static Sprite Sprite
        {
            get
            {
                if (_tried) return _sprite;
                _tried = true;
                try
                {
                    string path = Path.Combine(Plugin.PluginDir ?? "", "art", "CampfireLogo.png");
                    if (!File.Exists(path))
                    {
                        Plugin.Log.LogWarning("Logo image not found (" + path + "); card backs stay plain.");
                        return null;
                    }
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        name = "CampfireCards/Logo",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                    };
                    if (!tex.LoadImage(File.ReadAllBytes(path), markNonReadable: true))
                    {
                        Plugin.Log.LogWarning("Logo image could not be decoded; card backs stay plain.");
                        return null;
                    }
                    _sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    _sprite.name = tex.name;
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("Logo image could not be loaded: " + e.Message);
                }
                return _sprite;
            }
        }
    }
}
