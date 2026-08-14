using System;

namespace HanziDefend.Data
{
    [Serializable]
    public sealed class StatCurve
    {
        public float Base { get; set; }

        public float Growth { get; set; }
    }

    public enum UnitFaction
    {
        Unknown = 0,
        Ally,
        Enemy,
        Boss
    }

    public enum UnitTier
    {
        Green = 1,
        Blue = 2,
        Purple = 3,
        Gold = 4
    }

    public enum UnitLayer
    {
        Unknown = 0,
        Ground,
        Air
    }

    public enum UnitSpawnMode
    {
        Unknown = 0,
        Instant,
        Delayed
    }

    public enum TargetingMode
    {
        Unknown = 0,
        Nearest,
        Backline,
        RushBase,
        Suicide
    }

    /// <summary>
    /// Logical deployment occupancy inside the GridW x GridH bounding box.
    /// Row zero is the lower edge; columns increase from left to right.
    /// </summary>
    public enum UnitFootprintShape
    {
        Unknown = 0,
        Rectangle,
        MissingUpperRight,
        MissingLowerLeft
    }

    public enum UnitType
    {
        Unknown = 0,
        Infantry,
        Cavalry,
        Naval,
        Air,
        Building,
        Special
    }

    public enum ArmorType
    {
        Unknown = 0,
        Unarmored,
        Light,
        Heavy,
        Building
    }

    public enum AttackType
    {
        Unknown = 0,
        None,
        Slash,
        Blunt,
        Arrow,
        Siege
    }

    /// <summary>
    /// Union of the defender classifications addressable by M1 positional bonuses.
    /// </summary>
    public enum BonusTarget
    {
        Unknown = 0,
        Cavalry,
        HeavyArmor,
        Building
    }

    [Serializable]
    public sealed class BonusVsDef
    {
        public BonusTarget Target { get; set; }

        public float Value { get; set; }
    }

    [Serializable]
    public class BaseDef
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public float Hp { get; set; }

        public float Armor { get; set; }

        public UnitType UnitType { get; set; }

        public ArmorType ArmorType { get; set; }
    }

    [Serializable]
    public sealed class BossDef : BaseDef
    {
        public float Atk { get; set; }

        public float Range { get; set; }

        public float AtkSpeed { get; set; }

        public float Pierce { get; set; }

        public AttackType AtkType { get; set; }

        public BonusVsDef[] BonusVs { get; set; } = Array.Empty<BonusVsDef>();

        public string[] Effects { get; set; } = Array.Empty<string>();
    }
}
