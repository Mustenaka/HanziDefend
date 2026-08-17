using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    public readonly struct WaveSpawnContext
    {
        public WaveSpawnContext(
            string waveSetId,
            int waveIndex,
            int groupIndex,
            int ordinal,
            double scheduledTimeSeconds,
            EnemyRank rewardRank)
        {
            WaveSetId = waveSetId;
            WaveIndex = waveIndex;
            GroupIndex = groupIndex;
            Ordinal = ordinal;
            ScheduledTimeSeconds = scheduledTimeSeconds;
            RewardRank = rewardRank;
        }

        public string WaveSetId { get; }
        public int WaveIndex { get; }
        public int GroupIndex { get; }
        public int Ordinal { get; }
        public double ScheduledTimeSeconds { get; }
        public EnemyRank RewardRank { get; }
    }

    public readonly struct UnitSpawnedEvent
    {
        public UnitSpawnedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            int entityId,
            string definitionId,
            BattleTeam team,
            int level,
            BattleStats stats,
            float currentHp,
            Vector2 position,
            Vector2 facing,
            BattleUnitState state)
            : this(
                sequence,
                tickIndex,
                simulatedTimeSeconds,
                entityId,
                definitionId,
                team,
                level,
                stats,
                currentHp,
                position,
                facing,
                state,
                null)
        {
        }

        public UnitSpawnedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            int entityId,
            string definitionId,
            BattleTeam team,
            int level,
            BattleStats stats,
            float currentHp,
            Vector2 position,
            Vector2 facing,
            BattleUnitState state,
            WaveSpawnContext? waveContext)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            EntityId = entityId;
            DefinitionId = definitionId;
            Team = team;
            Level = level;
            Stats = stats;
            CurrentHp = currentHp;
            Position = position;
            Facing = facing;
            State = state;
            WaveContext = waveContext;
        }

        public long Sequence { get; }

        public long TickIndex { get; }

        public double SimulatedTimeSeconds { get; }

        public int EntityId { get; }

        public string DefinitionId { get; }

        public BattleTeam Team { get; }

        public int Level { get; }

        public BattleStats Stats { get; }

        public float CurrentHp { get; }

        public Vector2 Position { get; }

        public Vector2 Facing { get; }

        public BattleUnitState State { get; }

        public WaveSpawnContext? WaveContext { get; }
    }

    public readonly struct UnitAttackedEvent
    {
        public UnitAttackedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            long attackId,
            int attackerEntityId,
            int targetEntityId,
            string attackerDefinitionId,
            string targetDefinitionId,
            BattleTeam attackerTeam,
            BattleTeam targetTeam,
            Vector2 attackerPosition,
            Vector2 targetPosition,
            Vector2 facing)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            AttackId = attackId;
            AttackerEntityId = attackerEntityId;
            TargetEntityId = targetEntityId;
            AttackerDefinitionId = attackerDefinitionId;
            TargetDefinitionId = targetDefinitionId;
            AttackerTeam = attackerTeam;
            TargetTeam = targetTeam;
            AttackerPosition = attackerPosition;
            TargetPosition = targetPosition;
            Facing = facing;
        }

        public long Sequence { get; }

        public long TickIndex { get; }

        public double SimulatedTimeSeconds { get; }

        public long AttackId { get; }

        public int AttackerEntityId { get; }

        public int TargetEntityId { get; }

        public string AttackerDefinitionId { get; }

        public string TargetDefinitionId { get; }

        public BattleTeam AttackerTeam { get; }

        public BattleTeam TargetTeam { get; }

        public Vector2 AttackerPosition { get; }

        public Vector2 TargetPosition { get; }

        public Vector2 Facing { get; }

        public Vector2 AttackerFacing => Facing;
    }

    public readonly struct DamageDealtEvent
    {
        public DamageDealtEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            long attackId,
            int sourceEntityId,
            int targetEntityId,
            int amount,
            float hpBefore,
            float hpAfter,
            float maxHp,
            Vector2 hitPosition,
            bool isLethal)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            AttackId = attackId;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            Amount = amount;
            HpBefore = hpBefore;
            HpAfter = hpAfter;
            MaxHp = maxHp;
            HitPosition = hitPosition;
            IsLethal = isLethal;
        }

        public long Sequence { get; }

        public long TickIndex { get; }

        public double SimulatedTimeSeconds { get; }

        public long AttackId { get; }

        public int SourceEntityId { get; }

        public int TargetEntityId { get; }

        public int Amount { get; }

        public float HpBefore { get; }

        public float HpAfter { get; }

        public float MaxHp { get; }

        public Vector2 HitPosition { get; }

        public bool IsLethal { get; }
    }

    public readonly struct UnitDiedEvent
    {
        public UnitDiedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            int entityId,
            string definitionId,
            BattleTeam team,
            int killerEntityId,
            Vector2 position,
            float finalHp,
            float maxHp,
            EnemyRank? rewardRank)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            EntityId = entityId;
            DefinitionId = definitionId;
            Team = team;
            KillerEntityId = killerEntityId;
            Position = position;
            FinalHp = finalHp;
            MaxHp = maxHp;
            RewardRank = rewardRank;
        }

        public long Sequence { get; }

        public long TickIndex { get; }

        public double SimulatedTimeSeconds { get; }

        public int EntityId { get; }

        public string DefinitionId { get; }

        public BattleTeam Team { get; }

        public int KillerEntityId { get; }

        public Vector2 Position { get; }

        public float FinalHp { get; }

        public float MaxHp { get; }

        public EnemyRank? RewardRank { get; }
    }

    public readonly struct CoinDroppedEvent
    {
        public CoinDroppedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            int sourceEntityId,
            string sourceDefinitionId,
            EnemyRank rank,
            int amount,
            int totalCoins,
            Vector2 position)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            SourceEntityId = sourceEntityId;
            SourceDefinitionId = sourceDefinitionId;
            Rank = rank;
            Amount = amount;
            TotalCoins = totalCoins;
            Position = position;
        }

        public long Sequence { get; }

        public long TickIndex { get; }

        public double SimulatedTimeSeconds { get; }

        public int SourceEntityId { get; }

        public string SourceDefinitionId { get; }

        public EnemyRank Rank { get; }

        public int Amount { get; }

        public int TotalCoins { get; }

        public Vector2 Position { get; }
    }

    public readonly struct WaveStartedEvent
    {
        public WaveStartedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            double scheduledTimeSeconds,
            string waveSetId,
            int waveIndex,
            EnemyRank rewardRank)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            ScheduledTimeSeconds = scheduledTimeSeconds;
            WaveSetId = waveSetId;
            WaveIndex = waveIndex;
            RewardRank = rewardRank;
        }

        public long Sequence { get; }
        public long TickIndex { get; }
        public double SimulatedTimeSeconds { get; }
        public double ScheduledTimeSeconds { get; }
        public string WaveSetId { get; }
        public int WaveIndex { get; }
        public EnemyRank RewardRank { get; }
    }

    public readonly struct BossSpawnedEvent
    {
        public BossSpawnedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            int entityId,
            string definitionId,
            int level,
            BattleStats stats,
            Vector2 position,
            WaveSpawnContext waveContext)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            EntityId = entityId;
            DefinitionId = definitionId;
            Level = level;
            Stats = stats;
            Position = position;
            WaveContext = waveContext;
        }

        public long Sequence { get; }
        public long TickIndex { get; }
        public double SimulatedTimeSeconds { get; }
        public int EntityId { get; }
        public string DefinitionId { get; }
        public int Level { get; }
        public BattleStats Stats { get; }
        public Vector2 Position { get; }
        public WaveSpawnContext WaveContext { get; }
    }

    public readonly struct BaseDamagedEvent
    {
        public BaseDamagedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            long attackId,
            int sourceEntityId,
            int baseEntityId,
            BattleTeam baseTeam,
            int amount,
            float hpBefore,
            float hpAfter,
            float maxHp,
            Vector2 position)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            AttackId = attackId;
            SourceEntityId = sourceEntityId;
            BaseEntityId = baseEntityId;
            BaseTeam = baseTeam;
            Amount = amount;
            HpBefore = hpBefore;
            HpAfter = hpAfter;
            MaxHp = maxHp;
            Position = position;
        }

        public long Sequence { get; }
        public long TickIndex { get; }
        public double SimulatedTimeSeconds { get; }
        public long AttackId { get; }
        public int SourceEntityId { get; }
        public int BaseEntityId { get; }
        public BattleTeam BaseTeam { get; }
        public int Amount { get; }
        public float HpBefore { get; }
        public float HpAfter { get; }
        public float MaxHp { get; }
        public Vector2 Position { get; }
        public bool IsDestroyed => HpAfter <= 0f;
    }

    public readonly struct BattleSettledEvent
    {
        public BattleSettledEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            BattleResult result,
            int allyBaseEntityId,
            int? bossEntityId,
            float allyBaseHp,
            float bossHp,
            bool allyBaseDestroyed,
            bool bossDestroyed)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            Result = result;
            AllyBaseEntityId = allyBaseEntityId;
            BossEntityId = bossEntityId;
            AllyBaseHp = allyBaseHp;
            BossHp = bossHp;
            AllyBaseDestroyed = allyBaseDestroyed;
            BossDestroyed = bossDestroyed;
        }

        public long Sequence { get; }
        public long TickIndex { get; }
        public double SimulatedTimeSeconds { get; }
        public BattleResult Result { get; }
        public int AllyBaseEntityId { get; }
        public int? BossEntityId { get; }
        public float AllyBaseHp { get; }
        public float BossHp { get; }
        public bool AllyBaseDestroyed { get; }
        public bool BossDestroyed { get; }
    }

    public interface IBattleEvents
    {
        void UnitSpawned(UnitSpawnedEvent eventData);

        void UnitAttacked(UnitAttackedEvent eventData);

        void DamageDealt(DamageDealtEvent eventData);

        void UnitDied(UnitDiedEvent eventData);

        void CoinDropped(CoinDroppedEvent eventData);
    }

    public interface IBattleEncounterEvents : IBattleEvents
    {
        void WaveStarted(WaveStartedEvent eventData);

        void BossSpawned(BossSpawnedEvent eventData);

        void BaseDamaged(BaseDamagedEvent eventData);

        void BattleSettled(BattleSettledEvent eventData);
    }

    /// <summary>
    /// A deployed ally that has not entered the field yet (WO-F1 §B). The grid cell is paid for and
    /// committed at t=0, but a Delayed unit only walks on after its own <c>cooldown</c> seconds, so
    /// something has to tell the player "this one is still coming" rather than "this one failed".
    /// </summary>
    public readonly struct AllyDeploymentQueuedEvent
    {
        public AllyDeploymentQueuedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            int ticket,
            string definitionId,
            int level,
            Vector2 position,
            float delaySeconds,
            double entryTimeSeconds)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            Ticket = ticket;
            DefinitionId = definitionId;
            Level = level;
            Position = position;
            DelaySeconds = delaySeconds;
            EntryTimeSeconds = entryTimeSeconds;
        }

        public long Sequence { get; }
        public long TickIndex { get; }
        public double SimulatedTimeSeconds { get; }

        /// <summary>Run-local id of the pending slot; pairs a queued event with its entered event.</summary>
        public int Ticket { get; }

        public string DefinitionId { get; }
        public int Level { get; }
        public Vector2 Position { get; }
        public float DelaySeconds { get; }
        public double EntryTimeSeconds { get; }
    }

    /// <summary>A queued ally has walked on; the placeholder raised by its queued event is done.</summary>
    public readonly struct AllyDeploymentEnteredEvent
    {
        public AllyDeploymentEnteredEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            int ticket,
            int entityId,
            string definitionId,
            int level,
            Vector2 position,
            double scheduledEntryTimeSeconds)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            Ticket = ticket;
            EntityId = entityId;
            DefinitionId = definitionId;
            Level = level;
            Position = position;
            ScheduledEntryTimeSeconds = scheduledEntryTimeSeconds;
        }

        public long Sequence { get; }
        public long TickIndex { get; }
        public double SimulatedTimeSeconds { get; }
        public int Ticket { get; }
        public int EntityId { get; }
        public string DefinitionId { get; }
        public int Level { get; }
        public Vector2 Position { get; }
        public double ScheduledEntryTimeSeconds { get; }
    }

    /// <summary>
    /// Optional companion to <see cref="IBattleEvents"/>, discovered by cast the same way
    /// <c>IBattleEffectEvents</c> is. Listeners that do not care about entry timing need no change.
    /// </summary>
    public interface IBattleDeploymentEvents
    {
        void AllyDeploymentQueued(AllyDeploymentQueuedEvent eventData);

        void AllyDeploymentEntered(AllyDeploymentEnteredEvent eventData);
    }

    public sealed class NullBattleDeploymentEvents : IBattleDeploymentEvents
    {
        public static NullBattleDeploymentEvents Instance { get; } = new NullBattleDeploymentEvents();

        public void AllyDeploymentQueued(AllyDeploymentQueuedEvent eventData)
        {
        }

        public void AllyDeploymentEntered(AllyDeploymentEnteredEvent eventData)
        {
        }
    }

    public sealed class NullBattleEvents : IBattleEncounterEvents
    {
        public static NullBattleEvents Instance { get; } = new NullBattleEvents();

        public void UnitSpawned(UnitSpawnedEvent eventData)
        {
        }

        public void UnitAttacked(UnitAttackedEvent eventData)
        {
        }

        public void DamageDealt(DamageDealtEvent eventData)
        {
        }

        public void UnitDied(UnitDiedEvent eventData)
        {
        }

        public void CoinDropped(CoinDroppedEvent eventData)
        {
        }

        public void WaveStarted(WaveStartedEvent eventData)
        {
        }

        public void BossSpawned(BossSpawnedEvent eventData)
        {
        }

        public void BaseDamaged(BaseDamagedEvent eventData)
        {
        }

        public void BattleSettled(BattleSettledEvent eventData)
        {
        }
    }
}
