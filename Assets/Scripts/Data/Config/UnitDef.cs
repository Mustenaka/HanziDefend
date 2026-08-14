using System;

namespace HanziDefend.Data
{
    public enum UnitTraitType
    {
        Unknown = 0,
        Charge,
        Trample,
        PiercingShot,
        FireAura,
        IceAura,
        DeathSpawn
    }

    [Serializable]
    public sealed class UnitTraitDef
    {
        public UnitTraitType Type { get; set; }

        public float Multiplier { get; set; }

        public float DecayRate { get; set; }

        public float MinimumMultiplier { get; set; }

        public bool ExcludesMainBase { get; set; }

        public string UnitId { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    [Serializable]
    public sealed class UnitDef
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public UnitFaction Faction { get; set; }

        public UnitTier Tier { get; set; }

        public int GridW { get; set; }

        public int GridH { get; set; }

        public UnitFootprintShape Footprint { get; set; }

        public UnitLayer Layer { get; set; }

        public UnitSpawnMode SpawnMode { get; set; }

        public TargetingMode Targeting { get; set; }

        public UnitType UnitType { get; set; }

        public ArmorType ArmorType { get; set; }

        public AttackType AtkType { get; set; }

        public BonusVsDef[] BonusVs { get; set; } = Array.Empty<BonusVsDef>();

        public UnitTraitDef[] Traits { get; set; } = Array.Empty<UnitTraitDef>();

        public string[] Effects { get; set; } = Array.Empty<string>();

        public StatCurve Hp { get; set; } = new StatCurve();

        public StatCurve Atk { get; set; } = new StatCurve();

        public StatCurve Range { get; set; } = new StatCurve();

        public StatCurve MinRange { get; set; } = new StatCurve();

        public StatCurve AtkSpeed { get; set; } = new StatCurve();

        public StatCurve Cooldown { get; set; } = new StatCurve();

        public StatCurve Armor { get; set; } = new StatCurve();

        public StatCurve Pierce { get; set; } = new StatCurve();

        public StatCurve MoveSpeed { get; set; } = new StatCurve();
    }
}
