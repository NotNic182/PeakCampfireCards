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

        public static void Raise(Hello m, ReceiverGroup g) => RaiseRaw(Hello.Code, m.Write(), new RaiseEventOptions { Receivers = g });
        public static void Raise(PickSessionStart m, ReceiverGroup g) => RaiseRaw(PickSessionStart.Code, m.Write(), new RaiseEventOptions { Receivers = g });
        public static void Raise(PickMade m, ReceiverGroup g) => RaiseRaw(PickMade.Code, m.Write(), new RaiseEventOptions { Receivers = g });
        public static void Raise(SecondWindUsed m, ReceiverGroup g) => RaiseRaw(SecondWindUsed.Code, m.Write(), new RaiseEventOptions { Receivers = g });
        public static void Raise(SessionEnd m, ReceiverGroup g) => RaiseRaw(SessionEnd.Code, m.Write(), new RaiseEventOptions { Receivers = g });

        public static void RaiseToMaster(Arrival m) => RaiseRaw(Arrival.Code, m.Write(), new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient });
        public static void RaiseToMaster(StateRequest m) => RaiseRaw(StateRequest.Code, m.Write(), new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient });

        public static void RaiseTo(StateSync m, int actor)
        {
            if (actor == LocalActor) { NetHandlers.OnStateSync(m, LocalActor); return; }
            RaiseRaw(StateSync.Code, m.Write(), new RaiseEventOptions { TargetActors = new[] { actor } });
        }
    }
}
