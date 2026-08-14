using System;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode
{
    public sealed class DeploymentGridTests
    {
        private static readonly UnitFootprint OneByOne = UnitFootprint.Rectangle(1, 1);
        private static readonly UnitFootprint TwoByOne = UnitFootprint.Rectangle(2, 1);
        private static readonly UnitFootprint OneByTwo = UnitFootprint.Rectangle(1, 2);
        private static readonly UnitFootprint TwoByTwo = UnitFootprint.Rectangle(2, 2);
        private static readonly UnitFootprint ThreeByOne = UnitFootprint.Rectangle(3, 1);

        private static readonly UnitFootprint NucFootprint = new UnitFootprint(new[]
        {
            Cell(0, 0),
            Cell(1, 0),
            Cell(0, 1)
        });

        private static readonly UnitFootprint ChcFootprint = new UnitFootprint(new[]
        {
            Cell(1, 0),
            Cell(0, 1),
            Cell(1, 1)
        });

        // ---------------------------------------------------------------- field and initial mask

        [Test]
        public void CreateFromConfig_LevelOneIsSevenBySevenWithTheCentreThreeByThreeUnlocked()
        {
            DeploymentGrid grid = DeploymentGrid.CreateFromConfig(GameConfig.Load(), "level_1_1");

            Assert.That(grid.Width, Is.EqualTo(7));
            Assert.That(grid.Height, Is.EqualTo(7));
            Assert.That(grid.CellCount, Is.EqualTo(49));
            Assert.That(grid.UnlockedCellCount, Is.EqualTo(9));
            Assert.That(grid.IsFullyUnlocked, Is.False);
            Assert.That(grid.GetUnlockedCells(), Is.EqualTo(CentreCells()));
        }

        [Test]
        public void CreateFromConfig_EveryCellOutsideTheCentreThreeByThreeStartsLocked()
        {
            DeploymentGrid grid = DeploymentGrid.CreateFromConfig(GameConfig.Load(), "level_1_1");
            var centre = new HashSet<GridCoordinate>(CentreCells());

            for (int row = 0; row < 7; row++)
            for (int column = 0; column < 7; column++)
            {
                GridCoordinate cell = Cell(column, row);
                Assert.That(grid.IsUnlocked(cell), Is.EqualTo(centre.Contains(cell)), $"cell {cell}");
            }

            Assert.That(grid.IsUnlocked(Cell(-1, 3)), Is.False, "out of bounds reads as locked");
            Assert.That(grid.IsUnlocked(Cell(7, 3)), Is.False, "out of bounds reads as locked");
        }

        [TestCase(DeploymentGridOrientation.ColumnsHorizontal)]
        [TestCase(DeploymentGridOrientation.ColumnsVertical)]
        public void CreateFromConfig_EveryLevelSharesTheSameFixedFieldAndInitialMask(
            DeploymentGridOrientation orientation)
        {
            GameConfig config = GameConfig.Load();

            foreach (LevelDef level in config.Levels)
            {
                DeploymentGrid grid = DeploymentGrid.CreateFromConfig(config, level.Id, orientation);
                Assert.That(grid.Width, Is.EqualTo(7), level.Id);
                Assert.That(grid.Height, Is.EqualTo(7), level.Id);
                Assert.That(grid.UnlockedCellCount, Is.EqualTo(9), level.Id);
                Assert.That(grid.PhysicalColumns, Is.EqualTo(7), level.Id);
                Assert.That(grid.PhysicalRows, Is.EqualTo(7), level.Id);
            }
        }

        // ----------------------------------------------------------------------- packing on a mask

        [TestCase(1, 1, 9)]
        [TestCase(2, 1, 6)]
        [TestCase(1, 2, 6)]
        [TestCase(2, 2, 4)]
        [TestCase(3, 1, 3)]
        [TestCase(1, 3, 3)]
        public void StartingMask_RectangularFootprintAnchorCountMatchesM104Table(
            int width,
            int height,
            int expected)
        {
            Assert.That(
                NewGrid().CountAvailableAnchors(UnitFootprint.Rectangle(width, height)),
                Is.EqualTo(expected));
        }

        [Test]
        public void StartingMask_BothLShapesHaveFourAnchorsLikeTheirBoundingBox()
        {
            DeploymentGrid grid = NewGrid();

            Assert.That(grid.CountAvailableAnchors(NucFootprint), Is.EqualTo(4));
            Assert.That(grid.CountAvailableAnchors(ChcFootprint), Is.EqualTo(4));
        }

        [Test]
        public void Packing_ThreeOneByOneAndOneTwoByTwo_FitsSevenOfNineUnlockedCells()
        {
            DeploymentGrid grid = NewGrid();

            grid.Apply(Unit("square", TwoByTwo), Cell(2, 2));
            grid.Apply(Unit("one-a", OneByOne), Cell(4, 2));
            grid.Apply(Unit("one-b", OneByOne), Cell(4, 3));
            grid.Apply(Unit("one-c", OneByOne), Cell(4, 4));

            AssertPacking(grid, expectedPlacements: 4, expectedOccupiedCells: 7);
        }

        [Test]
        public void Packing_TwoByTwoOneByTwoAndTwoByOne_FitsEightOfNineUnlockedCells()
        {
            DeploymentGrid grid = NewGrid();

            grid.Apply(Unit("square", TwoByTwo), Cell(2, 2));
            grid.Apply(Unit("vertical", OneByTwo), Cell(4, 2));
            grid.Apply(Unit("horizontal", TwoByOne), Cell(2, 4));

            AssertPacking(grid, expectedPlacements: 3, expectedOccupiedCells: 8);
        }

        [Test]
        public void Packing_TwoTwoByTwoFootprints_StillCannotCoexistInsideTheStartingMask()
        {
            IReadOnlyList<GridCoordinate> possibleFirstAnchors = NewGrid().GetAvailableAnchors(TwoByTwo);

            Assert.That(possibleFirstAnchors, Has.Count.EqualTo(4));
            foreach (GridCoordinate firstAnchor in possibleFirstAnchors)
            {
                DeploymentGrid grid = NewGrid();
                grid.Apply(Unit("first", TwoByTwo), firstAnchor);

                Assert.That(
                    grid.CountAvailableAnchors(TwoByTwo),
                    Is.Zero,
                    $"A second 2x2 unexpectedly fit after placing the first at {firstAnchor}.");
            }
        }

        [Test]
        public void Packing_UnlockingOneFlankingColumnLetsTwoTwoByTwoFootprintsCoexist()
        {
            DeploymentGrid grid = NewGrid();
            grid.ApplyUnlock(UnlockCardFactory.Create(UnlockCardShape.OneByThree), Cell(1, 2));

            grid.Apply(Unit("first", TwoByTwo), Cell(1, 2));

            Assert.That(grid.UnlockedCellCount, Is.EqualTo(12));
            Assert.That(grid.CountAvailableAnchors(TwoByTwo), Is.EqualTo(2));
            Assert.That(grid.Apply(Unit("second", TwoByTwo), Cell(3, 2)).Action,
                Is.EqualTo(DeploymentActionKind.Place));
        }

        [Test]
        public void Nuc_MissingUpperRightCellAcceptsOneByOne()
        {
            DeploymentGrid grid = NewGrid();
            grid.Apply(Unit("nuc", NucFootprint), Cell(2, 2));

            DeploymentApplyResult result = grid.Apply(Unit("filler", OneByOne), Cell(3, 3));

            Assert.That(NucFootprint.OccupiedCellCount, Is.EqualTo(3));
            Assert.That(result.Action, Is.EqualTo(DeploymentActionKind.Place));
            Assert.That(grid.TryGetPlacementAt(Cell(3, 3), out DeploymentPlacement filler), Is.True);
            Assert.That(filler.DeploymentId, Is.EqualTo("filler"));
        }

        [Test]
        public void Chc_MissingLowerLeftCellAcceptsOneByOne()
        {
            DeploymentGrid grid = NewGrid();
            grid.Apply(Unit("chc", ChcFootprint), Cell(2, 2));

            DeploymentApplyResult result = grid.Apply(Unit("filler", OneByOne), Cell(2, 2));

            Assert.That(ChcFootprint.OccupiedCellCount, Is.EqualTo(3));
            Assert.That(result.Action, Is.EqualTo(DeploymentActionKind.Place));
            Assert.That(grid.TryGetPlacementAt(Cell(2, 2), out DeploymentPlacement filler), Is.True);
            Assert.That(filler.DeploymentId, Is.EqualTo("filler"));
        }

        [Test]
        public void RealGameConfig_NucAndChcProduceTheirLockedLFootprintsWithoutIdRules()
        {
            GameConfig config = GameConfig.Load();
            UnitFootprint nuc = UnitFootprint.FromDefinition(config.GetUnit("nuc"));
            UnitFootprint chc = UnitFootprint.FromDefinition(config.GetUnit("chc"));

            Assert.That(config.GetUnit("nuc").Footprint, Is.EqualTo(UnitFootprintShape.MissingUpperRight));
            Assert.That(config.GetUnit("chc").Footprint, Is.EqualTo(UnitFootprintShape.MissingLowerLeft));
            Assert.That(nuc.OccupiedOffsets, Is.EquivalentTo(new[] { Cell(0, 0), Cell(1, 0), Cell(0, 1) }));
            Assert.That(chc.OccupiedOffsets, Is.EquivalentTo(new[] { Cell(1, 0), Cell(0, 1), Cell(1, 1) }));
        }

        // --------------------------------------------------------------- placement against the mask

        [TestCase(4, 2)]
        [TestCase(2, 4)]
        [TestCase(1, 2)]
        [TestCase(2, 1)]
        public void Evaluate_FootprintCrossingTheUnlockedBoundary_IsRejectedAsLockedCell(int column, int row)
        {
            DeploymentEvaluation evaluation = NewGrid().Evaluate(Unit("square", TwoByTwo), Cell(column, row));

            Assert.That(evaluation.IsValid, Is.False);
            Assert.That(evaluation.Action, Is.EqualTo(DeploymentActionKind.Invalid));
            Assert.That(evaluation.Failure, Is.EqualTo(DeploymentFailureReason.CellLocked));
            Assert.That(evaluation.Message, Is.Not.Empty);
        }

        [TestCase(6, 3)]
        [TestCase(3, 6)]
        [TestCase(-1, 3)]
        [TestCase(3, -1)]
        public void Evaluate_FootprintCrossingTheFieldBoundary_IsRejectedAsOutOfBounds(int column, int row)
        {
            DeploymentEvaluation evaluation = FullyUnlockedGrid()
                .Evaluate(Unit("square", TwoByTwo), Cell(column, row));

            Assert.That(evaluation.IsValid, Is.False);
            Assert.That(evaluation.Failure, Is.EqualTo(DeploymentFailureReason.OutOfBounds));
        }

        [Test]
        public void Apply_InvalidPlacementThrowsWithoutMutatingGrid()
        {
            DeploymentGrid grid = NewGrid();

            Assert.Throws<InvalidOperationException>(() => grid.Apply(Unit("square", TwoByTwo), Cell(4, 4)));
            Assert.That(grid.Placements, Is.Empty);
        }

        [Test]
        public void ConfiguredGrid_ShapeWithNoLegalPositionAnywhereReportsFootprintLocked()
        {
            DeploymentGrid grid = ConfiguredGrid(VerticalStripCells());

            DeploymentEvaluation evaluation = grid.Evaluate(Unit("locked", TwoByTwo), Cell(3, 2));

            Assert.That(evaluation.IsValid, Is.False);
            Assert.That(evaluation.Failure, Is.EqualTo(DeploymentFailureReason.FootprintLocked));
            Assert.That(grid.Placements, Is.Empty);
        }

        [Test]
        public void ConfiguredGrid_IsFootprintUnlockedTracksTheMaskInsteadOfAColumnThreshold()
        {
            DeploymentGrid strip = ConfiguredGrid(VerticalStripCells());

            Assert.That(strip.IsFootprintUnlocked(OneByOne), Is.True);
            Assert.That(strip.IsFootprintUnlocked(OneByTwo), Is.True);
            Assert.That(strip.IsFootprintUnlocked(TwoByOne), Is.False);
            Assert.That(strip.IsFootprintUnlocked(TwoByTwo), Is.False);
            Assert.That(strip.IsFootprintUnlocked(ThreeByOne), Is.False);
            Assert.That(strip.IsFootprintUnlocked(NucFootprint), Is.False);

            strip.ApplyUnlock(UnlockCardFactory.Create(UnlockCardShape.OneByThree), Cell(2, 2));

            Assert.That(strip.IsFootprintUnlocked(TwoByOne), Is.True);
            Assert.That(strip.IsFootprintUnlocked(TwoByTwo), Is.True);
            Assert.That(strip.IsFootprintUnlocked(NucFootprint), Is.True);
            Assert.That(strip.IsFootprintUnlocked(ThreeByOne), Is.False, "a 3x1 still needs a third column");
        }

        [Test]
        public void HasUnlockedPlacement_IgnoresOccupancySoAFullGridStillOffersItsShapes()
        {
            DeploymentGrid grid = ConfiguredGrid(CentreCells());
            int next = 0;
            foreach (GridCoordinate cell in CentreCells())
            {
                grid.Apply(Unit($"filler-{next++}", OneByOne), cell);
            }

            Assert.That(grid.CountAvailableAnchors(OneByOne), Is.Zero);
            Assert.That(grid.HasUnlockedPlacement(OneByOne), Is.True);
            Assert.That(grid.HasUnlockedPlacement(TwoByTwo), Is.True);
        }

        // --------------------------------------------------------------------------- unlock cards

        [TestCase(UnlockCardShape.OneByOne, 1)]
        [TestCase(UnlockCardShape.TwoByOne, 2)]
        [TestCase(UnlockCardShape.OneByTwo, 2)]
        [TestCase(UnlockCardShape.TwoByTwo, 4)]
        [TestCase(UnlockCardShape.TwoByTwoNotched, 3)]
        [TestCase(UnlockCardShape.ThreeByOne, 3)]
        [TestCase(UnlockCardShape.OneByThree, 3)]
        public void UnlockCard_SevenShapesCoverTheirDeclaredCellCount(UnlockCardShape shape, int expectedCells)
        {
            Assert.That(UnlockCardFactory.Shapes, Has.Count.EqualTo(7));
            Assert.That(UnlockCardFactory.Create(shape).CellCount, Is.EqualTo(expectedCells));
        }

        [TestCase(UnlockCardShape.OneByOne, 3, 1)]
        [TestCase(UnlockCardShape.TwoByOne, 2, 1)]
        [TestCase(UnlockCardShape.OneByTwo, 3, 0)]
        [TestCase(UnlockCardShape.TwoByTwo, 2, 0)]
        [TestCase(UnlockCardShape.ThreeByOne, 2, 1)]
        [TestCase(UnlockCardShape.OneByThree, 1, 2)]
        public void EvaluateUnlock_ShapeTouchingTheUnlockedAreaIsLegal(
            UnlockCardShape shape,
            int column,
            int row)
        {
            DeploymentGrid grid = NewGrid();
            UnlockCard card = UnlockCardFactory.Create(shape);

            UnlockEvaluation evaluation = grid.EvaluateUnlock(card, Cell(column, row));

            Assert.That(evaluation.IsValid, Is.True, evaluation.Message);
            Assert.That(evaluation.Failure, Is.EqualTo(UnlockFailureReason.None));
            Assert.That(evaluation.NewlyUnlockedCells, Is.Not.Empty);
        }

        [TestCase(UnlockCardShape.OneByOne, 7, 3)]
        [TestCase(UnlockCardShape.TwoByOne, 6, 3)]
        [TestCase(UnlockCardShape.OneByTwo, 3, 6)]
        [TestCase(UnlockCardShape.TwoByTwo, 6, 3)]
        [TestCase(UnlockCardShape.ThreeByOne, 5, 3)]
        [TestCase(UnlockCardShape.OneByThree, 3, 5)]
        public void EvaluateUnlock_ShapeLeavingTheSevenBySevenFieldIsRejected(
            UnlockCardShape shape,
            int column,
            int row)
        {
            UnlockEvaluation evaluation = FullyLockedFrontierGrid()
                .EvaluateUnlock(UnlockCardFactory.Create(shape), Cell(column, row));

            Assert.That(evaluation.IsValid, Is.False);
            Assert.That(evaluation.Failure, Is.EqualTo(UnlockFailureReason.OutOfBounds));
            Assert.That(evaluation.NewlyUnlockedCells, Is.Empty);
        }

        [TestCase(UnlockCardShape.OneByOne)]
        [TestCase(UnlockCardShape.TwoByOne)]
        [TestCase(UnlockCardShape.OneByTwo)]
        [TestCase(UnlockCardShape.TwoByTwo)]
        [TestCase(UnlockCardShape.ThreeByOne)]
        [TestCase(UnlockCardShape.OneByThree)]
        public void EvaluateUnlock_ShapeDetachedFromTheUnlockedAreaIsRejectedAsAnIsland(UnlockCardShape shape)
        {
            UnlockEvaluation evaluation = NewGrid()
                .EvaluateUnlock(UnlockCardFactory.Create(shape), Cell(0, 0));

            Assert.That(evaluation.IsValid, Is.False);
            Assert.That(evaluation.Failure, Is.EqualTo(UnlockFailureReason.NotAdjacent));
            Assert.That(evaluation.NewlyUnlockedCells, Is.Empty);
        }

        // Anchored at (2,1) the card straddles the frontier row, so how much of it is new depends
        // on which corner the notch removed. All four directions must still be legal.
        [TestCase(GridCorner.LowerLeft, 1)]
        [TestCase(GridCorner.LowerRight, 1)]
        [TestCase(GridCorner.UpperLeft, 2)]
        [TestCase(GridCorner.UpperRight, 2)]
        public void EvaluateUnlock_NotchedTwoByTwoIsLegalInAllFourNotchDirections(
            GridCorner notch,
            int expectedNewCells)
        {
            DeploymentGrid grid = NewGrid();
            UnlockCard card = UnlockCardFactory.Create(UnlockCardShape.TwoByTwoNotched, notch);

            UnlockEvaluation evaluation = grid.EvaluateUnlock(card, Cell(2, 1));

            Assert.That(card.CellCount, Is.EqualTo(3));
            Assert.That(card.Notch, Is.EqualTo(notch));
            Assert.That(evaluation.IsValid, Is.True, evaluation.Message);
            Assert.That(evaluation.NewlyUnlockedCells, Has.Count.EqualTo(expectedNewCells));
            Assert.That(
                evaluation.NewlyUnlockedCells.All(cell => cell.Row == 1 && !grid.IsUnlocked(cell)),
                Is.True,
                "only previously locked frontier cells may be reported as newly unlocked");
        }

        [TestCase(GridCorner.LowerLeft)]
        [TestCase(GridCorner.LowerRight)]
        [TestCase(GridCorner.UpperLeft)]
        [TestCase(GridCorner.UpperRight)]
        public void EvaluateUnlock_NotchedTwoByTwoRejectsOutOfBoundsAndIslandsInAllFourDirections(
            GridCorner notch)
        {
            UnlockCard card = UnlockCardFactory.Create(UnlockCardShape.TwoByTwoNotched, notch);

            Assert.That(
                FullyLockedFrontierGrid().EvaluateUnlock(card, Cell(6, 3)).Failure,
                Is.EqualTo(UnlockFailureReason.OutOfBounds));
            Assert.That(
                NewGrid().EvaluateUnlock(card, Cell(0, 0)).Failure,
                Is.EqualTo(UnlockFailureReason.NotAdjacent));
        }

        [TestCase(GridCorner.LowerLeft, 0, 0)]
        [TestCase(GridCorner.LowerRight, 1, 0)]
        [TestCase(GridCorner.UpperLeft, 0, 1)]
        [TestCase(GridCorner.UpperRight, 1, 1)]
        public void NotchedTwoByTwo_OmitsExactlyTheRequestedCornerOffset(
            GridCorner notch,
            int missingColumn,
            int missingRow)
        {
            UnlockCard card = UnlockCardFactory.Create(UnlockCardShape.TwoByTwoNotched, notch);

            Assert.That(card.Footprint.OccupiedOffsets, Has.No.Member(Cell(missingColumn, missingRow)));
            Assert.That(card.Footprint.Width, Is.EqualTo(2));
            Assert.That(card.Footprint.Height, Is.EqualTo(2));
        }

        [Test]
        public void EvaluateUnlock_OverlappingUnlockedCellsIsAllowedWhileAtLeastOneCellIsNew()
        {
            DeploymentGrid grid = NewGrid();

            UnlockEvaluation evaluation = grid.EvaluateUnlock(
                UnlockCardFactory.Create(UnlockCardShape.TwoByTwo),
                Cell(2, 1));

            Assert.That(evaluation.IsValid, Is.True);
            Assert.That(evaluation.NewlyUnlockedCells, Is.EqualTo(new[] { Cell(2, 1), Cell(3, 1) }));
        }

        [Test]
        public void EvaluateUnlock_CardCoveringOnlyUnlockedCellsIsRejected()
        {
            UnlockEvaluation evaluation = NewGrid()
                .EvaluateUnlock(UnlockCardFactory.Create(UnlockCardShape.TwoByTwo), Cell(2, 2));

            Assert.That(evaluation.IsValid, Is.False);
            Assert.That(evaluation.Failure, Is.EqualTo(UnlockFailureReason.NoNewCells));
        }

        [Test]
        public void ApplyUnlock_OpensExactlyTheNewCellsAndRejectsAnIllegalAnchorWithoutMutating()
        {
            DeploymentGrid grid = NewGrid();

            UnlockApplyResult result = grid.ApplyUnlock(
                UnlockCardFactory.Create(UnlockCardShape.TwoByOne),
                Cell(2, 1));

            Assert.That(result.NewlyUnlockedCells, Is.EqualTo(new[] { Cell(2, 1), Cell(3, 1) }));
            Assert.That(result.UnlockedCellCount, Is.EqualTo(11));
            Assert.That(grid.IsUnlocked(Cell(2, 1)), Is.True);
            Assert.That(grid.IsUnlocked(Cell(4, 1)), Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                grid.ApplyUnlock(UnlockCardFactory.Create(UnlockCardShape.OneByOne), Cell(0, 6)));
            Assert.That(grid.UnlockedCellCount, Is.EqualTo(11));
        }

        [Test]
        public void GetLegalUnlockAnchors_ListsOnlyAnchorsEvaluateUnlockAccepts()
        {
            DeploymentGrid grid = NewGrid();
            UnlockCard card = UnlockCardFactory.Create(UnlockCardShape.OneByOne);

            IReadOnlyList<GridCoordinate> anchors = grid.GetLegalUnlockAnchors(card);

            Assert.That(anchors, Has.Count.EqualTo(12), "the 3x3 frontier is twelve cells");
            Assert.That(anchors.All(anchor => grid.EvaluateUnlock(card, anchor).IsValid), Is.True);
            Assert.That(anchors, Has.No.Member(Cell(3, 3)), "already unlocked");
            Assert.That(anchors, Has.No.Member(Cell(0, 0)), "island");
        }

        [Test]
        public void UnlockedMask_NeverGrowsADisconnectedIslandAcrossAWholeRandomRun()
        {
            var rng = new Rng(0xC5A11Du);
            DeploymentGrid grid = NewGrid();

            for (int step = 0; step < 400 && !grid.IsFullyUnlocked; step++)
            {
                UnlockCard card = UnlockCardFactory.Draw(rng);
                IReadOnlyList<GridCoordinate> anchors = grid.GetLegalUnlockAnchors(card);
                if (anchors.Count == 0) continue;
                grid.ApplyUnlock(card, anchors[rng.NextInt(anchors.Count)]);
                Assert.That(IsConnected(grid), Is.True, $"mask split into islands at step {step}");
            }

            Assert.That(grid.IsFullyUnlocked, Is.True, "random legal unlocks should reach the whole field");
        }

        [Test]
        public void UnlockCardFactory_DrawIsSeededAndReproducesTheSameCardSequence()
        {
            var first = new Rng(0x5EED01u);
            var second = new Rng(0x5EED01u);
            var shapes = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 0; index < 500; index++)
            {
                UnlockCard drawn = UnlockCardFactory.Draw(first);
                Assert.That(drawn, Is.EqualTo(UnlockCardFactory.Draw(second)));
                shapes.Add(drawn.ContentId);
            }

            Assert.That(shapes, Has.Count.EqualTo(10), "six plain shapes plus four notch directions");
        }

        [Test]
        public void UnlockCardFactory_ContentIdRoundTripsThroughTryParse()
        {
            foreach (UnlockCardShape shape in UnlockCardFactory.Shapes)
            foreach (GridCorner corner in UnlockCardFactory.NotchCorners)
            {
                UnlockCard card = UnlockCardFactory.Create(shape, corner);
                Assert.That(UnlockCardFactory.TryParse(card.ContentId, out UnlockCard parsed), Is.True, card.ContentId);
                Assert.That(parsed, Is.EqualTo(card));
                Assert.That(parsed.Footprint, Is.EqualTo(card.Footprint));
            }

            Assert.That(UnlockCardFactory.TryParse("unlock_nonsense", out _), Is.False);
        }

        // --------------------------------------------------------------------------- auto unlock

        [Test]
        public void AutoUnlock_FirstCellIsTheOneDirectlyInFrontOfTheAllyBase()
        {
            DeploymentGrid grid = NewGrid();

            UnlockApplyResult result = grid.AutoUnlockNearestToBase();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Card, Is.Null, "the per-stage unlock is not card driven");
            Assert.That(result.Anchor, Is.EqualTo(Cell(3, 1)));
            Assert.That(result.UnlockedCellCount, Is.EqualTo(10));
        }

        [Test]
        public void AutoUnlock_WalksTowardTheBaseAndBreaksTiesTowardTheCentreThenTheLowerColumn()
        {
            DeploymentGrid grid = NewGrid();

            GridCoordinate[] opened = Enumerable.Range(0, 8)
                .Select(_ => grid.AutoUnlockNearestToBase().Anchor)
                .ToArray();

            // (3,1) is nearest and reachable; (3,0) only becomes reachable once (3,1) is open.
            // (2,0)/(4,0) tie on distance and row, so the more central then lower column wins.
            // (1,0)/(5,0) tie with (2,1)/(4,1) on distance, and the lower row breaks it.
            Assert.That(opened, Is.EqualTo(new[]
            {
                Cell(3, 1),
                Cell(3, 0),
                Cell(2, 0),
                Cell(4, 0),
                Cell(1, 0),
                Cell(5, 0),
                Cell(2, 1),
                Cell(4, 1)
            }));
        }

        [Test]
        public void AutoUnlock_NeverPicksACellDetachedFromTheUnlockedArea()
        {
            DeploymentGrid grid = NewGrid();

            for (int step = 0; step < 40; step++)
            {
                Assert.That(grid.TryFindAutoUnlockCell(out GridCoordinate next), Is.True, $"step {step}");
                Assert.That(TouchesUnlocked(grid, next), Is.True, $"step {step} picked island {next}");
                grid.AutoUnlockNearestToBase();
                Assert.That(IsConnected(grid), Is.True, $"step {step}");
            }
        }

        [Test]
        public void AutoUnlock_IsDeterministicAcrossRunsAndIndependentOfAnyRngSeed()
        {
            GridCoordinate[] first = AutoUnlockSequence();
            GridCoordinate[] second = AutoUnlockSequence();

            Assert.That(first, Has.Length.EqualTo(40));
            Assert.That(second, Is.EqualTo(first));

            // No overload takes an Rng, and consuming unrelated streams cannot shift the ranking.
            var noise = new Rng(0xDEADBEEFu);
            for (int index = 0; index < 100; index++) noise.NextUInt();
            Assert.That(AutoUnlockSequence(), Is.EqualTo(first));
        }

        [Test]
        public void AutoUnlock_StopsQuietlyOnceTheWholeFieldIsUnlocked()
        {
            DeploymentGrid grid = NewGrid();
            for (int step = 0; step < 40; step++) grid.AutoUnlockNearestToBase();

            Assert.That(grid.IsFullyUnlocked, Is.True);
            Assert.That(grid.UnlockedCellCount, Is.EqualTo(49));
            Assert.That(grid.TryFindAutoUnlockCell(out _), Is.False);
            Assert.DoesNotThrow(() => grid.AutoUnlockNearestToBase());
            Assert.That(grid.AutoUnlockNearestToBase(), Is.Null);
            Assert.That(grid.UnlockedCellCount, Is.EqualTo(49));
        }

        [Test]
        public void AutoUnlock_RowOffsetComesFromEconomyJsonRatherThanACodeConstant()
        {
            Assert.That(GameConfig.Load().Economy.GridUnlock.BaseAnchorRowOffset, Is.EqualTo(-1f).Within(0.0001f));
            Assert.That(GameConfig.Load().Economy.GridUnlock.AutoUnlockPerMinorStage, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------- shape unlock semantics

        [Test]
        public void ShapeUnlocks_MaskThatCannotHoldATwoByTwoDropsEveryTwoWideUnitFromThePool()
        {
            GameConfig config = GameConfig.Load();
            DeploymentGrid strip = ConfiguredGrid(VerticalStripCells());

            string[] eligible = UnitCardPoolPolicy.GetEligibleUnits(config, strip)
                .Select(unit => unit.Id).ToArray();

            Assert.That(eligible, Is.EquivalentTo(new[] { "zu", "gong", "huo", "bing", "mao", "nub" }));
            foreach (string wide in new[] { "dun", "dao", "qqi", "zqi", "tie", "lia", "nuc", "chc" })
            {
                Assert.That(eligible, Does.Not.Contain(wide));
            }
        }

        [Test]
        public void ShapeUnlocks_UnlockingASecondColumnRestoresTheTwoWideUnitsToThePool()
        {
            GameConfig config = GameConfig.Load();
            DeploymentGrid strip = ConfiguredGrid(VerticalStripCells());
            strip.ApplyUnlock(UnlockCardFactory.Create(UnlockCardShape.OneByThree), Cell(2, 2));

            string[] eligible = UnitCardPoolPolicy.GetEligibleUnits(config, strip)
                .Select(unit => unit.Id).ToArray();

            Assert.That(eligible, Does.Contain("dun"));
            Assert.That(eligible, Does.Contain("tie"));
            Assert.That(eligible, Does.Contain("nuc"));
            Assert.That(eligible, Does.Not.Contain("zqi"), "a 3x1 still needs three columns");
        }

        [Test]
        public void ShapeUnlocks_StartingCentreMaskAlreadyOffersEveryAllyUnit()
        {
            GameConfig config = GameConfig.Load();

            string[] eligible = UnitCardPoolPolicy.GetEligibleUnits(config, ConfiguredGrid(CentreCells()))
                .Select(unit => unit.Id).ToArray();

            Assert.That(eligible, Is.EquivalentTo(config.AllyUnits.Select(unit => unit.Id)));
        }

        // ------------------------------------------------------------------------ merging (WO-C1)

        [Test]
        public void Merge_SameIdAndLevelUpgradesTargetAndConsumesIncomingDeployment()
        {
            DeploymentGrid grid = NewGrid();
            grid.Apply(Unit("green-a", OneByOne, unitId: "gong"), Cell(2, 2));

            DeploymentApplyResult result = grid.Apply(Unit("green-b", OneByOne, unitId: "gong"), Cell(2, 2));

            Assert.That(result.Action, Is.EqualTo(DeploymentActionKind.Merge));
            Assert.That(result.Placement.DeploymentId, Is.EqualTo("green-a"));
            Assert.That(result.Placement.Level, Is.EqualTo(2));
            Assert.That(result.ConsumedDeploymentId, Is.EqualTo("green-b"));
            Assert.That(grid.Placements, Has.Count.EqualTo(1));
        }

        [Test]
        public void Merge_FourGreenUnitsProduceExactlyOnePurpleUnit()
        {
            DeploymentGrid grid = NewGrid();

            grid.Apply(Unit("green-a", OneByOne, unitId: "gong"), Cell(2, 2));
            grid.Apply(Unit("green-b", OneByOne, unitId: "gong"), Cell(2, 2));
            grid.Apply(Unit("green-c", OneByOne, unitId: "gong"), Cell(4, 2));
            grid.Apply(Unit("green-d", OneByOne, unitId: "gong"), Cell(4, 2));
            DeploymentApplyResult result = grid.Move("green-c", Cell(2, 2));

            Assert.That(result.Action, Is.EqualTo(DeploymentActionKind.Merge));
            Assert.That(result.Placement.Level, Is.EqualTo(3), "level 3 is purple");
            Assert.That(result.Placement.Unit.Tier, Is.EqualTo(UnitTier.Purple));
            Assert.That(grid.Placements.Single().DeploymentId, Is.EqualTo("green-a"));
        }

        [Test]
        public void Merge_PurplePairProducesGoldAndLevelFourIsCapped()
        {
            DeploymentGrid grid = NewGrid();
            grid.Apply(Unit("purple-a", OneByOne, level: 3, unitId: "gong"), Cell(2, 2));

            DeploymentApplyResult result = grid.Apply(
                Unit("purple-b", OneByOne, level: 3, unitId: "gong"), Cell(2, 2));

            Assert.That(result.Placement.Level, Is.EqualTo(4));
            Assert.That(result.Placement.Unit.Tier, Is.EqualTo(UnitTier.Gold));
            Assert.That(
                grid.Evaluate(Unit("gold-b", OneByOne, level: 4, unitId: "gong"), Cell(2, 2)).Failure,
                Is.EqualTo(DeploymentFailureReason.MaximumLevelReached));
        }

        [Test]
        public void Merge_FullMaskStillAllowsDropOntoSameIdAndLevelTarget()
        {
            DeploymentGrid grid = NewGrid();
            int nextId = 0;
            foreach (GridCoordinate cell in CentreCells())
            {
                string deploymentId = nextId++ == 0 ? "merge-target" : $"filler-{nextId}";
                string unitId = deploymentId == "merge-target" ? "gong" : deploymentId;
                grid.Apply(Unit(deploymentId, OneByOne, unitId: unitId), cell);
            }

            Assert.That(grid.CountAvailableAnchors(OneByOne), Is.Zero);
            DeploymentEvaluation evaluation = grid.Evaluate(
                Unit("incoming", OneByOne, unitId: "gong"), Cell(2, 2));

            Assert.That(evaluation.Action, Is.EqualTo(DeploymentActionKind.Merge));
            Assert.That(evaluation.MergeTargetDeploymentId, Is.EqualTo("merge-target"));
        }

        [Test]
        public void Merge_MismatchedUnitLevelOrFootprintReportsThePreciseFailure()
        {
            DeploymentGrid grid = NewGrid();
            grid.Apply(Unit("target", OneByOne, level: 2, unitId: "gong"), Cell(2, 2));

            Assert.That(
                grid.Evaluate(Unit("other-unit", OneByOne, level: 2, unitId: "zu"), Cell(2, 2)).Failure,
                Is.EqualTo(DeploymentFailureReason.UnitMismatch));
            Assert.That(
                grid.Evaluate(Unit("other-level", OneByOne, level: 1, unitId: "gong"), Cell(2, 2)).Failure,
                Is.EqualTo(DeploymentFailureReason.LevelMismatch));
            Assert.That(
                grid.Evaluate(Unit("other-shape", TwoByOne, level: 2, unitId: "gong"), Cell(2, 2)).Failure,
                Is.EqualTo(DeploymentFailureReason.FootprintMismatch));
        }

        [Test]
        public void Evaluate_OverlappingTwoDifferentDeployments_ReportsOccupiedWithSortedBlockers()
        {
            DeploymentGrid grid = NewGrid();
            grid.Apply(Unit("z-blocker", OneByOne), Cell(2, 2));
            grid.Apply(Unit("a-blocker", OneByOne), Cell(3, 2));

            DeploymentEvaluation evaluation = grid.Evaluate(Unit("incoming", TwoByOne), Cell(2, 2));

            Assert.That(evaluation.Failure, Is.EqualTo(DeploymentFailureReason.Occupied));
            Assert.That(evaluation.BlockingDeploymentIds, Is.EqualTo(new[] { "a-blocker", "z-blocker" }));
        }

        [Test]
        public void RemoveAndMove_KeepOccupancyConsistentAndRollBackAtomicallyOnFailure()
        {
            DeploymentGrid grid = NewGrid();
            grid.Apply(Unit("wide", TwoByOne), Cell(2, 2));

            Assert.Throws<InvalidOperationException>(() => grid.Move("wide", Cell(4, 4)));
            Assert.That(grid.TryGetPlacement("wide", out DeploymentPlacement restored), Is.True);
            Assert.That(restored.Anchor, Is.EqualTo(Cell(2, 2)));

            DeploymentApplyResult moved = grid.Move("wide", Cell(3, 4));
            Assert.That(moved.Placement.Anchor, Is.EqualTo(Cell(3, 4)));
            Assert.That(grid.TryGetPlacementAt(Cell(2, 2), out _), Is.False);
            Assert.That(grid.TryGetPlacementAt(Cell(4, 4), out _), Is.True);

            Assert.That(grid.Remove("wide"), Is.True);
            Assert.That(grid.Remove("wide"), Is.False);
            Assert.That(grid.CountAvailableAnchors(OneByOne), Is.EqualTo(9));
        }

        // ------------------------------------------------------------------- mask save and restore

        [Test]
        public void SaveUnlockMask_RoundTripsThroughAFreshGrid()
        {
            DeploymentGrid grid = NewGrid();
            grid.ApplyUnlock(UnlockCardFactory.Create(UnlockCardShape.TwoByTwo), Cell(2, 1));
            grid.AutoUnlockNearestToBase();

            bool[] mask = grid.SaveUnlockMask();
            var restored = new DeploymentGrid(7, 7, CellsFromMask(mask));

            Assert.That(mask, Has.Length.EqualTo(49));
            Assert.That(restored.UnlockedCellCount, Is.EqualTo(grid.UnlockedCellCount));
            Assert.That(restored.GetUnlockedCells(), Is.EqualTo(grid.GetUnlockedCells()));
        }

        [Test]
        public void SaveUnlockMask_IsACopyThatCannotWriteBackIntoTheGrid()
        {
            DeploymentGrid grid = NewGrid();

            bool[] mask = grid.SaveUnlockMask();
            mask[0] = true;

            Assert.That(grid.IsUnlocked(Cell(0, 0)), Is.False);
            Assert.That(grid.UnlockedCellCount, Is.EqualTo(9));
        }

        [Test]
        public void MinorStageAdvance_InheritsTheUnlockMaskAndMajorStageResetRebuildsIt()
        {
            GameConfig config = GameConfig.Load();
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0x9A5Cu);
            economy.Grid.ApplyUnlock(UnlockCardFactory.Create(UnlockCardShape.OneByThree), Cell(1, 2));
            economy.Grid.AutoUnlockNearestToBase();
            economy.SnapshotToRunState();
            int inherited = economy.Grid.UnlockedCellCount;
            bool[] mask = economy.State.UnlockedCells.ToArray();

            economy.AdvanceToMinorStage(config.GetLevel("level_1_2"));

            Assert.That(inherited, Is.EqualTo(13));
            Assert.That(economy.Grid.UnlockedCellCount, Is.EqualTo(inherited));
            Assert.That(economy.State.UnlockedCells, Is.EqualTo(mask));
            Assert.That(economy.State.GridWidth, Is.EqualTo(7));
            Assert.That(economy.State.GridHeight, Is.EqualTo(7));

            economy.ResetForMajorStage(config.GetLevel("level_1_1"), 0x9A5Cu);

            Assert.That(economy.Grid.UnlockedCellCount, Is.EqualTo(9));
            Assert.That(economy.Grid.GetUnlockedCells(), Is.EqualTo(CentreCells()));
            Assert.That(economy.State.UnlockedCells.Count(value => value), Is.EqualTo(9));
        }

        [Test]
        public void Restore_MaskWithTheWrongLengthFallsBackToTheLevelInitialRect()
        {
            GameConfig config = GameConfig.Load();
            var state = new RunState
            {
                Coins = 45,
                StageIndex = 1,
                GridWidth = 7,
                GridHeight = 7,
                UnlockedCells = new bool[9],
                Seed = 1u,
                DeployedGrid = Array.Empty<DeployedUnitState>(),
                OwnedEffects = Array.Empty<string>()
            };

            // A wrong-length mask is treated as absent and falls back to the level's own rect,
            // which keeps a corrupted save loadable instead of throwing at the player.
            CardEconomy restored = CardEconomy.Restore(config, config.GetLevel("level_1_1"), state);

            Assert.That(restored.Grid.UnlockedCellCount, Is.EqualTo(9));
            Assert.That(restored.State.UnlockedCells, Has.Length.EqualTo(49));
        }

        // ------------------------------------------------------------------------------ orientation

        [Test]
        public void VerticalOrientation_TransposesLogicalCoordinatesWithoutChangingPackingCounts()
        {
            DeploymentGrid vertical = NewGrid(DeploymentGridOrientation.ColumnsVertical);
            GridCoordinate logical = Cell(4, 2);

            Assert.That(vertical.ToPhysical(logical), Is.EqualTo(Cell(2, 4)));
            Assert.That(vertical.ToLogical(vertical.ToPhysical(logical)), Is.EqualTo(logical));
            Assert.That(vertical.CountAvailableAnchors(TwoByTwo), Is.EqualTo(4));
            Assert.That(vertical.CountAvailableAnchors(ThreeByOne), Is.EqualTo(3));
        }

        [TestCase(DeploymentGridOrientation.ColumnsHorizontal)]
        [TestCase(DeploymentGridOrientation.ColumnsVertical)]
        public void OrientationConfiguration_SameLogicalPackingAndUnlockRemainValid(
            DeploymentGridOrientation orientation)
        {
            DeploymentGrid grid = NewGrid(orientation);

            grid.Apply(Unit("square", TwoByTwo), Cell(2, 2));
            grid.Apply(Unit("vertical", OneByTwo), Cell(4, 2));
            grid.Apply(Unit("horizontal", TwoByOne), Cell(2, 4));
            grid.ApplyUnlock(UnlockCardFactory.Create(UnlockCardShape.OneByOne), Cell(3, 1));

            AssertPacking(grid, expectedPlacements: 3, expectedOccupiedCells: 8);
            Assert.That(grid.UnlockedCellCount, Is.EqualTo(10));
        }

        // ---------------------------------------------------------------------------------- helpers

        private static DeploymentGrid NewGrid(
            DeploymentGridOrientation orientation = DeploymentGridOrientation.ColumnsHorizontal)
        {
            return new DeploymentGrid(7, 7, CentreCells(), orientation);
        }

        /// <summary>A grid with the economy card-pool rules attached, so shape gating is live.</summary>
        private static DeploymentGrid ConfiguredGrid(IEnumerable<GridCoordinate> unlocked)
        {
            GameConfig config = GameConfig.Load();
            return new DeploymentGrid(
                7,
                7,
                unlocked,
                DeploymentGridOrientation.ColumnsHorizontal,
                config.Economy.CardPool,
                config.Economy.GridUnlock.BaseAnchorRowOffset);
        }

        /// <summary>A single unlocked cell in the far corner, so every field edge is a frontier.</summary>
        private static DeploymentGrid FullyLockedFrontierGrid()
        {
            return new DeploymentGrid(7, 7, new[] { Cell(6, 6) });
        }

        private static DeploymentGrid FullyUnlockedGrid()
        {
            var all = new List<GridCoordinate>(49);
            for (int row = 0; row < 7; row++)
            for (int column = 0; column < 7; column++)
                all.Add(Cell(column, row));
            return new DeploymentGrid(7, 7, all);
        }

        private static GridCoordinate[] CentreCells()
        {
            var cells = new List<GridCoordinate>(9);
            for (int row = 2; row <= 4; row++)
            for (int column = 2; column <= 4; column++)
                cells.Add(Cell(column, row));
            return cells.ToArray();
        }

        /// <summary>A one-wide unlocked strip: the smallest mask that cannot hold any 2-wide shape.</summary>
        private static GridCoordinate[] VerticalStripCells()
        {
            return new[] { Cell(3, 2), Cell(3, 3), Cell(3, 4) };
        }

        private static GridCoordinate[] AutoUnlockSequence()
        {
            DeploymentGrid grid = NewGrid();
            var opened = new List<GridCoordinate>();
            UnlockApplyResult result;
            while ((result = grid.AutoUnlockNearestToBase()) != null)
            {
                opened.Add(result.Anchor);
            }
            return opened.ToArray();
        }

        private static IEnumerable<GridCoordinate> CellsFromMask(bool[] mask)
        {
            for (int row = 0; row < 7; row++)
            for (int column = 0; column < 7; column++)
                if (mask[(row * 7) + column]) yield return Cell(column, row);
        }

        private static bool TouchesUnlocked(DeploymentGrid grid, GridCoordinate cell)
        {
            return grid.IsUnlocked(Cell(cell.Column + 1, cell.Row))
                   || grid.IsUnlocked(Cell(cell.Column - 1, cell.Row))
                   || grid.IsUnlocked(Cell(cell.Column, cell.Row + 1))
                   || grid.IsUnlocked(Cell(cell.Column, cell.Row - 1));
        }

        /// <summary>Flood fills the mask to prove the unlocked area is a single orthogonal region.</summary>
        private static bool IsConnected(DeploymentGrid grid)
        {
            IReadOnlyList<GridCoordinate> unlocked = grid.GetUnlockedCells();
            if (unlocked.Count == 0) return true;

            var seen = new HashSet<GridCoordinate> { unlocked[0] };
            var pending = new Queue<GridCoordinate>();
            pending.Enqueue(unlocked[0]);
            while (pending.Count > 0)
            {
                GridCoordinate current = pending.Dequeue();
                foreach (GridCoordinate step in new[] { Cell(1, 0), Cell(-1, 0), Cell(0, 1), Cell(0, -1) })
                {
                    GridCoordinate next = current + step;
                    if (grid.IsUnlocked(next) && seen.Add(next)) pending.Enqueue(next);
                }
            }

            return seen.Count == unlocked.Count;
        }

        private static DeploymentUnit Unit(
            string deploymentId,
            UnitFootprint footprint,
            int level = 1,
            string unitId = null)
        {
            return new DeploymentUnit(deploymentId, unitId ?? deploymentId, level, footprint);
        }

        private static GridCoordinate Cell(int column, int row)
        {
            return new GridCoordinate(column, row);
        }

        private static void AssertPacking(
            DeploymentGrid grid,
            int expectedPlacements,
            int expectedOccupiedCells)
        {
            Assert.That(grid.Placements, Has.Count.EqualTo(expectedPlacements));
            Assert.That(
                grid.Placements.Sum(placement => placement.Footprint.OccupiedCellCount),
                Is.EqualTo(expectedOccupiedCells));
        }
    }
}
