using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    internal sealed class BattleBase
    {
        internal BattleBase(
            int entityId,
            BattleTeam team,
            float maxHp,
            float armor,
            Vector2 position)
        {
            EntityId = entityId;
            Team = team;
            MaxHp = maxHp;
            CurrentHp = maxHp;
            Armor = armor;
            Position = position;
        }

        internal int EntityId { get; }

        internal BattleTeam Team { get; }

        internal float MaxHp { get; }

        internal float CurrentHp { get; set; }

        internal float Armor { get; }

        internal Vector2 Position { get; }

        internal bool IsDestroyed => CurrentHp <= 0f;

        internal Rigidbody2D Rigidbody { get; set; }

        internal Collider2D Collider { get; set; }

        internal BattleTargetBody Body { get; set; }

        internal BattleBaseSnapshot Snapshot()
        {
            return new BattleBaseSnapshot(EntityId, Team, MaxHp, CurrentHp, Armor, Position);
        }
    }

}
