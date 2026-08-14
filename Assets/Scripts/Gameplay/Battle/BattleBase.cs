using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    internal sealed class BattleBase
    {
        internal BattleBase(
            int entityId,
            string definitionId,
            BattleTeam team,
            float maxHp,
            float armor,
            Vector2 position,
            UnitType unitType,
            ArmorType armorType)
        {
            EntityId = entityId;
            DefinitionId = definitionId;
            Team = team;
            MaxHp = maxHp;
            CurrentHp = maxHp;
            Armor = armor;
            Position = position;
            UnitType = unitType;
            ArmorType = armorType;
        }

        internal int EntityId { get; }

        internal string DefinitionId { get; }

        internal BattleTeam Team { get; }

        internal float MaxHp { get; }

        internal float CurrentHp { get; set; }

        internal float Armor { get; }

        internal Vector2 Position { get; }

        internal UnitType UnitType { get; }

        internal ArmorType ArmorType { get; }

        internal bool IsDestroyed => CurrentHp <= 0f;

        internal BattleBaseSnapshot Snapshot()
        {
            return new BattleBaseSnapshot(
                EntityId, DefinitionId, Team, MaxHp, CurrentHp, Armor, Position, UnitType, ArmorType);
        }
    }

}
