using System;
using System.Collections.Generic;
using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    /// <summary>
    /// Deterministic, manually-driven battle kernel. The public constructor creates the
    /// WO-B1 sandbox; CreateEncounter adds bases, the configured timeline and settlement.
    /// </summary>
    public sealed partial class BattleSystem : IDisposable
    {
        private const string AllyBaseDefinitionId = "base_ally";
        private const string EnemyBaseDefinitionId = "base_enemy";

        private readonly GameConfig config;
        private readonly BattleRulesDef rules;
        private readonly IBattleEvents events;
        private readonly IBattleEncounterEvents encounterEvents;
        private readonly IBattleEffectEvents effectEvents;
        private readonly RngStreams rngStreams;
        private readonly List<BattleUnit> units = new List<BattleUnit>();
        private readonly Dictionary<int, BattleUnit> unitsById = new Dictionary<int, BattleUnit>();
        private readonly Dictionary<int, BattleBase> basesById = new Dictionary<int, BattleBase>();
        private readonly List<Collider2D> queryResults = new List<Collider2D>();
        private readonly List<TargetRef> targetCandidates = new List<TargetRef>();
        private readonly HashSet<int> candidateEntityIds = new HashSet<int>();
        private readonly List<AttackIntent> anchorAttackIntents = new List<AttackIntent>();
        private readonly ContactFilter2D queryFilter;
        private readonly SimulationMode2D previousSimulationMode;
        private readonly GameObject physicsRoot;

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

        public BattleSystem(GameConfig config, uint seed, IBattleEvents battleEvents = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            rules = config.Economy?.Battle
                    ?? throw new ArgumentException("Battle rules are required.", nameof(config));
            events = battleEvents ?? NullBattleEvents.Instance;
            encounterEvents = battleEvents as IBattleEncounterEvents ?? NullBattleEvents.Instance;
            effectEvents = battleEvents as IBattleEffectEvents ?? NullBattleEffectEvents.Instance;
            rngStreams = new RngStreams(seed);
            queryFilter = ContactFilter2D.noFilter;
            queryFilter.useTriggers = true;

            previousSimulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            physicsRoot = new GameObject("HanziDefend Battle Physics");
            physicsRoot.hideFlags = HideFlags.HideAndDontSave;
        }

        public static BattleSystem CreateEncounter(
            GameConfig config,
            string levelId,
            uint seed,
            IBattleEvents battleEvents = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            LevelDef level = config.GetLevel(levelId);
            var system = new BattleSystem(config, seed, battleEvents);
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
            return SpawnUnit(definition, request.Level, request.Position, request.RewardRank, null);
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
                SynchronizeBodies();
                Physics2D.SyncTransforms();
                if (!Physics2D.Simulate(dt))
                {
                    throw new InvalidOperationException("Physics2D refused the manual simulation step.");
                }

                anchorAttackIntents.Clear();
                for (int index = 0; index < units.Count; index++)
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

                    if (unit.Targeting == TargetingMode.Suicide
                        && distance <= unit.Stats.Range)
                    {
                        unit.State = BattleUnitState.Attack;
                        TriggerSuicide(unit, target.EntityId);
                        continue;
                    }

                    if (distance <= unit.Stats.Range)
                    {
                        unit.State = BattleUnitState.Attack;
                        TryAttack(unit, target);
                    }
                    else
                    {
                        unit.State = BattleUnitState.Move;
                        MoveTowardsRange(unit, target.Position, delta, distance, dt);
                    }
                }

                ResolveAnchorAttackIntents();
                ApplySeparation();
                SynchronizeBodies();
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
            for (int index = 0; index < units.Count; index++)
            {
                DisablePhysics(units[index].Collider, units[index].Rigidbody);
            }

            foreach (BattleBase value in basesById.Values)
            {
                DisablePhysics(value.Collider, value.Rigidbody);
            }

            if (physicsRoot != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(physicsRoot);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(physicsRoot);
                }
            }

            Physics2D.simulationMode = previousSimulationMode;
        }

        private static void DisablePhysics(Collider2D collider, Rigidbody2D rigidbody)
        {
            if (collider != null)
            {
                collider.enabled = false;
            }

            if (rigidbody != null)
            {
                rigidbody.simulated = false;
            }
        }

        private void InitializeEncounter(LevelDef level)
        {
            Position2Def allyPosition = rules.AllyBasePosition
                                        ?? throw new InvalidOperationException("Ally base position is required.");
            Position2Def enemyPosition = rules.EnemyBasePosition
                                         ?? throw new InvalidOperationException("Enemy base position is required.");

            allyBase = CreateBase(
                BattleTeam.Ally,
                level.BaseHp,
                config.Bases.Ally.Armor,
                new Vector2(allyPosition.X, allyPosition.Y));
            enemyBase = CreateBase(
                BattleTeam.Enemy,
                config.Bases.Enemy.Hp,
                config.Bases.Enemy.Armor,
                new Vector2(enemyPosition.X, enemyPosition.Y));
            waveScheduler = new WaveScheduler(config, level, rngStreams.Battle);
            Coins = level.StartCoins;
            encounterInitialized = true;
        }

        private BattleBase CreateBase(
            BattleTeam team,
            float maxHp,
            float armor,
            Vector2 position)
        {
            int entityId = nextEntityId++;
            var value = new BattleBase(entityId, team, maxHp, armor, position);
            CreatePhysicsBody(value);
            basesById.Add(entityId, value);
            return value;
        }

        private int SpawnUnit(
            UnitDef definition,
            int level,
            Vector2 position,
            EnemyRank rewardRank,
            WaveSpawnContext? waveContext)
        {
            BattleTeam team;
            EnemyRank? awardedRank;
            switch (definition.Faction)
            {
                case UnitFaction.Ally:
                    team = BattleTeam.Ally;
                    awardedRank = null;
                    break;
                case UnitFaction.Enemy:
                    team = BattleTeam.Enemy;
                    awardedRank = rewardRank;
                    break;
                default:
                    throw new NotSupportedException($"Cannot spawn faction {definition.Faction} as a UnitDef.");
            }

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
                definition.Targeting);

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
                TargetingMode.Nearest);
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
            CreatePhysicsBody(unit);
            units.Add(unit);
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
            if (definition.Faction != UnitFaction.Enemy)
            {
                throw new InvalidOperationException(
                    $"Wave {scheduled.WaveIndex} cannot spawn non-enemy unit '{definition.Id}'.");
            }

            SpawnUnit(definition, scheduled.Level, scheduled.Position, scheduled.RewardRank, context);
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
                definition.AtkSpeed,
                default,
                definition.Armor,
                definition.Pierce,
                default);
        }

        private void CreatePhysicsBody(BattleUnit unit)
        {
            var gameObject = new GameObject($"BattleUnit {unit.EntityId} {unit.DefinitionId}");
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            gameObject.transform.SetParent(physicsRoot.transform, false);
            gameObject.transform.position = unit.Position;

            var body = gameObject.AddComponent<BattleTargetBody>();
            body.Owner = this;
            body.EntityId = unit.EntityId;
            body.Kind = unit.TargetKind;
            var rigidbody = gameObject.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Kinematic;
            rigidbody.gravityScale = 0f;
            rigidbody.freezeRotation = true;
            rigidbody.position = unit.Position;
            var collider = gameObject.AddComponent<CircleCollider2D>();
            collider.radius = rules.ColliderRadius;
            collider.isTrigger = true;

            unit.Body = body;
            unit.Rigidbody = rigidbody;
            unit.Collider = collider;
        }

        private void CreatePhysicsBody(BattleBase value)
        {
            string definitionId = BaseDefinitionId(value.Team);
            var gameObject = new GameObject($"BattleBase {value.EntityId} {definitionId}");
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            gameObject.transform.SetParent(physicsRoot.transform, false);
            gameObject.transform.position = value.Position;

            var body = gameObject.AddComponent<BattleTargetBody>();
            body.Owner = this;
            body.EntityId = value.EntityId;
            body.Kind = BattleTargetKind.Base;
            var rigidbody = gameObject.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Kinematic;
            rigidbody.gravityScale = 0f;
            rigidbody.freezeRotation = true;
            rigidbody.position = value.Position;
            var collider = gameObject.AddComponent<CircleCollider2D>();
            collider.radius = rules.ColliderRadius;
            collider.isTrigger = true;

            value.Body = body;
            value.Rigidbody = rigidbody;
            value.Collider = collider;
        }

        private void UpdateTarget(BattleUnit unit)
        {
            bool hasLivingTarget = TryGetLivingTarget(unit, out _);
            if (hasLivingTarget && SimulatedTimeSeconds < unit.NextRetargetTime)
            {
                return;
            }

            TargetRef selected;
            switch (unit.Targeting)
            {
                case TargetingMode.Nearest:
                    selected = FindNearestTarget(unit, true);
                    break;
                case TargetingMode.Backline:
                    selected = FindBacklineTarget(unit);
                    break;
                case TargetingMode.RushBase:
                    selected = FindOpposingBase(unit.Team);
                    break;
                case TargetingMode.Suicide:
                    selected = FindNearestTarget(unit, false);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unit '{unit.DefinitionId}' has unsupported targeting mode {unit.Targeting}.");
            }

            unit.TargetEntityId = selected.IsValid ? selected.EntityId : (int?)null;
            unit.NextRetargetTime = SimulatedTimeSeconds + rules.RetargetInterval;
        }

        private TargetRef FindNearestTarget(BattleUnit seeker, bool includeBases)
        {
            CollectTargetCandidates(seeker, includeBases);
            targetCandidates.Sort((left, right) =>
            {
                float leftDistance = (left.Position - seeker.Position).sqrMagnitude;
                float rightDistance = (right.Position - seeker.Position).sqrMagnitude;
                int distanceOrder = leftDistance.CompareTo(rightDistance);
                return distanceOrder != 0 ? distanceOrder : left.EntityId.CompareTo(right.EntityId);
            });

            return targetCandidates.Count == 0 ? default : targetCandidates[0];
        }

        private TargetRef FindBacklineTarget(BattleUnit seeker)
        {
            CollectTargetCandidates(seeker, true);
            bool hasCombatant = false;
            for (int index = 0; index < targetCandidates.Count; index++)
            {
                if (targetCandidates[index].Kind != BattleTargetKind.Base)
                {
                    hasCombatant = true;
                    break;
                }
            }

            if (hasCombatant)
            {
                targetCandidates.RemoveAll(value => value.Kind == BattleTargetKind.Base);
            }

            float direction = seeker.Team == BattleTeam.Ally ? 1f : -1f;
            targetCandidates.Sort((left, right) =>
            {
                float leftRow = left.Position.y * direction;
                float rightRow = right.Position.y * direction;
                int rowOrder = rightRow.CompareTo(leftRow);
                if (rowOrder != 0)
                {
                    return rowOrder;
                }

                float leftDistance = (left.Position - seeker.Position).sqrMagnitude;
                float rightDistance = (right.Position - seeker.Position).sqrMagnitude;
                int distanceOrder = leftDistance.CompareTo(rightDistance);
                return distanceOrder != 0 ? distanceOrder : left.EntityId.CompareTo(right.EntityId);
            });

            return targetCandidates.Count == 0 ? default : targetCandidates[0];
        }

        private TargetRef FindOpposingBase(BattleTeam team)
        {
            BattleBase target = team == BattleTeam.Ally ? enemyBase : allyBase;
            return target != null && !target.IsDestroyed ? new TargetRef(target) : default;
        }

        private void CollectTargetCandidates(BattleUnit seeker, bool includeBases)
        {
            queryResults.Clear();
            Physics2D.OverlapCircle(seeker.Position, rules.TargetSearchRadius, queryFilter, queryResults);
            targetCandidates.Clear();
            candidateEntityIds.Clear();

            for (int index = 0; index < queryResults.Count; index++)
            {
                Collider2D collider = queryResults[index];
                if (collider == null
                    || !collider.TryGetComponent(out BattleTargetBody body)
                    || !ReferenceEquals(body.Owner, this)
                    || body.EntityId == seeker.EntityId
                    || !candidateEntityIds.Add(body.EntityId)
                    || !TryResolveTarget(body.EntityId, out TargetRef candidate)
                    || !candidate.IsAlive
                    || candidate.Team == seeker.Team
                    || (!includeBases && candidate.Kind == BattleTargetKind.Base))
                {
                    continue;
                }

                targetCandidates.Add(candidate);
            }
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
            float dt)
        {
            if (distance <= 0f || unit.Stats.MoveSpeed <= 0f)
            {
                return;
            }

            float gap = Mathf.Max(0f, distance - unit.Stats.Range);
            float travel = Mathf.Min(unit.Stats.MoveSpeed * dt, gap);
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

            DealDamage(
                intent.AttackId,
                attacker.EntityId,
                target,
                attacker.Stats.Atk,
                attacker.Stats.Pierce);
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
            DisablePhysics(unit.Collider, unit.Rigidbody);

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
            for (int firstIndex = 0; firstIndex < units.Count; firstIndex++)
            {
                BattleUnit first = units[firstIndex];
                if (first.State == BattleUnitState.Dead || first.IsBoss)
                {
                    continue;
                }

                for (int secondIndex = firstIndex + 1; secondIndex < units.Count; secondIndex++)
                {
                    BattleUnit second = units[secondIndex];
                    if (second.State == BattleUnitState.Dead
                        || second.IsBoss
                        || first.Team != second.Team
                        || Mathf.Abs(first.Position.x - second.Position.x) > rules.SameColumnTolerance)
                    {
                        continue;
                    }

                    float yGap = Mathf.Abs(first.Position.y - second.Position.y);
                    if (yGap >= rules.SeparationDistance)
                    {
                        continue;
                    }

                    BattleUnit leader;
                    BattleUnit follower;
                    float direction = first.Team == BattleTeam.Ally ? 1f : -1f;
                    float firstProgress = first.Position.y * direction;
                    float secondProgress = second.Position.y * direction;
                    if (firstProgress > secondProgress
                        || (firstProgress == secondProgress && first.EntityId < second.EntityId))
                    {
                        leader = first;
                        follower = second;
                    }
                    else
                    {
                        leader = second;
                        follower = first;
                    }

                    follower.Position = new Vector2(
                        follower.Position.x,
                        leader.Position.y - direction * rules.SeparationDistance);
                }
            }
        }

        private void SynchronizeBodies()
        {
            for (int index = 0; index < units.Count; index++)
            {
                BattleUnit unit = units[index];
                if (unit.Rigidbody != null && unit.Rigidbody.simulated)
                {
                    unit.Rigidbody.position = unit.Position;
                    unit.Body.transform.position = unit.Position;
                }
            }
        }

        private bool TryGetBoss(out BattleUnit boss)
        {
            boss = null;
            return BossEntityId.HasValue && unitsById.TryGetValue(BossEntityId.Value, out boss);
        }

        private static string BaseDefinitionId(BattleTeam team)
        {
            return team == BattleTeam.Ally ? AllyBaseDefinitionId : EnemyBaseDefinitionId;
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

            internal string DefinitionId => Unit != null ? Unit.DefinitionId : BaseDefinitionId(Base.Team);

            internal BattleTeam Team => Unit != null ? Unit.Team : Base.Team;

            internal BattleTargetKind Kind => Unit != null ? Unit.TargetKind : BattleTargetKind.Base;

            internal Vector2 Position => Unit != null ? Unit.Position : Base.Position;

            internal float Armor => Unit != null ? Unit.Stats.Armor : Base.Armor;

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
