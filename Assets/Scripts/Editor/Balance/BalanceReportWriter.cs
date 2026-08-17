using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HanziDefend.Gameplay.Battle;

namespace HanziDefend.Editor.Balance
{
    public static class BalanceReportWriter
    {
        private static readonly Encoding CsvEncoding = new UTF8Encoding(true);

        public static void Write(BalanceRunReport report, string outputDirectory)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
            }

            string fullDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullDirectory);
            WriteCsv(Path.Combine(fullDirectory, "summary.csv"), SummaryRows(report));
            WriteCsv(Path.Combine(fullDirectory, "battles.csv"), BattleRows(report));
            WriteCsv(Path.Combine(fullDirectory, "unit_metrics.csv"), UnitRows(report));
            WriteCsv(Path.Combine(fullDirectory, "coin_curve.csv"), CoinRows(report));
            WriteCsv(Path.Combine(fullDirectory, "armor_distribution.csv"), ArmorRows(report));
            File.WriteAllText(
                Path.Combine(fullDirectory, "report.md"),
                BuildMarkdown(report),
                CsvEncoding);
        }

        private static IEnumerable<IReadOnlyList<string>> SummaryRows(BalanceRunReport report)
        {
            yield return new[]
            {
                "lineup_id", "lineup_name", "level_id", "stage_index", "unlocked_cells", "games",
                "wins", "losses", "timeouts", "win_rate", "mean_duration_s", "median_duration_s",
                "p95_duration_s", "mean_wall_ms", "cohort_wall_s", "mean_end_coins",
                "mean_dropped_coins", "peak_concurrent_units", "enemy_death_y_p90",
                "enemy_death_y_p90_pre_castle", "enemy_zero_attack_death_rate", "enemy_lifetime_p50",
                "mean_last_ally_entry_s"
            };
            foreach (BalanceCohortSummary value in report.Cohorts)
            {
                yield return new[]
                {
                    value.LineupId,
                    value.LineupName,
                    value.LevelId,
                    I(value.StageIndex),
                    I(value.UnlockedCellCount),
                    I(value.Games),
                    I(value.Wins),
                    I(value.Losses),
                    I(value.Timeouts),
                    F(value.WinRate),
                    F(value.MeanDurationSeconds),
                    F(value.MedianDurationSeconds),
                    F(value.P95DurationSeconds),
                    F(value.MeanWallClockMilliseconds),
                    F(value.TotalWallClockSeconds),
                    F(value.MeanEndCoins),
                    F(value.MeanDroppedCoins),
                    I(value.PeakConcurrentUnits),
                    F(value.EnemyDeathYP90),
                    F(value.EnemyDeathYP90BeforeCastle),
                    F(value.EnemyZeroAttackDeathRate),
                    F(value.EnemyLifetimeP50),
                    F(value.MeanLastAllyEntrySeconds)
                };
            }
        }

        private static IEnumerable<IReadOnlyList<string>> BattleRows(BalanceRunReport report)
        {
            yield return new[]
            {
                "lineup_id", "lineup_name", "level_id", "stage_index", "unlocked_cells", "game_index",
                "seed_hex", "result", "timed_out", "duration_s", "ticks", "wall_ms",
                "start_coins", "dropped_coins", "end_coins", "ally_base_hp",
                "ally_base_max_hp", "boss_hp", "boss_max_hp",
                "peak_concurrent_units", "last_ally_entry_s", "enemy_deaths", "enemy_death_y_p90"
            };
            foreach (BalanceBattleResult value in report.Battles)
            {
                yield return new[]
                {
                    value.LineupId,
                    value.LineupName,
                    value.LevelId,
                    I(value.StageIndex),
                    I(value.UnlockedCellCount),
                    I(value.GameIndex),
                    "0x" + value.Seed.ToString("X8", CultureInfo.InvariantCulture),
                    value.Result.ToString(),
                    value.TimedOut ? "true" : "false",
                    F(value.DurationSeconds),
                    value.TickCount.ToString(CultureInfo.InvariantCulture),
                    F(value.WallClockMilliseconds),
                    I(value.StartCoins),
                    I(value.DroppedCoins),
                    I(value.EndCoins),
                    F(value.AllyBaseHp),
                    F(value.AllyBaseMaxHp),
                    F(value.BossHp),
                    F(value.BossMaxHp),
                    I(value.PeakConcurrentUnits),
                    F(value.LastAllyEntrySeconds),
                    I(value.EnemyDeathPositionsY.Count),
                    F(Percentile(value.EnemyDeathPositionsY, 0.90d))
                };
            }
        }

        /// <summary>Percentile of one battle's enemy death heights; NaN when nothing died.</summary>
        private static double Percentile(IReadOnlyList<float> values, double percentile)
        {
            if (values == null || values.Count == 0)
            {
                return double.NaN;
            }

            var sorted = new List<float>(values);
            sorted.Sort();
            int rank = Math.Max(0, (int)Math.Ceiling(percentile * sorted.Count) - 1);
            return sorted[Math.Min(rank, sorted.Count - 1)];
        }

        private static IEnumerable<IReadOnlyList<string>> UnitRows(BalanceRunReport report)
        {
            yield return new[]
            {
                "lineup_id", "level_id", "stage_index", "team", "unit_id", "spawn_count",
                "survivor_count", "survivor_rate", "total_damage", "cohort_battle_s",
                "battle_dps", "active_seconds", "active_dps", "mean_survival_s"
            };
            foreach (BalanceUnitSummary value in report.Units)
            {
                yield return new[]
                {
                    value.LineupId,
                    value.LevelId,
                    I(value.StageIndex),
                    value.Team.ToString(),
                    value.UnitId,
                    I(value.SpawnCount),
                    I(value.SurvivorCount),
                    F(value.SurvivorRate),
                    value.TotalDamage.ToString(CultureInfo.InvariantCulture),
                    F(value.CohortBattleSeconds),
                    F(value.Dps),
                    F(value.TotalActiveSeconds),
                    F(value.ActiveDps),
                    F(value.MeanSurvivalSeconds)
                };
            }
        }

        private static IEnumerable<IReadOnlyList<string>> CoinRows(BalanceRunReport report)
        {
            yield return new[]
            {
                "lineup_id", "level_id", "stage_index", "game_index", "seed_hex",
                "time_s", "event", "source_unit_id", "delta", "total_coins"
            };
            foreach (BalanceBattleResult battle in report.Battles)
            foreach (BalanceCoinPoint point in battle.CoinCurve)
            {
                yield return new[]
                {
                    battle.LineupId,
                    battle.LevelId,
                    I(battle.StageIndex),
                    I(battle.GameIndex),
                    "0x" + battle.Seed.ToString("X8", CultureInfo.InvariantCulture),
                    F(point.TimeSeconds),
                    point.EventKind,
                    point.SourceUnitId,
                    I(point.Delta),
                    I(point.TotalCoins)
                };
            }
        }

        private static IEnumerable<IReadOnlyList<string>> ArmorRows(BalanceRunReport report)
        {
            yield return new[]
            {
                "wave_set_id", "act", "first_wave", "last_wave", "unarmored", "light",
                "heavy", "building", "other", "non_boss_total", "total", "unarmored_rate",
                "light_rate", "heavy_rate", "building_rate", "heavy_at_least_15_percent"
            };
            foreach (BalanceArmorDistribution value in report.ArmorDistributions)
            {
                yield return new[]
                {
                    value.WaveSetId,
                    value.Phase,
                    I(value.FirstWave),
                    I(value.LastWave),
                    I(value.Unarmored),
                    I(value.Light),
                    I(value.Heavy),
                    I(value.Building),
                    I(value.Other),
                    I(value.NonBossTotal),
                    I(value.Total),
                    F(value.UnarmoredRate),
                    F(value.LightRate),
                    F(value.HeavyRate),
                    F(value.BuildingRate),
                    value.HeavyMeetsFifteenPercent ? "true" : "false"
                };
            }
        }

        private static string BuildMarkdown(BalanceRunReport report)
        {
            var builder = new StringBuilder();
            BalanceAcceptanceResult acceptance = BalanceAcceptanceEvaluator.Evaluate(report);
            builder.AppendLine("# BalanceRunner report");
            builder.AppendLine();
            builder.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC  ");
            builder.AppendLine($"Seed: `0x{report.Request.Seed:X8}`  ");
            string execution = report.Request.UsesParallelExecution
                ? $"parallel ({report.Request.EffectiveMaxDegreeOfParallelism} workers)"
                : "serial";
            builder.AppendLine($"Execution: {execution}, no-render, physics step: `{report.Request.SimulatePhysics}`  ");
            builder.AppendLine($"Games: **{report.Battles.Count}** ({report.Request.GamesPerCohort} per cohort)  ");
            builder.AppendLine($"Wall clock: **{report.WallClockSeconds:0.000}s**  ");
            builder.AppendLine($"100-game projection: **{BalanceAcceptanceEvaluator.ProjectHundredGames(report):0.000}s**  ");
            builder.AppendLine($"Timeouts: **{report.Battles.Count(value => value.TimedOut)}**");
            builder.AppendLine();
            builder.AppendLine("## Lineups");
            builder.AppendLine();
            builder.AppendLine("`Cells` is the unlock mask the cohort deploys on: the level's starting rect");
            builder.AppendLine("unless the lineup carries its own accumulated mask. `Levels` names the stages the");
            builder.AppendLine("board is meaningful on — an accumulated stage-five board is not run against stage one.");
            builder.AppendLine();
            builder.AppendLine("| Lineup id | Name | Cells | Levels | Composition | Siege |");
            builder.AppendLine("|---|---|---:|---|---|---|");
            foreach (BalanceLineup lineup in report.Request.Lineups)
            {
                string levels = lineup.LevelIds == null
                    ? "all"
                    : string.Join(", ", lineup.LevelIds);
                builder.AppendLine(
                    $"| `{lineup.Id}` | {lineup.DisplayName} | "
                    + $"{(lineup.UnlockedCells == null ? "level" : lineup.DeclaredUnlockedCellCount.ToString(CultureInfo.InvariantCulture))} | "
                    + $"{levels} | {Composition(lineup)} | {(lineup.ExpectedToContainSiege ? "yes" : "no")} |");
            }

            builder.AppendLine();
            builder.AppendLine("## Cohorts");
            builder.AppendLine();
            builder.AppendLine("`0-atk` is the share of enemy deaths that never landed an attack (ceiling 35%) and");
            builder.AppendLine("`life p50` the median enemy lifetime (floor 6.0s). Those two are the front-line gate.");
            builder.AppendLine("`Death Y p90` is retained as an observation only: it cannot tell a front line that");
            builder.AppendLine("never formed apart from a siege engine out-ranging the enemy spawn door.");
            builder.AppendLine();
            builder.AppendLine("| Lineup | Stage | Cells | Games | Win rate | Timeouts | Mean duration | P95 duration | Mean wall/game | Mean drops | Peak units | 0-atk | life p50 | Death Y p90 | pre-castle |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (BalanceCohortSummary value in report.Cohorts)
            {
                builder.AppendLine(
                    $"| {value.LineupName} | {value.StageIndex} | {value.UnlockedCellCount} | {value.Games} | "
                    + $"{value.WinRate:P1} | {value.Timeouts} | {value.MeanDurationSeconds:0.0}s | "
                    + $"{value.P95DurationSeconds:0.0}s | {value.MeanWallClockMilliseconds:0.00}ms | "
                    + $"{value.MeanDroppedCoins:0.0} | {value.PeakConcurrentUnits} | "
                    + $"{value.EnemyZeroAttackDeathRate:P1} | {value.EnemyLifetimeP50:0.0}s | "
                    + $"{value.EnemyDeathYP90:0.00} | {value.EnemyDeathYP90BeforeCastle:0.00} |");
            }

            builder.AppendLine();
            builder.AppendLine("## Net difficulty");
            builder.AppendLine();
            builder.AppendLine("`budget / measured ally DPS`, normalised so stage one is 1.0 — the single number that");
            builder.AppendLine("says how much harder the run actually gets. It must rise at every stage.");
            builder.AppendLine();
            builder.AppendLine("| Stage | Measured DPS | Difficulty scalar | Net difficulty (S1=1) |");
            builder.AppendLine("|---:|---:|---:|---:|");
            IReadOnlyList<float> net = WaveSetSpecTable.NetDifficultyNormalised;
            IReadOnlyList<float> dps = WaveSetSpecTable.MeasuredAllyDpsPerStage;
            IReadOnlyList<float> scalars = WaveSetSpecTable.StageDifficultyScalars;
            for (int index = 0; index < net.Count; index++)
            {
                builder.AppendLine(
                    $"| {index + 1} | {dps[index]:0} | {scalars[index]:0.00} | **{net[index]:0.000}** |");
            }

            builder.AppendLine();
            builder.AppendLine("## Acceptance checks");
            builder.AppendLine();
            foreach (BalanceAcceptanceCheck check in acceptance.Checks)
            {
                AppendCheck(builder, check.Label, check.Passed, check.Evidence);
            }

            builder.AppendLine();
            builder.AppendLine("## Armor distribution");
            builder.AppendLine();
            builder.AppendLine("Counted per act; the castle is excluded from the non-boss denominator.");
            builder.AppendLine();
            builder.AppendLine("| Wave set | Act | Unarmored | Light | Heavy | Building | Heavy floor |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---|");
            foreach (BalanceArmorDistribution value in report.ArmorDistributions)
            {
                builder.AppendLine(
                    $"| {value.WaveSetId} | {value.Phase} | {value.UnarmoredRate:P1} | "
                    + $"{value.LightRate:P1} | {value.HeavyRate:P1} | {value.BuildingRate:P1} | "
                    + $"{(value.HeavyMeetsFifteenPercent ? "PASS" : "n/a / below")} |");
            }

            builder.AppendLine();
            builder.AppendLine("## CSV files");
            builder.AppendLine();
            builder.AppendLine("- `summary.csv`: cohort grid columns, win rate, duration and wall-clock summary");
            builder.AppendLine("- `battles.csv`: every seed and battle result");
            builder.AppendLine("- `unit_metrics.csv`: battle-window DPS, alive-window DPS, survival time and survivor rate per unit id");
            builder.AppendLine("- `coin_curve.csv`: raw deterministic coin curve points for every battle");
            builder.AppendLine("- `armor_distribution.csv`: configured wave armor counts and ratios");
            return builder.ToString();
        }

        private static string Composition(BalanceLineup lineup)
        {
            return string.Join(
                " + ",
                lineup.Spawns
                    .GroupBy(value => value.UnitId, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => $"{group.Count()}×`{group.Key}`"));
        }

        private static void AppendCheck(StringBuilder builder, string label, bool passed, string evidence)
        {
            builder.AppendLine($"- [{(passed ? "x" : " ")}] {label}: {evidence}");
        }

        private static void WriteCsv(string path, IEnumerable<IReadOnlyList<string>> rows)
        {
            var builder = new StringBuilder();
            foreach (IReadOnlyList<string> row in rows)
            {
                for (int index = 0; index < row.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(Escape(row[index]));
                }

                builder.Append("\r\n");
            }

            File.WriteAllText(path, builder.ToString(), CsvEncoding);
        }

        private static string Escape(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private static string I(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string F(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string F(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
