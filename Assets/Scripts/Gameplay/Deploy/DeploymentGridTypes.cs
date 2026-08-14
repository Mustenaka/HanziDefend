using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HanziDefend.Data;

namespace HanziDefend.Gameplay.Deploy
{
    /// <summary>
    /// A logical grid coordinate. Columns increase toward the grid's trailing expansion side;
    /// rows are independent of rendering orientation.
    /// </summary>
    public readonly struct GridCoordinate : IEquatable<GridCoordinate>, IComparable<GridCoordinate>
    {
        public GridCoordinate(int column, int row)
        {
            Column = column;
            Row = row;
        }

        public int Column { get; }

        public int Row { get; }

        /// <summary>Provides deterministic row-major ordering.</summary>
        public int CompareTo(GridCoordinate other)
        {
            int rowComparison = Row.CompareTo(other.Row);
            return rowComparison != 0 ? rowComparison : Column.CompareTo(other.Column);
        }

        public bool Equals(GridCoordinate other)
        {
            return Column == other.Column && Row == other.Row;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCoordinate other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Column * 397) ^ Row;
            }
        }

        public override string ToString()
        {
            return $"({Column},{Row})";
        }

        public static GridCoordinate operator +(GridCoordinate left, GridCoordinate right)
        {
            return new GridCoordinate(left.Column + right.Column, left.Row + right.Row);
        }

        public static bool operator ==(GridCoordinate left, GridCoordinate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GridCoordinate left, GridCoordinate right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Immutable occupied offsets relative to a unit anchor. Offsets may be negative and may
    /// contain holes, so future non-rectangular shapes do not require another grid contract.
    /// </summary>
    public sealed class UnitFootprint : IEquatable<UnitFootprint>
    {
        private readonly GridCoordinate[] offsets;
        private readonly ReadOnlyCollection<GridCoordinate> readOnlyOffsets;

        public UnitFootprint(IEnumerable<GridCoordinate> occupiedOffsets)
        {
            if (occupiedOffsets == null)
            {
                throw new ArgumentNullException(nameof(occupiedOffsets));
            }

            var collected = new List<GridCoordinate>();
            var unique = new HashSet<GridCoordinate>();
            foreach (GridCoordinate offset in occupiedOffsets)
            {
                if (!unique.Add(offset))
                {
                    throw new ArgumentException(
                        $"Footprint contains duplicate occupied offset {offset}.",
                        nameof(occupiedOffsets));
                }

                collected.Add(offset);
            }

            if (collected.Count == 0)
            {
                throw new ArgumentException(
                    "A footprint must contain at least one occupied offset.",
                    nameof(occupiedOffsets));
            }

            collected.Sort();
            offsets = collected.ToArray();
            readOnlyOffsets = Array.AsReadOnly(offsets);

            int minColumn = offsets[0].Column;
            int maxColumn = offsets[0].Column;
            int minRow = offsets[0].Row;
            int maxRow = offsets[0].Row;
            for (int index = 1; index < offsets.Length; index++)
            {
                GridCoordinate offset = offsets[index];
                minColumn = Math.Min(minColumn, offset.Column);
                maxColumn = Math.Max(maxColumn, offset.Column);
                minRow = Math.Min(minRow, offset.Row);
                maxRow = Math.Max(maxRow, offset.Row);
            }

            MinColumnOffset = minColumn;
            MaxColumnOffset = maxColumn;
            MinRowOffset = minRow;
            MaxRowOffset = maxRow;
        }

        public IReadOnlyList<GridCoordinate> OccupiedOffsets => readOnlyOffsets;

        public int OccupiedCellCount => offsets.Length;

        public int MinColumnOffset { get; }

        public int MaxColumnOffset { get; }

        public int MinRowOffset { get; }

        public int MaxRowOffset { get; }

        public int Width => MaxColumnOffset - MinColumnOffset + 1;

        public int Height => MaxRowOffset - MinRowOffset + 1;

        public static UnitFootprint Rectangle(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Footprint width must be positive.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Footprint height must be positive.");
            }

            var occupied = new GridCoordinate[checked(width * height)];
            int index = 0;
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    occupied[index++] = new GridCoordinate(column, row);
                }
            }

            return new UnitFootprint(occupied);
        }

        /// <summary>Builds logical occupancy solely from the data contract; unit ids are never special-cased.</summary>
        public static UnitFootprint FromDefinition(UnitDef definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (definition.GridW <= 0 || definition.GridH <= 0)
            {
                throw new ArgumentException(
                    $"Unit '{definition.Id}' has invalid footprint bounds {definition.GridW}x{definition.GridH}.",
                    nameof(definition));
            }

            if (definition.Footprint == UnitFootprintShape.Rectangle)
            {
                return Rectangle(definition.GridW, definition.GridH);
            }

            bool missingUpperRight = definition.Footprint == UnitFootprintShape.MissingUpperRight;
            bool missingLowerLeft = definition.Footprint == UnitFootprintShape.MissingLowerLeft;
            if (!missingUpperRight && !missingLowerLeft)
            {
                throw new ArgumentException(
                    $"Unit '{definition.Id}' has unsupported footprint '{definition.Footprint}'.",
                    nameof(definition));
            }

            if (definition.GridW != 2 || definition.GridH != 2)
            {
                throw new ArgumentException(
                    $"Unit '{definition.Id}' footprint '{definition.Footprint}' requires a 2x2 bounding box.",
                    nameof(definition));
            }

            var occupied = new List<GridCoordinate>(3);
            for (int row = 0; row < definition.GridH; row++)
            {
                for (int column = 0; column < definition.GridW; column++)
                {
                    bool isMissingCell = (missingUpperRight && column == definition.GridW - 1 && row == definition.GridH - 1)
                                         || (missingLowerLeft && column == 0 && row == 0);
                    if (!isMissingCell)
                    {
                        occupied.Add(new GridCoordinate(column, row));
                    }
                }
            }

            return new UnitFootprint(occupied);
        }

        public bool Equals(UnitFootprint other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null || offsets.Length != other.offsets.Length)
            {
                return false;
            }

            for (int index = 0; index < offsets.Length; index++)
            {
                if (offsets[index] != other.offsets[index])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UnitFootprint);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                for (int index = 0; index < offsets.Length; index++)
                {
                    hash = hash * 31 + offsets[index].GetHashCode();
                }

                return hash;
            }
        }
    }

    public enum DeploymentGridOrientation
    {
        ColumnsHorizontal,
        ColumnsVertical
    }

    /// <summary>The four corners a 2x2 unlock card can be notched at.</summary>
    public enum GridCorner
    {
        LowerLeft,
        LowerRight,
        UpperLeft,
        UpperRight
    }

    /// <summary>The seven cell-unlock card shapes. Dimensions read width by height.</summary>
    public enum UnlockCardShape
    {
        OneByOne,
        TwoByOne,
        OneByTwo,
        TwoByTwo,
        TwoByTwoNotched,
        ThreeByOne,
        OneByThree
    }

    public enum DeploymentActionKind
    {
        Invalid,
        Place,
        Merge
    }

    public enum DeploymentFailureReason
    {
        None,
        OutOfBounds,
        FootprintLocked,
        CellLocked,
        Occupied,
        DuplicateDeploymentId,
        UnknownDeployment,
        SameDeployment,
        UnitMismatch,
        LevelMismatch,
        FootprintMismatch,
        MaximumLevelReached
    }

    /// <summary>A uniquely addressable unit which may be placed on the deployment grid.</summary>
    public sealed class DeploymentUnit
    {
        public DeploymentUnit(string deploymentId, string unitId, int level, UnitFootprint footprint)
        {
            if (string.IsNullOrWhiteSpace(deploymentId))
            {
                throw new ArgumentException("Deployment id is required.", nameof(deploymentId));
            }

            if (string.IsNullOrWhiteSpace(unitId))
            {
                throw new ArgumentException("Unit id is required.", nameof(unitId));
            }

            if (level < DeploymentGrid.MinimumUnitLevel || level > DeploymentGrid.MaximumUnitLevel)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(level),
                    $"Unit level must be in [{DeploymentGrid.MinimumUnitLevel},{DeploymentGrid.MaximumUnitLevel}].");
            }

            DeploymentId = deploymentId;
            UnitId = unitId;
            Level = level;
            Footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        }

        public string DeploymentId { get; }

        public string UnitId { get; }

        public int Level { get; }

        /// <summary>The four deployment levels map directly to Green, Blue, Purple and Gold.</summary>
        public UnitTier Tier => (UnitTier)Level;

        public UnitFootprint Footprint { get; }

        internal DeploymentUnit WithLevel(int level)
        {
            return new DeploymentUnit(DeploymentId, UnitId, level, Footprint);
        }
    }

    /// <summary>Immutable placement snapshot returned by every public query.</summary>
    public sealed class DeploymentPlacement
    {
        internal DeploymentPlacement(DeploymentUnit unit, GridCoordinate anchor)
        {
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
            Anchor = anchor;
        }

        public DeploymentUnit Unit { get; }

        public string DeploymentId => Unit.DeploymentId;

        public string UnitId => Unit.UnitId;

        public int Level => Unit.Level;

        public UnitFootprint Footprint => Unit.Footprint;

        public GridCoordinate Anchor { get; }

        internal DeploymentPlacement At(GridCoordinate anchor)
        {
            return new DeploymentPlacement(Unit, anchor);
        }

        internal DeploymentPlacement WithUnit(DeploymentUnit unit)
        {
            return new DeploymentPlacement(unit, Anchor);
        }
    }

    public sealed class DeploymentEvaluation
    {
        internal DeploymentEvaluation(
            DeploymentActionKind action,
            DeploymentFailureReason failure,
            GridCoordinate anchor,
            string mergeTargetDeploymentId,
            IReadOnlyList<string> blockingDeploymentIds,
            string message)
        {
            Action = action;
            Failure = failure;
            Anchor = anchor;
            MergeTargetDeploymentId = mergeTargetDeploymentId;
            BlockingDeploymentIds = blockingDeploymentIds ?? Array.Empty<string>();
            Message = message ?? string.Empty;
        }

        public DeploymentActionKind Action { get; }

        public DeploymentFailureReason Failure { get; }

        public GridCoordinate Anchor { get; }

        public string MergeTargetDeploymentId { get; }

        public IReadOnlyList<string> BlockingDeploymentIds { get; }

        public string Message { get; }

        public bool IsValid => Action != DeploymentActionKind.Invalid;
    }

    public sealed class DeploymentApplyResult
    {
        internal DeploymentApplyResult(
            DeploymentActionKind action,
            DeploymentPlacement placement,
            string consumedDeploymentId)
        {
            Action = action;
            Placement = placement ?? throw new ArgumentNullException(nameof(placement));
            ConsumedDeploymentId = consumedDeploymentId;
        }

        public DeploymentActionKind Action { get; }

        /// <summary>The placed unit, or the upgraded merge target.</summary>
        public DeploymentPlacement Placement { get; }

        /// <summary>For Merge, the incoming/source deployment id consumed by the merge.</summary>
        public string ConsumedDeploymentId { get; }
    }

    public enum UnlockFailureReason
    {
        None,

        /// <summary>Part of the card falls outside the fixed playfield.</summary>
        OutOfBounds,

        /// <summary>Every covered cell is already unlocked, so the card would unlock nothing.</summary>
        NoNewCells,

        /// <summary>The card touches no unlocked cell, which would leave a disconnected island.</summary>
        NotAdjacent
    }

    /// <summary>
    /// One minted unlock card: an immutable shape plus the cells it covers relative to its anchor.
    /// Cards are minted by the RNG-backed draw and then placed, so the shape cannot drift in between.
    /// </summary>
    public sealed class UnlockCard : IEquatable<UnlockCard>
    {
        internal UnlockCard(UnlockCardShape shape, GridCorner notch, UnitFootprint footprint)
        {
            Shape = shape;
            Notch = notch;
            Footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        }

        public UnlockCardShape Shape { get; }

        /// <summary>Meaningful only for <see cref="UnlockCardShape.TwoByTwoNotched"/>.</summary>
        public GridCorner Notch { get; }

        public UnitFootprint Footprint { get; }

        public int CellCount => Footprint.OccupiedCellCount;

        /// <summary>Stable, data-free content id so views and saves can name the card.</summary>
        public string ContentId => Shape == UnlockCardShape.TwoByTwoNotched
            ? $"unlock_{Shape}_{Notch}"
            : $"unlock_{Shape}";

        public bool Equals(UnlockCard other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other != null
                   && Shape == other.Shape
                   && (Shape != UnlockCardShape.TwoByTwoNotched || Notch == other.Notch);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UnlockCard);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int notch = Shape == UnlockCardShape.TwoByTwoNotched ? (int)Notch : -1;
                return ((int)Shape * 397) ^ notch;
            }
        }

        public override string ToString()
        {
            return ContentId;
        }
    }

    public sealed class UnlockEvaluation
    {
        internal UnlockEvaluation(
            UnlockFailureReason failure,
            GridCoordinate anchor,
            IReadOnlyList<GridCoordinate> newlyUnlockedCells,
            string message)
        {
            Failure = failure;
            Anchor = anchor;
            NewlyUnlockedCells = newlyUnlockedCells ?? Array.Empty<GridCoordinate>();
            Message = message ?? string.Empty;
        }

        public UnlockFailureReason Failure { get; }

        public GridCoordinate Anchor { get; }

        /// <summary>Cells this placement would flip from locked to unlocked, in row-major order.</summary>
        public IReadOnlyList<GridCoordinate> NewlyUnlockedCells { get; }

        public string Message { get; }

        public bool IsValid => Failure == UnlockFailureReason.None;
    }

    public sealed class UnlockApplyResult
    {
        internal UnlockApplyResult(
            UnlockCard card,
            GridCoordinate anchor,
            IReadOnlyList<GridCoordinate> newlyUnlockedCells,
            int unlockedCellCount)
        {
            Card = card;
            Anchor = anchor;
            NewlyUnlockedCells = newlyUnlockedCells ?? Array.Empty<GridCoordinate>();
            UnlockedCellCount = unlockedCellCount;
        }

        /// <summary>Null for the automatic per-stage unlock, which is not driven by a card.</summary>
        public UnlockCard Card { get; }

        public GridCoordinate Anchor { get; }

        public IReadOnlyList<GridCoordinate> NewlyUnlockedCells { get; }

        /// <summary>Total unlocked cells after the change.</summary>
        public int UnlockedCellCount { get; }
    }
}
