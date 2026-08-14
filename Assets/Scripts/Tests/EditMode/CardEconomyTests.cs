using System;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode
{
    public sealed class CardEconomyTests
    {
        private GameConfig config;

        [SetUp]
        public void SetUp() => config = GameConfig.Load();

        [Test]
        public void StartNew_UsesLevelEconomyAndPersistsAllRngStreams()
        {
            CardEconomy economy = Start();

            Assert.That(economy.State.Coins, Is.EqualTo(45));
            Assert.That(economy.State.StageIndex, Is.EqualTo(1));
            Assert.That(economy.Grid.Width, Is.EqualTo(7));
            Assert.That(economy.Grid.UnlockedCellCount, Is.EqualTo(9));
            Assert.That(economy.CurrentOffer, Is.Null);
            Assert.That(economy.State.RngStreamsState.battle, Is.Not.Null);
            Assert.That(economy.State.RngStreamsState.cardDraw, Is.Not.Null);
            Assert.That(economy.State.RngStreamsState.settlement, Is.Not.Null);
        }

        [Test]
        public void StartNew_CanonicalizesCallerLevelToJsonDefinition()
        {
            var forged = new LevelDef
            {
                Id = "level_1_1",
                StageIndex = 99,
                StartCoins = 999,
                GridWidth = 3,
                GridHeight = 1,
                InitialUnlock = new GridRectDef { Col = 0, Row = 0, Width = 1, Height = 1 }
            };

            CardEconomy economy = CardEconomy.StartNew(config, forged, 123u);

            Assert.That(economy.State.Coins, Is.EqualTo(45));
            Assert.That(economy.State.StageIndex, Is.EqualTo(1));
            Assert.That(economy.Grid.Width, Is.EqualTo(7));
            Assert.That(economy.Grid.Height, Is.EqualTo(7));
            Assert.That(economy.Grid.UnlockedCellCount, Is.EqualTo(9));
        }

        [TestCase(false, 3)]
        [TestCase(true, 4)]
        public void DrawOffer_NormalAndLuckyRollUseConfiguredCardCounts(bool lucky, int expectedCount)
        {
            CardOffer offer = Start(SeedForLucky(lucky)).DrawOffer();

            Assert.That(offer.IsLucky, Is.EqualTo(lucky));
            Assert.That(offer.Cards, Has.Count.EqualTo(expectedCount));
        }

        [Test]
        public void DrawOffer_IsOneFreeOfferPerMinorStage()
        {
            CardEconomy economy = Start();
            CardOffer first = economy.DrawOffer();

            Assert.That(economy.State.Coins, Is.EqualTo(45));
            Assert.That(economy.CurrentOffer, Is.SameAs(first));
            Assert.Throws<InvalidOperationException>(() => economy.DrawOffer());
        }

        [Test]
        public void DrawOffer_TenThousandCategorySamplesStayInsideExpectedIntervals()
        {
            var counts = new int[4];
            int total = 0;
            CardEconomy economy = Start(seed: 0xC2C2C2u, coins: int.MaxValue);
            AddSamples(economy.DrawOffer(), counts, ref total, 10000);

            while (total < 10000)
            {
                Assert.That(economy.TryRefresh(out CardOffer offer), Is.True);
                AddSamples(offer, counts, ref total, 10000);
            }

            AssertRatio(counts[(int)CardCategory.Unit], total, 0.68f, 0.72f, "unit");
            AssertRatio(counts[(int)CardCategory.Unlock], total, 0.13f, 0.17f, "unlock");
            AssertRatio(counts[(int)CardCategory.Buff], total, 0.10f, 0.14f, "buff");
            AssertRatio(counts[(int)CardCategory.Global], total, 0.02f, 0.04f, "global");
        }

        [Test]
        public void GetEffectiveWeights_DefaultGridMatchesConfiguredSeventyFifteenTwelveThree()
        {
            EffectiveCardWeights weights = Start().GetEffectiveWeights();

            Assert.That(weights.Unit, Is.EqualTo(70f).Within(0.0001f));
            Assert.That(weights.Unlock, Is.EqualTo(15f).Within(0.0001f));
            Assert.That(weights.Buff, Is.EqualTo(12f).Within(0.0001f));
            Assert.That(weights.Global, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(weights.Total, Is.EqualTo(100f).Within(0.0001f));
        }

        [Test]
        public void GetEffectiveWeights_FullyUnlockedFieldZerosUnlockAndRedistributesProportionally()
        {
            CardEconomy economy = StartFullyUnlocked();
            EffectiveCardWeights weights = economy.GetEffectiveWeights();

            Assert.That(economy.Grid.IsFullyUnlocked, Is.True);
            Assert.That(weights.Unlock, Is.Zero);
            Assert.That(weights.Total, Is.EqualTo(100f).Within(0.0001f));
            Assert.That(weights.Unit / weights.Buff, Is.EqualTo(70f / 12f).Within(0.0001f));
            Assert.That(weights.Unit / weights.Global, Is.EqualTo(70f / 3f).Within(0.0001f));
        }

        [Test]
        public void DrawOffer_FullyUnlockedFieldNeverProducesUnlockCards()
        {
            CardEconomy economy = StartFullyUnlocked(coins: 1000000);
            var categories = new List<CardCategory>();
            categories.AddRange(economy.DrawOffer().Cards.Select(card => card.Category));
            for (int index = 0; index < 500; index++)
            {
                economy.TryRefresh(out CardOffer offer);
                categories.AddRange(offer.Cards.Select(card => card.Category));
            }

            Assert.That(categories.Contains(CardCategory.Unlock), Is.False);
        }

        [Test]
        public void DrawOffer_NarrowMaskDropsEveryUnitShapeThatCannotFitAndRedistributesTheRest()
        {
            CardEconomy economy = StartWithUnlockedRect(1, 3, coins: 1000000, seed: 0x5721Fu);
            HashSet<string> eligible = UnitCardPoolPolicy.GetEligibleUnits(config, economy.Grid)
                .Select(unit => unit.Id).ToHashSet(StringComparer.Ordinal);
            var unitIds = new List<string>();
            unitIds.AddRange(economy.DrawOffer().Cards.Where(IsUnit).Select(card => card.ContentId));
            for (int index = 0; index < 500; index++)
            {
                economy.TryRefresh(out CardOffer offer);
                unitIds.AddRange(offer.Cards.Where(IsUnit).Select(card => card.ContentId));
            }

            Assert.That(eligible, Is.EquivalentTo(new[] { "zu", "gong", "huo", "bing", "mao", "nub" }));
            Assert.That(unitIds, Is.Not.Empty);
            Assert.That(unitIds.All(eligible.Contains), Is.True, "no locked shape may leak into the hand");
            foreach (string wide in new[] { "tie", "zqi", "dun", "dao", "qqi", "lia", "nuc", "chc" })
            {
                Assert.That(unitIds, Does.Not.Contain(wide));
            }
            // Weight zero for the excluded shapes means the survivors absorb their share.
            Assert.That(eligible.All(unitIds.Contains), Is.True, "every eligible unit still appears");
        }

        [Test]
        public void DrawOffer_StartingCentreMaskAlreadyOffersEveryAllyUnitShape()
        {
            CardEconomy economy = Start(coins: 1000000);
            HashSet<string> eligible = UnitCardPoolPolicy.GetEligibleUnits(config, economy.Grid)
                .Select(unit => unit.Id).ToHashSet(StringComparer.Ordinal);
            var unitIds = new List<string>();
            unitIds.AddRange(economy.DrawOffer().Cards.Where(IsUnit).Select(card => card.ContentId));
            for (int index = 0; index < 500; index++)
            {
                economy.TryRefresh(out CardOffer offer);
                unitIds.AddRange(offer.Cards.Where(IsUnit).Select(card => card.ContentId));
            }

            Assert.That(eligible, Is.EquivalentTo(config.AllyUnits.Select(unit => unit.Id)));
            Assert.That(unitIds.All(eligible.Contains), Is.True);
        }

        [Test]
        public void DrawOffer_StageFourGuaranteeOccupiesOneSlotAndIsSiege()
        {
            CardEconomy economy = Start(stageIndex: 4, seed: 0x51554945u);
            CardOffer offer = economy.DrawOffer();

            Assert.That(offer.Cards[0].Category, Is.EqualTo(CardCategory.Unit));
            Assert.That(config.GetUnit(offer.Cards[0].ContentId).AtkType, Is.EqualTo(AttackType.Siege));
            Assert.That(economy.State.HasOfferedGuaranteedMachineryCard, Is.True);
            Assert.That(offer.Cards, Has.Count.EqualTo(offer.IsLucky ? 4 : 3));
        }

        [TestCase(0, 15)]
        [TestCase(1, 20)]
        [TestCase(2, 25)]
        public void NextRefreshCost_FollowsFormula(int refreshCount, int expected)
        {
            CardEconomy economy = Start(coins: 100);
            economy.State.RefreshCount = refreshCount;

            Assert.That(economy.NextRefreshCost, Is.EqualTo(expected));
            Assert.That(economy.NextRefreshCost, Is.EqualTo(Formula.RefreshCost(refreshCount, config.Economy)));
        }

        [Test]
        public void TryRefresh_SuccessChargesAndReplacesWholeHand()
        {
            CardEconomy economy = Start(coins: 100);
            CardOffer first = economy.DrawOffer();

            Assert.That(economy.TryRefresh(out CardOffer second), Is.True);

            Assert.That(economy.State.Coins, Is.EqualTo(85));
            Assert.That(economy.State.RefreshCount, Is.EqualTo(1));
            Assert.That(economy.NextRefreshCost, Is.EqualTo(20));
            Assert.That(economy.CurrentOffer, Is.SameAs(second));
            Assert.That(economy.CurrentOffer, Is.Not.SameAs(first));
        }

        [Test]
        public void TryRefresh_BeforeFreeOfferIsRejectedWithoutMutation()
        {
            CardEconomy economy = Start(coins: 100);
            RngStreamsState before = economy.RandomStreams.SaveState();

            Assert.Throws<InvalidOperationException>(() => economy.TryRefresh(out _));

            Assert.That(economy.State.Coins, Is.EqualTo(100));
            Assert.That(economy.State.RefreshCount, Is.Zero);
            AssertStreamsEqual(before, economy.RandomStreams.SaveState());
        }

        [Test]
        public void TryRefresh_InsufficientCoinsIsAtomicAndConsumesNoRng()
        {
            CardEconomy economy = Start(coins: 14, stageIndex: 4, seed: 0xBAD5EEDu);
            CardOffer current = economy.DrawOffer();
            RngStreamsState before = economy.RandomStreams.SaveState();
            bool guarantee = economy.State.HasOfferedGuaranteedMachineryCard;

            Assert.That(economy.TryRefresh(out CardOffer offer), Is.False);

            Assert.That(offer, Is.Null);
            Assert.That(economy.CurrentOffer, Is.SameAs(current));
            Assert.That(economy.State.Coins, Is.EqualTo(14));
            Assert.That(economy.State.RefreshCount, Is.Zero);
            Assert.That(economy.State.HasOfferedGuaranteedMachineryCard, Is.EqualTo(guarantee));
            AssertStreamsEqual(before, economy.RandomStreams.SaveState());
        }

        [Test]
        public void TryPlayUnlockCard_LegalAnchorOpensCellsAndConsumesTheCardForFree()
        {
            CardEconomy economy = EconomyWithCurrentCard(CardCategory.Unlock, coins: 200);
            CardOfferItem card = economy.CurrentOffer.Cards.First(IsUnlock);
            int beforeCoins = economy.State.Coins;
            int beforeCells = economy.Grid.UnlockedCellCount;
            GridCoordinate anchor = economy.Grid.GetLegalUnlockAnchors(card.Unlock)[0];

            Assert.That(economy.TryPlayUnlockCard(card, anchor, out UnlockApplyResult result), Is.True);

            Assert.That(result.NewlyUnlockedCells, Is.Not.Empty);
            Assert.That(economy.Grid.UnlockedCellCount,
                Is.EqualTo(beforeCells + result.NewlyUnlockedCells.Count));
            Assert.That(economy.State.Coins, Is.EqualTo(beforeCoins), "acquiring the card is what costs");
            Assert.That(economy.State.UnlockedCells.Count(value => value),
                Is.EqualTo(economy.Grid.UnlockedCellCount));
            Assert.That(economy.CurrentOffer.Cards.Contains(card), Is.False);
        }

        [Test]
        public void TryPlayUnlockCard_IllegalAnchorIsAtomicAndKeepsTheCardInHand()
        {
            CardEconomy economy = EconomyWithCurrentCard(CardCategory.Unlock, coins: 200);
            CardOfferItem card = economy.CurrentOffer.Cards.First(IsUnlock);
            int beforeCells = economy.Grid.UnlockedCellCount;
            RngStreamsState before = economy.RandomStreams.SaveState();

            Assert.That(economy.TryPlayUnlockCard(card, new GridCoordinate(0, 0), out UnlockApplyResult result), Is.False);

            Assert.That(result, Is.Null);
            Assert.That(economy.Grid.UnlockedCellCount, Is.EqualTo(beforeCells));
            Assert.That(economy.CurrentOffer.Cards, Does.Contain(card));
            AssertStreamsEqual(before, economy.RandomStreams.SaveState());
        }

        [Test]
        public void TryPlayUnlockCard_ForgedCardIsRejectedWithoutMutation()
        {
            CardEconomy economy = EconomyWithCurrentCard(CardCategory.Unlock, coins: 200);
            var forged = new CardOfferItem(UnlockCardFactory.Create(UnlockCardShape.OneByOne));
            int beforeCells = economy.Grid.UnlockedCellCount;

            Assert.Throws<InvalidOperationException>(() =>
                economy.TryPlayUnlockCard(forged, new GridCoordinate(3, 1), out _));
            Assert.That(economy.Grid.UnlockedCellCount, Is.EqualTo(beforeCells));
        }

        [TestCase(0, 40)]
        [TestCase(1, 60)]
        [TestCase(2, 80)]
        public void NextUnlockPurchaseCost_FollowsTheJsonPriceCurve(int purchaseCount, int expected)
        {
            CardEconomy economy = Start(coins: 1000);
            economy.State.UnlockPurchaseCount = purchaseCount;

            Assert.That(economy.NextUnlockPurchaseCost, Is.EqualTo(expected));
            Assert.That(economy.NextUnlockPurchaseCost,
                Is.EqualTo(Formula.UnlockPurchaseCost(purchaseCount, config.Economy)));
        }

        [Test]
        public void TryPurchaseUnlockCard_ChargesTheRisingPriceAndAddsOneCardToTheHand()
        {
            CardEconomy economy = Start(coins: 1000);
            economy.DrawOffer();
            int handSize = economy.CurrentOffer.Cards.Count;

            Assert.That(economy.TryPurchaseUnlockCard(out CardOfferItem first), Is.True);
            Assert.That(economy.TryPurchaseUnlockCard(out CardOfferItem second), Is.True);

            Assert.That(first.Category, Is.EqualTo(CardCategory.Unlock));
            Assert.That(first.Unlock, Is.Not.Null);
            Assert.That(second.Unlock, Is.Not.Null);
            Assert.That(economy.State.Coins, Is.EqualTo(1000 - 40 - 60));
            Assert.That(economy.State.UnlockPurchaseCount, Is.EqualTo(2));
            Assert.That(economy.NextUnlockPurchaseCost, Is.EqualTo(80));
            Assert.That(economy.CurrentOffer.Cards, Has.Count.EqualTo(handSize + 2));
            Assert.That(economy.CurrentOffer.Cards, Does.Contain(first));
        }

        [Test]
        public void TryPurchaseUnlockCard_InsufficientCoinsIsAtomicAndConsumesNoRng()
        {
            CardEconomy economy = Start(coins: 39);
            economy.DrawOffer();
            int handSize = economy.CurrentOffer.Cards.Count;
            RngStreamsState before = economy.RandomStreams.SaveState();

            Assert.That(economy.CanPurchaseUnlockCard, Is.False);
            Assert.That(economy.TryPurchaseUnlockCard(out CardOfferItem card), Is.False);

            Assert.That(card, Is.Null);
            Assert.That(economy.State.Coins, Is.EqualTo(39));
            Assert.That(economy.State.UnlockPurchaseCount, Is.Zero);
            Assert.That(economy.CurrentOffer.Cards, Has.Count.EqualTo(handSize));
            AssertStreamsEqual(before, economy.RandomStreams.SaveState());
        }

        [Test]
        public void TryGrantUnlockCard_AddsAFreeCardAndStopsOnceTheFieldIsFullyUnlocked()
        {
            CardEconomy economy = Start(coins: 45);
            economy.DrawOffer();
            int handSize = economy.CurrentOffer.Cards.Count;

            Assert.That(economy.TryGrantUnlockCard(out CardOfferItem card), Is.True);

            Assert.That(card.Category, Is.EqualTo(CardCategory.Unlock));
            Assert.That(economy.State.Coins, Is.EqualTo(45), "the ad, not coins, pays for this card");
            Assert.That(economy.CurrentOffer.Cards, Has.Count.EqualTo(handSize + 1));

            CardEconomy full = StartFullyUnlocked(coins: 1000);
            full.DrawOffer();
            Assert.That(full.TryGrantUnlockCard(out CardOfferItem none), Is.False);
            Assert.That(none, Is.Null);
            Assert.That(full.TryPurchaseUnlockCard(out _), Is.False);
            Assert.That(full.CanPurchaseUnlockCard, Is.False);
        }

        [Test]
        public void AutoUnlockForClearedMinorStage_OpensTheConfiguredBudgetThenStopsQuietly()
        {
            CardEconomy economy = Start();

            IReadOnlyList<GridCoordinate> opened = economy.AutoUnlockForClearedMinorStage();

            Assert.That(opened, Has.Count.EqualTo(config.Economy.GridUnlock.AutoUnlockPerMinorStage));
            Assert.That(opened[0], Is.EqualTo(new GridCoordinate(3, 1)));
            Assert.That(economy.Grid.UnlockedCellCount, Is.EqualTo(10));
            Assert.That(economy.State.UnlockedCells.Count(value => value), Is.EqualTo(10));

            CardEconomy full = StartFullyUnlocked();
            Assert.That(full.AutoUnlockForClearedMinorStage(), Is.Empty);
            Assert.That(full.Grid.UnlockedCellCount, Is.EqualTo(49));
        }

        [Test]
        public void CreditBattleDrops_AddsCoinsAndRejectsNegativeAmounts()
        {
            CardEconomy economy = Start();
            economy.CreditBattleDrops(17);
            Assert.That(economy.State.Coins, Is.EqualTo(62));
            Assert.Throws<ArgumentOutOfRangeException>(() => economy.CreditBattleDrops(-1));
            Assert.That(economy.State.Coins, Is.EqualTo(62));
        }

        [Test]
        public void DrawOffer_ContentIdsAlwaysComeFromConfiguredCategoryPools()
        {
            CardEconomy economy = Start(coins: 1000000);
            HashSet<string> units = UnitCardPoolPolicy.GetEligibleUnits(config, economy.Grid)
                .Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
            HashSet<string> buffs = config.Economy.CardPool.BuffEffectIds.ToHashSet(StringComparer.Ordinal);
            HashSet<string> globals = config.Economy.CardPool.GlobalEffectIds.ToHashSet(StringComparer.Ordinal);
            var cards = new List<CardOfferItem>(economy.DrawOffer().Cards);
            for (int index = 0; index < 500; index++)
            {
                economy.TryRefresh(out CardOffer offer);
                cards.AddRange(offer.Cards);
            }

            foreach (CardOfferItem card in cards)
            {
                switch (card.Category)
                {
                    case CardCategory.Unit: Assert.That(units, Does.Contain(card.ContentId)); break;
                    case CardCategory.Unlock:
                        Assert.That(card.Unlock, Is.Not.Null);
                        Assert.That(card.ContentId, Is.EqualTo(card.Unlock.ContentId));
                        Assert.That(UnlockCardFactory.TryParse(card.ContentId, out _), Is.True);
                        break;
                    case CardCategory.Buff: Assert.That(buffs, Does.Contain(card.ContentId)); break;
                    case CardCategory.Global: Assert.That(globals, Does.Contain(card.ContentId)); break;
                    default: Assert.Fail($"Unexpected card category {card.Category}."); break;
                }
            }
        }

        [Test]
        public void AcquireEffect_StackRuleRecordsDuplicateAndConsumesEachRealCard()
        {
            CardEconomy economy = EconomyWithCurrentCard(CardCategory.Buff, coins: 1000000);
            CardOfferItem first = economy.CurrentOffer.Cards.First(IsBuff);
            economy.AcquireEffect(first);
            Assert.That(economy.CurrentOffer.Cards.Contains(first), Is.False);

            CardOfferItem second = RefreshUntil(economy, IsBuff);
            economy.AcquireEffect(second);

            Assert.That(economy.State.OwnedEffects.Count(value => value == "buff_front_shield"), Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => economy.AcquireEffect(second));
        }

        [Test]
        public void PlaceUnit_ConsumesHandCardAndRejectsReuseOrForgery()
        {
            CardEconomy economy = EconomyWithCurrentCard(CardCategory.Unit);
            CardOfferItem card = economy.CurrentOffer.Cards.First(IsUnit);

            DeploymentApplyResult result = economy.PlaceUnit(card, StartAnchor());

            Assert.That(result.Action, Is.EqualTo(DeploymentActionKind.Place));
            Assert.That(economy.CurrentOffer.Cards.Contains(card), Is.False);
            Assert.Throws<InvalidOperationException>(() => economy.PlaceUnit(card, StartAnchor(0, 2)));
            Assert.Throws<InvalidOperationException>(() => economy.PlaceUnit(
                new CardOfferItem(CardCategory.Unit, "gong"), StartAnchor(0, 2)));
        }

        [Test]
        public void PlaceUnit_FailedPlacementDoesNotMergeOrConsumeCurrentCard()
        {
            CardEconomy economy = EconomyWithCurrentCard(CardCategory.Unit, seed: 0xFA11EDu);
            CardOfferItem card = economy.CurrentOffer.Cards.First(IsUnit);
            RngStreamsState before = economy.RandomStreams.SaveState();

            Assert.Throws<InvalidOperationException>(() => economy.PlaceUnit(card, new GridCoordinate(-1, 3)));

            Assert.That(economy.Grid.Placements, Is.Empty);
            Assert.That(economy.CurrentOffer.Cards, Does.Contain(card));
            AssertStreamsEqual(before, economy.RandomStreams.SaveState());
        }

        [Test]
        public void AdvanceToMinorStage_InheritsRunStateAndReopensOneFreeOffer()
        {
            CardEconomy economy = EconomyWithCurrentCard(CardCategory.Unit, coins: 1000000, seed: 0x1A2B3C4Du);
            CardOfferItem unit = economy.CurrentOffer.Cards.First(IsUnit);
            economy.PlaceUnit(unit, "saved-unit", StartAnchor());
            CardOfferItem buff = RefreshUntil(economy, IsBuff);
            economy.AcquireEffect(buff);
            economy.TryRefresh(out _);
            economy.State.HasOfferedGuaranteedMachineryCard = true;
            economy.SnapshotToRunState();
            int coins = economy.State.Coins;
            int refreshes = economy.State.RefreshCount;
            RngStreamsState rng = economy.State.RngStreamsState;

            economy.AdvanceToMinorStage(config.GetLevel("level_1_2"));

            Assert.That(economy.State.StageIndex, Is.EqualTo(2));
            Assert.That(economy.State.Coins, Is.EqualTo(coins));
            Assert.That(economy.State.RefreshCount, Is.EqualTo(refreshes));
            Assert.That(economy.State.OwnedEffects, Does.Contain("buff_front_shield"));
            Assert.That(economy.State.DeployedGrid.Single().DeploymentId, Is.EqualTo("saved-unit"));
            Assert.That(economy.State.HasOfferedGuaranteedMachineryCard, Is.True);
            Assert.That(economy.CurrentOffer, Is.Null);
            AssertStreamsEqual(rng, economy.State.RngStreamsState);
            Assert.That(economy.DrawOffer(), Is.Not.Null, "next minor stage gets one free offer");
            Assert.Throws<InvalidOperationException>(() => economy.DrawOffer());
        }

        [Test]
        public void ResetForMajorStage_ClearsRunDataHandAndReseedsStreams()
        {
            CardEconomy economy = EconomyWithCurrentCard(CardCategory.Unit, coins: 1000000, seed: 111u);
            economy.PlaceUnit(economy.CurrentOffer.Cards.First(IsUnit), "saved-unit", StartAnchor());
            economy.State.OwnedEffects = new[] { "buff_front_shield" };
            economy.State.RefreshCount = 3;
            economy.State.HasOfferedGuaranteedMachineryCard = true;

            economy.ResetForMajorStage(config.GetLevel("level_1_1"), 222u);

            Assert.That(economy.State.Coins, Is.EqualTo(45));
            Assert.That(economy.State.RefreshCount, Is.Zero);
            Assert.That(economy.State.StageIndex, Is.EqualTo(1));
            Assert.That(economy.State.GridWidth, Is.EqualTo(7));
            Assert.That(economy.State.UnlockedCells.Count(value => value), Is.EqualTo(9));
            Assert.That(economy.State.UnlockPurchaseCount, Is.Zero);
            Assert.That(economy.State.DeployedGrid, Is.Empty);
            Assert.That(economy.State.OwnedEffects, Is.Empty);
            Assert.That(economy.State.HasOfferedGuaranteedMachineryCard, Is.False);
            Assert.That(economy.RandomStreams.MasterSeed, Is.EqualTo(222u));
            Assert.That(economy.CurrentOffer, Is.Null);
            Assert.That(economy.DrawOffer(), Is.Not.Null);
        }

        [Test]
        public void Restore_RoundTripsLShapePlacementRngAndAutomaticDeploymentSequence()
        {
            var state = new RunState
            {
                Coins = 200,
                RefreshCount = 2,
                StageIndex = 1,
                GridWidth = 7,
                GridHeight = 7,
                Seed = 0x51504E47u,
                OwnedEffects = new[] { "buff_front_shield" },
                DeployedGrid = new[]
                {
                    new DeployedUnitState { DeploymentId = "deploy_00000007", UnitId = "nuc", Level = 1, Col = 3, Row = 3 }
                },
                RngStreamsState = new RngStreams(0x51504E47u).SaveState()
            };
            CardEconomy restored = CardEconomy.Restore(config, config.GetLevel("level_1_1"), state);
            DeploymentPlacement lShape = restored.Grid.Placements.Single();

            Assert.That(restored.State.Coins, Is.EqualTo(200));
            Assert.That(restored.State.RefreshCount, Is.EqualTo(2));
            Assert.That(restored.State.OwnedEffects, Is.EqualTo(new[] { "buff_front_shield" }));
            Assert.That(lShape.Footprint.OccupiedCellCount, Is.EqualTo(3));
            CardOfferItem oneByOne = DrawUntilUnit(restored, unit => unit.GridW == 1 && unit.GridH == 1);
            DeploymentApplyResult placed = restored.PlaceUnit(oneByOne, new GridCoordinate(2, 2));

            Assert.That(placed.Placement.DeploymentId, Is.EqualTo("deploy_00000008"));
            Assert.That(restored.State.DeployedGrid, Has.Length.EqualTo(2));
        }

        [Test]
        public void SameSeed_ProducesIdenticalOfferSequenceEntryByEntry()
        {
            CardEconomy first = Start(0x5A4D45u, coins: 1000000);
            CardEconomy second = Start(0x5A4D45u, coins: 1000000);
            AssertOffersEqual(first.DrawOffer(), second.DrawOffer());
            for (int index = 0; index < 100; index++)
            {
                first.TryRefresh(out CardOffer firstOffer);
                second.TryRefresh(out CardOffer secondOffer);
                AssertOffersEqual(firstOffer, secondOffer);
            }
        }

        [Test]
        public void ConsumingBattleAndSettlementStreams_DoesNotAffectCardDrawSequence()
        {
            CardEconomy consumed = Start(0x51545245u, coins: 1000000);
            CardEconomy untouched = Start(0x51545245u, coins: 1000000);
            for (int index = 0; index < 200; index++)
            {
                consumed.RandomStreams.Battle.NextUInt();
                consumed.RandomStreams.Settlement.NextUInt();
            }

            AssertOffersEqual(consumed.DrawOffer(), untouched.DrawOffer());
            for (int index = 0; index < 100; index++)
            {
                consumed.TryRefresh(out CardOffer consumedOffer);
                untouched.TryRefresh(out CardOffer untouchedOffer);
                AssertOffersEqual(consumedOffer, untouchedOffer);
            }
        }

        private CardEconomy Start(uint seed = 0xC2DEC0DEu, int coins = 45, int stageIndex = 1)
        {
            LevelDef level = config.GetLevel($"level_1_{stageIndex}");
            CardEconomy economy = CardEconomy.StartNew(config, level, seed);
            economy.State.Coins = coins;
            return economy;
        }

        /// <summary>Restores a run whose unlock mask is a rectangle centred on the playfield.</summary>
        private CardEconomy StartWithUnlockedRect(
            int width,
            int height,
            int coins = 200,
            uint seed = 0xA11CEu)
        {
            LevelDef level = config.GetLevel("level_1_1");
            var streams = new RngStreams(seed);
            var mask = new bool[level.GridWidth * level.GridHeight];
            int firstColumn = (level.GridWidth - width) / 2;
            int firstRow = (level.GridHeight - height) / 2;
            for (int row = firstRow; row < firstRow + height; row++)
            for (int column = firstColumn; column < firstColumn + width; column++)
            {
                mask[(row * level.GridWidth) + column] = true;
            }

            var state = new RunState
            {
                Coins = coins,
                StageIndex = 1,
                GridWidth = level.GridWidth,
                GridHeight = level.GridHeight,
                UnlockedCells = mask,
                Seed = streams.MasterSeed,
                DeployedGrid = Array.Empty<DeployedUnitState>(),
                OwnedEffects = Array.Empty<string>(),
                RngStreamsState = streams.SaveState()
            };
            return CardEconomy.Restore(config, level, state);
        }

        private CardEconomy StartFullyUnlocked(int coins = 200, uint seed = 0xA11CEu)
        {
            return StartWithUnlockedRect(7, 7, coins, seed);
        }

        /// <summary>The anchor of the level's starting unlock rect, where a 1x1 always fits.</summary>
        private GridCoordinate StartAnchor(int columnOffset = 0, int rowOffset = 0)
        {
            GridRectDef rect = config.GetLevel("level_1_1").InitialUnlock;
            return new GridCoordinate(rect.Col + columnOffset, rect.Row + rowOffset);
        }

        private CardEconomy EconomyWithCurrentCard(
            CardCategory category,
            int coins = 200,
            uint seed = 0xC4A4D5u)
        {
            CardEconomy economy = Start(seed, coins);
            CardOffer offer = economy.DrawOffer();
            for (int attempt = 0; attempt < 2000 && !offer.Cards.Any(card => card.Category == category); attempt++)
            {
                Assert.That(economy.TryRefresh(out offer), Is.True);
            }
            Assert.That(offer.Cards.Any(card => card.Category == category), Is.True,
                $"No {category} card was drawn within the deterministic search bound.");
            return economy;
        }

        private CardOfferItem RefreshUntil(CardEconomy economy, Func<CardOfferItem, bool> predicate)
        {
            for (int attempt = 0; attempt < 2000; attempt++)
            {
                Assert.That(economy.TryRefresh(out CardOffer offer), Is.True);
                CardOfferItem match = offer.Cards.FirstOrDefault(predicate);
                if (match != null) return match;
            }
            throw new AssertionException("No matching card was drawn within the deterministic search bound.");
        }

        private CardOfferItem DrawUntilUnit(CardEconomy economy, Func<UnitDef, bool> predicate)
        {
            CardOffer offer = economy.DrawOffer();
            for (int attempt = 0; attempt < 2000; attempt++)
            {
                CardOfferItem match = offer.Cards.FirstOrDefault(card =>
                    card.Category == CardCategory.Unit && predicate(config.GetUnit(card.ContentId)));
                if (match != null) return match;
                economy.State.Coins = 1000000;
                Assert.That(economy.TryRefresh(out offer), Is.True);
            }
            throw new AssertionException("No matching unit card was drawn within the deterministic search bound.");
        }

        private static void AddSamples(CardOffer offer, int[] counts, ref int total, int maximum)
        {
            foreach (CardOfferItem card in offer.Cards)
            {
                if (total == maximum) return;
                counts[(int)card.Category]++;
                total++;
            }
        }

        private static bool IsUnit(CardOfferItem card) => card.Category == CardCategory.Unit;
        private static bool IsUnlock(CardOfferItem card) => card.Category == CardCategory.Unlock;
        private static bool IsBuff(CardOfferItem card) => card.Category == CardCategory.Buff;

        private static uint SeedForLucky(bool expectedLucky)
        {
            for (uint seed = 1; seed < 100000; seed++)
            {
                if ((new RngStreams(seed).CardDraw.NextFloat() < 0.10f) == expectedLucky) return seed;
            }
            throw new AssertionException($"Could not find a seed whose lucky result is {expectedLucky}.");
        }

        private static void AssertRatio(int count, int total, float minimum, float maximum, string label)
        {
            float ratio = (float)count / total;
            Assert.That(ratio, Is.InRange(minimum, maximum), $"{label} ratio ({count}/{total})");
        }

        private static void AssertOffersEqual(CardOffer expected, CardOffer actual)
        {
            Assert.That(actual.IsLucky, Is.EqualTo(expected.IsLucky));
            Assert.That(actual.Cards.Count, Is.EqualTo(expected.Cards.Count));
            for (int index = 0; index < expected.Cards.Count; index++)
            {
                Assert.That(actual.Cards[index].Category, Is.EqualTo(expected.Cards[index].Category), $"card {index}");
                Assert.That(actual.Cards[index].ContentId, Is.EqualTo(expected.Cards[index].ContentId), $"card {index}");
            }
        }

        private static void AssertStreamsEqual(RngStreamsState expected, RngStreamsState actual)
        {
            Assert.That(actual.battle.state, Is.EqualTo(expected.battle.state), "battle stream");
            Assert.That(actual.cardDraw.state, Is.EqualTo(expected.cardDraw.state), "card draw stream");
            Assert.That(actual.settlement.state, Is.EqualTo(expected.settlement.state), "settlement stream");
        }
    }
}
