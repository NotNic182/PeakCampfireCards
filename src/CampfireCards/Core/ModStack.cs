using System;
using System.Collections.Generic;
using CampfireCards.Generated;

namespace CampfireCards.Core
{
    /// <summary>One modifier that currently applies to a scout (its condition already holds).</summary>
    public struct ActiveMod
    {
        public byte CardId;
        public int ModIndex;
        public Channel Channel;
        public ModOp Op;
        public float Value;
        public CardRarity Rarity;
    }

    /// <summary>
    /// Folds active modifiers into one value per channel, always from scratch (no stat drift):
    /// mul channels multiply, add channels add; normal cards are capped (stamina-cost reduction, max-stamina floor)
    /// before Mythic modifiers are applied, so Mythics can go past the caps; a 'set' override wins over everything.
    /// Unity-free so it can be unit tested.
    /// </summary>
    public sealed class ModResult
    {
        private readonly float[] _normalMul = new float[ChannelTable.Count];
        private readonly float[] _mythicMul = new float[ChannelTable.Count];
        private readonly float[] _normalAdd = new float[ChannelTable.Count];
        private readonly float[] _mythicAdd = new float[ChannelTable.Count];
        private readonly bool[] _hasSet = new bool[ChannelTable.Count];
        private readonly float[] _set = new float[ChannelTable.Count];
        private readonly float[] _final = new float[ChannelTable.Count];
        public readonly HashSet<byte> Sources = new HashSet<byte>();

        public ModResult()
        {
            Reset();
        }

        public void Reset()
        {
            for (int i = 0; i < ChannelTable.Count; i++)
            {
                _normalMul[i] = 1f; _mythicMul[i] = 1f; _normalAdd[i] = 0f; _mythicAdd[i] = 0f; _hasSet[i] = false; _set[i] = 0f;
                _final[i] = ChannelTable.Defs[i].IsMul ? 1f : 0f;
            }
            Sources.Clear();
        }

        public void Fold(IEnumerable<ActiveMod> mods, CardSettings s)
        {
            Reset();
            foreach (var m in mods)
            {
                int c = (int)m.Channel;
                bool mythic = m.Rarity == CardRarity.Mythic;
                Sources.Add(m.CardId);
                switch (m.Op)
                {
                    case ModOp.mul:
                        if (mythic) _mythicMul[c] *= Math.Max(0f, m.Value); else _normalMul[c] *= Math.Max(0f, m.Value);
                        break;
                    case ModOp.add:
                        if (mythic) _mythicAdd[c] += m.Value; else _normalAdd[c] += m.Value;
                        break;
                    case ModOp.set:
                        _hasSet[c] = true; _set[c] = m.Value;
                        break;
                }
            }
            for (int i = 0; i < ChannelTable.Count; i++)
            {
                var def = ChannelTable.Defs[i];
                float v;
                if (def.IsMul)
                {
                    float normal = _normalMul[i];
                    if (def.Cap == CapGroup.staminaCost) normal = Math.Max(normal, 1f - s.StaminaCostCap);
                    v = normal * _mythicMul[i];
                }
                else
                {
                    float normal = _normalAdd[i];
                    if (def.Cap == CapGroup.maxStamina) normal = Math.Max(normal, s.MaxStaminaFloor - 1f);
                    v = normal + _mythicAdd[i];
                }
                _final[i] = _hasSet[i] ? _set[i] : v;
            }
        }

        public float this[Channel c] => _final[(int)c];
    }
}
