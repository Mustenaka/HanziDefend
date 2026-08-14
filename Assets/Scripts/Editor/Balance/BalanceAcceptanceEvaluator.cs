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
            double stageFiveHeavyRate)
        {
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
                    "Mean duration A_siege_nub/stage 1",
                    InRange(
                        metrics.StageOneMeanDurationSeconds,
                        MinimumMeanDurationSeconds,
                        MaximumMeanDurationSeconds),
                    Seconds(metrics.StageOneMeanDurationSeconds) + "; target 90-150s"),
                Check(
                    "stage5_duration",
                    "Mean duration A_siege_nub/stage 5",
                    InRange(
                        metrics.StageFiveMeanDurationSeconds,
                        MinimumMeanDurationSeconds,
                        MaximumMeanDurationSeconds),
                    Seconds(metrics.StageFiveMeanDurationSeconds) + "; target 90-150s"),
                Check(
                    "stage1_heavy_armor",
                    "Heavy armor share main_20",
                    AtLeast(metrics.StageOneHeavyRate, MinimumHeavyArmorRate),
                    Percent(metrics.StageOneHeavyRate) + "; floor 15%"),
                Check(
                    "stage5_heavy_armor",
                    "Heavy armor share main_20_stage_5",
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
            BalanceCohortSummary stageOne = report.Cohorts.FirstOrDefault(value =>
                value.LineupId == "A_siege_nub" && value.StageIndex == 1);
            BalanceCohortSummary stageFive = report.Cohorts.FirstOrDefault(value =>
                value.LineupId == "A_siege_nub" && value.StageIndex == 5);
            BalanceArmorDistribution stageOneArmor = report.ArmorDistributions.FirstOrDefault(value =>
                value.WaveSetId == "main_20" && value.FirstWave == 1 && value.LastWave == 19);
            BalanceArmorDistribution stageFiveArmor = report.ArmorDistributions.FirstOrDefault(value =>
                value.WaveSetId == "main_20_stage_5" && value.FirstWave == 1 && value.LastWave == 19);

            return new BalanceAcceptanceMetrics(
                ProjectHundredGames(report),
                report.Battles.Count(value => value.TimedOut),
                stageOne?.WinRate ?? double.NaN,
                stageFive?.WinRate ?? double.NaN,
                stageOne?.MeanDurationSeconds ?? double.NaN,
                stageFive?.MeanDurationSeconds ?? double.NaN,
                stageOneArmor?.HeavyRate ?? double.NaN,
                stageFiveArmor?.HeavyRate ?? double.NaN);
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
