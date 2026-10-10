using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using PeakRounds.Generated;
using UnityEngine;

namespace PeakRounds.Rounds
{
    /// <summary>
    /// systems.json RoundsReader: opens the player's ROUNDS install with AssetsTools.NET and reads the real cards,
    /// textures and sounds listed in design/rounds_assets.json. Runs on a background thread; Unity objects are made lazily on the main thread.
    /// </summary>
    public static class RoundsReader
    {
        public static RoundsData Data;
        public static volatile bool Ready;
        private static Thread _thread;

        private static readonly HashSet<string> WantedTextures = new HashSet<string>(new[] { RA_CardBG.Name, RA_SquareSprite.Name, "Icons_Default" });
        private static readonly HashSet<string> WantedClips = new HashSet<string>(
            RA_SfxCardPick.Names.Concat(RA_SfxCardSwitch.Names).Concat(RA_SfxCardsAppear.Names).Concat(RA_SfxHover.Names));

        public static void BeginLoad(string pluginDir, string overridePath)
        {
            Ready = false;
            _thread = new Thread(() =>
            {
                var data = new RoundsData();
                try
                {
                    Load(data, pluginDir, overridePath);
                }
                catch (Exception e)
                {
                    data.Error = e.GetType().Name + ": " + e.Message;
                    Plugin.Log.LogError("RoundsReader failed: " + e);
                }
                Data = data;
                Ready = true;
                Plugin.Log.LogInfo(Summary(data));
            })
            { IsBackground = true, Name = "PeakRounds.RoundsReader" };
            _thread.Start();
        }

        public static string Summary(RoundsData d)
        {
            if (d.NotInstalled) return "ROUNDS not installed (" + d.LocateNote + ")";
            if (d.Error != null) return "ROUNDS read failed: " + d.Error + " (" + d.LocateNote + ")";
            return $"ROUNDS {d.UnityVersion} at {d.Root}: {d.Cards.Count} cards ({d.PoolNames.Count} in pool), {d.Textures.Count} textures, {d.Clips.Count(c => c.Value.Pcm != null)}/{d.Clips.Count} sounds, {d.EnglishStrings.Count} strings, themes {(d.ThemesFromGame ? "from level0" : "fallback")} in {d.LoadSeconds:0.00}s. {string.Join(" ", d.Notes)}";
        }

        private static void Load(RoundsData data, string pluginDir, string overridePath)
        {
            var sw = Stopwatch.StartNew();
            string how;
            string root = RoundsLocator.Find(overridePath, out how);
            data.LocateNote = how;
            if (root == null) { data.NotInstalled = true; data.Error = "ROUNDS not found"; return; }
            data.Root = root;
            string dataDir = Path.Combine(root, RoundsInstall.DataFolder);
            string managed = Path.Combine(root, RoundsInstall.Managed); // RA_ManagedDlls: Assembly-CSharp.dll etc. read as metadata by Mono.Cecil
            if (!File.Exists(Path.Combine(managed, RA_ManagedDlls.Name))) throw new FileNotFoundException(RA_ManagedDlls.Name + " missing in " + managed);

            var am = new AssetsManager();
            string tpk = Path.Combine(pluginDir, "classdata.tpk");
            if (!File.Exists(tpk)) throw new FileNotFoundException("classdata.tpk missing next to PeakRounds.dll");
            am.LoadClassPackage(tpk);
            am.MonoTempGenerator = new MonoCecilTempGenerator(managed);
            am.UseTemplateFieldCache = true;
            am.UseMonoTemplateFieldCache = true;
            am.UseQuickLookup = true;

            // --- RA_SharedAssets0 + RA_GlobalGameManagers (loaded as its dependency)
            var shared = am.LoadAssetsFile(Path.Combine(dataDir, RA_SharedAssets0.Name), true);
            am.LoadClassDatabaseFromPackage(shared.file.Metadata.UnityVersion);
            data.UnityVersion = shared.file.Metadata.UnityVersion;
            var scripts = AssetHelper.GetAssetsFileScriptInfos(am, shared);

            // --- RA_CardInfo
            foreach (var info in shared.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                AssetTypeReference tr;
                if (!scripts.TryGetValue(info.GetScriptIndex(shared.file), out tr) || tr.ClassName != "CardInfo") continue;
                try
                {
                    var card = ReadCard(am, shared, info, data, dataDir);
                    if (card != null)
                    {
                        data.Cards[card.GameObjectName] = card;
                        data.CardsByPathId[info.PathId] = card;
                    }
                }
                catch (Exception e)
                {
                    data.Notes.Add($"card {info.PathId}: {e.Message}");
                }
            }

            // --- RA_CardBG, RA_SquareSprite, Icons_Default by name
            foreach (var info in shared.file.GetAssetsOfType(AssetClassID.Texture2D))
            {
                string name = AssetHelper.GetAssetNameFast(shared.file, am.ClassDatabase, info);
                if (!WantedTextures.Contains(name) || data.Textures.ContainsKey(name)) continue;
                try { data.Textures[name] = ReadTexture(am.GetBaseField(shared, info), dataDir); }
                catch (Exception e) { data.Notes.Add($"texture {name}: {e.Message}"); }
            }

            // --- RA_SfxCardPick, RA_SfxCardSwitch, RA_SfxCardsAppear, RA_SfxHover
            foreach (var info in shared.file.GetAssetsOfType(AssetClassID.AudioClip))
            {
                string name = AssetHelper.GetAssetNameFast(shared.file, am.ClassDatabase, info);
                if (!WantedClips.Contains(name) || data.Clips.ContainsKey(name)) continue;
                var clip = new RoundsClip { Name = name };
                data.Clips[name] = clip;
                try { ReadClip(am.GetBaseField(shared, info), dataDir, clip); }
                catch (Exception e) { clip.Error = e.Message; }
            }

            // --- RA_Level0: the live pool and the theme colours (optional)
            try { ReadLevel0(am, dataDir, shared, data); }
            catch (Exception e) { data.Notes.Add("level0: " + e.Message); }
            if (data.PoolNames.Count == 0) data.PoolNames.AddRange(data.Cards.Keys);

            // --- RA_EnglishStrings (optional)
            try { ReadEnglish(am, dataDir, data); }
            catch (Exception e) { data.Notes.Add("strings: " + e.Message); }
            foreach (var c in data.Cards.Values)
            {
                string s;
                if (c.NameKey != 0 && data.EnglishStrings.TryGetValue(c.NameKey, out s) && !string.IsNullOrWhiteSpace(s)) c.LocalizedName = s;
                if (c.DescriptionKey != 0 && data.EnglishStrings.TryGetValue(c.DescriptionKey, out s) && !string.IsNullOrWhiteSpace(s)) c.LocalizedDescription = s;
                foreach (var st in c.Stats)
                    if (st.LocalizedStatKey != 0 && data.EnglishStrings.TryGetValue(st.LocalizedStatKey, out s) && !string.IsNullOrWhiteSpace(s)) st.LocalizedStat = s;
            }

            am.UnloadAll(true);
            data.LoadSeconds = sw.Elapsed.TotalSeconds;
        }

        private static RoundsCard ReadCard(AssetsManager am, AssetsFileInstance inst, AssetFileInfo info, RoundsData data, string dataDir)
        {
            var bf = am.GetBaseField(inst, info);
            var card = new RoundsCard { PathId = info.PathId };
            var go = am.GetExtAsset(inst, bf["m_GameObject"]);
            card.GameObjectName = go.baseField != null ? go.baseField["m_Name"].AsString : ("card_" + info.PathId);
            card.LegacyName = bf["cardName"].AsString;
            card.LegacyDescription = bf["cardDestription"].AsString;
            card.Rarity = bf["rarity"].AsInt;
            card.ColorTheme = bf["colorTheme"].AsInt;
            card.AllowMultiple = bf["allowMultiple"].AsBool;
            var cc = bf["cardColor"];
            if (!cc.IsDummy) card.CardColor = new Color(cc["r"].AsFloat, cc["g"].AsFloat, cc["b"].AsFloat, cc["a"].AsFloat);
            card.NameKey = KeyOf(bf["m_localizedCardName"]);
            card.DescriptionKey = KeyOf(bf["m_localizedCardDescription"]);
            var stats = bf["cardStats"];
            if (!stats.IsDummy)
            {
                foreach (var s in stats["Array"])
                {
                    card.Stats.Add(new RoundsCardStat
                    {
                        Positive = s["positive"].AsBool,
                        Amount = s["amount"].AsString,
                        SimpleAmount = s["simepleAmount"].AsInt,
                        Stat = s["stat"].AsString,
                        LocalizedStatKey = KeyOf(s["m_localizedStat"]),
                    });
                }
            }
            // icon: CardInfo.sprite -> Sprite.m_RD.texture -> Texture2D
            var spriteField = bf["sprite"];
            if (!spriteField.IsDummy && spriteField["m_PathID"].AsLong != 0)
            {
                var sprite = am.GetExtAsset(inst, spriteField);
                if (sprite.baseField != null)
                {
                    var texPtr = sprite.baseField["m_RD"]["texture"];
                    var tex = am.GetExtAsset(sprite.file, texPtr);
                    if (tex.baseField != null)
                    {
                        string tname = tex.baseField["m_Name"].AsString;
                        card.IconTextureName = tname;
                        Plugin.Verbose("icon " + tname + " (" + RA_CardIcons.Format + ")");
                        if (!data.Textures.ContainsKey(tname))
                        {
                            try { data.Textures[tname] = ReadTexture(tex.baseField, Path.GetDirectoryName(tex.file.path)); }
                            catch (Exception e) { data.Notes.Add($"icon {tname}: {e.Message}"); }
                        }
                    }
                }
            }
            return card;
        }

        private static long KeyOf(AssetTypeValueField localizedString)
        {
            if (localizedString == null || localizedString.IsDummy) return 0;
            var r = localizedString["m_TableEntryReference"];
            if (r.IsDummy) return 0;
            var id = r["m_KeyId"];
            return id.IsDummy ? 0 : id.AsLong;
        }

        private static RoundsTexture ReadTexture(AssetTypeValueField bf, string dir)
        {
            var t = new RoundsTexture
            {
                Name = bf["m_Name"].AsString,
                Width = bf["m_Width"].AsInt,
                Height = bf["m_Height"].AsInt,
                Format = bf["m_TextureFormat"].AsInt,
                MipCount = bf["m_MipCount"].IsDummy ? 1 : bf["m_MipCount"].AsInt,
            };
            var img = bf["image data"];
            byte[] bytes = img.IsDummy ? null : img.AsByteArray;
            if (bytes == null || bytes.Length == 0)
            {
                var sd = bf["m_StreamData"];
                ulong offset = sd["offset"].AsULong;
                uint size = sd["size"].AsUInt;
                string path = sd["path"].AsString;
                if (size > 0 && !string.IsNullOrEmpty(path))
                    bytes = ReadSlice(Path.Combine(dir, Path.GetFileName(path.Replace("archive:/", ""))), (long)offset, size);
            }
            t.Data = bytes;
            return t;
        }

        private static void ReadClip(AssetTypeValueField bf, string dir, RoundsClip clip)
        {
            clip.Channels = bf["m_Channels"].AsInt;
            clip.Frequency = bf["m_Frequency"].AsInt;
            var res = bf["m_Resource"];
            string source = res["m_Source"].AsString;
            ulong offset = res["m_Offset"].AsULong;
            ulong size = res["m_Size"].AsULong;
            if (string.IsNullOrEmpty(source) || size == 0) { clip.Error = "no resource"; return; }
            byte[] fsb = ReadSlice(Path.Combine(dir, Path.GetFileName(source.Replace("archive:/", ""))), (long)offset, (uint)size);
            float[] pcm; int ch, rate; string err;
            if (Fsb5Vorbis.TryDecode(fsb, out pcm, out ch, out rate, out err))
            {
                clip.Pcm = pcm; clip.Channels = ch; clip.Frequency = rate;
            }
            else clip.Error = err;
        }

        private static byte[] ReadSlice(string path, long offset, uint size)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Position = offset;
                var buf = new byte[size];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return buf;
            }
        }

        private static void ReadLevel0(AssetsManager am, string dataDir, AssetsFileInstance shared, RoundsData data)
        {
            string path = Path.Combine(dataDir, "level0");
            if (!File.Exists(path)) return;
            var lvl = am.LoadAssetsFile(path, true);
            var scripts = AssetHelper.GetAssetsFileScriptInfos(am, lvl);
            foreach (var info in lvl.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                AssetTypeReference tr;
                if (!scripts.TryGetValue(info.GetScriptIndex(lvl.file), out tr) || tr.ClassName != "CardChoice") continue;
                var bf = am.GetBaseField(lvl, info);
                var cards = bf["cards"];
                if (!cards.IsDummy)
                {
                    foreach (var p in cards["Array"])
                    {
                        var ext = am.GetExtAsset(lvl, p, true);
                        if (ext.info == null || ext.file == null) continue;
                        if (!string.Equals(Path.GetFileName(ext.file.path), RA_SharedAssets0.Name, StringComparison.OrdinalIgnoreCase)) continue;
                        RoundsCard c;
                        if (data.CardsByPathId.TryGetValue(ext.info.PathId, out c) && !data.PoolNames.Contains(c.GameObjectName)) data.PoolNames.Add(c.GameObjectName);
                    }
                }
                var themes = bf["cardThemes"];
                if (!themes.IsDummy)
                {
                    var arr = new RoundsTheme[9];
                    bool any = false;
                    foreach (var t in themes["Array"])
                    {
                        int idx = t["themeType"].AsInt;
                        if (idx < 0 || idx >= arr.Length) continue;
                        var tc = t["targetColor"]; var bc = t["bgColor"];
                        arr[idx] = new RoundsTheme
                        {
                            Target = new Color(tc["r"].AsFloat, tc["g"].AsFloat, tc["b"].AsFloat, 1f),
                            Bg = new Color(bc["r"].AsFloat, bc["g"].AsFloat, bc["b"].AsFloat, 1f),
                        };
                        any = true;
                    }
                    if (any)
                    {
                        var def = RoundsData.DefaultThemes();
                        for (int i = 0; i < arr.Length; i++) if (arr[i].Target == default(Color)) arr[i] = def[i];
                        data.Themes = arr; data.ThemesFromGame = true;
                    }
                }
                break;
            }
        }

        private static void ReadEnglish(AssetsManager am, string dataDir, RoundsData data)
        {
            string path = Path.Combine(dataDir, "StreamingAssets", "aa", "StandaloneWindows64", "localization-string-tables-english(unitedstates)(en-us)_assets_all.bundle");
            if (!File.Exists(path)) { data.Notes.Add("no en-US bundle"); return; }
            var bun = am.LoadBundleFile(path, true);
            var inst = am.LoadAssetsFileFromBundle(bun, 0, false);
            foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                AssetTypeValueField bf = null;
                try { bf = am.GetBaseField(inst, info); } catch { }
                if (bf == null) continue;
                var table = bf["m_TableData"];
                if (table.IsDummy) continue;
                string name = bf["m_Name"].AsString ?? "";
                if (!name.StartsWith("StringTableCards", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var e in table["Array"])
                    data.EnglishStrings[e["m_Id"].AsLong] = e["m_Localized"].AsString;
            }
        }
    }
}
