using System;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using HanziDefend.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HanziDefend.Tests.PlayMode
{
    /// <summary>
    /// WO-C9: a deployed unit must read as one card, at a level you can see, with a stat sheet
    /// behind it. The player's report was "根本看不懂我部署了什么，全都是蓝色，也看不懂等级" — the
    /// grid was drawing a unit as N recoloured cells, each stamped with the same name and the same
    /// blue, so a 2x2 wrote its name four times and every level looked identical.
    ///
    /// <para>Nothing here decides a rule. Levels come from DeploymentGrid, the palette from
    /// economy.json and every stat from GameConfig; these tests only assert the view says what those
    /// three already decided.</para>
    /// </summary>
    public sealed class DeployUnitCardTests
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
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < eventSystems.Length; index++)
            {
                if (eventSystems[index] != null && eventSystems[index].gameObject.name == "M1 EventSystem")
                {
                    UnityEngine.Object.DestroyImmediate(eventSystems[index].gameObject);
                }
            }
        }

        // --------------------------------------------------------------- 必改 1: whole-card render

        /// <summary>
        /// The headline change: one card per unit, whatever its footprint. A 2x2 used to produce
        /// four separately labelled cells, which is where "看不懂我部署了什么" came from.
        /// </summary>
        [TestCase("zu", 1)]
        [TestCase("dun", 2)]
        [TestCase("mao", 2)]
        [TestCase("tie", 4)]
        [TestCase("zqi", 3)]
        public void DeployedUnit_DrawsOneCardAcrossItsWholeFootprint(string unitId, int occupiedCells)
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, unitId, 1, new GridCoordinate(2, 2));

            Assert.That(screen.DeployedUnitCardCount, Is.EqualTo(1),
                "one deployed unit is one card, not one card per occupied cell");
            Assert.That(screen.DeployedCardCellOffsets(id), Has.Count.EqualTo(occupiedCells),
                "the card still covers every cell the unit occupies");
            Assert.That(
                screen.DeployedCardName(id).Replace("\n", string.Empty),
                Is.EqualTo(config.GetUnit(unitId).Name),
                "the name is written once, across the card");
        }

        /// <summary>
        /// Writing direction follows the card's shape, as the reference art does: a 3x1 reads left
        /// to right, a 1x2 stacks down the column. Forcing a tall card to read horizontally would
        /// either shrink the text to nothing or spill it over its neighbours.
        /// </summary>
        [TestCase("zqi", false)]
        [TestCase("dun", false)]
        [TestCase("mao", true)]
        [TestCase("nub", true)]
        public void DeployedCardName_RunsDownTallCardsAndAcrossWideOnes(string unitId, bool expectVertical)
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, unitId, 1, new GridCoordinate(2, 2));

            string rendered = screen.DeployedCardName(id);
            Assert.That(rendered.Replace("\n", string.Empty), Is.EqualTo(config.GetUnit(unitId).Name));
            Assert.That(rendered.Contains("\n"), Is.EqualTo(expectVertical),
                expectVertical ? "a tall card stacks its characters" : "a wide card writes across");
        }

        /// <summary>
        /// Both notched units, rendered from the same footprint data the rules use. A 2x2 card with
        /// a painted-on corner would pass a bounding-box check and still be a lie.
        /// </summary>
        [TestCase("nuc", UnitFootprintShape.MissingUpperRight, 1, 1)]
        [TestCase("chc", UnitFootprintShape.MissingLowerLeft, 0, 0)]
        public void DeployedLShape_DrawsThreeTilesAndReallyOmitsItsNotchedCorner(
            string unitId, UnitFootprintShape expectedShape, int missingColumn, int missingRow)
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, unitId, 1, new GridCoordinate(2, 2));

            IReadOnlyList<GridCoordinate> drawn = screen.DeployedCardCellOffsets(id);
            Assert.That(config.GetUnit(unitId).Footprint, Is.EqualTo(expectedShape));
            Assert.That(drawn, Has.Count.EqualTo(3), $"{unitId} must draw 3 tiles, not a filled 2x2");
            Assert.That(drawn, Has.No.Member(new GridCoordinate(missingColumn, missingRow)),
                $"{unitId} must leave its notched corner empty");

            // The bounding box is still square: the notch shows in which tiles exist, not in size.
            Vector2 size = screen.DeployedCardSize(id);
            Assert.That(size.x, Is.EqualTo(size.y).Within(0.01f));
        }

        /// <summary>An empty unlocked slot carries no text — labels belong to units, not to cells.</summary>
        [Test]
        public void EmptyUnlockedCells_CarryNoLabel()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            Deploy(screen, config, "zu", 1, new GridCoordinate(2, 2));

            Text[] labels = screen.GetComponentsInChildren<Text>(true)
                .Where(value => value.gameObject.name == "Cell Label")
                .ToArray();

            Assert.That(labels, Is.Not.Empty, "the cell label objects still exist");
            Assert.That(labels.All(value => string.IsNullOrEmpty(value.text)), Is.True,
                "no cell may stamp a unit's name on itself any more");
        }

        // --------------------------------------------------------------- 必改 2: readable levels

        /// <summary>Every level gets its own badge digit and its own colour, both from data.</summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void DeployedUnit_ShowsItsLevelAsABadgeAndInItsOwnColour(int level)
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, "zu", level, new GridCoordinate(2, 2));

            Assert.That(screen.DeployedCardLevelBadge(id), Is.EqualTo(level.ToString()));
            Assert.That(screen.DeployedCardFillColor(id), Is.EqualTo(screen.TierFillColor(level)));
            Assert.That(screen.DeployedCardBorderColor(id), Is.EqualTo(screen.TierBorderColor(level)));
        }

        /// <summary>
        /// The four reachable levels must be told apart at a glance, which is the whole point of
        /// "全都是蓝色". Distance is measured in RGB so a palette edit that quietly makes two levels
        /// similar goes red rather than shipping.
        /// </summary>
        [Test]
        public void TierColours_AreMutuallyDistinctAcrossEveryReachableLevel()
        {
            DeployScreen screen = CreateScreen(out _);

            for (int first = 1; first <= DeploymentGrid.MaximumUnitLevel; first++)
            {
                for (int second = first + 1; second <= DeploymentGrid.MaximumUnitLevel; second++)
                {
                    Assert.That(
                        Distance(screen.TierFillColor(first), screen.TierFillColor(second)),
                        Is.GreaterThan(0.25f),
                        $"level {first} and level {second} must not look alike");
                }
            }
        }

        /// <summary>
        /// The palette is deliberately one entry longer than merging can reach. This pins both
        /// halves at once: a fifth colour exists, and the merge ceiling did <b>not</b> move to meet
        /// it. Raising the ceiling to 5 without a design decision fails here.
        /// </summary>
        [Test]
        public void PaletteHasFiveLevels_ButMergingStillStopsAtFour()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);

            Assert.That(screen.TierColorCount, Is.EqualTo(5), "five colours are defined");
            Assert.That(screen.TierColorName(5), Is.EqualTo("红"), "the reserved fifth level is red");
            Assert.That(DeploymentGrid.MaximumUnitLevel, Is.EqualTo(4),
                "merging must still stop at 4 — the fifth colour is reserved, not reachable");
            Assert.That(Enum.GetValues(typeof(UnitTier)).Length, Is.EqualTo(4),
                "the tier enum must not have grown to match the palette");

            // And the rule layer really does refuse a fifth merge.
            UnitDef unit = config.GetUnit("zu");
            Assert.That(
                () => screen.Economy.Grid.Apply(
                    new DeploymentUnit("over", unit.Id, 5, UnitFootprint.FromDefinition(unit)),
                    new GridCoordinate(2, 2)),
                Throws.Exception,
                "a level-5 unit cannot be placed by the current rules");
        }

        // --------------------------------------------------------------- 必改 3: slot boundaries

        /// <summary>
        /// Each slot draws a floor inset inside its own tile, so the tile's rim reads as the cell
        /// boundary and stays visible around whatever stands in it. Without the rim, "拖上去不知道是
        /// 啥" — you cannot see where one cell ends and the next begins.
        /// </summary>
        [Test]
        public void EveryCell_DrawsAnInsetFloorSoItsBoundaryIsVisible()
        {
            DeployScreen screen = CreateScreen(out _);

            Image[] floors = screen.GetComponentsInChildren<Image>(true)
                .Where(value => value.gameObject.name == "Cell Floor")
                .ToArray();

            Assert.That(floors, Has.Length.EqualTo(screen.FieldCellCount), "one floor per cell");
            RectTransform floor = floors[0].rectTransform;
            Assert.That(floor.offsetMin.x, Is.GreaterThan(0f), "the floor is inset from the tile edge");
            Assert.That(floor.offsetMax.x, Is.LessThan(0f));
            Assert.That(floors.All(value => !value.raycastTarget), Is.True,
                "the floor must not steal the cell's pointer events");
        }

        /// <summary>
        /// The unit card sits inside its slots rather than covering them, so a drag preview painted
        /// on the cells underneath still shows as a ring around an occupied unit.
        /// </summary>
        [Test]
        public void DeployedCard_SitsInsideItsFootprintSoTheSlotRimStaysVisible()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, "zu", 1, new GridCoordinate(2, 2));

            Vector2 card = screen.DeployedCardSize(id);
            Assert.That(card.x, Is.GreaterThan(0f));
            Assert.That(card.x, Is.LessThanOrEqualTo(screen.CellEdge + 0.01f),
                "a 1x1 card must not spill past its own slot");
        }

        // --------------------------------------------------------------- 必改 4: undo a placement

        /// <summary>
        /// Dropping a deployed unit on the hand takes it back: the grid frees up and the card
        /// returns. Before WO-C9 a misdrop was permanent for the rest of the stage.
        /// </summary>
        [Test]
        public void DraggingADeployedUnitOntoTheHand_UndoesThePlacementAndReturnsTheCard()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, "zu", 1, new GridCoordinate(2, 2));
            int handBefore = screen.RenderedHandCount;

            Assert.That(screen.BeginPlacementDrag(id, screen.CellScreenPoint(new GridCoordinate(2, 2))), Is.True);
            screen.DragTo(HandScreenPoint(screen));
            DeployDragOutcome outcome = screen.EndDrag();

            Assert.That(outcome, Is.EqualTo(DeployDragOutcome.ReturnedToHand));
            Assert.That(screen.Economy.Grid.Placements, Is.Empty, "the cells are free again");
            Assert.That(screen.DeployedUnitCardCount, Is.Zero, "and the card is off the board");
            Assert.That(screen.RenderedHandCount, Is.EqualTo(handBefore + 1), "the card came back to hand");
            Assert.That(screen.Economy.CurrentOffer.Cards.Any(
                value => value.Category == CardCategory.Unit && value.ContentId == "zu"), Is.True);
        }

        /// <summary>
        /// A merged unit is refused, with a reason. Returning a level-3 unit as one card would
        /// silently burn two merges; returning three would mint cards. Either way the merge economy
        /// would move, and undoing a misdrop is not licence to reprice merging.
        /// </summary>
        [Test]
        public void DraggingAMergedUnitOntoTheHand_IsRefusedAndLeavesItOnTheGrid()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, "zu", 3, new GridCoordinate(2, 2));

            Assert.That(screen.BeginPlacementDrag(id, screen.CellScreenPoint(new GridCoordinate(2, 2))), Is.True);
            screen.DragTo(HandScreenPoint(screen));
            DeployDragOutcome outcome = screen.EndDrag();

            Assert.That(outcome, Is.EqualTo(DeployDragOutcome.Rejected));
            Assert.That(screen.Economy.Grid.Placements, Has.Count.EqualTo(1), "it stays deployed");
            Assert.That(screen.FeedbackMessage, Does.Contain("合成"), "and the player is told why");
        }

        /// <summary>The undo goes through CardEconomy, not through the view poking the grid.</summary>
        [Test]
        public void ReturnToHand_IsOwnedByCardEconomyAndRefusesUnknownIds()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            Deploy(screen, config, "zu", 1, new GridCoordinate(2, 2));

            Assert.That(screen.Economy.TryReturnUnitToHand("no-such-unit", out string reason), Is.False);
            Assert.That(reason, Is.Not.Empty);
            Assert.That(screen.Economy.Grid.Placements, Has.Count.EqualTo(1));
        }

        // --------------------------------------------------------------- 必改 5: the stat sheet

        /// <summary>Every field the work order names must be present, on a real unit.</summary>
        [Test]
        public void InfoPanel_ShowsEveryRequiredFieldForADeployedUnit()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, "nub", 2, new GridCoordinate(2, 2));

            Assert.That(screen.ShowDeployedUnitInfo(id), Is.True);
            Assert.That(screen.IsInfoPanelOpen, Is.True);

            string[] required =
            {
                "汉字名", "显示名", "占格", "等级", "HP", "ATK", "攻速", "射程",
                "移速", "护甲值", "穿透", "护甲类型", "攻击类型", "兵种类型", "对位加成", "特性"
            };
            foreach (string field in required)
            {
                Assert.That(
                    screen.InfoPanelRows.Any(row => row.StartsWith(field, StringComparison.Ordinal)),
                    Is.True,
                    $"the info panel must show 「{field}」");
            }
        }

        /// <summary>
        /// The numbers are GameConfig's, levelled by the same formula the battle uses. Recomputing
        /// them here from the definition is the point: if the panel ever hard-codes a table, the two
        /// sides diverge and this goes red.
        /// </summary>
        [Test]
        public void InfoPanel_ReadsItsNumbersFromGameConfigAtTheUnitsActualLevel()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            const int level = 3;
            string id = Deploy(screen, config, "gong", level, new GridCoordinate(2, 2));
            UnitDef definition = config.GetUnit("gong");

            screen.ShowDeployedUnitInfo(id);

            Assert.That(screen.InfoPanelUnitId, Is.EqualTo("gong"));
            Assert.That(screen.InfoPanelLevel, Is.EqualTo(level));
            AssertRow(screen, "汉字名", definition.Name);
            AssertRow(screen, "等级", level.ToString());
            AssertRow(screen, "HP", Expected(definition.Hp, level));
            AssertRow(screen, "ATK", Expected(definition.Atk, level));
            AssertRow(screen, "射程", Expected(definition.Range, level));
            AssertRow(screen, "护甲值", Expected(definition.Armor, level));
            AssertRow(screen, "攻击类型", "弓箭");
            AssertRow(screen, "兵种类型", "步兵");
        }

        /// <summary>A unit with a counter bonus and a trait must print both, not "无".</summary>
        [Test]
        public void InfoPanel_PrintsCounterBonusesAndTraits()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, "mao", 1, new GridCoordinate(2, 2));

            screen.ShowDeployedUnitInfo(id);

            Assert.That(config.GetUnit("mao").BonusVs, Is.Not.Empty, "长矛 really does counter cavalry");
            string bonus = Row(screen, "对位加成");
            Assert.That(bonus, Does.Contain("对骑兵"));
            Assert.That(bonus, Does.Not.Contain("无"));
        }

        /// <summary>
        /// The sheet closes on demand and leaves nothing behind that could swallow the next drag —
        /// the work order's "关掉后能立刻继续操作".
        /// </summary>
        [Test]
        public void InfoPanel_ClosesAndLeavesDraggingImmediatelyUsable()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, "zu", 1, new GridCoordinate(2, 2));
            screen.ShowDeployedUnitInfo(id);
            Assert.That(screen.IsInfoPanelOpen, Is.True);

            screen.CloseUnitInfo();

            Assert.That(screen.IsInfoPanelOpen, Is.False);
            Assert.That(screen.InfoPanelUnitId, Is.Empty);
            Assert.That(
                screen.BeginPlacementDrag(id, screen.CellScreenPoint(new GridCoordinate(2, 2))), Is.True,
                "dragging must work the instant the sheet is closed");
            screen.CancelDrag();
        }

        /// <summary>Tapping a hand card opens the same sheet at the card's own base level.</summary>
        [Test]
        public void TappingAHandCard_OpensTheSameSheet()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("dao", out GameConfig config, out CardOfferItem card);

            screen.ActivateCard(card);

            Assert.That(screen.IsInfoPanelOpen, Is.True);
            Assert.That(screen.InfoPanelUnitId, Is.EqualTo("dao"));
            Assert.That(screen.InfoPanelLevel, Is.EqualTo((int)config.GetUnit("dao").Tier));
        }

        // --------------------------------------------------------------- boundary: the C4 gates

        /// <summary>
        /// Unit cards must not creep into the drag path. They are rebuilt only when the deployment
        /// set changes, so a drag frame still repaints at most twice the dragged footprint even with
        /// a board full of cards — the WO-C4 bound, restated with the new renderer in place.
        /// </summary>
        [Test]
        public void DragFramesStayBounded_EvenWithABoardFullOfUnitCards()
        {
            DeployScreen screen = CreateScreenWithUnitInHand("zu", out GameConfig config, out CardOfferItem card);
            Deploy(screen, config, "gong", 2, new GridCoordinate(2, 2));
            Deploy(screen, config, "huo", 3, new GridCoordinate(2, 3));
            Deploy(screen, config, "bing", 4, new GridCoordinate(2, 4));
            Assert.That(screen.DeployedUnitCardCount, Is.EqualTo(3));

            UnitFootprint footprint = screen.HandCardFootprint(card);
            var anchors = new[]
            {
                new GridCoordinate(3, 2), new GridCoordinate(4, 2),
                new GridCoordinate(3, 3), new GridCoordinate(4, 4)
            };
            screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(anchors[0], footprint));

            int worst = 0;
            for (int frame = 0; frame < 24; frame++)
            {
                int before = screen.CellVisitCount;
                screen.DragTo(screen.DragScreenPointForAnchor(anchors[frame % anchors.Length], footprint));
                worst = Math.Max(worst, screen.CellVisitCount - before);
            }
            screen.CancelDrag();

            Assert.That(worst, Is.LessThanOrEqualTo(2 * footprint.OccupiedCellCount),
                "the new renderer must not add per-cell work to a drag frame");
        }

        // ------------------------------------------------------------------------------ fixtures

        private static string Expected(StatCurve curve, int level)
        {
            return Formula.StatAtLevel(curve.Base, curve.Growth, level).ToString("0.##");
        }

        private static string Row(DeployScreen screen, string label)
        {
            return screen.InfoPanelRows.FirstOrDefault(
                value => value.StartsWith(label, StringComparison.Ordinal)) ?? string.Empty;
        }

        private static void AssertRow(DeployScreen screen, string label, string expected)
        {
            Assert.That(Row(screen, label), Does.Contain(expected), $"row 「{label}」");
        }

        private static float Distance(Color left, Color right)
        {
            return new Vector3(left.r - right.r, left.g - right.g, left.b - right.b).magnitude;
        }

        /// <summary>Screen point inside the hand panel, where a released unit is taken back.</summary>
        private static Vector2 HandScreenPoint(DeployScreen screen)
        {
            RectTransform hand = screen.GetComponentsInChildren<RectTransform>(true)
                .First(value => value.gameObject.name == "Hand Panel");
            Vector3[] corners = new Vector3[4];
            hand.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        /// <summary>
        /// Places a unit at a level straight through the grid, the way a finished drag or a merge
        /// would leave it, then refreshes as the screen's own commit path does.
        /// </summary>
        private static string Deploy(
            DeployScreen screen, GameConfig config, string unitId, int level, GridCoordinate anchor)
        {
            UnitDef definition = config.GetUnit(unitId);
            string id = $"c9-{unitId}-{anchor.Column}-{anchor.Row}";
            screen.Economy.Grid.Apply(
                new DeploymentUnit(id, definition.Id, level, UnitFootprint.FromDefinition(definition)), anchor);
            screen.Economy.SnapshotToRunState();
            screen.RefreshAll();
            return id;
        }

        private DeployScreen CreateScreen(out GameConfig config)
        {
            config = GameConfig.Load();
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC9D001u);
            economy.State.Coins = int.MaxValue;
            return Host(config, economy);
        }

        private DeployScreen CreateScreenWithUnitInHand(
            string unitId, out GameConfig config, out CardOfferItem card)
        {
            config = GameConfig.Load();
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC9D001u);
            economy.State.Coins = int.MaxValue;
            DeployScreen screen = Host(config, economy);

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

        private DeployScreen Host(GameConfig config, CardEconomy economy)
        {
            var canvasObject = new GameObject(
                "Deploy Unit Card Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cleanup.Add(canvasObject);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 1920f);

            var screenObject = new GameObject("DeployRoot", typeof(RectTransform));
            screenObject.transform.SetParent(canvasObject.transform, false);
            cleanup.Add(screenObject);
            DeployScreen screen = screenObject.AddComponent<DeployScreen>();
            screen.Initialize(config, economy, new NullArtSource(), null);
            return screen;
        }

        private sealed class NullArtSource : IBattleArtSource
        {
            public Sprite Find(string assetKey) => null;
        }
    }
}
