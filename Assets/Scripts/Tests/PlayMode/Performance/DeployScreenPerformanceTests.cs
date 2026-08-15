using System;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using HanziDefend.View;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HanziDefend.Tests.PlayMode.Performance
{
    /// <summary>
    /// The deployment screen's performance gates.
    ///
    /// <para><b>These assert counts, never wall-clock time.</b> This project has twice had absolute
    /// timings polluted by concurrent editor sessions and machine load (TECH_DEBT rows 28-29), and a
    /// red that only reproduces on one machine teaches nobody anything. A count of canvas writes and
    /// rule-layer queries is identical on every machine, so a red here is always a real regression.</para>
    ///
    /// <para>What they guard: WO-C4's first pass called <c>RefreshCells()</c> from <c>DragTo()</c>,
    /// which re-asked the grid about all 49 cells and rewrote all 49 images <i>every drag frame</i>,
    /// forcing a full uGUI canvas rebuild. Dragging became unusable and no existing test noticed,
    /// because 19 programmatic drags verify logic but never frame cost.</para>
    /// </summary>
    public sealed class DeployScreenPerformanceTests
    {
        private const int DragFrames = 40;

        private GameObject canvasObject;
        private GameObject screenObject;

        [TearDown]
        public void TearDown()
        {
            if (screenObject != null) UnityEngine.Object.DestroyImmediate(screenObject);
            if (canvasObject != null) UnityEngine.Object.DestroyImmediate(canvasObject);

            EventSystem[] eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < eventSystems.Length; index++)
            {
                if (eventSystems[index] != null && eventSystems[index].gameObject.name == "M1 EventSystem")
                {
                    UnityEngine.Object.DestroyImmediate(eventSystems[index].gameObject);
                }
            }
        }

        /// <summary>
        /// The headline gate: one drag frame may repaint at most twice the card's own footprint —
        /// the cells the previous preview lit, plus the cells the new one lights. Crucially the bound
        /// is a function of the CARD, not of the field, so a bigger grid cannot make it grow.
        /// </summary>
        [Test, Performance]
        [TestCase("zu", 1)]
        [TestCase("dun", 2)]
        [TestCase("mao", 2)]
        [TestCase("nuc", 3)]
        [TestCase("tie", 4)]
        public void DragFrame_RepaintsAtMostTwiceTheFootprintNeverTheWholeField(
            string unitId,
            int footprintCells)
        {
            DeployScreen screen = CreateScreenWithUnitInHand(unitId, out CardOfferItem card);
            UnitFootprint footprint = screen.HandCardFootprint(card);
            Assert.That(footprint.OccupiedCellCount, Is.EqualTo(footprintCells), "fixture footprint");

            int budgetPerFrame = Math.Min(8, 2 * footprintCells);
            var anchors = new[]
            {
                new GridCoordinate(2, 2), new GridCoordinate(3, 3), new GridCoordinate(2, 3),
                new GridCoordinate(3, 2), new GridCoordinate(0, 0), new GridCoordinate(4, 4)
            };

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(anchors[0], footprint));
            int worstFrame = 0;
            int totalVisits = 0;
            int worstWrites = 0;
            for (int frame = 0; frame < DragFrames; frame++)
            {
                int visitsBefore = screen.CellVisitCount;
                int writesBefore = screen.CellColorWriteCount;
                screen.DragTo(screen.DragScreenPointForAnchor(anchors[frame % anchors.Length], footprint));
                int visited = screen.CellVisitCount - visitsBefore;
                worstFrame = Math.Max(worstFrame, visited);
                worstWrites = Math.Max(worstWrites, screen.CellColorWriteCount - writesBefore);
                totalVisits += visited;
            }
            screen.EndDrag();

            Measure.Custom(
                new SampleGroup("DeployDrag.WorstFrameCellVisits", SampleUnit.Undefined, false), worstFrame);
            TestContext.WriteLine(
                $"{unitId} ({footprintCells} cells): worst frame touched {worstFrame} cells "
                + $"(budget {budgetPerFrame}), wrote {worstWrites}; {totalVisits} touches across "
                + $"{DragFrames} frames; field has {screen.FieldCellCount} cells.");

            Assert.That(worstFrame, Is.LessThanOrEqualTo(budgetPerFrame),
                "A drag frame must only restore the previous footprint and paint the new one.");
            Assert.That(worstWrites, Is.LessThanOrEqualTo(budgetPerFrame),
                "Actual colour writes are bounded by the same footprint budget.");
            Assert.That(worstFrame, Is.LessThan(screen.FieldCellCount),
                "A drag frame must never walk the whole field.");
            Assert.That(totalVisits, Is.LessThan(DragFrames * screen.FieldCellCount / 4),
                $"{DragFrames} drag frames stayed far below a per-frame full refresh.");
        }

        /// <summary>
        /// The same bound stated as independence: the per-frame repaint tracks footprint size and is
        /// unaffected by how many cells the field has. A 4-cell card costs at most 4x a 1-cell card,
        /// and neither gets anywhere near the 49-cell field.
        /// </summary>
        [Test, Performance]
        public void DragFrame_RepaintScalesWithFootprintNotWithFieldSize()
        {
            int single = WorstFrameWritesFor("zu");
            int quad = WorstFrameWritesFor("tie");

            TestContext.WriteLine($"worst-frame cell touches: 1-cell card={single}, 4-cell card={quad}.");
            Assert.That(single, Is.LessThanOrEqualTo(2));
            Assert.That(quad, Is.LessThanOrEqualTo(8));
            Assert.That(quad, Is.LessThanOrEqualTo(single * 4),
                "Repaint cost must scale with the card's cells, not with the field's.");
        }

        /// <summary>
        /// A drag frame asks the rule layer O(1) questions — one evaluation for the anchor under the
        /// cursor. The regression asked 2 per cell (IsUnlocked + TryGetPlacementAt over all 49).
        /// </summary>
        [Test, Performance]
        public void DragFrame_AsksConstantRuleQueriesNotOnePerCell()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("tie", out CardOfferItem card);
            UnitFootprint footprint = screen.HandCardFootprint(card);
            var anchors = new[]
            {
                new GridCoordinate(2, 2), new GridCoordinate(3, 3), new GridCoordinate(0, 0)
            };

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(anchors[0], footprint));
            int before = screen.GridQueryCount;
            int worstFrame = 0;
            for (int frame = 0; frame < DragFrames; frame++)
            {
                int frameBefore = screen.GridQueryCount;
                screen.DragTo(screen.DragScreenPointForAnchor(anchors[frame % anchors.Length], footprint));
                worstFrame = Math.Max(worstFrame, screen.GridQueryCount - frameBefore);
            }
            int total = screen.GridQueryCount - before;
            screen.EndDrag();

            Measure.Custom(
                new SampleGroup("DeployDrag.RuleQueriesPerFrame", SampleUnit.Undefined, false), worstFrame);
            TestContext.WriteLine(
                $"rule queries: worst frame={worstFrame}, {total} across {DragFrames} frames, "
                + $"field has {screen.FieldCellCount} cells.");

            Assert.That(worstFrame, Is.EqualTo(1),
                "One drag frame evaluates the anchor exactly once, and never walks the field.");
            Assert.That(total, Is.EqualTo(DragFrames),
                "Rule-layer cost is one query per frame — linear in frames, constant in cells.");
            Assert.That(total, Is.LessThan(screen.FieldCellCount),
                $"All {DragFrames} drag frames together must cost less than one full-field pass.");
        }

        /// <summary>
        /// Guards the split itself: the expensive full pass runs on real board changes and nowhere
        /// else. If someone reintroduces RefreshCells() into the drag loop, this goes red.
        /// </summary>
        [Test, Performance]
        public void FullRefresh_RunsOnBoardChangesAndNotDuringDragging()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out CardOfferItem card);
            UnitFootprint footprint = screen.HandCardFootprint(card);

            // A full refresh touches the whole field: it is allowed to be expensive.
            int beforeRefresh = screen.GridQueryCount;
            screen.RefreshAll();
            int refreshQueries = screen.GridQueryCount - beforeRefresh;
            Assert.That(refreshQueries, Is.GreaterThanOrEqualTo(screen.FieldCellCount),
                "RefreshAll is the full pass; it should query at least once per cell.");

            // Dragging must not pay that cost.
            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(new GridCoordinate(2, 2), footprint));
            int beforeDrag = screen.GridQueryCount;
            for (int frame = 0; frame < DragFrames; frame++)
            {
                screen.DragTo(screen.DragScreenPointForAnchor(
                    new GridCoordinate(2 + (frame % 3), 2 + (frame % 3)), footprint));
            }
            int dragQueries = screen.GridQueryCount - beforeDrag;
            screen.EndDrag();

            TestContext.WriteLine(
                $"one RefreshAll = {refreshQueries} queries; {DragFrames} drag frames = {dragQueries} queries.");
            Assert.That(dragQueries, Is.EqualTo(DragFrames),
                "Each drag frame asks exactly one question; none of them triggers the full pass.");
            Assert.That(dragQueries, Is.LessThan(refreshQueries),
                $"{DragFrames} drag frames must cost less than a single full refresh.");
        }

        /// <summary>
        /// The preview still has to be correct after going incremental: stale cells from the previous
        /// frame must be restored, so exactly the current footprint is lit and nothing else.
        /// </summary>
        [Test]
        public void IncrementalRepaint_LeavesNoStaleHighlightBehind()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("tie", out CardOfferItem card);
            UnitFootprint footprint = screen.HandCardFootprint(card);

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(new GridCoordinate(2, 2), footprint));
            screen.DragTo(screen.DragScreenPointForAnchor(new GridCoordinate(3, 3), footprint));

            Assert.That(screen.HighlightedCellCount, Is.EqualTo(footprint.OccupiedCellCount));
            foreach (GridCoordinate offset in footprint.OccupiedOffsets)
            {
                Assert.That(screen.GetHighlight(new GridCoordinate(3, 3) + offset),
                    Is.Not.EqualTo(DeployCellHighlight.None), "current footprint is lit");
            }
            Assert.That(screen.GetHighlight(new GridCoordinate(2, 2)), Is.EqualTo(DeployCellHighlight.None),
                "the cell the previous frame lit must have been restored");

            screen.EndDrag();
            Assert.That(screen.HighlightedCellCount, Is.Zero, "ending a drag clears every highlight");
        }

        /// <summary>Locked cells are plain dark tiles now — no 40 repeated glyphs over the field.</summary>
        [Test]
        public void LockedCells_RenderAsPlainDarkTilesWithoutPerCellText()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out _);
            Transform grid = screen.transform.Find("Deployment Grid Panel/Deployment Grid");

            int lockedWithText = 0;
            for (int row = 0; row < 7; row++)
            for (int column = 0; column < 7; column++)
            {
                bool unlocked = screen.Economy.Grid.IsUnlocked(new GridCoordinate(column, row));
                if (unlocked)
                {
                    continue;
                }

                Text label = grid.Find($"Cell {column},{row}").GetComponentInChildren<Text>();
                if (!string.IsNullOrEmpty(label.text))
                {
                    lockedWithText++;
                }
            }

            Assert.That(screen.LockedCellCount, Is.EqualTo(40), "the starting mask locks 40 cells");
            Assert.That(lockedWithText, Is.Zero,
                "Locked cells must not each carry a glyph; 40 of them drowned the playable centre.");
        }

        // ------------------------------------------------------------------------------- helpers

        private int WorstFrameWritesFor(string unitId)
        {
            DeployScreen screen = CreateScreenWithUnitInHand(unitId, out CardOfferItem card);
            UnitFootprint footprint = screen.HandCardFootprint(card);
            var anchors = new[]
            {
                new GridCoordinate(2, 2), new GridCoordinate(3, 3), new GridCoordinate(2, 3)
            };

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(anchors[0], footprint));
            int worst = 0;
            for (int frame = 0; frame < DragFrames; frame++)
            {
                int before = screen.CellVisitCount;
                screen.DragTo(screen.DragScreenPointForAnchor(anchors[frame % anchors.Length], footprint));
                worst = Math.Max(worst, screen.CellVisitCount - before);
            }
            screen.EndDrag();
            TearDown();
            return worst;
        }

        private DeployScreen CreateScreenWithUnitInHand(string unitId, out CardOfferItem card)
        {
            GameConfig config = GameConfig.Load();
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC4D001u);
            economy.State.Coins = int.MaxValue;

            canvasObject = new GameObject(
                "Deploy Perf Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 1920f);

            screenObject = new GameObject("DeployRoot", typeof(RectTransform));
            screenObject.transform.SetParent(canvasObject.transform, false);
            DeployScreen screen = screenObject.AddComponent<DeployScreen>();
            screen.Initialize(config, economy, new NullArtSource(), null);

            for (int attempt = 0; attempt < 4000; attempt++)
            {
                CardOfferItem match = economy.CurrentOffer.Cards.FirstOrDefault(
                    value => value.Category == CardCategory.Unit
                             && string.Equals(value.ContentId, unitId, StringComparison.Ordinal));
                if (match != null)
                {
                    card = match;
                    return screen;
                }

                economy.State.Coins = int.MaxValue;
                Assert.That(economy.TryRefresh(out _), Is.True);
            }

            throw new AssertionException($"'{unitId}' never appeared within the deterministic bound.");
        }

        private sealed class NullArtSource : IBattleArtSource
        {
            public Sprite Find(string assetKey)
            {
                return null;
            }
        }
    }
}
