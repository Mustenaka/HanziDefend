using System;
using System.Collections.Generic;
using HanziDefend.Data;
using HanziDefend.Gameplay.Performance;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    /// <summary>
    /// Deterministic, manually-driven battle kernel. The public constructor creates the
    /// WO-B1 sandbox; CreateEncounter adds bases, the configured timeline and settlement.
    /// </summary>
    public sealed partial class BattleSystem : IDisposable
    {
        public const string IceAuraPresentationEffectId = "trait_ice_aura";

        private readonly GameConfig config;
        private readonly BattleRulesDef rules;
        private readonly IBattleEvents events;
        private readonly IBattleEncounterEvents encounterEvents;
        private readonly IBattleEffectEvents effectEvents;
        private readonly RngStreams rngStreams;
        private readonly List<BattleUnit> units = new List<BattleUnit>();
        private readonly List<BattleUnit> aliveAllyUnits = new List<BattleUnit>();
        private readonly List<BattleUnit> aliveEnemyUnits = new List<BattleUnit>();
        private readonly Dictionary<int, BattleUnit> unitsById = new Dictionary<int, BattleUnit>();
        private readonly Dictionary<int, BattleBase> basesById = new Dictionary<int, BattleBase>();
        private readonly List<AttackIntent> anchorAttackIntents = new List<AttackIntent>();
        private readonly List<BattleUnit> separationUnits = new List<BattleUnit>();
        private readonly Dictionary<long, List<BattleUnit>> separationBuckets =
            new Dictionary<long, List<BattleUnit>>();
        private readonly List<List<BattleUnit>> activeSeparationBuckets =
            new List<List<BattleUnit>>();
        private readonly List<BattleUnit> separationNeighbors = new List<BattleUnit>();
        private readonly List<Vector2> targetQueryPositions = new List<Vector2>();
        private readonly SimulationMode2D previousSimulationMode;
        private readonly bool simulatePhysics;

        private static readonly Comparison<BattleUnit> SeparationOrderComparison =
            CompareSeparationOrder;

        private WaveScheduler waveScheduler;
        private BattleBase allyBase;
        private BattleBase enemyBase;
        private int nextEntityId = 1;
        private long nextEventSequence = 1;
        private long nextAttackId = 1;
        private bool encounterInitialized;
        private bool tickInProgress;
        private bool tickSettlementPhase;
        private bool disposed;

        public BattleSystem(
            GameConfig config,
            uint seed,
            IBattleEvents battleEvents = null,
            bool simulatePhysics = true)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            rules = config.Economy?.Battle
                    ?? throw new ArgumentException("Battle rules are required.", nameof(config));
            events = battleEvents ?? NullBattleEvents.Instance;
            encounterEvents = battleEvents as IBattleEncounterEvents ?? NullBattleEvents.Instance;
            effectEvents = battleEvents as IBattleEffectEvents ?? NullBattleEffectEvents.Instance;
            rngStreams = new RngStreams(seed);
            this.simulatePhysics = simulatePhysics;

            if (simulatePhysics)
            {
                previousSimulationMode = Physics2D.simulationMode;
                Physics2D.simulationMode = SimulationMode2D.Script;
            }
        }

        public static BattleSystem CreateEncounter(
            GameConfig config,
            string levelId,
            uint seed,
            IBattleEvents battleEvents = null,
            bool simulatePhysics = true)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            LevelDef level = config.GetLevel(levelId);
            var system = new BattleSystem(config, seed, battleEvents, simulatePhysics);
            try
            {
                system.InitializeEncounter(level);
                return system;
            }
            catch
            {
                system.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Recreates an encounter from the run's persisted independent RNG streams. The wave
        /// scheduler consumes the restored Battle stream while CardDraw and Settlement remain
        /// untouched, preserving a five-stage replay as one continuous run.
        /// </summary>
        public static BattleSystem CreateEncounter(
            GameConfig config,
            string levelId,
            uint seed,
            RngStreamsState randomState,
            IBattleEvents battleEvents = null,
            bool simulatePhysics = true)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            LevelDef level = config.GetLevel(levelId);
            var system = new BattleSystem(config, seed, battleEvents, simulatePhysics);
            try
            {
                if (randomState != null)
                {
                    system.rngStreams.RestoreState(randomState);
                }
                system.InitializeEncounter(level);
                return system;
            }
            catch
            {
                system.Dispose();
                throw;
            }
        }

        public float FixedDeltaTime => 1f / rules.TickRateHz;

        public long TickIndex { get; private set; }

        public double SimulatedTimeSeconds { get; private set; }

        public int DroppedCoins { get; private set; }

        public BattleResult Result { get; private set; }

        public bool IsSettled => Result != BattleResult.None;

        public bool IsEncounter => encounterInitialized;

        public int TotalWaveCount => waveScheduler?.TotalWaveCount ?? 0;

        public int CurrentWaveIndex => waveScheduler?.CurrentWaveIndex ?? 0;

        public int AllyBaseEntityId
        {
            get
            {
                ThrowIfDisposed();
                RequireEncounter();
                return allyBase.EntityId;
            }
        }

        public int EnemyBaseEntityId
        {
            get
            {
                ThrowIfDisposed();
                RequireEncounter();
                return enemyBase.EntityId;
            }
        }

        public int? BossEntityId { get; private set; }

        public RngState SaveRngState()
        {
            ThrowIfDisposed();
            return rngStreams.Battle.SaveState();
        }

        public RngStreamsState SaveRngStreamsState()
        {
            ThrowIfDisposed();
            return rngStreams.SaveState();
        }

        public int Spawn(UnitSpawnRequest request)
        {
            ThrowIfDisposed();
            ThrowIfSettled();
            if (string.IsNullOrWhiteSpace(request.DefinitionId))
            {
                throw new ArgumentException("Definition id is required.", nameof(request));
            }

            UnitDef definition = config.GetUnit(request.DefinitionId);
            return SpawnUnit(definition, request.Level, request.Position, request.RewardRank, null, null);
        }

        public int GetAliveCount(BattleTeam team)
        {
            ThrowIfDisposed();
            int result = 0;
            for (int index = 0; index < units.Count; index++)
            {
                BattleUnit unit = units[index];
                if (unit.Team == team && unit.State != BattleUnitState.Dead)
                {
                    result++;
                }
            }

            return result;
        }

        public BattleUnitSnapshot GetUnitSnapshot(int entityId)
        {
            ThrowIfDisposed();
            if (!unitsById.TryGetValue(entityId, out BattleUnit unit))
            {
                throw new KeyNotFoundException($"Unknown battle unit {entityId}.");
            }

            return unit.Snapshot();
        }

        public BattleBaseSnapshot GetBaseSnapshot(int entityId)
        {
            ThrowIfDisposed();
            if (!basesById.TryGetValue(entityId, out BattleBase value))
            {
                throw new KeyNotFoundException($"Unknown battle base {entityId}.");
            }

            return value.Snapshot();
        }

        public IReadOnlyList<BattleUnitSnapshot> CaptureSnapshot()
        {
            ThrowIfDisposed();
            var result = new BattleUnitSnapshot[units.Count];
            for (int index = 0; index < units.Count; index++)
            {
                result[index] = units[index].Snapshot();
            }

            return result;
        }

        public void Tick(float dt)
        {
            ThrowIfDisposed();
            if (IsSettled)
            {
                return;
            }

            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(dt), "Tick duration must be finite and positive.");
            }

            if (tickInProgress)
            {
                throw new InvalidOperationException("BattleSystem.Tick cannot be called reentrantly.");
            }

            tickInProgress = true;
            try
            {
                TickIndex++;
                SimulatedTimeSeconds += dt;

                ExpireEffects();

                waveScheduler?.Advance(SimulatedTimeSeconds, PublishWaveStarted, SpawnScheduled);

                ApplySeparation();
                CaptureTargetQueryPositions();
                if (simulatePhysics && !Physics2D.Simulate(dt))
                {
                    throw new InvalidOperationException("Physics2D refused the manual simulation step.");
                }

                anchorAttackIntents.Clear();
                int actingUnitCount = units.Count;
                for (int index = 0; index < actingUnitCount; index++)
                {
                    BattleUnit unit = units[index];
                    if (unit.State == BattleUnitState.Dead)
                    {
                        continue;
                    }

                    UpdateTarget(unit);
                    if (!TryGetLivingTarget(unit, out TargetRef target))
                    {
                        unit.State = BattleUnitState.Idle;
                        continue;
                    }

                    Vector2 delta = target.Position - unit.Position;
                    float distance = delta.magnitude;
                    unit.Facing = ResolveFacing(delta, unit.Facing);

                    if (TryAdvanceTrample(unit, target, dt))
                    {
                        continue;
                    }

                    if (unit.Targeting == TargetingMode.Suicide
                        && distance <= unit.Stats.Range)
                    {
                        unit.State = BattleUnitState.Attack;
                        TriggerSuicide(unit, target.EntityId);
                        continue;
                    }

                    if (distance >= unit.Stats.MinRange && distance <= unit.Stats.Range)
                    {
                        unit.State = BattleUnitState.Attack;
                        TryAttack(unit, target);
                    }
                    else if (distance < unit.Stats.MinRange)
                    {
                        // M1 does not introduce retreat behaviour. A minimum-range unit
                        // simply holds until an enemy enters its legal attack annulus.
                        unit.State = BattleUnitState.Idle;
                    }
                    else
                    {
                        unit.State = BattleUnitState.Move;
                        MoveTowardsRange(
                            unit,
                            target.Position,
                            delta,
                            distance,
                            GetTraitAdjustedMoveSpeed(unit),
                            dt);
                    }
                }

                ResolveAnchorAttackIntents();
                ApplySeparation();
                tickSettlementPhase = true;
                TrySettle();
            }
            finally
            {
                tickSettlementPhase = false;
                tickInProgress = false;
            }
        }

        /// <summary>
        /// The sole result-writing exit. A simultaneous destroyed base and boss is a Win.
        /// Sandbox instances never settle.
        /// </summary>
        public BattleResult TrySettle()
        {
            ThrowIfDisposed();
            if (!encounterInitialized
                || IsSettled
                || (tickInProgress && !tickSettlementPhase))
            {
                return Result;
            }

            bool allyBaseDestroyed = allyBase.IsDestroyed;
            bool bossDestroyed = TryGetBoss(out BattleUnit boss) && boss.State == BattleUnitState.Dead;
            BattleResult candidate = bossDestroyed
                ? BattleResult.Win
                : allyBaseDestroyed
                    ? BattleResult.Lose
                    : BattleResult.None;
            if (candidate == BattleResult.None)
            {
                return Result;
            }

            Result = candidate;
            encounterEvents.BattleSettled(new BattleSettledEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                Result,
                allyBase.EntityId,
                BossEntityId,
                allyBase.CurrentHp,
                boss == null ? float.NaN : boss.CurrentHp,
                allyBaseDestroyed,
                bossDestroyed));
            return Result;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (simulatePhysics)
            {
                Physics2D.simulationMode = previousSimulationMode;
            }
        }

        private void InitializeEncounter(LevelDef level)
        {
            Position2Def allyPosition = rules.AllyBasePosition
                                        ?? throw new InvalidOperationException("Ally base position is required.");
            Position2Def enemyPosition = rules.EnemyBasePosition
                                         ?? throw new InvalidOperationException("Enemy base position is required.");

            allyBase = CreateBase(
                config.Bases.Ally,
                BattleTeam.Ally,
                level.BaseHp,
                new Vector2(allyPosition.X, allyPosition.Y));
            enemyBase = CreateBase(
                config.Bases.Enemy,
                BattleTeam.Enemy,
                config.Bases.Enemy.Hp,
                new Vector2(enemyPosition.X, enemyPosition.Y));
            waveScheduler = new WaveScheduler(config, level, rngStreams.Battle);
            Coins = level.StartCoins;
            encounterInitialized = true;
        }

        private BattleBase CreateBase(
            BaseDef definition,
            BattleTeam team,
            float maxHp,
            Vector2 position)
        {
            int entityId = nextEntityId++;
            var value = new BattleBase(
                entityId,
                definition.Id,
                team,
                maxHp,
                definition.Armor,
                position,
                definition.UnitType,
                definition.ArmorType);
            basesById.Add(entityId, value);
            return value;
        }

        private int SpawnUnit(
            UnitDef definition,
            int level,
            Vector2 position,
            EnemyRank rewardRank,
            WaveSpawnContext? waveContext,
            BattleTeam? teamOverride,
            bool awardsCoins = true)
        {
            BattleTeam definitionTeam;
            switch (definition.Faction)
            {
                case UnitFaction.Ally:
                    definitionTeam = BattleTeam.Ally;
                    break;
                case UnitFaction.Enemy:
                    definitionTeam = BattleTeam.Enemy;
                    break;
                default:
                    throw new NotSupportedException($"Cannot spawn faction {definition.Faction} as a UnitDef.");
            }

            BattleTeam team = teamOverride ?? definitionTeam;
            if (definition.Faction == UnitFaction.Enemy && team == BattleTeam.Ally)
            {
                throw new InvalidOperationException(
                    $"Enemy-exclusive unit '{definition.Id}' cannot spawn for the ally team.");
            }

            EnemyRank? awardedRank = team == BattleTeam.Enemy && awardsCoins
                ? rewardRank
                : (EnemyRank?)null;

            BattleStats stats = BuildStats(definition, level);
            int entityId = nextEntityId++;
            Vector2 facing = team == BattleTeam.Ally ? Vector2.up : Vector2.down;
            var unit = new BattleUnit(
                entityId,
                definition.Id,
                team,
                level,
                stats,
                position,
                facing,
                awardedRank,
                BattleTargetKind.Unit,
                definition.Targeting,
                definition.UnitType,
                definition.ArmorType,
                definition.AtkType,
                definition.BonusVs,
                definition.Traits);

            AddUnit(unit, waveContext);
            ApplyAttachedEffects(unit, definition.Effects, EffectTrigger.UnitSpawn);
            ApplyCommanderPassiveToNewAlly(unit);
            return entityId;
        }

        private int SpawnBoss(BossDef definition, int level, Vector2 position, WaveSpawnContext context)
        {
            if (BossEntityId.HasValue)
            {
                throw new InvalidOperationException("An encounter can spawn only one boss.");
            }

            BattleStats stats = BuildBossStats(definition);
            int entityId = nextEntityId++;
            var boss = new BattleUnit(
                entityId,
                definition.Id,
                BattleTeam.Enemy,
                level,
                stats,
                position,
                Vector2.down,
                context.RewardRank,
                BattleTargetKind.Boss,
                TargetingMode.Nearest,
                definition.UnitType,
                definition.ArmorType,
                definition.AtkType,
                definition.BonusVs,
                Array.Empty<UnitTraitDef>());
            BossEntityId = entityId;
            AddUnit(boss, context);
            encounterEvents.BossSpawned(new BossSpawnedEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                entityId,
                definition.Id,
                level,
                stats,
                position,
                context));
            ApplyAttachedEffects(boss, definition.Effects, EffectTrigger.UnitSpawn);
            return entityId;
        }

        private void AddUnit(BattleUnit unit, WaveSpawnContext? waveContext)
        {
            units.Add(unit);
            (unit.Team == BattleTeam.Ally ? aliveAllyUnits : aliveEnemyUnits).Add(unit);
            unitsById.Add(unit.EntityId, unit);
            events.UnitSpawned(new UnitSpawnedEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                unit.EntityId,
                unit.DefinitionId,
                unit.Team,
                unit.Level,
                unit.Stats,
                unit.CurrentHp,
                unit.Position,
                unit.Facing,
                unit.State,
                waveContext));
        }

        private void PublishWaveStarted(ScheduledWaveStart scheduled)
        {
            encounterEvents.WaveStarted(new WaveStartedEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                scheduled.ScheduledTime,
                scheduled.WaveSetId,
                scheduled.WaveIndex,
                scheduled.RewardRank));
        }

        private void SpawnScheduled(ScheduledSpawn scheduled)
        {
            var context = new WaveSpawnContext(
                scheduled.WaveSetId,
                scheduled.WaveIndex,
                scheduled.GroupIndex,
                scheduled.Ordinal,
                scheduled.ScheduledTime,
                scheduled.RewardRank);
            if (config.BossesById.TryGetValue(scheduled.UnitId, out BossDef boss))
            {
                SpawnBoss(boss, scheduled.Level, scheduled.Position, context);
                return;
            }

            UnitDef definition = config.GetUnit(scheduled.UnitId);
            // The fourteen deployable definitions are shared by both sides. Their catalog
            // faction controls public/deployment spawns, while a wave always owns its
            // combatants as Enemy regardless of that default.
            SpawnUnit(
                definition,
                scheduled.Level,
                scheduled.Position,
                scheduled.RewardRank,
                context,
                BattleTeam.Enemy);
        }

        private static Vector2 ResolveFacing(Vector2 delta, Vector2 previous)
        {
            return delta.sqrMagnitude > 0f ? delta.normalized : previous;
        }

        private static BattleStats BuildStats(UnitDef definition, int level)
        {
            return new BattleStats(
                Formula.StatAtLevel(definition.Hp.Base, definition.Hp.Growth, level),
                Formula.StatAtLevel(definition.Atk.Base, definition.Atk.Growth, level),
                Formula.StatAtLevel(definition.Range.Base, definition.Range.Growth, level),
                Formula.StatAtLevel(definition.MinRange.Base, definition.MinRange.Growth, level),
                Formula.StatAtLevel(definition.AtkSpeed.Base, definition.AtkSpeed.Growth, level),
                Formula.StatAtLevel(definition.Cooldown.Base, definition.Cooldown.Growth, level),
                Formula.StatAtLevel(definition.Armor.Base, definition.Armor.Growth, level),
                Formula.StatAtLevel(definition.Pierce.Base, definition.Pierce.Growth, level),
                Formula.StatAtLevel(definition.MoveSpeed.Base, definition.MoveSpeed.Growth, level));
        }

        private static BattleStats BuildBossStats(BossDef definition)
        {
            return new BattleStats(
                definition.Hp,
                definition.Atk,
                definition.Range,
                0f,
                definition.AtkSpeed,
                default,
                definition.Armor,
                definition.Pierce,
                default);
        }

        private void UpdateTarget(BattleUnit unit)
        {
            bool hasLivingTarget = TryGetLivingTarget(unit, out TargetRef currentTarget)
                                   && (currentTarget.Position - unit.Position).sqrMagnitude
                                   >= unit.Stats.MinRange * unit.Stats.MinRange;
            if (hasLivingTarget && SimulatedTimeSeconds < unit.NextRetargetTime)
            {
                return;
            }

            TargetRef selected;
            if (HasPendingTrample(unit))
            {
                selected = FindBacklineTarget(unit);
            }
            else switch (unit.Targeting)
            {
                case TargetingMode.Nearest:
                    selected = unit.IsBoss && unit.UnitType == UnitType.Building
                        ? FindCastleTarget(unit)
                        : FindNearestTarget(unit, true);
                    break;
                case TargetingMode.Backline:
                    selected = FindBacklineTarget(unit);
                    break;
                case TargetingMode.RushBase:
                    selected = FindOpposingBuilding(unit.Team);
                    break;
                case TargetingMode.Suicide:
                    selected = FindNearestTarget(unit, false);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unit '{unit.DefinitionId}' has unsupported targeting mode {unit.Targeting}.");
            }

            unit.TargetEntityId = selected.IsValid ? selected.EntityId : (int?)null;
            unit.NextRetargetTime = RetargetPhaseSchedule.GetNextDeadline(
                SimulatedTimeSeconds,
                unit.EntityId,
                rules.RetargetInterval,
                FixedDeltaTime);
        }

        private TargetRef FindNearestTarget(BattleUnit seeker, bool includeBases)
        {
            return FindBestTarget(seeker, includeBases, TargetSelectionMode.Nearest);
        }

        private TargetRef FindCastleTarget(BattleUnit castle)
        {
            return FindBestTarget(castle, true, TargetSelectionMode.Castle);
        }

        private TargetRef FindBacklineTarget(BattleUnit seeker)
        {
            return FindBestTarget(seeker, true, TargetSelectionMode.Backline);
        }

        private TargetRef FindOpposingBuilding(BattleTeam team)
        {
            if (team == BattleTeam.Ally
                && TryGetBoss(out BattleUnit boss)
                && boss.State != BattleUnitState.Dead
                && boss.UnitType == UnitType.Building)
            {
                return new TargetRef(boss);
            }

            BattleBase target = team == BattleTeam.Ally ? enemyBase : allyBase;
            return target != null && !target.IsDestroyed ? new TargetRef(target) : default;
        }

        private TargetRef FindOpposingBase(BattleTeam team)
        {
            BattleBase target = team == BattleTeam.Ally ? enemyBase : allyBase;
            return target != null && !target.IsDestroyed ? new TargetRef(target) : default;
        }

        private TargetRef FindBestTarget(
            BattleUnit seeker,
            bool includeBases,
            TargetSelectionMode selectionMode)
        {
            TargetRef best = default;
            float bestDistanceSquared = default;

            List<BattleUnit> candidates = seeker.Team == BattleTeam.Ally
                ? aliveEnemyUnits
                : aliveAllyUnits;
            for (int index = 0; index < candidates.Count; index++)
            {
                var candidate = new TargetRef(candidates[index]);
                if (!TryGetTargetDistanceSquared(seeker, candidate, includeBases, out float distanceSquared)
                    || (best.IsValid
                        && !IsBetterTarget(
                            seeker,
                            selectionMode,
                            candidate,
                            distanceSquared,
                            best,
                            bestDistanceSquared)))
                {
                    continue;
                }

                best = candidate;
                bestDistanceSquared = distanceSquared;
            }

            if (includeBases)
            {
                TargetRef candidate = FindOpposingBase(seeker.Team);
                if (candidate.IsValid
                    && TryGetTargetDistanceSquared(seeker, candidate, true, out float distanceSquared)
                    && (!best.IsValid
                        || IsBetterTarget(
                            seeker,
                            selectionMode,
                            candidate,
                            distanceSquared,
                            best,
                            bestDistanceSquared)))
                {
                    best = candidate;
                }
            }

            return best;
        }

        private bool TryGetTargetDistanceSquared(
            BattleUnit seeker,
            TargetRef candidate,
            bool includeBases,
            out float distanceSquared)
        {
            distanceSquared = default;
            if (!candidate.IsValid
                || candidate.EntityId == seeker.EntityId
                || !candidate.IsAlive
                || candidate.Team == seeker.Team
                || (!includeBases && candidate.Kind == BattleTargetKind.Base))
            {
                return false;
            }

            distanceSquared = (candidate.Position - seeker.Position).sqrMagnitude;
            float minimumRangeSquared = seeker.Stats.MinRange * seeker.Stats.MinRange;
            if (distanceSquared < minimumRangeSquared)
            {
                return false;
            }

            // OverlapCircle included a target when its tick-start collider touched the
            // query circle. Keep that snapshot timing while replacing the query itself.
            float searchDistance = rules.TargetSearchRadius + rules.ColliderRadius;
            Vector2 queryPosition = candidate.EntityId < targetQueryPositions.Count
                ? targetQueryPositions[candidate.EntityId]
                : candidate.Position;
            return (queryPosition - seeker.Position).sqrMagnitude <= searchDistance * searchDistance;
        }

        private void CaptureTargetQueryPositions()
        {
            while (targetQueryPositions.Count < nextEntityId)
            {
                targetQueryPositions.Add(default);
            }

            for (int index = 0; index < units.Count; index++)
            {
                BattleUnit unit = units[index];
                targetQueryPositions[unit.EntityId] = unit.Position;
            }

            if (allyBase != null)
            {
                targetQueryPositions[allyBase.EntityId] = allyBase.Position;
            }

            if (enemyBase != null)
            {
                targetQueryPositions[enemyBase.EntityId] = enemyBase.Position;
            }
        }

        private static bool IsBetterTarget(
            BattleUnit seeker,
            TargetSelectionMode selectionMode,
            TargetRef candidate,
            float candidateDistanceSquared,
            TargetRef current,
            float currentDistanceSquared)
        {
            if (selectionMode == TargetSelectionMode.Castle)
            {
                bool candidateIsSiege = candidate.Unit != null
                                        && candidate.AttackType == AttackType.Siege;
                bool currentIsSiege = current.Unit != null
                                      && current.AttackType == AttackType.Siege;
                if (candidateIsSiege != currentIsSiege)
                {
                    return candidateIsSiege;
                }
            }
            else if (selectionMode == TargetSelectionMode.Backline)
            {
                bool candidateIsCombatant = candidate.Kind != BattleTargetKind.Base;
                bool currentIsCombatant = current.Kind != BattleTargetKind.Base;
                if (candidateIsCombatant != currentIsCombatant)
                {
                    return candidateIsCombatant;
                }

                float direction = seeker.Team == BattleTeam.Ally ? 1f : -1f;
                float candidateProgress = candidate.Position.y * direction;
                float currentProgress = current.Position.y * direction;
                int progressOrder = candidateProgress.CompareTo(currentProgress);
                if (progressOrder != 0)
                {
                    return progressOrder > 0;
                }
            }

            int distanceOrder = candidateDistanceSquared.CompareTo(currentDistanceSquared);
            return distanceOrder != 0
                ? distanceOrder < 0
                : candidate.EntityId < current.EntityId;
        }

        private bool TryGetLivingTarget(BattleUnit unit, out TargetRef target)
        {
            target = default;
            return unit.TargetEntityId.HasValue
                   && TryResolveTarget(unit.TargetEntityId.Value, out target)
                   && target.IsAlive;
        }

        private bool TryResolveTarget(int entityId, out TargetRef target)
        {
            if (unitsById.TryGetValue(entityId, out BattleUnit unit))
            {
                target = new TargetRef(unit);
                return true;
            }

            if (basesById.TryGetValue(entityId, out BattleBase value))
            {
                target = new TargetRef(value);
                return true;
            }

            target = default;
            return false;
        }

        private static void MoveTowardsRange(
            BattleUnit unit,
            Vector2 targetPosition,
            Vector2 delta,
            float distance,
            float moveSpeed,
            float dt)
        {
            if (distance <= 0f || moveSpeed <= 0f)
            {
                return;
            }

            float gap = Mathf.Max(0f, distance - unit.Stats.Range);
            float travel = Mathf.Min(moveSpeed * dt, gap);
            unit.Position += delta / distance * travel;
            unit.Facing = ResolveFacing(targetPosition - unit.Position, unit.Facing);
        }

        private void TryAttack(BattleUnit attacker, TargetRef target)
        {
            if (attacker.Stats.AtkSpeed <= 0f
                || SimulatedTimeSeconds + double.Epsilon < attacker.NextAttackTime)
            {
                return;
            }

            long attackId = nextAttackId++;
            attacker.NextAttackTime = SimulatedTimeSeconds + 1d / attacker.Stats.AtkSpeed;
            var intent = new AttackIntent(attackId, attacker, target.EntityId);
            if (target.Kind == BattleTargetKind.Unit)
            {
                ResolveAttack(intent);
            }
            else
            {
                // Settlement anchors resolve after every unit has had its action opportunity.
                // Thus a boss and ally can both land attacks on different anchors in one tick.
                anchorAttackIntents.Add(intent);
            }
        }

        private void ResolveAnchorAttackIntents()
        {
            for (int index = 0; index < anchorAttackIntents.Count; index++)
            {
                ResolveAttack(anchorAttackIntents[index]);
            }
        }

        private void ResolveAttack(AttackIntent intent)
        {
            if (!TryResolveTarget(intent.TargetEntityId, out TargetRef target) || !target.IsAlive)
            {
                return;
            }

            BattleUnit attacker = intent.Attacker;
            events.UnitAttacked(new UnitAttackedEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                intent.AttackId,
                attacker.EntityId,
                target.EntityId,
                attacker.DefinitionId,
                target.DefinitionId,
                attacker.Team,
                target.Team,
                attacker.Position,
                target.Position,
                attacker.Facing));

            float attack = GetTraitAdjustedAttack(attacker);
            if (!TryResolvePiercingShot(intent.AttackId, attacker, target, attack))
            {
                DealDamage(
                    intent.AttackId,
                    attacker.EntityId,
                    target,
                    attack,
                    attacker.Stats.Pierce);
                ApplyAttackAuras(intent.AttackId, attacker, target);
            }
        }

        private void Kill(BattleUnit unit, int killerEntityId)
        {
            if (unit.State == BattleUnitState.Dead)
            {
                return;
            }

            unit.State = BattleUnitState.Dead;
            unit.TargetEntityId = null;
            unit.CurrentHp = 0f;
            (unit.Team == BattleTeam.Ally ? aliveAllyUnits : aliveEnemyUnits).Remove(unit);
            events.UnitDied(new UnitDiedEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                unit.EntityId,
                unit.DefinitionId,
                unit.Team,
                killerEntityId,
                unit.Position,
                unit.CurrentHp,
                unit.Stats.MaxHp,
                unit.RewardRank));

            SpawnDeathChildren(unit);

            if (!unit.RewardRank.HasValue)
            {
                return;
            }

            int amount = Formula.DropCoins(unit.RewardRank.Value, config.Economy);
            DroppedCoins = checked(DroppedCoins + amount);
            Coins = checked(Coins + amount);
            events.CoinDropped(new CoinDroppedEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                unit.EntityId,
                unit.DefinitionId,
                unit.RewardRank.Value,
                amount,
                DroppedCoins,
                unit.Position));
        }

        private void ApplySeparation()
        {
            ClearSeparationBuckets();
            separationUnits.Clear();
            for (int index = 0; index < units.Count; index++)
            {
                BattleUnit unit = units[index];
                if (unit.State != BattleUnitState.Dead
                    && !unit.IsBoss
                    && !HasPendingTrample(unit))
                {
                    separationUnits.Add(unit);
                }
            }

            if (separationUnits.Count < 2)
            {
                return;
            }

            separationUnits.Sort(SeparationOrderComparison);
            float cellSize = Mathf.Max(rules.SeparationDistance, rules.SameColumnTolerance);
            for (int index = 0; index < separationUnits.Count; index++)
            {
                BattleUnit follower = separationUnits[index];
                ResolveSeparationAgainstLeaders(follower, index, cellSize);
                AddToSeparationBucket(follower, cellSize);
            }
        }

        private void ResolveSeparationAgainstLeaders(
            BattleUnit follower,
            int followerIndex,
            float cellSize)
        {
            // Moving away from a leader can expose the follower to a different adjacent
            // bucket. Re-query until stable; the bounded fallback keeps pathological
            // layouts finite while preserving the same progress/EntityId leader order.
            for (int pass = 0; pass < separationUnits.Count; pass++)
            {
                CollectSeparationNeighbors(follower, cellSize);
                if (separationNeighbors.Count == 0)
                {
                    return;
                }

                separationNeighbors.Sort(SeparationOrderComparison);
                bool adjusted = false;
                for (int index = 0; index < separationNeighbors.Count; index++)
                {
                    adjusted |= TrySeparatePair(separationNeighbors[index], follower);
                }

                if (!adjusted)
                {
                    return;
                }
            }

            CollectSeparationNeighbors(follower, cellSize);
            for (int index = 0; index < separationNeighbors.Count; index++)
            {
                if (IsSeparationViolation(separationNeighbors[index], follower))
                {
                    MoveBehindRearLeader(follower, followerIndex);
                    return;
                }
            }
        }

        private void CollectSeparationNeighbors(BattleUnit follower, float cellSize)
        {
            separationNeighbors.Clear();
            int centerX = Mathf.FloorToInt(follower.Position.x / cellSize);
            int centerY = Mathf.FloorToInt(follower.Position.y / cellSize);
            for (int yOffset = -1; yOffset <= 1; yOffset++)
            {
                for (int xOffset = -1; xOffset <= 1; xOffset++)
                {
                    long key = SeparationBucketKey(centerX + xOffset, centerY + yOffset);
                    if (!separationBuckets.TryGetValue(key, out List<BattleUnit> bucket))
                    {
                        continue;
                    }

                    for (int index = 0; index < bucket.Count; index++)
                    {
                        BattleUnit leader = bucket[index];
                        if (leader.Team == follower.Team)
                        {
                            separationNeighbors.Add(leader);
                        }
                    }
                }
            }
        }

        private bool TrySeparatePair(BattleUnit leader, BattleUnit follower)
        {
            if (!IsSeparationViolation(leader, follower))
            {
                return false;
            }

            float direction = follower.Team == BattleTeam.Ally ? 1f : -1f;
            float xGap = Mathf.Abs(leader.Position.x - follower.Position.x);
            if (xGap <= rules.SameColumnTolerance)
            {
                // Preserve the accepted same-column contract: X stays fixed and only
                // the follower is moved backward along battle progress.
                follower.Position = new Vector2(
                    follower.Position.x,
                    leader.Position.y - direction * rules.SeparationDistance);
                return true;
            }

            Vector2 offset = follower.Position - leader.Position;
            float distance = offset.magnitude;
            if (distance <= 0f)
            {
                follower.Position = new Vector2(
                    follower.Position.x,
                    leader.Position.y - direction * rules.SeparationDistance);
                return true;
            }

            follower.Position = leader.Position + offset / distance * rules.SeparationDistance;
            return true;
        }

        private bool IsSeparationViolation(BattleUnit leader, BattleUnit follower)
        {
            float xGap = Mathf.Abs(leader.Position.x - follower.Position.x);
            if (xGap <= rules.SameColumnTolerance)
            {
                float yGap = Mathf.Abs(leader.Position.y - follower.Position.y);
                return yGap < rules.SeparationDistance
                       && !Mathf.Approximately(yGap, rules.SeparationDistance);
            }

            float distanceSquared = (leader.Position - follower.Position).sqrMagnitude;
            float separationSquared = rules.SeparationDistance * rules.SeparationDistance;
            return distanceSquared < separationSquared
                   && !Mathf.Approximately(distanceSquared, separationSquared);
        }

        private void MoveBehindRearLeader(BattleUnit follower, int followerIndex)
        {
            BattleUnit rearLeader = null;
            float direction = follower.Team == BattleTeam.Ally ? 1f : -1f;
            float rearProgress = float.PositiveInfinity;
            for (int index = 0; index < followerIndex; index++)
            {
                BattleUnit candidate = separationUnits[index];
                if (candidate.Team != follower.Team)
                {
                    continue;
                }

                float progress = candidate.Position.y * direction;
                if (progress < rearProgress)
                {
                    rearProgress = progress;
                    rearLeader = candidate;
                }
            }

            if (rearLeader != null)
            {
                follower.Position = new Vector2(
                    follower.Position.x,
                    rearLeader.Position.y - direction * rules.SeparationDistance);
            }
        }

        private void AddToSeparationBucket(BattleUnit unit, float cellSize)
        {
            int cellX = Mathf.FloorToInt(unit.Position.x / cellSize);
            int cellY = Mathf.FloorToInt(unit.Position.y / cellSize);
            long key = SeparationBucketKey(cellX, cellY);
            if (!separationBuckets.TryGetValue(key, out List<BattleUnit> bucket))
            {
                bucket = new List<BattleUnit>();
                separationBuckets.Add(key, bucket);
            }

            if (bucket.Count == 0)
            {
                activeSeparationBuckets.Add(bucket);
            }

            bucket.Add(unit);
        }

        private void ClearSeparationBuckets()
        {
            for (int index = 0; index < activeSeparationBuckets.Count; index++)
            {
                activeSeparationBuckets[index].Clear();
            }

            activeSeparationBuckets.Clear();
        }

        private static int CompareSeparationOrder(BattleUnit left, BattleUnit right)
        {
            int teamOrder = left.Team.CompareTo(right.Team);
            if (teamOrder != 0)
            {
                return teamOrder;
            }

            float direction = left.Team == BattleTeam.Ally ? 1f : -1f;
            float leftProgress = left.Position.y * direction;
            float rightProgress = right.Position.y * direction;
            int progressOrder = rightProgress.CompareTo(leftProgress);
            return progressOrder != 0
                ? progressOrder
                : left.EntityId.CompareTo(right.EntityId);
        }

        private static long SeparationBucketKey(int x, int y)
        {
            return unchecked(((long)x << 32) | (uint)y);
        }

        private bool TryGetBoss(out BattleUnit boss)
        {
            boss = null;
            return BossEntityId.HasValue && unitsById.TryGetValue(BossEntityId.Value, out boss);
        }

        private long NextEventSequence()
        {
            return nextEventSequence++;
        }

        private void RequireEncounter()
        {
            if (!encounterInitialized)
            {
                throw new InvalidOperationException("This operation requires a full battle encounter.");
            }
        }

        private void ThrowIfSettled()
        {
            if (IsSettled)
            {
                throw new InvalidOperationException("The battle is settled and frozen.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(BattleSystem));
            }
        }

        private enum TargetSelectionMode
        {
            Nearest,
            Backline,
            Castle
        }

        private readonly struct AttackIntent
        {
            internal AttackIntent(long attackId, BattleUnit attacker, int targetEntityId)
            {
                AttackId = attackId;
                Attacker = attacker;
                TargetEntityId = targetEntityId;
            }

            internal long AttackId { get; }

            internal BattleUnit Attacker { get; }

            internal int TargetEntityId { get; }
        }

        private readonly struct TargetRef
        {
            internal TargetRef(BattleUnit unit)
            {
                Unit = unit;
                Base = null;
            }

            internal TargetRef(BattleBase value)
            {
                Unit = null;
                Base = value;
            }

            internal BattleUnit Unit { get; }

            internal BattleBase Base { get; }

            internal bool IsValid => Unit != null || Base != null;

            internal int EntityId => Unit != null ? Unit.EntityId : Base.EntityId;

            internal string DefinitionId => Unit != null ? Unit.DefinitionId : Base.DefinitionId;

            internal BattleTeam Team => Unit != null ? Unit.Team : Base.Team;

            internal BattleTargetKind Kind => Unit != null ? Unit.TargetKind : BattleTargetKind.Base;

            internal Vector2 Position => Unit != null ? Unit.Position : Base.Position;

            internal float Armor => Unit != null ? Unit.Stats.Armor : Base.Armor;

            internal UnitType UnitType => Unit != null ? Unit.UnitType : Base.UnitType;

            internal ArmorType ArmorType => Unit != null ? Unit.ArmorType : Base.ArmorType;

            internal AttackType AttackType => Unit != null ? Unit.AttackType : AttackType.None;

            internal float MaxHp => Unit != null ? Unit.Stats.MaxHp : Base.MaxHp;

            internal float CurrentHp => Unit != null ? Unit.CurrentHp : Base.CurrentHp;

            internal bool IsAlive => Unit != null
                ? Unit.State != BattleUnitState.Dead
                : Base != null && !Base.IsDestroyed;

            internal void SetCurrentHp(float value)
            {
                if (Unit != null)
                {
                    Unit.CurrentHp = value;
                }
                else
                {
                    Base.CurrentHp = value;
                }
            }
        }
    }
}
