using System;
using System.Collections;
using System.Collections.Generic;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HanziDefend.View
{
    /// <summary>How a cell is currently being highlighted by a drag preview.</summary>
    public enum DeployCellHighlight
    {
        None,
        Valid,
        Invalid,
        Merge
    }

    /// <summary>Presentation-only drag cues. WO-E2 owns the actual audio assets.</summary>
    public enum DeployDragCue
    {
        Pickup,
        Snap,
        Placed,
        Rejected
    }

    public enum DeployDragOutcome
    {
        None,
        Placed,
        Merged,
        Unlocked,
        Rejected
    }

    /// <summary>
    /// Full-screen deployment projection. It asks CardEconomy/DeploymentGrid for every decision and
    /// only translates their results into shapes, colors, labels and commands. WO-C4 added the
    /// footprint-shaped hand, the 1:1 drag ghost and the three-state landing preview; none of it
    /// decides legality on its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeployScreen : MonoBehaviour
    {
        private const string PreviewDeploymentId = "__deploy_preview__";
        private const float ReturnDurationSeconds = 0.18f;
        private const float BounceDurationSeconds = 0.20f;
        private const float BounceScale = 1.14f;
        private const float MergePulseHz = 2.4f;

        private static readonly Color PageColor = new Color(0.055f, 0.07f, 0.105f, 0.98f);
        private static readonly Color PanelColor = new Color(0.09f, 0.12f, 0.17f, 0.96f);
        private static readonly Color UnlockedCellColor = new Color(0.18f, 0.23f, 0.29f, 1f);
        private static readonly Color LockedCellColor = new Color(0.055f, 0.065f, 0.08f, 1f);
        private static readonly Color OccupiedCellColor = new Color(0.17f, 0.39f, 0.58f, 1f);
        private static readonly Color ValidPreviewColor = new Color(0.12f, 0.72f, 0.36f, 1f);
        private static readonly Color InvalidPreviewColor = new Color(0.86f, 0.16f, 0.18f, 1f);
        private static readonly Color MergePreviewColor = new Color(0.97f, 0.78f, 0.18f, 1f);

        private readonly Dictionary<GridCoordinate, CellVisual> cells =
            new Dictionary<GridCoordinate, CellVisual>();
        private readonly List<HandCard> handCards = new List<HandCard>();
        private readonly List<GridCoordinate> mergePulseCells = new List<GridCoordinate>();

        private GameConfig config;
        private CardEconomy economy;
        private IBattleArtSource artSource;
        private Action battleRequested;
        private Func<bool> rewardedRefresh;
        private Func<bool> rewardedUnlock;
        private Action<DeploymentActionKind> deploymentCommitted;
        private Action<DeployDragCue> dragCue;
        private Font font;
        private RectTransform handRoot;
        private RectTransform gridRoot;
        private RectTransform dragLayer;
        private Text coinText;
        private Text refreshText;
        private Text feedbackText;
        private Text stageText;
        private Image commanderImage;
        private bool initialized;

        private DeployGridMetrics metrics;
        private DeployGridMetrics handMetrics;
        private DeployUiRulesDef uiRules;
        private DragSession drag;
        private Coroutine returnRoutine;

        public CardEconomy Economy => economy;

        public string FeedbackMessage => feedbackText == null ? string.Empty : feedbackText.text;

        public int RenderedCellCount => cells.Count;

        public int RenderedHandCount => handCards.Count;

        /// <summary>Edge of one grid cell. Hand cards and the drag ghost are measured against it.</summary>
        public float CellEdge => metrics.CellEdge;

        public bool IsDragging => drag != null;

        /// <summary>Bounding-box size of the live drag ghost, in canvas units.</summary>
        public Vector2 DragGhostSize => drag?.Ghost == null ? Vector2.zero : drag.Ghost.Size;

        public bool DragSnapped => drag != null && drag.Snapped;

        public GridCoordinate DragAnchor => drag?.Anchor ?? default;

        public DeployDragOutcome LastDragOutcome { get; private set; }

        /// <summary>True while a rejected card is animating back into the hand.</summary>
        public bool IsReturningToHand => returnRoutine != null;

        public int LockedCellCount
        {
            get
            {
                int count = 0;
                foreach (CellVisual cell in cells.Values)
                {
                    if (!cell.IsUnlocked)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public void Initialize(
            GameConfig gameConfig,
            CardEconomy cardEconomy,
            IBattleArtSource battleArtSource,
            Action onBattleRequested,
            Func<bool> onRewardedRefresh = null,
            Action<DeploymentActionKind> onDeploymentCommitted = null,
            Func<bool> onRewardedUnlock = null,
            Action<DeployDragCue> onDragCue = null)
        {
            if (initialized)
            {
                throw new InvalidOperationException("DeployScreen has already been initialized.");
            }

            config = gameConfig ?? throw new ArgumentNullException(nameof(gameConfig));
            economy = cardEconomy ?? throw new ArgumentNullException(nameof(cardEconomy));
            artSource = battleArtSource ?? throw new ArgumentNullException(nameof(battleArtSource));
            battleRequested = onBattleRequested;
            rewardedRefresh = onRewardedRefresh;
            rewardedUnlock = onRewardedUnlock;
            deploymentCommitted = onDeploymentCommitted;
            dragCue = onDragCue;
            uiRules = config.Economy.DeployUi
                ?? throw new InvalidOperationException("economy.deployUi is required by the deploy screen.");
            font = RuntimeUiFactory.LoadFont();

            BuildHierarchy();
            Subscribe();
            if (economy.CanDrawFreeOffer)
            {
                economy.DrawOffer();
            }
            initialized = true;
            RefreshAll();
        }

        // ------------------------------------------------------------------ hand shape queries

        /// <summary>Bounding-box size of a hand card. Always footprint aspect on the shared metric.</summary>
        public Vector2 HandCardSize(CardOfferItem card)
        {
            HandCard entry = FindHandCard(card);
            return entry?.Visual == null ? Vector2.zero : entry.Visual.Size;
        }

        /// <summary>The cells a hand card actually draws — notched shapes report only their 3 cells.</summary>
        public IReadOnlyList<GridCoordinate> HandCardCellOffsets(CardOfferItem card)
        {
            HandCard entry = FindHandCard(card);
            return entry?.Visual == null
                ? Array.Empty<GridCoordinate>()
                : entry.Visual.CellOffsets;
        }

        /// <summary>Footprint of a hand card, resolved through the same data path the grid uses.</summary>
        public UnitFootprint HandCardFootprint(CardOfferItem card)
        {
            return ResolveFootprint(card);
        }

        public DeployCellHighlight GetHighlight(GridCoordinate coordinate)
        {
            return cells.TryGetValue(coordinate, out CellVisual cell) ? cell.Highlight : DeployCellHighlight.None;
        }

        // ------------------------------------------------------------------------ drag lifecycle

        /// <summary>
        /// Starts dragging a hand card. The ghost is built at the grid's own cell edge, so what the
        /// player is holding is exactly the size of the space it will occupy — the whole point of
        /// WO-C4's second half.
        /// </summary>
        public bool BeginCardDrag(CardOfferItem card, Vector2 screenPoint)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (card.Category != CardCategory.Unit && card.Category != CardCategory.Unlock)
            {
                return false;
            }

            HandCard source = FindHandCard(card);
            UnitFootprint footprint = ResolveFootprint(card);
            if (footprint == null)
            {
                return false;
            }

            CancelDrag();
            drag = new DragSession
            {
                Card = card,
                Footprint = footprint,
                SourceHandCard = source,
                Ghost = DeployCardVisual.Create(
                    "Drag Ghost", dragLayer, footprint, metrics, CardColor(card.Category))
            };
            drag.Ghost.SetRaycastTarget(false);
            drag.Ghost.AddLabel(font, CardLabel(card), metrics.CellEdge);
            if (source?.Root != null)
            {
                source.Root.gameObject.SetActive(false);
            }

            LastDragOutcome = DeployDragOutcome.None;
            dragCue?.Invoke(DeployDragCue.Pickup);
            DragTo(screenPoint);
            return true;
        }

        /// <summary>Starts dragging a unit already on the grid, using its own footprint.</summary>
        public bool BeginPlacementDrag(string deploymentId, Vector2 screenPoint)
        {
            if (!economy.Grid.TryGetPlacement(deploymentId, out DeploymentPlacement placement))
            {
                SetFeedback("部署单位已不存在");
                return false;
            }

            CancelDrag();
            drag = new DragSession
            {
                DeploymentId = deploymentId,
                Footprint = placement.Footprint,
                Ghost = DeployCardVisual.Create(
                    "Drag Ghost", dragLayer, placement.Footprint, metrics, OccupiedCellColor)
            };
            drag.Ghost.SetRaycastTarget(false);
            drag.Ghost.AddLabel(font, config.GetUnit(placement.UnitId).Name, metrics.CellEdge);

            LastDragOutcome = DeployDragOutcome.None;
            dragCue?.Invoke(DeployDragCue.Pickup);
            DragTo(screenPoint);
            return true;
        }

        /// <summary>
        /// Moves the ghost, resolves the anchor under it and repaints the landing preview. Snapping
        /// only engages on anchors the grid itself accepts, so the ghost never lies about legality.
        /// </summary>
        public void DragTo(Vector2 screenPoint)
        {
            if (drag == null)
            {
                return;
            }

            Vector2 local = ScreenToLocal(dragLayer, screenPoint);
            Vector2 lifted = local + new Vector2(0f, uiRules.DragLiftCells * metrics.CellEdge);

            bool hadAnchor = TryResolveAnchor(lifted, drag.Footprint, out GridCoordinate anchor, out float distance);
            DeployCellHighlight highlight = DeployCellHighlight.Invalid;
            string message = "拖出了部署区";
            bool legal = false;

            if (hadAnchor)
            {
                highlight = EvaluateHighlight(anchor, out message, out legal);
            }

            bool snap = hadAnchor && legal && distance <= uiRules.SnapRadiusCells;
            if (snap && !drag.Snapped)
            {
                dragCue?.Invoke(DeployDragCue.Snap);
            }
            drag.Snapped = snap;
            drag.Anchor = anchor;
            drag.HasAnchor = hadAnchor;
            drag.Ghost.Rect.anchoredPosition = snap
                ? FieldToDragLayer(GhostCentreForAnchor(anchor, drag.Footprint))
                : lifted;

            RefreshCells();
            if (hadAnchor)
            {
                PaintFootprint(drag.Footprint, anchor, highlight);
            }
            SetFeedback(message);
        }

        /// <summary>
        /// Releases the drag. A legal anchor commits through CardEconomy/DeploymentGrid; anything
        /// else animates the card back into the hand rather than vanishing.
        /// </summary>
        public DeployDragOutcome EndDrag()
        {
            if (drag == null)
            {
                return DeployDragOutcome.None;
            }

            DragSession session = drag;
            DeployDragOutcome outcome = DeployDragOutcome.Rejected;
            if (session.HasAnchor)
            {
                outcome = Commit(session, session.Anchor);
            }
            else
            {
                SetFeedback("拖出了部署区");
            }

            LastDragOutcome = outcome;
            drag = null;

            if (outcome == DeployDragOutcome.Rejected)
            {
                dragCue?.Invoke(DeployDragCue.Rejected);
                returnRoutine = StartCoroutine(ReturnGhostToHand(session));
            }
            else
            {
                dragCue?.Invoke(DeployDragCue.Placed);
                DestroyGhost(session);
                RestoreHandCard(session);
                StartCoroutine(BounceCells(session.Footprint, session.Anchor));
            }

            return outcome;
        }

        public void CancelDrag()
        {
            if (drag == null)
            {
                return;
            }
            DragSession session = drag;
            drag = null;
            DestroyGhost(session);
            RestoreHandCard(session);
            RefreshCells();
        }

        /// <summary>
        /// Nearest anchor for a footprint whose bounding box is centred at a drag-layer point.
        /// Pure geometry: it answers "which cell is the cursor over", never "is that legal".
        /// </summary>
        public bool TryResolveAnchor(
            Vector2 dragLayerPoint,
            UnitFootprint footprint,
            out GridCoordinate anchor,
            out float distanceInCells)
        {
            anchor = default;
            distanceInCells = float.MaxValue;
            if (footprint == null || gridRoot == null)
            {
                return false;
            }

            Vector2 fieldPoint = DragLayerToField(dragLayerPoint);
            Vector2 cornerPoint = fieldPoint - new Vector2(
                (footprint.Width - 1) * 0.5f * metrics.Step,
                (footprint.Height - 1) * 0.5f * metrics.Step);
            GridCoordinate cornerCell = metrics.NearestCell(
                cornerPoint, economy.Grid.Width, economy.Grid.Height, out distanceInCells);
            anchor = new GridCoordinate(
                cornerCell.Column - footprint.MinColumnOffset,
                cornerCell.Row - footprint.MinRowOffset);
            return cells.ContainsKey(cornerCell);
        }

        // --------------------------------------------------------------- legacy preview/commit API

        public DeploymentEvaluation PreviewCard(CardOfferItem card, GridCoordinate anchor)
        {
            DeploymentUnit preview = CreatePreviewUnit(card);
            DeploymentEvaluation evaluation = economy.Grid.Evaluate(preview, anchor);
            RefreshCells();
            PaintFootprint(preview.Footprint, anchor, HighlightFor(evaluation));
            SetFeedback(DescribeEvaluation(evaluation));
            return evaluation;
        }

        /// <summary>Previews an unlock card's covered cells; the grid alone decides legality.</summary>
        public UnlockEvaluation PreviewUnlockCard(CardOfferItem card, GridCoordinate anchor)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (card.Category != CardCategory.Unlock || card.Unlock == null)
            {
                throw new ArgumentException("Only an unlock card has an unlock preview.", nameof(card));
            }

            UnlockEvaluation evaluation = economy.Grid.EvaluateUnlock(card.Unlock, anchor);
            RefreshCells();
            PaintFootprint(
                card.Unlock.Footprint,
                anchor,
                evaluation.IsValid ? DeployCellHighlight.Valid : DeployCellHighlight.Invalid);
            SetFeedback(DescribeUnlock(evaluation));
            return evaluation;
        }

        public bool CommitUnlockCard(CardOfferItem card, GridCoordinate anchor)
        {
            bool unlocked = economy.TryPlayUnlockCard(card, anchor, out UnlockApplyResult result);
            if (unlocked)
            {
                SetFeedback($"解锁 {result.NewlyUnlockedCells.Count} 格");
                RefreshAll();
                return true;
            }

            SetFeedback(DescribeUnlock(economy.Grid.EvaluateUnlock(card.Unlock, anchor)));
            RefreshCells();
            PaintFootprint(card.Unlock.Footprint, anchor, DeployCellHighlight.Invalid);
            return false;
        }

        public bool CommitCard(CardOfferItem card, GridCoordinate anchor)
        {
            DeploymentUnit preview = CreatePreviewUnit(card);
            DeploymentEvaluation evaluation = economy.Grid.Evaluate(preview, anchor);
            if (!evaluation.IsValid)
            {
                SetFeedback(DescribeEvaluation(evaluation));
                RefreshCells();
                PaintFootprint(preview.Footprint, anchor, DeployCellHighlight.Invalid);
                return false;
            }

            try
            {
                DeploymentApplyResult result = economy.PlaceUnit(card, anchor);
                SetFeedback(result.Action == DeploymentActionKind.Merge ? "合成成功" : "部署成功");
                deploymentCommitted?.Invoke(result.Action);
                RefreshAll();
                return true;
            }
            catch (InvalidOperationException exception)
            {
                SetFeedback(exception.Message);
                RefreshCells();
                PaintFootprint(preview.Footprint, anchor, DeployCellHighlight.Invalid);
                return false;
            }
            catch (ArgumentException exception)
            {
                SetFeedback(exception.Message);
                RefreshCells();
                PaintFootprint(preview.Footprint, anchor, DeployCellHighlight.Invalid);
                return false;
            }
        }

        public bool PreviewPlacement(string deploymentId, GridCoordinate anchor)
        {
            if (!economy.Grid.TryGetPlacement(deploymentId, out DeploymentPlacement placement))
            {
                SetFeedback("部署单位已不存在");
                return false;
            }

            var preview = new DeploymentUnit(
                PreviewDeploymentId, placement.UnitId, placement.Level, placement.Footprint);
            DeploymentEvaluation evaluation = economy.Grid.Evaluate(preview, anchor);
            RefreshCells();
            PaintFootprint(preview.Footprint, anchor, HighlightFor(evaluation));
            SetFeedback(evaluation.IsValid
                ? evaluation.Action == DeploymentActionKind.Merge ? "松开以合成" : "松开以移动"
                : DescribeEvaluation(evaluation));
            return evaluation.IsValid;
        }

        public bool CommitPlacement(string deploymentId, GridCoordinate anchor)
        {
            try
            {
                DeploymentApplyResult result = economy.Grid.Move(deploymentId, anchor);
                economy.SnapshotToRunState();
                SetFeedback(result.Action == DeploymentActionKind.Merge ? "合成成功" : "移动成功");
                deploymentCommitted?.Invoke(result.Action);
                RefreshAll();
                return true;
            }
            catch (InvalidOperationException exception)
            {
                SetFeedback(exception.Message);
                RefreshCells();
                PaintSingleCell(anchor, InvalidPreviewColor);
                return false;
            }
        }

        public void CancelPreview()
        {
            RefreshCells();
        }

        public bool ActivateCard(CardOfferItem card)
        {
            try
            {
                if (card.Category == CardCategory.Unlock)
                {
                    SetFeedback("拖动解锁卡到网格边缘");
                    return false;
                }

                if (card.Category == CardCategory.Buff || card.Category == CardCategory.Global)
                {
                    economy.AcquireEffect(card);
                    SetFeedback("效果已加入本局");
                    RefreshAll();
                    return true;
                }

                SetFeedback("拖动兵器卡到网格");
                return false;
            }
            catch (InvalidOperationException exception)
            {
                SetFeedback(exception.Message);
                return false;
            }
        }

        public bool RefreshOffer()
        {
            bool refreshed = economy.TryRefresh(out _);
            SetFeedback(refreshed ? "手牌已刷新" : "刷新失败：由经济系统拒绝");
            RefreshAll();
            return refreshed;
        }

        public bool RewardedRefresh()
        {
            bool refreshed = rewardedRefresh != null && rewardedRefresh();
            SetFeedback(refreshed ? "Mock 广告完成，已刷新" : "Mock 广告刷新暂不可用");
            RefreshAll();
            return refreshed;
        }

        public bool PurchaseUnlockCard()
        {
            bool purchased = economy.TryPurchaseUnlockCard(out CardOfferItem card);
            SetFeedback(purchased
                ? $"购得解锁卡 {UnlockShapeLabel(card.Unlock)}"
                : "金币不足或已全部解锁");
            RefreshAll();
            return purchased;
        }

        public bool RewardedUnlockCard()
        {
            bool granted = rewardedUnlock != null && rewardedUnlock();
            SetFeedback(granted ? "Mock 广告完成，获得解锁卡" : "Mock 广告解锁暂不可用");
            RefreshAll();
            return granted;
        }

        public void RequestBattle()
        {
            SetFeedback("进入战斗");
            battleRequested?.Invoke();
        }

        public void RefreshAll()
        {
            if (economy == null)
            {
                return;
            }

            RefreshHeader();
            RefreshCells();
            RebuildHand();
        }

        // ------------------------------------------------------------------------------- internals

        private DeployDragOutcome Commit(DragSession session, GridCoordinate anchor)
        {
            if (session.Card != null && session.Card.Category == CardCategory.Unlock)
            {
                return CommitUnlockCard(session.Card, anchor)
                    ? DeployDragOutcome.Unlocked
                    : DeployDragOutcome.Rejected;
            }

            if (session.Card != null)
            {
                DeploymentEvaluation evaluation = economy.Grid.Evaluate(CreatePreviewUnit(session.Card), anchor);
                bool merge = evaluation.IsValid && evaluation.Action == DeploymentActionKind.Merge;
                return CommitCard(session.Card, anchor)
                    ? merge ? DeployDragOutcome.Merged : DeployDragOutcome.Placed
                    : DeployDragOutcome.Rejected;
            }

            if (!string.IsNullOrEmpty(session.DeploymentId))
            {
                if (!economy.Grid.TryGetPlacement(session.DeploymentId, out DeploymentPlacement placement))
                {
                    return DeployDragOutcome.Rejected;
                }

                var preview = new DeploymentUnit(
                    PreviewDeploymentId, placement.UnitId, placement.Level, placement.Footprint);
                DeploymentEvaluation evaluation = economy.Grid.Evaluate(preview, anchor);
                if (!evaluation.IsValid)
                {
                    SetFeedback(DescribeEvaluation(evaluation));
                    return DeployDragOutcome.Rejected;
                }

                bool merge = evaluation.Action == DeploymentActionKind.Merge;
                return CommitPlacement(session.DeploymentId, anchor)
                    ? merge ? DeployDragOutcome.Merged : DeployDragOutcome.Placed
                    : DeployDragOutcome.Rejected;
            }

            return DeployDragOutcome.Rejected;
        }

        private DeployCellHighlight EvaluateHighlight(GridCoordinate anchor, out string message, out bool legal)
        {
            if (drag.Card != null && drag.Card.Category == CardCategory.Unlock)
            {
                UnlockEvaluation unlock = economy.Grid.EvaluateUnlock(drag.Card.Unlock, anchor);
                message = DescribeUnlock(unlock);
                legal = unlock.IsValid;
                return legal ? DeployCellHighlight.Valid : DeployCellHighlight.Invalid;
            }

            DeploymentUnit preview;
            if (drag.Card != null)
            {
                preview = CreatePreviewUnit(drag.Card);
            }
            else if (economy.Grid.TryGetPlacement(drag.DeploymentId, out DeploymentPlacement placement))
            {
                preview = new DeploymentUnit(
                    PreviewDeploymentId, placement.UnitId, placement.Level, placement.Footprint);
            }
            else
            {
                message = "部署单位已不存在";
                legal = false;
                return DeployCellHighlight.Invalid;
            }

            DeploymentEvaluation evaluation = economy.Grid.Evaluate(preview, anchor);
            message = DescribeEvaluation(evaluation);
            legal = evaluation.IsValid;
            return HighlightFor(evaluation);
        }

        private static DeployCellHighlight HighlightFor(DeploymentEvaluation evaluation)
        {
            if (!evaluation.IsValid)
            {
                return DeployCellHighlight.Invalid;
            }
            return evaluation.Action == DeploymentActionKind.Merge
                ? DeployCellHighlight.Merge
                : DeployCellHighlight.Valid;
        }

        /// <summary>
        /// Turns a grid verdict into a player-facing reason. Silent failures were the actual
        /// complaint behind WO-C4: "该格尚未解锁" is the high-frequency case on a 7x7 mask.
        /// </summary>
        private static string DescribeEvaluation(DeploymentEvaluation evaluation)
        {
            if (evaluation.IsValid)
            {
                return evaluation.Action == DeploymentActionKind.Merge ? "可合成" : "可落位";
            }

            switch (evaluation.Failure)
            {
                case DeploymentFailureReason.OutOfBounds:
                    return "放不下：超出 7×7 场地";
                case DeploymentFailureReason.CellLocked:
                    return "放不下：该格尚未解锁";
                case DeploymentFailureReason.FootprintLocked:
                    return "放不下：已解锁区域容不下这个形状";
                case DeploymentFailureReason.Occupied:
                    return "放不下：格子已被其他单位占用";
                case DeploymentFailureReason.UnitMismatch:
                    return "不能合成：兵种不同";
                case DeploymentFailureReason.LevelMismatch:
                    return "不能合成：等级不同";
                case DeploymentFailureReason.FootprintMismatch:
                    return "不能合成：占格不同";
                case DeploymentFailureReason.MaximumLevelReached:
                    return "不能合成：已是最高等级";
                case DeploymentFailureReason.SameDeployment:
                    return "不能放在自己身上";
                default:
                    return string.IsNullOrWhiteSpace(evaluation.Message) ? "放不下" : evaluation.Message;
            }
        }

        private static string DescribeUnlock(UnlockEvaluation evaluation)
        {
            if (evaluation.IsValid)
            {
                return $"可解锁 {evaluation.NewlyUnlockedCells.Count} 格";
            }

            switch (evaluation.Failure)
            {
                case UnlockFailureReason.OutOfBounds:
                    return "解锁失败：超出 7×7 场地";
                case UnlockFailureReason.NoNewCells:
                    return "解锁失败：覆盖的格子已全部解锁";
                case UnlockFailureReason.NotAdjacent:
                    return "解锁失败：必须贴着已解锁区域";
                default:
                    return string.IsNullOrWhiteSpace(evaluation.Message) ? "解锁失败" : evaluation.Message;
            }
        }

        private UnitFootprint ResolveFootprint(CardOfferItem card)
        {
            if (card == null)
            {
                return null;
            }
            if (card.Category == CardCategory.Unlock)
            {
                return card.Unlock?.Footprint;
            }
            if (card.Category != CardCategory.Unit)
            {
                return null;
            }
            return UnitFootprint.FromDefinition(config.GetUnit(card.ContentId));
        }

        private HandCard FindHandCard(CardOfferItem card)
        {
            for (int index = 0; index < handCards.Count; index++)
            {
                if (ReferenceEquals(handCards[index].Card, card))
                {
                    return handCards[index];
                }
            }
            return null;
        }

        private Vector2 GhostCentreForAnchor(GridCoordinate anchor, UnitFootprint footprint)
        {
            GridCoordinate cornerCell = new GridCoordinate(
                anchor.Column + footprint.MinColumnOffset,
                anchor.Row + footprint.MinRowOffset);
            Vector2 corner = metrics.CellCentreInField(cornerCell, economy.Grid.Width, economy.Grid.Height);
            return corner + new Vector2(
                (footprint.Width - 1) * 0.5f * metrics.Step,
                (footprint.Height - 1) * 0.5f * metrics.Step);
        }

        private Vector2 DragLayerToField(Vector2 dragLayerPoint)
        {
            Vector3 world = dragLayer.TransformPoint(dragLayerPoint);
            return gridRoot.InverseTransformPoint(world);
        }

        private Vector2 FieldToDragLayer(Vector2 fieldPoint)
        {
            Vector3 world = gridRoot.TransformPoint(fieldPoint);
            return dragLayer.InverseTransformPoint(world);
        }

        private static Vector2 ScreenToLocal(RectTransform target, Vector2 screenPoint)
        {
            Canvas canvas = target.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                target, screenPoint, camera, out Vector2 local);
            return local;
        }

        /// <summary>Screen position of a grid cell centre; used by tests and by snap verification.</summary>
        public Vector2 CellScreenPoint(GridCoordinate cell)
        {
            Vector2 field = metrics.CellCentreInField(cell, economy.Grid.Width, economy.Grid.Height);
            Vector3 world = gridRoot.TransformPoint(field);
            Canvas canvas = gridRoot.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.WorldToScreenPoint(camera, world);
        }

        /// <summary>
        /// Screen point whose lifted ghost centres a footprint on <paramref name="anchor"/>. Tests
        /// use it to aim a drag the same way a player would.
        /// </summary>
        public Vector2 DragScreenPointForAnchor(GridCoordinate anchor, UnitFootprint footprint)
        {
            Vector2 ghostCentre = GhostCentreForAnchor(anchor, footprint);
            Vector2 layerPoint = FieldToDragLayer(ghostCentre);
            Vector2 cursorLayerPoint = layerPoint - new Vector2(0f, uiRules.DragLiftCells * metrics.CellEdge);
            Vector3 world = dragLayer.TransformPoint(cursorLayerPoint);
            Canvas canvas = dragLayer.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.WorldToScreenPoint(camera, world);
        }

        private void DestroyGhost(DragSession session)
        {
            if (session.Ghost != null)
            {
                Destroy(session.Ghost.gameObject);
                session.Ghost = null;
            }
        }

        private void RestoreHandCard(DragSession session)
        {
            if (session.SourceHandCard?.Root != null)
            {
                session.SourceHandCard.Root.gameObject.SetActive(true);
            }
        }

        private IEnumerator ReturnGhostToHand(DragSession session)
        {
            if (session.Ghost == null)
            {
                returnRoutine = null;
                RestoreHandCard(session);
                yield break;
            }

            RectTransform ghostRect = session.Ghost.Rect;
            Vector2 from = ghostRect.anchoredPosition;
            Vector2 to = from;
            if (session.SourceHandCard?.Root != null)
            {
                Vector3 world = session.SourceHandCard.Root.TransformPoint(Vector3.zero);
                to = dragLayer.InverseTransformPoint(world);
            }

            float elapsed = 0f;
            while (elapsed < ReturnDurationSeconds && ghostRect != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / ReturnDurationSeconds);
                float eased = 1f - ((1f - t) * (1f - t));
                ghostRect.anchoredPosition = Vector2.Lerp(from, to, eased);
                ghostRect.localScale = Vector3.one * Mathf.Lerp(1f, uiRules.HandCardScale, eased);
                yield return null;
            }

            DestroyGhost(session);
            RestoreHandCard(session);
            RefreshCells();
            returnRoutine = null;
        }

        private IEnumerator BounceCells(UnitFootprint footprint, GridCoordinate anchor)
        {
            var targets = new List<RectTransform>(footprint.OccupiedOffsets.Count);
            for (int index = 0; index < footprint.OccupiedOffsets.Count; index++)
            {
                GridCoordinate coordinate = anchor + footprint.OccupiedOffsets[index];
                if (cells.TryGetValue(coordinate, out CellVisual cell) && cell.Image != null)
                {
                    targets.Add(cell.Image.rectTransform);
                }
            }

            float elapsed = 0f;
            while (elapsed < BounceDurationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / BounceDurationSeconds);
                float scale = 1f + ((BounceScale - 1f) * Mathf.Sin(t * Mathf.PI));
                for (int index = 0; index < targets.Count; index++)
                {
                    if (targets[index] != null)
                    {
                        targets[index].localScale = Vector3.one * scale;
                    }
                }
                yield return null;
            }

            for (int index = 0; index < targets.Count; index++)
            {
                if (targets[index] != null)
                {
                    targets[index].localScale = Vector3.one;
                }
            }
        }

        private void Update()
        {
            if (mergePulseCells.Count == 0)
            {
                return;
            }

            float pulse = 0.72f + (0.28f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI * MergePulseHz)));
            for (int index = 0; index < mergePulseCells.Count; index++)
            {
                if (cells.TryGetValue(mergePulseCells[index], out CellVisual cell) && cell.Image != null)
                {
                    cell.Image.color = new Color(
                        MergePreviewColor.r * pulse,
                        MergePreviewColor.g * pulse,
                        MergePreviewColor.b * pulse,
                        1f);
                }
            }
        }

        private void BuildHierarchy()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
            {
                root = gameObject.AddComponent<RectTransform>();
            }
            RuntimeUiFactory.Stretch(root);
            Image page = GetComponent<Image>();
            if (page == null)
            {
                page = gameObject.AddComponent<Image>();
            }
            page.color = PageColor;
            page.raycastTarget = true;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                throw new InvalidOperationException("DeployScreen requires a parent Canvas.");
            }
            RuntimeUiFactory.EnsureEventSystem(canvas.transform);

            BuildHeader(root);
            BuildGrid(root);
            BuildHand(root);
            BuildFooter(root);
            BuildDragLayer(root);
        }

        private void BuildHeader(RectTransform root)
        {
            RectTransform header = RuntimeUiFactory.CreatePanel("Deploy Header", root, PanelColor);
            RuntimeUiFactory.SetAnchors(header, new Vector2(0.035f, 0.865f), new Vector2(0.965f, 0.98f));

            commanderImage = RuntimeUiFactory.CreateImage("Commander Portrait", header, new Color(0.24f, 0.42f, 0.66f));
            RuntimeUiFactory.SetAnchors(commanderImage.rectTransform, new Vector2(0.02f, 0.12f), new Vector2(0.13f, 0.88f));
            commanderImage.preserveAspect = true;
            commanderImage.sprite = artSource.Find("commander/cmd_bei/avatar");

            stageText = RuntimeUiFactory.CreateText("Stage Progress", header, font, 34, FontStyle.Bold, TextAnchor.MiddleLeft);
            RuntimeUiFactory.SetAnchors(stageText.rectTransform, new Vector2(0.16f, 0.52f), new Vector2(0.50f, 0.92f));
            coinText = RuntimeUiFactory.CreateText("Coins", header, font, 32, FontStyle.Bold, TextAnchor.MiddleLeft);
            RuntimeUiFactory.SetAnchors(coinText.rectTransform, new Vector2(0.52f, 0.52f), new Vector2(0.74f, 0.92f));
            Text commander = RuntimeUiFactory.CreateText("Commander", header, font, 24, FontStyle.Normal, TextAnchor.MiddleLeft);
            commander.text = "指挥官 · 刘备";
            RuntimeUiFactory.SetAnchors(commander.rectTransform, new Vector2(0.16f, 0.08f), new Vector2(0.55f, 0.5f));

            Button battle = RuntimeUiFactory.CreateButton(
                "Battle Button", header, new Color(0.78f, 0.22f, 0.12f), font, "战 斗", 34);
            RuntimeUiFactory.SetAnchors(battle.GetComponent<RectTransform>(), new Vector2(0.77f, 0.14f), new Vector2(0.98f, 0.86f));
            battle.onClick.AddListener(RequestBattle);
        }

        private void BuildGrid(RectTransform root)
        {
            RectTransform panel = RuntimeUiFactory.CreatePanel("Deployment Grid Panel", root, PanelColor);
            RuntimeUiFactory.SetAnchors(panel, new Vector2(0.035f, 0.365f), new Vector2(0.965f, 0.835f));
            Text title = RuntimeUiFactory.CreateText("Grid Title", panel, font, 30, FontStyle.Bold, TextAnchor.MiddleLeft);
            title.text = "部署阵地 · 拖动兵器卡落位";
            RuntimeUiFactory.SetAnchors(title.rectTransform, new Vector2(0.025f, 0.9f), new Vector2(0.975f, 0.985f));

            var fieldObject = new GameObject("Deployment Grid", typeof(RectTransform));
            gridRoot = fieldObject.GetComponent<RectTransform>();
            gridRoot.SetParent(panel, false);
            RuntimeUiFactory.SetAnchors(gridRoot, new Vector2(0.025f, 0.05f), new Vector2(0.975f, 0.88f));
            gridRoot.ForceUpdateRectTransforms();

            Vector2 available = gridRoot.rect.size;
            if (available.x <= 1f || available.y <= 1f)
            {
                // The canvas has not laid out yet in some test hosts; fall back to the design size.
                available = new Vector2(954f, 749f);
            }
            metrics = DeployGridMetrics.Fit(
                available, economy.Grid.Width, economy.Grid.Height, uiRules.CellSpacingRatio);
            handMetrics = metrics.Scaled(uiRules.HandCardScale);

            for (int row = 0; row < economy.Grid.Height; row++)
            {
                for (int column = 0; column < economy.Grid.Width; column++)
                {
                    var coordinate = new GridCoordinate(column, row);
                    Image image = RuntimeUiFactory.CreateImage($"Cell {column},{row}", gridRoot, UnlockedCellColor);
                    image.raycastTarget = true;
                    RectTransform cellRect = image.rectTransform;
                    cellRect.anchorMin = new Vector2(0.5f, 0.5f);
                    cellRect.anchorMax = new Vector2(0.5f, 0.5f);
                    cellRect.pivot = new Vector2(0.5f, 0.5f);
                    cellRect.sizeDelta = new Vector2(metrics.CellEdge, metrics.CellEdge);
                    cellRect.anchoredPosition = metrics.CellCentreInField(
                        coordinate, economy.Grid.Width, economy.Grid.Height);

                    Text label = RuntimeUiFactory.CreateText(
                        "Cell Label", image.transform, font,
                        Mathf.Max(10, Mathf.RoundToInt(metrics.CellEdge * 0.22f)),
                        FontStyle.Bold, TextAnchor.MiddleCenter);
                    RuntimeUiFactory.Stretch(label.rectTransform);

                    DeployDropCell drop = image.gameObject.AddComponent<DeployDropCell>();
                    drop.Initialize(this, coordinate);
                    cells.Add(coordinate, new CellVisual(image, label));
                }
            }
        }

        private void BuildHand(RectTransform root)
        {
            RectTransform panel = RuntimeUiFactory.CreatePanel("Hand Panel", root, PanelColor);
            RuntimeUiFactory.SetAnchors(panel, new Vector2(0.035f, 0.13f), new Vector2(0.965f, 0.335f));
            Text title = RuntimeUiFactory.CreateText("Hand Title", panel, font, 28, FontStyle.Bold, TextAnchor.MiddleLeft);
            title.text = "手 牌";
            RuntimeUiFactory.SetAnchors(title.rectTransform, new Vector2(0.025f, 0.82f), new Vector2(0.3f, 0.98f));

            var handObject = new GameObject("Cards", typeof(RectTransform));
            handRoot = handObject.GetComponent<RectTransform>();
            handRoot.SetParent(panel, false);
            RuntimeUiFactory.SetAnchors(handRoot, new Vector2(0.025f, 0.06f), new Vector2(0.975f, 0.79f));
        }

        private void BuildFooter(RectTransform root)
        {
            RectTransform panel = RuntimeUiFactory.CreatePanel("Deploy Footer", root, PanelColor);
            RuntimeUiFactory.SetAnchors(panel, new Vector2(0.035f, 0.02f), new Vector2(0.965f, 0.105f));

            Button refresh = RuntimeUiFactory.CreateButton(
                "Refresh", panel, new Color(0.18f, 0.42f, 0.65f), font, "刷新");
            RuntimeUiFactory.SetAnchors(refresh.GetComponent<RectTransform>(), new Vector2(0.015f, 0.14f), new Vector2(0.22f, 0.86f));
            refreshText = refresh.GetComponentInChildren<Text>();
            refresh.onClick.AddListener(() => RefreshOffer());

            Button adRefresh = RuntimeUiFactory.CreateButton(
                "Rewarded Refresh", panel, new Color(0.47f, 0.29f, 0.62f), font, "广告刷新 · Mock", 22);
            RuntimeUiFactory.SetAnchors(adRefresh.GetComponent<RectTransform>(), new Vector2(0.235f, 0.14f), new Vector2(0.48f, 0.86f));
            adRefresh.onClick.AddListener(() => RewardedRefresh());

            Button buyUnlock = RuntimeUiFactory.CreateButton(
                "Unlock Purchase", panel, new Color(0.56f, 0.39f, 0.13f), font, "购买解锁卡", 22);
            RuntimeUiFactory.SetAnchors(buyUnlock.GetComponent<RectTransform>(), new Vector2(0.495f, 0.14f), new Vector2(0.615f, 0.86f));
            buyUnlock.onClick.AddListener(() => PurchaseUnlockCard());

            Button adUnlock = RuntimeUiFactory.CreateButton(
                "Rewarded Unlock", panel, new Color(0.38f, 0.32f, 0.6f), font, "广告解锁卡", 22);
            RuntimeUiFactory.SetAnchors(adUnlock.GetComponent<RectTransform>(), new Vector2(0.625f, 0.14f), new Vector2(0.745f, 0.86f));
            adUnlock.onClick.AddListener(() => RewardedUnlockCard());

            feedbackText = RuntimeUiFactory.CreateText("Feedback", panel, font, 21, FontStyle.Bold, TextAnchor.MiddleLeft);
            RuntimeUiFactory.SetAnchors(feedbackText.rectTransform, new Vector2(0.755f, 0.08f), new Vector2(0.985f, 0.92f));
            feedbackText.text = "准备部署";
        }

        /// <summary>Topmost layer the drag ghost lives on, so it is never occluded by panels.</summary>
        private void BuildDragLayer(RectTransform root)
        {
            var layerObject = new GameObject("Drag Layer", typeof(RectTransform));
            dragLayer = layerObject.GetComponent<RectTransform>();
            dragLayer.SetParent(root, false);
            RuntimeUiFactory.Stretch(dragLayer);
            dragLayer.SetAsLastSibling();
        }

        private void Subscribe()
        {
            economy.CoinsChanged += HandleCoinsChanged;
            economy.HandChanged += HandleHandChanged;
            economy.DeploymentsChanged += RefreshCells;
        }

        private void OnDestroy()
        {
            if (economy == null)
            {
                return;
            }
            economy.CoinsChanged -= HandleCoinsChanged;
            economy.HandChanged -= HandleHandChanged;
            economy.DeploymentsChanged -= RefreshCells;
        }

        private void HandleCoinsChanged(int value)
        {
            RefreshHeader();
        }

        private void HandleHandChanged(CardOffer value)
        {
            RebuildHand();
        }

        private void RefreshHeader()
        {
            if (coinText == null)
            {
                return;
            }
            coinText.text = $"金币  {economy.State.Coins}";
            stageText.text = $"小关 {economy.State.StageIndex}/{config.Levels.Count}";
            refreshText.text = $"刷新  {economy.NextRefreshCost}";
        }

        private void RefreshCells()
        {
            mergePulseCells.Clear();
            foreach (KeyValuePair<GridCoordinate, CellVisual> pair in cells)
            {
                GridCoordinate coordinate = pair.Key;
                CellVisual cell = pair.Value;
                cell.Highlight = DeployCellHighlight.None;
                cell.IsUnlocked = economy.Grid.IsUnlocked(coordinate);
                if (!cell.IsUnlocked)
                {
                    cell.Image.color = LockedCellColor;
                    cell.Label.text = "锁";
                    cell.DragHandle?.Clear();
                    continue;
                }

                if (economy.Grid.TryGetPlacementAt(coordinate, out DeploymentPlacement placement))
                {
                    cell.Image.color = OccupiedCellColor;
                    cell.Label.text = $"{config.GetUnit(placement.UnitId).Name}\n{TierLabel(placement.Level)}";
                    if (cell.DragHandle == null)
                    {
                        cell.DragHandle = cell.Image.gameObject.AddComponent<DeployPlacementDragHandle>();
                    }
                    cell.DragHandle.Initialize(this, placement.DeploymentId);
                }
                else
                {
                    cell.Image.color = UnlockedCellColor;
                    cell.Label.text = string.Empty;
                    cell.DragHandle?.Clear();
                }
            }
        }

        private void RebuildHand()
        {
            if (handRoot == null)
            {
                return;
            }
            for (int index = 0; index < handCards.Count; index++)
            {
                if (handCards[index].Root != null)
                {
                    Destroy(handCards[index].Root.gameObject);
                }
            }
            handCards.Clear();

            CardOffer offer = economy.CurrentOffer;
            if (offer == null)
            {
                return;
            }

            // Lay cards out side by side on their own true widths so a 3x1 stays long and a 2x2
            // stays square. A uniform layout group is exactly what flattened them before WO-C4.
            var widths = new float[offer.Cards.Count];
            float totalWidth = 0f;
            float gap = handMetrics.Step * 0.5f;
            for (int index = 0; index < offer.Cards.Count; index++)
            {
                UnitFootprint footprint = ResolveFootprint(offer.Cards[index]);
                widths[index] = footprint == null
                    ? handMetrics.CellEdge * 1.6f
                    : handMetrics.FootprintSize(footprint).x;
                totalWidth += widths[index];
            }
            totalWidth += gap * Mathf.Max(0, offer.Cards.Count - 1);

            float cursor = -totalWidth * 0.5f;
            for (int index = 0; index < offer.Cards.Count; index++)
            {
                float centre = cursor + (widths[index] * 0.5f);
                CreateCard(offer.Cards[index], index, centre);
                cursor += widths[index] + gap;
            }
        }

        private void CreateCard(CardOfferItem card, int index, float centreX)
        {
            UnitFootprint footprint = ResolveFootprint(card);
            RectTransform rootRect;
            DeployCardVisual visual = null;

            if (footprint != null)
            {
                visual = DeployCardVisual.Create(
                    $"Card {index} {card.ContentId}", handRoot, footprint, handMetrics, CardColor(card.Category));
                visual.SetRaycastTarget(true);
                visual.AddArt(ResolveCardSprite(card));
                visual.AddLabel(font, CardLabel(card), handMetrics.CellEdge);
                rootRect = visual.Rect;
            }
            else
            {
                // Buff and global cards have no footprint; they keep a neutral 1x1-ish chip.
                Image chip = RuntimeUiFactory.CreateImage(
                    $"Card {index} {card.ContentId}", handRoot, CardColor(card.Category));
                chip.raycastTarget = true;
                rootRect = chip.rectTransform;
                rootRect.anchorMin = new Vector2(0.5f, 0.5f);
                rootRect.anchorMax = new Vector2(0.5f, 0.5f);
                rootRect.pivot = new Vector2(0.5f, 0.5f);
                rootRect.sizeDelta = new Vector2(handMetrics.CellEdge * 1.6f, handMetrics.CellEdge);
                Text label = RuntimeUiFactory.CreateText(
                    "Card Label", chip.transform, font,
                    Mathf.Max(10, Mathf.RoundToInt(handMetrics.CellEdge * 0.26f)),
                    FontStyle.Bold, TextAnchor.MiddleCenter);
                RuntimeUiFactory.Stretch(label.rectTransform);
                label.text = CardLabel(card);
            }

            rootRect.anchoredPosition = new Vector2(centreX, 0f);
            DeployCardDragHandle handle = rootRect.gameObject.AddComponent<DeployCardDragHandle>();
            handle.Initialize(this, card);
            handCards.Add(new HandCard(card, rootRect, visual));
        }

        private DeploymentUnit CreatePreviewUnit(CardOfferItem card)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }
            if (card.Category != CardCategory.Unit)
            {
                throw new ArgumentException("Only a unit card has a deployment preview.", nameof(card));
            }
            UnitDef definition = config.GetUnit(card.ContentId);
            return new DeploymentUnit(
                PreviewDeploymentId,
                definition.Id,
                (int)definition.Tier,
                UnitFootprint.FromDefinition(definition));
        }

        /// <summary>Highlights every cell the footprint covers — notched shapes skip their gap.</summary>
        private void PaintFootprint(UnitFootprint footprint, GridCoordinate anchor, DeployCellHighlight highlight)
        {
            Color color = HighlightColor(highlight);
            bool painted = false;
            for (int index = 0; index < footprint.OccupiedOffsets.Count; index++)
            {
                GridCoordinate coordinate = anchor + footprint.OccupiedOffsets[index];
                if (cells.TryGetValue(coordinate, out CellVisual cell))
                {
                    cell.Image.color = color;
                    cell.Highlight = highlight;
                    if (highlight == DeployCellHighlight.Merge)
                    {
                        mergePulseCells.Add(coordinate);
                    }
                    painted = true;
                }
            }
            if (!painted)
            {
                PaintSingleCell(anchor, color);
            }
        }

        private static Color HighlightColor(DeployCellHighlight highlight)
        {
            switch (highlight)
            {
                case DeployCellHighlight.Valid: return ValidPreviewColor;
                case DeployCellHighlight.Merge: return MergePreviewColor;
                case DeployCellHighlight.Invalid: return InvalidPreviewColor;
                default: return UnlockedCellColor;
            }
        }

        private void PaintSingleCell(GridCoordinate coordinate, Color color)
        {
            if (cells.TryGetValue(coordinate, out CellVisual cell))
            {
                cell.Image.color = color;
            }
        }

        private Sprite ResolveCardSprite(CardOfferItem card)
        {
            if (card.Category == CardCategory.Unit)
            {
                return artSource.Find($"unit/{card.ContentId}/idle");
            }
            if (card.Category == CardCategory.Unlock)
            {
                return artSource.Find("ui/unlock_cells");
            }
            return artSource.Find($"effect/{card.ContentId}/icon");
        }

        private string CardLabel(CardOfferItem card)
        {
            switch (card.Category)
            {
                case CardCategory.Unit:
                {
                    UnitDef unit = config.GetUnit(card.ContentId);
                    return unit.Name;
                }
                case CardCategory.Unlock:
                    return $"解锁\n{UnlockShapeLabel(card.Unlock)}";
                default:
                    return config.GetEffect(card.ContentId).Name;
            }
        }

        private static string UnlockShapeLabel(UnlockCard card)
        {
            switch (card.Shape)
            {
                case UnlockCardShape.OneByOne: return "1×1";
                case UnlockCardShape.TwoByOne: return "2×1";
                case UnlockCardShape.OneByTwo: return "1×2";
                case UnlockCardShape.TwoByTwo: return "2×2";
                case UnlockCardShape.ThreeByOne: return "3×1";
                case UnlockCardShape.OneByThree: return "1×3";
                default: return $"缺{NotchLabel(card.Notch)}";
            }
        }

        private static string NotchLabel(GridCorner corner)
        {
            switch (corner)
            {
                case GridCorner.LowerLeft: return "左下";
                case GridCorner.LowerRight: return "右下";
                case GridCorner.UpperLeft: return "左上";
                default: return "右上";
            }
        }

        private static Color CardColor(CardCategory category)
        {
            switch (category)
            {
                case CardCategory.Unit:
                    return new Color(0.14f, 0.31f, 0.48f, 1f);
                case CardCategory.Unlock:
                    return new Color(0.52f, 0.35f, 0.12f, 1f);
                case CardCategory.Buff:
                    return new Color(0.16f, 0.45f, 0.3f, 1f);
                default:
                    return new Color(0.42f, 0.22f, 0.53f, 1f);
            }
        }

        private static string TierLabel(int level)
        {
            switch (level)
            {
                case 1: return "绿";
                case 2: return "蓝";
                case 3: return "紫";
                default: return "金";
            }
        }

        private void SetFeedback(string message)
        {
            if (feedbackText != null)
            {
                feedbackText.text = string.IsNullOrWhiteSpace(message) ? "操作未完成" : message;
            }
        }

        private sealed class CellVisual
        {
            internal CellVisual(Image image, Text label)
            {
                Image = image;
                Label = label;
            }

            internal Image Image { get; }
            internal Text Label { get; }
            internal bool IsUnlocked { get; set; }
            internal DeployCellHighlight Highlight { get; set; }
            internal DeployPlacementDragHandle DragHandle { get; set; }
        }

        private sealed class HandCard
        {
            internal HandCard(CardOfferItem card, RectTransform root, DeployCardVisual visual)
            {
                Card = card;
                Root = root;
                Visual = visual;
            }

            internal CardOfferItem Card { get; }
            internal RectTransform Root { get; }
            internal DeployCardVisual Visual { get; }
        }

        private sealed class DragSession
        {
            internal CardOfferItem Card;
            internal string DeploymentId;
            internal UnitFootprint Footprint;
            internal DeployCardVisual Ghost;
            internal HandCard SourceHandCard;
            internal GridCoordinate Anchor;
            internal bool HasAnchor;
            internal bool Snapped;
        }
    }

    internal sealed class DeployDropCell : MonoBehaviour, IDropHandler
    {
        private DeployScreen owner;
        private GridCoordinate coordinate;

        internal void Initialize(DeployScreen screen, GridCoordinate gridCoordinate)
        {
            owner = screen;
            coordinate = gridCoordinate;
        }

        internal GridCoordinate Coordinate => coordinate;

        /// <summary>
        /// The drag ghost owns anchor resolution, so a drop here just ends the drag. Keeping the
        /// handler makes uGUI treat cells as valid drop targets.
        /// </summary>
        public void OnDrop(PointerEventData eventData)
        {
        }
    }

    internal sealed class DeployCardDragHandle : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private DeployScreen owner;
        private CardOfferItem card;
        private bool dragging;

        internal void Initialize(DeployScreen screen, CardOfferItem item)
        {
            owner = screen;
            card = item;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = owner != null && owner.BeginCardDrag(card, eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragging)
            {
                owner.DragTo(eventData.position);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragging)
            {
                owner.EndDrag();
                dragging = false;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!dragging)
            {
                owner?.ActivateCard(card);
            }
        }
    }

    internal sealed class DeployPlacementDragHandle : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private DeployScreen owner;
        private string deploymentId;
        private bool dragging;

        internal void Initialize(DeployScreen screen, string id)
        {
            owner = screen;
            deploymentId = id;
            enabled = true;
        }

        internal void Clear()
        {
            owner = null;
            deploymentId = null;
            enabled = false;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = owner != null
                       && !string.IsNullOrEmpty(deploymentId)
                       && owner.BeginPlacementDrag(deploymentId, eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragging)
            {
                owner.DragTo(eventData.position);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragging)
            {
                owner.EndDrag();
                dragging = false;
            }
        }
    }
}
