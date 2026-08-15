using System;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Deploy;
using HanziDefend.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace HanziDefend.Tests.PlayMode
{
    public sealed class GameFlowTests
    {
        private readonly List<GameFlow> flows = new List<GameFlow>();
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = flows.Count - 1; index >= 0; index--)
            {
                flows[index]?.Dispose();
            }
            flows.Clear();
            for (int index = objects.Count - 1; index >= 0; index--)
            {
                if (objects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objects[index]);
                }
            }
            objects.Clear();
        }

        [Test]
        public void InitialState_IsDeployWithFreshRunState()
        {
            GameFlow flow = CreateFlow(FastWinConfig());

            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Deploy));
            Assert.That(flow.StageNumber, Is.EqualTo(1));
            Assert.That(flow.RunState.Coins, Is.EqualTo(45));
            Assert.That(flow.RunState.DeployedGrid, Is.Empty);
            Assert.That(flow.RunState.OwnedEffects, Is.Empty);
            Assert.That(flow.RunState.GridWidth, Is.EqualTo(7));
            Assert.That(flow.RunState.GridHeight, Is.EqualTo(7));
            Assert.That(flow.RunState.UnlockedCells.Count(value => value), Is.EqualTo(9));
        }

        [Test]
        public void StartBattle_MapsPersistedDeploymentAndChangesOnlyToBattle()
        {
            GameFlow flow = CreateFlow(FastWinConfig());
            AddDeployment(flow, "gong", "flow-gong", 2, 3, 2);

            flow.StartBattle();

            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Battle));
            Assert.That(flow.Battle, Is.Not.Null);
            Assert.That(flow.Battle.GetAliveCount(BattleTeam.Ally), Is.EqualTo(1));
            BattleUnitSnapshot snapshot = flow.Battle.CaptureSnapshot().Single(value => value.Team == BattleTeam.Ally);
            Assert.That(snapshot.DefinitionId, Is.EqualTo("gong"));
            Assert.That(snapshot.Level, Is.EqualTo(2));
        }

        [Test]
        public void DeployPhase_DoesNotAdvanceBattleTick()
        {
            GameFlow flow = CreateFlow(FastWinConfig());

            flow.Tick(10f);

            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Deploy));
            Assert.That(flow.Battle, Is.Null);
        }

        [Test]
        public void Win_ChangesToRewardAndFreezesBattleTickImmediately()
        {
            GameFlow flow = CreateFlow(FastWinConfig());
            AddDeployment(flow, "gong", "winner", 1, 3, 2);
            flow.StartBattle();

            TickUntilPhaseChanges(flow, GameFlowPhase.Battle, 20);
            long settledTick = flow.Battle.TickIndex;
            flow.Tick(flow.Battle.FixedDeltaTime);

            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Reward));
            Assert.That(flow.Reward.CurrentOffer.Cards, Has.Count.EqualTo(3));
            Assert.That(flow.Battle.IsSettled, Is.True);
            Assert.That(flow.Battle.TickIndex, Is.EqualTo(settledTick));
        }

        [Test]
        public void RewardSelection_AdvancesStageAndPreservesCoinsDeploymentEffectsGridAndRngStreams()
        {
            GameFlow flow = CreateFlow(FastWinConfig());
            AddDeployment(flow, "gong", "persistent", 2, 3, 2);
            flow.Economy.Grid.ApplyUnlock(
                UnlockCardFactory.Create(UnlockCardShape.OneByThree), new GridCoordinate(1, 2));
            flow.Economy.SnapshotToRunState();
            int unlockedBeforeBattle = flow.Economy.Grid.UnlockedCellCount;
            flow.StartBattle();
            TickUntilPhaseChanges(flow, GameFlowPhase.Battle, 20);
            int coinsAfterBattle = flow.RunState.Coins;
            uint battleState = flow.RunState.RngStreamsState.battle.state;
            string selected = flow.Reward.CurrentOffer.Cards[0].EffectId;

            flow.Reward.Select(0);
            flow.BeginNextStage();

            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Deploy));
            Assert.That(flow.StageNumber, Is.EqualTo(2));
            Assert.That(flow.RunState.Coins, Is.EqualTo(coinsAfterBattle));
            Assert.That(flow.RunState.DeployedGrid.Single().DeploymentId, Is.EqualTo("persistent"));
            Assert.That(flow.RunState.DeployedGrid.Single().Level, Is.EqualTo(2));
            Assert.That(flow.RunState.OwnedEffects, Does.Contain(selected));
            Assert.That(unlockedBeforeBattle, Is.EqualTo(12));
            // The card unlocked three cells and clearing the stage auto-unlocked one more, and the
            // whole mask is inherited by the next minor stage.
            Assert.That(flow.RunState.UnlockedCells.Count(value => value), Is.EqualTo(13));
            Assert.That(flow.Economy.Grid.UnlockedCellCount, Is.EqualTo(13));
            Assert.That(flow.Economy.Grid.IsUnlocked(new GridCoordinate(3, 1)), Is.True,
                "the automatic unlock opens the cell nearest the ally base");
            Assert.That(flow.RunState.RngStreamsState.battle.state, Is.EqualTo(battleState));
        }

        /// <summary>
        /// A full round trip: deploy, fight, take a reward, and arrive in the NEXT stage's deploy
        /// phase ready to play.
        ///
        /// <para>The other flow tests all verify that RunState is inherited correctly, and every one
        /// of them passed while stage 2 was unplayable: nobody dealt its free hand, so the hand was
        /// empty and refreshing threw. Inheritance was never the broken part — <b>arriving</b> was.
        /// This asserts the arrival: a hand exists, refresh works, and the reward is on the books.</para>
        /// </summary>
        [Test]
        public void SecondMinorStage_ArrivesPlayableWithAHandRefreshAndTheChosenReward()
        {
            GameFlow flow = CreateFlow(FastWinConfig());
            AddDeployment(flow, "gong", "round-trip", 1, 3, 2);

            Assert.That(flow.Economy.CurrentOffer, Is.Not.Null, "stage 1 is dealt its free hand");
            int stageOneCards = flow.Economy.CurrentOffer.Cards.Count;

            flow.StartBattle();
            TickUntilPhaseChanges(flow, GameFlowPhase.Battle, 20);
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Reward));

            string chosen = flow.Reward.CurrentOffer.Cards[0].EffectId;
            flow.Reward.Select(0);
            flow.BeginNextStage();

            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Deploy));
            Assert.That(flow.StageNumber, Is.EqualTo(2));

            // 1. The new stage has a hand of its own.
            Assert.That(flow.Economy.CurrentOffer, Is.Not.Null,
                "stage 2 must be dealt its own free hand on arrival");
            Assert.That(flow.Economy.CurrentOffer.Cards, Is.Not.Empty, "stage 2's hand must not be empty");
            Assert.That(stageOneCards, Is.GreaterThan(0));

            // 2. Refresh works instead of throwing the free-offer guard.
            flow.RunState.Coins = 1000;
            Assert.DoesNotThrow(() => flow.Economy.TryRefresh(out _),
                "refreshing in stage 2 must not hit the 'draw the free offer first' guard");
            Assert.That(flow.Economy.CurrentOffer.Cards, Is.Not.Empty);

            // Buying an unlock card runs through the same guard.
            flow.RunState.Coins = 1000;
            Assert.DoesNotThrow(() => flow.Economy.TryPurchaseUnlockCard(out _));

            // 3. The reward chosen at settlement is carried and nameable.
            Assert.That(flow.RunState.OwnedEffects, Does.Contain(chosen));
        }

        /// <summary>
        /// The same deploy screen instance carried from stage 1 into stage 2, exactly as the
        /// bootstrap does it.
        ///
        /// <para>Reuse is the whole point. Building a fresh screen for stage 2 hides the bug,
        /// because <c>Initialize</c> would deal the hand on the way in — which is precisely why the
        /// shipped build broke and the tests did not: the real bootstrap keeps one screen and only
        /// calls <c>RefreshAll</c> on each phase change.</para>
        /// </summary>
        [Test]
        public void SecondMinorStage_ReusedDeployScreenShowsTheHandAndTheOwnedEffects()
        {
            GameConfig config = FastWinConfig();
            GameFlow flow = CreateFlow(config);

            // Stage 1: the screen is built once and kept for the rest of the run.
            DeployScreen screen = HostDeployScreen(config, flow);
            Assert.That(screen.RenderedHandCount, Is.GreaterThan(0), "stage 1 renders a hand");

            AddDeployment(flow, "gong", "round-trip-ui", 1, 3, 2);
            flow.StartBattle();
            TickUntilPhaseChanges(flow, GameFlowPhase.Battle, 20);
            string chosen = flow.Reward.CurrentOffer.Cards[0].EffectId;
            flow.Reward.Select(0);
            flow.BeginNextStage();

            // What the bootstrap does on re-entering Deploy: no rebuild, just a refresh.
            screen.RefreshAll();

            Assert.That(flow.StageNumber, Is.EqualTo(2));
            Assert.That(screen.RenderedHandCount, Is.GreaterThan(0),
                "the reused deploy screen must render stage 2's hand");
            Assert.That(flow.RunState.OwnedEffects, Does.Contain(chosen));
            Assert.That(screen.OwnedEffectChipCount, Is.GreaterThan(0),
                "effects won at settlement must be visible on the deploy screen");
            Assert.That(
                screen.OwnedEffectChipCount,
                Is.EqualTo(flow.RunState.OwnedEffects.Distinct().Count()),
                "one chip per distinct owned effect");

            // The refresh button goes through the same guard that used to throw.
            flow.RunState.Coins = 1000;
            Assert.DoesNotThrow(() => screen.RefreshOffer());
        }

        /// <summary>
        /// Units come out as a line across the base, and the card you put on the left is the one
        /// that comes out on the left.
        ///
        /// <para>The ordering half of that is not decoration — it is the strategic dimension the
        /// deploy grid buys, and the reason the spread is a mapping rather than a random scatter.
        /// The width half is the bug: a fixed 1.2-per-column step spanned 2.4 world units on the
        /// three columns that start unlocked, against a battlefield some 10.8 wide, so everyone
        /// spawned in a heap.</para>
        /// </summary>
        [Test]
        public void StartBattle_SpreadsSpawnsAcrossTheBaseAndKeepsTheLeftCardOnTheLeft()
        {
            GameConfig config = FastWinConfig();
            GameFlow flow = CreateFlow(config);

            // The three columns that start unlocked, left to right.
            AddDeployment(flow, "zu", "spread-left", 1, 2, 2);
            AddDeployment(flow, "gong", "spread-middle", 1, 3, 2);
            AddDeployment(flow, "huo", "spread-right", 1, 4, 2);

            flow.StartBattle();
            Dictionary<string, float> x = AllySpawnX(flow);

            Assert.That(x["zu"], Is.LessThan(x["gong"]), "the card placed on the left spawns on the left");
            Assert.That(x["gong"], Is.LessThan(x["huo"]), "and the one placed on the right, on the right");
            Assert.That(
                x["huo"] - x["zu"],
                Is.EqualTo(config.Economy.Battle.DeploymentSpreadWidth).Within(0.01f),
                "three unlocked columns still fill the base");
        }

        /// <summary>
        /// The line fills the base whether three columns are unlocked or seven: more columns pack
        /// tighter, they do not reach further. Locking the total width to the configured value —
        /// rather than to the column count — is what keeps the formation readable all run.
        /// </summary>
        [Test]
        public void StartBattle_SpreadWidthDoesNotChangeWhenMoreColumnsUnlock()
        {
            GameConfig config = FastWinConfig();
            float spreadWidth = config.Economy.Battle.DeploymentSpreadWidth;

            GameFlow narrow = CreateFlow(config);
            AddDeployment(narrow, "zu", "narrow-left", 1, 2, 2);
            AddDeployment(narrow, "huo", "narrow-right", 1, 4, 2);
            narrow.StartBattle();
            Dictionary<string, float> narrowX = AllySpawnX(narrow);

            GameFlow wide = CreateFlow(config);
            wide.Economy.Grid.ApplyUnlock(
                UnlockCardFactory.Create(UnlockCardShape.ThreeByOne), new GridCoordinate(0, 2));
            wide.Economy.Grid.ApplyUnlock(
                UnlockCardFactory.Create(UnlockCardShape.ThreeByOne), new GridCoordinate(4, 2));
            wide.Economy.SnapshotToRunState();
            AddDeployment(wide, "zu", "wide-left", 1, 0, 2);
            AddDeployment(wide, "huo", "wide-right", 1, 6, 2);
            wide.StartBattle();
            Dictionary<string, float> wideX = AllySpawnX(wide);

            Assert.That(
                UnlockedColumnCount(wide), Is.GreaterThan(UnlockedColumnCount(narrow)),
                "the wide run must really have unlocked more columns, or this proves nothing");
            Assert.That(narrowX["huo"] - narrowX["zu"], Is.EqualTo(spreadWidth).Within(0.01f));
            Assert.That(wideX["huo"] - wideX["zu"], Is.EqualTo(spreadWidth).Within(0.01f));
        }

        [Test]
        public void FiveWins_ReachMajorVictoryAndRestartClearsRunState()
        {
            GameFlow flow = CreateFlow(FastWinConfig());
            AddDeployment(flow, "gong", "five-stage-winner", 1, 3, 2);

            for (int stage = 1; stage <= 5; stage++)
            {
                Assert.That(flow.StageNumber, Is.EqualTo(stage));
                flow.StartBattle();
                TickUntilPhaseChanges(flow, GameFlowPhase.Battle, 20);
                Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Reward));
                flow.Reward.Select(0);
                flow.BeginNextStage();
            }

            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.MajorVictory));
            Assert.That(flow.StageNumber, Is.EqualTo(5));
            flow.CompleteMajorVictoryAndRestart();
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Deploy));
            Assert.That(flow.StageNumber, Is.EqualTo(1));
            Assert.That(flow.RunState.Coins, Is.EqualTo(45));
            Assert.That(flow.RunState.DeployedGrid, Is.Empty);
            Assert.That(flow.RunState.OwnedEffects, Is.Empty);
            Assert.That(flow.RunState.RefreshCount, Is.Zero);
            Assert.That(flow.RunState.UnlockedCells.Count(value => value), Is.EqualTo(9),
                "a major-stage reset rebuilds the mask from the level rect");
            Assert.That(flow.RunState.UnlockPurchaseCount, Is.Zero);
        }

        [Test]
        public void AllyBaseDestroyed_ChangesToDefeatAndRestartProvidesFreshRun()
        {
            GameFlow flow = CreateFlow(FastLoseConfig());
            flow.StartBattle();

            TickUntilPhaseChanges(flow, GameFlowPhase.Battle, 20);

            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Defeat));
            Assert.That(flow.Battle.Result, Is.EqualTo(BattleResult.Lose));
            uint defeatedSeed = flow.RunState.Seed;
            flow.RestartMajorStage();
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.Deploy));
            Assert.That(flow.StageNumber, Is.EqualTo(1));
            Assert.That(flow.RunState.Seed, Is.Not.EqualTo(defeatedSeed));
        }

        [Test]
        public void RewardScreen_RewardAndTerminalStatesAlwaysExposeAContinuation()
        {
            GameConfig config = FastWinConfig();
            GameFlow flow = CreateFlow(config);
            AddDeployment(flow, "gong", "reward-ui-winner", 1, 3, 2);
            flow.StartBattle();
            TickUntilPhaseChanges(flow, GameFlowPhase.Battle, 20);
            RewardScreen screen = CreateRewardScreen();

            screen.Show(flow, config, new NullArtSource(), GameFlowPhase.Reward);
            Assert.That(screen.RenderedCardCount, Is.EqualTo(3));
            Assert.That(screen.GetComponentsInChildren<Button>(true), Has.Length.GreaterThanOrEqualTo(3));

            flow.Reward.Select(0);
            flow.BeginNextStage();
            // A standalone terminal projection must still render exactly one explicit exit.
            screen.Show(flow, config, new NullArtSource(), GameFlowPhase.Defeat);
            Assert.That(screen.ExitButtonCount, Is.EqualTo(1));
        }

        private GameFlow CreateFlow(GameConfig config)
        {
            var flow = new GameFlow(config, 0xD2002026u);
            flows.Add(flow);
            return flow;
        }

        /// <summary>Builds a deploy screen over a live flow, the way the bootstrap does.</summary>
        private DeployScreen HostDeployScreen(GameConfig config, GameFlow flow)
        {
            var canvasObject = new GameObject(
                "Flow Deploy Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            objects.Add(canvasObject);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 1920f);

            var screenObject = new GameObject("DeployRoot", typeof(RectTransform));
            screenObject.transform.SetParent(canvasObject.transform, false);
            objects.Add(screenObject);
            DeployScreen screen = screenObject.AddComponent<DeployScreen>();
            screen.Initialize(config, flow.Economy, new FlowNullArtSource(), null);
            return screen;
        }

        private sealed class FlowNullArtSource : IBattleArtSource
        {
            public Sprite Find(string assetKey) => null;
        }

        private RewardScreen CreateRewardScreen()
        {
            var canvasObject = new GameObject("Flow Test Canvas", typeof(RectTransform), typeof(Canvas));
            objects.Add(canvasObject);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var screenObject = new GameObject("RewardRoot", typeof(RectTransform));
            objects.Add(screenObject);
            screenObject.transform.SetParent(canvasObject.transform, false);
            return screenObject.AddComponent<RewardScreen>();
        }

        /// <summary>Spawn X of each ally unit in a just-started battle, keyed by unit id.</summary>
        private static Dictionary<string, float> AllySpawnX(GameFlow flow)
        {
            return flow.Battle.CaptureSnapshot()
                .Where(value => value.Team == BattleTeam.Ally)
                .ToDictionary(value => value.DefinitionId, value => value.Position.x);
        }

        private static int UnlockedColumnCount(GameFlow flow)
        {
            bool[] mask = flow.RunState.UnlockedCells;
            int width = flow.RunState.GridWidth;
            return Enumerable.Range(0, width).Count(
                column => Enumerable.Range(0, flow.RunState.GridHeight)
                    .Any(row => mask[(row * width) + column]));
        }

        private static void AddDeployment(
            GameFlow flow,
            string unitId,
            string deploymentId,
            int level,
            int column,
            int row)
        {
            UnitDef definition = GameConfig.Load().GetUnit(unitId);
            flow.Economy.Grid.Apply(
                new DeploymentUnit(deploymentId, unitId, level, UnitFootprint.FromDefinition(definition)),
                new GridCoordinate(column, row));
            flow.Economy.SnapshotToRunState();
        }

        private static void TickUntilPhaseChanges(GameFlow flow, GameFlowPhase phase, int maxTicks)
        {
            for (int index = 0; index < maxTicks && flow.Phase == phase; index++)
            {
                flow.Tick(flow.Battle.FixedDeltaTime);
            }
            Assert.That(flow.Phase, Is.Not.EqualTo(phase), "Flow did not leave the expected phase in time.");
        }

        private static GameConfig FastWinConfig()
        {
            GameConfig config = GameConfig.Load();
            ConfigureSingleBossWave(config);
            config.Economy.Battle.TargetSearchRadius = 200f;
            config.Bases.Enemy.Hp = 1f;
            config.Bases.Enemy.Armor = 0f;
            BossDef boss = config.GetBoss("bld_cheng");
            boss.Hp = 1f;
            boss.Armor = 0f;
            boss.Atk = 0f;
            boss.Range = 0f;
            boss.AtkSpeed = 0f;
            UnitDef ally = config.GetUnit("gong");
            SetCurve(ally.Hp, 10000f);
            SetCurve(ally.Atk, 10000f);
            SetCurve(ally.Range, 200f);
            SetCurve(ally.MinRange, 0f);
            SetCurve(ally.AtkSpeed, 30f);
            SetCurve(ally.Pierce, 100f);
            SetCurve(ally.MoveSpeed, 0f);
            return config;
        }

        private static GameConfig FastLoseConfig()
        {
            GameConfig config = GameConfig.Load();
            ConfigureSingleBossWave(config);
            config.Economy.Battle.TargetSearchRadius = 200f;
            foreach (LevelDef level in config.Levels)
            {
                level.BaseHp = 1f;
            }
            config.Bases.Enemy.Hp = 1f;
            BossDef boss = config.GetBoss("bld_cheng");
            boss.Hp = 10000f;
            boss.Armor = 100f;
            boss.Atk = 10000f;
            boss.Range = 200f;
            boss.AtkSpeed = 30f;
            boss.Pierce = 100f;
            return config;
        }

        // Every wave set has to be collapsed, not just level 1's. WO-E1 split the five sub-stages
        // onto five distinct wave sets (main_20, main_20_stage_2 .. _5); patching only Levels[0]
        // left stages 2-5 running the real ~94s timeline, which no 20-tick fixture can finish.
        private static void ConfigureSingleBossWave(GameConfig config)
        {
            foreach (LevelDef level in config.Levels)
            {
                config.GetWaveSet(level.WaveSetId).Waves = new[]
                {
                    new WaveDef
                    {
                        Index = 20,
                        RewardRank = EnemyRank.Boss,
                        DelaySec = 0.001f,
                        Spawns = new[]
                        {
                            new WaveSpawnDef
                            {
                                UnitId = "bld_cheng",
                                Level = 1,
                                Count = 1,
                                SpreadX = 0f,
                                IntervalSec = 0f
                            }
                        }
                    }
                };
            }
        }

        private static void SetCurve(StatCurve curve, float value)
        {
            curve.Base = value;
            curve.Growth = 0f;
        }

        private sealed class NullArtSource : IBattleArtSource
        {
            public Sprite Find(string key) => null;
        }
    }
}
