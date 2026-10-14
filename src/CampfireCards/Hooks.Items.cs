using System;
using System.Collections;
using System.Collections.Generic;
using CampfireCards.Abilities;
using CampfireCards.Cards;
using CampfireCards.Generated;
using CampfireCards.Net;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core.Serizalization;

namespace CampfireCards
{
    /// <summary>
    /// Item hooks. Item actions run on the client of the scout using the item (the eater, or the fed teammate), so the
    /// local player's multipliers are the right ones; a feeder's own card (Field Medic) is read from the synced collection.
    /// </summary>
    public static partial class Hooks
    {
        private static readonly AccessTools.FieldRef<ItemActionBase, Item> ActionItem =
            AccessTools.FieldRefAccess<ItemActionBase, Item>("item");

        /// <summary>Everything one use of one item did to the local scout, gathered over its actions and reported once.</summary>
        private sealed class ItemUse
        {
            public Item Item;
            public int Frame;
            public bool Food, Cooked, Curative;
            public float Hunger;
            public float InjuryBefore;
            public readonly HashSet<CharacterAfflictions.STATUSTYPE> Cured = new HashSet<CharacterAfflictions.STATUSTYPE>();
        }

        private static readonly List<ItemUse> _batches = new List<ItemUse>();

        private static ItemUse Batch(Item item, Character user)
        {
            foreach (var b in _batches) if (b.Item == item && b.Frame == Time.frameCount) return b;
            var nb = new ItemUse
            {
                Item = item,
                Frame = Time.frameCount,
                InjuryBefore = user.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Injury)
            };
            _batches.Add(nb);
            return nb;
        }

        /// <summary>Report finished item uses (one frame later, once every action of the use has run).</summary>
        private static void FlushItemUses(Character local)
        {
            for (int i = _batches.Count - 1; i >= 0; i--)
            {
                var b = _batches[i];
                if (b.Frame >= Time.frameCount) continue;
                _batches.RemoveAt(i);
                if (b.Food) Plugin.Verbose($"Ate {(b.Item != null ? b.Item.GetItemName() : "?")}: cooked={b.Cooked}, Hunger removed {b.Hunger * 100f:0.#}");
                if (b.Food) LocalCards.OnFood(local, new FoodEvent { Item = b.Item, Cooked = b.Cooked, HungerRemoved = b.Hunger, InjuryBefore = b.InjuryBefore });
                if (b.Curative) LocalCards.OnCurative(local, new CurativeEvent { Item = b.Item, Cured = b.Cured });
            }
        }

        public static bool IsCooked(Item item)
        {
            if (item == null) return false;
            try
            {
                IntItemData v;
                if (item.data != null && item.data.TryGetDataEntry<IntItemData>(DataEntryKey.CookedAmount, out v)) return v.Value > 0;
                return item.cooking != null && item.cooking.timesCookedLocal > 0;
            }
            catch { return false; }
        }

        public static bool IsFood(Item item) => item != null && item.GetComponent<Action_RestoreHunger>() != null;

        /// <summary>
        /// Medical items, by prefab name (research notes_items.md): bandages, first aid kit, antidote, cure-all, medicinal
        /// root, aloe, heat pack, healing amulet, blowgun dart. Berries, snacks, mushrooms and coconut are food, not medicine.
        /// </summary>
        private static readonly string[] MedicalItemNames =
        {
            "Bandages", "FirstAidKit", "Antidote", "Cure-All", "MedicinalRoot", "Mandrake_Hidden", "AloeVera", "Heat Pack",
            "Amulet_Healing", "HealingDart Variant"
        };
        private static HashSet<ushort> _medicalIds;

        public static bool IsCurativeItem(Item item)
        {
            if (item == null) return false;
            if (_medicalIds == null || _medicalIds.Count == 0)
            {
                var ids = new HashSet<ushort>();
                foreach (var name in MedicalItemNames)
                {
                    Item prefab;
                    if (ItemDatabase.TryGetItem(name, out prefab) && prefab != null) ids.Add(prefab.itemID);
                }
                if (ids.Count == 0) return false;   // database not loaded yet: try again next time
                _medicalIds = ids;
            }
            return _medicalIds.Contains(item.itemID);
        }

        // ------------------------------------------------------------------ food
        /// <summary>
        /// hooks.json restoreHunger prefix: foodHunger (and cookedFoodHunger for cooked food) for the eater. The exact
        /// scaled amount goes to the game; Hooks.Status keeps the part its 0.025 step would discard (StatusCarry).
        /// </summary>
        public static void OnRestoreHungerPrefix(Action_RestoreHunger __instance, out float __state)
        {
            __state = __instance.restorationAmount;
            _itemActionDepth++;
            var item = ActionItem(__instance);
            var eater = item != null ? item.holderCharacter : null;
            if (!Mine(eater)) return;
            bool cooked = IsCooked(item);
            float k = Core.StatComposites.FoodNutrition(Effects.Local, cooked);
            if (__state > 0f && Mathf.Abs(k - 1f) > 0.0001f)
            {
                __instance.restorationAmount = __state * k;
                _itemScaled = true;
            }
            var b = Batch(item, eater);
            b.Food = true;
            b.Cooked = cooked;
            b.Hunger += __instance.restorationAmount;
        }

        public static void OnRestoreHunger(Action_RestoreHunger __instance, float __state)
        {
            __instance.restorationAmount = __state;   // cooking also edits this field; never leave our value behind
            if (_itemActionDepth > 0) _itemActionDepth--;
            _itemScaled = false;
        }

        // ------------------------------------------------------------------ healing and curatives
        /// <summary>
        /// hooks.json modifyStatus prefix: Injury removed by an item is scaled by healReceived (this scout) and by the user's
        /// healGiven (someone else used it on this scout) or healSelfUse (this scout used it on itself).
        /// </summary>
        public static void OnModifyStatusActionPrefix(Action_ModifyStatus __instance, out float __state)
        {
            __state = __instance.changeAmount;
            _itemActionDepth++;
            if (__state >= 0f) return;   // adds a status (e.g. Scorchberry heat): not healing
            var item = ActionItem(__instance);
            var target = item != null ? item.holderCharacter : null;
            if (!Mine(target)) return;
            var type = __instance.statusType;
            if (type == CharacterAfflictions.STATUSTYPE.Hunger) return;
            var b = Batch(item, target);
            if (type == CharacterAfflictions.STATUSTYPE.Injury)
            {
                float k = Effects.Local[Channel.healReceived];
                Character feeder;
                if (item.TryGetFeeder(out feeder) && feeder != null && feeder != target)
                    k *= Effects.ActorChannel(NetSend.ActorOf(feeder), Channel.healGiven);
                else
                    k *= Effects.Local[Channel.healSelfUse];
                if (Mathf.Abs(k - 1f) > 0.0001f)
                {
                    __instance.changeAmount = __state * k;   // exact; Hooks.Status keeps the step leftover
                    _itemScaled = true;
                }
            }
            // SubtractStatus(Poison, ..., decreasedNaturally:false) also subtracts Spores on the local scout.
            if (Core.CurativeSelection.RecordMedicalCure(b.Cured, type,
                CharacterAfflictions.STATUSTYPE.Poison, CharacterAfflictions.STATUSTYPE.Spores, IsCurativeItem(item)))
                b.Curative = true;
        }

        public static void OnModifyStatusAction(Action_ModifyStatus __instance, float __state)
        {
            __instance.changeAmount = __state;
            if (_itemActionDepth > 0) _itemActionDepth--;
            _itemScaled = false;
        }

        /// <summary>hooks.json applyAffliction: a medical item that works through an affliction (Heat Pack warms over time).</summary>
        public static void OnApplyAfflictionActionPrefix(Action_ApplyAffliction __instance)
        {
            var item = ActionItem(__instance);
            var target = item != null ? item.holderCharacter : null;
            if (!Mine(target) || !IsCurativeItem(item)) return;
            var b = Batch(item, target);
            b.Curative = true;
            if (WarmsCold(__instance.affliction)) b.Cured.Add(CharacterAfflictions.STATUSTYPE.Cold);
            if (__instance.extraAfflictions != null)
                foreach (var a in __instance.extraAfflictions) if (WarmsCold(a)) b.Cured.Add(CharacterAfflictions.STATUSTYPE.Cold);
        }

        private static bool WarmsCold(Peak.Afflictions.Affliction a) =>
            a is Peak.Afflictions.Affliction_AdjustColdOverTime;

        /// <summary>hooks.json healingGem prefix: the Healing Amulet heals through an affliction; note what it treats.</summary>
        public static void OnHealingGemActionPrefix(Peak.Action_HealingGem __instance, out float[] __state)
        {
            __state = null;
            var item = ActionItem(__instance);
            var target = item != null ? item.holderCharacter : null;
            if (!Mine(target) || !IsCurativeItem(item)) return;
            var aff = target.refs.afflictions;
            __state = new float[StatusCount];
            for (int i = 0; i < __state.Length; i++) __state[i] = aff.GetCurrentStatus((CharacterAfflictions.STATUSTYPE)i);
        }

        /// <summary>hooks.json healingGem postfix: every status the amulet lowered counts as treated (Mini Tenacity picks another).</summary>
        public static void OnHealingGemAction(Peak.Action_HealingGem __instance, float[] __state)
        {
            if (__state == null) return;
            var item = ActionItem(__instance);
            var target = item != null ? item.holderCharacter : null;
            if (!Mine(target)) return;
            var aff = target.refs.afflictions;
            var b = Batch(item, target);
            b.Curative = true;
            for (int i = 0; i < __state.Length; i++)
                if (aff.GetCurrentStatus((CharacterAfflictions.STATUSTYPE)i) < __state[i] - 0.0001f) b.Cured.Add((CharacterAfflictions.STATUSTYPE)i);
        }

        /// <summary>hooks.json clearAllStatus: an item that clears every status counts as one curative use.</summary>
        public static void OnClearAllStatusActionPrefix(Action_ClearAllStatus __instance)
        {
            var item = ActionItem(__instance);
            var target = item != null ? item.holderCharacter : null;
            if (!Mine(target) || !IsCurativeItem(item)) return;
            var b = Batch(item, target);
            b.Curative = true;
            foreach (CharacterAfflictions.STATUSTYPE t in Enum.GetValues(typeof(CharacterAfflictions.STATUSTYPE))) b.Cured.Add(t);
        }

        // ------------------------------------------------------------------ Lucky Lunch: keep the food
        /// <summary>
        /// One keep-or-use-up decision per use of an item (Core.ItemTransaction). Every action of one use runs inside the
        /// same FinishCast call, so the reduceUses, consumeAndSpawn and consumeDelayed hooks of that use all get the first
        /// answer: the once-per-biome charge is spent exactly once and no path consumes a food another path kept.
        /// </summary>
        private static readonly Core.ItemTransaction _lunch = new Core.ItemTransaction();

        private static bool LuckyLunchSaves(Item item)
        {
            if (item == null) return false;
            return _lunch.Decide(item.GetInstanceID(), Time.frameCount, () =>
            {
                var eater = item.holderCharacter;
                if (!Mine(eater) || !IsFood(item)) return false;
                var ll = LocalCards.Get<Ability_LuckyLunch>();
                if (ll == null) return false;
                bool cooked = IsCooked(item);
                bool kept = cooked && ll.TryUse();
                if (!cooked) ll.HintUncooked();
                Plugin.Verbose($"Lucky Lunch: {item.GetItemName()} cooked={cooked} biome={LocalCards.Biome} -> {(kept ? "kept" : cooked ? "used up (already used this biome)" : "used up (not cooked)")}");
                return kept;
            });
        }

        /// <summary>A kept item's on-consume effects (stamina, afflictions) run once per use, however many paths kept it.</summary>
        private static void FireConsumedOnce(Item item)
        {
            if (item != null && item.OnConsumed != null && _lunch.TryFireEffects(item.GetInstanceID(), Time.frameCount)) item.OnConsumed();
        }

        /// <summary>
        /// hooks.json consumeDelayed: the food's on-consume effects still run, but the Consume RPC (which removes it from
        /// the pocket on every client) is never sent, so the item stays where it was.
        /// </summary>
        public static bool OnConsumeDelayedPrefix(Item __instance, bool ignoreActions, ref IEnumerator __result)
        {
            if (!LuckyLunchSaves(__instance)) return true;
            __result = KeepItem(__instance, ignoreActions);
            return false;
        }

        private static IEnumerator KeepItem(Item item, bool ignoreActions)
        {
            if (!ignoreActions) FireConsumedOnce(item);
            yield return null;
            // a fed item points at the eater while it is "being consumed"; give it back to the feeder
            if (item != null && item.overrideHolderCharacter != null && item.trueHolderCharacter != item.overrideHolderCharacter)
                item.overrideHolderCharacter = null;
        }

        /// <summary>hooks.json reduceUses: multi-use cooked food keeps its use.</summary>
        public static bool OnReduceUsesPrefix(Action_ReduceUses __instance) => !LuckyLunchSaves(ActionItem(__instance));

        /// <summary>hooks.json consumeAndSpawn: food that leaves a peel is kept, and no peel is spawned.</summary>
        public static bool OnConsumeAndSpawnPrefix(Action_ConsumeAndSpawn __instance)
        {
            var item = ActionItem(__instance);
            if (!LuckyLunchSaves(item)) return true;
            FireConsumedOnce(item);
            return false;
        }

        // ------------------------------------------------------------------ Pocket Tradeoff: locked pocket
        /// <summary>The third pocket is blocked for new items. Items already in it remain retrievable.</summary>
        public const int LockedPocket = 2;

        private static bool PocketLocked(Player p)
        {
            try
            {
                var view = p.photonView;
                if (view == null || view.Owner == null) return false;
                return RunState.RunStarted && Core.CardSettings.Current.DrawbacksEnabled && Effects.HasAbility(view.Owner.ActorNumber, "PocketTradeoff");
            }
            catch { return false; }
        }

        /// <summary>
        /// hooks.json addItem (master only). The game fills the first empty pocket; when the only empty one is the locked
        /// pocket, the item goes to the hands slot like a full inventory, or the pickup is refused and it stays on the ground.
        /// </summary>
        public static bool OnAddItemPrefix(Player __instance, ushort itemID, ref ItemInstanceData instanceData, ref ItemSlot slot, ref bool __result)
        {
            if (!PhotonNetwork.IsMasterClient || !PocketLocked(__instance)) return true;
            Item prefab;
            if (!ItemDatabase.TryGetItem(itemID, out prefab) || prefab is Backpack) return true;
            var slots = __instance.itemSlots;
            if (slots == null || slots.Length <= LockedPocket || !slots[LockedPocket].IsEmpty()) return true;
            for (int i = 0; i < slots.Length; i++)
                if (i != LockedPocket && slots[i].IsEmpty()) return true;   // vanilla takes an earlier empty pocket

            if (instanceData == null)
            {
                instanceData = new ItemInstanceData(Guid.NewGuid());
                ItemInstanceDataHandler.AddInstanceData(instanceData);
            }
            var ch = __instance.character;
            if (__instance.tempFullSlot.IsEmpty() && ch != null && !ch.data.isClimbingAnything)
            {
                __instance.tempFullSlot.SetItem(prefab, instanceData);
                slot = __instance.tempFullSlot;
                byte[] array = IBinarySerializable.ToManagedArray(new InventorySyncData(__instance.itemSlots, __instance.backpackSlot, __instance.tempFullSlot));
                __instance.photonView.RPC("SyncInventoryRPC", RpcTarget.Others, array, false);
                __result = true;
                return false;
            }
            slot = null;
            __result = false;
            return false;
        }

        /// <summary>hooks.json hasEmptySlot: the client-side pickup check skips the locked pocket too.</summary>
        public static bool OnHasEmptySlotPrefix(Player __instance, ushort itemID, ref bool __result)
        {
            if (!PocketLocked(__instance)) return true;
            Item prefab;
            if (!ItemDatabase.TryGetItem(itemID, out prefab) || prefab is Backpack) return true;
            var slots = __instance.itemSlots;
            for (int i = 0; i < slots.Length; i++)
            {
                if (i == LockedPocket) continue;
                if (slots[i].IsEmpty()) { __result = true; return false; }
            }
            __result = __instance.tempFullSlot.IsEmpty();
            return false;
        }

        // ------------------------------------------------------------------ campfire item spawns (host)
        /// <summary>Host: every campfire lit this run (by segment), so rewards are placed next to the real fire.</summary>
        private static readonly Dictionary<byte, Campfire> _litFires = new Dictionary<byte, Campfire>();
        private static readonly Core.SpawnLedger _spawns = new Core.SpawnLedger();
        /// <summary>A reward may land at most this far from its fire (horizontal metres), whatever the spot search returns.</summary>
        private const float MaxSpawnDistance = 7f;

        private static void ResetCampfireSpawns()
        {
            _litFires.Clear();
            _spawns.Reset();
        }

        /// <summary>Host: a campfire was really lit this run; its card rewards may now be claimed, once each.</summary>
        private static void HostCampfireLit(Campfire fire)
        {
            byte seg = (byte)fire.advanceToSegment;
            _litFires[seg] = fire;
            _spawns.FireLit(seg);
        }

        /// <summary>
        /// Host: a card's campfire item (Rope Baron, Supply Scout). The host checks that the sender owns a card whose
        /// reward is this item, that the fire was lit this run and that this reward was not spawned yet, then places it
        /// next to that fire itself. The client only names the item and the fire.
        /// </summary>
        public static void HostSpawnItem(SpawnItem m, int sender)
        {
            if (!PhotonNetwork.IsMasterClient && !PhotonNetwork.OfflineMode) return;
            bool owns = false;
            bool once = false;
            foreach (var id in Effects.CardsOf(sender))
            {
                var def = Core.CardSettings.Current.Card(id);
                if (def != null && def.Ability == "CampfireSpawn" && Mathf.RoundToInt(Core.CardSettings.Current.Param(def, "item", -1f)) == m.item) owns = true;
                if (def != null && def.Ability == "NextCampfireSpawn" && Mathf.RoundToInt(Core.CardSettings.Current.Param(def, "item", -1f)) == m.item)
                {
                    once = true;
                    owns = (Effects.Flags(sender) & Effects.FlagBalloonUsed) == 0;
                }
            }
            if (m.item >= CardTable.SpawnItems.Length) owns = false;
            var verdict = _spawns.TryClaim(sender, m.item, m.segment, owns);
            if (verdict != Core.SpawnVerdict.Ok) { Plugin.Log.LogWarning($"Refused item {m.item} for actor {sender} at fire {m.segment}: {verdict}"); return; }

            Campfire fire;
            if (!_litFires.TryGetValue(m.segment, out fire) || fire == null) { Plugin.Log.LogWarning($"Item {m.item}: fire {m.segment} is gone"); return; }
            string name = CardTable.SpawnItems[m.item];
            Item prefab;
            if (!ItemDatabase.TryGetItem(name, out prefab)) { Plugin.Log.LogWarning("Unknown item prefab " + name); return; }
            Vector3 pos;
            if (!SpawnSpot(fire, sender * 3 + m.item, out pos)) { Plugin.Log.LogWarning($"Item {name} for actor {sender}: no room next to the fire"); return; }
            var go = PhotonNetwork.InstantiateItemRoom(name, pos + Vector3.up * 0.25f, Quaternion.identity);
            var spawned = go != null ? go.GetComponent<Item>() : null;
            if (spawned != null) spawned.SetKinematicNetworked(true, pos + Vector3.up * 0.25f, Quaternion.identity);
            if (spawned != null && once)
            {
                Effects.SetFlag(sender, Effects.FlagBalloonUsed, true);
                NetSend.Raise(new CardRewardUsed { actor = sender }, Photon.Realtime.ReceiverGroup.All);
            }
            Plugin.Verbose($"Spawned {name} for {sender} at {pos} (fire {m.segment})");
        }

        /// <summary>Host: a ground spot by the fire (the search the owner used to run), never further than MaxSpawnDistance.</summary>
        private static bool SpawnSpot(Campfire fire, int salt, out Vector3 pos)
        {
            Vector3 normal;
            Vector3 c = fire.transform.position;
            if (!SafeSpot.NearCampfire(fire, salt, out pos, out normal))
            {
                if (!SafeSpot.Near(c, 2.5f, 6f, out pos)) return false;
                pos -= Vector3.up * 1f;   // Near returns a torso height; items sit on the ground
            }
            var flat = new Vector2(pos.x - c.x, pos.z - c.z);
            return flat.magnitude <= MaxSpawnDistance && Mathf.Abs(pos.y - c.y) <= 4f;
        }
    }
}
