using CampfireCards.Cards;
using CampfireCards.Generated;
using CampfireCards.Ui;

namespace CampfireCards.Net
{
    /// <summary>systems.json Net: the receive side of design/net.json (Generated/NetEvents.cs dispatches here).</summary>
    public static class NetHandlers
    {
        public static void OnHello(Hello m, int sender)
        {
            Plugin.Log.LogInfo($"Player {sender} runs {Plugin.Name} {m.modVersion} ({m.cardCount} cards, deck {m.deckHash:X8})");
            if (m.modVersion != Plugin.Version || m.deckHash != Deck.Hash)
                Hud.Toast($"{NetSend.NameOf(sender)} has a different version of Campfire Cards ({m.modVersion})");
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

        public static void OnSecondWindUsed(SecondWindUsed m, int sender)
        {
            Effects.SecondWindSpent.Add(m.actor);
            Hud.Toast(string.Format(Ui_Toast.SecondWindTemplate, m.actor == NetSend.LocalActor ? "you" : NetSend.NameOf(m.actor)));
        }

        public static void OnSessionEnd(SessionEnd m, int sender)
        {
            PickSession.OnSessionEnd(m);
        }
    }
}
