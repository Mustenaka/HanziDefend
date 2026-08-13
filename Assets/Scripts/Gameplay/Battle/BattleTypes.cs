using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    public enum BattleTeam
    {
        Ally,
        Enemy
    }

    public enum BattleUnitState
    {
        Idle,
        Move,
        Attack,
        Dead
    }

    public enum BattleResult
    {
        None,
        Win,
        Lose
    }

    public enum BattleTargetKind
    {
        Unit,
        Boss,
        Base
    }

    public readonly struct UnitSpawnRequest
    {
        public UnitSpawnRequest(
            string definitionId,
            int level,
            Vector2 position,
            EnemyRank rewardRank = EnemyRank.Normal)
        {
            DefinitionId = definitionId;
            Level = level;
            Position = position;
            RewardRank = rewardRank;
        }

        public string DefinitionId { get; }

        public int Level { get; }

        public Vector2 Position { get; }

        public EnemyRank RewardRank { get; }
    }

    public readonly struct BattleBaseSnapshot
    {
        public BattleBaseSnapshot(
            int entityId,
            BattleTeam team,
            float maxHp,
            float currentHp,
            float armor,
            Vector2 position)
        {
            EntityId = entityId;
            Team = team;
            MaxHp = maxHp;
            CurrentHp = currentHp;
            Armor = armor;
            Position = position;
        }

        public int EntityId { get; }

        public BattleTeam Team { get; }

        public float MaxHp { get; }

        public float CurrentHp { get; }

        public float Armor { get; }

        public Vector2 Position { get; }

        public bool IsDestroyed => CurrentHp <= 0f;
    }

    public readonly struct BattleStats
    {
        public BattleStats(
            float maxHp,
            float atk,
            float range,
            float atkSpeed,
            float cooldown,
            float armor,
            float pierce,
            float moveSpeed)
        {
            MaxHp = maxHp;
            Atk = atk;
            Range = range;
            AtkSpeed = atkSpeed;
            Cooldown = cooldown;
            Armor = armor;
            Pierce = pierce;
            MoveSpeed = moveSpeed;
        }

        public float MaxHp { get; }

        public float Atk { get; }

        public float Range { get; }

        public float AtkSpeed { get; }

        public float Cooldown { get; }

        public float Armor { get; }

        public float Pierce { get; }

        public float MoveSpeed { get; }
    }

    public readonly struct BattleUnitSnapshot
    {
        public BattleUnitSnapshot(
            int entityId,
            string definitionId,
            BattleTeam team,
            int level,
            BattleStats stats,
            float currentHp,
            Vector2 position,
            Vector2 facing,
            BattleUnitState state,
            int? targetEntityId)
            : this(
                entityId,
                definitionId,
                team,
                level,
                stats,
                currentHp,
                position,
                facing,
                state,
                targetEntityId,
                BattleTargetKind.Unit,
                TargetingMode.Nearest,
                0f)
        {
        }

        public BattleUnitSnapshot(
            int entityId,
            string definitionId,
            BattleTeam team,
            int level,
            BattleStats stats,
            float currentHp,
            Vector2 position,
            Vector2 facing,
            BattleUnitState state,
            int? targetEntityId,
            BattleTargetKind targetKind)
            : this(
                entityId,
                definitionId,
                team,
                level,
                stats,
                currentHp,
                position,
                facing,
                state,
                targetEntityId,
                targetKind,
                TargetingMode.Nearest,
                0f)
        {
        }

        public BattleUnitSnapshot(
            int entityId,
            string definitionId,
            BattleTeam team,
            int level,
            BattleStats stats,
            float currentHp,
            Vector2 position,
            Vector2 facing,
            BattleUnitState state,
            int? targetEntityId,
            BattleTargetKind targetKind,
            TargetingMode targeting,
            float currentShield)
        {
            EntityId = entityId;
            DefinitionId = definitionId;
            Team = team;
            Level = level;
            Stats = stats;
            CurrentHp = currentHp;
            Position = position;
            Facing = facing;
            State = state;
            TargetEntityId = targetEntityId;
            TargetKind = targetKind;
            Targeting = targeting;
            CurrentShield = currentShield;
        }

        public int EntityId { get; }

        public string DefinitionId { get; }

        public BattleTeam Team { get; }

        public int Level { get; }

        public BattleStats Stats { get; }

        public float CurrentHp { get; }

        public Vector2 Position { get; }

        public Vector2 Facing { get; }

        public BattleUnitState State { get; }

        public int? TargetEntityId { get; }

        public BattleTargetKind TargetKind { get; }

        public TargetingMode Targeting { get; }

        public float CurrentShield { get; }

        public float Shield => CurrentShield;
    }
}
