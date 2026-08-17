using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Deploy;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor.Balance
{
    public sealed class BalanceDpsMeasurement
    {
        internal BalanceDpsMeasurement(
            int stageIndex,
            string levelId,
            int unitCount,
            int unlockedCellCount,
            long allyDamage,
            double battleSeconds,
            double winRate,
            double meanDurationSeconds,
            double enemyDeathYP90,
            int peakConcurrentUnits,
            double meanDroppedCoins)
        {
            StageIndex = stageIndex;
            LevelId = levelId;
            UnitCount = unitCount;
            UnlockedCellCount = unlockedCellCount;
            AllyDamage = allyDamage;
            BattleSeconds = battleSeconds;
            WinRate = winRate;
            MeanDurationSeconds = meanDurationSeconds;
            EnemyDeathYP90 = enemyDeathYP90;
            PeakConcurrentUnits = peakConcurrentUnits;
            MeanDroppedCoins = meanDroppedCoins;
        }

        public int StageIndex { get; }
        public string LevelId { get; }
        public int UnitCount { get; }
        public int UnlockedCellCount { get; }
        public long AllyDamage { get; }
        public double BattleSeconds { get; }
        public double WinRate { get; }
        public double MeanDurationSeconds { get; }
        public double EnemyDeathYP90 { get; }
        public int PeakConcurrentUnits { get; }
        public double MeanDroppedCoins { get; }

        /// <summary>
        /// Damage the board actually lands per second of battle. Weighted by the armour mix by
        /// construction: it is measured against the stage's own waves, so every point of it already
        /// went through the type multiplier and the armour mitigation that mix produces.
        /// </summary>
        public double EffectiveDps => BattleSeconds <= 0d ? 0d : AllyDamage / BattleSeconds;
    }

    /// <summary>
    /// Measures the ally DPS that WO-F1 §A.2 requires the enemy volume to be derived from.
    ///
    /// <para>Deliberately measured, not modelled. A closed-form estimate has to assume how long each
    /// unit survives, how much of its damage lands on the right armour class, and how much is
    /// overkill; WO-E4 already showed that route producing a lineup with the highest theoretical DPS
    /// in the table and a zero win rate. Running the board and dividing total damage by total battle
    /// seconds sidesteps all three assumptions.</para>
    ///
    /// <para>The stage-one and stage-five boards are the frozen reference lineups; stages two to
    /// four are taken from the same simulated run so the whole curve comes from one player.</para>
    /// </summary>
    public static class BalanceDpsProbe
    {
        public const uint DefaultSeed = 0xE1002026u;

        public static IReadOnlyList<BalanceDpsMeasurement> MeasureAllStages(
            GameConfig config,
            uint seed = DefaultSeed,
            int gamesPerStage = 12,
            int maxDegreeOfParallelism = 0)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            IReadOnlyList<RunAccumulationStage> run = RunAccumulationSimulator.Simulate(config, seed);
            var levels = new List<LevelDef>(config.Levels);
            levels.Sort((left, right) => left.StageIndex.CompareTo(right.StageIndex));

            var lineups = new List<BalanceLineup>(levels.Count);
            var levelIds = new List<string>(levels.Count);
            for (int index = 0; index < levels.Count; index++)
            {
                LevelDef level = levels[index];
                RunAccumulationStage stage = run[index];
                lineups.Add(ToLineup(config, stage, level));
                levelIds.Add(level.Id);
            }

            var request = new BalanceRunRequest(
                gamesPerStage,
                levelIds,
                lineups,
                seed,
                // Just past the 150s acceptance band. A probe battle still running at 170s is a
                // stalemate, and letting it grind on to 300 costs minutes of wall clock per game
                // — enough to make the tuning loop unusable — while adding no information.
                170d,
                false,
                null,
                maxDegreeOfParallelism);
            BalanceRunReport report = new BalanceRunner().Run(config, request);

            var result = new List<BalanceDpsMeasurement>(levels.Count);
            for (int index = 0; index < levels.Count; index++)
            {
                LevelDef level = levels[index];
                BalanceCohortSummary cohort = report.Cohorts.Single(value =>
                    value.LevelId == level.Id);
                long allyDamage = report.Units
                    .Where(value => value.LevelId == level.Id && value.Team == BattleTeam.Ally)
                    .Sum(value => value.TotalDamage);
                double battleSeconds = report.Battles
                    .Where(value => value.LevelId == level.Id)
                    .Sum(value => value.DurationSeconds);

                result.Add(new BalanceDpsMeasurement(
                    level.StageIndex,
                    level.Id,
                    run[index].UnitCount,
                    run[index].UnlockedCellCount,
                    allyDamage,
                    battleSeconds,
                    cohort.WinRate,
                    cohort.MeanDurationSeconds,
                    cohort.EnemyDeathYP90,
                    cohort.PeakConcurrentUnits,
                    cohort.MeanDroppedCoins));
            }

            return result.AsReadOnly();
        }

        /// <summary>Turns one simulated stage into a runnable lineup pinned to that stage's level.</summary>
        public static BalanceLineup ToLineup(
            GameConfig config,
            RunAccumulationStage stage,
            LevelDef level)
        {
            var spawns = new List<BalanceLineupSpawn>(stage.Deployments.Count);
            bool siege = false;
            for (int index = 0; index < stage.Deployments.Count; index++)
            {
                DeployedUnitState deployment = stage.Deployments[index];
                siege |= config.GetUnit(deployment.UnitId).AtkType == AttackType.Siege;
                spawns.Add(new BalanceLineupSpawn(
                    deployment.UnitId,
                    deployment.Level,
                    deployment.Col,
                    deployment.Row));
            }

            return new BalanceLineup(
                "probe_s" + stage.StageIndex.ToString(CultureInfo.InvariantCulture),
                "DPS probe, stage " + stage.StageIndex.ToString(CultureInfo.InvariantCulture),
                siege,
                spawns,
                stage.UnlockedCells,
                new[] { level.Id });
        }

        [MenuItem("HanziDefend/Balance/Measure Ally DPS")]
        public static void MeasureFromMenu()
        {
            Debug.Log(Describe(MeasureAllStages(GameConfig.Load())));
        }

        public static string Describe(IReadOnlyList<BalanceDpsMeasurement> measurements)
        {
            var text = new StringBuilder();
            text.AppendLine("stage | units | cells | effective DPS | win rate | mean duration | death Y p90 | peak units | drops");
            for (int index = 0; index < measurements.Count; index++)
            {
                BalanceDpsMeasurement value = measurements[index];
                text.Append("S").Append(value.StageIndex)
                    .Append(" | ").Append(value.UnitCount)
                    .Append(" | ").Append(value.UnlockedCellCount)
                    .Append(" | ").Append(value.EffectiveDps.ToString("F0", CultureInfo.InvariantCulture))
                    .Append(" | ").Append(value.WinRate.ToString("P1", CultureInfo.InvariantCulture))
                    .Append(" | ").Append(value.MeanDurationSeconds.ToString("F1", CultureInfo.InvariantCulture)).Append('s')
                    .Append(" | ").Append(value.EnemyDeathYP90.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" | ").Append(value.PeakConcurrentUnits)
                    .Append(" | ").Append(value.MeanDroppedCoins.ToString("F0", CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            return text.ToString();
        }
    }
}
