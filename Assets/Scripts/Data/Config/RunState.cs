using System;

namespace HanziDefend.Data
{
    [Serializable]
    public sealed class DeployedUnitState
    {
        public string UnitId { get; set; } = string.Empty;

        public int Level { get; set; }

        public int Col { get; set; }

        public int Row { get; set; }
    }

    [Serializable]
    public sealed class RunState
    {
        public int Coins { get; set; }

        public DeployedUnitState[] DeployedGrid { get; set; } = Array.Empty<DeployedUnitState>();

        public string[] OwnedEffects { get; set; } = Array.Empty<string>();

        public int RefreshCount { get; set; }

        public int StageIndex { get; set; }

        public uint Seed { get; set; }

        public RngState RngState { get; set; }
    }
}
