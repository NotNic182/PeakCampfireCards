using System.Collections.Generic;
using CampfireCards.Core;
using CampfireCards.Generated;
using CampfireCards.Net;
using UnityEngine;

namespace CampfireCards.Cards
{
    public static partial class Effects
    {
        private static readonly List<ActiveMod> _mods = new List<ActiveMod>();
        private static readonly HashSet<long> _seenTeamMods = new HashSet<long>();

        public static void Recompute(Character me)
        {
            _mods.Clear();
            _seenTeamMods.Clear();
            SharedBurdenOwner = int.MinValue;
            SharedBurdenFactor = 1f;
            float sharedBest = float.MaxValue;
            var s = CardSettings.Current;
            int localActor = NetSend.LocalActor;

            foreach (var kv in CardsByActor)
            {
                bool isMe = kv.Key == localActor;
                Character owner = null;
                float dist = float.MaxValue;
                if (!isMe && PlayerHandler.TryGetCharacter(kv.Key, out owner) && owner != null && owner.data != null && !owner.data.dead)
                    dist = Vector3.Distance(owner.Center, me.Center);

                foreach (var id in kv.Value)
                {
                    var def = s.Card(id);
                    if (def == null) continue;
                    float radius = s.Param(def, "radius", 6f);
                    for (int i = 0; i < def.Mods.Length; i++)
                    {
                        var m = def.Mods[i];
                        bool apply = false;
                        switch (m.Target)
                        {
                            case ModTarget.self:
                                apply = isMe && Condition(m.When, me, def);
                                break;
                            case ModTarget.team:
                                apply = !isMe && dist <= radius;
                                break;
                            case ModTarget.teamAndSelf:
                                apply = isMe || dist <= radius;
                                break;
                        }
                        if (!apply) continue;
                        if (m.Target != ModTarget.self)
                        {
                            if (def.Ability == "SharedBurden")
                            {
                                // Compare every owner before ordinary aura de-duplication. Only the nearest owner
                                // supplies protection, and the Injury hook sends that owner the prevented share.
                                if (dist < sharedBest) { sharedBest = dist; SharedBurdenOwner = kv.Key; SharedBurdenFactor = s.ModValue(def, i); }
                                continue;
                            }
                            long key = ((long)def.Id << 8) | (uint)i;   // the same aura from two owners counts once
                            if (!_seenTeamMods.Add(key)) continue;
                        }
                        _mods.Add(new ActiveMod { CardId = def.Id, ModIndex = i, Channel = m.Channel, Op = m.Op, Value = s.ModValue(def, i), Rarity = def.Rarity });
                    }
                }
            }
            AddPenalties(_mods, localActor);
            Local.Fold(_mods, s);
            ApplyFields(me);
        }
    }
}
