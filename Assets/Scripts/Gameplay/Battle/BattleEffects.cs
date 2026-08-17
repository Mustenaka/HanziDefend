using System;
using System.Collections.Generic;
using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    public readonly struct EffectExecutionContext
    {
        private static readonly IReadOnlyList<int> EmptyAdjacentAllies = Array.Empty<int>();

        private readonly IReadOnlyList<int> adjacentAllyEntityIds;

        public EffectExecutionContext(
            int? sourceEntityId,
            BattleTeam sourceTeam,
            Vector2 origin,
            int spawnLevel,
            IReadOnlyList<int> adjacentAllyEntityIds = null,
            int? restrictToEntityId = null)
        {
            SourceEntityId = sourceEntityId;
            SourceTeam = sourceTeam;
            Origin = origin;
            SpawnLevel = spawnLevel;
            this.adjacentAllyEntityIds = CopyAdjacentAllies(adjacentAllyEntityIds);
            RestrictToEntityId = restrictToEntityId;
        }

        public int? SourceEntityId { get; }

        public BattleTeam SourceTeam { get; }

        public Vector2 Origin { get; }

        public int SpawnLevel { get; }

        /// <summary>
        /// When set, the effect resolves as if this were the only entity on the field: every
        /// per-target op keeps just this entity, and one-shot global ops (SpawnUnit, ModifyCoins)
        /// are skipped entirely.
        ///
        /// <para>This is what lets an ally that enters late (WO-F1 §B) inherit the run's carried
        /// buffs. Replaying an <c>AllyAll</c> buff unrestricted would apply it a second time to the
        /// units already standing there, and a Stack-rule buff such as <c>buff_atk_up</c> would
        /// compound once per late arrival.</para>
        /// </summary>
        public int? RestrictToEntityId { get; }

        public IReadOnlyList<int> AdjacentAllyEntityIds => adjacentAllyEntityIds ?? EmptyAdjacentAllies;

        private static IReadOnlyList<int> CopyAdjacentAllies(IReadOnlyList<int> source)
        {
            if (source == null || source.Count == 0)
            {
                return EmptyAdjacentAllies;
            }

            var copy = new int[source.Count];
            for (int index = 0; index < copy.Length; index++)
            {
                copy[index] = source[index];
            }

            return copy;
        }
    }

    public readonly struct EffectExecutionResult
    {
        public EffectExecutionResult(
            string effectId,
            int appliedOperationCount,
            int affectedTargetCount,
            int spawnedUnitCount,
            int coinDelta)
        {
            if (string.IsNullOrWhiteSpace(effectId))
            {
                throw new ArgumentException("Effect id is required.", nameof(effectId));
            }

            if (appliedOperationCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(appliedOperationCount));
            }

            if (affectedTargetCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(affectedTargetCount));
            }

            if (spawnedUnitCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spawnedUnitCount));
            }

            EffectId = effectId;
            AppliedOperationCount = appliedOperationCount;
            AffectedTargetCount = affectedTargetCount;
            SpawnedUnitCount = spawnedUnitCount;
            CoinDelta = coinDelta;
        }

        public string EffectId { get; }

        public int AppliedOperationCount { get; }

        public int AffectedTargetCount { get; }

        public int SpawnedUnitCount { get; }

        public int CoinDelta { get; }

        public int AppliedCount => AppliedOperationCount;

        public int AffectedCount => AffectedTargetCount;

        public int SpawnedCount => SpawnedUnitCount;
    }

    public readonly struct ActiveEffectSnapshot
    {
        public ActiveEffectSnapshot(
            long instanceId,
            string effectId,
            int opIndex,
            int? sourceEntityId,
            int targetEntityId,
            EffectOpCode op,
            string stat,
            StatModifierMode mode,
            float value,
            float remainingDurationSeconds,
            float remainingShield,
            bool isPermanent)
        {
            InstanceId = instanceId;
            EffectId = effectId ?? string.Empty;
            OpIndex = opIndex;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            Op = op;
            Stat = stat ?? string.Empty;
            Mode = mode;
            Value = value;
            RemainingDurationSeconds = remainingDurationSeconds;
            RemainingShield = remainingShield;
            IsPermanent = isPermanent;
        }

        public long InstanceId { get; }

        public string EffectId { get; }

        public int OpIndex { get; }

        public int? SourceEntityId { get; }

        public int TargetEntityId { get; }

        public EffectOpCode Op { get; }

        public string Stat { get; }

        public StatModifierMode Mode { get; }

        public float Value { get; }

        /// <summary>-1 denotes a permanent effect.</summary>
        public float RemainingDurationSeconds { get; }

        public float RemainingShield { get; }

        public bool IsPermanent { get; }
    }

    internal sealed class ActiveEffectInstance
    {
        private const double PermanentExpirationTimeSeconds = -1d;

        internal ActiveEffectInstance(
            long instanceId,
            string effectId,
            int opIndex,
            int? sourceEntityId,
            int targetEntityId,
            EffectOpCode op,
            string stat,
            StatModifierMode mode,
            float value,
            double expiresAtSeconds,
            float remainingShield)
        {
            InstanceId = instanceId;
            EffectId = effectId ?? string.Empty;
            OpIndex = opIndex;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            Op = op;
            Stat = stat ?? string.Empty;
            Mode = mode;
            Value = value;
            ExpiresAtSeconds = expiresAtSeconds;
            RemainingShield = remainingShield;
        }

        internal long InstanceId { get; }

        internal string EffectId { get; }

        internal int OpIndex { get; }

        internal int? SourceEntityId { get; }

        internal int TargetEntityId { get; }

        internal EffectOpCode Op { get; }

        internal string Stat { get; }

        internal StatModifierMode Mode { get; }

        internal float Value { get; }

        internal double ExpiresAtSeconds { get; set; }

        internal float RemainingShield { get; set; }

        internal bool IsPermanent => ExpiresAtSeconds < 0d || double.IsPositiveInfinity(ExpiresAtSeconds);

        internal bool MatchesStackingKey(
            string effectId,
            int? sourceEntityId,
            int targetEntityId,
            int opIndex)
        {
            return string.Equals(EffectId, effectId, StringComparison.Ordinal)
                   && SourceEntityId == sourceEntityId
                   && TargetEntityId == targetEntityId
                   && OpIndex == opIndex;
        }

        internal ActiveEffectSnapshot Snapshot(double nowSeconds)
        {
            bool isPermanent = IsPermanent;
            float remainingDurationSeconds = isPermanent
                ? (float)PermanentExpirationTimeSeconds
                : Mathf.Max(0f, (float)(ExpiresAtSeconds - nowSeconds));

            return new ActiveEffectSnapshot(
                InstanceId,
                EffectId,
                OpIndex,
                SourceEntityId,
                TargetEntityId,
                Op,
                Stat,
                Mode,
                Value,
                remainingDurationSeconds,
                RemainingShield,
                isPermanent);
        }
    }

    public readonly struct EffectAppliedEvent
    {
        public EffectAppliedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            long? instanceId,
            string effectId,
            int opIndex,
            int? sourceEntityId,
            int targetEntityId,
            EffectOpCode op,
            string stat,
            StatModifierMode mode,
            float value,
            float durationSeconds)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            InstanceId = instanceId;
            EffectId = effectId ?? string.Empty;
            OpIndex = opIndex;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            Op = op;
            Stat = stat ?? string.Empty;
            Mode = mode;
            Value = value;
            DurationSeconds = durationSeconds;
        }

        public long Sequence { get; }

        public long TickIndex { get; }

        public double SimulatedTimeSeconds { get; }

        public double SimulatedTime => SimulatedTimeSeconds;

        public long? InstanceId { get; }

        public string EffectId { get; }

        public int OpIndex { get; }

        public int? SourceEntityId { get; }

        public int TargetEntityId { get; }

        public EffectOpCode Op { get; }

        public string Stat { get; }

        public StatModifierMode Mode { get; }

        public float Value { get; }

        /// <summary>-1 denotes a permanent effect.</summary>
        public float DurationSeconds { get; }

        public bool IsPermanent => DurationSeconds < 0f;
    }

    public readonly struct EffectExpiredEvent
    {
        public EffectExpiredEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            long instanceId,
            string effectId,
            int opIndex,
            int? sourceEntityId,
            int targetEntityId,
            EffectOpCode op,
            string stat,
            StatModifierMode mode,
            float value)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            InstanceId = instanceId;
            EffectId = effectId ?? string.Empty;
            OpIndex = opIndex;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            Op = op;
            Stat = stat ?? string.Empty;
            Mode = mode;
            Value = value;
        }

        public long Sequence { get; }

        public long TickIndex { get; }

        public double SimulatedTimeSeconds { get; }

        public double SimulatedTime => SimulatedTimeSeconds;

        public long InstanceId { get; }

        public string EffectId { get; }

        public int OpIndex { get; }

        public int? SourceEntityId { get; }

        public int TargetEntityId { get; }

        public EffectOpCode Op { get; }

        public string Stat { get; }

        public StatModifierMode Mode { get; }

        public float Value { get; }
    }

    public readonly struct UnitHealedEvent
    {
        public UnitHealedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            string effectId,
            int opIndex,
            int? sourceEntityId,
            int targetEntityId,
            float requestedAmount,
            float appliedAmount,
            float hpBefore,
            float hpAfter,
            float maxHp)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            EffectId = effectId ?? string.Empty;
            OpIndex = opIndex;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            RequestedAmount = requestedAmount;
            AppliedAmount = appliedAmount;
            HpBefore = hpBefore;
            HpAfter = hpAfter;
            MaxHp = maxHp;
        }

        public long Sequence { get; }

        public long TickIndex { get; }

        public double SimulatedTimeSeconds { get; }

        public double SimulatedTime => SimulatedTimeSeconds;

        public string EffectId { get; }

        public int OpIndex { get; }

        public int? SourceEntityId { get; }

        public int TargetEntityId { get; }

        public float RequestedAmount { get; }

        public float AppliedAmount { get; }

        public float Amount => AppliedAmount;

        public float HpBefore { get; }

        public float HpAfter { get; }

        public float MaxHp { get; }
    }

    public readonly struct ShieldChangedEvent
    {
        public ShieldChangedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            long instanceId,
            string effectId,
            int opIndex,
            int? sourceEntityId,
            int targetEntityId,
            float delta,
            float shieldBefore,
            float shieldAfter)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            InstanceId = instanceId;
            EffectId = effectId ?? string.Empty;
            OpIndex = opIndex;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            Delta = delta;
            ShieldBefore = shieldBefore;
            ShieldAfter = shieldAfter;
        }

        public long Sequence { get; }

        public long TickIndex { get; }

        public double SimulatedTimeSeconds { get; }

        public double SimulatedTime => SimulatedTimeSeconds;

        public long InstanceId { get; }

        public string EffectId { get; }

        public int OpIndex { get; }

        public int? SourceEntityId { get; }

        public int TargetEntityId { get; }

        public float Delta { get; }

        public float Value => Delta;

        public float ShieldBefore { get; }

        public float ShieldAfter { get; }

        public bool IsDepleted => ShieldAfter <= 0f;
    }

    public readonly struct CoinsModifiedEvent
    {
        public CoinsModifiedEvent(
            long sequence,
            long tickIndex,
            double simulatedTimeSeconds,
            string effectId,
            int opIndex,
            int? sourceEntityId,
            BattleTeam targetTeam,
            int delta,
            int coinsBefore,
            int coinsAfter)
        {
            Sequence = sequence;
            TickIndex = tickIndex;
            SimulatedTimeSeconds = simulatedTimeSeconds;
            EffectId = effectId ?? string.Empty;
            OpIndex = opIndex;
            SourceEntityId = sourceEntityId;
            TargetTeam = targetTeam;
            Delta = delta;
            CoinsBefore = coinsBefore;
            CoinsAfter = coinsAfter;
        }

        public long Sequence { get; }

        public long TickIndex { get; }

        public double SimulatedTimeSeconds { get; }

        public double SimulatedTime => SimulatedTimeSeconds;

        public string EffectId { get; }

        public int OpIndex { get; }

        public int? SourceEntityId { get; }

        public BattleTeam TargetTeam { get; }

        public int Delta { get; }

        public int Value => Delta;

        public int CoinsBefore { get; }

        public int CoinsAfter { get; }

        public int TotalCoins => CoinsAfter;
    }

    public interface IBattleEffectEvents
    {
        void EffectApplied(EffectAppliedEvent eventData);

        void EffectExpired(EffectExpiredEvent eventData);

        void UnitHealed(UnitHealedEvent eventData);

        void ShieldChanged(ShieldChangedEvent eventData);

        void CoinsModified(CoinsModifiedEvent eventData);
    }

    public sealed class NullBattleEffectEvents : IBattleEffectEvents
    {
        public static NullBattleEffectEvents Instance { get; } = new NullBattleEffectEvents();

        public void EffectApplied(EffectAppliedEvent eventData) { }

        public void EffectExpired(EffectExpiredEvent eventData) { }

        public void UnitHealed(UnitHealedEvent eventData) { }

        public void ShieldChanged(ShieldChangedEvent eventData) { }

        public void CoinsModified(CoinsModifiedEvent eventData) { }
    }
}
