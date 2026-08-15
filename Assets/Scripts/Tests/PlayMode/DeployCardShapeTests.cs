using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using HanziDefend.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace HanziDefend.Tests.PlayMode
{
    /// <summary>
    /// WO-C4: hand cards must read as their footprint, and dragging must show the player where the
    /// card will land and why it will or will not fit. Every legality answer in here comes from
    /// DeploymentGrid — these tests only assert that the view reports what the grid decided.
    /// </summary>
    public sealed class DeployCardShapeTests
    {
        private readonly List<GameObject> cleanup = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = cleanup.Count - 1; index >= 0; index--)
            {
                if (cleanup[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(cleanup[index]);
                }
            }
            cleanup.Clear();

            EventSystem[] eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < eventSystems.Length; index++)
            {
                if (eventSystems[index] != null && eventSystems[index].gameObject.name == "M1 EventSystem")
                {
                    UnityEngine.Object.DestroyImmediate(eventSystems[index].gameObject);
                }
            }
        }

        // ------------------------------------------------------------------ part 1: card shapes

        [TestCase("zu", 1, 1)]
        [TestCase("dun", 2, 1)]
        [TestCase("mao", 1, 2)]
        [TestCase("tie", 2, 2)]
        [TestCase("zqi", 3, 1)]
        public void HandCard_BoundingBoxMatchesItsFootprintOnTheSharedMetric(
            string unitId,
            int expectedColumns,
            int expectedRows)
        {
            DeployScreen screen = CreateScreenWithUnitInHand(unitId, out _, out CardOfferItem card);

            Vector2 size = screen.HandCardSize(card);

            float edge = screen.HandCellEdge;
            float spacing = screen.HandCellSpacing;
            Assert.That(size.x, Is.EqualTo(Span(expectedColumns, edge, spacing)).Within(0.01f),
                $"{unitId} card width must be {expectedColumns} cells wide.");
            Assert.That(size.y, Is.EqualTo(Span(expectedRows, edge, spacing)).Within(0.01f),
                $"{unitId} card height must be {expectedRows} cells tall.");
        }

        [Test]
        public void HandCards_OfDifferentFootprintsDoNotCollapseToOneUniformStrip()
        {
            DeployScreen wide = CreateScreenWithUnitInHand("zqi", out _, out CardOfferItem wideCard);
            Vector2 wideSize = wide.HandCardSize(wideCard);

            DeployScreen square = CreateScreenWithUnitInHand("zu", out _, out CardOfferItem squareCard);
            Vector2 squareSize = square.HandCardSize(squareCard);

            Assert.That(squareSize.x, Is.EqualTo(squareSize.y).Within(0.01f), "1x1 must be square.");
            Assert.That(wideSize.x / wideSize.y, Is.GreaterThan(2.5f), "3x1 must stay a long strip.");
            Assert.That(wideSize.x, Is.GreaterThan(squareSize.x * 2.5f),
                "A 3x1 card must be far wider than a 1x1 card, not padded to the same width.");
        }

        [TestCase("nuc", UnitFootprintShape.MissingUpperRight, 1, 1)]
        [TestCase("chc", UnitFootprintShape.MissingLowerLeft, 0, 0)]
        public void LShapedHandCard_DrawsThreeTilesAndReallyOmitsItsNotchedCorner(
            string unitId,
            UnitFootprintShape expectedShape,
            int missingColumn,
            int missingRow)
        {
            DeployScreen screen = CreateScreenWithUnitInHand(unitId, out GameConfig config, out CardOfferItem card);

            IReadOnlyList<GridCoordinate> drawn = screen.HandCardCellOffsets(card);
            UnitFootprint footprint = screen.HandCardFootprint(card);

            Assert.That(config.GetUnit(unitId).Footprint, Is.EqualTo(expectedShape));
            Assert.That(drawn, Has.Count.EqualTo(3), $"{unitId} must draw 3 tiles, not a full 2x2.");
            Assert.That(drawn, Has.No.Member(new GridCoordinate(missingColumn, missingRow)),
                $"{unitId} must leave its notched corner empty.");
            Assert.That(drawn, Is.EquivalentTo(footprint.OccupiedOffsets),
                "The card must draw exactly the data's occupied offsets.");

            // The bounding box is still 2x2 even though only three tiles exist.
            Vector2 size = screen.HandCardSize(card);
            Assert.That(size.x, Is.EqualTo(size.y).Within(0.01f));
        }

        [Test]
        public void HandCards_AreHorizontallyCentredWithEvenGapsWhateverTheirWidths()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zqi", out _, out _);
            var hand = (RectTransform)screen.transform.Find("Hand Panel/Cards");
            var cards = new List<RectTransform>();
            foreach (RectTransform child in hand)
            {
                cards.Add(child);
            }
            cards.Sort((left, right) => left.anchoredPosition.x.CompareTo(right.anchoredPosition.x));

            Assert.That(cards, Is.Not.Empty);
            float min = cards.Min(card => card.anchoredPosition.x - (card.sizeDelta.x * 0.5f));
            float max = cards.Max(card => card.anchoredPosition.x + (card.sizeDelta.x * 0.5f));
            float midpoint = (min + max) * 0.5f;

            // handRoot is symmetric about x, so a centred row has its span midpoint at zero.
            Assert.That(midpoint, Is.EqualTo(0f).Within(1f),
                $"The hand row must be centred; span [{min:F1}, {max:F1}] is off by {midpoint:F1}px.");
            Assert.That(max - min, Is.LessThanOrEqualTo(hand.rect.width + 1f),
                "The hand row must fit inside its panel.");

            Assert.That(cards, Has.Count.EqualTo(screen.RenderedHandCount),
                "Only the live hand may be parented; a discarded row must not linger on screen.");

            var gaps = new List<float>();
            for (int index = 1; index < cards.Count; index++)
            {
                gaps.Add((cards[index].anchoredPosition.x - (cards[index].sizeDelta.x * 0.5f))
                         - (cards[index - 1].anchoredPosition.x + (cards[index - 1].sizeDelta.x * 0.5f)));
            }

            for (int index = 0; index < gaps.Count; index++)
            {
                Assert.That(gaps[index], Is.GreaterThan(0f),
                    $"Gap {index + 1} is {gaps[index]:F1}px — cards must not overlap.");
                Assert.That(gaps[index], Is.EqualTo(gaps[0]).Within(1f),
                    "Every gap must be identical, so unequal card widths still read as an even row.");
            }
        }

        [Test]
        public void HandCardMetric_IsTheGridMetricTimesTheConfiguredScale()
        {
            DeployScreen screen = CreateScreen(out _, out GameConfig config);

            float scale = config.Economy.DeployUi.HandCardScale;
            Assert.That(screen.HandCellEdge, Is.EqualTo(screen.CellEdge * scale).Within(0.01f),
                "Hand cards must derive from the grid's own cell edge, not a separate constant.");
            Assert.That(screen.HandCellSpacing, Is.EqualTo(screen.CellSpacing * scale).Within(0.01f));
        }

        // ---------------------------------------------------------------- part 2: drag feedback

        [Test]
        public void BeginCardDrag_GhostIsExactlyTheGridSpaceTheCardWillOccupy()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("tie", out _, out CardOfferItem card);

            Assert.That(screen.BeginCardDrag(card, screen.CellScreenPoint(new GridCoordinate(3, 3))), Is.True);

            Vector2 ghost = screen.DragGhostSize;
            float expected = Span(2, screen.CellEdge, screen.CellSpacing);
            Assert.That(ghost.x, Is.EqualTo(expected).Within(0.01f));
            Assert.That(ghost.y, Is.EqualTo(expected).Within(0.01f));
            Assert.That(ghost.x, Is.GreaterThan(screen.HandCardSize(card).x),
                "The ghost must scale up from hand size to grid size on pickup.");
        }

        [Test]
        public void DragOverFreeUnlockedCells_HighlightsEveryCoveredCellGreen()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("tie", out _, out CardOfferItem card);
            var anchor = new GridCoordinate(2, 2);

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(anchor, screen.HandCardFootprint(card)));

            foreach (GridCoordinate offset in screen.HandCardFootprint(card).OccupiedOffsets)
            {
                Assert.That(screen.GetHighlight(anchor + offset), Is.EqualTo(DeployCellHighlight.Valid),
                    $"cell {anchor + offset} should preview as placeable.");
            }
            Assert.That(screen.GetHighlight(new GridCoordinate(4, 4)), Is.EqualTo(DeployCellHighlight.None),
                "Cells outside the footprint must not be highlighted.");
            Assert.That(screen.FeedbackMessage, Does.Contain("可落位"));
        }

        [Test]
        public void DragOverALockedCell_HighlightsRedAndNamesTheUnlockReason()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out _, out CardOfferItem card);
            var locked = new GridCoordinate(0, 0);

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(locked, screen.HandCardFootprint(card)));

            Assert.That(screen.GetHighlight(locked), Is.EqualTo(DeployCellHighlight.Invalid));
            Assert.That(screen.FeedbackMessage, Does.Contain("尚未解锁"),
                "A locked cell is the high-frequency failure on a 7x7 mask and must say so.");
        }

        [Test]
        public void DragOverAMatchingUnit_HighlightsGoldForMerge()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out GameConfig config, out CardOfferItem card);
            var anchor = new GridCoordinate(3, 3);
            UnitDef unit = config.GetUnit("zu");
            screen.Economy.Grid.Apply(
                new DeploymentUnit("merge-target", unit.Id, 1, UnitFootprint.FromDefinition(unit)), anchor);
            screen.RefreshAll();

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(anchor, screen.HandCardFootprint(card)));

            Assert.That(screen.GetHighlight(anchor), Is.EqualTo(DeployCellHighlight.Merge));
            Assert.That(screen.FeedbackMessage, Does.Contain("可合成"));
        }

        [Test]
        public void DragOverAnOccupiedMismatch_HighlightsRedAndSaysWhyItCannotMerge()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out GameConfig config, out CardOfferItem card);
            var anchor = new GridCoordinate(3, 3);
            UnitDef blocker = config.GetUnit("gong");
            screen.Economy.Grid.Apply(
                new DeploymentUnit("blocker", blocker.Id, 1, UnitFootprint.FromDefinition(blocker)), anchor);
            screen.RefreshAll();

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(anchor, screen.HandCardFootprint(card)));

            Assert.That(screen.GetHighlight(anchor), Is.EqualTo(DeployCellHighlight.Invalid));
            Assert.That(screen.FeedbackMessage, Does.Contain("兵种不同"));
        }

        [Test]
        public void ThreeHighlightStates_AreVisuallyDistinctFromEachOtherAndFromTheIdleCell()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out GameConfig config, out CardOfferItem card);
            UnitFootprint footprint = screen.HandCardFootprint(card);
            var free = new GridCoordinate(2, 2);
            var merge = new GridCoordinate(3, 3);
            var locked = new GridCoordinate(0, 0);
            UnitDef unit = config.GetUnit("zu");
            screen.Economy.Grid.Apply(
                new DeploymentUnit("merge-target", unit.Id, 1, UnitFootprint.FromDefinition(unit)), merge);
            screen.RefreshAll();

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(free, footprint));
            Color validColor = CellColor(screen, free);
            screen.DragTo(screen.DragScreenPointForAnchor(merge, footprint));
            Color mergeColor = CellColor(screen, merge);
            screen.DragTo(screen.DragScreenPointForAnchor(locked, footprint));
            Color invalidColor = CellColor(screen, locked);
            screen.EndDrag();
            Color idleColor = CellColor(screen, free);

            Assert.That(Distance(validColor, mergeColor), Is.GreaterThan(0.25f), "valid vs merge");
            Assert.That(Distance(validColor, invalidColor), Is.GreaterThan(0.25f), "valid vs invalid");
            Assert.That(Distance(mergeColor, invalidColor), Is.GreaterThan(0.25f), "merge vs invalid");
            Assert.That(Distance(validColor, idleColor), Is.GreaterThan(0.25f), "valid vs idle cell");
        }

        [Test]
        public void DragWithinTheSnapRadius_SnapsTheGhostToTheCellCentre()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out _, out CardOfferItem card);
            UnitFootprint footprint = screen.HandCardFootprint(card);
            var anchor = new GridCoordinate(3, 3);
            Vector2 centred = screen.DragScreenPointForAnchor(anchor, footprint);
            float step = screen.CellEdge + screen.CellSpacing;
            float radiusPixels = screen.SnapRadiusCells * step;

            screen.BeginCardDrag(card, centred);
            Assert.That(screen.DragSnapped, Is.True, "Dead centre on a legal cell must snap.");

            // Just inside the radius still snaps, and keeps naming the same anchor.
            screen.DragTo(centred + new Vector2(radiusPixels * 0.5f, 0f));
            Assert.That(screen.DragSnapped, Is.True);
            Assert.That(screen.DragAnchor, Is.EqualTo(anchor));

            // Distance is measured to the NEAREST cell, so the only way out of every cell's radius
            // is the diagonal gap between four of them: 0.45 cells on both axes is 0.64 away.
            screen.DragTo(centred + new Vector2(step * 0.45f, step * 0.45f));
            Assert.That(screen.SnapRadiusCells, Is.LessThan(0.63f),
                "A radius at or above the 0.707 half-diagonal would snap everywhere and mean nothing.");
            Assert.That(screen.DragSnapped, Is.False, "Beyond the configured radius the ghost stays free.");
        }

        [Test]
        public void SnapNeverEngagesOnAnIllegalAnchor()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out _, out CardOfferItem card);
            var locked = new GridCoordinate(0, 0);

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(locked, screen.HandCardFootprint(card)));

            Assert.That(screen.DragSnapped, Is.False,
                "Snapping to a cell the grid rejects would tell the player a lie.");
        }

        [UnityTest]
        public IEnumerator ReleasingOnAnIllegalCell_ReturnsToHandInsteadOfVanishing()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out _, out CardOfferItem card);
            int handBefore = screen.RenderedHandCount;
            int placementsBefore = screen.Economy.Grid.Placements.Count;

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(
                new GridCoordinate(0, 0), screen.HandCardFootprint(card)));
            DeployDragOutcome outcome = screen.EndDrag();

            Assert.That(outcome, Is.EqualTo(DeployDragOutcome.Rejected));
            Assert.That(screen.IsReturningToHand, Is.True, "A rejected card must animate back, not disappear.");
            Assert.That(screen.Economy.Grid.Placements, Has.Count.EqualTo(placementsBefore));

            float deadline = Time.realtimeSinceStartup + 2f;
            while (screen.IsReturningToHand && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(screen.IsReturningToHand, Is.False, "The return animation must finish.");
            Assert.That(screen.RenderedHandCount, Is.EqualTo(handBefore), "The card is still in hand.");
            Assert.That(screen.IsDragging, Is.False);
        }

        [Test]
        public void ReleasingOnALegalCell_PlacesThroughCardEconomyAndClearsTheDrag()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out _, out CardOfferItem card);
            int placementsBefore = screen.Economy.Grid.Placements.Count;

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(
                new GridCoordinate(3, 3), screen.HandCardFootprint(card)));
            DeployDragOutcome outcome = screen.EndDrag();

            Assert.That(outcome, Is.EqualTo(DeployDragOutcome.Placed));
            Assert.That(screen.Economy.Grid.Placements, Has.Count.EqualTo(placementsBefore + 1));
            Assert.That(screen.IsDragging, Is.False);
            Assert.That(screen.FeedbackMessage, Does.Contain("部署成功"));
        }

        [Test]
        public void ReleasingOnAMatchingUnit_ReportsAMergeOutcome()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out GameConfig config, out CardOfferItem card);
            var anchor = new GridCoordinate(3, 3);
            UnitDef unit = config.GetUnit("zu");
            screen.Economy.Grid.Apply(
                new DeploymentUnit("merge-target", unit.Id, 1, UnitFootprint.FromDefinition(unit)), anchor);
            screen.RefreshAll();

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(anchor, screen.HandCardFootprint(card)));
            DeployDragOutcome outcome = screen.EndDrag();

            Assert.That(outcome, Is.EqualTo(DeployDragOutcome.Merged));
            Assert.That(screen.Economy.Grid.Placements.Single().Level, Is.EqualTo(2));
        }

        [Test]
        public void DragCues_FireForPickupSnapAndRejection()
        {
            var cues = new List<DeployDragCue>();
            DeployScreen screen = CreateScreenWithUnitInHand(
                "zu", out _, out CardOfferItem card, cue => cues.Add(cue));
            UnitFootprint footprint = screen.HandCardFootprint(card);

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(new GridCoordinate(3, 3), footprint));
            screen.DragTo(screen.DragScreenPointForAnchor(new GridCoordinate(0, 0), footprint));
            screen.EndDrag();

            Assert.That(cues, Does.Contain(DeployDragCue.Pickup));
            Assert.That(cues, Does.Contain(DeployDragCue.Snap));
            Assert.That(cues, Does.Contain(DeployDragCue.Rejected));
            Assert.That(cues, Has.No.Member(DeployDragCue.Placed));
        }

        [Test]
        public void DraggingAnLShape_HighlightsOnlyItsThreeCellsAndSkipsTheNotch()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("nuc", out _, out CardOfferItem card);
            UnitFootprint footprint = screen.HandCardFootprint(card);
            var anchor = new GridCoordinate(2, 2);

            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(anchor, footprint));

            foreach (GridCoordinate offset in footprint.OccupiedOffsets)
            {
                Assert.That(screen.GetHighlight(anchor + offset), Is.Not.EqualTo(DeployCellHighlight.None));
            }
            Assert.That(
                screen.GetHighlight(anchor + new GridCoordinate(1, 1)),
                Is.EqualTo(DeployCellHighlight.None),
                "The notched corner must stay unhighlighted.");
        }

        // ------------------------------------------------------------------------------ helpers

        private static float Span(int cells, float edge, float spacing)
        {
            return (cells * edge) + ((cells - 1) * spacing);
        }

        private static Color CellColor(DeployScreen screen, GridCoordinate cell)
        {
            Transform grid = screen.transform.Find("Deployment Grid Panel/Deployment Grid");
            Transform node = grid.Find($"Cell {cell.Column},{cell.Row}");
            return node.GetComponent<Image>().color;
        }

        private static float Distance(Color left, Color right)
        {
            return new Vector3(left.r - right.r, left.g - right.g, left.b - right.b).magnitude;
        }

        private DeployScreen CreateScreen(out CardEconomy economy, out GameConfig config)
        {
            config = GameConfig.Load();
            economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC4D001u);
            return Host(config, economy, null);
        }

        /// <summary>
        /// Refreshes the deterministic offer until the wanted unit is in hand. The screen rebuilds
        /// its hand from the CardEconomy event, so the card under test is a real hand card.
        /// </summary>
        private DeployScreen CreateScreenWithUnitInHand(
            string unitId,
            out GameConfig config,
            out CardOfferItem card,
            Action<DeployDragCue> onDragCue = null)
        {
            config = GameConfig.Load();
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC4D001u);
            economy.State.Coins = int.MaxValue;
            DeployScreen screen = Host(config, economy, onDragCue);

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

            throw new AssertionException($"'{unitId}' never appeared within the deterministic refresh bound.");
        }

        private DeployScreen Host(GameConfig config, CardEconomy economy, Action<DeployDragCue> onDragCue)
        {
            var canvasObject = new GameObject(
                "Deploy Shape Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            cleanup.Add(canvasObject);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1080f, 1920f);

            var screenObject = new GameObject("DeployRoot", typeof(RectTransform));
            screenObject.transform.SetParent(canvasObject.transform, false);
            cleanup.Add(screenObject);
            DeployScreen screen = screenObject.AddComponent<DeployScreen>();
            screen.Initialize(config, economy, new NullArtSource(), null, onDragCue: onDragCue);
            return screen;
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
