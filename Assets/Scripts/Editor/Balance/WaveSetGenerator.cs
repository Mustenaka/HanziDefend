using System;
using System.Collections.Generic;
using System.Globalization;
using HanziDefend.Data;

namespace HanziDefend.Editor.Balance
{
    /// <summary>One act's shape: how long it runs, how hard it pushes, and what it is made of.</summary>
    public sealed class WaveActSpec
    {
        public WaveActSpec(
            int act,
            float durationSeconds,
            float gapBeforeSeconds,
            float pressureCoefficient,
            float unarmoredShare,
            float lightShare,
            float heavyShare,
            int squadSize,
            float squadIntervalSeconds,
            float maximumSpawnGapSeconds)
        {
            MaximumSpawnGapSeconds = maximumSpawnGapSeconds;
            Act = act;
            DurationSeconds = durationSeconds;
            GapBeforeSeconds = gapBeforeSeconds;
            PressureCoefficient = pressureCoefficient;
            UnarmoredShare = unarmoredShare;
            LightShare = lightShare;
            HeavyShare = heavyShare;
            SquadSize = squadSize;
            SquadIntervalSeconds = squadIntervalSeconds;
        }

        public int Act { get; }
        public float DurationSeconds { get; }

        /// <summary>Silence held before this act's first combatant. Zero for act 1.</summary>
        public float GapBeforeSeconds { get; }

        /// <summary>WO-F1 §A.2 pressure coefficient: 0.55 / 0.85 / 1.05.</summary>
        public float PressureCoefficient { get; }

        public float UnarmoredShare { get; }
        public float LightShare { get; }
        public float HeavyShare { get; }

        /// <summary>
        /// Largest squad the act will send; one squad is one wave, so this is also the wave size.
        /// It is a ceiling rather than a fixed size — see <see cref="MaximumSpawnGapSeconds"/>.
        /// </summary>
        public int SquadSize { get; }

        /// <summary>Seconds between two members of the same squad.</summary>
        public float SquadIntervalSeconds { get; }

        /// <summary>
        /// Longest silence tolerated <i>inside</i> the act. Squads shrink below
        /// <see cref="SquadSize"/> when the act's body count is too small to fill its length —
        /// a thin act must arrive as a steady trickle, because a hole inside an act that rivals the
        /// pause between acts destroys the very signal the pause is there to send.
        /// </summary>
        public float MaximumSpawnGapSeconds { get; }
    }

    /// <summary>Everything needed to derive one minor stage's wave set from a measured ally DPS.</summary>
    public sealed class WaveSetSpec
    {
        public WaveSetSpec(
            string waveSetId,
            int stageIndex,
            float measuredAllyDps,
            float rushShareOfUnarmored,
            float stageDifficultyScalar,
            IReadOnlyList<WaveActSpec> acts)
        {
            WaveSetId = waveSetId;
            StageIndex = stageIndex;
            MeasuredAllyDps = measuredAllyDps;
            RushShareOfUnarmored = rushShareOfUnarmored;
            StageDifficultyScalar = stageDifficultyScalar;
            Acts = acts;
        }

        public string WaveSetId { get; }
        public int StageIndex { get; }

        /// <summary>
        /// Effective damage per second the stage's reference lineup actually lands, weighted by the
        /// armour mix it faces. Measured with <see cref="BalanceDpsProbe"/>, never estimated —
        /// the whole point of WO-F1 §A.2 is that the enemy budget is derived from this number.
        /// </summary>
        public float MeasuredAllyDps { get; }

        /// <summary>
        /// Fraction of the unarmored slot given to <c>e_lang</c>, the one unit that runs past the
        /// front line straight at the camp. It rises with the stage index so later stages actually
        /// leak, which is what makes the camp's hit points mean something.
        /// </summary>
        public float RushShareOfUnarmored { get; }

        /// <summary>
        /// Per-stage difficulty ramp, on top of the act coefficients.
        ///
        /// <para>WO-F1 §A.2's formula alone cannot produce the two different win-rate bands the
        /// acceptance targets ask for. Deriving the budget from the player's own measured DPS makes
        /// difficulty roughly <i>constant</i> across stages by construction — a bigger board simply
        /// gets a bigger schedule — and measurement confirmed it: every stage landed at 75-100%
        /// against a 55-75% / 30-50% pair of targets. Worse, a larger board is more resilient than
        /// its raw DPS suggests, so stage five came out no harder than stage one. This scalar is the
        /// term that makes later stages actually harder; the act coefficients keep their 0.55 /
        /// 0.85 / 1.05 shape untouched.</para>
        /// </summary>
        public float StageDifficultyScalar { get; }

        public IReadOnlyList<WaveActSpec> Acts { get; }

        public float BudgetFor(WaveActSpec act)
        {
            return MeasuredAllyDps * act.DurationSeconds * act.PressureCoefficient * StageDifficultyScalar;
        }
    }

    /// <summary>
    /// Derives waves.json from per-stage hit-point budgets instead of hand-authored waves.
    ///
    /// <para>WO-F1 §A.2 requires the enemy volume to be reverse-derived from measured ally DPS, and
    /// the answer is 100–220 combatants per stage across five stages. Hand-typing that is neither
    /// reviewable nor re-derivable: when the ally table moves, the only honest response is to re-run
    /// the derivation, and that is only possible if the derivation is code. Every number in the
    /// generated file traces back to one of the specs below plus the armour mix in M1-04 §6.</para>
    ///
    /// <para>Deterministic by construction — no RNG anywhere. Spawn scatter is still random at
    /// battle time, drawn from <c>Rng.Battle</c> exactly as before.</para>
    /// </summary>
    public static class WaveSetGenerator
    {
        /// <summary>Beat of quiet before the first enemy of the battle.</summary>
        public const float OpeningSilenceSeconds = 2f;

        private const float UnarmoredSpreadX = 2.6f;
        private const float ArmoredSpreadX = 2.1f;
        private const float CastleSpreadX = 0f;

        /// <summary>Fill units for each armour class, in the order the apportionment prefers them.</summary>
        private static readonly string[] LightUnitIds = { "e_liu", "e_shan" };

        private static readonly string[] HeavyUnitIds = { "tie", "zqi" };

        private const string FillUnitId = "zu";
        private const string RushUnitId = "e_lang";
        private const string CastleUnitId = "bld_cheng";

        /// <summary>
        /// Builds one wave set. Takes the raw unit catalog rather than a whole
        /// <see cref="GameConfig"/> because it needs nothing but base hit points — and because the
        /// generator must stay runnable when waves.json itself does not load yet, which is exactly
        /// the state the file is in every time the act structure changes.
        /// </summary>
        public static WaveSetDef Generate(UnitCatalog units, WaveSetSpec spec)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            var config = UnitHpTable.From(units);

            var waves = new List<WaveDef>();
            var startTimes = new List<double>();
            double previousActLastSpawn = 0d;
            int waveIndex = 1;

            for (int actIndex = 0; actIndex < spec.Acts.Count; actIndex++)
            {
                WaveActSpec act = spec.Acts[actIndex];
                double actStart = actIndex == 0
                    ? OpeningSilenceSeconds
                    : previousActLastSpawn + act.GapBeforeSeconds;

                var squads = BuildSquads(config, spec, act);
                bool castleOpensAct = act.Act == WaveActs.Third;
                int slotCount = squads.Count + (castleOpensAct ? 1 : 0);
                int largestSquad = 1;
                for (int index = 0; index < squads.Count; index++)
                {
                    if (squads[index].Count > largestSquad)
                    {
                        largestSquad = squads[index].Count;
                    }
                }

                double squadSpan = (largestSquad - 1) * act.SquadIntervalSeconds;
                double spacing = slotCount <= 1
                    ? 0d
                    : Math.Max(0.1d, (act.DurationSeconds - squadSpan) / (slotCount - 1));

                int slot = 0;
                if (castleOpensAct)
                {
                    // The castle is the act's opening beat, not its reward for clearing the act.
                    waves.Add(new WaveDef
                    {
                        Index = waveIndex++,
                        Act = act.Act,
                        RewardRank = EnemyRank.Boss,
                        Spawns = new[]
                        {
                            new WaveSpawnDef
                            {
                                UnitId = CastleUnitId,
                                Level = 1,
                                Count = 1,
                                SpreadX = CastleSpreadX,
                                IntervalSec = 0f
                            }
                        }
                    });
                    startTimes.Add(actStart);
                    slot++;
                }

                for (int index = 0; index < squads.Count; index++)
                {
                    Squad squad = squads[index];
                    waves.Add(new WaveDef
                    {
                        Index = waveIndex++,
                        Act = act.Act,
                        RewardRank = squad.RewardRank,
                        Spawns = new[]
                        {
                            new WaveSpawnDef
                            {
                                UnitId = squad.UnitId,
                                Level = 1,
                                Count = squad.Count,
                                SpreadX = squad.SpreadX,
                                IntervalSec = act.SquadIntervalSeconds
                            }
                        }
                    });
                    startTimes.Add(actStart + slot * spacing);
                    slot++;
                }

                previousActLastSpawn = startTimes[startTimes.Count - 1]
                                       + (squads.Count == 0
                                           ? 0d
                                           : (squads[squads.Count - 1].Count - 1) * act.SquadIntervalSeconds);
            }

            ApplyDelays(waves, startTimes);
            return new WaveSetDef { Id = spec.WaveSetId, Waves = waves.ToArray() };
        }

        /// <summary>
        /// Converts absolute wave start times into the relative <c>delaySec</c> the schedule stores.
        /// Values are rounded to centiseconds so the JSON stays readable and diffable.
        /// </summary>
        private static void ApplyDelays(List<WaveDef> waves, List<double> startTimes)
        {
            double previous = 0d;
            for (int index = 0; index < waves.Count; index++)
            {
                double rounded = Math.Round(startTimes[index], 2, MidpointRounding.AwayFromZero);
                waves[index].DelaySec = (float)Math.Max(0d, rounded - previous);
                previous = rounded;
            }
        }

        /// <summary>
        /// Turns one act's hit-point budget into an ordered squad list.
        ///
        /// <para>The armour mix in M1-04 §6 counts <i>bodies</i>, not hit points, so the combatant
        /// count is solved first from the mix-weighted average body, and the budget only sets how
        /// many bodies there are. Squads are then dealt round-robin across the armour classes so a
        /// heavy squad lands roughly every third wave instead of all of them arriving together.</para>
        /// </summary>
        private static List<Squad> BuildSquads(UnitHpTable config, WaveSetSpec spec, WaveActSpec act)
        {
            float budget = spec.BudgetFor(act);
            float unarmoredHp = Blend(
                config,
                new[] { FillUnitId, RushUnitId },
                new[] { 1f - spec.RushShareOfUnarmored, spec.RushShareOfUnarmored });
            float lightHp = Blend(config, LightUnitIds, new[] { 0.5f, 0.5f });
            float heavyHp = Blend(config, HeavyUnitIds, new[] { 0.5f, 0.5f });
            float averageBodyHp = act.UnarmoredShare * unarmoredHp
                                  + act.LightShare * lightHp
                                  + act.HeavyShare * heavyHp;
            int bodies = Math.Max(1, (int)Math.Round(budget / averageBodyHp, MidpointRounding.AwayFromZero));

            // Squad size follows from the act's length, not the other way round: an act carrying
            // few bodies still has to fill its own running time, so it sends them in smaller
            // groups more often rather than in three fat clumps with silence between them.
            int desiredSlots = Math.Max(
                1,
                (int)Math.Ceiling(act.DurationSeconds / Math.Max(0.5f, act.MaximumSpawnGapSeconds)));
            int squadSize = Math.Max(
                1,
                Math.Min(
                    act.SquadSize,
                    (int)Math.Ceiling(bodies / (double)desiredSlots)));

            int[] perClass = Apportion(
                bodies,
                new[] { act.UnarmoredShare, act.LightShare, act.HeavyShare });

            var unarmored = new List<Squad>();
            var light = new List<Squad>();
            var heavy = new List<Squad>();
            AppendSquads(
                unarmored,
                Apportion(perClass[0], new[] { 1f - spec.RushShareOfUnarmored, spec.RushShareOfUnarmored }),
                new[] { FillUnitId, RushUnitId },
                squadSize,
                UnarmoredSpreadX,
                EnemyRank.Normal);
            AppendSquads(
                light,
                Apportion(perClass[1], new[] { 0.5f, 0.5f }),
                LightUnitIds,
                squadSize,
                ArmoredSpreadX,
                EnemyRank.Normal);
            AppendSquads(
                heavy,
                Apportion(perClass[2], new[] { 0.5f, 0.5f }),
                HeavyUnitIds,
                squadSize,
                ArmoredSpreadX,
                // Heavy squads are the ones that actually hurt, so they are the ones that pay more.
                // Tying Elite to armour class keeps the reward tied to the difficulty rather than to
                // an arbitrary wave number.
                EnemyRank.Elite);

            return Interleave(unarmored, light, heavy);
        }

        private static void AppendSquads(
            List<Squad> output,
            int[] counts,
            string[] unitIds,
            int squadSize,
            float spreadX,
            EnemyRank rewardRank)
        {
            for (int index = 0; index < counts.Length; index++)
            {
                int remaining = counts[index];
                while (remaining > 0)
                {
                    // A trailing single is folded into the squad before it rather than shipped as a
                    // one-unit wave: a lone body arriving on its own reads as a mistake, not a wave.
                    int size = remaining <= squadSize ? remaining : squadSize;
                    if (remaining - size == 1 && size > 1)
                    {
                        size++;
                    }

                    output.Add(new Squad(unitIds[index], size, spreadX, rewardRank));
                    remaining -= size;
                }
            }
        }

        /// <summary>
        /// Deals the three armour classes into one stream, keeping each class evenly spread by
        /// always taking from whichever class is furthest behind its own share of the deck.
        /// </summary>
        private static List<Squad> Interleave(List<Squad> first, List<Squad> second, List<Squad> third)
        {
            var sources = new[] { first, second, third };
            var cursors = new int[sources.Length];
            int total = first.Count + second.Count + third.Count;
            var result = new List<Squad>(total);

            for (int step = 0; step < total; step++)
            {
                int best = -1;
                double bestProgress = double.PositiveInfinity;
                for (int index = 0; index < sources.Length; index++)
                {
                    if (cursors[index] >= sources[index].Count)
                    {
                        continue;
                    }

                    // Progress is "how far through my own queue am I", so the class with the fewest
                    // squads still gets spread across the whole act instead of bunching at the front.
                    double progress = (cursors[index] + 0.5d) / sources[index].Count;
                    if (progress < bestProgress)
                    {
                        bestProgress = progress;
                        best = index;
                    }
                }

                if (best < 0)
                {
                    break;
                }

                result.Add(sources[best][cursors[best]]);
                cursors[best]++;
            }

            return result;
        }

        /// <summary>Largest-remainder apportionment: shares sum to the total exactly, no drift.</summary>
        internal static int[] Apportion(int total, float[] shares)
        {
            var result = new int[shares.Length];
            if (total <= 0)
            {
                return result;
            }

            double shareSum = 0d;
            for (int index = 0; index < shares.Length; index++)
            {
                shareSum += shares[index];
            }

            if (shareSum <= 0d)
            {
                result[0] = total;
                return result;
            }

            var remainders = new double[shares.Length];
            int assigned = 0;
            for (int index = 0; index < shares.Length; index++)
            {
                double exact = total * shares[index] / shareSum;
                result[index] = (int)Math.Floor(exact);
                remainders[index] = exact - result[index];
                assigned += result[index];
            }

            while (assigned < total)
            {
                int best = 0;
                double bestRemainder = double.NegativeInfinity;
                for (int index = 0; index < shares.Length; index++)
                {
                    if (remainders[index] > bestRemainder)
                    {
                        bestRemainder = remainders[index];
                        best = index;
                    }
                }

                result[best]++;
                remainders[best] = double.NegativeInfinity;
                assigned++;
            }

            return result;
        }

        private static float Blend(UnitHpTable config, string[] unitIds, float[] weights)
        {
            float total = 0f;
            float weightSum = 0f;
            for (int index = 0; index < unitIds.Length; index++)
            {
                total += weights[index] * config.BaseHp(unitIds[index]);
                weightSum += weights[index];
            }

            return weightSum <= 0f ? 1f : total / weightSum;
        }

        /// <summary>Base hit points by unit id — the only thing the generator reads from unit data.</summary>
        private sealed class UnitHpTable
        {
            private readonly Dictionary<string, float> baseHpById;

            private UnitHpTable(Dictionary<string, float> baseHpById)
            {
                this.baseHpById = baseHpById;
            }

            internal static UnitHpTable From(UnitCatalog units)
            {
                var table = new Dictionary<string, float>(StringComparer.Ordinal);
                UnitDef[] entries = units.Units ?? Array.Empty<UnitDef>();
                for (int index = 0; index < entries.Length; index++)
                {
                    UnitDef unit = entries[index];
                    if (unit != null && !string.IsNullOrEmpty(unit.Id) && unit.Hp != null)
                    {
                        table[unit.Id] = unit.Hp.Base;
                    }
                }

                return new UnitHpTable(table);
            }

            internal float BaseHp(string unitId)
            {
                if (!baseHpById.TryGetValue(unitId, out float value))
                {
                    throw new KeyNotFoundException(
                        $"Wave generation references unknown unit '{unitId}'.");
                }

                return value;
            }
        }

        /// <summary>Human-readable derivation trace, for the review report and DECISIONS.md.</summary>
        public static string DescribeBudget(GameConfig config, WaveSetSpec spec)
        {
            WaveSetDef generated = Generate(config.UnitCatalog, spec);
            WaveTimeline timeline = WaveTimeline.Compile(config, generated);
            var text = new System.Text.StringBuilder();
            text.Append(spec.WaveSetId)
                .Append(" (stage ").Append(spec.StageIndex)
                .Append(", measured ally DPS ")
                .Append(spec.MeasuredAllyDps.ToString("F0", CultureInfo.InvariantCulture))
                .AppendLine(")");
            for (int index = 0; index < spec.Acts.Count; index++)
            {
                WaveActSpec act = spec.Acts[index];
                WaveActSummary summary = timeline.GetAct(act.Act);
                text.Append("  act ").Append(act.Act)
                    .Append(": budget ")
                    .Append(spec.BudgetFor(act).ToString("F0", CultureInfo.InvariantCulture))
                    .Append(" = ").Append(spec.MeasuredAllyDps.ToString("F0", CultureInfo.InvariantCulture))
                    .Append(" x ").Append(act.DurationSeconds.ToString("F0", CultureInfo.InvariantCulture))
                    .Append("s x ").Append(act.PressureCoefficient.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" -> built ").Append(summary.CombatantHp.ToString("F0", CultureInfo.InvariantCulture))
                    .Append(" hp / ").Append(summary.CombatantCount).Append(" bodies")
                    .Append(", ").Append(summary.DurationSeconds.ToString("F1", CultureInfo.InvariantCulture))
                    .Append("s, gap before ")
                    .Append(summary.GapBeforeSeconds.ToString("F1", CultureInfo.InvariantCulture))
                    .Append("s, longest hole ")
                    .Append(summary.LongestInternalGapSeconds.ToString("F2", CultureInfo.InvariantCulture))
                    .Append("s, armour ")
                    .Append(summary.UnarmoredRate.ToString("P0", CultureInfo.InvariantCulture)).Append('/')
                    .Append(summary.LightRate.ToString("P0", CultureInfo.InvariantCulture)).Append('/')
                    .Append(summary.HeavyRate.ToString("P0", CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            return text.ToString();
        }

        private readonly struct Squad
        {
            internal Squad(string unitId, int count, float spreadX, EnemyRank rewardRank)
            {
                UnitId = unitId;
                Count = count;
                SpreadX = spreadX;
                RewardRank = rewardRank;
            }

            internal string UnitId { get; }
            internal int Count { get; }
            internal float SpreadX { get; }
            internal EnemyRank RewardRank { get; }
        }
    }
}
