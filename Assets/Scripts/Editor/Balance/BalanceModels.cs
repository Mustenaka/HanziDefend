using System;
using System.Collections.Generic;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Deploy;

namespace HanziDefend.Editor.Balance
{
    public readonly struct BalanceLineupSpawn
    {
        /// <summary>
        /// One deployed unit. <paramref name="column"/> and <paramref name="row"/> are absolute
        /// playfield coordinates on the 7x7 grid — the same numbers <c>RunState.DeployedGrid</c>
        /// stores, so a lineup can be copied straight out of a simulated run and back in.
        /// </summary>
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
            IReadOnlyList<GridCoordinate> unlockedCells = null,
            IReadOnlyList<string> levelIds = null)
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

            Spawns = Snapshot(spawns);
            UnlockedCells = unlockedCells == null ? null : Snapshot(unlockedCells);
            LevelIds = levelIds == null ? null : Snapshot(levelIds);
        }

        public string Id { get; }

        public string DisplayName { get; }

        public bool ExpectedToContainSiege { get; }

        public IReadOnlyList<BalanceLineupSpawn> Spawns { get; }

        /// <summary>
        /// The unlock mask this lineup stands on, in absolute playfield coordinates; null keeps the
        /// level's own <c>initialUnlock</c> rect.
        ///
        /// <para>It is a cell set rather than a column count because a real run's mask is not a
        /// rectangle: it grows one unlock card at a time and ends up an irregular blob around the
        /// camp. WO-E4's centred-rectangle override could only express 3, 5 or 7 columns, which is
        /// precisely the abstraction WO-F1 §0.5 identifies as making the reference model a different
        /// game from the one being played. The runner still checks that a mask is reachable.</para>
        /// </summary>
        public IReadOnlyList<GridCoordinate> UnlockedCells { get; }

        /// <summary>
        /// Levels this lineup is meaningful on; null means all of them. A board accumulated by the
        /// end of stage five says nothing when it is dropped into stage one's waves, so the two
        /// reference boards each name their own stage instead of being crossed with every level.
        /// </summary>
        public IReadOnlyList<string> LevelIds { get; }

        public int DeclaredUnlockedCellCount => UnlockedCells?.Count ?? 0;

        public bool AppliesTo(string levelId)
        {
            if (LevelIds == null)
            {
                return true;
            }

            for (int index = 0; index < LevelIds.Count; index++)
            {
                if (string.Equals(LevelIds[index], levelId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

    /// <summary>
    /// The reference boards, lifted verbatim out of <see cref="RunAccumulationSimulator"/> run
    /// <c>0xE1002026</c> — the project's own default balance seed.
    ///
    /// <para>They are frozen literals rather than a live call into the simulator on purpose: a
    /// reference lineup that silently rewrites itself whenever an economy number moves is not a
    /// reference. Regenerate them deliberately (the simulator prints them in this exact form),
    /// review the diff, and paste. <c>BalanceRunnerTests</c> pins the properties WO-F1 §C requires
    /// so a careless paste cannot quietly shrink the model back to six level-1 units.</para>
    /// </summary>
    public static class BalanceReferenceLineups
    {
        private const string StageOneLevelId = "level_1_1";
        private const string StageFiveLevelId = "level_1_5";

        /// <summary>The level's own starting 3x3, spelled out so stage-one lineups read the same way.</summary>
        private static readonly GridCoordinate[] StageOneMask =
        {
            new GridCoordinate(2, 2), new GridCoordinate(3, 2), new GridCoordinate(4, 2),
            new GridCoordinate(2, 3), new GridCoordinate(3, 3), new GridCoordinate(4, 3),
            new GridCoordinate(2, 4), new GridCoordinate(3, 4), new GridCoordinate(4, 4)
        };

        /// <summary>
        /// Twenty-one cells: the starting 3x3, plus four automatic per-stage unlocks and the cards
        /// a run's coins actually bought by stage five. Irregular and bottom-heavy, because that is
        /// what unlock cards ranked by "nearest the camp" produce.
        /// </summary>
        private static readonly GridCoordinate[] StageFiveMask =
        {
            new GridCoordinate(0, 0), new GridCoordinate(1, 0), new GridCoordinate(2, 0),
            new GridCoordinate(3, 0), new GridCoordinate(4, 0), new GridCoordinate(5, 0),
            new GridCoordinate(6, 0),
            new GridCoordinate(1, 1), new GridCoordinate(2, 1), new GridCoordinate(3, 1),
            new GridCoordinate(4, 1), new GridCoordinate(5, 1),
            new GridCoordinate(2, 2), new GridCoordinate(3, 2), new GridCoordinate(4, 2),
            new GridCoordinate(2, 3), new GridCoordinate(3, 3), new GridCoordinate(4, 3),
            new GridCoordinate(2, 4), new GridCoordinate(3, 4), new GridCoordinate(4, 4)
        };

        private static readonly IReadOnlyList<BalanceLineup> AllLineups = Array.AsReadOnly(new[]
        {
            // Stage one, with siege. Four units on the untouched 3x3 and every one of its nine cells
            // used: this is what 45 starting coins and two free hands plus one 40-coin refresh buy.
            new BalanceLineup(
                "A_real_s1",
                "A_real_s1 · 真实开局（弩车2+长矛+冰+重骑，3×3 满格）",
                true,
                new[]
                {
                    new BalanceLineupSpawn("nuc", 2, 3, 2),
                    new BalanceLineupSpawn("mao", 1, 2, 2),
                    new BalanceLineupSpawn("bing", 1, 4, 3),
                    new BalanceLineupSpawn("zqi", 1, 2, 4)
                },
                StageOneMask,
                new[] { StageOneLevelId }),

            // Stage five, with siege: eleven units carrying two siege pieces and six merges,
            // spread over twenty-one unlocked cells. This is the board WO-F1 §C asks the balance
            // targets to be measured against.
            new BalanceLineup(
                "A_real_s5",
                "A_real_s5 · 真实积累 S5（11 单位 / 21 格 / 含弩车+冲车）",
                true,
                new[]
                {
                    new BalanceLineupSpawn("nuc", 2, 3, 2),
                    new BalanceLineupSpawn("mao", 2, 2, 2),
                    new BalanceLineupSpawn("bing", 1, 4, 3),
                    new BalanceLineupSpawn("zqi", 2, 2, 4),
                    new BalanceLineupSpawn("huo", 1, 3, 1),
                    new BalanceLineupSpawn("chc", 2, 4, 0),
                    new BalanceLineupSpawn("qqi", 2, 3, 0),
                    new BalanceLineupSpawn("gong", 1, 2, 0),
                    new BalanceLineupSpawn("zu", 1, 1, 0),
                    new BalanceLineupSpawn("dao", 2, 1, 1),
                    new BalanceLineupSpawn("gong", 1, 0, 0)
                },
                StageFiveMask,
                new[] { StageFiveLevelId }),

            // The same policy on the same seed, declining every siege card. Same unlock mask —
            // refusing siege changes what gets deployed, not how the grid grows.
            new BalanceLineup(
                "B_real_s1_nosiege",
                "B_real_s1_nosiege · 真实开局·无器械（长矛+冰+重骑+弓）",
                false,
                new[]
                {
                    new BalanceLineupSpawn("mao", 1, 3, 2),
                    new BalanceLineupSpawn("bing", 1, 2, 2),
                    new BalanceLineupSpawn("zqi", 1, 2, 4),
                    new BalanceLineupSpawn("gong", 1, 4, 2)
                },
                StageOneMask,
                new[] { StageOneLevelId }),

            new BalanceLineup(
                "B_real_s5_nosiege",
                "B_real_s5_nosiege · 真实积累 S5·无器械（12 单位 / 21 格）",
                false,
                new[]
                {
                    new BalanceLineupSpawn("mao", 2, 3, 2),
                    new BalanceLineupSpawn("bing", 1, 2, 2),
                    new BalanceLineupSpawn("zqi", 2, 2, 4),
                    new BalanceLineupSpawn("gong", 1, 4, 2),
                    new BalanceLineupSpawn("huo", 1, 3, 1),
                    new BalanceLineupSpawn("mao", 2, 4, 0),
                    new BalanceLineupSpawn("zu", 1, 3, 0),
                    new BalanceLineupSpawn("zu", 1, 5, 0),
                    new BalanceLineupSpawn("gong", 1, 2, 0),
                    new BalanceLineupSpawn("zu", 1, 1, 0),
                    new BalanceLineupSpawn("dao", 2, 1, 1),
                    new BalanceLineupSpawn("gong", 1, 0, 0)
                },
                StageFiveMask,
                new[] { StageFiveLevelId })
        });

        public static IReadOnlyList<BalanceLineup> All => AllLineups;

        /// <summary>Cohort id whose stage-1 numbers the locked win-rate and duration gates read.</summary>
        public const string StageOneGateLineupId = "A_real_s1";

        /// <summary>Cohort id whose stage-5 numbers the locked win-rate and duration gates read.</summary>
        public const string StageFiveGateLineupId = "A_real_s5";
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
            Cohorts = BuildCohorts();
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

        /// <summary>The lineup/level pairs that will actually run, after each lineup's own filter.</summary>
        public IReadOnlyList<BalanceCohortKey> Cohorts { get; }

        public int EffectiveMaxDegreeOfParallelism => MaxDegreeOfParallelism == 0
            ? Math.Max(1, Math.Min(4, Environment.ProcessorCount))
            : MaxDegreeOfParallelism;

        public int CohortCount => Cohorts.Count;

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
                // The target band is 90-150s. A battle still running at 300 has not "nearly won":
                // it is the stalemate WO-F1 §A set out to remove, and calling it a timeout at 300
                // instead of 600 halves the cost of finding that out.
                300d,
                false,
                outputDirectory);
        }

        private IReadOnlyList<BalanceCohortKey> BuildCohorts()
        {
            var cohorts = new List<BalanceCohortKey>();
            for (int lineupIndex = 0; lineupIndex < Lineups.Count; lineupIndex++)
            {
                BalanceLineup lineup = Lineups[lineupIndex];
                for (int levelIndex = 0; levelIndex < LevelIds.Count; levelIndex++)
                {
                    if (lineup.AppliesTo(LevelIds[levelIndex]))
                    {
                        cohorts.Add(new BalanceCohortKey(lineup, LevelIds[levelIndex]));
                    }
                }
            }

            if (cohorts.Count == 0)
            {
                throw new ArgumentException(
                    "No lineup applies to any requested level; the run would be empty.");
            }

            return cohorts.AsReadOnly();
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

    public readonly struct BalanceCohortKey
    {
        internal BalanceCohortKey(BalanceLineup lineup, string levelId)
        {
            Lineup = lineup;
            LevelId = levelId;
        }

        public BalanceLineup Lineup { get; }
        public string LevelId { get; }
    }

    public sealed class BalanceBattleResult
    {
        internal BalanceBattleResult(
            string lineupId,
            string lineupName,
            string levelId,
            int stageIndex,
            int unlockedCellCount,
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
            int peakConcurrentUnits,
            double lastAllyEntrySeconds,
            IReadOnlyList<float> enemyDeathPositionsY,
            IReadOnlyList<float> enemyDeathPositionsYBeforeCastle,
            IReadOnlyList<BalanceEntityResult> entities,
            IReadOnlyList<BalanceCoinPoint> coinCurve)
        {
            LineupId = lineupId;
            LineupName = lineupName;
            LevelId = levelId;
            StageIndex = stageIndex;
            UnlockedCellCount = unlockedCellCount;
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
            PeakConcurrentUnits = peakConcurrentUnits;
            LastAllyEntrySeconds = lastAllyEntrySeconds;
            EnemyDeathPositionsY = enemyDeathPositionsY;
            EnemyDeathPositionsYBeforeCastle = enemyDeathPositionsYBeforeCastle;
            Entities = entities;
            CoinCurve = coinCurve;
        }

        public string LineupId { get; }
        public string LineupName { get; }
        public string LevelId { get; }
        public int StageIndex { get; }

        /// <summary>Unlocked deployment cells this battle actually ran on.</summary>
        public int UnlockedCellCount { get; }

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

        /// <summary>
        /// Highest number of living combatants on the field at once. The enemy count went up by
        /// five to ten times in WO-F1, so this is the number the performance budget now rides on.
        /// </summary>
        public int PeakConcurrentUnits { get; }

        /// <summary>When the last queued ally walked on; zero when every unit was Instant.</summary>
        public double LastAllyEntrySeconds { get; }

        /// <summary>
        /// Y coordinate of every enemy death. Enemies spawn at Y=6 and the camp sits at Y=-8, so a
        /// high value means the enemy died at its own door and the front line never moved — which is
        /// the symptom WO-F1 §B exists to detect.
        /// </summary>
        public IReadOnlyList<float> EnemyDeathPositionsY { get; }

        /// <summary>Enemy death heights from before the castle spawned; see the cohort summary.</summary>
        public IReadOnlyList<float> EnemyDeathPositionsYBeforeCastle { get; }

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
            long damage,
            float deathPositionY)
        {
            EntityId = entityId;
            UnitId = unitId;
            Team = team;
            SpawnTimeSeconds = spawnTimeSeconds;
            EndTimeSeconds = endTimeSeconds;
            Survived = survived;
            Damage = damage;
            DeathPositionY = deathPositionY;
        }

        public int EntityId { get; }
        public string UnitId { get; }
        public BattleTeam Team { get; }
        public double SpawnTimeSeconds { get; }
        public double EndTimeSeconds { get; }
        public bool Survived { get; }
        public long Damage { get; }

        /// <summary>Where this unit died along the battle axis; NaN when it survived.</summary>
        public float DeathPositionY { get; }

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
            int unlockedCellCount,
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
            double meanEndCoins,
            double meanDroppedCoins,
            int peakConcurrentUnits,
            double enemyDeathYP90,
            double enemyDeathYP90BeforeCastle,
            double meanLastAllyEntrySeconds)
        {
            LineupId = lineupId;
            LineupName = lineupName;
            LevelId = levelId;
            StageIndex = stageIndex;
            UnlockedCellCount = unlockedCellCount;
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
            MeanDroppedCoins = meanDroppedCoins;
            PeakConcurrentUnits = peakConcurrentUnits;
            EnemyDeathYP90 = enemyDeathYP90;
            EnemyDeathYP90BeforeCastle = enemyDeathYP90BeforeCastle;
            MeanLastAllyEntrySeconds = meanLastAllyEntrySeconds;
        }

        public string LineupId { get; }
        public string LineupName { get; }
        public string LevelId { get; }
        public int StageIndex { get; }
        public int UnlockedCellCount { get; }
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

        /// <summary>Coins the battle itself paid out, averaged over the cohort.</summary>
        public double MeanDroppedCoins { get; }

        public int PeakConcurrentUnits { get; }

        /// <summary>
        /// 90th percentile of enemy death Y across the cohort. Below 4.0 the front line has formed
        /// in midfield; at or above it the enemy is still dying on its own doorstep (WO-F1 §B).
        /// </summary>
        public double EnemyDeathYP90 { get; }

        /// <summary>
        /// The same percentile restricted to deaths before the castle arrives. This is the number
        /// the WO-F1 §B threshold is about: whether a front line forms in midfield. The all-battle
        /// figure above also contains act three, where the line is supposed to march up and take
        /// the castle, so it reads high by design once the assault starts.
        /// </summary>
        public double EnemyDeathYP90BeforeCastle { get; }

        public double MeanLastAllyEntrySeconds { get; }
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

        /// <summary>Which act these counts describe: <c>ACT1</c>..<c>ACT3</c>, or <c>ALL</c>.</summary>
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
