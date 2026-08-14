using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using HanziDefend.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HanziDefend.Tests.PlayMode
{
    [TestFixture]
    public sealed class BattleViewTests
    {
        private static readonly BattleStats Stats = new BattleStats(
            100f, 12f, 2f, 0f, 1f, 1f, 2f, 1f, 1f);

        private GameConfig config;
        private GameObject root;
        private BattleView view;
        private RecordingCommands commands;
        private Texture2D transientTexture;
        private Sprite transientSprite;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            config = GameConfig.Load();
        }

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("BattleView Test Root");
            view = root.AddComponent<BattleView>();
            commands = new RecordingCommands();
            view.Initialize(config, new NullArtSource(), commands);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }

            if (transientSprite != null)
            {
                Object.DestroyImmediate(transientSprite);
            }

            if (transientTexture != null)
            {
                Object.DestroyImmediate(transientTexture);
            }
        }

        [Test]
        public void ImplementsAllThreeBattleEventContracts()
        {
            Assert.That(view, Is.InstanceOf<IBattleEvents>());
            Assert.That(view, Is.InstanceOf<IBattleEncounterEvents>());
            Assert.That(view, Is.InstanceOf<IBattleEffectEvents>());
        }

        [Test]
        public void PrepareForEncounter_RecyclesStaleEntityIdsAndHudDeploymentState()
        {
            Spawn(1, "gong", BattleTeam.Ally, Vector2.zero);
            int createdBeforeReset = view.CreatedVisualCount;
            Assert.That(view.HudState.DeployedUnitIds, Has.Count.EqualTo(1));

            view.PrepareForEncounter();
            Spawn(1, "dao", BattleTeam.Ally, Vector2.one);

            Assert.That(view.ActiveVisualCount, Is.EqualTo(1));
            Assert.That(view.CreatedVisualCount, Is.EqualTo(createdBeforeReset));
            Assert.That(view.HudState.DeployedUnitIds, Is.EqualTo(new[] { "dao" }));
            Assert.That(view.TryGetVisualDebug(1, out BattleVisualDebug current), Is.True);
            Assert.That(current.DefinitionId, Is.EqualTo("dao"));
        }

        [TestCase("gong", 1, 1)]
        [TestCase("dao", 2, 1)]
        [TestCase("mao", 1, 2)]
        [TestCase("tie", 2, 2)]
        [TestCase("zqi", 3, 1)]
        [TestCase("nuc", 2, 2)]
        [TestCase("chc", 2, 2)]
        public void UnitDisplaySize_UsesGridFootprint(
            string definitionId,
            int expectedGridWidth,
            int expectedGridHeight)
        {
            Vector2 actual = view.GetDefinitionDisplaySize(definitionId);

            Assert.That(actual.x,
                Is.EqualTo(expectedGridWidth * BattleView.GridCellWorldSize).Within(0.0001f));
            Assert.That(actual.y,
                Is.EqualTo(expectedGridHeight * BattleView.GridCellWorldSize).Within(0.0001f));
        }

        [Test]
        public void SnapshotSync_InterpolatesPosition_AndSortsLowerYInFront()
        {
            Spawn(1, "gong", BattleTeam.Ally, new Vector2(0f, -2f));
            Spawn(2, "e_lang", BattleTeam.Enemy, new Vector2(0f, 2f));
            view.AdvancePresentation(0f);

            Assert.That(view.TryGetVisualDebug(1, out BattleVisualDebug lower), Is.True);
            Assert.That(view.TryGetVisualDebug(2, out BattleVisualDebug upper), Is.True);
            Assert.That(lower.SortingOrder, Is.GreaterThan(upper.SortingOrder));

            view.SyncSnapshots(new[]
            {
                Snapshot(1, "gong", BattleTeam.Ally, new Vector2(0f, 2f))
            });
            view.AdvancePresentation(0.025f);

            Assert.That(view.TryGetVisualDebug(1, out BattleVisualDebug moved), Is.True);
            Assert.That(moved.Position.y, Is.GreaterThan(-2f));
            Assert.That(moved.Position.y, Is.LessThan(2f));
            Assert.That(moved.TargetPosition, Is.EqualTo(new Vector2(0f, 2f)));
        }

        [Test]
        public void DamageEvent_RevealsHealthBar_StartsFlash_AndSpawnsSpriteCombatNumber()
        {
            Spawn(10, "gong", BattleTeam.Ally, Vector2.zero);

            view.DamageDealt(new DamageDealtEvent(
                1, 1, 0.1d, 1, 20, 10, 25, 100f, 75f, 100f, Vector2.zero, false));
            view.AdvancePresentation(0.02f);

            Assert.That(view.TryGetVisualDebug(10, out BattleVisualDebug debug), Is.True);
            Assert.That(debug.HealthRatio, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(debug.HealthBarAlpha, Is.GreaterThan(0f));
            Assert.That(debug.IsFlashing, Is.True);
            Assert.That(view.ActiveCombatNumberCount, Is.EqualTo(1));
            Assert.That(view.CreatedCombatNumberCount, Is.EqualTo(1));
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (var index = 0; index < components.Length; index++)
            {
                string typeName = components[index].GetType().FullName;
                Assert.That(typeName, Is.Not.EqualTo("UnityEngine.TextMesh"));
                Assert.That(typeName, Is.Not.EqualTo("UnityEngine.UI.Text"));
                Assert.That(typeName, Does.Not.StartWith("TMPro."));
            }
        }

        [Test]
        public void HitFreezeAndDeathFx_ReuseThePrewarmedSpritePool()
        {
            transientTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            transientTexture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            transientTexture.Apply(false, true);
            transientSprite = Sprite.Create(
                transientTexture,
                new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f),
                2f);
            RecreateView(new EverySpriteArtSource(transientSprite));

            Spawn(1, "gong", BattleTeam.Ally, Vector2.zero);
            Spawn(2, "e_lang", BattleTeam.Enemy, Vector2.one);
            int createdBeforeEvents = view.CreatedFxCount;

            view.DamageDealt(new DamageDealtEvent(
                1, 1, 0.1d, 1, 1, 2, 25, 100f, 75f, 100f, Vector2.one, false));
            view.EffectApplied(new EffectAppliedEvent(
                2, 2, 0.15d, null, BattleSystem.IceAuraPresentationEffectId, 0, 1, 2,
                EffectOpCode.AddStat, "moveSpeed", StatModifierMode.Mul, 0.7f, 1f));
            view.UnitDied(new UnitDiedEvent(
                3, 3, 0.2d, 2, "e_lang", BattleTeam.Enemy, 1, Vector2.one, 0f, 100f, null));

            Assert.That(view.ActiveFxCount, Is.EqualTo(3),
                "A hit from an IceAura source plus the target death must project hit, freeze, and death FX.");
            Assert.That(view.CreatedFxCount, Is.EqualTo(createdBeforeEvents),
                "Presentation events must rent from the prewarmed pool.");

            view.AdvancePresentation(1f);

            Assert.That(view.ActiveFxCount, Is.Zero);
            Assert.That(view.CreatedFxCount, Is.EqualTo(createdBeforeEvents));
        }

        [Test]
        public void CombatNumbers_AggregateSameTargetInsideShortWindow_WithDeterministicOffset()
        {
            Spawn(10, "gong", BattleTeam.Ally, Vector2.zero);
            DealDamage(1, 10, 7, new Vector2(0.25f, 0.5f));
            DealDamage(2, 10, 11, new Vector2(0.25f, 0.5f));

            Assert.That(view.ActiveCombatNumberCount, Is.EqualTo(1));
            Assert.That(view.CreatedCombatNumberCount, Is.EqualTo(1));
            Assert.That(view.TryGetCombatNumberDebug(10, false, out CombatNumberDebug combined), Is.True);
            Assert.That(combined.AccumulatedAmount, Is.EqualTo(18));

            Vector2 firstPosition = combined.Position;
            view.AdvancePresentation(0.15f);
            DealDamage(3, 10, 5, new Vector2(0.25f, 0.5f));

            Assert.That(view.ActiveCombatNumberCount, Is.EqualTo(2));
            Assert.That(view.TryGetCombatNumberDebug(10, false, out CombatNumberDebug next), Is.True);
            Assert.That(next.AccumulatedAmount, Is.EqualTo(5));
            Assert.That(Mathf.Abs(next.Position.x - firstPosition.x), Is.GreaterThan(0.0001f),
                "Distinct deterministic event sequences should spread simultaneous combat numbers.");

            Vector2 deterministicPosition = next.Position;
            view.AdvancePresentation(1f);
            DealDamage(3, 10, 5, new Vector2(0.25f, 0.5f));
            Assert.That(view.TryGetCombatNumberDebug(10, false, out CombatNumberDebug repeated), Is.True);
            Assert.That(repeated.Position, Is.EqualTo(deterministicPosition),
                "The same target and event sequence must reproduce the exact visual offset without RNG.");
        }

        [Test]
        public void CombatNumbers_EnforceHardCap_AndReuseExpiredPoolEntry()
        {
            const int denseDamageEventCount = 64;
            for (var index = 0; index < denseDamageEventCount; index++)
            {
                DealDamage(index + 1, 1000 + index, 1, Vector2.zero);
            }

            Assert.That(view.ActiveCombatNumberCount, Is.LessThanOrEqualTo(BattleView.MaxActiveCombatNumbers));
            Assert.That(view.ActiveCombatNumberCount, Is.EqualTo(BattleView.MaxActiveCombatNumbers));
            Assert.That(view.CreatedCombatNumberCount, Is.EqualTo(BattleView.MaxActiveCombatNumbers));
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (var index = 0; index < components.Length; index++)
            {
                string typeName = components[index].GetType().FullName;
                Assert.That(typeName, Is.Not.EqualTo("UnityEngine.TextMesh"));
                Assert.That(typeName, Is.Not.EqualTo("UnityEngine.UI.Text"));
                Assert.That(typeName, Does.Not.StartWith("TMPro."));
            }

            view.AdvancePresentation(1f);
            Assert.That(view.ActiveCombatNumberCount, Is.Zero);
            int createdBeforeReuse = view.CreatedCombatNumberCount;

            DealDamage(100, 5000, 9, Vector2.zero);

            Assert.That(view.ActiveCombatNumberCount, Is.EqualTo(1));
            Assert.That(view.CreatedCombatNumberCount, Is.EqualTo(createdBeforeReuse));
        }

        [Test]
        public void BattlefieldContent_ClampsEntityHealthFootprintAndCombatNumberInsideLane()
        {
            Spawn(10, "tie", BattleTeam.Ally, new Vector2(100f, 100f));
            view.AdvancePresentation(0f);
            Assert.That(view.TryGetVisualDebug(10, out BattleVisualDebug entity), Is.True);

            Rect bounds = BattleView.BattlefieldWorldBounds;
            Assert.That(entity.Position.x - entity.DisplaySize.x * 0.5f, Is.GreaterThanOrEqualTo(bounds.xMin));
            Assert.That(entity.Position.x + entity.DisplaySize.x * 0.5f, Is.LessThanOrEqualTo(bounds.xMax));
            Assert.That(entity.Position.y - entity.DisplaySize.y * 0.5f, Is.GreaterThanOrEqualTo(bounds.yMin));
            Assert.That(entity.Position.y + entity.DisplaySize.y * 0.5f, Is.LessThanOrEqualTo(bounds.yMax));

            DealDamage(1, 10, 123, new Vector2(-100f, -100f));
            Assert.That(view.TryGetCombatNumberDebug(10, false, out CombatNumberDebug number), Is.True);
            Assert.That(number.Position.x - number.HalfExtents.x, Is.GreaterThanOrEqualTo(bounds.xMin));
            Assert.That(number.Position.x + number.HalfExtents.x, Is.LessThanOrEqualTo(bounds.xMax));
            Assert.That(number.Position.y - number.HalfExtents.y, Is.GreaterThanOrEqualTo(bounds.yMin));
            Assert.That(number.Position.y + number.HalfExtents.y, Is.LessThanOrEqualTo(bounds.yMax));
        }

        [Test]
        public void BottomPivotMultiGridArtworkAndHealthRenderers_StayInsideTopAndBottomLaneBounds()
        {
            transientTexture = new Texture2D(8, 16, TextureFormat.RGBA32, false);
            var pixels = new Color32[8 * 16];
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = new Color32(255, 255, 255, 255);
            }

            transientTexture.SetPixels32(pixels);
            transientTexture.Apply(false, true);
            transientSprite = Sprite.Create(
                transientTexture,
                new Rect(0f, 0f, 8f, 16f),
                new Vector2(0.5f, 0f),
                16f);
            RecreateView(new SingleSpriteArtSource(transientSprite));

            Spawn(31, "tie", BattleTeam.Ally, new Vector2(0f, 100f));
            Spawn(32, "tie", BattleTeam.Ally, new Vector2(0f, -100f));
            DealDamage(1, 31, 10, new Vector2(0f, 100f));
            DealDamage(2, 32, 10, new Vector2(0f, -100f));
            view.AdvancePresentation(0.02f);

            AssertEntityRenderersInsideLane(31);
            AssertEntityRenderersInsideLane(32);
        }

        [Test]
        public void YSorting_IsAStableEntityIdTotalOrder_AndAlwaysBelowOverlayCanvasRange()
        {
            const int entityCount = 200;
            var observedOrders = new HashSet<int>();
            for (var entityId = 1; entityId <= entityCount; entityId++)
            {
                Spawn(entityId, "gong", BattleTeam.Ally, Vector2.zero);
            }

            view.AdvancePresentation(0f);
            int previousOrder = int.MaxValue;
            for (var entityId = 1; entityId <= entityCount; entityId++)
            {
                Assert.That(view.TryGetVisualDebug(entityId, out BattleVisualDebug visual), Is.True);
                Assert.That(visual.SortingOrder, Is.LessThan(previousOrder));
                Assert.That(visual.SortingOrder, Is.LessThan(short.MaxValue));
                Assert.That(observedOrders.Add(visual.SortingOrder), Is.True);
                previousOrder = visual.SortingOrder;
            }

            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                Assert.That(renderers[index].sortingOrder, Is.LessThan(short.MaxValue));
            }
        }

        [Test]
        public void HealthBar_FadesAfterTwoSeconds_WhileCastleHealthStaysVisible()
        {
            Spawn(10, "gong", BattleTeam.Ally, Vector2.zero);
            view.DamageDealt(new DamageDealtEvent(
                1, 1, 0.1d, 1, 20, 10, 1, 100f, 99f, 100f, Vector2.zero, false));
            view.AdvancePresentation(BattleView.HealthBarVisibleSeconds + 0.5f);

            Assert.That(view.TryGetVisualDebug(10, out BattleVisualDebug ordinary), Is.True);
            Assert.That(ordinary.HealthBarAlpha, Is.EqualTo(0f).Within(0.0001f));

            Spawn(99, "bld_cheng", BattleTeam.Enemy, new Vector2(0f, 7f));
            view.BossSpawned(new BossSpawnedEvent(
                2,
                2,
                1d,
                99,
                "bld_cheng",
                1,
                Stats,
                new Vector2(0f, 7f),
                new WaveSpawnContext("main_20", 20, 0, 0, 1d, EnemyRank.Boss)));
            view.AdvancePresentation(BattleView.HealthBarVisibleSeconds + 0.5f);

            Assert.That(view.TryGetVisualDebug(99, out BattleVisualDebug castle), Is.True);
            Assert.That(castle.AlwaysShowHealth, Is.True);
            Assert.That(castle.HealthBarAlpha, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void DeathFade_ReturnsVisualToPool_AndNextSpawnReusesIt()
        {
            Spawn(1, "gong", BattleTeam.Ally, Vector2.zero);
            int createdBeforeDeath = view.CreatedVisualCount;

            view.UnitDied(new UnitDiedEvent(
                1, 1, 0.1d, 1, "gong", BattleTeam.Ally, 2, Vector2.zero, 0f, 100f, null));
            view.AdvancePresentation(1f);

            Assert.That(view.ActiveVisualCount, Is.Zero);
            Assert.That(view.PooledVisualCount, Is.EqualTo(1));

            Spawn(2, "dao", BattleTeam.Ally, Vector2.one);

            Assert.That(view.ActiveVisualCount, Is.EqualTo(1));
            Assert.That(view.PooledVisualCount, Is.Zero);
            Assert.That(view.CreatedVisualCount, Is.EqualTo(createdBeforeDeath));
        }

        [Test]
        public void Hud_ProjectsBattleEventsAndCommandState()
        {
            view.WaveStarted(new WaveStartedEvent(
                1, 1, 0d, 0d, "main_20", 7, EnemyRank.Normal));
            view.CoinDropped(new CoinDroppedEvent(
                2, 2, 0.1d, 3, "e_lang", EnemyRank.Normal, 8, 123, Vector2.zero));
            view.BaseDamaged(new BaseDamagedEvent(
                3, 3, 0.2d, 1, 3, 50, BattleTeam.Ally, 10, 80f, 50f, 100f, Vector2.zero));
            commands.AutoRun = false;
            commands.CooldownRemaining = 2.75f;
            view.AdvancePresentation(0f);

            Assert.That(view.HudState.WaveIndex, Is.EqualTo(7));
            Assert.That(view.HudState.WaveTotal, Is.EqualTo(20));
            Assert.That(view.HudState.Coins, Is.EqualTo(123));
            Assert.That(view.HudState.AllyBaseHealthRatio, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(view.HudState.AutoRun, Is.False);
            Assert.That(view.HudState.CommanderCooldown, Is.EqualTo(2.75f).Within(0.0001f));
        }

        [Test]
        public void EffectEvents_ProjectHealingShieldAndCoinFeedback()
        {
            Spawn(10, "gong", BattleTeam.Ally, Vector2.zero);

            view.UnitHealed(new UnitHealedEvent(
                1, 1, 0.1d, "cmd_bei_active", 0, null, 10,
                30f, 20f, 60f, 80f, 100f));
            view.ShieldChanged(new ShieldChangedEvent(
                2, 2, 0.2d, 1, "shield", 0, null, 10, 25f, 0f, 25f));
            view.CoinsModified(new CoinsModifiedEvent(
                3, 3, 0.3d, "coins", 0, null, BattleTeam.Ally, 10, 78, 88));
            view.AdvancePresentation(0.02f);

            Assert.That(view.TryGetVisualDebug(10, out BattleVisualDebug visual), Is.True);
            Assert.That(visual.HealthRatio, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(visual.IsFlashing, Is.True);
            Assert.That(view.ActiveCombatNumberCount, Is.EqualTo(1));
            Assert.That(view.HudState.Coins, Is.EqualTo(88));
        }

        [Test]
        public void HudButtons_ForwardOnlyCommandsToInjectedSink()
        {
            Assert.That(view.RequestCommanderActivation(), Is.True);
            view.ToggleAutoRun();
            view.RequestSingleStep();

            Assert.That(commands.ActivationRequests, Is.EqualTo(1));
            Assert.That(commands.AutoRun, Is.False);
            Assert.That(commands.StepRequests, Is.EqualTo(1));
        }

        [Test]
        public void ReviewMilestones_AreDrivenOnlyByEncounterEvents()
        {
            Assert.That(view.CurrentReviewStage, Is.EqualTo(BattleReviewStage.Opening));
            Assert.That(view.TryDequeueReviewStage(out BattleReviewStage opening), Is.True);
            Assert.That(opening, Is.EqualTo(BattleReviewStage.Opening));

            view.WaveStarted(new WaveStartedEvent(
                1, 1, 0d, 0d, "main_20", 10, EnemyRank.Normal));
            Assert.That(view.CurrentReviewStage, Is.EqualTo(BattleReviewStage.Mid));

            Spawn(99, "bld_cheng", BattleTeam.Enemy, new Vector2(0f, 7f));
            view.BossSpawned(new BossSpawnedEvent(
                2,
                2,
                1d,
                99,
                "bld_cheng",
                1,
                Stats,
                new Vector2(0f, 7f),
                new WaveSpawnContext("main_20", 20, 0, 0, 1d, EnemyRank.Boss)));
            Assert.That(view.CurrentReviewStage, Is.EqualTo(BattleReviewStage.Castle));

            view.BattleSettled(new BattleSettledEvent(
                3, 3, 2d, BattleResult.Win, 50, 99, 4000f, 0f, false, true));
            Assert.That(view.CurrentReviewStage, Is.EqualTo(BattleReviewStage.Castle));
            Assert.That(view.HudState.Status, Is.EqualTo("敌方城堡登场"));
            Assert.That(view.TryDequeueReviewStage(out BattleReviewStage middle), Is.True);
            Assert.That(middle, Is.EqualTo(BattleReviewStage.Mid));
            Assert.That(view.TryDequeueReviewStage(out BattleReviewStage castleStage), Is.True);
            Assert.That(castleStage, Is.EqualTo(BattleReviewStage.Castle));
            Assert.That(view.TryDequeueReviewStage(out _), Is.False,
                "Settlement capture must not become consumable in the castle event frame.");

            view.AdvancePresentation(0.1f);
            Assert.That(view.HudState.Status, Is.EqualTo("敌方城堡登场"));
            Assert.That(view.TryDequeueReviewStage(out _), Is.False);

            view.AcknowledgeReviewStage(BattleReviewStage.Castle);

            Assert.That(view.CurrentReviewStage, Is.EqualTo(BattleReviewStage.Settlement));
            Assert.That(view.HudState.Status, Is.EqualTo("战斗结果：Win"));
            Assert.That(view.TryDequeueReviewStage(out BattleReviewStage settlement), Is.True);
            Assert.That(settlement, Is.EqualTo(BattleReviewStage.Settlement));
        }

        [Test]
        public void SettlementReviewStage_FallsBackAfterPresentationDelayWithoutCaptureCoordinator()
        {
            Assert.That(view.TryDequeueReviewStage(out _), Is.True);
            Spawn(99, "bld_cheng", BattleTeam.Enemy, new Vector2(0f, 7f));
            view.BossSpawned(new BossSpawnedEvent(
                1,
                1,
                1d,
                99,
                "bld_cheng",
                1,
                Stats,
                new Vector2(0f, 7f),
                new WaveSpawnContext("main_20", 20, 0, 0, 1d, EnemyRank.Boss)));
            view.BattleSettled(new BattleSettledEvent(
                2, 2, 1d, BattleResult.Lose, 50, 99, 0f, 1f, true, false));
            Assert.That(view.TryDequeueReviewStage(out BattleReviewStage castle), Is.True);
            Assert.That(castle, Is.EqualTo(BattleReviewStage.Castle));

            view.AdvancePresentation(BattleView.ReviewCaptureFallbackSeconds + 0.01f);

            Assert.That(view.HudState.Status, Is.EqualTo("战斗结果：Lose"));
            Assert.That(view.TryDequeueReviewStage(out BattleReviewStage settlement), Is.True);
            Assert.That(settlement, Is.EqualTo(BattleReviewStage.Settlement));
        }

        [Test]
        public void TwoHundredSyntheticViews_StayWithinEditorPresentationBudget()
        {
            const int entityCount = 200;
            const int sampledFrames = 180;
            for (var entityId = 1; entityId <= entityCount; entityId++)
            {
                float x = (entityId % 20) * 0.25f - 2.5f;
                float y = (entityId / 20) * 0.25f - 1.25f;
                Spawn(entityId, "gong", BattleTeam.Ally, new Vector2(x, y));
            }

            for (var index = 0; index < 30; index++)
            {
                view.AdvancePresentation(1f / 60f);
            }

            var stopwatch = Stopwatch.StartNew();
            for (var index = 0; index < sampledFrames; index++)
            {
                view.AdvancePresentation(1f / 60f);
            }

            stopwatch.Stop();
            double averageMilliseconds = stopwatch.Elapsed.TotalMilliseconds / sampledFrames;

            Assert.That(view.ActiveVisualCount, Is.EqualTo(entityCount));
            Assert.That(view.CreatedVisualCount, Is.EqualTo(entityCount));
            Assert.That(averageMilliseconds, Is.LessThanOrEqualTo(1000d / 45d),
                $"200-unit presentation averaged {averageMilliseconds:F3} ms; Editor budget is 22.222 ms.");
        }

        [UnityTest]
        public IEnumerator TwoHundredVisibleSpriteViews_RenderAtLeastFortyFiveFramesPerSecondInEditor()
        {
            const int entityCount = 200;
            const int warmupFrames = 15;
            const int sampledFrames = 60;
            var cameraObject = new GameObject("BattleView Render Budget Camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 9.6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            for (var entityId = 1; entityId <= entityCount; entityId++)
            {
                float x = (entityId % 20) * 0.25f - 2.5f;
                float y = (entityId / 20) * 0.25f - 1.25f;
                Spawn(entityId, "gong", BattleTeam.Ally, new Vector2(x, y));
            }

            for (var index = 0; index < warmupFrames; index++)
            {
                yield return null;
            }

            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>();
            int visibleRenderers = 0;
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index].enabled && renderers[index].gameObject.activeInHierarchy)
                {
                    visibleRenderers++;
                }
            }

            Assert.That(visibleRenderers, Is.GreaterThanOrEqualTo(entityCount),
                "The frame budget must include at least 200 active SpriteRenderers.");

            double startedAt = Time.realtimeSinceStartupAsDouble;
            for (var index = 0; index < sampledFrames; index++)
            {
                yield return null;
            }

            double elapsed = Time.realtimeSinceStartupAsDouble - startedAt;
            double framesPerSecond = sampledFrames / elapsed;
            UnityEngine.Debug.Log(
                $"[WO-B4 Render FPS] {entityCount} visible sprite views: " +
                $"{framesPerSecond:F2} FPS over {sampledFrames} frames ({elapsed:F3}s).");
            Assert.That(framesPerSecond, Is.GreaterThanOrEqualTo(45d),
                $"200 visible SpriteRenderers averaged {framesPerSecond:F1} FPS over {sampledFrames} real frames.");
        }

        private void Spawn(int entityId, string definitionId, BattleTeam team, Vector2 position)
        {
            view.UnitSpawned(new UnitSpawnedEvent(
                entityId,
                0,
                0d,
                entityId,
                definitionId,
                team,
                1,
                Stats,
                Stats.MaxHp,
                position,
                team == BattleTeam.Ally ? Vector2.up : Vector2.down,
                BattleUnitState.Idle));
        }

        private void RecreateView(IBattleArtSource artSource)
        {
            Object.DestroyImmediate(root);
            root = new GameObject("BattleView Test Root");
            view = root.AddComponent<BattleView>();
            commands = new RecordingCommands();
            view.Initialize(config, artSource, commands);
        }

        private void AssertEntityRenderersInsideLane(int entityId)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            Transform entityRoot = null;
            string expectedName = $"Battle Entity {entityId} [tie]";
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == expectedName)
                {
                    entityRoot = transforms[index];
                    break;
                }
            }

            Assert.That(entityRoot, Is.Not.Null, $"Missing visual root for entity {entityId}.");
            SpriteRenderer artwork = entityRoot.Find("Artwork").GetComponent<SpriteRenderer>();
            SpriteRenderer healthBackground = entityRoot.Find("Health Background").GetComponent<SpriteRenderer>();
            SpriteRenderer healthFill = entityRoot.Find("Health Fill").GetComponent<SpriteRenderer>();
            Assert.That(artwork.bounds.center.x, Is.EqualTo(entityRoot.position.x).Within(0.0001f));
            Assert.That(artwork.bounds.center.y, Is.EqualTo(entityRoot.position.y).Within(0.0001f),
                "Bottom-pivot artwork must be centered on the entity root after scaling.");
            AssertRendererInsideLane(artwork, $"entity {entityId} artwork");
            AssertRendererInsideLane(healthBackground, $"entity {entityId} health background");
            AssertRendererInsideLane(healthFill, $"entity {entityId} health fill");
        }

        private static void AssertRendererInsideLane(SpriteRenderer renderer, string label)
        {
            Rect lane = BattleView.BattlefieldWorldBounds;
            Bounds bounds = renderer.bounds;
            const float epsilon = 0.0001f;
            Assert.That(bounds.min.x, Is.GreaterThanOrEqualTo(lane.xMin - epsilon), label);
            Assert.That(bounds.max.x, Is.LessThanOrEqualTo(lane.xMax + epsilon), label);
            Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(lane.yMin - epsilon), label);
            Assert.That(bounds.max.y, Is.LessThanOrEqualTo(lane.yMax + epsilon), label);
        }

        private void DealDamage(long sequence, int targetEntityId, int amount, Vector2 position)
        {
            view.DamageDealt(new DamageDealtEvent(
                sequence,
                sequence,
                sequence * 0.01d,
                sequence,
                1,
                targetEntityId,
                amount,
                100f,
                Mathf.Max(0f, 100f - amount),
                100f,
                position,
                false));
        }

        private static BattleUnitSnapshot Snapshot(
            int entityId,
            string definitionId,
            BattleTeam team,
            Vector2 position)
        {
            return new BattleUnitSnapshot(
                entityId,
                definitionId,
                team,
                1,
                Stats,
                Stats.MaxHp,
                position,
                team == BattleTeam.Ally ? Vector2.up : Vector2.down,
                BattleUnitState.Move,
                null);
        }

        private sealed class NullArtSource : IBattleArtSource
        {
            public Sprite Find(string key)
            {
                return null;
            }
        }

        private sealed class SingleSpriteArtSource : IBattleArtSource
        {
            private readonly Sprite sprite;

            public SingleSpriteArtSource(Sprite sprite)
            {
                this.sprite = sprite;
            }

            public Sprite Find(string key)
            {
                return key != null && key.StartsWith("unit/") ? sprite : null;
            }
        }

        private sealed class EverySpriteArtSource : IBattleArtSource
        {
            private readonly Sprite sprite;

            public EverySpriteArtSource(Sprite sprite)
            {
                this.sprite = sprite;
            }

            public Sprite Find(string key)
            {
                return string.IsNullOrEmpty(key) ? null : sprite;
            }
        }

        private sealed class RecordingCommands : IBattleViewCommands
        {
            public bool AutoRun { get; set; } = true;

            public float CooldownRemaining { get; set; }

            public float CommanderCooldownRemaining => CooldownRemaining;

            public int ActivationRequests { get; private set; }

            public int StepRequests { get; private set; }

            public bool TryActivateCommander()
            {
                ActivationRequests++;
                return true;
            }

            public void StepOnce()
            {
                StepRequests++;
            }
        }
    }
}
