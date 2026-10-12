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
        public float[] RarityWeights;
        public int OfferCount;
        public bool MythicsEnabled = true;
        /// <summary>false (default) = a player can never own the same card twice in a run.</summary>
        public bool AllowDuplicates;
        /// <summary>Soft cap: past this combined stamina-cost reduction (0.6 = 60%), further discounts count at reduced strength.</summary>
        public float StaminaCostCap = 0.6f;
        /// <summary>Normal cards combined cannot pull maximum stamina below this (0.5 = 50).</summary>
        public float MaxStaminaFloor = 0.5f;

        public readonly bool[] Enabled;
        public readonly float[][] ModValues;
        public readonly Dictionary<string, float>[] Params;

        private readonly Dictionary<byte, int> _indexById = new Dictionary<byte, int>();

        public CardSettings()
        {
            var cards = CardTable.Cards;
            RarityWeights = (float[])CardTable.RarityWeights.Clone();
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

        public float ModValue(CardDef c, int modIndex)
        {
            int i = IndexOf(c.Id);
            return i < 0 ? c.Mods[modIndex].Value : ModValues[i][modIndex];
        }

        public float Param(CardDef c, string name, float fallback = 0f)
        {
            int i = IndexOf(c.Id);
            float v;
            if (i >= 0 && Params[i].TryGetValue(name, out v)) return v;
            return c.Param(name, fallback);
        }

        public float Weight(CardRarity r)
        {
            int i = (int)r;
            return i < RarityWeights.Length ? Math.Max(0f, RarityWeights[i]) : 0f;
        }
    }
}
