using System;

namespace HanziDefend.Data
{
    [Serializable]
    public sealed class UnitCatalog
    {
        public UnitDef[] Units { get; set; } = Array.Empty<UnitDef>();

        public BossDef[] Bosses { get; set; } = Array.Empty<BossDef>();

        public string[] CommanderIds { get; set; } = Array.Empty<string>();
    }

    [Serializable]
    public sealed class CommanderCatalog
    {
        public CommanderDef[] Commanders { get; set; } = Array.Empty<CommanderDef>();
    }

    [Serializable]
    public sealed class WaveCatalog
    {
        public WaveSetDef[] WaveSets { get; set; } = Array.Empty<WaveSetDef>();
    }

    [Serializable]
    public sealed class BaseCatalog
    {
        public BaseDef Ally { get; set; } = new BaseDef();

        public BaseDef Enemy { get; set; } = new BaseDef();
    }

    [Serializable]
    public sealed class LevelCatalog
    {
        public LevelDef[] Levels { get; set; } = Array.Empty<LevelDef>();

        public BaseCatalog Bases { get; set; } = new BaseCatalog();
    }

    [Serializable]
    public sealed class EffectCatalog
    {
        public EffectDef[] Effects { get; set; } = Array.Empty<EffectDef>();
    }
}
