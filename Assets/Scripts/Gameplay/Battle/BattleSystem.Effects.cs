using System;
using System.Collections.Generic;
using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    public sealed partial class BattleSystem
    {
        private readonly List<ActiveEffectInstance> activeEffects = new List<ActiveEffectInstance>();
        private readonly List<TargetRef> effectTargets = new List<TargetRef>();
        private readonly Dictionary<string, double> commanderReadyTimes =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly HashSet<string> configuredCommanders = new HashSet<string>(StringComparer.Ordinal);
        private long nextEffectInstanceId = 1;

        public int Coins { get; private set; }

        public EffectExecutionContext ContextForUnit(
            int sourceEntityId,
            IReadOnlyList<int> adjacentAllyEntityIds = null)
        {
            ThrowIfDisposed();
            if (!unitsById.TryGetValue(sourceEntityId, out BattleUnit source))
            {
                throw new KeyNotFoundException($"Unknown battle unit {sourceEntityId}.");
            }

            return new EffectExecutionContext(
                sourceEntityId,
                source.Team,
                source.Position,
                source.Level,
                adjacentAllyEntityIds);
        }

        public IReadOnlyList<ActiveEffectSnapshot> CaptureActiveEffects()
        {
            ThrowIfDisposed();
            var snapshots = new ActiveEffectSnapshot[activeEffects.Count];
            for (int index = 0; index < activeEffects.Count; index++)
            {
                snapshots[index] = activeEffects[index].Snapshot(SimulatedTimeSeconds);
            }

            return snapshots;
        }

        public EffectExecutionResult ApplyEffect(string effectId, EffectExecutionContext context)
        {
            ThrowIfDisposed();
            ThrowIfSettled();
            EffectDef definition = config.GetEffect(effectId);
            ValidateExecutionContext(context);
            return ExecuteEffect(definition, NormalizeContext(context));
        }

        public void ConfigureCommander(string commanderId)
        {
            ThrowIfDisposed();
            ThrowIfSettled();
            CommanderDef commander = config.GetCommander(commanderId);
            if (!configuredCommanders.Add(commander.Id))
            {
                return;
            }

            commanderReadyTimes.Add(commander.Id, SimulatedTimeSeconds);
            ApplyEffect(
                commander.PassiveEffectId,
                new EffectExecutionContext(null, BattleTeam.Ally, ResolveTeamOrigin(BattleTeam.Ally), 1));
        }

        public bool TryActivateCommander(string commanderId)
        {
            ThrowIfDisposed();
            ThrowIfSettled();
            CommanderDef commander = config.GetCommander(commanderId);
            if (!configuredCommanders.Contains(commander.Id))
            {
                throw new InvalidOperationException($"Commander '{commander.Id}' has not been configured.");
            }

            if (SimulatedTimeSeconds + double.Epsilon < commanderReadyTimes[commander.Id])
            {
                return false;
            }

            ApplyEffect(
                commander.ActiveEffectId,
                new EffectExecutionContext(null, BattleTeam.Ally, ResolveTeamOrigin(BattleTeam.Ally), 1));
            commanderReadyTimes[commander.Id] = SimulatedTimeSeconds + commander.ActiveCooldown;
            return true;
        }

        public float GetCommanderCooldownRemaining(string commanderId)
        {
            ThrowIfDisposed();
            config.GetCommander(commanderId);
            if (!commanderReadyTimes.TryGetValue(commanderId, out double readyAt))
            {
                return 0f;
            }

            return Mathf.Max(0f, (float)(readyAt - SimulatedTimeSeconds));
        }

        private EffectExecutionResult ExecuteEffect(EffectDef effect, EffectExecutionContext context)
        {
            int operations = 0;
            int affected = 0;
            int spawned = 0;
            int coinDelta = 0;

            for (int opIndex = 0; opIndex < effect.Ops.Length; opIndex++)
            {
                EffectOpDef op = effect.Ops[opIndex];
                ResolveEffectTargets(op, context, effectTargets);
                operations++;

                if (op.Op == EffectOpCode.SpawnUnit)
                {
                    for (int ordinal = 0; ordinal < op.Count; ordinal++)
                    {
                        Spawn(new UnitSpawnRequest(op.UnitId, context.SpawnLevel, context.Origin));
                        spawned++;
                    }

                    PublishInstantEffect(effect.Id, opIndex, context.SourceEntityId, 0, op);
                    continue;
                }

                if (op.Op == EffectOpCode.ModifyCoins)
                {
                    int delta = CheckedCoinDelta(op.Value);
                    int before = Coins;
                    Coins = checked(Coins + delta);
                    coinDelta = checked(coinDelta + delta);
                    effectEvents.CoinsModified(new CoinsModifiedEvent(
                        NextEventSequence(), TickIndex, SimulatedTimeSeconds, effect.Id, opIndex,
                        context.SourceEntityId, context.SourceTeam, delta, before, Coins));
                    PublishInstantEffect(effect.Id, opIndex, context.SourceEntityId, 0, op);
                    continue;
                }

                for (int targetIndex = 0; targetIndex < effectTargets.Count; targetIndex++)
                {
                    TargetRef target = effectTargets[targetIndex];
                    switch (op.Op)
                    {
                        case EffectOpCode.AddStat:
                            AddStatEffect(effect, opIndex, context.SourceEntityId, target, op);
                            break;
                        case EffectOpCode.Heal:
                            HealTarget(effect.Id, opIndex, context.SourceEntityId, target, op.Value);
                            break;
                        case EffectOpCode.Damage:
                            DealDamage(nextAttackId++, context.SourceEntityId ?? 0, target, op.Value, 0f);
                            PublishInstantEffect(
                                effect.Id, opIndex, context.SourceEntityId, target.EntityId, op);
                            break;
                        case EffectOpCode.GrantShield:
                            GrantShield(effect, opIndex, context.SourceEntityId, target, op);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported effect op {op.Op}.");
                    }

                    affected++;
                }
            }

            TrySettle();
            return new EffectExecutionResult(effect.Id, operations, affected, spawned, coinDelta);
        }

        private void ResolveEffectTargets(
            EffectOpDef op,
            EffectExecutionContext context,
            List<TargetRef> output)
        {
            output.Clear();
            switch (op.Target)
            {
                case EffectTarget.SelfUnit:
                    if (context.SourceEntityId.HasValue
                        && TryResolveTarget(context.SourceEntityId.Value, out TargetRef self)
                        && self.IsAlive)
                    {
                        output.Add(self);
                    }
                    break;
                case EffectTarget.AllyAll:
                    AddTeamUnits(context.SourceTeam, output);
                    break;
                case EffectTarget.AllyAdjacent:
                    AddAdjacentAllies(context, output);
                    break;
                case EffectTarget.EnemyNearest:
                    AddNearestEffectEnemy(context, output);
                    break;
                case EffectTarget.EnemyInRadius:
                    AddEnemiesInRadius(context, op.Radius, output);
                    break;
                case EffectTarget.EnemyBase:
                    TargetRef enemyBaseTarget = FindOpposingBase(context.SourceTeam);
                    if (enemyBaseTarget.IsValid)
                    {
                        output.Add(enemyBaseTarget);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported effect target {op.Target}.");
            }
        }

        private void AddTeamUnits(BattleTeam team, List<TargetRef> output)
        {
            for (int index = 0; index < units.Count; index++)
            {
                BattleUnit unit = units[index];
                if (unit.Team == team && unit.State != BattleUnitState.Dead)
                {
                    output.Add(new TargetRef(unit));
                }
            }
        }

        private void AddAdjacentAllies(EffectExecutionContext context, List<TargetRef> output)
        {
            var seen = new HashSet<int>();
            IReadOnlyList<int> ids = context.AdjacentAllyEntityIds;
            for (int index = 0; index < ids.Count; index++)
            {
                int entityId = ids[index];
                if (seen.Add(entityId)
                    && unitsById.TryGetValue(entityId, out BattleUnit unit)
                    && unit.Team == context.SourceTeam
                    && unit.State != BattleUnitState.Dead)
                {
                    output.Add(new TargetRef(unit));
                }
            }
        }

        private void AddNearestEffectEnemy(EffectExecutionContext context, List<TargetRef> output)
        {
            TargetRef best = default;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < units.Count; index++)
            {
                BattleUnit unit = units[index];
                if (unit.Team == context.SourceTeam || unit.State == BattleUnitState.Dead)
                {
                    continue;
                }

                float distance = (unit.Position - context.Origin).sqrMagnitude;
                if (distance < bestDistance
                    || (distance == bestDistance && (!best.IsValid || unit.EntityId < best.EntityId)))
                {
                    best = new TargetRef(unit);
                    bestDistance = distance;
                }
            }

            if (best.IsValid)
            {
                output.Add(best);
            }
        }

        private void AddEnemiesInRadius(
            EffectExecutionContext context,
            float radius,
            List<TargetRef> output)
        {
            float radiusSquared = radius * radius;
            for (int index = 0; index < units.Count; index++)
            {
                BattleUnit unit = units[index];
                if (unit.Team != context.SourceTeam
                    && unit.State != BattleUnitState.Dead
                    && (unit.Position - context.Origin).sqrMagnitude <= radiusSquared)
                {
                    output.Add(new TargetRef(unit));
                }
            }
        }

        private void AddStatEffect(
            EffectDef effect,
            int opIndex,
            int? sourceEntityId,
            TargetRef target,
            EffectOpDef op)
        {
            if (target.Unit == null)
            {
                return;
            }

            ActiveEffectInstance instance = GetOrCreateActiveEffect(
                effect, opIndex, sourceEntityId, target.EntityId, op, 0f);
            RecalculateStats(target.Unit);
            PublishEffectApplied(instance, op);
        }

        private void GrantShield(
            EffectDef effect,
            int opIndex,
            int? sourceEntityId,
            TargetRef target,
            EffectOpDef op)
        {
            if (target.Unit == null)
            {
                return;
            }

            ActiveEffectInstance existing = FindRefreshInstance(effect, opIndex, sourceEntityId, target.EntityId);
            if (existing != null)
            {
                existing.ExpiresAtSeconds = ExpirationTime(op.Duration);
                PublishEffectApplied(existing, op);
                return;
            }

            float before = target.Unit.CurrentShield;
            ActiveEffectInstance instance = CreateActiveEffect(
                effect.Id, opIndex, sourceEntityId, target.EntityId, op, op.Value);
            target.Unit.CurrentShield += op.Value;
            effectEvents.ShieldChanged(new ShieldChangedEvent(
                NextEventSequence(), TickIndex, SimulatedTimeSeconds, instance.InstanceId,
                effect.Id, opIndex, sourceEntityId, target.EntityId, op.Value, before,
                target.Unit.CurrentShield));
            PublishEffectApplied(instance, op);
        }

        private void HealTarget(
            string effectId,
            int opIndex,
            int? sourceEntityId,
            TargetRef target,
            float requested)
        {
            if (target.Unit == null)
            {
                return;
            }

            float before = target.Unit.CurrentHp;
            float after = Mathf.Min(target.Unit.Stats.MaxHp, before + requested);
            target.Unit.CurrentHp = after;
            effectEvents.UnitHealed(new UnitHealedEvent(
                NextEventSequence(), TickIndex, SimulatedTimeSeconds, effectId, opIndex,
                sourceEntityId, target.EntityId, requested, after - before, before, after,
                target.Unit.Stats.MaxHp));
            PublishInstantEffect(effectId, opIndex, sourceEntityId, target.EntityId,
                config.GetEffect(effectId).Ops[opIndex]);
        }

        private ActiveEffectInstance GetOrCreateActiveEffect(
            EffectDef effect,
            int opIndex,
            int? sourceEntityId,
            int targetEntityId,
            EffectOpDef op,
            float remainingShield)
        {
            ActiveEffectInstance existing = FindRefreshInstance(
                effect, opIndex, sourceEntityId, targetEntityId);
            if (existing != null)
            {
                existing.ExpiresAtSeconds = ExpirationTime(op.Duration);
                return existing;
            }

            return CreateActiveEffect(
                effect.Id, opIndex, sourceEntityId, targetEntityId, op, remainingShield);
        }

        private ActiveEffectInstance FindRefreshInstance(
            EffectDef effect,
            int opIndex,
            int? sourceEntityId,
            int targetEntityId)
        {
            if (effect.Stacking != EffectStackingRule.Refresh)
            {
                return null;
            }

            for (int index = 0; index < activeEffects.Count; index++)
            {
                ActiveEffectInstance candidate = activeEffects[index];
                if (candidate.MatchesStackingKey(effect.Id, sourceEntityId, targetEntityId, opIndex))
                {
                    return candidate;
                }
            }

            return null;
        }

        private ActiveEffectInstance CreateActiveEffect(
            string effectId,
            int opIndex,
            int? sourceEntityId,
            int targetEntityId,
            EffectOpDef op,
            float remainingShield)
        {
            var instance = new ActiveEffectInstance(
                nextEffectInstanceId++, effectId, opIndex, sourceEntityId, targetEntityId,
                op.Op, op.Stat, op.Mode, op.Value, ExpirationTime(op.Duration), remainingShield);
            activeEffects.Add(instance);
            return instance;
        }

        private double ExpirationTime(float duration)
        {
            return duration < 0f ? -1d : SimulatedTimeSeconds + duration;
        }

        private void ExpireEffects()
        {
            for (int index = activeEffects.Count - 1; index >= 0; index--)
            {
                ActiveEffectInstance instance = activeEffects[index];
                if (instance.IsPermanent || SimulatedTimeSeconds < instance.ExpiresAtSeconds)
                {
                    continue;
                }

                activeEffects.RemoveAt(index);
                if (instance.Op == EffectOpCode.AddStat
                    && unitsById.TryGetValue(instance.TargetEntityId, out BattleUnit unit))
                {
                    RecalculateStats(unit);
                }
                else if (instance.Op == EffectOpCode.GrantShield
                         && instance.RemainingShield > 0f
                         && unitsById.TryGetValue(instance.TargetEntityId, out unit))
                {
                    float before = unit.CurrentShield;
                    unit.CurrentShield = Mathf.Max(0f, before - instance.RemainingShield);
                    effectEvents.ShieldChanged(new ShieldChangedEvent(
                        NextEventSequence(), TickIndex, SimulatedTimeSeconds, instance.InstanceId,
                        instance.EffectId, instance.OpIndex, instance.SourceEntityId,
                        instance.TargetEntityId, unit.CurrentShield - before, before,
                        unit.CurrentShield));
                }

                effectEvents.EffectExpired(new EffectExpiredEvent(
                    NextEventSequence(), TickIndex, SimulatedTimeSeconds, instance.InstanceId,
                    instance.EffectId, instance.OpIndex, instance.SourceEntityId,
                    instance.TargetEntityId, instance.Op, instance.Stat, instance.Mode,
                    instance.Value));
            }
        }

        private void RecalculateStats(BattleUnit unit)
        {
            BattleStats value = unit.BaseStats;
            value = RecalculateStat(unit, value, "hp");
            value = RecalculateStat(unit, value, "atk");
            value = RecalculateStat(unit, value, "range");
            value = RecalculateStat(unit, value, "atkSpeed");
            value = RecalculateStat(unit, value, "cooldown");
            value = RecalculateStat(unit, value, "armor");
            value = RecalculateStat(unit, value, "pierce");
            value = RecalculateStat(unit, value, "moveSpeed");
            unit.Stats = value;
            unit.CurrentHp = Mathf.Min(unit.CurrentHp, value.MaxHp);
        }

        private BattleStats RecalculateStat(BattleUnit unit, BattleStats current, string stat)
        {
            float baseValue = GetStat(unit.BaseStats, stat);
            float add = 0f;
            float mul = 0f;
            for (int index = 0; index < activeEffects.Count; index++)
            {
                ActiveEffectInstance effect = activeEffects[index];
                if (effect.TargetEntityId != unit.EntityId
                    || effect.Op != EffectOpCode.AddStat
                    || !string.Equals(effect.Stat, stat, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (effect.Mode == StatModifierMode.Add)
                {
                    add += effect.Value;
                }
                else if (effect.Mode == StatModifierMode.Mul)
                {
                    mul += effect.Value;
                }
            }

            return SetStat(current, stat, (baseValue + add) * (1f + mul));
        }

        private static float GetStat(BattleStats stats, string stat)
        {
            switch (stat.ToLowerInvariant())
            {
                case "hp": return stats.MaxHp;
                case "atk": return stats.Atk;
                case "range": return stats.Range;
                case "atkspeed": return stats.AtkSpeed;
                case "cooldown": return stats.Cooldown;
                case "armor": return stats.Armor;
                case "pierce": return stats.Pierce;
                case "movespeed": return stats.MoveSpeed;
                default: throw new InvalidOperationException($"Unknown battle stat '{stat}'.");
            }
        }

        private static BattleStats SetStat(BattleStats value, string stat, float replacement)
        {
            string key = stat.ToLowerInvariant();
            return new BattleStats(
                key == "hp" ? replacement : value.MaxHp,
                key == "atk" ? replacement : value.Atk,
                key == "range" ? replacement : value.Range,
                value.MinRange,
                key == "atkspeed" ? replacement : value.AtkSpeed,
                key == "cooldown" ? replacement : value.Cooldown,
                key == "armor" ? replacement : value.Armor,
                key == "pierce" ? replacement : value.Pierce,
                key == "movespeed" ? replacement : value.MoveSpeed);
        }

        private void ApplyAttachedEffects(
            BattleUnit unit,
            string[] effectIds,
            EffectTrigger trigger)
        {
            if (effectIds == null)
            {
                return;
            }

            for (int index = 0; index < effectIds.Length; index++)
            {
                EffectDef effect = config.GetEffect(effectIds[index]);
                if (effect.Trigger == trigger)
                {
                    ExecuteEffect(effect, new EffectExecutionContext(
                        unit.EntityId, unit.Team, unit.Position, unit.Level));
                }
            }
        }

        private void ApplyCommanderPassiveToNewAlly(BattleUnit unit)
        {
            if (unit.Team != BattleTeam.Ally)
            {
                return;
            }

            foreach (string commanderId in configuredCommanders)
            {
                CommanderDef commander = config.GetCommander(commanderId);
                ExecuteEffect(config.GetEffect(commander.PassiveEffectId), new EffectExecutionContext(
                    null, BattleTeam.Ally, unit.Position, unit.Level));
            }
        }

        private void TriggerSuicide(BattleUnit unit, int contactedEntityId)
        {
            if (unit.State == BattleUnitState.Dead)
            {
                return;
            }

            ApplyAttachedEffects(unit, config.GetUnit(unit.DefinitionId).Effects,
                EffectTrigger.SuicideContact);
            Kill(unit, contactedEntityId);
        }

        private void DealDamage(
            long attackId,
            int sourceEntityId,
            TargetRef target,
            float attack,
            float pierce)
        {
            if (!target.IsAlive)
            {
                return;
            }

            AttackType attackType = AttackType.None;
            IReadOnlyList<BonusVsDef> bonusVs = Array.Empty<BonusVsDef>();
            if (sourceEntityId != 0 && unitsById.TryGetValue(sourceEntityId, out BattleUnit source))
            {
                attackType = source.AttackType;
                bonusVs = source.BonusVs;
            }

            int damage = Formula.Damage(
                attack,
                attackType,
                bonusVs,
                target.Armor,
                target.ArmorType,
                target.UnitType,
                pierce,
                config.Economy);
            int hpDamage = damage;
            if (target.Unit != null && target.Unit.CurrentShield > 0f)
            {
                hpDamage = AbsorbShieldDamage(target.Unit, damage);
            }

            float hpBefore = target.CurrentHp;
            float hpAfter = Mathf.Max(0f, hpBefore - hpDamage);
            target.SetCurrentHp(hpAfter);
            bool lethal = hpAfter <= 0f;
            events.DamageDealt(new DamageDealtEvent(
                NextEventSequence(), TickIndex, SimulatedTimeSeconds, attackId,
                sourceEntityId, target.EntityId, damage, hpBefore, hpAfter,
                target.MaxHp, target.Position, lethal));

            if (target.Kind == BattleTargetKind.Base)
            {
                BattleBase value = target.Base;
                encounterEvents.BaseDamaged(new BaseDamagedEvent(
                    NextEventSequence(), TickIndex, SimulatedTimeSeconds, attackId,
                    sourceEntityId, value.EntityId, value.Team, damage, hpBefore,
                    hpAfter, value.MaxHp, value.Position));
                return;
            }

            if (lethal)
            {
                Kill(target.Unit, sourceEntityId);
            }
        }

        private int AbsorbShieldDamage(BattleUnit unit, int damage)
        {
            float remaining = damage;
            for (int index = 0; index < activeEffects.Count && remaining > 0f; index++)
            {
                ActiveEffectInstance shield = activeEffects[index];
                if (shield.TargetEntityId != unit.EntityId
                    || shield.Op != EffectOpCode.GrantShield
                    || shield.RemainingShield <= 0f)
                {
                    continue;
                }

                float before = unit.CurrentShield;
                float absorbed = Mathf.Min(shield.RemainingShield, remaining);
                shield.RemainingShield -= absorbed;
                unit.CurrentShield -= absorbed;
                remaining -= absorbed;
                effectEvents.ShieldChanged(new ShieldChangedEvent(
                    NextEventSequence(), TickIndex, SimulatedTimeSeconds, shield.InstanceId,
                    shield.EffectId, shield.OpIndex, shield.SourceEntityId, unit.EntityId,
                    -absorbed, before, unit.CurrentShield));
            }

            return Mathf.CeilToInt(remaining);
        }

        private void PublishEffectApplied(ActiveEffectInstance instance, EffectOpDef op)
        {
            effectEvents.EffectApplied(new EffectAppliedEvent(
                NextEventSequence(), TickIndex, SimulatedTimeSeconds, instance.InstanceId,
                instance.EffectId, instance.OpIndex, instance.SourceEntityId,
                instance.TargetEntityId, op.Op, op.Stat, op.Mode, op.Value, op.Duration));
        }

        private void PublishInstantEffect(
            string effectId,
            int opIndex,
            int? sourceEntityId,
            int targetEntityId,
            EffectOpDef op)
        {
            effectEvents.EffectApplied(new EffectAppliedEvent(
                NextEventSequence(), TickIndex, SimulatedTimeSeconds, null, effectId,
                opIndex, sourceEntityId, targetEntityId, op.Op, op.Stat, op.Mode,
                op.Value, op.Duration));
        }

        private Vector2 ResolveTeamOrigin(BattleTeam team)
        {
            BattleBase value = team == BattleTeam.Ally ? allyBase : enemyBase;
            return value != null ? value.Position : Vector2.zero;
        }

        private void ValidateExecutionContext(EffectExecutionContext context)
        {
            if (context.SpawnLevel < 1 || context.SpawnLevel > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(context), "Spawn level must be in [1, 4].");
            }

            if (context.SourceEntityId.HasValue)
            {
                if (!unitsById.TryGetValue(context.SourceEntityId.Value, out BattleUnit source)
                    || source.State == BattleUnitState.Dead)
                {
                    throw new ArgumentException("Effect source must be a living battle unit.", nameof(context));
                }

                if (source.Team != context.SourceTeam)
                {
                    throw new ArgumentException("Effect source team does not match the source unit.", nameof(context));
                }
            }
        }

        private EffectExecutionContext NormalizeContext(EffectExecutionContext context)
        {
            if (!context.SourceEntityId.HasValue)
            {
                return context;
            }

            BattleUnit source = unitsById[context.SourceEntityId.Value];
            return new EffectExecutionContext(
                source.EntityId,
                source.Team,
                source.Position,
                source.Level,
                context.AdjacentAllyEntityIds);
        }

        private static int CheckedCoinDelta(float value)
        {
            if (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            {
                throw new InvalidOperationException("ModifyCoins value must be an integer in range.");
            }
            return checked((int)value);
        }
    }
}
