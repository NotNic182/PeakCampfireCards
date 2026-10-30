using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace CampfireCards.Net
{
    /// <summary>systems.json Net: RaiseEvent helpers for the structs in Generated/NetEvents.cs.</summary>
    public static class NetSend
    {
        public static bool IsHost => PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode || !PhotonNetwork.InRoom;
        public static int LocalActor => PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : 1;

        /// <summary>Solo = offline play, or an online room with nobody else in it.</summary>
        public static bool IsSolo => PhotonNetwork.OfflineMode || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.PlayerCount <= 1;

        public static int ActorOf(Character c)
        {
            if (c == null) return int.MinValue;
            try
            {
                var view = c.photonView;
                if (view != null && view.Owner != null) return view.Owner.ActorNumber;
            }
            catch { }
            return c.IsLocal ? LocalActor : int.MinValue;
        }

        public static string NameOf(int actor)
        {
            try
            {
                Character c;
                if (PlayerHandler.TryGetCharacter(actor, out c) && c != null && !string.IsNullOrEmpty(c.characterName)) { RememberName(c.characterName); return NoTags(c.characterName); }
                var room = PhotonNetwork.CurrentRoom;
                Photon.Realtime.Player p;
                if (room != null && room.Players.TryGetValue(actor, out p) && !string.IsNullOrEmpty(p.NickName)) { RememberName(p.NickName); return NoTags(p.NickName); }
            }
            catch { }
            return actor == LocalActor ? "You" : "Player " + actor;
        }

        /// <summary>
        /// Every player name this session has seen, raw and tag-defused. The log a bug report attaches spans the whole
        /// session, so the scrub list (BugReportScreen.CollectLog -> LogScrub) must cover names from earlier lobbies and
        /// players who already left - the current room's list alone is not enough.
        /// </summary>
        private static readonly HashSet<string> _seenNames = new HashSet<string>();

        public static void RememberName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.Length < 2 || _seenNames.Count > 512) return;   // a lobby cycling names must not grow this unbounded
            _seenNames.Add(name);
            _seenNames.Add(NoTags(name));
        }

        public static List<string> SeenNames() => new List<string>(_seenNames);

        /// <summary>
        /// Names land inside TMP rich text (team headers, auras, toasts), and a nickname like "&lt;size=400%&gt;" must
        /// never restyle the UI. A zero-width space after each &lt; makes TMP render the bracket verbatim without
        /// changing what the name looks like; &lt;noparse&gt; wrapping is escapable by the name itself, this is not.
        /// </summary>
        private static string NoTags(string s) => string.IsNullOrEmpty(s) ? s : s.Replace("<", "<​");

        private static void RaiseRaw(byte code, byte[] payload, RaiseEventOptions options)
        {
            if (PhotonNetwork.NetworkingClient == null) return;
            PhotonNetwork.RaiseEvent(code, payload, options, SendOptions.SendReliable);
        }

        public static void Raise(INetEvent e, ReceiverGroup g) => RaiseRaw(e.EventCode, e.Write(), new RaiseEventOptions { Receivers = g });

        public static void RaiseToMaster(INetEvent e) => RaiseRaw(e.EventCode, e.Write(), new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient });

        /// <summary>Send to one player. To ourselves it is delivered directly (offline play has no loopback for targeted events).</summary>
        public static void RaiseTo(INetEvent e, int actor)
        {
            if (actor == LocalActor) { NetDispatcher.Dispatch(e.EventCode, e.Write(), LocalActor); return; }
            RaiseRaw(e.EventCode, e.Write(), new RaiseEventOptions { TargetActors = new[] { actor } });
        }
    }
}
