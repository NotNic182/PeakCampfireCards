using System;
using System.Collections.Generic;
using CampfireCards.Core;
using CampfireCards.Generated;
using UnityEngine;

namespace CampfireCards.Abilities
{
    /// <summary>
    /// A card's custom behaviour (cards.json "ability"). One instance exists per owned card, only on the owner's client.
    /// Instances are created when the card is acquired and dropped when it is removed or the run ends, so every timer,
    /// latch and listener they hold goes away with them (no global subscriptions).
    /// </summary>
    public abstract class Ability
    {
        public CardDef Card;
        protected CardSettings S => CardSettings.Current;
        protected float P(string name, float fallback = 0f) => S.Param(Card, name, fallback);
        protected static float Now => Time.time;

        /// <summary>The card was just picked or granted (not on state sync / reconnect).</summary>
        public virtual void OnAcquired(Character me) { }
        /// <summary>The card left the collection (sacrifice) or the run ended. Undo anything not driven by modifiers.</summary>
        public virtual void OnRemoved(Character me) { }
        public virtual void Tick(Character me, float dt) { }
        /// <summary>A campfire was newly lit. acquiredThisFire = the card was picked during this fire's session.</summary>
        public virtual void OnCampfireLit(Character me, bool acquiredThisFire) { }
        public virtual void OnBiomeStarted(Character me, int biome) { }
        public virtual void OnFoodEaten(Character me, FoodEvent food) { }
        public virtual void OnCurativeUsed(Character me, CurativeEvent cure) { }
        /// <summary>The local scout is going down. Return true if this card revived it (only one revival per event).</summary>
        public virtual bool TryRevive(Character me, int incapEvent) => false;
        /// <summary>Manual activation (ability key). Return true if it did something.</summary>
        public virtual bool Activate(Character me) => false;
        /// <summary>Line for the ability bar and whether it reads as ready; AbilityStatus.None when there is nothing to show.</summary>
        public virtual AbilityStatus Status(Character me) => AbilityStatus.None;
        public virtual bool IsManual => false;
        public virtual bool ConditionFlag(Condition c) => false;

        /// <summary>
        /// The ability bar line for a once-per-biome charge (ui.json AbilityBar): "Name: ready" or "Name: used this biome",
        /// with the key in brackets when the card is pressed. ready overrides the ready word ("place anchor").
        /// </summary>
        protected AbilityStatus BiomeCharge(bool available, string key = null, string ready = null) =>
            available ? AbilityStatus.Ok(Line(key, ready ?? Ui_AbilityBar.Ready)) : AbilityStatus.Wait(Line(key, Ui_AbilityBar.UsedThisBiome));

        /// <summary>"Name: state", or "Name [key]: state".</summary>
        protected string Line(string key, string state) =>
            key == null ? string.Format(Ui_AbilityBar.StatusTemplate, Card.Name, state) : string.Format(Ui_AbilityBar.KeyedTemplate, Card.Name, key, state);

        /// <summary>The toast for pressing a card that has no charge left this biome.</summary>
        protected string AlreadyUsedThisBiome => string.Format(Ui_AbilityBar.AlreadyUsedThisBiome, Card.Name);

        /// <summary>A status or stamina amount as players read it (0.25 -> "25", 0.075 -> "7.5").</summary>
        public static string Points(float v) => (v * 100f).ToString("0.#");

        /// <summary>A status by the name players know it by (the game's Hot status is "Heat").</summary>
        public static string StatusName(CharacterAfflictions.STATUSTYPE t) => t == CharacterAfflictions.STATUSTYPE.Hot ? "Heat" : t.ToString();
    }

    /// <summary>An ability bar line and its state: Ready shows green, waiting (cooldown, used, placed) shows grey.</summary>
    public readonly struct AbilityStatus
    {
        public readonly string Text;
        public readonly bool Ready;

        public AbilityStatus(string text, bool ready) { Text = text; Ready = ready; }

        public bool IsEmpty => string.IsNullOrEmpty(Text);
        public static AbilityStatus None => default;
        public static AbilityStatus Ok(string text) => new AbilityStatus(text, true);
        public static AbilityStatus Wait(string text) => new AbilityStatus(text, false);
    }

    public struct FoodEvent
    {
        public Item Item;
        public bool Cooked;
        /// <summary>Hunger the item removed (after card multipliers), status units.</summary>
        public float HungerRemoved;
        /// <summary>The eater's Injury just before this item's own effects ran.</summary>
        public float InjuryBefore;
    }

    public struct CurativeEvent
    {
        public Item Item;
        /// <summary>Statuses the item itself removed, so a bonus cleanse picks a different one.</summary>
        public HashSet<CharacterAfflictions.STATUSTYPE> Cured;
    }

    /// <summary>Finds the ability class for a card by name: cards.json ability "X" maps to class Ability_X.</summary>
    public static class AbilityRegistry
    {
        private static Dictionary<string, Type> _types;

        public static Ability Create(CardDef card)
        {
            if (string.IsNullOrEmpty(card.Ability)) return null;
            if (_types == null)
            {
                _types = new Dictionary<string, Type>();
                foreach (var t in typeof(Ability).Assembly.GetTypes())
                    if (!t.IsAbstract && typeof(Ability).IsAssignableFrom(t) && t.Name.StartsWith("Ability_"))
                        _types[t.Name.Substring("Ability_".Length)] = t;
            }
            Type type;
            if (!_types.TryGetValue(card.Ability, out type))
            {
                Plugin.Log.LogError($"Card {card.Name}: no ability class Ability_{card.Ability}");
                return null;
            }
            var a = (Ability)Activator.CreateInstance(type);
            a.Card = card;
            return a;
        }
    }
}
