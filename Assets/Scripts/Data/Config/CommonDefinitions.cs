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

    [Serializable]
    public class BaseDef
    {
        public float Hp { get; set; }

        public float Armor { get; set; }
    }

    [Serializable]
    public sealed class BossDef : BaseDef
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public float Atk { get; set; }

        public float Range { get; set; }

        public float AtkSpeed { get; set; }

        public float Pierce { get; set; }

        public string[] Effects { get; set; } = Array.Empty<string>();
    }
}
