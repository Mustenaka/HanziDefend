using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HanziDefend.Editor.Balance
{
    public readonly struct BalanceAcceptanceMetrics
    {
        public BalanceAcceptanceMetrics(
            double hundredGameWallClockSeconds,
            int timeoutCount,
            double stageOneWinRate,
            double stageFiveWinRate,
            double stageOneMeanDurationSeconds,
            double stageFiveMeanDurationSeconds,
            double stageOneHeavyRate,
            double stageFiveHeavyRate,
            double worstZeroAttackDeathRate,
            double worstEnemyLifetimeP50,
            double netDifficultyMonotonicSlack)
        {
            WorstZeroAttackDeathRate = worstZeroAttackDeathRate;
            WorstEnemyLifetimeP50 = worstEnemyLifetimeP50;
            NetDifficultyMonotonicSlack = netDifficultyMonotonicSlack;
            HundredGameWallClockSeconds = hundredGameWallClockSeconds;
            TimeoutCount = timeoutCount;
            StageOneWinRate = stageOneWinRate;
            StageFiveWinRate = stageFiveWinRate;
            StageOneMeanDurationSeconds = stageOneMeanDurationSeconds;
            StageFiveMeanDurationSeconds = stageFiveMeanDurationSeconds;
            StageOneHeavyRate = stageOneHeavyRate;
            StageFiveHeavyRate = stageFiveHeavyRate;
        }

        public double HundredGameWallClockSeconds { get; }
        public int TimeoutCount { get; }
        public double StageOneWinRate { get; }
        public double StageFiveWinRate { get; }
        public double StageOneMeanDurationSeconds { get; }
        public double StageFiveMeanDurationSeconds { get; }
        public double StageOneHeavyRate { get; }
        public double StageFiveHeavyRate { get; }

        /// <summary>Highest share of enemy deaths that never landed an attack, across cohorts.</summary>
        public double WorstZeroAttackDeathRate { get; }

        /// <summary>Lowest median enemy lifetime across cohorts.</summary>
        public double WorstEnemyLifetimeP50 { get; }

        /// <summary>
        /// Smallest step in the normalised net-difficulty curve. Positive means every stage is
        /// harder than the one before it; zero or negative means the ramp is not monotonic.
        /// </summary>
        public double NetDifficultyMonotonicSlack { get; }
    }

    public sealed class BalanceAcceptanceCheck
    {
        internal BalanceAcceptanceCheck(string id, string label, bool passed, string evidence)
        {
            Id = id;
            Label = label;
            Passed = passed;
            Evidence = evidence;
        }

        public string Id { get; }
        public string Label { get; }
        public bool Passed { get; }
        public string Evidence { get; }
    }

    public sealed class BalanceAcceptanceResult
    {
        internal BalanceAcceptanceResult(
            BalanceAcceptanceMetrics metrics,
            IReadOnlyList<BalanceAcceptanceCheck> checks)
        {
            Metrics = metrics;
            Checks = checks;
            Passed = checks.All(value => value.Passed);
            FailureSummary = string.Join(
                "; ",
                checks.Where(value => !value.Passed)
                    .Select(value => value.Label + ": " + value.Evidence));
        }

        public BalanceAcceptanceMetrics Metrics { get; }
        public IReadOnlyList<BalanceAcceptanceCheck> Checks { get; }
        public bool Passed { get; }
        public string FailureSummary { get; }
    }

    /// <summary>
    /// Single source for WO-E1's locked thresholds. The report and the manual 100-game gate
    /// both consume this evaluator so a partial tuning result cannot silently relax acceptance.
    /// </summary>
    public static class BalanceAcceptanceEvaluator
    {
        public const double MaximumHundredGameWallClockSeconds = 60d;
        public const double StageOneMinimumWinRate = 0.55d;
        public const double StageOneMaximumWinRate = 0.75d;
        public const double StageFiveMinimumWinRate = 0.30d;
        public const double StageFiveMaximumWinRate = 0.50d;
        public const double MinimumMeanDurationSeconds = 90d;
        public const double MaximumMeanDurationSeconds = 150d;
        public const double MinimumHeavyArmorRate = 0.15d;

        /// <summary>
        /// WO-F1 review §three replaced the enemy death-height percentile with these two. The
        /// percentile could not tell "the front line never formed" apart from "our siege engine
        /// out-ranges their spawn door"; whether a unit ever swung cannot be confused that way.
        /// </summary>
        public const double MaximumZeroAttackDeathRate = 0.35d;

        public const double MinimumEnemyLifetimeP50Seconds = 6d;

        public static BalanceAcceptanceResult Evaluate(BalanceRunReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            return Evaluate(CreateMetrics(report));
        }

        public static BalanceAcceptanceResult Evaluate(BalanceAcceptanceMetrics metrics)
        {
            var checks = new[]
            {
                Check(
                    "performance",
                    "100 games < 60s",
                    metrics.HundredGameWallClockSeconds < MaximumHundredGameWallClockSeconds,
                    Seconds(metrics.HundredGameWallClockSeconds) + " measured/projected for 100 games"),
                Check(
                    "timeouts",
                    "No timed-out battles",
                    metrics.TimeoutCount == 0,
                    metrics.TimeoutCount.ToString(CultureInfo.InvariantCulture) + " timeout(s)"),
                Check(
                    "stage1_win_rate",
                    "A win rate, stage 1",
                    InRange(metrics.StageOneWinRate, StageOneMinimumWinRate, StageOneMaximumWinRate),
                    Percent(metrics.StageOneWinRate) + "; target 55%-75%"),
                Check(
                    "stage5_win_rate",
                    "A win rate, stage 5",
                    InRange(metrics.StageFiveWinRate, StageFiveMinimumWinRate, StageFiveMaximumWinRate),
                    Percent(metrics.StageFiveWinRate) + "; target 30%-50%"),
                Check(
                    "stage1_duration",
                    "Mean duration " + BalanceReferenceLineups.StageOneGateLineupId + "/stage 1",
                    InRange(
                        metrics.StageOneMeanDurationSeconds,
                        MinimumMeanDurationSeconds,
                        MaximumMeanDurationSeconds),
                    Seconds(metrics.StageOneMeanDurationSeconds) + "; target 90-150s"),
                Check(
                    "stage5_duration",
                    "Mean duration " + BalanceReferenceLineups.StageFiveGateLineupId + "/stage 5",
                    InRange(
                        metrics.StageFiveMeanDurationSeconds,
                        MinimumMeanDurationSeconds,
                        MaximumMeanDurationSeconds),
                    Seconds(metrics.StageFiveMeanDurationSeconds) + "; target 90-150s"),
                Check(
                    "stage1_heavy_armor",
                    "Heavy armor share main_20 (all acts)",
                    AtLeast(metrics.StageOneHeavyRate, MinimumHeavyArmorRate),
                    Percent(metrics.StageOneHeavyRate) + "; floor 15%"),
                Check(
                    "enemy_reaches_the_line",
                    "Enemy deaths with no attack landed",
                    AtMost(metrics.WorstZeroAttackDeathRate, MaximumZeroAttackDeathRate),
                    Percent(metrics.WorstZeroAttackDeathRate) + "; ceiling 35%"),
                Check(
                    "enemy_lifetime",
                    "Median enemy lifetime",
                    AtLeast(metrics.WorstEnemyLifetimeP50, MinimumEnemyLifetimeP50Seconds),
                    Seconds(metrics.WorstEnemyLifetimeP50) + "; floor 6.0s"),
                Check(
                    "net_difficulty_monotonic",
                    "Net difficulty rises every stage",
                    metrics.NetDifficultyMonotonicSlack > 0d,
                    "smallest step " + metrics.NetDifficultyMonotonicSlack.ToString("0.000", CultureInfo.InvariantCulture)),
                Check(
                    "stage5_heavy_armor",
                    "Heavy armor share main_20_stage_5 (all acts)",
                    AtLeast(metrics.StageFiveHeavyRate, MinimumHeavyArmorRate),
                    Percent(metrics.StageFiveHeavyRate) + "; floor 15%")
            };

            return new BalanceAcceptanceResult(metrics, Array.AsReadOnly(checks));
        }

        public static double ProjectHundredGames(BalanceRunReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            return report.Battles.Count == 0
                ? double.PositiveInfinity
                : report.WallClockSeconds * 100d / report.Battles.Count;
        }

        private static BalanceAcceptanceMetrics CreateMetrics(BalanceRunReport report)
        {
            // The gate reads the two accumulated reference boards: the stage-one board against
            // stage one, and the stage-five board against stage five. Crossing them would grade a
            // stage-five schedule against a four-unit opening board (WO-F1 §C).
            BalanceCohortSummary stageOne = report.Cohorts.FirstOrDefault(value =>
                value.LineupId == BalanceReferenceLineups.StageOneGateLineupId && value.StageIndex == 1);
            BalanceCohortSummary stageFive = report.Cohorts.FirstOrDefault(value =>
                value.LineupId == BalanceReferenceLineups.StageFiveGateLineupId && value.StageIndex == 5);
            BalanceArmorDistribution stageOneArmor = report.ArmorDistributions.FirstOrDefault(value =>
                value.WaveSetId == "main_20" && value.Phase == "ALL");
            BalanceArmorDistribution stageFiveArmor = report.ArmorDistributions.FirstOrDefault(value =>
                value.WaveSetId == "main_20_stage_5" && value.Phase == "ALL");

            return new BalanceAcceptanceMetrics(
                ProjectHundredGames(report),
                report.Battles.Count(value => value.TimedOut),
                stageOne?.WinRate ?? double.NaN,
                stageFive?.WinRate ?? double.NaN,
                stageOne?.MeanDurationSeconds ?? double.NaN,
                stageFive?.MeanDurationSeconds ?? double.NaN,
                stageOneArmor?.HeavyRate ?? double.NaN,
                stageFiveArmor?.HeavyRate ?? double.NaN,
                report.Cohorts.Count == 0
                    ? double.NaN
                    : report.Cohorts.Max(value => value.EnemyZeroAttackDeathRate),
                report.Cohorts.Count == 0
                    ? double.NaN
                    : report.Cohorts.Min(value => value.EnemyLifetimeP50),
                MonotonicSlack(WaveSetSpecTable.NetDifficultyNormalised));
        }

        /// <summary>Smallest step in a curve; negative or zero means it is not strictly rising.</summary>
        private static double MonotonicSlack(IReadOnlyList<float> curve)
        {
            if (curve == null || curve.Count < 2)
            {
                return double.NaN;
            }

            double smallest = double.PositiveInfinity;
            for (int index = 1; index < curve.Count; index++)
            {
                double step = curve[index] - curve[index - 1];
                if (step < smallest)
                {
                    smallest = step;
                }
            }

            return smallest;
        }

        private static BalanceAcceptanceCheck Check(
            string id,
            string label,
            bool passed,
            string evidence)
        {
            return new BalanceAcceptanceCheck(id, label, passed, evidence);
        }

        private static bool InRange(double value, double minimum, double maximum)
        {
            return !double.IsNaN(value) && value >= minimum && value <= maximum;
        }

        private static bool AtLeast(double value, double minimum)
        {
            return !double.IsNaN(value) && value >= minimum;
        }

        private static bool AtMost(double value, double maximum)
        {
            return !double.IsNaN(value) && value <= maximum;
        }

        private static string Percent(double value)
        {
            return double.IsNaN(value)
                ? "missing"
                : value.ToString("P1", CultureInfo.InvariantCulture);
        }

        private static string Seconds(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? "missing"
                : value.ToString("0.000", CultureInfo.InvariantCulture) + "s";
        }
    }
}
