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
    /// Plays two minor stages back to back in the real game and photographs what WO-C8 fixed.
    ///
    /// <para>The bug it exists to catch never showed up in a single stage: the deploy screen is
    /// built once and reused, so only the <i>second</i> stage arrived with an empty hand and a
    /// refresh button that threw. Nothing short of actually finishing stage one and walking back
    /// into deploy reproduces that, which is why this drives the shipped bootstrap rather than a
    /// fixture — same screen instance, same phase callbacks, same everything.</para>
    ///
    /// <para>Both battles are really fought — the spread shots are of live spawns, at the opening
    /// frame before the line closes on anything — but the result is then forced to a win the way
    /// <c>QueueReviewCaptureCoordinator</c> already does. That is not papering over a defect: the
    /// stage-one wave beats a full garrison of maxed units today, which is the known-unmet WO-E1
    /// balance, suspended and tracked separately. Waiting for a real win would make this tool
    /// unable to reach stage two at all, which is the only place the bug it checks for lives.</para>
    ///
    /// <para>Frames are spread on purpose: play-mode <c>Destroy()</c> is deferred to end of frame,
    /// so a screen rebuilt this tick still has last tick's children in the picture.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class CrossStageCaptureTool
    {
        private const string PendingKey = "HanziDefend.WOC8.CapturePending";
        /// <summary>Frames of real combat before the result is declared. Long enough that the shot
        /// is of a battle that actually happened, short enough that the base is still standing.</summary>
        private const int BattleFrameBudget = 150;

        private static readonly List<string> CapturedPaths = new List<string>();
        private static readonly List<string> Findings = new List<string>();

        private static bool running;
        private static int step;
        private static int settleFrames;
        private static int battleFrames;

        static CrossStageCaptureTool()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        [MenuItem("HanziDefend/Capture Cross-Stage Round Trip")]
        public static void CaptureAll()
        {
            if (EditorApplication.isPlaying)
            {
                Begin();
                return;
            }

            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnEditorUpdate()
        {
            if (!running)
            {
                if (!SessionState.GetBool(PendingKey, false)
                    || !EditorApplication.isPlaying
                    || EditorApplication.isCompiling
                    || EditorApplication.isUpdating)
                {
                    return;
                }

                SessionState.SetBool(PendingKey, false);
                settleFrames = 30;
                Begin();
                return;
            }

            if (settleFrames > 0)
            {
                settleFrames--;
                return;
            }

            try
            {
                Advance();
            }
            catch (Exception exception)
            {
                Findings.Add($"FAILED at step {step}: {exception.Message}");
                Debug.LogError($"WO-C8 cross-stage capture failed at step {step}: {exception}");
                Finish();
            }
        }

        private static void Advance()
        {
            M1GameBootstrap bootstrap = RequireBootstrap();

            switch (step)
            {
                case 0:
                    OpenStageOne(bootstrap);
                    Shot("c8-stage1-deploy");
                    Next(2);
                    break;

                case 1:
                    // Simulation stays off until the shot is taken, so the spread photographed is
                    // the spawn itself. Two frames of drift is enough to close the line visibly.
                    bootstrap.AutoRun = false;
                    bootstrap.SimulationSpeed = 12f;
                    bootstrap.StartBattle();
                    battleFrames = 0;
                    Next(2);
                    break;

                case 2:
                    Shot("c8-stage1-battle-spread");
                    RecordSpread(bootstrap, "stage 1");
                    bootstrap.AutoRun = true;
                    Next(1);
                    break;

                case 3:
                    if (!AwaitSettlement(bootstrap))
                    {
                        return;
                    }
                    Next(2);
                    break;

                case 4:
                    TakeReward(bootstrap);
                    Next(4);
                    break;

                case 5:
                    // Everything the bug broke, checked on the reused screen the moment it returns.
                    InspectSecondStage(bootstrap);
                    Shot("c8-stage2-deploy");
                    Next(2);
                    break;

                case 6:
                    OpenStageTwo(bootstrap);
                    bootstrap.StartBattle();
                    battleFrames = 0;
                    Next(2);
                    break;

                case 7:
                    Shot("c8-stage2-battle-spread");
                    RecordSpread(bootstrap, "stage 2");
                    bootstrap.AutoRun = true;
                    Next(1);
                    break;

                case 8:
                    if (!AwaitSettlement(bootstrap))
                    {
                        return;
                    }
                    Findings.Add("two minor stages played through to settlement");
                    Finish();
                    break;
            }
        }

        private static void Next(int frames)
        {
            step++;
            settleFrames = frames;
        }

        private static void OpenStageOne(M1GameBootstrap bootstrap)
        {
            bootstrap.AutoRun = false;
            GameFlow flow = bootstrap.Flow;
            RequirePhase(flow, GameFlowPhase.Deploy);
            Findings.Add($"stage 1 hand rendered: {bootstrap.DeployScreen.RenderedHandCount} cards");
            if (bootstrap.DeployScreen.RenderedHandCount == 0)
            {
                throw new InvalidOperationException("stage 1 arrived with an empty hand");
            }

            GarrisonUnlockedCells(bootstrap, 3);
            bootstrap.DeployScreen.RefreshAll();
        }

        private static void OpenStageTwo(M1GameBootstrap bootstrap)
        {
            GarrisonUnlockedCells(bootstrap, 4);
            bootstrap.DeployScreen.RefreshAll();
            bootstrap.AutoRun = false;
            bootstrap.SimulationSpeed = 12f;
        }

        /// <summary>
        /// Fills every unlocked cell with a levelled 1x1 unit. Placed through the grid, which is
        /// where a finished drag lands anyway, so the board is one a player could have built.
        /// </summary>
        private static void GarrisonUnlockedCells(M1GameBootstrap bootstrap, int level)
        {
            string[] fighters = { "zu", "gong" };
            CardEconomy economy = bootstrap.Flow.Economy;
            GameConfig config = bootstrap.Config;
            DeploymentGrid grid = economy.Grid;
            int placed = 0;

            for (int row = 0; row < grid.Height; row++)
            for (int column = 0; column < grid.Width; column++)
            {
                var cell = new GridCoordinate(column, row);
                if (!grid.IsUnlocked(cell) || grid.TryGetPlacementAt(cell, out _))
                {
                    continue;
                }

                UnitDef unit = config.GetUnit(fighters[placed % fighters.Length]);
                var incoming = new DeploymentUnit(
                    $"c8-{column}-{row}", unit.Id, level, UnitFootprint.FromDefinition(unit));

                // Place only, never merge: a merge would eat a neighbour and thin the line out.
                DeploymentEvaluation evaluation = grid.Evaluate(incoming, cell);
                if (!evaluation.IsValid || evaluation.Action != DeploymentActionKind.Place)
                {
                    continue;
                }

                grid.Apply(incoming, cell);
                placed++;
            }

            economy.SnapshotToRunState();
            Findings.Add($"garrisoned {placed} cells at level {level}");
        }

        /// <summary>
        /// Lets the battle run for real, then declares the win. See the type comment: the wave is
        /// unwinnable at present balance, so the alternative is never reaching stage two.
        /// </summary>
        private static bool AwaitSettlement(M1GameBootstrap bootstrap)
        {
            GameFlow flow = bootstrap.Flow;
            if (flow.Phase != GameFlowPhase.Battle)
            {
                Findings.Add($"battle already settled into {flow.Phase}");
                return true;
            }

            if (battleFrames++ < BattleFrameBudget)
            {
                return false;
            }

            BattleSystem battle = flow.Battle;
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
            Findings.Add(
                $"battle ran {battleFrames} frames to tick {battle.TickIndex}, then forced to Win "
                + "(WO-E1 balance is suspended, not solvable here)");
            return true;
        }

        private static void TakeReward(M1GameBootstrap bootstrap)
        {
            GameFlow flow = bootstrap.Flow;
            RequirePhase(flow, GameFlowPhase.Reward);
            string chosen = flow.Reward.CurrentOffer.Cards[0].EffectId;
            flow.Reward.Select(0);
            flow.BeginNextStage();
            RequirePhase(flow, GameFlowPhase.Deploy);
            Findings.Add($"settlement reward taken: {chosen}");
        }

        /// <summary>The three symptoms the player reported, checked where they were reported.</summary>
        private static void InspectSecondStage(M1GameBootstrap bootstrap)
        {
            GameFlow flow = bootstrap.Flow;
            DeployScreen screen = bootstrap.DeployScreen;
            if (flow.StageNumber != 2)
            {
                throw new InvalidOperationException($"expected stage 2, got {flow.StageNumber}");
            }

            int hand = screen.RenderedHandCount;
            Findings.Add($"stage 2 hand rendered: {hand} cards");
            if (hand == 0)
            {
                throw new InvalidOperationException("stage 2 arrived with an empty hand");
            }

            int chips = screen.OwnedEffectChipCount;
            Findings.Add(
                $"stage 2 owned-effect chips: {chips} for {flow.RunState.OwnedEffects.Length} owned "
                + $"({string.Join(", ", flow.RunState.OwnedEffects)})");
            if (chips == 0)
            {
                throw new InvalidOperationException("stage 2 shows no owned effects");
            }

            flow.RunState.Coins = 999;
            screen.RefreshOffer();
            Findings.Add($"refresh on stage 2 returned {screen.RenderedHandCount} cards without throwing");
            screen.RefreshAll();
        }

        private static void RecordSpread(M1GameBootstrap bootstrap, string label)
        {
            float[] xs = bootstrap.Flow.Battle.CaptureSnapshot()
                .Where(value => value.Team == BattleTeam.Ally)
                .Select(value => value.Position.x)
                .OrderBy(value => value)
                .ToArray();
            if (xs.Length == 0)
            {
                throw new InvalidOperationException($"{label} spawned no allies");
            }

            Findings.Add(
                $"{label} spawn span {xs[xs.Length - 1] - xs[0]:F2} across {xs.Length} units, "
                + $"x from {xs[0]:F2} to {xs[xs.Length - 1]:F2}");
        }

        private static void Shot(string label)
        {
            Canvas.ForceUpdateCanvases();
            CapturedPaths.Add(ScreenshotTool.CaptureForQueue(label));
        }

        private static void Begin()
        {
            running = true;
            step = 0;
            battleFrames = 0;
            CapturedPaths.Clear();
            Findings.Clear();
        }

        private static void Finish()
        {
            running = false;
            Debug.Log("WO-C8 cross-stage round trip:" + Environment.NewLine
                      + string.Join(Environment.NewLine, Findings) + Environment.NewLine
                      + "captures:" + Environment.NewLine
                      + string.Join(Environment.NewLine, CapturedPaths));
            EditorApplication.ExitPlaymode();
        }

        private static void RequirePhase(GameFlow flow, GameFlowPhase phase)
        {
            if (flow.Phase != phase)
            {
                throw new InvalidOperationException($"expected phase {phase}, found {flow.Phase}");
            }
        }

        private static M1GameBootstrap RequireBootstrap()
        {
            M1GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<M1GameBootstrap>();
            if (bootstrap == null || bootstrap.DeployScreen == null || bootstrap.Flow == null)
            {
                throw new InvalidOperationException("WO-C8 capture needs the M1 bootstrap running.");
            }
            return bootstrap;
        }
    }
}
