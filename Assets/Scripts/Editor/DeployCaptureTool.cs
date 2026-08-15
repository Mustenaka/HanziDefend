using System;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using HanziDefend.View;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor
{
    /// <summary>
    /// Captures the five WO-C4 review shots by driving the <b>running game's own</b> deploy screen.
    ///
    /// <para>Two earlier approaches were dropped (see TECH_DEBT, WO-C4). Capturing from Edit mode
    /// produced black frames because URP does not drive a runtime canvas through a bare
    /// <c>camera.Render()</c> outside the play loop. Building a private capture canvas on top of the
    /// live UI also failed: the screenshot tool re-projects every root overlay canvas through the
    /// capture camera and does not preserve sortingOrder between them, so the bootstrap's screen won
    /// the frame. Driving the real screen sidesteps both, and the shots are more honest for it —
    /// they are the actual game, not a lookalike.</para>
    ///
    /// <para>The sequence is spread over editor frames on purpose: Play-mode <c>Destroy()</c> is
    /// deferred to end of frame, so hand cards discarded while seeding an offer are still on screen
    /// during the same tick.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class DeployCaptureTool
    {
        private const string PendingKey = "HanziDefend.WOC4.CapturePending";

        private static readonly string[] ShotLabels =
        {
            "c4-hand-shapes",
            "c4-drag-valid-green",
            "c4-drag-invalid-red",
            "c4-drag-merge-gold",
            "c4-drag-locked-cell"
        };

        private static readonly List<string> CapturedPaths = new List<string>();

        private static bool running;
        private static int shotIndex;
        private static int settleFrames;
        private static bool built;

        static DeployCaptureTool()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        [MenuItem("HanziDefend/Capture Deploy Drag States")]
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
                // Let the bootstrap finish building its UI before touching it.
                settleFrames = 20;
                Begin();
                return;
            }

            if (settleFrames > 0)
            {
                settleFrames--;
                return;
            }

            if (shotIndex >= ShotLabels.Length)
            {
                Finish();
                return;
            }

            try
            {
                if (!built)
                {
                    BuildShot(shotIndex);
                    built = true;
                    settleFrames = 2;
                    return;
                }

                Canvas.ForceUpdateCanvases();
                CapturedPaths.Add(ScreenshotTool.CaptureForQueue(ShotLabels[shotIndex]));
                ResetShot();
                shotIndex++;
                built = false;
                settleFrames = 2;
            }
            catch (Exception exception)
            {
                Debug.LogError($"WO-C4 capture failed on shot {shotIndex}: {exception}");
                Finish();
            }
        }

        private static void Begin()
        {
            running = true;
            shotIndex = 0;
            built = false;
            CapturedPaths.Clear();
        }

        private static void Finish()
        {
            running = false;
            built = false;
            Debug.Log("WO-C4 deploy captures:" + Environment.NewLine
                      + string.Join(Environment.NewLine, CapturedPaths));
            EditorApplication.ExitPlaymode();
        }

        private static void BuildShot(int index)
        {
            M1GameBootstrap bootstrap = RequireBootstrap();
            DeployScreen screen = bootstrap.DeployScreen;
            CardEconomy economy = bootstrap.Flow.Economy;
            GameConfig config = bootstrap.Config;
            economy.State.Coins = int.MaxValue;

            if (index == 0)
            {
                SeekExpressiveHand(config, economy);
                screen.RefreshAll();
                return;
            }

            var shot = (DragShot)(index - 1);
            string unitId = shot == DragShot.Valid ? "tie" : "zu";
            CardOfferItem card = SeekUnitInHand(economy, unitId);

            GridCoordinate anchor;
            switch (shot)
            {
                case DragShot.Valid:
                    anchor = new GridCoordinate(2, 2);
                    break;
                case DragShot.Merge:
                    anchor = new GridCoordinate(3, 3);
                    PlaceFixture(economy, config, unitId, "capture-merge", anchor);
                    break;
                case DragShot.Invalid:
                    anchor = new GridCoordinate(3, 3);
                    PlaceFixture(economy, config, "gong", "capture-blocker", anchor);
                    break;
                default:
                    anchor = new GridCoordinate(0, 0);
                    break;
            }

            screen.RefreshAll();
            UnitFootprint footprint = screen.HandCardFootprint(card);
            if (!screen.BeginCardDrag(card, screen.DragScreenPointForAnchor(anchor, footprint)))
            {
                throw new InvalidOperationException($"Could not begin a drag for '{unitId}'.");
            }
        }

        /// <summary>Clears any live drag and fixture so the next shot starts from a clean board.</summary>
        private static void ResetShot()
        {
            M1GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<M1GameBootstrap>();
            if (bootstrap?.DeployScreen == null)
            {
                return;
            }

            bootstrap.DeployScreen.CancelDrag();
            CardEconomy economy = bootstrap.Flow.Economy;
            economy.Grid.Remove("capture-merge");
            economy.Grid.Remove("capture-blocker");
            economy.SnapshotToRunState();
            bootstrap.DeployScreen.RefreshAll();
        }

        private static void PlaceFixture(
            CardEconomy economy,
            GameConfig config,
            string unitId,
            string deploymentId,
            GridCoordinate anchor)
        {
            economy.Grid.Remove(deploymentId);
            UnitDef unit = config.GetUnit(unitId);
            economy.Grid.Apply(
                new DeploymentUnit(deploymentId, unit.Id, 1, UnitFootprint.FromDefinition(unit)),
                anchor);
            economy.SnapshotToRunState();
        }

        private static M1GameBootstrap RequireBootstrap()
        {
            M1GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<M1GameBootstrap>();
            if (bootstrap == null || bootstrap.DeployScreen == null || bootstrap.Flow == null)
            {
                throw new InvalidOperationException(
                    "WO-C4 capture needs the M1 bootstrap running in the Deploy phase.");
            }
            return bootstrap;
        }

        private enum DragShot
        {
            Valid,
            Invalid,
            Merge,
            Locked
        }

        /// <summary>
        /// Refreshes the real card pool until it deals a hand that shows the shape range, then stops
        /// on it. Nothing is injected — the captured hand is one a player can actually be dealt.
        /// </summary>
        private static void SeekExpressiveHand(GameConfig config, CardEconomy economy)
        {
            if (ScoreHand(config, economy.CurrentOffer) >= 100)
            {
                return;
            }

            for (int attempt = 0; attempt < 4000; attempt++)
            {
                economy.State.Coins = int.MaxValue;
                if (!economy.TryRefresh(out CardOffer offer))
                {
                    break;
                }

                if (ScoreHand(config, offer) >= 100)
                {
                    break;
                }
            }

            economy.State.Coins = 999;
        }

        /// <summary>Distinct footprints on show, with a bonus for including a notched L shape.</summary>
        private static int ScoreHand(GameConfig config, CardOffer offer)
        {
            if (offer == null)
            {
                return 0;
            }

            var shapes = new HashSet<(int Width, int Height, UnitFootprintShape Shape)>();
            bool hasNotch = false;
            for (int index = 0; index < offer.Cards.Count; index++)
            {
                CardOfferItem card = offer.Cards[index];
                if (card.Category != CardCategory.Unit)
                {
                    continue;
                }

                UnitDef unit = config.GetUnit(card.ContentId);
                shapes.Add((unit.GridW, unit.GridH, unit.Footprint));
                hasNotch |= unit.Footprint != UnitFootprintShape.Rectangle;
            }

            int score = shapes.Count * 10;
            if (hasNotch)
            {
                score += 40;
            }
            if (shapes.Count >= 3 && hasNotch)
            {
                score += 60;
            }
            return score;
        }

        /// <summary>Refreshes until the wanted unit is dealt, then returns that live hand card.</summary>
        private static CardOfferItem SeekUnitInHand(CardEconomy economy, string unitId)
        {
            for (int attempt = 0; attempt < 6000; attempt++)
            {
                CardOfferItem match = economy.CurrentOffer.Cards.FirstOrDefault(
                    value => value.Category == CardCategory.Unit
                             && string.Equals(value.ContentId, unitId, StringComparison.Ordinal));
                if (match != null)
                {
                    economy.State.Coins = 999;
                    return match;
                }

                economy.State.Coins = int.MaxValue;
                if (!economy.TryRefresh(out _))
                {
                    break;
                }
            }

            throw new InvalidOperationException($"'{unitId}' never appeared in the capture hand search.");
        }
    }
}
