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
    /// WO-C10: the card's layer order. WO-C9 put a level-coloured fill on top of the artwork and
    /// left the picture as a 30%-alpha ghost, which is the original complaint ("拖拽前是图片，落位后
    /// 是纯文字") one step less severe. The reference art layers it the other way: artwork is the
    /// card's body, the name sits on it with an outline, and the level colour survives as border,
    /// badge and a thin wash.
    ///
    /// <para>These assert structure, not pixels — which object is parented where, in what order,
    /// with what clipping. That is the part a screenshot cannot pin and a refactor can silently
    /// invert.</para>
    /// </summary>
    public sealed class DeployCardLayeringTests
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

        // ------------------------------------------------------------ artwork is the card's body

        /// <summary>
        /// Artwork exists and is drawn <b>under</b> the name but <b>over</b> the fill. Sibling order
        /// is what enforces that in uGUI, so this reads the order out rather than trusting a comment.
        /// </summary>
        [TestCase("zu")]
        [TestCase("zqi")]
        [TestCase("tie")]
        public void DeployedCard_DrawsArtworkBeneathTheNameAndAboveTheFill(string unitId)
        {
            // Labels forced on: WO-C11 drops them from multi-cell cards, but the layer *order* is
            // what this pins, and it has to hold wherever a name is drawn at all.
            DeployScreen screen = CreateLabelledScreen(out GameConfig config);
            string id = Deploy(screen, config, unitId, 1, new GridCoordinate(2, 2));
            Transform card = FindCard(screen, id);

            int fill = IndexOfFirst(card, "Tile");
            int art = IndexOfFirst(card, "Artwork");
            int wash = IndexOfFirst(card, "Tint");
            int name = IndexOfFirst(card, "Unit Name");

            Assert.That(art, Is.GreaterThanOrEqualTo(0), "the card must draw its artwork at all");
            Assert.That(art, Is.GreaterThan(fill), "artwork sits above the bed, not under it");
            Assert.That(wash, Is.GreaterThan(art), "the level wash goes over the artwork");
            Assert.That(name, Is.GreaterThan(wash), "and the name goes over everything");
        }

        /// <summary>
        /// The level wash must stay thin enough to see through. This is the exact knob that, turned
        /// up, reproduces WO-C9's buried artwork — so it is pinned to the config value and the
        /// config's own ceiling rather than left to drift.
        /// </summary>
        [Test]
        public void LevelWash_IsThinEnoughToLeaveTheArtworkVisible()
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, "zu", 3, new GridCoordinate(2, 2));

            Image wash = FindAll<Image>(FindCard(screen, id), "Tint").Single();
            float configured = config.Economy.DeployUi.UnitCardTintAlpha;

            Assert.That(wash.color.a, Is.EqualTo(configured).Within(0.001f), "the wash comes from JSON");
            Assert.That(configured, Is.LessThan(0.6f),
                "past this the wash becomes a fill again and the artwork disappears");
            Assert.That(wash.color.r, Is.EqualTo(screen.TierFillColor(3).r).Within(0.001f),
                "and it is still the level's own colour");
        }

        /// <summary>
        /// A notched unit clips its picture to the real silhouette — one masked copy per tile,
        /// reassembled into a single image. WO-C4 first asked for this and it had never been done:
        /// the art used to fill the 2x2 bounding box and spill into the missing corner.
        /// </summary>
        [TestCase("nuc")]
        [TestCase("chc")]
        public void NotchedCard_ClipsItsArtworkToTheTilesThatExist(string unitId)
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, unitId, 1, new GridCoordinate(2, 2));
            Transform card = FindCard(screen, id);

            int tiles = screen.DeployedCardCellOffsets(id).Count;
            Assert.That(tiles, Is.EqualTo(3), "fixture: this unit really is notched");
            Assert.That(FindAll<Image>(card, "Artwork"), Has.Count.EqualTo(tiles),
                "one artwork copy per surviving tile");
            Assert.That(card.GetComponentsInChildren<RectMask2D>(true), Has.Length.EqualTo(tiles),
                "each copy is clipped to its own tile, so the notch holds no picture");
        }

        /// <summary>
        /// The rectangular majority takes the cheap path: the bounding box already is the
        /// silhouette, so masking every tile would only buy canvas batch breaks.
        /// </summary>
        [TestCase("zu")]
        [TestCase("zqi")]
        [TestCase("tie")]
        public void RectangularCard_DrawsOneUnclippedArtworkWithNoMaskOverhead(string unitId)
        {
            DeployScreen screen = CreateScreen(out GameConfig config);
            string id = Deploy(screen, config, unitId, 1, new GridCoordinate(2, 2));
            Transform card = FindCard(screen, id);

            Assert.That(FindAll<Image>(card, "Artwork"), Has.Count.EqualTo(1));
            Assert.That(card.GetComponentsInChildren<RectMask2D>(true), Is.Empty,
                "a rectangle needs no clipping — masks here would be pure cost");
        }

        /// <summary>A light name on a light patch of artwork needs its own contrast.</summary>
        [Test]
        public void UnitName_CarriesAnOutlineSoItReadsOverAnyArtwork()
        {
            DeployScreen screen = CreateLabelledScreen(out GameConfig config);
            string id = Deploy(screen, config, "tie", 2, new GridCoordinate(2, 2));

            Text name = FindAll<Text>(FindCard(screen, id), "Unit Name").Single();
            var outline = name.GetComponent<Outline>();

            Assert.That(outline, Is.Not.Null, "the name must be outlined now that it sits on a picture");
            Assert.That(outline.effectColor.a, Is.GreaterThan(0.5f));
            Assert.That(Mathf.Abs(outline.effectDistance.x), Is.GreaterThanOrEqualTo(1.5f));
        }

        // ------------------------------------------------------------ type scales with the card

        /// <summary>
        /// Type size must be derived from the card, not effectively constant. Measured before this
        /// change, a 3x1 「重骑兵」 and a 1x1 「卒」 both came out at 66pt because the depth cap was
        /// the only binding term — so a card three times as long carried identical type.
        /// </summary>
        [Test]
        public void NameFontSize_GrowsWithTheCardRatherThanStayingConstant()
        {
            DeployScreen screen = CreateLabelledScreen(out GameConfig config);
            string small = Deploy(screen, config, "zu", 1, new GridCoordinate(2, 2));
            string medium = Deploy(screen, config, "dun", 1, new GridCoordinate(1, 4));
            string large = Deploy(screen, config, "zqi", 1, new GridCoordinate(2, 6));

            int one = FontOf(screen, small);
            int two = FontOf(screen, medium);
            int three = FontOf(screen, large);

            Assert.That(two, Is.GreaterThan(one), "a 2x1 carries bigger type than a 1x1");
            Assert.That(three, Is.GreaterThan(two), "and a 3x1 bigger again");
        }

        /// <summary>
        /// Growing type must not overflow the card. The depth still bounds it, because a horizontal
        /// card only ever has one cell of height to write in however long it gets.
        /// </summary>
        [TestCase("zu")]
        [TestCase("dun")]
        [TestCase("zqi")]
        [TestCase("mao")]
        public void NameFontSize_NeverOutgrowsTheCardItSitsOn(string unitId)
        {
            DeployScreen screen = CreateLabelledScreen(out GameConfig config);
            string id = Deploy(screen, config, unitId, 1, new GridCoordinate(2, 2));

            Vector2 card = screen.DeployedCardSize(id);
            float depth = Mathf.Min(card.x, card.y);

            Assert.That(FontOf(screen, id), Is.LessThanOrEqualTo(depth * 0.76f),
                "type has to stay inside the border it is written on");
        }

        // ------------------------------------------------------------ one visual language in hand

        /// <summary>
        /// An effect card used to be a bare coloured rectangle beside fully dressed unit cards —
        /// two languages in one row, and nothing to say how good the effect was. It now carries the
        /// same border, badge and outlined name.
        /// </summary>
        [Test]
        public void EffectCard_WearsTheSameBorderAndBadgeAsAUnitCard()
        {
            DeployScreen screen = CreateScreenWithCard(
                CardCategory.Buff, null, true, out GameConfig config, out CardOfferItem card);
            Transform visual = FindHandCard(screen, card);

            Assert.That(FindAll<Image>(visual, "Edge"), Is.Not.Empty, "effect cards get a border too");
            Assert.That(FindAll<Text>(visual, "Level"), Is.Not.Empty, "and a corner badge");
            Assert.That(
                FindAll<Text>(visual, "Unit Name").Single().text.Replace("\n", string.Empty),
                Is.EqualTo(config.GetEffect(card.ContentId).Name),
                "showing the effect's own name");
        }

        /// <summary>
        /// The badge marks kind, not level. Printing a "1" on a buff would assert something untrue —
        /// effects have no level — so rarity rides the palette and the badge says what the card is.
        /// </summary>
        [Test]
        public void EffectCardBadge_MarksItsKindAndItsRarityRidesTheSharedPalette()
        {
            DeployScreen screen = CreateScreenWithCard(
                CardCategory.Buff, null, true, out GameConfig config, out CardOfferItem card);
            Transform visual = FindHandCard(screen, card);
            EffectDef effect = config.GetEffect(card.ContentId);

            string badge = FindAll<Text>(visual, "Level").Single().text;
            Assert.That(badge, Is.EqualTo("增"), "a buff is marked as a buff, not as level 1");

            int expected = RarityLevel(effect.Rarity);
            Image edge = FindAll<Image>(visual, "Edge").First();
            Assert.That(edge.color, Is.EqualTo(screen.TierBorderColor(expected)),
                $"'{effect.Rarity}' must read in the same colour language units use");
        }

        /// <summary>An unlock card is not a level-1 anything either, so it gets its own mark.</summary>
        [Test]
        public void UnlockCard_KeepsItsOwnMarkRatherThanBorrowingALevel()
        {
            // Deliberately does NOT open the grid: the pool stops offering unlock cards once every
            // cell is already unlocked, which is correct behaviour and would starve this fixture.
            DeployScreen screen = CreateScreenWithCard(
                CardCategory.Unlock, null, false, out _, out CardOfferItem card);

            Assert.That(FindAll<Text>(FindHandCard(screen, card), "Level").Single().text, Is.EqualTo("锁"));
        }

        // ------------------------------------------------------------ the hand row fills its panel

        /// <summary>
        /// The row was already centred — measured, its midpoint was 0.00 — but the cards bunched in
        /// the middle third and left the panel looking empty on both sides. Gaps now open up to
        /// spread the same cards across most of the row, which is what "右侧留白" was really about.
        /// </summary>
        [Test]
        public void HandRow_StaysCentredAndNowFillsMostOfThePanel()
        {
            DeployScreen screen = CreateScreen(out _);
            RectTransform row = screen.GetComponentsInChildren<RectTransform>(true)
                .First(value => value.gameObject.name == "Cards");
            row.ForceUpdateRectTransforms();

            var cards = Enumerable.Range(0, row.childCount)
                .Select(index => (RectTransform)row.GetChild(index))
                .ToArray();
            Assert.That(cards, Is.Not.Empty);

            float left = cards.Min(c => c.anchoredPosition.x - (c.sizeDelta.x * 0.5f));
            float right = cards.Max(c => c.anchoredPosition.x + (c.sizeDelta.x * 0.5f));

            Assert.That((left + right) * 0.5f, Is.EqualTo(0f).Within(1f), "still centred");
            Assert.That(right - left, Is.GreaterThan(row.rect.width * 0.5f),
                "and no longer huddled in the middle third");
            Assert.That(right - left, Is.LessThanOrEqualTo(row.rect.width),
                "without pushing cards off the panel");
        }

        // ------------------------------------------------------------ the WO-C4 bound still holds

        /// <summary>
        /// Artwork, masks and washes all live on cards, which are rebuilt only when the deployment
        /// set changes. A drag frame must still touch at most twice the dragged footprint — the
        /// WO-C4 bound, restated against the heavier renderer.
        /// </summary>
        [Test]
        public void DragFrames_StayBoundedWithTheHeavierCardRenderer()
        {
            DeployScreen screen = CreateScreenWithCard("zu", out GameConfig config, out CardOfferItem card);
            Deploy(screen, config, "nuc", 2, new GridCoordinate(1, 1));
            Deploy(screen, config, "zqi", 3, new GridCoordinate(2, 4));

            UnitFootprint footprint = screen.HandCardFootprint(card);
            var anchors = new[]
            {
                new GridCoordinate(3, 2), new GridCoordinate(4, 3),
                new GridCoordinate(3, 3), new GridCoordinate(4, 2)
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

            Assert.That(worst, Is.LessThanOrEqualTo(2 * footprint.OccupiedCellCount));
        }

        // ------------------------------------------------------------------------------ fixtures

        private static int RarityLevel(string rarity)
        {
            switch (rarity)
            {
                case "Common": return 1;
                case "Uncommon": return 2;
                case "Rare": return 3;
                case "Epic": return 4;
                case "Commander": return 5;
                default: return 1;
            }
        }

        private static int FontOf(DeployScreen screen, string deploymentId)
        {
            return FindAll<Text>(FindCard(screen, deploymentId), "Unit Name").Single().fontSize;
        }

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

        /// <summary>Sibling index of the first descendant whose name starts with <paramref name="name"/>.</summary>
        private static int IndexOfFirst(Transform card, string name)
        {
            for (int index = 0; index < card.childCount; index++)
            {
                Transform child = card.GetChild(index);
                if (child.gameObject.name.StartsWith(name, StringComparison.Ordinal))
                {
                    return index;
                }

                // Notched cards park artwork and wash inside their tiles so the mask can clip them;
                // a tile's own children are drawn with the tile, so report the tile's index.
                for (int inner = 0; inner < child.childCount; inner++)
                {
                    if (child.GetChild(inner).gameObject.name.StartsWith(name, StringComparison.Ordinal))
                    {
                        return index;
                    }
                }
            }
            return -1;
        }

        private static string Deploy(
            DeployScreen screen, GameConfig config, string unitId, int level, GridCoordinate anchor)
        {
            UnitDef definition = config.GetUnit(unitId);
            string id = $"c10-{unitId}";
            screen.Economy.Grid.Apply(
                new DeploymentUnit(id, definition.Id, level, UnitFootprint.FromDefinition(definition)), anchor);
            screen.Economy.SnapshotToRunState();
            screen.RefreshAll();
            return id;
        }

        /// <summary>
        /// A screen with name labels forced on for every footprint. WO-C11 suppresses them on cards
        /// big enough to show their artwork, but the layering and type rules still govern every
        /// label that does get drawn — raising the config threshold is exactly how a shipping build
        /// would turn them back on, so this exercises the real path.
        /// </summary>
        private DeployScreen CreateLabelledScreen(out GameConfig config)
        {
            config = GameConfig.Load();
            config.Economy.DeployUi.NameLabelMaxFootprintCells = 9;
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC10001u);
            economy.State.Coins = int.MaxValue;
            DeployScreen screen = Host(config, economy);
            OpenWholeGrid(economy);
            screen.RefreshAll();
            return screen;
        }

        private DeployScreen CreateScreen(out GameConfig config)
        {
            config = GameConfig.Load();
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC10001u);
            economy.State.Coins = int.MaxValue;
            DeployScreen screen = Host(config, economy);
            OpenWholeGrid(economy);
            screen.RefreshAll();
            return screen;
        }

        private DeployScreen CreateScreenWithCard(
            string unitId, out GameConfig config, out CardOfferItem card)
        {
            return CreateScreenWithCard(CardCategory.Unit, unitId, true, out config, out card);
        }

        /// <summary>Refreshes deterministically until the offer holds the card kind under test.</summary>
        private DeployScreen CreateScreenWithCard(
            CardCategory category,
            string contentId,
            bool openGrid,
            out GameConfig config,
            out CardOfferItem card)
        {
            config = GameConfig.Load();
            CardEconomy economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC10001u);
            economy.State.Coins = int.MaxValue;
            DeployScreen screen = Host(config, economy);
            if (openGrid)
            {
                OpenWholeGrid(economy);
            }
            screen.RefreshAll();

            for (int attempt = 0; attempt < 4000; attempt++)
            {
                CardOfferItem match = economy.CurrentOffer.Cards.FirstOrDefault(
                    value => value.Category == category
                             && (contentId == null
                                 || string.Equals(value.ContentId, contentId, StringComparison.Ordinal)));
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

        /// <summary>Opens every cell so fixtures can place wide units anywhere on the board.</summary>
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
                "Deploy Layering Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cleanup.Add(canvasObject);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 1920f);

            var screenObject = new GameObject("DeployRoot", typeof(RectTransform));
            screenObject.transform.SetParent(canvasObject.transform, false);
            cleanup.Add(screenObject);
            DeployScreen screen = screenObject.AddComponent<DeployScreen>();

            // The real art source, not a null stub: these tests are about how artwork is layered and
            // clipped, and a stub that returns no sprite would make every one of them vacuously pass.
            screen.Initialize(config, economy, new ResourcesBattleArtSource(), null);
            return screen;
        }
    }
}
