using System;
using System.Collections.Generic;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Deploy;

namespace HanziDefend.Editor.Balance
{
    public readonly struct BalanceLineupSpawn
    {
        public BalanceLineupSpawn(string unitId, int level, int column, int row)
        {
            UnitId = string.IsNullOrWhiteSpace(unitId)
                ? throw new ArgumentException("Unit id is required.", nameof(unitId))
                : unitId;
            Level = level;
            Anchor = new GridCoordinate(column, row);
        }

        public string UnitId { get; }

        public int Level { get; }

        public GridCoordinate Anchor { get; }
    }

    public sealed class BalanceLineup
    {
        public BalanceLineup(
            string id,
            string displayName,
            bool expectedToContainSiege,
            IReadOnlyList<BalanceLineupSpawn> spawns,
            int gridColumnsOverride = 0)
        {
            Id = string.IsNullOrWhiteSpace(id)
                ? throw new ArgumentException("Lineup id is required.", nameof(id))
                : id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            ExpectedToContainSiege = expectedToContainSiege;
            if (spawns == null || spawns.Count == 0)
            {
                throw new ArgumentException("A balance lineup requires at least one unit.", nameof(spawns));
            }

            if (gridColumnsOverride < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gridColumnsOverride));
            }

            GridColumnsOverride = gridColumnsOverride;
            var snapshot = new BalanceLineupSpawn[spawns.Count];
            for (int index = 0; index < spawns.Count; index++)
            {
                snapshot[index] = spawns[index];
            }

            Spawns = Array.AsReadOnly(snapshot);
        }

        public string Id { get; }

        public string DisplayName { get; }

        public bool ExpectedToContainSiege { get; }

        /// <summary>
        /// Width of the unlocked region this lineup deploys on; zero keeps the level's own
        /// <c>initialUnlock</c> rect. WO-E4 uses it to probe wider shape tiers without editing
        /// levels.json, because the unlock mask is run state rather than a level property. The
        /// runner rejects any width that cannot be reached by unlocking cells around the initial
        /// rect, so an override is always a state a real run can grow into.
        /// </summary>
        public int GridColumnsOverride { get; }

        public IReadOnlyList<BalanceLineupSpawn> Spawns { get; }
    }

    public static class BalanceReferenceLineups
    {
        private static readonly IReadOnlyList<BalanceLineup> AllLineups = Array.AsReadOnly(new[]
        {
            new BalanceLineup(
                "A_siege_nub",
                "A · 含器械（3弓+1盾+1矛+1弩兵）",
                true,
                new[]
                {
                    new BalanceLineupSpawn("dun", 1, 0, 0),
                    new BalanceLineupSpawn("mao", 1, 2, 0),
                    new BalanceLineupSpawn("nub", 1, 0, 1),
                    new BalanceLineupSpawn("gong", 1, 1, 1),
                    new BalanceLineupSpawn("gong", 1, 1, 2),
                    new BalanceLineupSpawn("gong", 1, 2, 2)
                }),
            // Kept verbatim as the WO-E1 vertical baseline. It is also the worst possible
            // anti-building draw: three of its six units are gong, and Arrow is 0.25x versus
            // Building armour. Renamed, never re-tuned, so WO-E4 numbers stay comparable.
            new BalanceLineup(
                "B_no_siege",
                "B_worst · 无器械最差组合基准（3弓+2盾+1矛）",
                false,
                new[]
                {
                    new BalanceLineupSpawn("dun", 1, 0, 0),
                    new BalanceLineupSpawn("dun", 1, 0, 1),
                    new BalanceLineupSpawn("mao", 1, 2, 0),
                    new BalanceLineupSpawn("gong", 1, 0, 2),
                    new BalanceLineupSpawn("gong", 1, 1, 2),
                    new BalanceLineupSpawn("gong", 1, 2, 2)
                }),
            // WO-E4: best anti-building siege-free lineup buildable under the 3-column shape
            // unlock (1x1 / 2x1 / 1x2 only). Per-cell damage versus bld_cheng at level 1:
            // qqi 12.0, dao 10.8, mao 8.4, dun 5.1, zu 5.0, gong 4.0. Three 2x1 is the maximum
            // a 3-wide grid takes, and the leftover column prefers one 1x2 mao over two 1x1.
            new BalanceLineup(
                "B_fair_s1",
                "B_fair_s1 · 无器械 3 列最优（3轻骑+1长矛+1卒）",
                false,
                new[]
                {
                    new BalanceLineupSpawn("qqi", 1, 0, 0),
                    new BalanceLineupSpawn("qqi", 1, 0, 1),
                    new BalanceLineupSpawn("qqi", 1, 0, 2),
                    new BalanceLineupSpawn("mao", 1, 2, 0),
                    new BalanceLineupSpawn("zu", 1, 2, 2)
                }),
            // WO-E4: same optimisation on a 5-wide unlocked region, where two 2x2 footprints fit.
            // lia is the highest per-cell anti-building unit in the whole table (24.3), and a
            // 5x3 region seats exactly two 2x2 footprints; the remaining seven cells reuse the
            // 3-wide optimum. WO-C5 note: the width is now an unlock-mask state, reachable by
            // playing one vertical unlock card on each flank of the level's starting rect.
            new BalanceLineup(
                "B_fair_s5",
                "B_fair_s5 · 无器械 5 列最优（2链甲+2轻骑+1长矛+1卒）",
                false,
                new[]
                {
                    new BalanceLineupSpawn("lia", 1, 0, 0),
                    new BalanceLineupSpawn("lia", 1, 2, 0),
                    new BalanceLineupSpawn("mao", 1, 4, 0),
                    new BalanceLineupSpawn("qqi", 1, 0, 2),
                    new BalanceLineupSpawn("qqi", 1, 2, 2),
                    new BalanceLineupSpawn("zu", 1, 4, 2)
                },
                gridColumnsOverride: 5)
        });

        public static IReadOnlyList<BalanceLineup> All => AllLineups;
    }

    public sealed class BalanceRunRequest
    {
        public BalanceRunRequest(
            int gamesPerCohort,
            IReadOnlyList<string> levelIds,
            IReadOnlyList<BalanceLineup> lineups,
            uint seed,
            double maximumSimulatedSeconds,
            bool simulatePhysics,
            string outputDirectory = null,
            int maxDegreeOfParallelism = 0)
        {
            if (gamesPerCohort <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gamesPerCohort));
            }

            if (levelIds == null || levelIds.Count == 0)
            {
                throw new ArgumentException("At least one level id is required.", nameof(levelIds));
            }

            if (lineups == null || lineups.Count == 0)
            {
                throw new ArgumentException("At least one lineup is required.", nameof(lineups));
            }

            if (double.IsNaN(maximumSimulatedSeconds)
                || double.IsInfinity(maximumSimulatedSeconds)
                || maximumSimulatedSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSimulatedSeconds));
            }

            if (maxDegreeOfParallelism < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
            }

            GamesPerCohort = gamesPerCohort;
            LevelIds = Snapshot(levelIds);
            Lineups = Snapshot(lineups);
            Seed = seed;
            MaximumSimulatedSeconds = maximumSimulatedSeconds;
            SimulatePhysics = simulatePhysics;
            OutputDirectory = outputDirectory;
            MaxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        public int GamesPerCohort { get; }

        public IReadOnlyList<string> LevelIds { get; }

        public IReadOnlyList<BalanceLineup> Lineups { get; }

        public uint Seed { get; }

        public double MaximumSimulatedSeconds { get; }

        public bool SimulatePhysics { get; }

        public string OutputDirectory { get; }

        /// <summary>Zero selects min(4, current CPU count); one forces deterministic serial execution.</summary>
        public int MaxDegreeOfParallelism { get; }

        public int EffectiveMaxDegreeOfParallelism => MaxDegreeOfParallelism == 0
            ? Math.Max(1, Math.Min(4, Environment.ProcessorCount))
            : MaxDegreeOfParallelism;

        public int CohortCount => checked(LevelIds.Count * Lineups.Count);

        public int TotalGames => checked(GamesPerCohort * CohortCount);

        public bool UsesParallelExecution => !SimulatePhysics
                                             && TotalGames > 1
                                             && EffectiveMaxDegreeOfParallelism > 1;

        public static BalanceRunRequest CreateDefault(
            int gamesPerCohort,
            string outputDirectory = null,
            uint seed = 0xE1002026u)
        {
            return new BalanceRunRequest(
                gamesPerCohort,
                new[] { "level_1_1", "level_1_5" },
                BalanceReferenceLineups.All,
                seed,
                600d,
                false,
                outputDirectory);
        }

        private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> source)
        {
            var values = new T[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                values[index] = source[index];
            }

            return Array.AsReadOnly(values);
        }
    }

    public sealed class BalanceBattleResult
    {
        internal BalanceBattleResult(
            string lineupId,
            string lineupName,
            string levelId,
            int stageIndex,
            int gridColumns,
            int gameIndex,
            uint seed,
            BattleResult result,
            double durationSeconds,
            long tickCount,
            double wallClockMilliseconds,
            int startCoins,
            int droppedCoins,
            int endCoins,
            float allyBaseHp,
            float allyBaseMaxHp,
            float bossHp,
            float bossMaxHp,
            bool timedOut,
            IReadOnlyList<BalanceEntityResult> entities,
            IReadOnlyList<BalanceCoinPoint> coinCurve)
        {
            LineupId = lineupId;
            LineupName = lineupName;
            LevelId = levelId;
            StageIndex = stageIndex;
            GridColumns = gridColumns;
            GameIndex = gameIndex;
            Seed = seed;
            Result = result;
            DurationSeconds = durationSeconds;
            TickCount = tickCount;
            WallClockMilliseconds = wallClockMilliseconds;
            StartCoins = startCoins;
            DroppedCoins = droppedCoins;
            EndCoins = endCoins;
            AllyBaseHp = allyBaseHp;
            AllyBaseMaxHp = allyBaseMaxHp;
            BossHp = bossHp;
            BossMaxHp = bossMaxHp;
            TimedOut = timedOut;
            Entities = entities;
            CoinCurve = coinCurve;
        }

        public string LineupId { get; }
        public string LineupName { get; }
        public string LevelId { get; }
        public int StageIndex { get; }
        /// <summary>Deployment columns this battle actually used, after any lineup override.</summary>
        public int GridColumns { get; }
        public int GameIndex { get; }
        public uint Seed { get; }
        public BattleResult Result { get; }
        public double DurationSeconds { get; }
        public long TickCount { get; }
        public double WallClockMilliseconds { get; }
        public int StartCoins { get; }
        public int DroppedCoins { get; }
        public int EndCoins { get; }
        public float AllyBaseHp { get; }
        public float AllyBaseMaxHp { get; }
        public float BossHp { get; }
        public float BossMaxHp { get; }
        public bool TimedOut { get; }
        public IReadOnlyList<BalanceEntityResult> Entities { get; }
        public IReadOnlyList<BalanceCoinPoint> CoinCurve { get; }
        public bool IsWin => Result == BattleResult.Win;
    }

    public sealed class BalanceEntityResult
    {
        internal BalanceEntityResult(
            int entityId,
            string unitId,
            BattleTeam team,
            double spawnTimeSeconds,
            double endTimeSeconds,
            bool survived,
            long damage)
        {
            EntityId = entityId;
            UnitId = unitId;
            Team = team;
            SpawnTimeSeconds = spawnTimeSeconds;
            EndTimeSeconds = endTimeSeconds;
            Survived = survived;
            Damage = damage;
        }

        public int EntityId { get; }
        public string UnitId { get; }
        public BattleTeam Team { get; }
        public double SpawnTimeSeconds { get; }
        public double EndTimeSeconds { get; }
        public bool Survived { get; }
        public long Damage { get; }
        public double SurvivalSeconds => Math.Max(0d, EndTimeSeconds - SpawnTimeSeconds);
    }

    public sealed class BalanceCoinPoint
    {
        internal BalanceCoinPoint(
            double timeSeconds,
            string eventKind,
            string sourceUnitId,
            int delta,
            int totalCoins)
        {
            TimeSeconds = timeSeconds;
            EventKind = eventKind;
            SourceUnitId = sourceUnitId;
            Delta = delta;
            TotalCoins = totalCoins;
        }

        public double TimeSeconds { get; }
        public string EventKind { get; }
        public string SourceUnitId { get; }
        public int Delta { get; }
        public int TotalCoins { get; }
    }

    public sealed class BalanceCohortSummary
    {
        internal BalanceCohortSummary(
            string lineupId,
            string lineupName,
            string levelId,
            int stageIndex,
            int gridColumns,
            int games,
            int wins,
            int losses,
            int timeouts,
            double winRate,
            double meanDurationSeconds,
            double medianDurationSeconds,
            double p95DurationSeconds,
            double meanWallClockMilliseconds,
            double totalWallClockSeconds,
            double meanEndCoins)
        {
            LineupId = lineupId;
            LineupName = lineupName;
            LevelId = levelId;
            StageIndex = stageIndex;
            GridColumns = gridColumns;
            Games = games;
            Wins = wins;
            Losses = losses;
            Timeouts = timeouts;
            WinRate = winRate;
            MeanDurationSeconds = meanDurationSeconds;
            MedianDurationSeconds = medianDurationSeconds;
            P95DurationSeconds = p95DurationSeconds;
            MeanWallClockMilliseconds = meanWallClockMilliseconds;
            TotalWallClockSeconds = totalWallClockSeconds;
            MeanEndCoins = meanEndCoins;
        }

        public string LineupId { get; }
        public string LineupName { get; }
        public string LevelId { get; }
        public int StageIndex { get; }
        /// <summary>Deployment columns this cohort actually used, after any lineup override.</summary>
        public int GridColumns { get; }
        public int Games { get; }
        public int Wins { get; }
        public int Losses { get; }
        public int Timeouts { get; }
        public double WinRate { get; }
        public double MeanDurationSeconds { get; }
        public double MedianDurationSeconds { get; }
        public double P95DurationSeconds { get; }
        public double MeanWallClockMilliseconds { get; }
        public double TotalWallClockSeconds { get; }
        public double MeanEndCoins { get; }
    }

    public sealed class BalanceUnitSummary
    {
        internal BalanceUnitSummary(
            string lineupId,
            string levelId,
            int stageIndex,
            BattleTeam team,
            string unitId,
            int spawnCount,
            int survivorCount,
            long totalDamage,
            double cohortBattleSeconds,
            double meanSurvivalSeconds)
        {
            LineupId = lineupId;
            LevelId = levelId;
            StageIndex = stageIndex;
            Team = team;
            UnitId = unitId;
            SpawnCount = spawnCount;
            SurvivorCount = survivorCount;
            TotalDamage = totalDamage;
            CohortBattleSeconds = cohortBattleSeconds;
            MeanSurvivalSeconds = meanSurvivalSeconds;
        }

        public string LineupId { get; }
        public string LevelId { get; }
        public int StageIndex { get; }
        public BattleTeam Team { get; }
        public string UnitId { get; }
        public int SpawnCount { get; }
        public int SurvivorCount { get; }
        public long TotalDamage { get; }
        public double CohortBattleSeconds { get; }
        public double MeanSurvivalSeconds { get; }
        public double Dps => CohortBattleSeconds <= 0d ? 0d : TotalDamage / CohortBattleSeconds;
        public double TotalActiveSeconds => MeanSurvivalSeconds * SpawnCount;
        public double ActiveDps => TotalActiveSeconds <= 0d ? 0d : TotalDamage / TotalActiveSeconds;
        public double SurvivorRate => SpawnCount <= 0 ? 0d : (double)SurvivorCount / SpawnCount;
    }

    public sealed class BalanceArmorDistribution
    {
        internal BalanceArmorDistribution(
            string waveSetId,
            string phase,
            int firstWave,
            int lastWave,
            int unarmored,
            int light,
            int heavy,
            int building,
            int other)
        {
            WaveSetId = waveSetId;
            Phase = phase;
            FirstWave = firstWave;
            LastWave = lastWave;
            Unarmored = unarmored;
            Light = light;
            Heavy = heavy;
            Building = building;
            Other = other;
        }

        public string WaveSetId { get; }
        public string Phase { get; }
        public int FirstWave { get; }
        public int LastWave { get; }
        public int Unarmored { get; }
        public int Light { get; }
        public int Heavy { get; }
        public int Building { get; }
        public int Other { get; }
        public int NonBossTotal => checked(Unarmored + Light + Heavy + Other);
        public int Total => checked(NonBossTotal + Building);
        public double UnarmoredRate => Rate(Unarmored, NonBossTotal);
        public double LightRate => Rate(Light, NonBossTotal);
        public double HeavyRate => Rate(Heavy, NonBossTotal);
        public double BuildingRate => Rate(Building, Total);
        public bool HeavyMeetsFifteenPercent => NonBossTotal > 0 && HeavyRate >= 0.15d;

        private static double Rate(int value, int total)
        {
            return total <= 0 ? 0d : (double)value / total;
        }
    }

    public sealed class BalanceRunReport
    {
        internal BalanceRunReport(
            BalanceRunRequest request,
            IReadOnlyList<BalanceBattleResult> battles,
            IReadOnlyList<BalanceCohortSummary> cohorts,
            IReadOnlyList<BalanceUnitSummary> units,
            IReadOnlyList<BalanceArmorDistribution> armorDistributions,
            double wallClockSeconds,
            string outputDirectory)
        {
            Request = request;
            Battles = battles;
            Cohorts = cohorts;
            Units = units;
            ArmorDistributions = armorDistributions;
            WallClockSeconds = wallClockSeconds;
            OutputDirectory = outputDirectory;
        }

        public BalanceRunRequest Request { get; }
        public IReadOnlyList<BalanceBattleResult> Battles { get; }
        public IReadOnlyList<BalanceCohortSummary> Cohorts { get; }
        public IReadOnlyList<BalanceUnitSummary> Units { get; }
        public IReadOnlyList<BalanceArmorDistribution> ArmorDistributions { get; }
        public double WallClockSeconds { get; }
        public string OutputDirectory { get; }
    }
}
