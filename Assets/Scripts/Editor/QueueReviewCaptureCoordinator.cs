using System;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Deploy;
using HanziDefend.View;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor
{
    /// <summary>
    /// Builds a deterministic, presentation-only review fixture for the unattended queue.
    /// Rules still transition through GameFlow; only battle outcomes and the late boss event are
    /// injected so seven screenshots can be captured without waiting through five real battles.
    /// </summary>
    [InitializeOnLoad]
    internal static class QueueReviewCaptureCoordinator
    {
        private const string MenuPath = "HanziDefend/Capture Queue Review";
        private const string ActiveSessionKey = "HanziDefend.QueueCapture.Active";
        private const string StepSessionKey = "HanziDefend.QueueCapture.Step";
        private const int RequiredCaptureCount = 7;

        private static bool active;
        private static int step;
        private static string pendingLabel;
        private static int pendingUpdates;
        private static bool captureInProgress;

        static QueueReviewCaptureCoordinator()
        {
            active = SessionState.GetBool(ActiveSessionKey, false);
            step = SessionState.GetInt(StepSessionKey, 0);
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem(MenuPath)]
        private static void StartCapture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Exit Play Mode before starting the queue review capture.");
                return;
            }

            active = true;
            step = 0;
            pendingLabel = null;
            pendingUpdates = 0;
            captureInProgress = false;
            PersistSession();
            Debug.Log("[Queue Capture] Session started; entering Play Mode for seven review frames.");
            EditorApplication.EnterPlaymode();
        }

        private static void Update()
        {
            if (!active || captureInProgress || !EditorApplication.isPlaying)
            {
                return;
            }

            M1GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<M1GameBootstrap>();
            if (bootstrap == null || bootstrap.Flow == null)
            {
                return;
            }

            bootstrap.AutoRun = false;
            if (!string.IsNullOrEmpty(pendingLabel))
            {
                if (pendingUpdates++ < 2)
                {
                    return;
                }

                CapturePending(bootstrap);
                return;
            }

            try
            {
                switch (step)
                {
                    case 0:
                        RequirePhase(bootstrap.Flow, GameFlowPhase.Deploy);
                        Schedule("stage1-deploy-initial");
                        break;
                    case 1:
                        PopulateMixedDeployment(bootstrap);
                        Schedule("stage1-deploy-mixed-shapes");
                        break;
                    case 2:
                        bootstrap.StartBattle();
                        Schedule("stage1-battle");
                        break;
                    case 3:
                        SettleCurrentBattleAsWin(bootstrap.Flow);
                        Schedule("stage1-reward");
                        break;
                    case 4:
                        AdvanceToStageThree(bootstrap);
                        Schedule("stage3-deploy-expanded");
                        break;
                    case 5:
                        AdvanceToStageFiveBoss(bootstrap);
                        Schedule("stage5-boss");
                        break;
                    case 6:
                        SettleCurrentBattleAsWin(bootstrap.Flow);
                        SelectRewardAndContinue(bootstrap.Flow);
                        RequirePhase(bootstrap.Flow, GameFlowPhase.MajorVictory);
                        Schedule("major-victory");
                        break;
                    default:
                        CompleteSession();
                        break;
                }
            }
            catch (Exception exception)
            {
                FailSession(exception);
            }
        }

        private static void CapturePending(M1GameBootstrap bootstrap)
        {
            captureInProgress = true;
            string label = pendingLabel;
            try
            {
                ValidateRootTruthTable(bootstrap);
                Canvas.ForceUpdateCanvases();
                string path = ScreenshotTool.CaptureForQueue(label);
                Debug.Log($"[Queue Capture] {step + 1}/{RequiredCaptureCount} {label}: {path}");
                step++;
                pendingLabel = null;
                pendingUpdates = 0;
                PersistSession();
            }
            catch (Exception exception)
            {
                FailSession(exception);
            }
            finally
            {
                captureInProgress = false;
            }
        }

        private static void PopulateMixedDeployment(M1GameBootstrap bootstrap)
        {
            RequirePhase(bootstrap.Flow, GameFlowPhase.Deploy);
            CardEconomy economy = bootstrap.Flow.Economy;
            if (economy.Grid.Placements.Count == 0)
            {
                // Anchors are relative to the level's starting unlock rect, so the fixture keeps
                // working whatever rect the level declares.
                LevelDef level = bootstrap.Config.GetLevel(bootstrap.Flow.CurrentLevelId);
                int col = level.InitialUnlock.Col;
                int row = level.InitialUnlock.Row;
                PlaceFixtureUnit(bootstrap.Config, economy, "dun", "queue-dun", col, row);
                PlaceFixtureUnit(bootstrap.Config, economy, "mao", "queue-mao", col + 2, row);
                PlaceFixtureUnit(bootstrap.Config, economy, "gong", "queue-gong", col, row + 1);
                economy.SnapshotToRunState();
            }
            bootstrap.DeployScreen.RefreshAll();
        }

        private static void PlaceFixtureUnit(
            GameConfig config,
            CardEconomy economy,
            string unitId,
            string deploymentId,
            int column,
            int row)
        {
            UnitDef definition = config.GetUnit(unitId);
            var unit = new DeploymentUnit(
                deploymentId,
                unitId,
                (int)UnitTier.Green,
                UnitFootprint.FromDefinition(definition));
            DeploymentApplyResult result = economy.Grid.Apply(unit, new GridCoordinate(column, row));
            if (result.Action != DeploymentActionKind.Place)
            {
                throw new InvalidOperationException(
                    $"Queue fixture expected to place '{unitId}' but received {result.Action}.");
            }
        }

        /// <summary>
        /// Grows the unlock mask by one vertical card on each flank so the stage-three capture shows
        /// a partly unlocked field rather than the untouched starting rect.
        /// </summary>
        private static void WidenUnlockedRegion(DeploymentGrid grid)
        {
            UnlockCard column = UnlockCardFactory.Create(UnlockCardShape.OneByThree);
            IReadOnlyList<GridCoordinate> anchors = grid.GetLegalUnlockAnchors(column);
            if (anchors.Count == 0)
            {
                return;
            }

            grid.ApplyUnlock(column, anchors[0]);
            anchors = grid.GetLegalUnlockAnchors(column);
            if (anchors.Count > 0)
            {
                grid.ApplyUnlock(column, anchors[anchors.Count - 1]);
            }
        }

        private static void AdvanceToStageThree(M1GameBootstrap bootstrap)
        {
            SelectRewardAndContinue(bootstrap.Flow);
            RequireStage(bootstrap.Flow, 2);
            WidenUnlockedRegion(bootstrap.Flow.Economy.Grid);
            bootstrap.Flow.Economy.CreditBattleDrops(bootstrap.Config.Economy.DropCoins.Boss);
            bootstrap.Flow.Economy.SnapshotToRunState();
            CompleteStageAndContinue(bootstrap);
            RequireStage(bootstrap.Flow, 3);
            bootstrap.DeployScreen.RefreshAll();
        }

        private static void AdvanceToStageFiveBoss(M1GameBootstrap bootstrap)
        {
            CompleteStageAndContinue(bootstrap);
            RequireStage(bootstrap.Flow, 4);
            CompleteStageAndContinue(bootstrap);
            RequireStage(bootstrap.Flow, 5);

            bootstrap.StartBattle();
            InjectStageFiveBossPresentation(bootstrap);
        }

        private static void CompleteStageAndContinue(M1GameBootstrap bootstrap)
        {
            RequirePhase(bootstrap.Flow, GameFlowPhase.Deploy);
            bootstrap.StartBattle();
            SettleCurrentBattleAsWin(bootstrap.Flow);
            SelectRewardAndContinue(bootstrap.Flow);
        }

        private static void SettleCurrentBattleAsWin(GameFlow flow)
        {
            RequirePhase(flow, GameFlowPhase.Battle);
            BattleSystem battle = flow.Battle
                ?? throw new InvalidOperationException("Queue fixture expected an active battle.");
            BattleBaseSnapshot allyBase = battle.GetBaseSnapshot(battle.AllyBaseEntityId);
            flow.BattleSettled(new BattleSettledEvent(
                long.MaxValue - flow.StageNumber,
                battle.TickIndex,
                battle.SimulatedTimeSeconds,
                BattleResult.Win,
                battle.AllyBaseEntityId,
                battle.BossEntityId,
                allyBase.CurrentHp,
                0f,
                false,
                true));
            RequirePhase(flow, GameFlowPhase.Reward);
        }

        private static void SelectRewardAndContinue(GameFlow flow)
        {
            RequirePhase(flow, GameFlowPhase.Reward);
            if (flow.Reward == null || flow.Reward.CurrentOffer == null || flow.Reward.CurrentOffer.Cards.Count == 0)
            {
                throw new InvalidOperationException("Queue fixture requires a settlement offer.");
            }
            flow.Reward.Select(0);
            flow.BeginNextStage();
        }

        private static void InjectStageFiveBossPresentation(M1GameBootstrap bootstrap)
        {
            GameFlow flow = bootstrap.Flow;
            RequirePhase(flow, GameFlowPhase.Battle);
            LevelDef level = bootstrap.Config.Levels.Single(value => value.StageIndex == 5);
            WaveSetDef waveSet = bootstrap.Config.GetWaveSet(level.WaveSetId);
            BossDef boss = bootstrap.Config.Bosses.First();
            Position2Def spawn = bootstrap.Config.Economy.Battle.EnemySpawnCenter;
            Vector2 position = new Vector2(spawn.X, spawn.Y);
            double time = flow.Battle.SimulatedTimeSeconds;
            long tick = flow.Battle.TickIndex;
            int entityId = int.MaxValue - 5;
            var stats = new BattleStats(
                boss.Hp,
                boss.Atk,
                boss.Range,
                0f,
                boss.AtkSpeed,
                1f / boss.AtkSpeed,
                boss.Armor,
                boss.Pierce,
                0f);
            var context = new WaveSpawnContext(
                level.WaveSetId,
                waveSet.Waves.Length,
                0,
                0,
                time,
                EnemyRank.Boss);

            flow.WaveStarted(new WaveStartedEvent(
                long.MaxValue - 30,
                tick,
                time,
                time,
                level.WaveSetId,
                waveSet.Waves.Length,
                EnemyRank.Boss));
            flow.UnitSpawned(new UnitSpawnedEvent(
                long.MaxValue - 20,
                tick,
                time,
                entityId,
                boss.Id,
                BattleTeam.Enemy,
                (int)UnitTier.Green,
                stats,
                stats.MaxHp,
                position,
                Vector2.down,
                BattleUnitState.Idle,
                context));
            flow.BossSpawned(new BossSpawnedEvent(
                long.MaxValue - 10,
                tick,
                time,
                entityId,
                boss.Id,
                (int)UnitTier.Green,
                stats,
                position,
                context));
        }

        private static void ValidateRootTruthTable(M1GameBootstrap bootstrap)
        {
            GameFlowPhase phase = bootstrap.Flow.Phase;
            bool expectedDeploy = phase == GameFlowPhase.Deploy;
            bool expectedBattle = phase != GameFlowPhase.Deploy;
            bool expectedReward = phase == GameFlowPhase.Reward
                                  || phase == GameFlowPhase.MajorVictory
                                  || phase == GameFlowPhase.Defeat;
            if (bootstrap.DeployRoot.activeSelf != expectedDeploy
                || bootstrap.BattleRoot.activeSelf != expectedBattle
                || bootstrap.RewardRoot.activeSelf != expectedReward)
            {
                throw new InvalidOperationException(
                    $"Queue capture root truth table mismatch in {phase}: " +
                    $"Deploy={bootstrap.DeployRoot.activeSelf}, " +
                    $"Battle={bootstrap.BattleRoot.activeSelf}, " +
                    $"Reward={bootstrap.RewardRoot.activeSelf}.");
            }
        }

        private static void RequirePhase(GameFlow flow, GameFlowPhase expected)
        {
            if (flow.Phase != expected)
            {
                throw new InvalidOperationException(
                    $"Queue fixture expected {expected} but GameFlow is {flow.Phase}.");
            }
        }

        private static void RequireStage(GameFlow flow, int expected)
        {
            if (flow.StageNumber != expected || flow.Phase != GameFlowPhase.Deploy)
            {
                throw new InvalidOperationException(
                    $"Queue fixture expected stage {expected} Deploy but is " +
                    $"stage {flow.StageNumber} {flow.Phase}.");
            }
        }

        private static void Schedule(string label)
        {
            pendingLabel = label;
            pendingUpdates = 0;
        }

        private static void CompleteSession()
        {
            active = false;
            PersistSession();
            Debug.Log("[Queue Capture] Seven screenshots captured; exiting Play Mode for import.");
            EditorApplication.ExitPlaymode();
        }

        private static void FailSession(Exception exception)
        {
            active = false;
            pendingLabel = null;
            PersistSession();
            Debug.LogException(exception);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && !active && step >= RequiredCaptureCount)
            {
                Debug.Log("[Queue Capture] Review PNG files imported into Assets/Screenshots.");
            }
        }

        private static void PersistSession()
        {
            SessionState.SetBool(ActiveSessionKey, active);
            SessionState.SetInt(StepSessionKey, step);
        }
    }
}
