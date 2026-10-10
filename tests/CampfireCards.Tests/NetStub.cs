using System.Collections.Generic;

namespace CampfireCards.Net
{
    /// <summary>
    /// Test stand-in for the game-side receive handlers, so Generated/NetEvents.cs (serialisation and dispatch) can be
    /// tested without Unity. Keep the method list in step with design/net.json "handler".
    /// </summary>
    public static class NetHandlers
    {
        public static readonly List<(byte code, int sender, object msg)> Last = new List<(byte, int, object)>();

        public static void OnHello(Hello m, int sender) => Last.Add((90, sender, m));
        public static void OnPickSessionStart(PickSessionStart m, int sender) => Last.Add((91, sender, m));
        public static void OnPickMade(PickMade m, int sender) => Last.Add((92, sender, m));
        public static void OnStateSync(StateSync m, int sender) => Last.Add((93, sender, m));
        public static void OnArrival(Arrival m, int sender) => Last.Add((94, sender, m));
        public static void OnStateRequest(StateRequest m, int sender) => Last.Add((95, sender, m));
        public static void OnCardsGranted(CardsGranted m, int sender) => Last.Add((96, sender, m));
        public static void OnSessionEnd(SessionEnd m, int sender) => Last.Add((97, sender, m));
        public static void OnCardSacrificed(CardSacrificed m, int sender) => Last.Add((98, sender, m));
        public static void OnSkipNextPick(SkipNextPick m, int sender) => Last.Add((99, sender, m));
        public static void OnReviveRequest(ReviveRequest m, int sender) => Last.Add((100, sender, m));
        public static void OnReviveGranted(ReviveGranted m, int sender) => Last.Add((101, sender, m));
        public static void OnInjuryShare(InjuryShare m, int sender) => Last.Add((102, sender, m));
        public static void OnBonusStamina(BonusStamina m, int sender) => Last.Add((103, sender, m));
        public static void OnSpawnItem(SpawnItem m, int sender) => Last.Add((104, sender, m));
        public static void OnRevivedBy(RevivedBy m, int sender) => Last.Add((105, sender, m));
        public static void OnPickRequest(PickRequest m, int sender) => Last.Add((106, sender, m));
    }
}
