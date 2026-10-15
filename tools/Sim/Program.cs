using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CampfireCards.Core;
using CampfireCards.Generated;

namespace CampfireCards.Sim
{
    /// <summary>
    /// Scores every card (standalone and in context) and plays Monte Carlo 4-player runs through the real dealer.
    /// Usage: dotnet run --project tools/Sim [-c Release] [-- runs=2000 out=build/sim]
    /// </summary>
    public static class Program
    {
        private const int Players = 4;
        private const int PicksPerRun = 5;      // opening + one campfire per biome
        private const int OfferSize = 3;
        private const float DeadOfferPts = 25f; // below this, the best option feels like nothing

        public static void Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            int runs = 2000;
            string outDir = Path.Combine("build", "sim");
            foreach (var a in args)
            {
                if (a.StartsWith("runs=")) runs = int.Parse(a.Substring(5));
                if (a.StartsWith("out=")) outDir = a.Substring(4);
            }
            Directory.CreateDirectory(outDir);
            var s = new CardSettings();   // sheet defaults: the balance everyone plays

            // pass 1: standalone values without the card-draw cards, to price what a bonus draw is worth
            var standalone = new Dictionary<byte, (float self, float team)>();
            foreach (var def in CardTable.Cards)
                if (!def.DrawsCards) standalone[def.Id] = StandaloneValue(def, s);
            var pool = CardTable.Cards.Where(c => !c.DrawsCards).ToArray();
            Valuer.AvgBonusCardValue = pool.Where(c => c.Rarity != CardRarity.Mythic)
                .Average(c => Headline(standalone[c.Id]));
            Valuer.AvgBonusCardValueToRare = pool.Where(c => c.Rarity <= CardRarity.Rare)
                .Average(c => Headline(standalone[c.Id]));
            foreach (var def in CardTable.Cards)
                if (def.DrawsCards) standalone[def.Id] = StandaloneValue(def, s);

            // pass 2: Monte Carlo through the real dealer
            var stats = CardTable.Cards.ToDictionary(c => c.Id, c => new CardStats());
            var offerBests = new List<float>();
            int offers = 0, deadOffers = 0, fires = 0, firesWithOverlap = 0;
            var topBuilds = new List<(float value, List<byte> cards)>();
            var rng = new Random(20261015);

            for (int run = 0; run < runs; run++)
            {
                var owned = Enumerable.Range(0, Players).Select(_ => new List<byte>()).ToArray();
                var claimed = new HashSet<byte>();
                for (int pick = 0; pick < PicksPerRun; pick++)
                {
                    var handsThisFire = new List<byte[]>();
                    for (int p = 0; p < Players; p++)
                    {
                        var rules = OfferRules.Campfire(OfferSize, solo: false,
                            hasSacrificeTarget: owned[p].Any(id => IsSacrificable(s, id)));
                        rules.ClaimedMythics = claimed;
                        var hand = CardDraw.Offer(rng, s, owned[p], rules);
                        if (hand.Count == 0) continue;
                        foreach (var id in hand) { var d = s.Card(id); if (d != null && d.Rarity == CardRarity.Mythic) claimed.Add(id); }
                        handsThisFire.Add(hand.ToArray());

                        float before = BuildValue(owned[p], s);
                        float best = float.MinValue; byte bestId = hand[0];
                        foreach (var id in hand)
                        {
                            stats[id].Offered++;
                            var cd = s.Card(id);
                            owned[p].Add(id);
                            float v = BuildValue(owned[p], s) - before + 0.5f * Valuer.TeamValue(cd, s);
                            // a draw card's future cards count at pick time (they are not in the build yet)
                            if (cd.DrawsCards) v += Valuer.AbilityValue(cd, s, 1f, drawsResolved: false) - Valuer.AbilityValue(cd, s, 1f, drawsResolved: true);
                            owned[p].RemoveAt(owned[p].Count - 1);
                            stats[id].MarginalSum += v; stats[id].MarginalN++;
                            if (v > best) { best = v; bestId = id; }
                        }
                        offers++; offerBests.Add(best);
                        if (best < DeadOfferPts) deadOffers++;
                        stats[bestId].BestInOffer++;
                        stats[bestId].Picked++;           // greedy policy: take the best
                        owned[p].Add(bestId);
                        var bestDef = s.Card(bestId);
                        if (bestDef != null && bestDef.DrawsCards) DrawBonus(owned[p], bestDef, claimed, rng, s, stats);
                    }
                    fires++;
                    if (handsThisFire.SelectMany(h => h).GroupBy(id => id).Any(g => g.Count() > 1)) firesWithOverlap++;
                }
                for (int p = 0; p < Players; p++)
                {
                    // rank finished builds in the same currency they were drafted in: self plus half the team effect
                    float teamPart = owned[p].Distinct().Sum(id => { var d = s.Card(id); return d == null ? 0f : 0.5f * Valuer.TeamValue(d, s); });
                    topBuilds.Add((BuildValue(owned[p], s) + teamPart, new List<byte>(owned[p])));
                }
            }

            Report(s, standalone, stats, offerBests, offers, deadOffers, fires, firesWithOverlap, topBuilds, runs, outDir);
        }

        private static bool IsSacrificable(CardSettings s, byte id)
        {
            var d = s.Card(id);
            return d != null && d.Rarity != CardRarity.Mythic && d.Ability != "MountainsBargain";
        }

        private static void DrawBonus(List<byte> build, CardDef source, HashSet<byte> claimed, Random rng, CardSettings s, Dictionary<byte, CardStats> stats)
        {
            int count = (int)Math.Round(s.Param(source, "bonusCards", 1f));
            var rules = OfferRules.Campfire(count, solo: false, hasSacrificeTarget: build.Any(id => IsSacrificable(s, id)));
            rules.ExcludeDrawCards = true;
            rules.MaxRarity = source.Ability == "DoubleDown" ? CardRarity.Rare : CardRarity.Mythic;
            rules.ClaimedMythics = claimed;
            foreach (var id in CardDraw.Offer(rng, s, build, rules))
            {
                build.Add(id);
                stats[id].Picked++;
                var d = s.Card(id);
                if (d != null && d.Rarity == CardRarity.Mythic) claimed.Add(id);
            }
        }

        private static (float self, float team) StandaloneValue(CardDef def, CardSettings s)
        {
            var empty = Valuer.FoldBuild(new CardDef[0], s);
            var with = Valuer.FoldBuild(new[] { def }, s);
            float self = Valuer.Score(with) - Valuer.Score(empty) + Valuer.AbilityValue(def, s, with[Channel.bonusStaminaGain]);
            return (self, Valuer.TeamValue(def, s));
        }

        private static float Headline((float self, float team) v) => v.self + 0.5f * v.team;

        private static float BuildValue(List<byte> build, CardSettings s)
        {
            var defs = build.Select(id => s.Card(id)).Where(d => d != null).ToList();
            var r = Valuer.FoldBuild(defs, s);
            float total = Valuer.Score(r);
            foreach (var d in defs.GroupBy(d => d.Id).Select(g => g.First()))   // duplicate abilities count once
                total += Valuer.AbilityValue(d, s, r[Channel.bonusStaminaGain], drawsResolved: true);
            return total;
        }

        private static string TierOf(float v) =>
            v < 10f ? "ASS" : v < 45f ? "weak" : v < 110f ? "decent" : v < 250f ? "strong" : "busted";

        private sealed class CardStats
        {
            public int Offered, Picked, BestInOffer, MarginalN;
            public float MarginalSum;
        }

        private static void Report(CardSettings s, Dictionary<byte, (float self, float team)> standalone,
            Dictionary<byte, CardStats> stats, List<float> offerBests, int offers, int deadOffers,
            int fires, int firesWithOverlap, List<(float value, List<byte> cards)> builds, int runs, string outDir)
        {
            var rows = CardTable.Cards.Select(c => new
            {
                Def = c,
                Self = standalone[c.Id].self,
                Team = standalone[c.Id].team,
                Headline = Headline(standalone[c.Id]),
                St = stats[c.Id],
            }).OrderByDescending(r => r.Headline).ToList();

            var csv = new StringBuilder("key,name,rarity,selfPtsPerBiome,teamPtsPerBiome,headline,tier,offered,pickedGreedy,bestInOfferShare,avgMarginal\n");
            foreach (var r in rows)
            {
                float bestShare = r.St.Offered > 0 ? (float)r.St.BestInOffer / r.St.Offered : 0f;
                float marginal = r.St.MarginalN > 0 ? r.St.MarginalSum / r.St.MarginalN : 0f;
                csv.Append($"{r.Def.Key},\"{r.Def.Name}\",{r.Def.Rarity},{r.Self:0.0},{r.Team:0.0},{r.Headline:0.0},{TierOf(r.Headline)},{r.St.Offered},{r.St.Picked},{bestShare:0.000},{marginal:0.0}\n");
            }
            File.WriteAllText(Path.Combine(outDir, "sim-cards.csv"), csv.ToString());

            offerBests.Sort();
            float Pct(double p) => offerBests[(int)(p * (offerBests.Count - 1))];
            var top = builds.OrderByDescending(b => b.value).Take(builds.Count / 4).ToList();
            var pickRate = CardTable.Cards.ToDictionary(c => c.Id, c => Math.Max(1, stats[c.Id].Picked));
            var inTop = new Dictionary<byte, int>();
            foreach (var b in top) foreach (var id in b.cards.Distinct()) inTop[id] = inTop.TryGetValue(id, out var n) ? n + 1 : 1;
            var chase = inTop.Where(kv => pickRate[kv.Key] >= builds.Count / 100)   // noise guard: picked in >=1% of builds
                .Select(kv => new { Id = kv.Key, Lift = (float)kv.Value / top.Count / ((float)pickRate[kv.Key] / builds.Count) })
                .OrderByDescending(x => x.Lift).Take(10).ToList();
            var pairs = new Dictionary<(byte, byte), int>();
            foreach (var b in top)
            {
                var set = b.cards.Distinct().OrderBy(x => x).ToArray();
                for (int i = 0; i < set.Length; i++)
                    for (int j = i + 1; j < set.Length; j++)
                    {
                        var k = (set[i], set[j]);
                        pairs[k] = pairs.TryGetValue(k, out var n) ? n + 1 : 1;
                    }
            }

            var rep = new StringBuilder();
            rep.AppendLine($"# Campfire Cards feel simulation ({runs} runs x {Players} players x {PicksPerRun} picks, greedy picks, sheet defaults)");
            rep.AppendLine($"\nCurrency: stamina-bar points per biome (full bar = 100; a biome demands ~{Profile.GrossSpend:0} gross). Assumptions in tools/Sim/Model.cs.");
            rep.AppendLine($"\n## Offer feel");
            rep.AppendLine($"- Offers dealt: {offers}; DEAD offers (best option under {DeadOfferPts} pts): {100f * deadOffers / offers:0.0}%");
            rep.AppendLine($"- Best-option value percentiles: p10 {Pct(.10):0}, p25 {Pct(.25):0}, median {Pct(.50):0}, p75 {Pct(.75):0}, p90 {Pct(.90):0}");
            rep.AppendLine($"- Fires where two players were offered the same card: {100f * firesWithOverlap / fires:0.0}%");
            rep.AppendLine("\n## Tier list (standalone value, self + half of what it gives the team)\n");
            rep.AppendLine("| Card | Rarity | Self | Team | Value | Tier | Best-in-offer |");
            rep.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var r in rows)
                rep.AppendLine($"| {r.Def.Name} | {r.Def.Rarity} | {r.Self:0} | {r.Team:0} | {r.Headline:0} | {TierOf(r.Headline)} | {(r.St.Offered > 0 ? 100f * r.St.BestInOffer / r.St.Offered : 0f):0}% |");
            rep.AppendLine("\n## Worth chasing (over-represented in the top quarter of finished builds)\n");
            foreach (var c in chase) rep.AppendLine($"- {s.Card(c.Id).Name} (x{c.Lift:0.0} lift)");
            rep.AppendLine("\n## Most common pairs in top builds\n");
            foreach (var p in pairs.OrderByDescending(kv => kv.Value).Take(8))
                rep.AppendLine($"- {s.Card(p.Key.Item1).Name} + {s.Card(p.Key.Item2).Name} ({p.Value} builds)");
            File.WriteAllText(Path.Combine(outDir, "sim-report.md"), rep.ToString());

            Console.WriteLine($"offers={offers} dead={100f * deadOffers / offers:0.0}% medianBest={Pct(.5):0} overlapFires={100f * firesWithOverlap / fires:0.0}%");
            Console.WriteLine($"avg bonus-card value={Valuer.AvgBonusCardValue:0} (to Rare {Valuer.AvgBonusCardValueToRare:0})");
            Console.WriteLine("Top 8 cards:  " + string.Join(" | ", rows.Take(8).Select(r => $"{r.Def.Name} {r.Headline:0}")));
            Console.WriteLine("Bottom 8:     " + string.Join(" | ", rows.Skip(rows.Count - 8).Select(r => $"{r.Def.Name} {r.Headline:0}")));
            Console.WriteLine($"Wrote {Path.Combine(outDir, "sim-cards.csv")} and sim-report.md");
        }
    }
}
