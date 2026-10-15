using System.Collections.Generic;
using System.Linq;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using CampfireCards.Ui;

namespace CampfireCards.Cards
{
    public static partial class Effects
    {
        /// <summary>After a player's collection changed (add, remove or sync). Cards/RunMemoryFile mirrors the state to
        /// disk so the host can give everyone their cards back after a crash or rejoin; tests leave it unset.</summary>
        public static System.Action<int> CollectionChanged;
        /// <summary>A SetAll in progress: its per-card adds report once, at the end.</summary>
        private static bool _bulkChange;

        /// <summary>
        /// A card joins a player's collection. live = picked or granted right now (acquisition effects run); false = state
        /// sync. Returns false when the card was refused (unknown, or a duplicate the rules do not allow).
        /// </summary>
        public static bool AddCard(int actor, byte cardId, bool live)
        {
            var list = CardsOf(actor);
            var def = CardSettings.Current.Card(cardId);
            if (def == null) return false;
            if (list.Contains(cardId) && (!CardSettings.Current.AllowDuplicates || def.Rarity == CardRarity.Mythic))
            {
                Plugin.Log.LogWarning($"Ignored duplicate card {def.Name} for actor {actor}");
                return false;
            }
            if (def.Rarity == CardRarity.Mythic && live && MythicOwner(cardId) != int.MinValue)
            {
                Plugin.Log.LogWarning($"Ignored {def.Name} for actor {actor}: a Mythic is once per run");
                return false;
            }
            list.Add(cardId);
            if (live && def.Ability == "DoubleDown" && CardSettings.Current.DrawbacksEnabled) SetFlag(actor, FlagOfferOne, true);
            if (actor == NetSend.LocalActor) LocalCards.OnAcquired(def, live);
            Hud.RefreshMyCards();
            if (!_bulkChange && CollectionChanged != null) CollectionChanged(actor);
            return true;
        }

        /// <summary>A copy leaves the collection; the shared ability is removed only when its last copy leaves.</summary>
        public static void RemoveCard(int actor, byte cardId)
        {
            var list = CardsOf(actor);
            if (!list.Remove(cardId)) return;
            var def = CardSettings.Current.Card(cardId);
            if (actor == NetSend.LocalActor && def != null && !list.Contains(cardId)) LocalCards.OnRemoved(def);
            Hud.RefreshMyCards();
            if (!_bulkChange && CollectionChanged != null) CollectionChanged(actor);
        }

        /// <summary>Reconcile a synced collection without resetting abilities that the local scout still owns.</summary>
        public static void SetAll(int actor, IEnumerable<byte> cards, byte flags)
        {
            var restored = cards.ToList();
            if (actor == NetSend.LocalActor)
            {
                foreach (var old in CardsOf(actor).Distinct().Where(id => !restored.Contains(id)).ToList())
                {
                    var d = CardSettings.Current.Card(old);
                    if (d != null) LocalCards.OnRemoved(d);
                }
            }
            CardsByActor[actor] = new List<byte>();
            _bulkChange = true;
            try { foreach (var id in restored) AddCard(actor, id, false); }
            finally { _bulkChange = false; }
            PickFlags[actor] = flags;
            if (CollectionChanged != null) CollectionChanged(actor);
        }
    }
}
