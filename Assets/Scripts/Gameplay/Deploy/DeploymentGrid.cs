using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HanziDefend.Data;

namespace HanziDefend.Gameplay.Deploy
{
    /// <summary>
    /// Deterministic, engine-independent deployment grid. The playfield is a fixed rectangle whose
    /// cells start locked; play unlocks them one shape at a time. Placement, merging, shape
    /// availability and packing are all decided against the unlock mask.
    /// </summary>
    public sealed class DeploymentGrid
    {
        public const int MinimumUnitLevel = 1;
        public const int MaximumUnitLevel = 4;

        private static readonly GridCoordinate[] OrthogonalNeighbours =
        {
            new GridCoordinate(1, 0),
            new GridCoordinate(-1, 0),
            new GridCoordinate(0, 1),
            new GridCoordinate(0, -1)
        };

        private readonly Dictionary<string, DeploymentPlacement> placements =
            new Dictionary<string, DeploymentPlacement>(StringComparer.Ordinal);
        private readonly Dictionary<GridCoordinate, string> occupants =
            new Dictionary<GridCoordinate, string>();
        private readonly bool[] unlocked;
        private readonly CardPoolRulesDef cardPoolRules;
        private readonly float baseAnchorRowOffset;

        public DeploymentGrid(
            int width,
            int height,
            IEnumerable<GridCoordinate> initiallyUnlocked,
            DeploymentGridOrientation orientation = DeploymentGridOrientation.ColumnsHorizontal,
            CardPoolRulesDef cardPoolRules = null,
            float baseAnchorRowOffset = -1f)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (initiallyUnlocked == null) throw new ArgumentNullException(nameof(initiallyUnlocked));
            if (orientation != DeploymentGridOrientation.ColumnsHorizontal &&
                orientation != DeploymentGridOrientation.ColumnsVertical)
                throw new ArgumentOutOfRangeException(nameof(orientation));

            Width = width;
            Height = height;
            Orientation = orientation;
            this.cardPoolRules = cardPoolRules;
            this.baseAnchorRowOffset = baseAnchorRowOffset;
            unlocked = new bool[checked(width * height)];

            int count = 0;
            foreach (GridCoordinate cell in initiallyUnlocked)
            {
                if (!Contains(cell))
                    throw new ArgumentOutOfRangeException(
                        nameof(initiallyUnlocked),
                        $"Initially unlocked cell {cell} is outside {width}x{height}.");
                if (!unlocked[Index(cell)]) count++;
                unlocked[Index(cell)] = true;
            }

            if (count == 0)
                throw new ArgumentException(
                    "At least one cell must start unlocked.",
                    nameof(initiallyUnlocked));
            UnlockedCellCount = count;
        }

        /// <summary>Creates the runtime grid exclusively from the selected level and economy rules.</summary>
        public static DeploymentGrid CreateFromConfig(
            GameConfig config,
            string levelId,
            DeploymentGridOrientation orientation = DeploymentGridOrientation.ColumnsHorizontal)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(levelId)) throw new ArgumentException("Level id is required.", nameof(levelId));

            LevelDef level = config.GetLevel(levelId);
            CardPoolRulesDef rules = config.Economy?.CardPool
                ?? throw new InvalidOperationException("economy.cardPool is required for a configured deployment grid.");
            GridUnlockRulesDef unlockRules = config.Economy?.GridUnlock
                ?? throw new InvalidOperationException("economy.gridUnlock is required for a configured deployment grid.");
            return new DeploymentGrid(
                level.GridWidth,
                level.GridHeight,
                EnumerateRect(level.InitialUnlock),
                orientation,
                rules,
                unlockRules.BaseAnchorRowOffset);
        }

        /// <summary>Expands a level's initial unlock rect into cells; the single reader of that def.</summary>
        public static IEnumerable<GridCoordinate> EnumerateRect(GridRectDef rect)
        {
            if (rect == null) throw new ArgumentNullException(nameof(rect));
            var cells = new List<GridCoordinate>(Math.Max(0, rect.Width) * Math.Max(0, rect.Height));
            for (int row = 0; row < rect.Height; row++)
            for (int column = 0; column < rect.Width; column++)
                cells.Add(new GridCoordinate(rect.Col + column, rect.Row + row));
            return cells;
        }

        public int Width { get; }
        public int Height { get; }
        public int CellCount => Width * Height;
        public DeploymentGridOrientation Orientation { get; }
        public int PhysicalColumns => Orientation == DeploymentGridOrientation.ColumnsHorizontal ? Width : Height;
        public int PhysicalRows => Orientation == DeploymentGridOrientation.ColumnsHorizontal ? Height : Width;

        public int UnlockedCellCount { get; private set; }
        public bool IsFullyUnlocked => UnlockedCellCount >= CellCount;

        public IReadOnlyList<DeploymentPlacement> Placements
        {
            get
            {
                var values = new List<DeploymentPlacement>(placements.Values);
                values.Sort((left, right) => string.CompareOrdinal(left.DeploymentId, right.DeploymentId));
                return new ReadOnlyCollection<DeploymentPlacement>(values);
            }
        }

        public GridCoordinate ToPhysical(GridCoordinate logical)
        {
            return Orientation == DeploymentGridOrientation.ColumnsHorizontal
                ? logical
                : new GridCoordinate(logical.Row, logical.Column);
        }

        public GridCoordinate ToLogical(GridCoordinate physical)
        {
            return Orientation == DeploymentGridOrientation.ColumnsHorizontal
                ? physical
                : new GridCoordinate(physical.Row, physical.Column);
        }

        public bool Contains(GridCoordinate cell) =>
            cell.Column >= 0 && cell.Column < Width && cell.Row >= 0 && cell.Row < Height;

        /// <summary>Public cell query for the deployment view. Out-of-bounds cells are locked.</summary>
        public bool IsUnlocked(GridCoordinate cell) => Contains(cell) && unlocked[Index(cell)];

        /// <summary>Row-major copy of the unlock mask, for run-state persistence.</summary>
        public bool[] SaveUnlockMask() => (bool[])unlocked.Clone();

        public IReadOnlyList<GridCoordinate> GetUnlockedCells()
        {
            var cells = new List<GridCoordinate>(UnlockedCellCount);
            for (int row = 0; row < Height; row++)
            for (int column = 0; column < Width; column++)
            {
                var cell = new GridCoordinate(column, row);
                if (unlocked[Index(cell)]) cells.Add(cell);
            }
            return cells.AsReadOnly();
        }

        /// <summary>
        /// Anchors where the footprint fits entirely inside the unlocked region, ignoring who is
        /// standing there. This is the shape-availability question the card pool asks: a shape stays
        /// offerable while the region can geometrically hold it, because dropping onto an occupied
        /// cell is still a legal merge.
        /// </summary>
        public IReadOnlyList<GridCoordinate> GetUnlockedAnchors(UnitFootprint footprint)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            var result = new List<GridCoordinate>();
            for (int row = 0; row < Height; row++)
            for (int column = 0; column < Width; column++)
            {
                var anchor = new GridCoordinate(column, row);
                if (FitsUnlockedArea(footprint, anchor)) result.Add(anchor);
            }
            return result.AsReadOnly();
        }

        /// <summary>
        /// Public shape query for the deployment view and the card pool. A grid created without
        /// economy rules is a geometry-only grid and therefore leaves shape gating to its caller.
        /// </summary>
        public bool IsFootprintUnlocked(UnitFootprint footprint)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            if (cardPoolRules == null) return true;
            if (cardPoolRules.ShapeUnlocks == null)
                throw new InvalidOperationException("economy.cardPool.shapeUnlocks is required.");

            return HasUnlockedPlacement(footprint);
        }

        /// <summary>Geometry-only check: does any anchor hold this whole footprint on unlocked cells?</summary>
        public bool HasUnlockedPlacement(UnitFootprint footprint)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            for (int row = 0; row < Height; row++)
            for (int column = 0; column < Width; column++)
            {
                if (FitsUnlockedArea(footprint, new GridCoordinate(column, row))) return true;
            }
            return false;
        }

        public DeploymentEvaluation Evaluate(DeploymentUnit incoming, GridCoordinate anchor)
        {
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));
            if (placements.ContainsKey(incoming.DeploymentId))
                return Invalid(anchor, DeploymentFailureReason.DuplicateDeploymentId,
                    $"Deployment '{incoming.DeploymentId}' is already on the grid.");
            if (!IsFootprintUnlocked(incoming.Footprint))
                return Invalid(anchor, DeploymentFailureReason.FootprintLocked,
                    $"Footprint {incoming.Footprint.Width}x{incoming.Footprint.Height} has no legal position " +
                    $"in the {UnlockedCellCount} unlocked cells.");

            var blockingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (GridCoordinate offset in incoming.Footprint.OccupiedOffsets)
            {
                GridCoordinate cell = anchor + offset;
                if (!Contains(cell))
                    return Invalid(anchor, DeploymentFailureReason.OutOfBounds,
                        $"Footprint cell {cell} is outside {Width}x{Height}.");
                if (!unlocked[Index(cell)])
                    return Invalid(anchor, DeploymentFailureReason.CellLocked,
                        $"Footprint cell {cell} is not unlocked yet.");
                if (occupants.TryGetValue(cell, out string blockingId)) blockingIds.Add(blockingId);
            }

            if (blockingIds.Count == 0)
                return new DeploymentEvaluation(DeploymentActionKind.Place, DeploymentFailureReason.None,
                    anchor, null, Array.Empty<string>(), string.Empty);

            var ordered = new List<string>(blockingIds);
            ordered.Sort(StringComparer.Ordinal);
            if (ordered.Count == 1)
            {
                DeploymentPlacement target = placements[ordered[0]];
                if (target.UnitId != incoming.UnitId)
                    return Invalid(anchor, DeploymentFailureReason.UnitMismatch,
                        $"Unit '{incoming.UnitId}' cannot merge with '{target.UnitId}'.", ordered);
                if (target.Level != incoming.Level)
                    return Invalid(anchor, DeploymentFailureReason.LevelMismatch,
                        $"Level {incoming.Level} cannot merge with level {target.Level}.", ordered);
                if (!target.Footprint.Equals(incoming.Footprint))
                    return Invalid(anchor, DeploymentFailureReason.FootprintMismatch,
                        $"Unit '{incoming.UnitId}' cannot merge deployments with different footprints.", ordered);
                if (target.Level >= MaximumUnitLevel)
                    return Invalid(anchor, DeploymentFailureReason.MaximumLevelReached,
                        $"Unit '{incoming.UnitId}' is already level {MaximumUnitLevel}.", ordered);
                return new DeploymentEvaluation(DeploymentActionKind.Merge, DeploymentFailureReason.None,
                    anchor, target.DeploymentId, ordered, string.Empty);
            }

            return Invalid(anchor, DeploymentFailureReason.Occupied,
                "The footprint overlaps deployed units that cannot merge.", ordered);
        }

        public DeploymentApplyResult Apply(DeploymentUnit incoming, GridCoordinate anchor)
        {
            DeploymentEvaluation evaluation = Evaluate(incoming, anchor);
            if (!evaluation.IsValid) throw new InvalidOperationException(evaluation.Message);

            if (evaluation.Action == DeploymentActionKind.Merge)
            {
                DeploymentPlacement target = placements[evaluation.MergeTargetDeploymentId];
                DeploymentUnit upgraded = target.Unit.WithLevel(target.Level + 1);
                DeploymentPlacement upgradedPlacement = target.WithUnit(upgraded);
                placements[target.DeploymentId] = upgradedPlacement;
                return new DeploymentApplyResult(DeploymentActionKind.Merge, upgradedPlacement, incoming.DeploymentId);
            }

            var placement = new DeploymentPlacement(incoming, anchor);
            placements.Add(incoming.DeploymentId, placement);
            Occupy(placement);
            return new DeploymentApplyResult(DeploymentActionKind.Place, placement, null);
        }

        public bool TryGetPlacement(string deploymentId, out DeploymentPlacement placement)
        {
            if (deploymentId == null) throw new ArgumentNullException(nameof(deploymentId));
            return placements.TryGetValue(deploymentId, out placement);
        }

        public bool TryGetPlacementAt(GridCoordinate cell, out DeploymentPlacement placement)
        {
            placement = null;
            return occupants.TryGetValue(cell, out string id) && placements.TryGetValue(id, out placement);
        }

        public bool Remove(string deploymentId)
        {
            if (deploymentId == null) throw new ArgumentNullException(nameof(deploymentId));
            if (!placements.TryGetValue(deploymentId, out DeploymentPlacement placement)) return false;
            Vacate(placement);
            placements.Remove(deploymentId);
            return true;
        }

        public DeploymentApplyResult Move(string deploymentId, GridCoordinate anchor)
        {
            if (!placements.TryGetValue(deploymentId, out DeploymentPlacement placement))
                throw new KeyNotFoundException($"Unknown deployment '{deploymentId}'.");
            Vacate(placement);
            placements.Remove(deploymentId);
            try
            {
                return Apply(placement.Unit, anchor);
            }
            catch
            {
                placements.Add(deploymentId, placement);
                Occupy(placement);
                throw;
            }
        }

        /// <summary>Anchors where the footprint fits on unlocked cells that are also free.</summary>
        public IReadOnlyList<GridCoordinate> GetAvailableAnchors(UnitFootprint footprint)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            var result = new List<GridCoordinate>();
            for (int row = 0; row < Height; row++)
            for (int column = 0; column < Width; column++)
            {
                GridCoordinate anchor = new GridCoordinate(column, row);
                bool valid = true;
                foreach (GridCoordinate offset in footprint.OccupiedOffsets)
                {
                    GridCoordinate cell = anchor + offset;
                    if (!IsUnlocked(cell) || occupants.ContainsKey(cell)) { valid = false; break; }
                }
                if (valid) result.Add(anchor);
            }
            return result.AsReadOnly();
        }

        public int CountAvailableAnchors(UnitFootprint footprint) => GetAvailableAnchors(footprint).Count;

        /// <summary>
        /// Checks an unlock card against the three placement rules: it must stay inside the
        /// playfield, unlock at least one new cell, and touch the already-unlocked region so the
        /// mask never grows a disconnected island. Overlapping unlocked cells is allowed.
        /// </summary>
        public UnlockEvaluation EvaluateUnlock(UnlockCard card, GridCoordinate anchor)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));

            var fresh = new List<GridCoordinate>(card.CellCount);
            bool touchesUnlocked = false;
            foreach (GridCoordinate offset in card.Footprint.OccupiedOffsets)
            {
                GridCoordinate cell = anchor + offset;
                if (!Contains(cell))
                    return InvalidUnlock(UnlockFailureReason.OutOfBounds, anchor,
                        $"Unlock cell {cell} is outside {Width}x{Height}.");
                if (unlocked[Index(cell)]) touchesUnlocked = true;
                else fresh.Add(cell);
            }

            if (fresh.Count == 0)
                return InvalidUnlock(UnlockFailureReason.NoNewCells, anchor,
                    "Every cell this unlock card covers is already unlocked.");

            if (!touchesUnlocked)
            {
                for (int index = 0; index < fresh.Count && !touchesUnlocked; index++)
                {
                    for (int step = 0; step < OrthogonalNeighbours.Length; step++)
                    {
                        if (IsUnlocked(fresh[index] + OrthogonalNeighbours[step]))
                        {
                            touchesUnlocked = true;
                            break;
                        }
                    }
                }
            }

            if (!touchesUnlocked)
                return InvalidUnlock(UnlockFailureReason.NotAdjacent, anchor,
                    "An unlock card must share an edge with the already unlocked area.");

            fresh.Sort();
            return new UnlockEvaluation(UnlockFailureReason.None, anchor, fresh.AsReadOnly(), string.Empty);
        }

        public UnlockApplyResult ApplyUnlock(UnlockCard card, GridCoordinate anchor)
        {
            UnlockEvaluation evaluation = EvaluateUnlock(card, anchor);
            if (!evaluation.IsValid) throw new InvalidOperationException(evaluation.Message);

            for (int index = 0; index < evaluation.NewlyUnlockedCells.Count; index++)
            {
                unlocked[Index(evaluation.NewlyUnlockedCells[index])] = true;
            }
            UnlockedCellCount += evaluation.NewlyUnlockedCells.Count;
            return new UnlockApplyResult(card, anchor, evaluation.NewlyUnlockedCells, UnlockedCellCount);
        }

        /// <summary>Legal anchors for one unlock card, for the deployment view's placement hints.</summary>
        public IReadOnlyList<GridCoordinate> GetLegalUnlockAnchors(UnlockCard card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            var result = new List<GridCoordinate>();
            for (int row = 0; row < Height; row++)
            for (int column = 0; column < Width; column++)
            {
                var anchor = new GridCoordinate(column, row);
                if (EvaluateUnlock(card, anchor).IsValid) result.Add(anchor);
            }
            return result.AsReadOnly();
        }

        /// <summary>
        /// Picks the locked cell a minor-stage clear should open: the one nearest the ally base
        /// among cells that already touch the unlocked region, so the mask stays connected.
        /// Ranking is fully deterministic and consumes no RNG — Euclidean distance to the base
        /// anchor first, then the lower row, then the more central column, then the lower column.
        /// Returns false once the whole playfield is unlocked or nothing is reachable.
        /// </summary>
        public bool TryFindAutoUnlockCell(out GridCoordinate cell)
        {
            cell = default;
            if (IsFullyUnlocked) return false;

            float baseColumn = (Width - 1) * 0.5f;
            bool found = false;
            float bestDistance = 0f;
            for (int row = 0; row < Height; row++)
            for (int column = 0; column < Width; column++)
            {
                var candidate = new GridCoordinate(column, row);
                if (unlocked[Index(candidate)] || !TouchesUnlocked(candidate)) continue;

                float dx = column - baseColumn;
                float dy = row - baseAnchorRowOffset;
                float distance = (dx * dx) + (dy * dy);
                if (!found || IsBetterAutoUnlock(candidate, distance, cell, bestDistance, baseColumn))
                {
                    cell = candidate;
                    bestDistance = distance;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Unlocks the automatic per-stage cell. Returns null when nothing is left to open.</summary>
        public UnlockApplyResult AutoUnlockNearestToBase()
        {
            if (!TryFindAutoUnlockCell(out GridCoordinate cell)) return null;

            unlocked[Index(cell)] = true;
            UnlockedCellCount++;
            var opened = new ReadOnlyCollection<GridCoordinate>(new List<GridCoordinate> { cell });
            return new UnlockApplyResult(null, cell, opened, UnlockedCellCount);
        }

        private bool IsBetterAutoUnlock(
            GridCoordinate candidate,
            float candidateDistance,
            GridCoordinate best,
            float bestDistance,
            float baseColumn)
        {
            if (candidateDistance != bestDistance) return candidateDistance < bestDistance;
            if (candidate.Row != best.Row) return candidate.Row < best.Row;

            float candidateOffset = Math.Abs(candidate.Column - baseColumn);
            float bestOffset = Math.Abs(best.Column - baseColumn);
            if (candidateOffset != bestOffset) return candidateOffset < bestOffset;
            return candidate.Column < best.Column;
        }

        private bool TouchesUnlocked(GridCoordinate cell)
        {
            for (int index = 0; index < OrthogonalNeighbours.Length; index++)
            {
                if (IsUnlocked(cell + OrthogonalNeighbours[index])) return true;
            }
            return false;
        }

        private bool FitsUnlockedArea(UnitFootprint footprint, GridCoordinate anchor)
        {
            foreach (GridCoordinate offset in footprint.OccupiedOffsets)
            {
                if (!IsUnlocked(anchor + offset)) return false;
            }
            return true;
        }

        private int Index(GridCoordinate cell) => (cell.Row * Width) + cell.Column;

        private void Occupy(DeploymentPlacement placement)
        {
            foreach (GridCoordinate offset in placement.Footprint.OccupiedOffsets)
                occupants.Add(placement.Anchor + offset, placement.DeploymentId);
        }

        private void Vacate(DeploymentPlacement placement)
        {
            foreach (GridCoordinate offset in placement.Footprint.OccupiedOffsets)
                occupants.Remove(placement.Anchor + offset);
        }

        private static DeploymentEvaluation Invalid(
            GridCoordinate anchor,
            DeploymentFailureReason failure,
            string message,
            IReadOnlyList<string> blocking = null)
        {
            return new DeploymentEvaluation(DeploymentActionKind.Invalid, failure, anchor, null,
                blocking ?? Array.Empty<string>(), message);
        }

        private static UnlockEvaluation InvalidUnlock(
            UnlockFailureReason failure,
            GridCoordinate anchor,
            string message)
        {
            return new UnlockEvaluation(failure, anchor, Array.Empty<GridCoordinate>(), message);
        }
    }
}
