using System;
using System.Collections.Generic;
using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    public sealed partial class BattleSystem
    {
        private readonly List<TraitTargetCandidate> piercingTargets =
            new List<TraitTargetCandidate>();
        private readonly List<TraitTargetCandidate> trampleTargets =
            new List<TraitTargetCandidate>();

        private static UnitTraitDef FindTrait(BattleUnit unit, UnitTraitType type)
        {
            UnitTraitDef[] traits = unit.Traits;
            for (int index = 0; index < traits.Length; index++)
            {
                UnitTraitDef trait = traits[index];
                if (trait != null && trait.Type == type)
                {
                    return trait;
                }
            }

            return null;
        }

        private static bool HasTrait(BattleUnit unit, UnitTraitType type)
        {
            return unit != null && FindTrait(unit, type) != null;
        }

        private static bool HasPendingTrample(BattleUnit unit)
        {
            return !unit.TrampleCompleted && HasTrait(unit, UnitTraitType.Trample);
        }

        private float GetTraitAdjustedMoveSpeed(BattleUnit unit)
        {
            float moveSpeed = unit.Stats.MoveSpeed;
            if (SimulatedTimeSeconds < unit.FrozenUntilSeconds)
            {
                moveSpeed *= unit.FrozenMoveSpeedMultiplier;
            }

            UnitTraitDef charge = FindTrait(unit, UnitTraitType.Charge);
            if (charge != null && !unit.ChargeConsumed)
            {
                moveSpeed *= charge.Multiplier;
            }

            return moveSpeed;
        }

        private static float GetTraitAdjustedAttack(BattleUnit attacker)
        {
            UnitTraitDef charge = FindTrait(attacker, UnitTraitType.Charge);
            if (charge == null || attacker.ChargeConsumed)
            {
                return attacker.Stats.Atk;
            }

            attacker.ChargeConsumed = true;
            return attacker.Stats.Atk * charge.Multiplier;
        }

        private bool TryAdvanceTrample(BattleUnit unit, TargetRef target, float dt)
        {
            if (!HasPendingTrample(unit))
            {
                return false;
            }

            // A trample rush is a unit-path mechanic. If only a settlement anchor remains,
            // finish the one-off rush and let the ordinary target/range rules take over.
            if (target.Kind == BattleTargetKind.Base)
            {
                unit.TrampleCompleted = true;
                unit.TargetEntityId = null;
                return false;
            }

            if (TryGetTrampleDestination(unit, out TargetRef rearTarget))
            {
                target = rearTarget;
            }

            Vector2 start = unit.Position;
            Vector2 delta = target.Position - start;
            float distance = delta.magnitude;
            float moveSpeed = GetTraitAdjustedMoveSpeed(unit);
            float travel = distance > 0f
                ? Mathf.Min(moveSpeed * dt, distance)
                : 0f;
            Vector2 end = distance > 0f
                ? start + delta / distance * travel
                : start;

            unit.State = BattleUnitState.Move;
            unit.Position = end;
            unit.Facing = ResolveFacing(target.Position - end, unit.Facing);
            ResolveTrampleHits(unit, start, end);

            if (distance <= 0f || travel >= distance)
            {
                unit.TrampleCompleted = true;
                unit.TargetEntityId = null;
            }

            return true;
        }

        private bool TryGetTrampleDestination(BattleUnit attacker, out TargetRef destination)
        {
            List<BattleUnit> candidates = attacker.Team == BattleTeam.Ally
                ? aliveEnemyUnits
                : aliveAllyUnits;
            float direction = attacker.Team == BattleTeam.Ally ? 1f : -1f;
            BattleUnit selected = null;
            float selectedProjection = float.NegativeInfinity;
            for (int index = 0; index < candidates.Count; index++)
            {
                BattleUnit candidate = candidates[index];
                if (candidate.State == BattleUnitState.Dead)
                {
                    continue;
                }

                float projection = (candidate.Position.y - attacker.Position.y) * direction;
                if (selected == null
                    || projection > selectedProjection
                    || (Mathf.Approximately(projection, selectedProjection)
                        && candidate.EntityId < selected.EntityId))
                {
                    selected = candidate;
                    selectedProjection = projection;
                }
            }

            destination = selected == null ? default : new TargetRef(selected);
            return selected != null;
        }

        private void ResolveTrampleHits(BattleUnit attacker, Vector2 start, Vector2 end)
        {
            trampleTargets.Clear();
            Vector2 segment = end - start;
            float segmentLengthSquared = segment.sqrMagnitude;
            float hitRadiusSquared = rules.ColliderRadius * rules.ColliderRadius;
            List<BattleUnit> candidates = attacker.Team == BattleTeam.Ally
                ? aliveEnemyUnits
                : aliveAllyUnits;

            for (int index = 0; index < candidates.Count; index++)
            {
                BattleUnit candidate = candidates[index];
                if (candidate.State == BattleUnitState.Dead
                    || attacker.TrampledEntityIds.Contains(candidate.EntityId))
                {
                    continue;
                }

                float projection = segmentLengthSquared > 0f
                    ? Mathf.Clamp01(Vector2.Dot(candidate.Position - start, segment)
                                    / segmentLengthSquared)
                    : 0f;
                Vector2 nearest = start + segment * projection;
                if ((candidate.Position - nearest).sqrMagnitude <= hitRadiusSquared)
                {
                    trampleTargets.Add(new TraitTargetCandidate(
                        new TargetRef(candidate),
                        projection));
                }
            }

            trampleTargets.Sort(CompareTraitTargets);
            if (trampleTargets.Count == 0)
            {
                return;
            }

            long attackId = nextAttackId++;
            for (int index = 0; index < trampleTargets.Count; index++)
            {
                TargetRef target = trampleTargets[index].Target;
                if (!target.IsAlive)
                {
                    continue;
                }

                attacker.TrampledEntityIds.Add(target.EntityId);
                PublishTraitAttack(attackId, attacker, target);
                DealDamage(
                    attackId,
                    attacker.EntityId,
                    target,
                    attacker.Stats.Atk,
                    attacker.Stats.Pierce);
                ApplyAttackAuras(attackId, attacker, target);
            }
        }

        private bool TryResolvePiercingShot(
            long attackId,
            BattleUnit attacker,
            TargetRef primaryTarget,
            float attack)
        {
            UnitTraitDef trait = FindTrait(attacker, UnitTraitType.PiercingShot);
            if (trait == null)
            {
                return false;
            }

            piercingTargets.Clear();
            Vector2 direction = primaryTarget.Position - attacker.Position;
            if (direction.sqrMagnitude > 0f)
            {
                direction.Normalize();
            }
            else if (attacker.Facing.sqrMagnitude > 0f)
            {
                direction = attacker.Facing.normalized;
            }
            else
            {
                direction = attacker.Team == BattleTeam.Ally ? Vector2.up : Vector2.down;
            }

            float hitRadiusSquared = rules.ColliderRadius * rules.ColliderRadius;
            List<BattleUnit> candidates = attacker.Team == BattleTeam.Ally
                ? aliveEnemyUnits
                : aliveAllyUnits;
            for (int index = 0; index < candidates.Count; index++)
            {
                BattleUnit candidate = candidates[index];
                Vector2 offset = candidate.Position - attacker.Position;
                float projection = Vector2.Dot(offset, direction);
                if (projection < 0f || projection > attacker.Stats.Range)
                {
                    continue;
                }

                Vector2 perpendicular = offset - direction * projection;
                if (perpendicular.sqrMagnitude <= hitRadiusSquared)
                {
                    piercingTargets.Add(new TraitTargetCandidate(
                        new TargetRef(candidate),
                        projection));
                }
            }

            if (primaryTarget.Kind == BattleTargetKind.Base && !trait.ExcludesMainBase)
            {
                float projection = Vector2.Dot(
                    primaryTarget.Position - attacker.Position,
                    direction);
                piercingTargets.Add(new TraitTargetCandidate(primaryTarget, projection));
            }

            piercingTargets.Sort(CompareTraitTargets);
            float multiplier = trait.Multiplier;
            for (int index = 0; index < piercingTargets.Count; index++)
            {
                TargetRef target = piercingTargets[index].Target;
                if (!target.IsAlive)
                {
                    continue;
                }

                if (target.EntityId != primaryTarget.EntityId)
                {
                    PublishTraitAttack(attackId, attacker, target);
                }

                DealDamage(
                    attackId,
                    attacker.EntityId,
                    target,
                    attack * multiplier,
                    attacker.Stats.Pierce);
                ApplyAttackAuras(attackId, attacker, target);
                multiplier = Mathf.Max(
                    trait.MinimumMultiplier,
                    multiplier * (1f - trait.DecayRate));
            }

            return true;
        }

        private void ApplyAttackAuras(long attackId, BattleUnit attacker, TargetRef target)
        {
            if (target.Unit == null || !target.IsAlive)
            {
                return;
            }

            if (!HasTrait(target.Unit, UnitTraitType.IceAura)
                && TryFindAuraSource(attacker, UnitTraitType.FireAura,
                    out BattleUnit fireSource, out UnitTraitDef fireAura))
            {
                DealDamage(
                    attackId,
                    fireSource.EntityId,
                    target,
                    target.MaxHp * fireAura.Multiplier,
                    fireSource.Stats.Pierce);
            }

            if (!target.IsAlive || HasTrait(target.Unit, UnitTraitType.FireAura))
            {
                return;
            }

            if (TryFindAuraSource(attacker, UnitTraitType.IceAura,
                    out BattleUnit iceSource, out UnitTraitDef iceAura))
            {
                if (SimulatedTimeSeconds >= target.Unit.FrozenUntilSeconds)
                {
                    target.Unit.FrozenMoveSpeedMultiplier = 1f;
                }

                target.Unit.FrozenUntilSeconds = Math.Max(
                    target.Unit.FrozenUntilSeconds,
                    SimulatedTimeSeconds + iceAura.DecayRate);
                target.Unit.FrozenMoveSpeedMultiplier = Mathf.Min(
                    target.Unit.FrozenMoveSpeedMultiplier,
                    Mathf.Clamp01(1f - iceAura.Multiplier));
                effectEvents.EffectApplied(new EffectAppliedEvent(
                    NextEventSequence(),
                    TickIndex,
                    SimulatedTimeSeconds,
                    null,
                    IceAuraPresentationEffectId,
                    0,
                    iceSource.EntityId,
                    target.EntityId,
                    EffectOpCode.AddStat,
                    "moveSpeed",
                    StatModifierMode.Mul,
                    target.Unit.FrozenMoveSpeedMultiplier,
                    iceAura.DecayRate));
            }
        }

        private bool TryFindAuraSource(
            BattleUnit attacker,
            UnitTraitType type,
            out BattleUnit source,
            out UnitTraitDef trait)
        {
            for (int index = 0; index < units.Count; index++)
            {
                BattleUnit candidate = units[index];
                UnitTraitDef candidateTrait = FindTrait(candidate, type);
                if (candidate.EntityId == attacker.EntityId
                    || candidate.Team != attacker.Team
                    || candidate.State == BattleUnitState.Dead
                    || candidateTrait == null)
                {
                    continue;
                }

                float radius = candidateTrait.MinimumMultiplier;
                if ((candidate.Position - attacker.Position).sqrMagnitude <= radius * radius)
                {
                    source = candidate;
                    trait = candidateTrait;
                    return true;
                }
            }

            source = null;
            trait = null;
            return false;
        }

        private void SpawnDeathChildren(BattleUnit unit)
        {
            UnitTraitDef trait = FindTrait(unit, UnitTraitType.DeathSpawn);
            if (trait == null)
            {
                return;
            }

            UnitDef childDefinition = config.GetUnit(trait.UnitId);
            for (int index = 0; index < trait.Count; index++)
            {
                SpawnUnit(
                    childDefinition,
                    unit.Level,
                    unit.Position,
                    EnemyRank.Normal,
                    null,
                    unit.Team,
                    false);
            }
        }

        private void PublishTraitAttack(long attackId, BattleUnit attacker, TargetRef target)
        {
            events.UnitAttacked(new UnitAttackedEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                attackId,
                attacker.EntityId,
                target.EntityId,
                attacker.DefinitionId,
                target.DefinitionId,
                attacker.Team,
                target.Team,
                attacker.Position,
                target.Position,
                attacker.Facing));
        }

        private static int CompareTraitTargets(
            TraitTargetCandidate left,
            TraitTargetCandidate right)
        {
            int projectionOrder = left.Projection.CompareTo(right.Projection);
            return projectionOrder != 0
                ? projectionOrder
                : left.Target.EntityId.CompareTo(right.Target.EntityId);
        }

        private readonly struct TraitTargetCandidate
        {
            internal TraitTargetCandidate(TargetRef target, float projection)
            {
                Target = target;
                Projection = projection;
            }

            internal TargetRef Target { get; }

            internal float Projection { get; }
        }
    }
}
