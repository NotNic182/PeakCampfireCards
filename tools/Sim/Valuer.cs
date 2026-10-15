using System;
using System.Collections.Generic;
using System.Linq;
using CampfireCards.Core;
using CampfireCards.Generated;

namespace CampfireCards.Sim
{
    /// <summary>
    /// Converts a build (a set of cards) into one score in stamina points per biome, folding the build's modifiers
    /// with the mod's REAL stacking code (ModResult: soft cost cap, max-stamina floor, Mythic bypass) after weighting
    /// each conditional modifier by its condition's uptime. Abilities get explicit per-biome formulas below that read
    /// the live sheet parameters. A card's value is the score with it minus the score without it.
    /// </summary>
    public static class Valuer
    {
        /// <summary>How much of an incoming status point sticks as standing bar load (temp statuses decay fast).</summary>
        private static readonly Dictionary<string, float> Persist = new Dictionary<string, float>
        {
            { "hunger", 0.4f }, { "cold", 0.3f }, { "hot", 0.3f }, { "poison", 0.3f }, { "spores", 0.3f },
            { "drowsy", 0.3f }, { "thorns", 0.35f }, { "injury", 0.5f },
            // curse never decays in the game and survives revives: a point gained stays for the rest of the run
            { "curse", 1f }, { "weight", 1f },
        };

        public static float StickOf(string status) => Persist[status];

        /// <summary>Fold the build's self-facing modifiers, each scaled by its condition's uptime.</summary>
        public static ModResult FoldBuild(IEnumerable<CardDef> build, CardSettings s)
        {
            var mods = new List<ActiveMod>();
            foreach (var def in build)
            {
                for (int i = 0; i < def.Mods.Length; i++)
                {
                    var m = def.Mods[i];
                    if (m.Target == ModTarget.team) continue;   // teammates only; valued separately
                    float u = Profile.UptimeOf(m.When);
                    float v = s.ModValue(def, i);
                    float eff = m.Op == ModOp.mul ? 1f + (v - 1f) * u : m.Op == ModOp.add ? v * u : v;
                    mods.Add(new ActiveMod { CardId = def.Id, ModIndex = i, Channel = m.Channel, Op = m.Op, Value = eff, Rarity = def.Rarity });
                }
            }
            var r = new ModResult();
            r.Fold(mods, s);
            return r;
        }

        /// <summary>
        /// The biome score of a folded build, in pts: minus everything the mountain charges (spend + occupied bar,
        /// bar points valued at Profile.BarValue), plus bar gains, reach/jump utility and the survival credit.
        /// </summary>
        public static float Score(ModResult r)
        {
            float climbCost = r[Channel.staminaUsage] * r[Channel.climbStamina];
            float sprintCost = r[Channel.staminaUsage] * r[Channel.sprintStamina];
            float climbTime = (float)Math.Pow(Math.Max(0.2f, r[Channel.climbSpeed]), -Profile.SpeedExponent);
            float sprintTime = (float)Math.Pow(Math.Max(0.2f, r[Channel.sprintSpeed]), -Profile.SpeedExponent);

            float spend =
                (Profile.ClimbSeconds * Profile.ClimbDrain * climbTime + Profile.ClimbJumps * Profile.JumpCost
                 + Profile.HangSeconds * Profile.HangDrain + Profile.RopeVineSeconds * Profile.RopeDrain) * climbCost
                + Profile.SprintSeconds * Profile.SprintDrain * sprintCost * sprintTime;

            float load = 0f;
            load += StatusLoad("hunger", HungerIncoming(r), r, 1f);
            load += StatusLoad("cold", Profile.Status["cold"].incoming * r[Channel.coldRate] * r[Channel.negativeStatusRate], r, r[Channel.coldDecay]);
            load += StatusLoad("hot", Profile.Status["hot"].incoming * r[Channel.heatRate] * r[Channel.negativeStatusRate], r, r[Channel.heatDecay]);
            load += StatusLoad("poison", Profile.Status["poison"].incoming * r[Channel.poisonRate] * r[Channel.negativeStatusRate], r, r[Channel.poisonDecay]);
            load += StatusLoad("spores", Profile.Status["spores"].incoming * r[Channel.sporesRate] * r[Channel.negativeStatusRate], r, r[Channel.sporesDecay]);
            load += StatusLoad("drowsy", Profile.Status["drowsy"].incoming * r[Channel.drowsyRate] * r[Channel.negativeStatusRate], r, 1f);
            load += StatusLoad("thorns", Profile.Status["thorns"].incoming * r[Channel.thornsRate] * r[Channel.negativeStatusRate], r, 1f / Math.Max(0.3f, r[Channel.thornRemovalTime]));
            load += InjuryLoad(r);
            load += CurseLoad(r);
            load += WeightLoad(r);

            float barGain = r[Channel.maxStamina] * 100f * Profile.BarValue;

            float utility =
                (r[Channel.grabReach] - 1f) * Profile.GrabReachUtilityPer100
                + (r[Channel.jumpHeight] - 1f) * Profile.JumpHeightUtilityPer100
                + (r[Channel.climbSpeed] - 1f) * Profile.ClimbPaceUtilityPer100
                + (r[Channel.sprintSpeed] - 1f) * Profile.SprintPaceUtilityPer100
                + (1f - r[Channel.slipForce]) * Profile.SlipUtilityPer100
                + (r[Channel.huddleStrength] - 1f) * 25f                       // warmth on demand
                + (1f - r[Channel.petrifyRate]) * 25f;                         // kiln-biome insurance
            // helpReach and healGiven are teammate-facing: valued in TeamValue at the same half weight as auras

            // symmetric: reducing fall injury buys survival, raising it costs survival
            float fallMul = r[Channel.fallInjury] * r[Channel.allInjury];
            float survival = (1f - fallMul) * Profile.FatalRiskPerBiome * Profile.DeathCost * 0.5f;

            return -spend - load * Profile.BarValue + barGain + utility + survival;
        }

        private static float StatusLoad(string status, float incoming, ModResult r, float decayMul)
        {
            float baseIn = Profile.Status[status].incoming;
            float baseLoad = Profile.Status[status].load;
            float load = baseIn > 0f ? baseLoad * (incoming / baseIn) : baseLoad;
            return load / (float)Math.Pow(Math.Max(0.3f, decayMul), 0.7);
        }

        private static float HungerIncoming(ModResult r)
        {
            float incoming = Profile.Status["hunger"].incoming * r[Channel.hungerRate] * r[Channel.negativeStatusRate];
            // better nutrition keeps the average lower: meals remove more than the baseline assumed
            float foodMul = r[Channel.foodHunger] * (Profile.CookedMealShare * r[Channel.cookedFoodHunger] + (1f - Profile.CookedMealShare) * r[Channel.uncookedFoodHunger]);
            float extraRemoved = Profile.MealsPerBiome * Profile.HungerPerMeal * (foodMul - 1f);
            return Math.Max(0f, incoming - extraRemoved);
        }

        private static float InjuryLoad(ModResult r)
        {
            float incoming = (Profile.FallsPerBiome * Profile.MeanFallInjury * r[Channel.fallInjury]
                              + 6f * r[Channel.nonFallInjury] + 2f * r[Channel.dynamiteInjury]) * r[Channel.allInjury];
            float healMul = r[Channel.healReceived] * r[Channel.healSelfUse];
            float extraHealed = Profile.HealsPerBiome * Profile.InjuryPerHeal * (healMul - 1f);
            float baseIn = Profile.Status["injury"].incoming;
            return Profile.Status["injury"].load * Math.Max(0.05f, (incoming - extraHealed * 0.5f) / baseIn);
        }

        private static float CurseLoad(ModResult r)
        {
            float reviveShare = Profile.CursePerRevive * Profile.RevivesPerBiomePerPlayer;   // curse lands on the revived scout once
            float otherShare = Profile.Status["curse"].incoming - reviveShare;
            float incoming = (reviveShare * r[Channel.reviveCurse] + otherShare) * r[Channel.curseRate] * r[Channel.negativeStatusRate];
            return Profile.Status["curse"].load * incoming / Profile.Status["curse"].incoming;
        }

        private static float WeightLoad(ModResult r)
        {
            float mix = Profile.LooseShare + Profile.HeldShare * r[Channel.heldItemWeight] + Profile.BackpackShare * r[Channel.backpackItemWeight];
            // totalWeight multiplies everything, the occasional carried scout included (BuildStats.CarriedWeight)
            return (Profile.CarriedWeightLoad * r[Channel.itemWeight] * mix + 1.5f * r[Channel.carryScoutWeight]) * r[Channel.totalWeight];
        }

        // ------------------------------------------------------------------ abilities

        /// <summary>Mean standalone value of a non-card-draw card; fills in what Clown Luggage / Dishonor draws are worth.</summary>
        public static float AvgBonusCardValue = 60f;
        public static float AvgBonusCardValueToRare = 50f;

        /// <summary>Self-facing pts/biome for the card's ability (0 when it has none). yellowMul scales yellow-stamina
        /// gains. drawsResolved: the build already contains the cards this one drew, so expectation terms are dropped.</summary>
        public static float AbilityValue(CardDef def, CardSettings s, float yellowMul, bool drawsResolved = false)
        {
            float P(string name, float fb = 0f) => s.Param(def, name, fb);
            float Y = Profile.YellowFactor * yellowMul;
            float pts(float statusUnits) => statusUnits * 100f;
            switch (def.Ability)
            {
                case "": return 0f;
                case "CampfireBonus": return pts(P("bonus", 0.15f)) * Y;                       // yellow at every fire (1/biome)
                case "CursedCrown":
                    return pts(P("bonusNow")) * Y / 4f + pts(P("bonusFire")) * Y
                           - pts(P("curseBudget")) / 4f * StickOf("curse") * Profile.BarValue;
                case "LoadedDice":
                {
                    float chance = P("chance", 0.65f);
                    return chance * pts(P("bonus")) * Y
                           - (1f - chance) * pts(P("injury")) * StickOf("injury") * Profile.BarValue;
                }
                case "BloodBattery":
                    // the injury arrives on a button the player times (next to heals): poison-grade persistence
                    return pts(P("bonus")) * Y - pts(P("injury")) * 0.3f * Profile.BarValue;
                case "Meditation": return pts(P("bonus")) * Y;
                case "SecondWind":
                {
                    float uses = Math.Min(Profile.BiomeSeconds / Math.Max(30f, P("cooldown", 180f)), 1.5f);
                    return uses * pts(P("restore")) * Y * 1.2f;                                 // emergency timing premium
                }
                case "EmergencyPurge":
                    return pts(P("remove")) * 0.5f /*threshold reached*/ * 0.5f * Profile.BarValue;
                case "OverhangRegen":   // the ability also refunds the grip drain the game charged while hanging
                    return pts(P("regenPerSecond")) * Profile.HangSeconds * 0.7f + Profile.HangSeconds * Profile.HangDrain;
                case "SecondSpring":                                                            // double jump on cooldown
                    return 50f - 2f * pts(P("drowsy")) * StickOf("drowsy") * Profile.BarValue;
                case "PulseClimber": return 0f;                                                 // its value is the conditional mod
                case "MomentumFiend":
                    return -2f * pts(P("drowsy")) * StickOf("drowsy") * Profile.BarValue;       // crash penalty; speed is the mod
                case "MushroomCycle":                                                           // BingBong's random power burst
                {
                    float perBiome = Profile.BiomeSeconds / Math.Max(60f, P("interval", 300f));
                    return perBiome * 25f;                                                      // avg of the three 20s powers
                }
                case "HoldRefund": return Profile.HangSeconds * Profile.HangDrain * P("refund", 0.75f);
                case "PhoenixClause":   // one save per run; the skipped pick is only owed when the save fires
                    return Profile.DeathCost * 0.45f / 4f - 0.45f * AvgBonusCardValue / 4f;
                case "DelayedConsequences":
                {
                    float totalMul = 1f - P("defer") + P("defer") * P("keep");
                    return Profile.FallsPerBiome * Profile.MeanFallInjury * (1f - totalMul) * StickOf("injury") * Profile.BarValue + 8f;
                }
                case "SafetyNet":
                    return Profile.FatalRiskPerBiome * 0.5f * (Profile.DeathCost - pts(P("injury")) * StickOf("injury") * Profile.BarValue);
                case "MountainsBargain":
                    return -AvgBonusCardValue - pts(P("curse")) * StickOf("curse") * Profile.BarValue;   // the +35 max stamina is its mod
                case "DoubleDeal":
                {
                    float penalty = ClownPenaltyCost(s, def);
                    float draw = drawsResolved ? 0f : P("bonusCards", 2f) * AvgBonusCardValue;  // resolved builds hold the real cards
                    return draw - penalty;
                }
                case "DoubleDown":
                    return (drawsResolved ? 0f : P("bonusCards", 2f) * AvgBonusCardValueToRare)
                           - pts(P("curse")) * StickOf("curse") * Profile.BarValue
                           - 15f;                                                               // the reduced next offer costs choice
                case "CampChef":                                                                // penalty for raw cookables; benefit is the mod
                    return -0.3f * pts(P("poison")) * StickOf("poison") * Profile.BarValue;
                case "HungryHands":
                    return Profile.MealsPerBiome * Profile.UptimeOf(Condition.injury20) * pts(P("heal")) * 0.5f * Profile.BarValue;
                case "LuckyLunch": return 15f;                                                  // one meal per biome not consumed
                case "CleansingRitual":
                    return Profile.CurativesPerBiome * pts(P("bonus")) * 0.5f * Profile.BarValue;
                case "PocketTradeoff": return -20f;                                             // pocket 3 locked; weight cut is the mod
                case "CampfireSpawn": return Profile.InjuryPerHeal * 0.3f * Profile.BarValue;   // a bandage at every fire
                case "NextCampfireSpawn": return 60f / 4f;                                      // one Balloon Bunch per run
                case "ScavengersInstinct": return 25f;                                          // found food/heal items
                case "HelpCost": return -1.5f * pts(P("stamina"));                              // pull fee; reach is team utility
                case "SharedBurden": return -15f;                                               // absorbed teammate injury (capped per hit)
                case "CampfireMeal": return Profile.Status["hunger"].load * 0.5f * Profile.BarValue;   // hunger cleared each fire
                case "HerosArrival": return 0.3f * pts(P("bonus")) * Y;                         // you are reviver or revived in ~0.3 events/biome
                case "EternalCampfire":                                                         // shared auto-revive leaves 40 injury
                    return Profile.FatalRiskPerBiome * (Profile.DeathCost - 40f * StickOf("injury") * Profile.BarValue);
                default:
                    throw new InvalidOperationException($"No value formula for ability {def.Ability} ({def.Name})");
            }
        }

        /// <summary>Clown Luggage's random permanent penalty: the mean cost of its four 10% options on the baseline.</summary>
        private static float ClownPenaltyCost(CardSettings s, CardDef def)
        {
            float P(string name, float fb) => s.Param(def, name, fb);
            float climb = SingleModCost(Channel.climbStamina, P("climbPenalty", 1.1f), s);
            float sprint = SingleModCost(Channel.sprintStamina, P("sprintPenalty", 1.1f), s);
            float injury = SingleModCost(Channel.allInjury, P("injuryPenalty", 1.1f), s);
            float hunger = SingleModCost(Channel.hungerRate, P("hungerPenalty", 1.1f), s);
            return (climb + sprint + injury + hunger) / 4f;
        }

        /// <summary>Cost (positive) or value (negative) of one always-on mul mod applied to the empty baseline.</summary>
        public static float SingleModCost(Channel ch, float mul, CardSettings s)
        {
            var baseline = new ModResult(); baseline.Fold(new ActiveMod[0], s);
            var with = new ModResult();
            with.Fold(new[] { new ActiveMod { Channel = ch, Op = ModOp.mul, Value = mul, Rarity = CardRarity.Common } }, s);
            return Score(baseline) - Score(with);
        }

        /// <summary>Value this card hands the rest of the lobby (team-target mods on the baseline, plus team ability shares).</summary>
        public static float TeamValue(CardDef def, CardSettings s)
        {
            float total = 0f;
            var baseline = new ModResult(); baseline.Fold(new ActiveMod[0], s);
            float baseScore = Score(baseline);
            for (int i = 0; i < def.Mods.Length; i++)
            {
                var m = def.Mods[i];
                float v = s.ModValue(def, i);
                // self-target but teammate-facing: helping hands and heals used ON teammates count here, at the
                // same half weight every aura gets, not as personal power
                if (m.Channel == Channel.helpReach) { total += (v - 1f) * Profile.HelpReachTeamUtilityPer100 * Profile.Teammates; continue; }
                if (m.Channel == Channel.healGiven) { total += (v - 1f) * 0.5f * Profile.InjuryPerHeal * 0.3f * Profile.BarValue; continue; }
                if (m.Target == ModTarget.self) continue;
                float u = Profile.UptimeOf(m.When == Condition.always ? Condition.teammateNear : m.When);
                float eff = m.Op == ModOp.mul ? 1f + (v - 1f) * u : m.Op == ModOp.add ? v * u : v;
                var r = new ModResult();
                r.Fold(new[] { new ActiveMod { CardId = def.Id, ModIndex = i, Channel = m.Channel, Op = m.Op, Value = eff, Rarity = def.Rarity } }, s);
                total += (Score(r) - baseScore) * Profile.Teammates;
            }
            switch (def.Ability)
            {
                case "HerosArrival": total += 0.3f * s.Param(def, "bonus", 1f) * 100f * Profile.YellowFactor; break;   // one partner per event
                case "EternalCampfire":
                    total += Profile.FatalRiskPerBiome * (Profile.DeathCost - 40f * StickOf("injury") * Profile.BarValue) * Profile.Teammates;
                    break;
            }
            return total;
        }
    }
}
