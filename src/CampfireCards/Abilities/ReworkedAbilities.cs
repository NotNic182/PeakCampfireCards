using System.Collections.Generic;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using CampfireCards.Ui;
using UnityEngine;
using Zorro.Core;

namespace CampfireCards.Abilities
{
    /// <summary>
    /// Fartberry Fitness: the poison drip (gas clouds and every poison-over-time affliction, which tick in tiny
    /// per-frame steps) never lands; discrete poison hits (stings, bites, blast vines, eating something vile) land
    /// harder as the drawback. The split is the per-event amount: nothing in the game delivers a real hit below
    /// gasThreshold, and no drip ever ticks above it.
    /// </summary>
    public sealed class Ability_GasImmunity : Ability
    {
        /// <summary>Factor for one poison event, judged by the UNSCALED amount the game asked for.</summary>
        public static float PoisonFactor(float eventAmount)
        {
            var a = LocalCards.Get<Ability_GasImmunity>();
            if (a == null) return 1f;
            // over-time afflictions tick statusPerSecond * dt, so the drip line scales with the frame that delivered it:
            // a lag hitch never promotes venom to a "hit" (erring toward immunity, never toward the drawback)
            float threshold = a.P("gasThreshold", 0.02f) * Mathf.Max(1f, Time.deltaTime * 60f);
            if (eventAmount < threshold) return 0f;
            return CardSettings.Current.DrawbacksEnabled ? a.P("hit", 1.2f) : 1f;
        }
    }

    /// <summary>
    /// TechTara: falls hurt base% less, and every Bandages in your pockets (up to limit) adds perBandage% more
    /// reduction. Counted at the moment the fall lands, from the real inventory slots.
    /// </summary>
    public sealed class Ability_BandageBrace : Ability
    {
        public static float FallFactor(Character me)
        {
            var a = LocalCards.Get<Ability_BandageBrace>();
            if (a == null) return 1f;
            float cut = a.P("base", 0.2f) + a.P("perBandage", 0.2f) * Bandages(me, a);
            return Mathf.Clamp01(1f - cut);
        }

        private static int Bandages(Character me, Ability_BandageBrace a)
        {
            int limit = Mathf.RoundToInt(a.P("limit", 3f)), n = 0;
            try
            {
                var player = me != null ? me.player : null;
                if (player == null) return 0;
                foreach (var slot in player.itemSlots)
                    if (slot != null && !slot.IsEmpty() && slot.GetPrefabName() == "Bandages") n++;
                if (player.tempFullSlot != null && !player.tempFullSlot.IsEmpty() && player.tempFullSlot.GetPrefabName() == "Bandages") n++;
                var bp = player.backpackSlot;
                if (bp != null && !bp.IsEmpty() && bp.data != null)
                {
                    BackpackData bd;
                    if (bp.data.TryGetDataEntry<BackpackData>(DataEntryKey.BackpackData, out bd))
                        foreach (var s in bd.itemSlots)
                            if (s != null && !s.IsEmpty() && s.GetPrefabName() == "Bandages") n++;
                }
            }
            catch (System.Exception e) { Plugin.Verbose("bandage count skipped: " + e.Message); }
            return Mathf.Min(limit, n);
        }

        public override AbilityStatus Status(Character me)
        {
            int n = Bandages(me, this);
            int cut = Mathf.RoundToInt(Mathf.Clamp01(P("base", 0.2f) + P("perBandage", 0.2f) * n) * 100f);
            return AbilityStatus.Ok(Line(null, $"falls hurt {cut}% less ({n} bandage{(n == 1 ? "" : "s")})"));
        }
    }

    /// <summary>
    /// Book of Broken Bones: a Book of Bones (the game's skeleton-revive tome) is spawned for you, once per run, next
    /// to the campfire you picked the card at (or the next one to be lit, for a card granted away from any fire).
    /// </summary>
    public sealed class Ability_BoneBook : Ability
    {
        public override void OnAcquired(Character me)
        {
            // a dead picker or a fireless grant just waits: OnCampfireLit retries until the flag confirms the spawn
            if (me == null || me.data.dead) return;
            TryRequest();
        }

        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (me.data.dead) return;
            TryRequest();   // the host ledger refuses duplicates per fire, so retrying at a new fire is safe
        }

        private void TryRequest()
        {
            if ((Effects.Flags(NetSend.LocalActor) & Effects.FlagBoneBook) != 0) return;
            var fire = LocalCards.LastCampfire;
            if (fire == null) return;
            int item = Mathf.RoundToInt(P("item", 3f));
            if (item < 0 || item >= CardTable.SpawnItems.Length) return;
            NetSend.RaiseToMaster(new SpawnItem { item = (byte)item, segment = (byte)fire.advanceToSegment });
        }

        public override AbilityStatus Status(Character me) =>
            (Effects.Flags(NetSend.LocalActor) & Effects.FlagBoneBook) != 0
                ? AbilityStatus.Wait(Line(null, "Book of Bones received"))
                : AbilityStatus.Wait(Line(null, "Book of Bones arrives at a campfire"));
    }

    /// <summary>
    /// Balright: at the next campfire, the owner picks one item from the mythic pool (cards.json mythicItems) and the
    /// host spawns it by the fire, once per run. The chooser waits for the card fan to close first.
    /// </summary>
    public sealed class Ability_MythicChoice : Ability
    {
        private bool _pendingChoice;
        private byte _fireSegment;

        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (acquiredThisFire || me.data.dead) return;
            if ((Effects.Flags(NetSend.LocalActor) & Effects.FlagBalloonUsed) != 0) return;
            var fire = LocalCards.LastCampfire;
            if (fire == null) return;
            _pendingChoice = true;
            _fireSegment = (byte)fire.advanceToSegment;
        }

        public override void Tick(Character me, float dt)
        {
            if (!_pendingChoice) return;
            if ((Effects.Flags(NetSend.LocalActor) & Effects.FlagBalloonUsed) != 0) { _pendingChoice = false; return; }
            if (me.data.dead) return;
            // wait out the whole campfire ceremony and every other blocking window (pick fans, deck, pause menu)
            if (PickSession.Active) return;
            if (CardPicker.Instance != null && CardPicker.Instance.isOpen) return;
            if (DeckScreen.Instance != null && DeckScreen.Instance.isOpen) return;
            if (ItemChooser.IsOpen) return;
            bool blocked = false;
            try { blocked = GUIManager.instance != null && (GUIManager.instance.windowBlockingInput || GUIManager.instance.wheelActive); } catch { }
            if (blocked) return;
            _pendingChoice = false;
            byte segment = _fireSegment;
            // Esc postpones: nothing re-arms here, the next OnCampfireLit offers the choice again
            ItemChooser.Show(string.Format(Ui_ItemChooser.Title, Card.Name), CardTable.MythicItems,
                chosen => NetSend.RaiseToMaster(new SpawnItem { item = chosen, segment = segment }),
                postponed => { });
        }

        public override void OnRemoved(Character me) => _pendingChoice = false;

        public override AbilityStatus Status(Character me) =>
            (Effects.Flags(NetSend.LocalActor) & Effects.FlagBalloonUsed) != 0
                ? AbilityStatus.Wait(Line(null, "mythic item received"))
                : AbilityStatus.Wait(Line(null, "choose a mythic item at the next fire"));
    }

    /// <summary>
    /// Scoutmaster's Care Package host helper: which extra items ride along with the bandage. Rolled on the host from
    /// the game's own item database: one random food (Action_RestoreHunger) and one random anything (never a backpack).
    /// </summary>
    public static class CarePackageRolls
    {
        /// <summary>Dev, prop and quest prefabs that live in the database but must never ride a care package.</summary>
        private static readonly string[] Junk =
            { "Backpack", "Prop", "UNUSED", "Cheat", "Peel", "Flag", "Passport", "Soul", "Idol", "Pandora", "Cursed", "Bugfix", "Chess" };

        public static string RandomItemName(bool foodOnly)
        {
            try
            {
                var pool = new List<string>();
                foreach (var kv in SingletonAsset<ItemDatabase>.Instance.itemLookup)
                {
                    var it = kv.Value;
                    if (it == null || it is Backpack) continue;
                    string name = it.name;
                    if (string.IsNullOrEmpty(name) || name.StartsWith("C_")) continue;
                    bool junk = false;
                    foreach (var j in Junk) if (name.IndexOf(j, System.StringComparison.OrdinalIgnoreCase) >= 0) { junk = true; break; }
                    if (junk) continue;
                    if (System.Array.IndexOf(CardTable.MythicItems, name) >= 0) continue;   // Balright's exclusives stay exclusive
                    string shown = null;
                    try { shown = it.GetItemName(); } catch { }
                    if (string.IsNullOrEmpty(shown)) continue;   // unnamed prefabs are props or dev leftovers
                    if (foodOnly && !Hooks.IsFood(it)) continue;
                    pool.Add(name);
                }
                return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
            }
            catch (System.Exception e)
            {
                Plugin.Verbose("care package roll skipped: " + e.Message);
                return null;
            }
        }
    }
}
