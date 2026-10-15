using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using CampfireCards.Ui;
using Photon.Pun;

namespace CampfireCards.Cards
{
    /// <summary>
    /// Run memory: every player's cards are written to disk (by player NAME, Core.RunMemory format) whenever a
    /// collection changes, and F8 with an empty GrantCard gives everyone in the room their saved cards back as a
    /// reason-9 restore (no pick-time effects). The run file mirrors the run being played; when a new run first saves,
    /// the old state rotates to the lastrun file, so a crashed run can be restored even after new picks were made.
    /// </summary>
    public static class RunMemoryFile
    {
        private static string RunPath => Path.Combine(Paths.ConfigPath, "notnic.campfirecards.run.txt");
        private static string LastRunPath => Path.Combine(Paths.ConfigPath, "notnic.campfirecards.lastrun.txt");

        /// <summary>Saves only run while the run this client started is the live one: a late joiner's state sync can
        /// arrive BEFORE RunManager.StartRun, and a save stamped with the previous run's id would rotate twice and
        /// destroy the lastrun backup.</summary>
        private static bool _runActive;
        private static int _activeRun;
        private static float _nextRestore;

        public static void Subscribe() => Effects.CollectionChanged += _ => Save();

        /// <summary>
        /// Run start (Hooks.OnRunStarted): the one rotation point. The previous state, whichever run it was, becomes
        /// the lastrun file; a rejoin into the same run gets its cards back from there through RestoreAll's merge.
        /// </summary>
        public static void OnRunStarted(int runId)
        {
            try
            {
                string onDisk = ReadSafe(RunPath);
                if (RunMemory.Parse(onDisk).Count > 0 && RunMemory.RunIdOf(onDisk) != runId)
                {
                    try { File.Copy(RunPath, LastRunPath, true); File.Delete(RunPath); }
                    catch (Exception e) { Plugin.Log.LogWarning("Run memory rotate: " + e.Message); }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Run memory rotate: " + e.Message); }
            _activeRun = runId;
            _runActive = true;
        }

        /// <summary>Run over (end screen): the finished state stays on disk; nothing further is recorded.</summary>
        public static void OnRunEnded() => _runActive = false;

        /// <summary>Mirror the full known state. A player with no cards keeps their saved entry: a rejoiner arrives
        /// with an empty collection, and overwriting their line would destroy exactly what the restore needs.</summary>
        private static void Save()
        {
            try
            {
                if (!_runActive || LocalCards.RunSeed != _activeRun) return;
                int runId = _activeRun;
                var entries = RunMemory.Parse(ReadSafe(RunPath));
                foreach (var kv in Effects.CardsByActor)
                {
                    if (kv.Value == null || kv.Value.Count == 0) continue;
                    string name = NameFor(kv.Key);
                    if (name == null) continue;
                    var keys = new List<string>();
                    foreach (var id in kv.Value)
                    {
                        var def = CardSettings.Current.Card(id);
                        if (def != null) keys.Add(def.Key);
                    }
                    if (keys.Count > 0) entries[name] = keys.ToArray();
                }
                if (entries.Count > 0) File.WriteAllText(RunPath, RunMemory.Serialize(runId, entries));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Run memory save: " + e.Message); }
        }

        /// <summary>
        /// Host (F8, GrantCard empty): everyone in the room gets the saved cards they do not hold, matched by player
        /// name across the run and lastrun files, granted as a reason-9 restore (no Curse, no bonus draws, repeatable).
        /// </summary>
        public static void RestoreAll()
        {
            try
            {
                // one restore per echo window: online, the grants only land after a server round trip, and a second
                // press before then would re-raise them (reason 9 is repeat-exempt, duplicates could double-grant)
                if (UnityEngine.Time.unscaledTime < _nextRestore) { Hud.Toast("Card restore: already on its way"); return; }
                var saved = RunMemory.Merge(RunMemory.Parse(ReadSafe(LastRunPath)), RunMemory.Parse(ReadSafe(RunPath)));
                if (saved.Count == 0) { Hud.Toast("Card restore: no saved run found"); return; }
                // a card still in someone's dealt hand is not granted: the owner picking it would have the pick
                // consumed and the card refused as a duplicate (same guard as the F6 tester)
                var offered = new HashSet<byte>();
                if (PickSession.Active) foreach (var id in PickSession.Turns.CardsStillOffered()) offered.Add(id);
                int cards = 0, players = 0, waiting = 0;
                var room = PhotonNetwork.CurrentRoom;
                var actors = new List<int>();
                if (room != null) foreach (var p in room.Players.Values) { if (p != null) actors.Add(p.ActorNumber); }
                if (actors.Count == 0) actors.Add(NetSend.LocalActor);
                foreach (var actor in actors)
                {
                    string name = NameFor(actor);
                    string[] keys;
                    if (name == null || !saved.TryGetValue(name, out keys)) continue;
                    var ownedKeys = new List<string>();
                    foreach (var id in Effects.CardsOf(actor))
                    {
                        var def = CardSettings.Current.Card(id);
                        if (def != null) ownedKeys.Add(def.Key);
                    }
                    var ids = new List<byte>();
                    foreach (var key in RunMemory.Missing(keys, ownedKeys))
                    {
                        var def = Array.Find(CardTable.Cards, c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
                        if (def == null) { Plugin.Log.LogWarning($"Card restore: no card with key \"{key}\" (saved by another version?)"); continue; }
                        if (offered.Contains(def.Id)) { waiting++; continue; }
                        ids.Add(def.Id);
                    }
                    if (ids.Count == 0) continue;
                    Plugin.Log.LogInfo($"Card restore: {name} gets {string.Join(", ", ids.ConvertAll(Deck.NameOf))}");
                    NetSend.Raise(new CardsGranted { sessionId = PickSession.SessionId, actor = actor, cards = ids.ToArray(), reason = 9 },
                        Photon.Realtime.ReceiverGroup.All);
                    cards += ids.Count;
                    players++;
                }
                if (cards > 0) _nextRestore = UnityEngine.Time.unscaledTime + 3f;
                string note = waiting > 0 ? $" ({waiting} on offer in the current pick: press F8 again after it)" : "";
                Hud.Toast(cards > 0
                    ? $"Card restore: {cards} {(cards == 1 ? "card" : "cards")} back to {players} {(players == 1 ? "scout" : "scouts")}{note}"
                    : "Card restore: everyone already holds their saved cards" + note);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Card restore: " + e.Message); }
        }

        private static string ReadSafe(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
            catch { return ""; }
        }

        /// <summary>The player's lobby name (rejoins keep it; actor numbers do not), or null when unknown.</summary>
        private static string NameFor(int actor)
        {
            try
            {
                var room = PhotonNetwork.CurrentRoom;
                Photon.Realtime.Player p;
                if (room != null && room.Players.TryGetValue(actor, out p) && p != null && !string.IsNullOrEmpty(p.NickName))
                {
                    string clean = RunMemory.CleanName(p.NickName);
                    return clean.Length > 0 ? clean : null;
                }
                if (actor == NetSend.LocalActor && !string.IsNullOrEmpty(PhotonNetwork.NickName))
                {
                    string clean = RunMemory.CleanName(PhotonNetwork.NickName);
                    return clean.Length > 0 ? clean : null;
                }
            }
            catch { }
            return null;
        }
    }
}
