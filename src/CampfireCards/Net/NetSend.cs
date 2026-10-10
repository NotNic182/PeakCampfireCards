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
                if (PlayerHandler.TryGetCharacter(actor, out c) && c != null && !string.IsNullOrEmpty(c.characterName)) return c.characterName;
                var room = PhotonNetwork.CurrentRoom;
                Photon.Realtime.Player p;
                if (room != null && room.Players.TryGetValue(actor, out p) && !string.IsNullOrEmpty(p.NickName)) return p.NickName;
            }
            catch { }
            return actor == LocalActor ? "You" : "Player " + actor;
        }

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
