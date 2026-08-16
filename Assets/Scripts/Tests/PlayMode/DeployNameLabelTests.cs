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
    /// WO-C11: a card big enough to show its artwork must not also write the unit's name.
    ///
    /// <para>This only became visible once WO-C10 promoted the artwork to the card's body. The art
    /// in this game <i>is</i> the character the unit is named after, so the label prints the same
    /// word a second time — on 铁甲兵 at a different size and position, and on 弩车 with the gold
    /// strokes of the picture and the white strokes of the label interleaved until neither reads.
    /// A 1x1 is exempt: its label covers the whole card and barely any artwork survives behind it,
    /// so there the text is what identifies the unit.</para>
    ///
    /// <para>The rule is judged on footprint, never on rendered size, because hand cards are drawn
    /// at 70% scale — a pixel threshold would give the same unit different answers in hand and on
    /// the board.</para>
    /// </summary>
    public sealed class DeployNameLabelTests
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

            EventSystem[] systems = UnityEngine.Object.FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < systems.Length; index++)
            {
                if (systems[index] != null && systems[index].gameObject.name == "M1 EventSystem")
                {
                    UnityEngine.Object.DestroyImmediate(systems[index].gameObject);
                }
            }
        }

        /// <summary>Every 1x1 keeps its name; everything larger drops it.</summary>
        [TestCase("zu", 1, true)]
        [TestCase("gong", 1, true)]
        [TestCase("huo", 1, true)]
        [TestCase("bing", 1, true)]
        [TestCase("dun", 2, false)]
        [TestCase("mao", 2, false)]
        [TestCase("zqi", 3, false)]
        [TestCase("tie", 4, false)]
        [TestCase("nuc", 3, false)]
        [TestCase("chc", 3, false)]
        public void DeployedCard_WritesItsNameOnlyWhenTooSmallToShowArtwork(
            string unitId, int occupiedCells, bool expectName)
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, unitId, 1, new GridCoordinate(2, 2));

            Assert.That(screen.DeployedCardCellOffsets(id), Has.Count.EqualTo(occupiedCells),
                "fixture: the footprint really is this size");
            Assert.That(FindAll<Text>(FindCard(screen, id), "Unit Name"),
                expectName ? Has.Count.EqualTo(1) : Is.Empty,
                expectName
                    ? "a 1x1 has no room for artwork behind its label, so it keeps the name"
                    : "a card this size shows the character as artwork already");
        }

        /// <summary>
        /// The threshold is data, not a constant. Raising it in JSON must bring every label back —
        /// which is also what makes the labelled fixtures in the WO-C9/C10 suites honest.
        /// </summary>
        [Test]
        public void TheThresholdComesFromJsonAndRaisingItRestoresEveryLabel()
        {
            Assert.That(GameConfig.Load().Economy.DeployUi.NameLabelMaxFootprintCells, Is.EqualTo(1),
                "shipping config labels 1x1 cards only");

            DeployScreen screen = CreateScreen(out GameConfig config, 9);
            string big = Deploy(screen, config, "tie", 1, new GridCoordinate(2, 2));

            Assert.That(FindAll<Text>(FindCard(screen, big), "Unit Name"), Has.Count.EqualTo(1),
                "raising the threshold must be all it takes to write names again");
        }

        /// <summary>
        /// Hand and board must agree. They render at different scales, so a rule written against
        /// pixels would show a name in one place and hide it in the other for the same unit.
        /// </summary>
        [TestCase("zu", true)]
        [TestCase("dun", false)]
        [TestCase("zqi", false)]
        public void HandCard_FollowsTheSameFootprintRuleAsTheBoardDespiteItsSmallerScale(
            string unitId, bool expectName)
        {
            DeployScreen screen = CreateScreenWithUnitInHand(unitId, out _, out CardOfferItem card);

            Assert.That(screen.HandCellEdge, Is.LessThan(screen.CellEdge),
                "fixture: the hand really is drawn smaller, so a pixel rule would diverge here");
            Assert.That(FindAll<Text>(FindHandCard(screen, card), "Unit Name"),
                expectName ? Has.Count.EqualTo(1) : Is.Empty);
        }

        /// <summary>The carried ghost matches what will land, so nothing appears or vanishes on drop.</summary>
        [TestCase("zu", true)]
        [TestCase("zqi", false)]
        public void DragGhost_MatchesWhatTheCardWillLookLikeOnceItLands(string unitId, bool expectName)
        {
            DeployScreen screen = CreateScreenWithUnitInHand(unitId, out _, out CardOfferItem card);
            UnitFootprint footprint = screen.HandCardFootprint(card);

            Assert.That(
                screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(new GridCoordinate(3, 3), footprint)),
                Is.True);
            Transform ghost = screen.GetComponentsInChildren<Transform>(true)
                .First(value => value.gameObject.name == "Drag Ghost");

            Assert.That(FindAll<Text>(ghost, "Unit Name"), expectName ? Has.Count.EqualTo(1) : Is.Empty);
            Assert.That(FindAll<Text>(ghost, "Level"), Has.Count.EqualTo(1),
                "the badge rides along either way — on an unlabelled card it is the only mark left");
            screen.CancelDrag();
        }

        /// <summary>
        /// Unlock and effect cards keep their labels at every size. Nothing in their artwork spells
        /// out "3x1" or the effect's name, so for them the label is the only carrier of that fact —
        /// the duplication this rule removes simply does not exist there.
        /// </summary>
        [Test]
        public void UnlockAndEffectCards_KeepTheirLabelsBecauseNothingDuplicatesThem()
        {
            DeployScreen unlockScreen = CreateScreenWithCategory(
                CardCategory.Unlock, out _, out CardOfferItem unlock);
            Transform unlockCard = FindHandCard(unlockScreen, unlock);
            Assert.That(unlock.Unlock.Footprint.OccupiedCellCount, Is.GreaterThan(1),
                "fixture: a multi-cell unlock card, the case a naive rule would strip");
            Assert.That(FindAll<Text>(unlockCard, "Unit Name"), Has.Count.EqualTo(1),
                "the shape label is the only place the card's size is written");

            DeployScreen effectScreen = CreateScreenWithCategory(
                CardCategory.Buff, out GameConfig config, out CardOfferItem buff);
            Assert.That(
                FindAll<Text>(FindHandCard(effectScreen, buff), "Unit Name").Single().text
                    .Replace("\n", string.Empty),
                Is.EqualTo(config.GetEffect(buff.ContentId).Name));
        }

        /// <summary>
        /// With the name gone, the stat sheet is the only way left to identify a big unit — so the
        /// path to it has to keep working on exactly the cards that lost their label.
        /// </summary>
        [TestCase("tie")]
        [TestCase("zqi")]
        [TestCase("nuc")]
        [TestCase("chc")]
        public void TheInfoPanelStillIdentifiesAUnitThatNoLongerCarriesAName(string unitId)
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, unitId, 2, new GridCoordinate(2, 2));
            Assert.That(FindAll<Text>(FindCard(screen, id), "Unit Name"), Is.Empty,
                "fixture: this card really did lose its label");

            Assert.That(screen.ShowDeployedUnitInfo(id), Is.True, "the sheet must still open");
            Assert.That(screen.IsInfoPanelOpen, Is.True);
            Assert.That(screen.InfoPanelUnitId, Is.EqualTo(unitId));
            Assert.That(screen.InfoPanelLevel, Is.EqualTo(2));
            Assert.That(
                screen.InfoPanelRows.Any(row => row.Contains(config.GetUnit(unitId).Name)), Is.True,
                "and it must name the unit the card no longer does");
        }

        /// <summary>Tapping the card is the gesture that reaches the sheet, and it still works.</summary>
        [Test]
        public void TappingAnUnlabelledCard_OpensTheSheetThroughTheRealPointerPath()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, "tie", 1, new GridCoordinate(2, 2));
            Transform card = FindCard(screen, id);

            // Through the pointer interface rather than the concrete handler type, which is internal
            // to the view — and through ExecuteEvents rather than a direct method call, because a
            // direct call would skip the dispatch layer where WO-C4's drag bug actually lived.
            Assert.That(card.GetComponent<IPointerClickHandler>(), Is.Not.Null,
                "the whole card is still the tap target");
            ExecuteEvents.Execute(
                card.gameObject,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);

            Assert.That(screen.IsInfoPanelOpen, Is.True);
            Assert.That(screen.InfoPanelUnitId, Is.EqualTo("tie"));
        }

        /// <summary>The level badge is untouched: it is the one mark that never duplicated artwork.</summary>
        [TestCase("tie", 3)]
        [TestCase("zqi", 4)]
        public void UnlabelledCards_StillShowTheirLevelBadgeAndBorder(string unitId, int level)
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, unitId, level, new GridCoordinate(2, 2));

            Assert.That(screen.DeployedCardLevelBadge(id), Is.EqualTo(level.ToString()));
            Assert.That(screen.DeployedCardBorderColor(id), Is.EqualTo(screen.TierBorderColor(level)));
        }

        // ------------------------------------------------------------------------------ fixtures

        private static Transform FindCard(DeployScreen screen, string deploymentId)
        {
            Transform card = screen.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => value.gameObject.name == $"Unit {deploymentId}");
            Assert.That(card, Is.Not.Null, $"no card rendered for '{deploymentId}'");
            return card;
        }

        private static Transform FindHandCard(DeployScreen screen, CardOfferItem card)
        {
            RectTransform row = screen.GetComponentsInChildren<RectTransform>(true)
                .First(value => value.gameObject.name == "Cards");
            Transform found = Enumerable.Range(0, row.childCount)
                .Select(index => row.GetChild(index))
                .FirstOrDefault(child => child.gameObject.name.EndsWith(card.ContentId, StringComparison.Ordinal));
            Assert.That(found, Is.Not.Null, $"no hand card rendered for '{card.ContentId}'");
            return found;
        }

        private static List<T> FindAll<T>(Transform root, string name) where T : Component
        {
            return root.GetComponentsInChildren<T>(true)
                .Where(value => value.gameObject.name.StartsWith(name, StringComparison.Ordinal))
                .ToList();
        }

        private static string Deploy(
            DeployScreen screen, GameConfig config, string unitId, int level, GridCoordinate anchor)
        {
            UnitDef definition = config.GetUnit(unitId);
            string id = $"c11-{unitId}";
            screen.Economy.Grid.Apply(
                new DeploymentUnit(id, definition.Id, level, UnitFootprint.FromDefinition(definition)), anchor);
            screen.Economy.SnapshotToRunState();
            screen.RefreshAll();
            return id;
        }

        private DeployScreen CreateScreen(out GameConfig config)
        {
            return CreateScreen(out config, null);
        }

        private DeployScreen CreateScreen(out GameConfig config, int? labelThreshold)
        {
            config = GameConfig.Load();
            if (labelThreshold.HasValue)
            {
                config.Economy.DeployUi.NameLabelMaxFootprintCells = labelThreshold.Value;
            }
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC11001u);
            economy.State.Coins = int.MaxValue;
            DeployScreen screen = Host(config, economy);
            OpenWholeGrid(economy);
            screen.RefreshAll();
            return screen;
        }

        private DeployScreen CreateScreenWithUnitInHand(
            string unitId, out GameConfig config, out CardOfferItem card)
        {
            config = GameConfig.Load();
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC11001u);
            economy.State.Coins = int.MaxValue;
            DeployScreen screen = Host(config, economy);
            OpenWholeGrid(economy);
            screen.RefreshAll();

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

        /// <summary>
        /// Refreshes until the offer holds a card of this category. The grid stays closed, because a
        /// fully unlocked board correctly stops the pool from ever offering an unlock card.
        /// </summary>
        private DeployScreen CreateScreenWithCategory(
            CardCategory category, out GameConfig config, out CardOfferItem card)
        {
            config = GameConfig.Load();
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC11001u);
            economy.State.Coins = int.MaxValue;
            DeployScreen screen = Host(config, economy);

            for (int attempt = 0; attempt < 4000; attempt++)
            {
                CardOfferItem match = economy.CurrentOffer.Cards.FirstOrDefault(
                    value => value.Category == category
                             && (category != CardCategory.Unlock
                                 || value.Unlock.Footprint.OccupiedCellCount > 1));
                if (match != null)
                {
                    card = match;
                    return screen;
                }

                economy.State.Coins = int.MaxValue;
                Assert.That(economy.TryRefresh(out _), Is.True);
            }

            throw new AssertionException($"no {category} card appeared within the deterministic bound.");
        }

        private static void OpenWholeGrid(CardEconomy economy)
        {
            DeploymentGrid grid = economy.Grid;
            for (int sweep = 0; sweep < grid.Width + grid.Height && !grid.IsFullyUnlocked; sweep++)
            {
                bool progressed = false;
                for (int row = 0; row < grid.Height; row++)
                for (int column = 0; column < grid.Width; column++)
                {
                    var cell = new GridCoordinate(column, row);
                    if (grid.IsUnlocked(cell))
                    {
                        continue;
                    }

                    UnlockCard unlock = UnlockCardFactory.Create(UnlockCardShape.OneByOne);
                    if (grid.EvaluateUnlock(unlock, cell).IsValid)
                    {
                        grid.ApplyUnlock(unlock, cell);
                        progressed = true;
                    }
                }

                if (!progressed)
                {
                    break;
                }
            }
            economy.SnapshotToRunState();
        }

        private DeployScreen Host(GameConfig config, CardEconomy economy)
        {
            var canvasObject = new GameObject(
                "Deploy Name Label Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cleanup.Add(canvasObject);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 1920f);

            var screenObject = new GameObject("DeployRoot", typeof(RectTransform));
            screenObject.transform.SetParent(canvasObject.transform, false);
            cleanup.Add(screenObject);
            DeployScreen screen = screenObject.AddComponent<DeployScreen>();
            screen.Initialize(config, economy, new ResourcesBattleArtSource(), null);
            return screen;
        }
    }
}
