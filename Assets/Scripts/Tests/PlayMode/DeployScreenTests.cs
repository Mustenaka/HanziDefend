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
    public sealed class DeployScreenTests
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

        [UnityTest]
        public IEnumerator InitialRender_ShowsTheWholeSevenBySevenFieldWithOnlyTheCentreUnlocked()
        {
            DeployScreen screen = CreateScreen(out CardEconomy economy);
            yield return null;

            Assert.That(economy.Grid.Width, Is.EqualTo(7));
            Assert.That(economy.Grid.Height, Is.EqualTo(7));
            Assert.That(economy.Grid.UnlockedCellCount, Is.EqualTo(9));
            Assert.That(screen.RenderedCellCount, Is.EqualTo(49));
            Assert.That(screen.LockedCellCount, Is.EqualTo(40));
            Assert.That(screen.RenderedHandCount, Is.InRange(3, 4));
        }

        [Test]
        public void PreviewOnALockedCell_UsesGridFailureAndDisplaysFeedback()
        {
            DeployScreen screen = CreateScreen(out _);
            var card = new CardOfferItem(CardCategory.Unit, "tie");

            DeploymentEvaluation evaluation = screen.PreviewCard(card, new GridCoordinate(0, 0));

            Assert.That(evaluation.IsValid, Is.False);
            Assert.That(evaluation.Failure, Is.EqualTo(DeploymentFailureReason.CellLocked));
            Assert.That(screen.FeedbackMessage, Is.Not.Empty);
        }

        [Test]
        public void Preview_AllSevenFootprintKinds_AreAcceptedInsideTheStartingCentre()
        {
            DeployScreen screen = CreateScreen(out CardEconomy economy);

            string[] unitIds = { "zu", "dun", "mao", "tie", "zqi", "nuc", "chc" };
            foreach (string unitId in unitIds)
            {
                DeploymentEvaluation evaluation = screen.PreviewCard(
                    new CardOfferItem(CardCategory.Unit, unitId),
                    new GridCoordinate(2, 2));
                Assert.That(evaluation.IsValid, Is.True, unitId);
            }

            Assert.That(screen.LockedCellCount, Is.EqualTo(40));
        }

        [Test]
        public void PreviewAndCommitUnlockCard_OpenCellsThroughTheGridRules()
        {
            DeployScreen screen = CreateScreen(out CardEconomy economy);
            Assert.That(economy.TryGrantUnlockCard(out CardOfferItem granted), Is.True);
            screen.RefreshAll();

            Assert.That(granted.Unlock, Is.Not.Null, "a granted unlock card carries its minted shape");
            Assert.That(screen.PreviewUnlockCard(granted, new GridCoordinate(0, 0)).IsValid, Is.False,
                "an island anchor stays illegal in the view too");

            GridCoordinate anchor = economy.Grid.GetLegalUnlockAnchors(granted.Unlock)[0];
            Assert.That(screen.PreviewUnlockCard(granted, anchor).IsValid, Is.True);
            Assert.That(screen.CommitUnlockCard(granted, anchor), Is.True);

            Assert.That(economy.Grid.UnlockedCellCount, Is.GreaterThan(9));
            Assert.That(screen.LockedCellCount, Is.EqualTo(49 - economy.Grid.UnlockedCellCount));
            Assert.That(screen.FeedbackMessage, Does.Contain("解锁"));
        }

        [Test]
        public void PurchaseUnlockCard_ChargesThroughTheEconomyAndShowsTheResult()
        {
            DeployScreen screen = CreateScreen(out CardEconomy economy);
            economy.State.Coins = 100;

            Assert.That(screen.PurchaseUnlockCard(), Is.True);

            Assert.That(economy.State.Coins, Is.EqualTo(60));
            Assert.That(economy.State.UnlockPurchaseCount, Is.EqualTo(1));
            Assert.That(economy.CurrentOffer.Cards.Any(value => value.Category == CardCategory.Unlock), Is.True);
            Assert.That(screen.RenderedHandCount, Is.EqualTo(economy.CurrentOffer.Cards.Count));
        }

        [Test]
        public void CommitCard_CurrentUnitOfferPlacesThroughCardEconomy()
        {
            DeploymentActionKind? observedAction = null;
            DeployScreen screen = CreateScreenWithUnitOffer(
                out CardEconomy economy,
                out CardOfferItem unitCard,
                action => observedAction = action);

            bool committed = screen.CommitCard(unitCard, new GridCoordinate(2, 2));

            Assert.That(committed, Is.True);
            Assert.That(observedAction, Is.EqualTo(DeploymentActionKind.Place));
            Assert.That(economy.Grid.Placements, Has.Count.EqualTo(1));
            Assert.That(economy.State.DeployedGrid, Has.Length.EqualTo(1));
            Assert.That(screen.RenderedHandCount, Is.EqualTo(economy.CurrentOffer.Cards.Count));
        }

        [Test]
        public void CommitPlacement_SameUnitAndLevel_MergesAndPersistsRunState()
        {
            DeploymentActionKind? observedAction = null;
            DeployScreen screen = CreateScreen(
                out CardEconomy economy,
                deploymentCommitted: action => observedAction = action);
            UnitDef unit = economy.Grid.IsFootprintUnlocked(UnitFootprint.Rectangle(1, 1))
                ? GameConfig.Load().GetUnit("zu")
                : throw new AssertionException("Configured 1x1 footprint unexpectedly locked.");
            UnitFootprint footprint = UnitFootprint.FromDefinition(unit);
            economy.Grid.Apply(new DeploymentUnit("first", unit.Id, 1, footprint), new GridCoordinate(2, 2));
            economy.Grid.Apply(new DeploymentUnit("second", unit.Id, 1, footprint), new GridCoordinate(3, 2));
            economy.SnapshotToRunState();
            screen.RefreshAll();

            bool committed = screen.CommitPlacement("second", new GridCoordinate(2, 2));

            Assert.That(committed, Is.True);
            Assert.That(observedAction, Is.EqualTo(DeploymentActionKind.Merge));
            Assert.That(economy.Grid.Placements, Has.Count.EqualTo(1));
            Assert.That(economy.Grid.Placements.Single().Level, Is.EqualTo(2));
            Assert.That(economy.State.DeployedGrid.Single().Level, Is.EqualTo(2));
            Assert.That(screen.FeedbackMessage, Does.Contain("合成"));
        }

        [Test]
        public void RequestBattle_OnlyRaisesInjectedCommand()
        {
            int requests = 0;
            DeployScreen screen = CreateScreen(out _, () => requests++);

            screen.RequestBattle();

            Assert.That(requests, Is.EqualTo(1));
        }

        [Test]
        public void RefreshOffer_WhenEconomyRejects_DoesNotChangeCoinsOrRefreshCount()
        {
            DeployScreen screen = CreateScreen(out CardEconomy economy);
            economy.State.Coins = 0;
            int coinsBefore = economy.State.Coins;
            int refreshCountBefore = economy.State.RefreshCount;

            bool refreshed = screen.RefreshOffer();

            Assert.That(refreshed, Is.False);
            Assert.That(economy.State.Coins, Is.EqualTo(coinsBefore));
            Assert.That(economy.State.RefreshCount, Is.EqualTo(refreshCountBefore));
            Assert.That(screen.FeedbackMessage, Does.Contain("经济系统"));
        }

        private DeployScreen CreateScreen(
            out CardEconomy economy,
            Action battleRequested = null,
            Action<DeploymentActionKind> deploymentCommitted = null)
        {
            GameConfig config = GameConfig.Load();
            economy = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), 0xC3D001u);
            return CreateScreen(config, economy, battleRequested, deploymentCommitted: deploymentCommitted);
        }

        private DeployScreen CreateScreenWithUnitOffer(
            out CardEconomy economy,
            out CardOfferItem unitCard,
            Action<DeploymentActionKind> deploymentCommitted = null)
        {
            GameConfig config = GameConfig.Load();
            for (uint seed = 1; seed < 2048; seed++)
            {
                CardEconomy candidate = CardEconomy.StartNew(config, config.GetLevel("level_1_1"), seed);
                candidate.DrawOffer();
                CardOfferItem candidateCard = candidate.CurrentOffer.Cards.FirstOrDefault(
                    value => value.Category == CardCategory.Unit);
                if (candidateCard != null)
                {
                    economy = candidate;
                    unitCard = candidateCard;
                    return CreateScreen(
                        config,
                        economy,
                        null,
                        drawInitialOffer: false,
                        deploymentCommitted: deploymentCommitted);
                }
            }

            throw new AssertionException("No unit card was found across the deterministic seed search.");
        }

        private DeployScreen CreateScreen(
            GameConfig config,
            CardEconomy economy,
            Action battleRequested,
            bool drawInitialOffer = true,
            Action<DeploymentActionKind> deploymentCommitted = null)
        {
            var canvasObject = new GameObject(
                "Deploy Test Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            cleanup.Add(canvasObject);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var screenObject = new GameObject("DeployRoot", typeof(RectTransform));
            screenObject.transform.SetParent(canvasObject.transform, false);
            cleanup.Add(screenObject);
            DeployScreen screen = screenObject.AddComponent<DeployScreen>();
            if (!drawInitialOffer && economy.CurrentOffer == null)
            {
                throw new AssertionException("Expected a pre-drawn offer.");
            }
            screen.Initialize(
                config,
                economy,
                new NullArtSource(),
                battleRequested,
                onDeploymentCommitted: deploymentCommitted);
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
