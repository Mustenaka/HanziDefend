using System;
using System.Collections.Generic;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using HanziDefend.View;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor
{
    /// <summary>
    /// The four deploy-card review shots (WO-C9 structure, WO-C10 layering), taken off the running game's own deploy screen.
    ///
    /// <para>Same approach as <c>DeployCaptureTool</c> and for the same reasons recorded in
    /// TECH_DEBT: capturing from Edit mode gives black frames because URP will not drive a runtime
    /// canvas outside the play loop, and a private capture canvas loses to the bootstrap's own
    /// because the screenshot path does not preserve sortingOrder between root overlay canvases.</para>
    ///
    /// <para>The board is built by placing units straight onto the grid at chosen levels — the same
    /// state a finished drag or a merge leaves behind — because the point of the shot is to show
    /// whether four different levels can be told apart, and waiting for the card pool to deal four
    /// merges would take the whole stage.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class DeployUnitCardCaptureTool
    {
        private const string PendingKey = "HanziDefend.WOC11.CapturePending";

        private static readonly List<string> CapturedPaths = new List<string>();
        private static readonly List<string> Findings = new List<string>();

        private static bool running;
        private static int step;
        private static int settleFrames;

        static DeployUnitCardCaptureTool()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        [MenuItem("HanziDefend/Capture Deployed Unit Cards")]
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
                Debug.LogError($"WO-C11 capture failed at step {step}: {exception}");
                Finish();
            }
        }

        private static void Advance()
        {
            M1GameBootstrap bootstrap = RequireBootstrap();
            DeployScreen screen = bootstrap.DeployScreen;

            switch (step)
            {
                case 0:
                    bootstrap.AutoRun = false;
                    WidenGrid(bootstrap);
                    BuildLevelLadder(bootstrap);
                    Shot("c11-levels-no-duplicate-names");
                    Next(2);
                    break;

                case 1:
                    BuildNotchedPair(bootstrap);
                    Shot("c11-l-shape-readable");
                    Next(2);
                    break;

                case 2:
                    // The stat sheet, opened on a unit that has both a counter bonus and a trait.
                    if (!screen.ShowDeployedUnitInfo(FindDeploymentId(bootstrap, "nub")))
                    {
                        throw new InvalidOperationException("could not open the info panel");
                    }
                    Findings.Add($"info panel rows: {screen.InfoPanelRows.Count}");
                    Shot("c11-info-panel-is-the-fallback");
                    Next(2);
                    break;

                case 3:
                    screen.CloseUnitInfo();
                    BeginReturnDrag(bootstrap);
                    Shot("c11-hand-row");
                    Next(2);
                    break;

                case 4:
                    screen.CancelDrag();
                    Finish();
                    break;
            }
        }

        private static void Next(int frames)
        {
            step++;
            settleFrames = frames;
        }

        /// <summary>
        /// Opens the whole board so the shots are not cramped into the starting 3x3.
        ///
        /// <para>Sweeps repeatedly rather than once: unlocking requires adjacency to an already-open
        /// cell, so a single row-major pass starts at a corner that nothing touches yet and gives up
        /// on most of the board. Each sweep grows the frontier by one ring.</para>
        /// </summary>
        private static void WidenGrid(M1GameBootstrap bootstrap)
        {
            DeploymentGrid grid = bootstrap.Flow.Economy.Grid;
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

                    UnlockCard card = UnlockCardFactory.Create(UnlockCardShape.OneByOne);
                    if (grid.EvaluateUnlock(card, cell).IsValid)
                    {
                        grid.ApplyUnlock(card, cell);
                        progressed = true;
                    }
                }

                if (!progressed)
                {
                    break;
                }
            }

            bootstrap.Flow.Economy.SnapshotToRunState();
            Findings.Add($"unlocked {grid.UnlockedCellCount}/{grid.CellCount} cells for the shots");
        }

        /// <summary>Four levels side by side — the shot that answers "看不懂等级".</summary>
        private static void BuildLevelLadder(M1GameBootstrap bootstrap)
        {
            Place(bootstrap, "zu", 1, new GridCoordinate(0, 5));
            Place(bootstrap, "gong", 2, new GridCoordinate(1, 5));
            Place(bootstrap, "huo", 3, new GridCoordinate(2, 5));
            Place(bootstrap, "bing", 4, new GridCoordinate(3, 5));
            Place(bootstrap, "zqi", 2, new GridCoordinate(4, 3));
            Place(bootstrap, "dun", 3, new GridCoordinate(0, 3));
            Place(bootstrap, "mao", 1, new GridCoordinate(3, 3));
            Place(bootstrap, "nub", 4, new GridCoordinate(6, 4));
            Refresh(bootstrap);
            Findings.Add($"level ladder: {bootstrap.DeployScreen.DeployedUnitCardCount} cards on the board");
        }

        private static void BuildNotchedPair(M1GameBootstrap bootstrap)
        {
            Place(bootstrap, "nuc", 2, new GridCoordinate(0, 0));
            Place(bootstrap, "chc", 3, new GridCoordinate(3, 0));
            Place(bootstrap, "tie", 1, new GridCoordinate(5, 0));
            Refresh(bootstrap);
            Findings.Add("notched pair placed: 弩车 缺右上 / 冲车 缺左下");
        }

        /// <summary>Picks a deployed unit up and parks the ghost over the hand, mid-undo.</summary>
        private static void BeginReturnDrag(M1GameBootstrap bootstrap)
        {
            DeployScreen screen = bootstrap.DeployScreen;
            string id = FindDeploymentId(bootstrap, "zu");
            if (!screen.BeginPlacementDrag(id, screen.CellScreenPoint(new GridCoordinate(0, 5))))
            {
                throw new InvalidOperationException("could not pick the unit up");
            }

            RectTransform hand = null;
            RectTransform[] rects = screen.GetComponentsInChildren<RectTransform>(true);
            for (int index = 0; index < rects.Length; index++)
            {
                if (rects[index].gameObject.name == "Hand Panel")
                {
                    hand = rects[index];
                    break;
                }
            }
            if (hand == null)
            {
                throw new InvalidOperationException("no hand panel to drag onto");
            }

            var corners = new Vector3[4];
            hand.GetWorldCorners(corners);
            screen.DragTo((corners[0] + corners[2]) * 0.5f);
            Findings.Add("dragging 卒 back onto the hand");
        }

        private static string FindDeploymentId(M1GameBootstrap bootstrap, string unitId)
        {
            IReadOnlyList<DeploymentPlacement> placements = bootstrap.Flow.Economy.Grid.Placements;
            for (int index = 0; index < placements.Count; index++)
            {
                if (string.Equals(placements[index].UnitId, unitId, StringComparison.Ordinal))
                {
                    return placements[index].DeploymentId;
                }
            }
            throw new InvalidOperationException($"'{unitId}' is not on the board");
        }

        private static void Place(M1GameBootstrap bootstrap, string unitId, int level, GridCoordinate anchor)
        {
            UnitDef definition = bootstrap.Config.GetUnit(unitId);
            var unit = new DeploymentUnit(
                $"c9-{unitId}", definition.Id, level, UnitFootprint.FromDefinition(definition));
            DeploymentGrid grid = bootstrap.Flow.Economy.Grid;
            DeploymentEvaluation evaluation = grid.Evaluate(unit, anchor);
            if (!evaluation.IsValid || evaluation.Action != DeploymentActionKind.Place)
            {
                Findings.Add($"skipped {unitId} at {anchor.Column},{anchor.Row}: {evaluation.Message}");
                return;
            }
            grid.Apply(unit, anchor);
        }

        private static void Refresh(M1GameBootstrap bootstrap)
        {
            bootstrap.Flow.Economy.SnapshotToRunState();
            bootstrap.DeployScreen.RefreshAll();
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
            CapturedPaths.Clear();
            Findings.Clear();
        }

        private static void Finish()
        {
            running = false;
            Debug.Log("WO-C11 card labels:" + Environment.NewLine
                      + string.Join(Environment.NewLine, Findings) + Environment.NewLine
                      + "captures:" + Environment.NewLine
                      + string.Join(Environment.NewLine, CapturedPaths));
            EditorApplication.ExitPlaymode();
        }

        private static M1GameBootstrap RequireBootstrap()
        {
            M1GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<M1GameBootstrap>();
            if (bootstrap == null || bootstrap.DeployScreen == null || bootstrap.Flow == null)
            {
                throw new InvalidOperationException("WO-C11 capture needs the M1 bootstrap in the Deploy phase.");
            }
            return bootstrap;
        }
    }
}
