using System;

namespace HanziDefend.Data
{
    [Serializable]
    public sealed class DeployedUnitState
    {
        public string DeploymentId { get; set; } = string.Empty;

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

        /// <summary>
        /// Paid refreshes taken in the <b>current minor stage</b>. WO-F1 §D reset it per stage:
        /// while it accumulated across the whole run the price ran 15/20/25/30… into stage 5, so the
        /// later stages — the ones with the most grid to fill — could afford the fewest hands.
        /// </summary>
        public int RefreshCount { get; set; }

        /// <summary>Free hands already taken this minor stage; the curve in economy.json caps it.</summary>
        public int FreeOffersUsed { get; set; }

        public int StageIndex { get; set; }

        /// <summary>Playfield width. Fixed for a major stage; only <see cref="UnlockedCells"/> grows.</summary>
        public int GridWidth { get; set; }

        public int GridHeight { get; set; }

        /// <summary>
        /// Row-major unlock mask of length <c>GridWidth * GridHeight</c>. It is inherited across
        /// minor stages and rebuilt from the level's initial unlock rect on a major-stage reset.
        /// </summary>
        public bool[] UnlockedCells { get; set; } = Array.Empty<bool>();

        /// <summary>Unlock cards bought with coins during this major stage; drives the price curve.</summary>
        public int UnlockPurchaseCount { get; set; }

        public uint Seed { get; set; }

        /// <summary>
        /// True after an equipment unit card has appeared in any offer during
        /// the current major run. The pre-boss guarantee consumes this state.
        /// </summary>
        public bool HasOfferedGuaranteedMachineryCard { get; set; }

        /// <summary>
        /// Current deterministic states for battle, card drawing and settlement.
        /// New run-state persistence should prefer this aggregate snapshot.
        /// </summary>
        public RngStreamsState RngStreamsState { get; set; }

        /// <summary>
        /// Legacy single-stream snapshot retained for backwards compatibility.
        /// </summary>
        public RngState RngState { get; set; }
    }
}
