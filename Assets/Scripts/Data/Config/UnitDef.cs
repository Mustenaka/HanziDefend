using System;

namespace HanziDefend.Data
{
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

        public UnitLayer Layer { get; set; }

        public UnitSpawnMode SpawnMode { get; set; }

        public TargetingMode Targeting { get; set; }

        public string[] Effects { get; set; } = Array.Empty<string>();

        public StatCurve Hp { get; set; } = new StatCurve();

        public StatCurve Atk { get; set; } = new StatCurve();

        public StatCurve Range { get; set; } = new StatCurve();

        public StatCurve AtkSpeed { get; set; } = new StatCurve();

        public StatCurve Cooldown { get; set; } = new StatCurve();

        public StatCurve Armor { get; set; } = new StatCurve();

        public StatCurve Pierce { get; set; } = new StatCurve();

        public StatCurve MoveSpeed { get; set; } = new StatCurve();
    }
}
