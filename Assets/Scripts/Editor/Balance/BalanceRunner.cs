using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Deploy;
using UnityEngine;

namespace HanziDefend.Editor.Balance
{
    /// <summary>
    /// Deterministic, no-render balance harness. BattleSystem owns deterministic gameplay; this
    /// class only supplies reference deployments, advances the public Tick API and records
    /// events. Physics stepping is disabled by default because the current battle kernel has
    /// no physics bodies, while normal runtime callers retain BattleSystem's default path.
    /// </summary>
    public sealed class BalanceRunner
    {
        public BalanceRunReport Run(BalanceRunRequest request)
        {
            return Run(GameConfig.Load(), request);
        }

        public BalanceRunReport Run(GameConfig config, BalanceRunRequest request)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ValidateRequest(config, request);
            var jobs = new BalanceJob[request.TotalGames];
            var results = new BalanceBattleResult[request.TotalGames];
            var seedRng = new Rng(request.Seed);
            int jobIndex = 0;
            for (int cohortIndex = 0; cohortIndex < request.Cohorts.Count; cohortIndex++)
            {
                BalanceCohortKey cohort = request.Cohorts[cohortIndex];
                for (int gameIndex = 0; gameIndex < request.GamesPerCohort; gameIndex++)
                {
                    uint seed = seedRng.NextUInt();
                    jobs[jobIndex++] = new BalanceJob(
                        cohort.Lineup,
                        cohort.LevelId,
                        gameIndex + 1,
                        seed);
                }
            }

            Stopwatch totalStopwatch = Stopwatch.StartNew();
            if (request.UsesParallelExecution)
            {
                // Every false-physics BattleSystem owns only managed per-encounter state.
                // Jobs write to fixed indexes so completion order cannot perturb CSV order.
                Parallel.For(
                    0,
                    jobs.Length,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = request.EffectiveMaxDegreeOfParallelism
                    },
                    index =>
                    {
                        BalanceJob job = jobs[index];
                        results[index] = RunSingle(
                            config,
                            job.Lineup,
                            job.LevelId,
                            job.GameIndex,
                            job.Seed,
                            request.MaximumSimulatedSeconds,
                            false);
                    });
            }
            else
            {
                // Physics2D simulation mode is process-global, so the physics path stays serial.
                for (int index = 0; index < jobs.Length; index++)
                {
                    BalanceJob job = jobs[index];
                    results[index] = RunSingle(
                        config,
                        job.Lineup,
                        job.LevelId,
                        job.GameIndex,
                        job.Seed,
                        request.MaximumSimulatedSeconds,
                        request.SimulatePhysics);
                }
            }

            totalStopwatch.Stop();
            IReadOnlyList<BalanceBattleResult> battleSnapshot = Array.AsReadOnly(results);
            IReadOnlyList<BalanceCohortSummary> cohorts = SummarizeCohorts(battleSnapshot);
            IReadOnlyList<BalanceUnitSummary> units = SummarizeUnits(battleSnapshot);
            IReadOnlyList<BalanceArmorDistribution> armor = AnalyzeArmor(config, request.LevelIds);

            var report = new BalanceRunReport(
                request,
                battleSnapshot,
                cohorts,
                units,
                armor,
                totalStopwatch.Elapsed.TotalSeconds,
                request.OutputDirectory);
            if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
            {
                BalanceReportWriter.Write(report, request.OutputDirectory);
            }

            return report;
        }

        private readonly struct BalanceJob
        {
            internal BalanceJob(BalanceLineup lineup, string levelId, int gameIndex, uint seed)
            {
                Lineup = lineup;
                LevelId = levelId;
                GameIndex = gameIndex;
                Seed = seed;
            }

            internal BalanceLineup Lineup { get; }
            internal string LevelId { get; }
            internal int GameIndex { get; }
            internal uint Seed { get; }
        }

        private static BalanceBattleResult RunSingle(
            GameConfig config,
            BalanceLineup lineup,
            string levelId,
            int gameIndex,
            uint seed,
            double maximumSimulatedSeconds,
            bool simulatePhysics)
        {
            LevelDef level = config.GetLevel(levelId);
            IReadOnlyList<GridCoordinate> unlockedCells = ResolveUnlockedCells(level, lineup);
            var collector = new BalanceEventCollector(level.StartCoins);
            BattleSystem system = null;
            try
            {
                system = BattleSystem.CreateEncounter(
                    config,
                    levelId,
                    seed,
                    collector,
                    simulatePhysics: simulatePhysics);
                ResolveColumnSpan(unlockedCells, out int firstColumn, out int lastColumn);
                for (int index = 0; index < lineup.Spawns.Count; index++)
                {
                    BalanceLineupSpawn spawn = lineup.Spawns[index];
                    // Deploy, not Spawn: the harness must obey the same entry timing the live flow
                    // does, or it measures a formation the player never fields (WO-F1 §B).
                    system.Deploy(new UnitSpawnRequest(
                        spawn.UnitId,
                        spawn.Level,
                        DeploymentSpawnMap.Resolve(
                            config.Economy.Battle,
                            firstColumn,
                            lastColumn,
                            spawn.Anchor.Column,
                            spawn.Anchor.Row)));
                }

                if (config.Commanders.Count > 0)
                {
                    system.ConfigureCommander(config.Commanders[0].Id);
                }

                long maximumTicks = checked((long)Math.Ceiling(
                    maximumSimulatedSeconds / system.FixedDeltaTime));
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!system.IsSettled && system.TickIndex < maximumTicks)
                {
                    system.Tick(system.FixedDeltaTime);
                }

                stopwatch.Stop();
                bool timedOut = !system.IsSettled;
                BattleBaseSnapshot allyBase = system.GetBaseSnapshot(system.AllyBaseEntityId);
                float bossHp = float.NaN;
                float bossMaxHp = float.NaN;
                if (system.BossEntityId.HasValue)
                {
                    BattleUnitSnapshot boss = system.GetUnitSnapshot(system.BossEntityId.Value);
                    bossHp = boss.CurrentHp;
                    bossMaxHp = boss.Stats.MaxHp;
                }

                IReadOnlyList<BalanceEntityResult> entities = collector.CompleteEntities(
                    system.SimulatedTimeSeconds);
                collector.CompleteCoinCurve(system.SimulatedTimeSeconds, system.Coins);
                return new BalanceBattleResult(
                    lineup.Id,
                    lineup.DisplayName,
                    levelId,
                    level.StageIndex,
                    unlockedCells.Count,
                    gameIndex,
                    seed,
                    system.Result,
                    system.SimulatedTimeSeconds,
                    system.TickIndex,
                    stopwatch.Elapsed.TotalMilliseconds,
                    level.StartCoins,
                    system.DroppedCoins,
                    system.Coins,
                    allyBase.CurrentHp,
                    allyBase.MaxHp,
                    bossHp,
                    bossMaxHp,
                    timedOut,
                    collector.PeakConcurrentUnits,
                    collector.LastAllyEntrySeconds,
                    collector.EnemyDeathPositionsY,
                    collector.EnemyDeathPositionsYBeforeCastle,
                    entities,
                    collector.CoinCurve);
            }
            finally
            {
                system?.Dispose();
            }
        }

        private static void ValidateRequest(GameConfig config, BalanceRunRequest request)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < request.LevelIds.Count; index++)
            {
                config.GetLevel(request.LevelIds[index]);
            }

            for (int lineupIndex = 0; lineupIndex < request.Lineups.Count; lineupIndex++)
            {
                BalanceLineup lineup = request.Lineups[lineupIndex];
                if (!ids.Add(lineup.Id))
                {
                    throw new ArgumentException($"Duplicate lineup id '{lineup.Id}'.", nameof(request));
                }

                bool containsSiege = false;
                for (int spawnIndex = 0; spawnIndex < lineup.Spawns.Count; spawnIndex++)
                {
                    BalanceLineupSpawn spawn = lineup.Spawns[spawnIndex];
                    UnitDef unit = config.GetUnit(spawn.UnitId);
                    containsSiege |= unit.AtkType == AttackType.Siege;
                    if (spawn.Level < DeploymentGrid.MinimumUnitLevel
                        || spawn.Level > DeploymentGrid.MaximumUnitLevel)
                    {
                        throw new ArgumentException(
                            $"Lineup '{lineup.Id}' has invalid level {spawn.Level} for '{spawn.UnitId}'.",
                            nameof(request));
                    }
                }

                if (containsSiege != lineup.ExpectedToContainSiege)
                {
                    throw new ArgumentException(
                        $"Lineup '{lineup.Id}' expected siege={lineup.ExpectedToContainSiege} "
                        + $"but resolved siege={containsSiege} from GameData.",
                        nameof(request));
                }

                for (int levelIndex = 0; levelIndex < request.LevelIds.Count; levelIndex++)
                {
                    if (lineup.AppliesTo(request.LevelIds[levelIndex]))
                    {
                        ValidateDeployment(config, request.LevelIds[levelIndex], lineup);
                    }
                }
            }
        }

        /// <summary>
        /// The unlock mask a cohort deploys on: the lineup's own cells, or the level's starting rect
        /// when it declares none. A declared mask is checked for reachability first — see
        /// <see cref="RequireReachableMask"/> — so a probe cohort always describes a board some run
        /// could actually have grown.
        /// </summary>
        public static IReadOnlyList<GridCoordinate> ResolveUnlockedCells(
            LevelDef level,
            BalanceLineup lineup)
        {
            if (level == null)
            {
                throw new ArgumentNullException(nameof(level));
            }

            if (lineup == null)
            {
                throw new ArgumentNullException(nameof(lineup));
            }

            if (lineup.UnlockedCells == null)
            {
                return new List<GridCoordinate>(DeploymentGrid.EnumerateRect(level.InitialUnlock))
                    .AsReadOnly();
            }

            RequireReachableMask(level, lineup);
            return lineup.UnlockedCells;
        }

        /// <summary>
        /// A mask is reachable exactly when a real run could have produced it: inside the field,
        /// containing every cell the level starts with, and edge-connected — the three properties
        /// M1-04 §4.3 gives unlock-card placement, which together guarantee the mask is one blob
        /// grown outward from the starting rect rather than an arbitrary set of cells.
        /// </summary>
        private static void RequireReachableMask(LevelDef level, BalanceLineup lineup)
        {
            var cells = new HashSet<GridCoordinate>(lineup.UnlockedCells);
            foreach (GridCoordinate cell in lineup.UnlockedCells)
            {
                if (cell.Column < 0 || cell.Column >= level.GridWidth
                    || cell.Row < 0 || cell.Row >= level.GridHeight)
                {
                    throw new ArgumentException(
                        $"Lineup '{lineup.Id}' unlocks {cell}, outside the "
                        + $"{level.GridWidth}x{level.GridHeight} field.",
                        nameof(lineup));
                }
            }

            foreach (GridCoordinate cell in DeploymentGrid.EnumerateRect(level.InitialUnlock))
            {
                if (!cells.Contains(cell))
                {
                    throw new ArgumentException(
                        $"Lineup '{lineup.Id}' omits {cell}, which level '{level.Id}' starts unlocked; "
                        + "a run can only add cells.",
                        nameof(lineup));
                }
            }

            var reached = new HashSet<GridCoordinate>();
            var frontier = new Stack<GridCoordinate>();
            GridCoordinate seed = new GridCoordinate(level.InitialUnlock.Col, level.InitialUnlock.Row);
            frontier.Push(seed);
            reached.Add(seed);
            GridCoordinate[] steps =
            {
                new GridCoordinate(1, 0), new GridCoordinate(-1, 0),
                new GridCoordinate(0, 1), new GridCoordinate(0, -1)
            };
            while (frontier.Count > 0)
            {
                GridCoordinate current = frontier.Pop();
                for (int index = 0; index < steps.Length; index++)
                {
                    GridCoordinate next = current + steps[index];
                    if (cells.Contains(next) && reached.Add(next))
                    {
                        frontier.Push(next);
                    }
                }
            }

            if (reached.Count != cells.Count)
            {
                throw new ArgumentException(
                    $"Lineup '{lineup.Id}' declares {cells.Count} unlocked cells but only "
                    + $"{reached.Count} of them connect to the starting rect; unlock cards cannot "
                    + "create islands.",
                    nameof(lineup));
            }
        }

        private static DeploymentGrid CreateGrid(
            GameConfig config,
            string levelId,
            BalanceLineup lineup)
        {
            LevelDef level = config.GetLevel(levelId);
            return new DeploymentGrid(
                level.GridWidth,
                level.GridHeight,
                ResolveUnlockedCells(level, lineup),
                DeploymentGridOrientation.ColumnsHorizontal,
                config.Economy.CardPool,
                config.Economy.GridUnlock.BaseAnchorRowOffset);
        }

        private static void ValidateDeployment(
            GameConfig config,
            string levelId,
            BalanceLineup lineup)
        {
            DeploymentGrid grid = CreateGrid(config, levelId, lineup);
            for (int index = 0; index < lineup.Spawns.Count; index++)
            {
                BalanceLineupSpawn spawn = lineup.Spawns[index];
                UnitDef definition = config.GetUnit(spawn.UnitId);
                var incoming = new DeploymentUnit(
                    $"{lineup.Id}:{index}",
                    spawn.UnitId,
                    spawn.Level,
                    UnitFootprint.FromDefinition(definition));
                DeploymentEvaluation evaluation = grid.Evaluate(incoming, spawn.Anchor);
                if (!evaluation.IsValid || evaluation.Action != DeploymentActionKind.Place)
                {
                    throw new ArgumentException(
                        $"Lineup '{lineup.Id}' does not fit level '{levelId}' at "
                        + $"{spawn.Anchor} for '{spawn.UnitId}': {evaluation.Message}",
                        nameof(lineup));
                }

                grid.Apply(incoming, spawn.Anchor);
            }
        }

        /// <summary>Leftmost and rightmost unlocked columns of an explicit cell list.</summary>
        internal static void ResolveColumnSpan(
            IReadOnlyList<GridCoordinate> cells,
            out int firstColumn,
            out int lastColumn)
        {
            firstColumn = int.MaxValue;
            lastColumn = int.MinValue;
            for (int index = 0; index < cells.Count; index++)
            {
                int column = cells[index].Column;
                if (column < firstColumn) firstColumn = column;
                if (column > lastColumn) lastColumn = column;
            }

            if (firstColumn > lastColumn)
            {
                firstColumn = 0;
                lastColumn = 0;
            }
        }

        private static IReadOnlyList<BalanceCohortSummary> SummarizeCohorts(
            IReadOnlyList<BalanceBattleResult> battles)
        {
            BalanceCohortSummary[] result = battles
                .GroupBy(value => new
                {
                    value.LineupId,
                    value.LineupName,
                    value.LevelId,
                    value.StageIndex,
                    value.UnlockedCellCount
                })
                .OrderBy(group => group.Key.LineupId, StringComparer.Ordinal)
                .ThenBy(group => group.Key.StageIndex)
                .Select(group =>
                {
                    BalanceBattleResult[] values = group.ToArray();
                    double[] durations = values
                        .Select(value => value.DurationSeconds)
                        .OrderBy(value => value)
                        .ToArray();
                    double[] deathY = values
                        .SelectMany(value => value.EnemyDeathPositionsY)
                        .Select(value => (double)value)
                        .OrderBy(value => value)
                        .ToArray();
                    double[] deathYBeforeCastle = values
                        .SelectMany(value => value.EnemyDeathPositionsYBeforeCastle)
                        .Select(value => (double)value)
                        .OrderBy(value => value)
                        .ToArray();
                    int wins = values.Count(value => value.Result == BattleResult.Win);
                    int losses = values.Count(value => value.Result == BattleResult.Lose);
                    int timeouts = values.Count(value => value.TimedOut);
                    return new BalanceCohortSummary(
                        group.Key.LineupId,
                        group.Key.LineupName,
                        group.Key.LevelId,
                        group.Key.StageIndex,
                        group.Key.UnlockedCellCount,
                        values.Length,
                        wins,
                        losses,
                        timeouts,
                        values.Length == 0 ? 0d : (double)wins / values.Length,
                        durations.Length == 0 ? 0d : durations.Average(),
                        Percentile(durations, 0.50d),
                        Percentile(durations, 0.95d),
                        values.Length == 0 ? 0d : values.Average(value => value.WallClockMilliseconds),
                        values.Sum(value => value.WallClockMilliseconds) / 1000d,
                        values.Length == 0 ? 0d : values.Average(value => value.EndCoins),
                        values.Length == 0 ? 0d : values.Average(value => value.DroppedCoins),
                        values.Length == 0 ? 0 : values.Max(value => value.PeakConcurrentUnits),
                        deathY.Length == 0 ? double.NaN : Percentile(deathY, 0.90d),
                        deathYBeforeCastle.Length == 0
                            ? double.NaN
                            : Percentile(deathYBeforeCastle, 0.90d),
                        values.Length == 0 ? 0d : values.Average(value => value.LastAllyEntrySeconds));
                })
                .ToArray();
            return Array.AsReadOnly(result);
        }

        private static IReadOnlyList<BalanceUnitSummary> SummarizeUnits(
            IReadOnlyList<BalanceBattleResult> battles)
        {
            var result = new List<BalanceUnitSummary>();
            foreach (IGrouping<string, BalanceBattleResult> cohort in battles
                         .GroupBy(CohortKey)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                BalanceBattleResult first = cohort.First();
                double battleSeconds = cohort.Sum(value => value.DurationSeconds);
                IEnumerable<BalanceEntityResult> entities = cohort.SelectMany(value => value.Entities);
                foreach (IGrouping<string, BalanceEntityResult> unitGroup in entities
                             .GroupBy(value => value.Team + "\u001f" + value.UnitId)
                             .OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    BalanceEntityResult unit = unitGroup.First();
                    BalanceEntityResult[] values = unitGroup.ToArray();
                    result.Add(new BalanceUnitSummary(
                        first.LineupId,
                        first.LevelId,
                        first.StageIndex,
                        unit.Team,
                        unit.UnitId,
                        values.Length,
                        values.Count(value => value.Survived),
                        values.Sum(value => value.Damage),
                        battleSeconds,
                        values.Average(value => value.SurvivalSeconds)));
                }
            }

            return Array.AsReadOnly(result.ToArray());
        }

        /// <summary>
        /// Counts armour by act rather than by wave-number bands. The old W01-W06 / W07-W12 /
        /// W13-W19 slicing was a stand-in for the three difficulty phases back when a wave was one
        /// enemy; now the acts are declared in the data, so the analysis reads them directly.
        /// </summary>
        private static IReadOnlyList<BalanceArmorDistribution> AnalyzeArmor(
            GameConfig config,
            IReadOnlyList<string> levelIds)
        {
            var waveSetIds = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<BalanceArmorDistribution>();
            for (int index = 0; index < levelIds.Count; index++)
            {
                string waveSetId = config.GetLevel(levelIds[index]).WaveSetId;
                if (!waveSetIds.Add(waveSetId))
                {
                    continue;
                }

                WaveTimeline timeline = WaveTimeline.Compile(config, config.GetWaveSet(waveSetId));
                int unarmored = 0;
                int light = 0;
                int heavy = 0;
                int building = 0;
                int firstWave = int.MaxValue;
                int lastWave = int.MinValue;
                for (int actIndex = 0; actIndex < timeline.Acts.Count; actIndex++)
                {
                    WaveActSummary act = timeline.Acts[actIndex];
                    result.Add(new BalanceArmorDistribution(
                        waveSetId,
                        "ACT" + act.Act.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        act.FirstWaveIndex,
                        act.LastWaveIndex,
                        act.Unarmored,
                        act.Light,
                        act.Heavy,
                        act.Building,
                        0));
                    unarmored += act.Unarmored;
                    light += act.Light;
                    heavy += act.Heavy;
                    building += act.Building;
                    if (act.FirstWaveIndex < firstWave) firstWave = act.FirstWaveIndex;
                    if (act.LastWaveIndex > lastWave) lastWave = act.LastWaveIndex;
                }

                result.Add(new BalanceArmorDistribution(
                    waveSetId,
                    "ALL",
                    firstWave == int.MaxValue ? 0 : firstWave,
                    lastWave == int.MinValue ? 0 : lastWave,
                    unarmored,
                    light,
                    heavy,
                    building,
                    0));
            }

            return Array.AsReadOnly(result.ToArray());
        }

        private static string CohortKey(BalanceBattleResult value)
        {
            return value.LineupId + "\u001f" + value.LevelId + "\u001f" + value.StageIndex;
        }

        private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0)
            {
                return 0d;
            }

            int rank = Math.Max(0, (int)Math.Ceiling(percentile * sortedValues.Count) - 1);
            return sortedValues[Math.Min(rank, sortedValues.Count - 1)];
        }

        private sealed class BalanceEventCollector
            : IBattleEncounterEvents, IBattleEffectEvents, IBattleDeploymentEvents
        {
            private readonly Dictionary<int, MutableEntity> entities =
                new Dictionary<int, MutableEntity>();
            private readonly List<BalanceCoinPoint> coinCurve = new List<BalanceCoinPoint>();
            private readonly List<float> enemyDeathPositionsY = new List<float>();
            private readonly List<float> enemyDeathPositionsYBeforeCastle = new List<float>();
            private int currentCoins;
            private int aliveUnits;
            private bool castleHasSpawned;

            internal BalanceEventCollector(int startCoins)
            {
                currentCoins = startCoins;
                coinCurve.Add(new BalanceCoinPoint(0d, "Start", string.Empty, 0, startCoins));
            }

            internal IReadOnlyList<BalanceCoinPoint> CoinCurve =>
                Array.AsReadOnly(coinCurve.ToArray());

            internal IReadOnlyList<float> EnemyDeathPositionsY =>
                Array.AsReadOnly(enemyDeathPositionsY.ToArray());

            /// <summary>
            /// Deaths from before the castle arrives — the window the WO-F1 §B threshold is really
            /// asking about. Act three deliberately marches the line up to the castle, so deaths up
            /// there are the assault working, not the front line failing to form.
            /// </summary>
            internal IReadOnlyList<float> EnemyDeathPositionsYBeforeCastle =>
                Array.AsReadOnly(enemyDeathPositionsYBeforeCastle.ToArray());

            internal int PeakConcurrentUnits { get; private set; }

            internal double LastAllyEntrySeconds { get; private set; }

            public void UnitSpawned(UnitSpawnedEvent eventData)
            {
                entities.Add(eventData.EntityId, new MutableEntity(
                    eventData.EntityId,
                    eventData.DefinitionId,
                    eventData.Team,
                    eventData.SimulatedTimeSeconds));
                aliveUnits++;
                if (aliveUnits > PeakConcurrentUnits)
                {
                    PeakConcurrentUnits = aliveUnits;
                }
            }

            public void UnitAttacked(UnitAttackedEvent eventData)
            {
            }

            public void DamageDealt(DamageDealtEvent eventData)
            {
                if (eventData.SourceEntityId != 0
                    && entities.TryGetValue(eventData.SourceEntityId, out MutableEntity source))
                {
                    source.Damage = checked(source.Damage + eventData.Amount);
                }
            }

            public void UnitDied(UnitDiedEvent eventData)
            {
                aliveUnits--;
                if (entities.TryGetValue(eventData.EntityId, out MutableEntity entity))
                {
                    entity.DeathTimeSeconds = eventData.SimulatedTimeSeconds;
                    entity.DeathPositionY = eventData.Position.y;
                }

                if (eventData.Team == BattleTeam.Enemy)
                {
                    enemyDeathPositionsY.Add(eventData.Position.y);
                    if (!castleHasSpawned)
                    {
                        enemyDeathPositionsYBeforeCastle.Add(eventData.Position.y);
                    }
                }
            }

            public void CoinDropped(CoinDroppedEvent eventData)
            {
                currentCoins = checked(currentCoins + eventData.Amount);
                coinCurve.Add(new BalanceCoinPoint(
                    eventData.SimulatedTimeSeconds,
                    "Drop",
                    eventData.SourceDefinitionId,
                    eventData.Amount,
                    currentCoins));
            }

            public void WaveStarted(WaveStartedEvent eventData)
            {
            }

            public void BossSpawned(BossSpawnedEvent eventData)
            {
                castleHasSpawned = true;
                if (!entities.ContainsKey(eventData.EntityId))
                {
                    entities.Add(eventData.EntityId, new MutableEntity(
                        eventData.EntityId,
                        eventData.DefinitionId,
                        BattleTeam.Enemy,
                        eventData.SimulatedTimeSeconds));
                }
            }

            public void BaseDamaged(BaseDamagedEvent eventData)
            {
            }

            public void BattleSettled(BattleSettledEvent eventData)
            {
            }

            public void EffectApplied(EffectAppliedEvent eventData)
            {
            }

            public void EffectExpired(EffectExpiredEvent eventData)
            {
            }

            public void UnitHealed(UnitHealedEvent eventData)
            {
            }

            public void ShieldChanged(ShieldChangedEvent eventData)
            {
            }

            public void CoinsModified(CoinsModifiedEvent eventData)
            {
                int delta = checked(eventData.CoinsAfter - currentCoins);
                currentCoins = eventData.CoinsAfter;
                coinCurve.Add(new BalanceCoinPoint(
                    eventData.SimulatedTimeSeconds,
                    "Effect",
                    eventData.EffectId,
                    delta,
                    currentCoins));
            }

            public void AllyDeploymentQueued(AllyDeploymentQueuedEvent eventData)
            {
                if (eventData.EntryTimeSeconds > LastAllyEntrySeconds)
                {
                    LastAllyEntrySeconds = eventData.EntryTimeSeconds;
                }
            }

            public void AllyDeploymentEntered(AllyDeploymentEnteredEvent eventData)
            {
            }

            internal IReadOnlyList<BalanceEntityResult> CompleteEntities(double endTimeSeconds)
            {
                BalanceEntityResult[] result = entities.Values
                    .OrderBy(value => value.EntityId)
                    .Select(value => new BalanceEntityResult(
                        value.EntityId,
                        value.UnitId,
                        value.Team,
                        value.SpawnTimeSeconds,
                        value.DeathTimeSeconds ?? endTimeSeconds,
                        !value.DeathTimeSeconds.HasValue,
                        value.Damage,
                        value.DeathPositionY))
                    .ToArray();
                return Array.AsReadOnly(result);
            }

            internal void CompleteCoinCurve(double endTimeSeconds, int endCoins)
            {
                currentCoins = endCoins;
                coinCurve.Add(new BalanceCoinPoint(
                    endTimeSeconds,
                    "End",
                    string.Empty,
                    0,
                    endCoins));
            }

            private sealed class MutableEntity
            {
                internal MutableEntity(
                    int entityId,
                    string unitId,
                    BattleTeam team,
                    double spawnTimeSeconds)
                {
                    EntityId = entityId;
                    UnitId = unitId;
                    Team = team;
                    SpawnTimeSeconds = spawnTimeSeconds;
                    DeathPositionY = float.NaN;
                }

                internal int EntityId { get; }
                internal string UnitId { get; }
                internal BattleTeam Team { get; }
                internal double SpawnTimeSeconds { get; }
                internal double? DeathTimeSeconds { get; set; }
                internal float DeathPositionY { get; set; }
                internal long Damage { get; set; }
            }
        }
    }
}
