using PeakRounds.Cards;
using PeakRounds.Generated;
using PeakRounds.Ui;

namespace PeakRounds.Net
{
    /// <summary>systems.json Net: the receive side of design/net.json (Generated/NetEvents.cs dispatches here).</summary>
    public static class NetHandlers
    {
        public static void OnHello(Hello m, int sender)
        {
            Plugin.Log.LogInfo($"Player {sender} runs {Plugin.Name} {m.modVersion}, ROUNDS read: {m.hasRounds} ({m.cardCount} cards)");
            if (m.modVersion != Plugin.Version) Hud.Toast($"{NetSend.NameOf(sender)} has a different mod version ({m.modVersion})");
            if (!m.hasRounds) Hud.Toast($"{NetSend.NameOf(sender)} has no ROUNDS install; they will see cards but not deal them");
            if (NetSend.IsHost && Effects.HasAnyState) NetSend.RaiseTo(Effects.BuildStateSync(PickSession.SessionId), sender);
        }

        public static void OnPickSessionStart(PickSessionStart m, int sender)
        {
            PickSession.OnStart(m);
        }

        public static void OnPickMade(PickMade m, int sender)
        {
            PickSession.OnPick(m);
        }

        public static void OnStateSync(StateSync m, int sender)
        {
            Effects.ApplyStateSync(m);
            Plugin.Verbose($"State sync from {sender}: {m.actors.Length} players");
        }

        public static void OnArrival(Arrival m, int sender)
        {
            PickSession.RecordArrival(sender, m.segment, m.time);
        }

        public static void OnStateRequest(StateRequest m, int sender)
        {
            if (!NetSend.IsHost) return;
            NetSend.RaiseTo(Effects.BuildStateSync(PickSession.SessionId), sender);
        }

        public static void OnPhoenixUsed(PhoenixUsed m, int sender)
        {
            Effects.PhoenixUsed.Add(m.actor);
            Hud.Toast(string.Format(Ui_Toast.PhoenixTemplate, m.actor == NetSend.LocalActor ? "you" : NetSend.NameOf(m.actor)));
        }

        public static void OnSessionEnd(SessionEnd m, int sender)
        {
            PickSession.OnSessionEnd(m);
        }
    }
}
