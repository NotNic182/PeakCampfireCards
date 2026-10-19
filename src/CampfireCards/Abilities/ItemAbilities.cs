using System.Collections.Generic;
using CampfireCards.Cards;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using CampfireCards.Ui;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>Camp Chef: cooked food removes more Hunger (modifier); food that could have been cooked but was not adds Poison.</summary>
    public sealed class Ability_CampChef : Ability
    {
        public override void OnFoodEaten(Character me, FoodEvent food)
        {
            if (food.Cooked || food.Item == null) return;
            var cooking = food.Item.cooking;
            if (cooking == null || !cooking.canBeCooked) return;   // packaged snacks cannot be cooked: no penalty
            CardStatus.Penalty(me, CharacterAfflictions.STATUSTYPE.Poison, P("poison", 0.075f));
        }
    }

    /// <summary>Hungry Hands: eating while Injury is at least 20 removes 10 Injury. The weaker food is a modifier.</summary>
    public sealed class Ability_HungryHands : Ability
    {
        public override void OnFoodEaten(Character me, FoodEvent food)
        {
            if (food.InjuryBefore < P("threshold", 0.2f) - 0.0001f) return;
            CardStatus.Remove(me, CharacterAfflictions.STATUSTYPE.Injury, P("heal", 0.075f));
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
            Hud.Toast(Card.Name + ": that food is still in your hand");
            return true;
        }

        private readonly Charge _hint = new Charge(perBiome: true);

        /// <summary>Uncooked food was eaten while the save was still available: say once per biome that only cooked food counts.</summary>
        public void HintUncooked()
        {
            if (_charge.Available(LocalCards.Biome) && _hint.TryUse(LocalCards.Biome))
                Hud.Toast(Card.Name + ": only cooked food is kept. Cook it at a campfire first.");
        }

        public override AbilityStatus Status(Character me) => BiomeCharge(_charge.Available(LocalCards.Biome), ready: Ui_AbilityBar.Ready + " (cooked food)");
    }

    /// <summary>
    /// Cleansing Ritual: a curative item or any food also removes a random amount (min..max) from your worst status it
    /// did not treat. Curse, Weight and Hunger are never the "worst other status".
    /// </summary>
    public sealed class Ability_CleansingRitual : Ability
    {
        private static readonly CharacterAfflictions.STATUSTYPE[] Candidates =
        {
            CharacterAfflictions.STATUSTYPE.Injury, CharacterAfflictions.STATUSTYPE.Poison, CharacterAfflictions.STATUSTYPE.Cold,
            CharacterAfflictions.STATUSTYPE.Hot, CharacterAfflictions.STATUSTYPE.Drowsy, CharacterAfflictions.STATUSTYPE.Spores
        };
        private static readonly HashSet<CharacterAfflictions.STATUSTYPE> FoodCured =
            new HashSet<CharacterAfflictions.STATUSTYPE> { CharacterAfflictions.STATUSTYPE.Hunger };
        /// <summary>A curative that also counts as food fires both hooks in one use; cleanse only once per frame.</summary>
        private float _lastCleanse = -1f;

        public override void OnCurativeUsed(Character me, CurativeEvent cure) => Cleanse(me, cure.Cured);

        public override void OnFoodEaten(Character me, FoodEvent food) => Cleanse(me, FoodCured);

        private void Cleanse(Character me, HashSet<CharacterAfflictions.STATUSTYPE> treated)
        {
            if (Time.unscaledTime == _lastCleanse) return;
            CharacterAfflictions.STATUSTYPE pick;
            if (CurativeSelection.TryHighestOther(Candidates, treated, t => CardStatus.Get(me, t), out pick))
            {
                _lastCleanse = Time.unscaledTime;
                float amount = UnityEngine.Random.Range(P("min", 0.025f), P("max", 0.125f));
                CardStatus.Remove(me, pick, amount);
                Hud.Toast($"{Card.Name}: -{Points(amount)} {StatusName(pick)}");
            }
        }
    }

    /// <summary>Half weight in exchange for blocking new use of pocket three; existing items can still be taken out.</summary>
    public sealed class Ability_PocketTradeoff : Ability
    {
        public override void OnAcquired(Character me)
        {
            if (S.DrawbacksEnabled) Hud.Toast(Card.Name + ": your third pocket is locked while you hold this card");
        }

        public override AbilityStatus Status(Character me) =>
            S.DrawbacksEnabled ? AbilityStatus.Wait(Line(null, "pocket 3 locked")) : AbilityStatus.None;
    }

    /// <summary>
    /// Scoutmaster's Care Package: an item for you next to every newly lit campfire. The owner only names the item and
    /// the fire; the host checks the card, spawns it once per fire and places it next to that fire (net.json SpawnItem),
    /// and rides a random food and a random item along with the bandage.
    /// </summary>
    public sealed class Ability_CampfireSpawn : Ability
    {
        public override void OnCampfireLit(Character me, bool acquiredThisFire)
        {
            if (acquiredThisFire || me.data.dead) return;
            var fire = LocalCards.LastCampfire;
            if (fire == null) return;
            int item = Mathf.RoundToInt(P("item", 0f));
            if (item < 0 || item >= CardTable.SpawnItems.Length) return;
            NetSend.RaiseToMaster(new SpawnItem { item = (byte)item, segment = (byte)fire.advanceToSegment });
        }
    }

    /// <summary>
    /// Scavenger's Instinct (TSA): nothing shows on its own. While a reveal key is held, that key's category is marked
    /// on screen (drawn over the world, so it shows through terrain): ScavengerFood = food, ScavengerLuggage = unopened
    /// luggage, ScavengerMisc = every other ground item. Keys can be held together; releasing them clears the markers.
    /// </summary>
    public sealed class Ability_ScavengersInstinct : Ability
    {
        private float _nextScan;
        private int _lastMask;
        private readonly List<Hud.Marker> _markers = new List<Hud.Marker>();
        private static readonly Dictionary<int, bool> _isFood = new Dictionary<int, bool>();
        private static readonly Color FoodColor = new Color(0.62f, 0.92f, 0.45f, 1f);
        private static readonly Color ItemColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color LuggageColor = new Color(1f, 0.82f, 0.35f, 1f);

        public override void Tick(Character me, float dt)
        {
            bool food = Controls.IsHeld(Controls.Act.ScavengerFood);
            bool luggage = Controls.IsHeld(Controls.Act.ScavengerLuggage);
            bool misc = Controls.IsHeld(Controls.Act.ScavengerMisc);
            int mask = (food ? 1 : 0) | (luggage ? 2 : 0) | (misc ? 4 : 0);
            if (me.data.dead) mask = 0;
            if (mask == 0)
            {
                if (_lastMask != 0) { _markers.Clear(); Hud.SetMarkers(_markers); }
                _lastMask = 0;
                return;
            }
            if (mask != _lastMask) _nextScan = 0f;   // a key went down or up: redraw now, not up to 0.25 s later
            _lastMask = mask;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.25f;

            float radius = P("radius", 12f);
            _markers.Clear();
            Vector3 c = me.Center;
            if (food || misc)
                foreach (var it in Item.ALL_ITEMS)
                {
                    if (it == null || it.itemState != ItemState.Ground || !it.gameObject.activeInHierarchy) continue;
                    Vector3 p = it.transform.position;
                    if (Vector3.Distance(p, c) > radius) continue;
                    bool isFood = IsFood(it);
                    if (isFood ? !food : !misc) continue;
                    _markers.Add(new Hud.Marker { World = p, Label = NameOf(it), Color = isFood ? FoodColor : ItemColor });
                }
            if (luggage)
                foreach (var l in Luggage.ALL_LUGGAGE)
                {
                    // the game only drops opened luggage from this list on the host, and scout statues are luggage too
                    if (l == null || !l.gameObject.activeInHierarchy || l.IsOpen || l is RespawnChest) continue;
                    Vector3 p = l.transform.position;
                    if (Vector3.Distance(p, c) > radius) continue;
                    _markers.Add(new Hud.Marker { World = p + Vector3.up * 0.5f, Label = "Luggage", Color = LuggageColor });
                }
            Hud.SetMarkers(_markers);
        }

        /// <summary>The ability bar names the three reveal keys, so the card teaches itself.</summary>
        public override AbilityStatus Status(Character me) =>
            AbilityStatus.Ok(Line(
                Controls.Label(Controls.Act.ScavengerFood) + " / " + Controls.Label(Controls.Act.ScavengerLuggage) + " / " + Controls.Label(Controls.Act.ScavengerMisc),
                Ui_AbilityBar.ScavengerHold));

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
            _lastMask = 0;
            _markers.Clear();
            Hud.ClearMarkers();
        }
    }

}
