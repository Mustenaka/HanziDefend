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
            for (int lineupIndex = 0; lineupIndex < request.Lineups.Count; lineupIndex++)
            {
                BalanceLineup lineup = request.Lineups[lineupIndex];
                for (int levelIndex = 0; levelIndex < request.LevelIds.Count; levelIndex++)
                {
                    string levelId = request.LevelIds[levelIndex];
                    for (int gameIndex = 0; gameIndex < request.GamesPerCohort; gameIndex++)
                    {
                        uint seed = seedRng.NextUInt();
                        jobs[jobIndex++] = new BalanceJob(
                            lineup,
                            levelId,
                            gameIndex + 1,
                            seed);
                    }
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
            int gridColumns = ResolveGridColumns(level, lineup);
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
                for (int index = 0; index < lineup.Spawns.Count; index++)
                {
                    BalanceLineupSpawn spawn = lineup.Spawns[index];
                    system.Spawn(new UnitSpawnRequest(
                        spawn.UnitId,
                        spawn.Level,
                        ResolveDeploymentPosition(config, gridColumns, spawn.Anchor)));
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
                    gridColumns,
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
                    ValidateDeployment(config, request.LevelIds[levelIndex], lineup);
                }
            }
        }

        /// <summary>
        /// Width of the unlocked region a cohort deploys on: the level's own initial unlock rect
        /// unless the lineup asks for a wider one. An override describes a centred rectangle that
        /// unlock cards can actually grow into, so a probe cohort always describes a reachable state.
        /// Lineup anchors are relative to that rectangle's lower-left corner.
        /// </summary>
        public static int ResolveGridColumns(LevelDef level, BalanceLineup lineup)
        {
            if (level == null)
            {
                throw new ArgumentNullException(nameof(level));
            }

            if (lineup == null)
            {
                throw new ArgumentNullException(nameof(lineup));
            }

            if (lineup.GridColumnsOverride == 0)
            {
                return level.InitialUnlock.Width;
            }

            if (lineup.GridColumnsOverride < level.InitialUnlock.Width
                || lineup.GridColumnsOverride > level.GridWidth)
            {
                throw new ArgumentException(
                    $"Lineup '{lineup.Id}' overrides level '{level.Id}' to "
                    + $"{lineup.GridColumnsOverride} unlocked columns, outside the reachable range "
                    + $"[{level.InitialUnlock.Width}, {level.GridWidth}].",
                    nameof(lineup));
            }

            if (((level.GridWidth - lineup.GridColumnsOverride) & 1) != 0)
            {
                throw new ArgumentException(
                    $"Lineup '{lineup.Id}' overrides level '{level.Id}' to "
                    + $"{lineup.GridColumnsOverride} unlocked columns, which cannot be centred on a "
                    + $"{level.GridWidth}-wide field.",
                    nameof(lineup));
            }

            return lineup.GridColumnsOverride;
        }

        private static DeploymentGrid CreateGrid(
            GameConfig config,
            string levelId,
            BalanceLineup lineup)
        {
            LevelDef level = config.GetLevel(levelId);
            int columns = ResolveGridColumns(level, lineup);
            return columns == level.InitialUnlock.Width
                ? DeploymentGrid.CreateFromConfig(config, levelId)
                : new DeploymentGrid(
                    level.GridWidth,
                    level.GridHeight,
                    DeploymentGrid.EnumerateRect(new GridRectDef
                    {
                        Col = (level.GridWidth - columns) / 2,
                        Row = level.InitialUnlock.Row,
                        Width = columns,
                        Height = level.InitialUnlock.Height
                    }),
                    DeploymentGridOrientation.ColumnsHorizontal,
                    config.Economy.CardPool,
                    config.Economy.GridUnlock.BaseAnchorRowOffset);
        }

        /// <summary>
        /// Translates a lineup anchor from unlocked-region space into playfield space, so lineup
        /// data stays written against a 0-based rectangle while the grid itself is the full field.
        /// </summary>
        public static GridCoordinate ToFieldAnchor(LevelDef level, BalanceLineup lineup, GridCoordinate anchor)
        {
            int columns = ResolveGridColumns(level, lineup);
            return new GridCoordinate(
                anchor.Column + ((level.GridWidth - columns) / 2),
                anchor.Row + level.InitialUnlock.Row);
        }

        private static void ValidateDeployment(
            GameConfig config,
            string levelId,
            BalanceLineup lineup)
        {
            LevelDef level = config.GetLevel(levelId);
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
                GridCoordinate anchor = ToFieldAnchor(level, lineup, spawn.Anchor);
                DeploymentEvaluation evaluation = grid.Evaluate(incoming, anchor);
                if (!evaluation.IsValid || evaluation.Action != DeploymentActionKind.Place)
                {
                    throw new ArgumentException(
                        $"Lineup '{lineup.Id}' does not fit level '{levelId}' at "
                        + $"{spawn.Anchor} for '{spawn.UnitId}': {evaluation.Message}",
                        nameof(lineup));
                }

                grid.Apply(incoming, anchor);
            }
        }

        private static Vector2 ResolveDeploymentPosition(
            GameConfig config,
            int gridColumns,
            GridCoordinate anchor)
        {
            BattleRulesDef battle = config.Economy.Battle;
            Position2Def basePosition = battle.AllyBasePosition;
            Position2Def originOffset = battle.DeploymentOriginOffset;
            Position2Def cellSize = battle.DeploymentCellSize;
            float x = basePosition.X
                      + originOffset.X
                      + (anchor.Column - (gridColumns - 1) * 0.5f) * cellSize.X;
            float y = basePosition.Y + originOffset.Y + anchor.Row * cellSize.Y;
            return new Vector2(x, y);
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
                    value.GridColumns
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
                    int wins = values.Count(value => value.Result == BattleResult.Win);
                    int losses = values.Count(value => value.Result == BattleResult.Lose);
                    int timeouts = values.Count(value => value.TimedOut);
                    return new BalanceCohortSummary(
                        group.Key.LineupId,
                        group.Key.LineupName,
                        group.Key.LevelId,
                        group.Key.StageIndex,
                        group.Key.GridColumns,
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
                        values.Length == 0 ? 0d : values.Average(value => value.EndCoins));
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

                WaveSetDef waveSet = config.GetWaveSet(waveSetId);
                result.Add(CountArmor(config, waveSet, "W01-W06", 1, 6));
                result.Add(CountArmor(config, waveSet, "W07-W12", 7, 12));
                result.Add(CountArmor(config, waveSet, "W13-W19", 13, 19));
                result.Add(CountArmor(config, waveSet, "W01-W19", 1, 19));
                result.Add(CountArmor(config, waveSet, "W20", 20, 20));
            }

            return Array.AsReadOnly(result.ToArray());
        }

        private static BalanceArmorDistribution CountArmor(
            GameConfig config,
            WaveSetDef waveSet,
            string phase,
            int firstWave,
            int lastWave)
        {
            int unarmored = 0;
            int light = 0;
            int heavy = 0;
            int building = 0;
            int other = 0;
            IEnumerable<WaveSpawnDef> spawns = waveSet.Waves
                .Where(value => value.Index >= firstWave && value.Index <= lastWave)
                .SelectMany(value => value.Spawns);
            foreach (WaveSpawnDef spawn in spawns)
            {
                ArmorType armorType = config.UnitsById.TryGetValue(spawn.UnitId, out UnitDef unit)
                    ? unit.ArmorType
                    : config.GetBoss(spawn.UnitId).ArmorType;
                switch (armorType)
                {
                    case ArmorType.Unarmored:
                        unarmored = checked(unarmored + spawn.Count);
                        break;
                    case ArmorType.Light:
                        light = checked(light + spawn.Count);
                        break;
                    case ArmorType.Heavy:
                        heavy = checked(heavy + spawn.Count);
                        break;
                    case ArmorType.Building:
                        building = checked(building + spawn.Count);
                        break;
                    default:
                        other = checked(other + spawn.Count);
                        break;
                }
            }

            return new BalanceArmorDistribution(
                waveSet.Id,
                phase,
                firstWave,
                lastWave,
                unarmored,
                light,
                heavy,
                building,
                other);
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

        private sealed class BalanceEventCollector : IBattleEncounterEvents, IBattleEffectEvents
        {
            private readonly Dictionary<int, MutableEntity> entities =
                new Dictionary<int, MutableEntity>();
            private readonly List<BalanceCoinPoint> coinCurve = new List<BalanceCoinPoint>();
            private int currentCoins;

            internal BalanceEventCollector(int startCoins)
            {
                currentCoins = startCoins;
                coinCurve.Add(new BalanceCoinPoint(0d, "Start", string.Empty, 0, startCoins));
            }

            internal IReadOnlyList<BalanceCoinPoint> CoinCurve =>
                Array.AsReadOnly(coinCurve.ToArray());

            public void UnitSpawned(UnitSpawnedEvent eventData)
            {
                entities.Add(eventData.EntityId, new MutableEntity(
                    eventData.EntityId,
                    eventData.DefinitionId,
                    eventData.Team,
                    eventData.SimulatedTimeSeconds));
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
                if (entities.TryGetValue(eventData.EntityId, out MutableEntity entity))
                {
                    entity.DeathTimeSeconds = eventData.SimulatedTimeSeconds;
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
                        value.Damage))
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
                }

                internal int EntityId { get; }
                internal string UnitId { get; }
                internal BattleTeam Team { get; }
                internal double SpawnTimeSeconds { get; }
                internal double? DeathTimeSeconds { get; set; }
                internal long Damage { get; set; }
            }
        }
    }
}
