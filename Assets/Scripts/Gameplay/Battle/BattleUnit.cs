using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    internal sealed class BattleUnit
    {
        internal BattleUnit(
            int entityId,
            string definitionId,
            BattleTeam team,
            int level,
            BattleStats stats,
            Vector2 position,
            Vector2 facing,
            EnemyRank? rewardRank,
            BattleTargetKind targetKind,
            TargetingMode targeting)
        {
            EntityId = entityId;
            DefinitionId = definitionId;
            Team = team;
            Level = level;
            BaseStats = stats;
            Stats = stats;
            CurrentHp = stats.MaxHp;
            Position = position;
            Facing = facing;
            State = BattleUnitState.Idle;
            RewardRank = rewardRank;
            TargetKind = targetKind;
            Targeting = targeting;
        }

        internal int EntityId { get; }

        internal string DefinitionId { get; }

        internal BattleTeam Team { get; }

        internal int Level { get; }

        internal BattleStats BaseStats { get; }

        internal BattleStats Stats { get; set; }

        internal float CurrentHp { get; set; }

        internal float CurrentShield { get; set; }

        internal Vector2 Position { get; set; }

        internal Vector2 Facing { get; set; }

        internal BattleUnitState State { get; set; }

        internal int? TargetEntityId { get; set; }

        internal EnemyRank? RewardRank { get; }

        internal BattleTargetKind TargetKind { get; }

        internal TargetingMode Targeting { get; }

        internal bool IsBoss => TargetKind == BattleTargetKind.Boss;

        internal double NextAttackTime { get; set; }

        internal double NextRetargetTime { get; set; }

        internal Rigidbody2D Rigidbody { get; set; }

        internal Collider2D Collider { get; set; }

        internal BattleTargetBody Body { get; set; }

        internal BattleUnitSnapshot Snapshot()
        {
            return new BattleUnitSnapshot(
                EntityId,
                DefinitionId,
                Team,
                Level,
                Stats,
                CurrentHp,
                Position,
                Facing,
                State,
                TargetEntityId,
                TargetKind,
                Targeting,
                CurrentShield);
        }
    }

    [DisallowMultipleComponent]
    internal sealed class BattleTargetBody : MonoBehaviour
    {
        internal object Owner { get; set; }

        internal int EntityId { get; set; }

        internal BattleTargetKind Kind { get; set; }
    }

}
