using System;
using System.Collections.Generic;
using CampfireCards.Generated;

namespace CampfireCards.Core
{
    /// <summary>
    /// Every tunable number of the card system. Defaults come from the design sheets (Generated/CardTable.cs);
    /// the plugin overwrites them from the BepInEx config. Unity-free so it can be unit tested.
    /// </summary>
    public sealed class CardSettings
    {
        /// <summary>The most cards one pick may offer: the config clamp and the network validation share it.</summary>
        public const int MaxOfferCount = 10;

        public float[] RarityWeights;
        public int OfferCount;
        public bool MythicsEnabled = true;
        /// <summary>false (default) = a player can never own the same card twice in a run.</summary>
        public bool AllowDuplicates;
        /// <summary>Soft cap: past this combined stamina-cost reduction (0.6 = 60%), further discounts count at reduced strength.</summary>
        public float StaminaCostCap = 0.6f;
        /// <summary>Normal cards combined cannot pull maximum stamina below this (0.5 = 50).</summary>
        public float MaxStaminaFloor = 0.5f;
        /// <summary>false = cards lose their drawbacks: hurting modifiers and ability penalties are skipped.</summary>
        public bool DrawbacksEnabled = true;
        /// <summary>Benefit strength per difficulty tier (Tenderfoot-Peak, Ascent 1-3, 4-6, 7-8): 1 = as the cards say.</summary>
        public float[] DifficultyStrength;
        /// <summary>
        /// The strength in play right now (set every frame from the run's Ascent, Core.DifficultyScale). ModValue and Param
        /// return benefits scaled by it; drawbacks and Mythics are never scaled. 1 outside a run and in tests.
        /// </summary>
        public float BenefitStrength = 1f;

        public readonly bool[] Enabled;
        public readonly float[][] ModValues;
        public readonly Dictionary<string, float>[] Params;

        private readonly Dictionary<byte, int> _indexById = new Dictionary<byte, int>();

        public CardSettings()
        {
            var cards = CardTable.Cards;
            RarityWeights = (float[])CardTable.RarityWeights.Clone();
            DifficultyStrength = (float[])CardTable.DifficultyStrength.Clone();
            OfferCount = CardTable.OfferCount;
            Enabled = new bool[cards.Length];
            ModValues = new float[cards.Length][];
            Params = new Dictionary<string, float>[cards.Length];
            for (int i = 0; i < cards.Length; i++)
            {
                Enabled[i] = true;
                ModValues[i] = new float[cards[i].Mods.Length];
                for (int m = 0; m < cards[i].Mods.Length; m++) ModValues[i][m] = cards[i].Mods[m].Value;
                Params[i] = new Dictionary<string, float>(cards[i].Params);
                _indexById[cards[i].Id] = i;
            }
        }

        public static CardSettings Current = new CardSettings();

        public int IndexOf(byte id)
        {
            int i;
            return _indexById.TryGetValue(id, out i) ? i : -1;
        }

        public CardDef Card(byte id)
        {
            int i = IndexOf(id);
            return i < 0 ? null : CardTable.Cards[i];
        }

        public bool IsEnabled(CardDef c)
        {
            int i = IndexOf(c.Id);
            if (i < 0 || !Enabled[i]) return false;
            if (c.Rarity == CardRarity.Mythic && !MythicsEnabled) return false;
            return true;
        }

        /// <summary>A modifier as it plays now: a benefit scaled by the difficulty's strength (Core.DifficultyScale).</summary>
        public float ModValue(CardDef c, int modIndex)
        {
            float raw = RawModValue(c, modIndex);
            var m = c.Mods[modIndex];
            var ch = ChannelTable.Defs[(int)m.Channel];
            // drawbacks off: a hurting modifier becomes "no change" (Mythics have no drawbacks to drop)
            if (!DrawbacksEnabled && DifficultyScale.Hurts(raw, m.Op, ch.IsMul, ch.HigherIsBetter)) return ch.IsMul ? 1f : 0f;
            if (DifficultyScale.Unscaled(BenefitStrength) || c.Rarity == CardRarity.Mythic) return raw;
            return DifficultyScale.Mod(raw, m.Op, ch.IsMul, ch.HigherIsBetter, DifficultyScale.CardStrength(c, BenefitStrength));
        }

        /// <summary>A modifier as configured (the Ascent 7-8 value), for the config and the lobby settings check.</summary>
        public float RawModValue(CardDef c, int modIndex)
        {
            int i = IndexOf(c.Id);
            return i < 0 ? c.Mods[modIndex].Value : ModValues[i][modIndex];
        }

        /// <summary>A card parameter as it plays now: below strength 1 benefit amounts shrink and waits grow; above 1 the reverse.</summary>
        public float Param(CardDef c, string name, float fallback = 0f)
        {
            float raw = RawParam(c, name, fallback);
            if (DifficultyScale.Unscaled(BenefitStrength) || c.Rarity == CardRarity.Mythic) return raw;
            return DifficultyScale.Param(c.Key, name, raw, DifficultyScale.CardStrength(c, BenefitStrength));
        }

        /// <summary>A card parameter as configured (the Ascent 7-8 value).</summary>
        public float RawParam(CardDef c, string name, float fallback = 0f)
        {
            int i = IndexOf(c.Id);
            float v;
            if (i >= 0 && Params[i].TryGetValue(name, out v)) return v;
            return c.Param(name, fallback);
        }

        /// <summary>The strength for a run's Ascent (Ascents.currentAscent: -1 Tenderfoot, 0 Peak, 1..8).</summary>
        public float StrengthFor(int ascent)
        {
            int tier = DifficultyScale.TierOf(ascent);
            return DifficultyStrength != null && tier < DifficultyStrength.Length ? Math.Max(0.05f, Math.Min(1.5f, DifficultyStrength[tier])) : 1f;
        }

        public float Weight(CardRarity r)
        {
            int i = (int)r;
            return i < RarityWeights.Length ? Math.Max(0f, RarityWeights[i]) : 0f;
        }
    }
}
