using System;

namespace HanziDefend.Data
{
    [Serializable]
    public sealed class CommanderDef
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string PassiveEffectId { get; set; } = string.Empty;

        public string ActiveEffectId { get; set; } = string.Empty;

        public float ActiveCooldown { get; set; }
    }

    [Serializable]
    public sealed class WaveSpawnDef
    {
        public string UnitId { get; set; } = string.Empty;

        public int Level { get; set; }

        public int Count { get; set; }

        public float SpreadX { get; set; }

        public float IntervalSec { get; set; }
    }

    [Serializable]
    public sealed class WaveDef
    {
        public int Index { get; set; }

        public EnemyRank RewardRank { get; set; } = (EnemyRank)(-1);

        public float DelaySec { get; set; }

        public WaveSpawnDef[] Spawns { get; set; } = Array.Empty<WaveSpawnDef>();
    }

    [Serializable]
    public sealed class WaveSetDef
    {
        public string Id { get; set; } = string.Empty;

        public WaveDef[] Waves { get; set; } = Array.Empty<WaveDef>();
    }

    [Serializable]
    public sealed class LevelDef
    {
        public string Id { get; set; } = string.Empty;

        public int StageIndex { get; set; }

        public string WaveSetId { get; set; } = string.Empty;

        public float BaseHp { get; set; }

        public int StartCoins { get; set; }

        public int GridCols { get; set; }

        public int GridRows { get; set; }

        public int GridMaxCols { get; set; }

        public int GridMaxRows { get; set; }
    }

    public enum EffectOpCode
    {
        Unknown = 0,
        AddStat,
        Heal,
        Damage,
        GrantShield,
        SpawnUnit,
        ModifyCoins
    }

    public enum EffectTarget
    {
        Unknown = 0,
        SelfUnit,
        AllyAll,
        AllyAdjacent,
        EnemyNearest,
        EnemyInRadius,
        EnemyBase
    }

    public enum StatModifierMode
    {
        Unknown = 0,
        Add,
        Mul
    }

    public enum EffectTrigger
    {
        Unknown = 0,
        Manual,
        BattleStart,
        UnitSpawn,
        SuicideContact
    }

    public enum EffectStackingRule
    {
        Unknown = 0,

        /// <summary>Every application creates an independent active instance.</summary>
        Stack,

        /// <summary>
        /// Reapplication refreshes the instance keyed by effect, source entity,
        /// target entity and op index without accumulating its magnitude again.
        /// </summary>
        Refresh
    }

    [Serializable]
    public sealed class EffectOpDef
    {
        public EffectOpCode Op { get; set; }

        public string Stat { get; set; } = string.Empty;

        public StatModifierMode Mode { get; set; }

        public float Value { get; set; }

        public EffectTarget Target { get; set; }

        /// <summary>-1 is permanent, 0 is instant, and positive values are seconds.</summary>
        public float Duration { get; set; }

        public string UnitId { get; set; } = string.Empty;

        public int Count { get; set; }

        public float Radius { get; set; }
    }

    [Serializable]
    public sealed class EffectDef
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Desc { get; set; } = string.Empty;

        public string Rarity { get; set; } = string.Empty;

        public EffectTrigger Trigger { get; set; }

        public EffectStackingRule Stacking { get; set; }

        public EffectOpDef[] Ops { get; set; } = Array.Empty<EffectOpDef>();
    }

    [Serializable]
    public sealed class DropCoinsDef
    {
        public int Normal { get; set; }

        public int Elite { get; set; }

        public int Boss { get; set; }
    }

    [Serializable]
    public sealed class DamageFormulaDef
    {
        public float ArmorScale { get; set; }

        public int MinimumDamage { get; set; }
    }

    [Serializable]
    public sealed class CardWeightsDef
    {
        public int Unit { get; set; }

        public int Expand { get; set; }

        public int Buff { get; set; }

        public int Global { get; set; }
    }

    [Serializable]
    public sealed class Position2Def
    {
        public float X { get; set; }

        public float Y { get; set; }
    }

    [Serializable]
    public sealed class BattleRulesDef
    {
        public int TickRateHz { get; set; }

        public float RetargetInterval { get; set; }

        public float TargetSearchRadius { get; set; }

        public float ColliderRadius { get; set; }

        public float SameColumnTolerance { get; set; }

        public float SeparationDistance { get; set; }

        public Position2Def AllyBasePosition { get; set; }

        public Position2Def EnemyBasePosition { get; set; }

        public Position2Def EnemySpawnCenter { get; set; }
    }

    [Serializable]
    public sealed class EconomyDef
    {
        public int RefreshBaseCost { get; set; }

        public int RefreshCostGrowth { get; set; }

        public DropCoinsDef DropCoins { get; set; } = new DropCoinsDef();

        public DamageFormulaDef Damage { get; set; } = new DamageFormulaDef();

        public CardWeightsDef CardWeights { get; set; } = new CardWeightsDef();

        public BattleRulesDef Battle { get; set; } = new BattleRulesDef();
    }
}
