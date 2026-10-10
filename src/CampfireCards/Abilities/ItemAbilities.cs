using System.Collections.Generic;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using CampfireCards.Ui;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>Iron Stomach: food removes more Hunger (modifier); every food eaten adds Drowsy.</summary>
    public sealed class Ability_IronStomach : Ability
    {
        public override void OnFoodEaten(Character me, FoodEvent food)
        {
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Drowsy, P("drowsy", 0.05f));
        }
    }

    /// <summary>Camp Chef: cooked food removes more Hunger (modifier); food that could have been cooked but was not adds Poison.</summary>
    public sealed class Ability_CampChef : Ability
    {
        public override void OnFoodEaten(Character me, FoodEvent food)
        {
            if (food.Cooked || food.Item == null) return;
            var cooking = food.Item.cooking;
            if (cooking == null || !cooking.canBeCooked) return;   // packaged snacks cannot be cooked: no penalty
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Poison, P("poison", 0.075f));
        }
    }

    /// <summary>Hungry Hands: eating while Injury is at least 20 removes 10 Injury. The weaker food is a modifier.</summary>
    public sealed class Ability_HungryHands : Ability
    {
        public override void OnFoodEaten(Character me, FoodEvent food)
        {
            if (food.InjuryBefore < P("threshold", 0.2f) - 0.0001f) return;
            CardStatus.Remove(me, CharacterAfflictions.STATUSTYPE.Injury, P("heal", 0.1f));
        }
    }

    /// <summary>
    /// Lucky Lunch: the first cooked food each biome is not used up (Hooks.OnConsumeDelayed / OnReduceUses keep it).
    /// The weaker food is a modifier.
    /// </summary>
    public sealed class Ability_LuckyLunch : Ability
    {
        private readonly Charge _charge = new Charge(perBiome: true);

        public bool TryUse()
        {
            if (!_charge.TryUse(LocalCards.Biome)) return false;
            Hud.Toast("Lucky Lunch: that one is still in your hand");
            return true;
        }

        public override string Status(Character me) => _charge.Available(LocalCards.Biome) ? "Lucky Lunch: ready" : "Lucky Lunch: used this biome";
    }

    /// <summary>
    /// Cleansing Ritual: a curative item used on you also removes 10 from your worst status it did not treat; each
    /// curative use adds 5 Hunger. Curse, Weight and Hunger are never the "worst other status" (Hunger is the drawback).
    /// </summary>
    public sealed class Ability_CleansingRitual : Ability
    {
        private static readonly CharacterAfflictions.STATUSTYPE[] Candidates =
        {
            CharacterAfflictions.STATUSTYPE.Injury, CharacterAfflictions.STATUSTYPE.Poison, CharacterAfflictions.STATUSTYPE.Cold,
            CharacterAfflictions.STATUSTYPE.Hot, CharacterAfflictions.STATUSTYPE.Drowsy, CharacterAfflictions.STATUSTYPE.Spores
        };

        public override void OnCurativeUsed(Character me, CurativeEvent cure)
        {
            float worst = 0f;
            CharacterAfflictions.STATUSTYPE pick = CharacterAfflictions.STATUSTYPE.Injury;
            foreach (var t in Candidates)
            {
                if (cure.Cured != null && cure.Cured.Contains(t)) continue;
                float v = CardStatus.Get(me, t);
                if (v > worst + 0.0001f) { worst = v; pick = t; }
            }
            if (worst > 0f)
            {
                CardStatus.Remove(me, pick, P("bonus", 0.1f));
                Hud.Toast("Cleansing Ritual: " + pick + " eased");
            }
            CardStatus.Add(me, CharacterAfflictions.STATUSTYPE.Hunger, P("hunger", 0.05f));
        }
    }

    /// <summary>
    /// Pack Rat: the heavier carried items are a modifier. The extra inventory slot is NOT implemented: PEAK hard-wires
    /// slot id 3 to the backpack in its inventory code, HUD, hotbar keys and network sync (see README "Known limitations").
    /// The card is disabled by default in the config for that reason.
    /// </summary>
    public sealed class Ability_PackRat : Ability
    {
        public override void OnAcquired(Character me)
        {
            Hud.Toast("Pack Rat: the extra slot is not available in this version (only the weight change applies)");
        }

        public override string Status(Character me) => "Pack Rat: extra slot unavailable";
    }

    /// <summary>Rope Baron / Supply Scout: an item for you next to every newly lit campfire. The host spawns it (net.json SpawnItem).</summary>
    public sealed class Ability_CampfireSpawn : Ability
    {
        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (acquiredThisFire || me.data.dead) return;
            var fire = LocalCards.LastCampfire;
            if (fire == null) return;
            int item = Mathf.RoundToInt(P("item", 0f));
            if (item < 0 || item >= CardTable.SpawnItems.Length) return;
            Vector3 point, normal;
            if (!SafeSpot.NearCampfire(fire, NetSend.LocalActor * 3 + item, out point, out normal))
            {
                if (!SafeSpot.Near(fire.transform.position, 2.5f, 6f, out point)) { Hud.Toast(Card.Name + ": no room next to the fire"); return; }
                point -= Vector3.up * 1f;   // Near returns a torso height; items sit on the ground
            }
            NetSend.RaiseToMaster(new SpawnItem { item = (byte)item, x = point.x, y = point.y, z = point.z });
        }
    }

    /// <summary>
    /// Scavenger's Instinct: food, items and unopened luggage within 15 m are marked on screen (drawn over the world, so
    /// they show through terrain). The hungrier body is a modifier.
    /// </summary>
    public sealed class Ability_ScavengersInstinct : Ability
    {
        private float _nextScan;
        private readonly List<Hud.Marker> _markers = new List<Hud.Marker>();
        private static readonly Dictionary<int, bool> _isFood = new Dictionary<int, bool>();
        private static readonly Color FoodColor = new Color(0.62f, 0.92f, 0.45f, 1f);
        private static readonly Color ItemColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color LuggageColor = new Color(1f, 0.82f, 0.35f, 1f);

        public override void Tick(Character me, float dt)
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.25f;
            float radius = P("radius", 15f);
            _markers.Clear();
            if (!me.data.dead)
            {
                Vector3 c = me.Center;
                foreach (var it in Item.ALL_ITEMS)
                {
                    if (it == null || it.itemState != ItemState.Ground || !it.gameObject.activeInHierarchy) continue;
                    Vector3 p = it.transform.position;
                    float d = Vector3.Distance(p, c);
                    if (d > radius) continue;
                    bool food = IsFood(it);
                    _markers.Add(new Hud.Marker { World = p, Label = NameOf(it), Color = food ? FoodColor : ItemColor });
                }
                foreach (var l in Luggage.ALL_LUGGAGE)
                {
                    if (l == null || !l.gameObject.activeInHierarchy) continue;
                    Vector3 p = l.transform.position;
                    if (Vector3.Distance(p, c) > radius) continue;
                    _markers.Add(new Hud.Marker { World = p + Vector3.up * 0.5f, Label = "Luggage", Color = LuggageColor });
                }
            }
            Hud.SetMarkers(_markers);
        }

        private static bool IsFood(Item it)
        {
            int id = it.GetInstanceID();
            bool f;
            if (!_isFood.TryGetValue(id, out f)) { f = Hooks.IsFood(it); _isFood[id] = f; }
            return f;
        }

        private static string NameOf(Item it)
        {
            try { var n = it.GetItemName(); if (!string.IsNullOrEmpty(n)) return n; } catch { }
            return it.gameObject.name.Replace("(Clone)", "").Trim();
        }

        public override void OnRemoved(Character me)
        {
            _markers.Clear();
            Hud.ClearMarkers();
        }
    }

    /// <summary>
    /// Double Deal: the host deals 2 bonus cards (never card-draw cards, see PickSession). The locked pocket is enforced
    /// by the host in Hooks.OnAddItemPrefix; an item already in that pocket stays there.
    /// </summary>
    public sealed class Ability_DoubleDeal : Ability
    {
        public override void OnAcquired(Character me)
        {
            Hud.Toast("Double Deal: your third pocket is locked for the rest of the run");
        }

        public override string Status(Character me) => "Double Deal: pocket 3 locked";
    }
}
