using System.Collections.Generic;
using UnityEngine;

namespace PeakRounds.Rounds
{
    /// <summary>One stat line of a ROUNDS card, as ROUNDS stores it (CardInfoStat).</summary>
    public class RoundsCardStat
    {
        public string Stat;          // legacy label, e.g. "HP"
        public string Amount;        // e.g. "+100%"
        public int SimpleAmount;     // CardInfoStat.SimpleAmount enum value
        public bool Positive;
        public long LocalizedStatKey;
        public string LocalizedStat; // resolved English label when the string table was read
        public string Label => string.IsNullOrEmpty(LocalizedStat) ? Stat : LocalizedStat;
    }

    /// <summary>A ROUNDS card read from the player's ROUNDS install (CardInfo prefab).</summary>
    public class RoundsCard
    {
        public long PathId;
        public string GameObjectName;
        public string LegacyName;
        public string LegacyDescription;
        public long NameKey, DescriptionKey;
        public string LocalizedName, LocalizedDescription;
        public int Rarity;         // 0 common, 1 uncommon, 2 rare
        public int ColorTheme;     // CardThemeColor.CardThemeColorType
        public Color CardColor;
        public bool AllowMultiple;
        public readonly List<RoundsCardStat> Stats = new List<RoundsCardStat>();
        public string IconTextureName;

        public string Name => !string.IsNullOrEmpty(LocalizedName) ? LocalizedName : (!string.IsNullOrEmpty(LegacyName) ? LegacyName : GameObjectName);
        public string Description => !string.IsNullOrEmpty(LocalizedDescription) ? LocalizedDescription : LegacyDescription;
    }

    /// <summary>Raw texture bytes read from ROUNDS plus the Unity texture made from them on the main thread.</summary>
    public class RoundsTexture
    {
        public string Name;
        public int Width, Height, Format, MipCount;
        public byte[] Data;
        public string Error;
        private Texture2D _tex;
        private Sprite _sprite;
        private bool _tried;

        public Texture2D Texture
        {
            get
            {
                if (_tex != null || _tried) return _tex;
                _tried = true;
                try
                {
                    if (Data == null || Data.Length == 0) return null;
                    var fmt = (TextureFormat)Format;
                    _tex = new Texture2D(Width, Height, fmt, MipCount > 1, false);
                    _tex.name = "ROUNDS/" + Name;
                    _tex.LoadRawTextureData(Data);
                    _tex.Apply(false, true);
                    _tex.wrapMode = TextureWrapMode.Clamp;
                }
                catch (System.Exception e)
                {
                    Error = e.Message;
                    _tex = null;
                }
                return _tex;
            }
        }

        public Sprite Sprite
        {
            get
            {
                if (_sprite != null) return _sprite;
                var t = Texture;
                if (t == null) return null;
                _sprite = UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
                return _sprite;
            }
        }
    }

    /// <summary>Decoded PCM of a ROUNDS sound plus the AudioClip made from it on the main thread.</summary>
    public class RoundsClip
    {
        public string Name;
        public int Channels, Frequency;
        public float[] Pcm;
        public string Error;
        private AudioClip _clip;
        private bool _tried;

        public AudioClip Clip
        {
            get
            {
                if (_clip != null || _tried) return _clip;
                _tried = true;
                if (Pcm == null || Pcm.Length == 0 || Channels <= 0) return null;
                try
                {
                    _clip = AudioClip.Create("ROUNDS/" + Name, Pcm.Length / Channels, Channels, Frequency, false);
                    _clip.SetData(Pcm, 0);
                }
                catch (System.Exception e)
                {
                    Error = e.Message;
                    _clip = null;
                }
                return _clip;
            }
        }
    }

    public struct RoundsTheme
    {
        public Color Target, Bg;
    }

    /// <summary>Everything the mod read from the player's ROUNDS install.</summary>
    public class RoundsData
    {
        public string Root;
        public string LocateNote;
        public bool NotInstalled;
        public string Error;
        public string UnityVersion;
        public double LoadSeconds;
        public readonly Dictionary<string, RoundsCard> Cards = new Dictionary<string, RoundsCard>();
        public readonly Dictionary<long, RoundsCard> CardsByPathId = new Dictionary<long, RoundsCard>();
        public readonly List<string> PoolNames = new List<string>();
        public readonly Dictionary<string, RoundsTexture> Textures = new Dictionary<string, RoundsTexture>();
        public readonly Dictionary<string, RoundsClip> Clips = new Dictionary<string, RoundsClip>();
        public readonly Dictionary<long, string> EnglishStrings = new Dictionary<long, string>();
        public RoundsTheme[] Themes = DefaultThemes();
        public bool ThemesFromGame;
        public readonly List<string> Notes = new List<string>();

        public RoundsTheme Theme(int index)
        {
            if (index < 0 || index >= Themes.Length) index = 3;
            return Themes[index];
        }

        public RoundsTexture Texture(string name)
        {
            RoundsTexture t;
            return name != null && Textures.TryGetValue(name, out t) ? t : null;
        }

        /// <summary>Fallback copy of ROUNDS's CardChoice.cardThemes (research/rounds_themes.json); replaced by the live values from level0 when it can be read.</summary>
        public static RoundsTheme[] DefaultThemes()
        {
            return new[]
            {
                new RoundsTheme { Target = new Color(0.8019f, 0.2761f, 0.2761f), Bg = new Color(0.1792f, 0.0634f, 0.0634f) }, // DestructiveRed
                new RoundsTheme { Target = new Color(0.7925f, 0.7338f, 0.2280f), Bg = new Color(0.1981f, 0.1816f, 0.0663f) }, // FirepowerYellow
                new RoundsTheme { Target = new Color(0.3137f, 0.5155f, 0.8196f), Bg = new Color(0.0525f, 0.1063f, 0.1887f) }, // DefensiveBlue
                new RoundsTheme { Target = new Color(0.6792f, 0.6792f, 0.6792f), Bg = new Color(0.1509f, 0.1509f, 0.1509f) }, // TechWhite
                new RoundsTheme { Target = new Color(0.4777f, 0.3137f, 0.8196f), Bg = new Color(0.08f, 0.04f, 0.14f) },       // EvilPurple
                new RoundsTheme { Target = new Color(0.0000f, 0.5755f, 0.2995f), Bg = new Color(0.0298f, 0.0943f, 0.0513f) }, // PoisonGreen
                new RoundsTheme { Target = new Color(0.5712f, 0.8019f, 0.1248f), Bg = new Color(0.1047f, 0.1509f, 0.0406f) }, // NatureBrown
                new RoundsTheme { Target = new Color(0.3137f, 0.7369f, 0.8196f), Bg = new Color(0.0259f, 0.0911f, 0.1038f) }, // ColdBlue
                new RoundsTheme { Target = new Color(0.8196f, 0.3137f, 0.5344f), Bg = new Color(0.1321f, 0.0355f, 0.0774f) }, // MagicPink
            };
        }
    }
}
