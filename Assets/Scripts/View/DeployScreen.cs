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
        Rejected,

        /// <summary>Dropped over the hand: the placement was taken back and its card returned.</summary>
        ReturnedToHand
    }

    /// <summary>One level's palette, parsed once from <c>economy.deployUi.tierColors</c>.</summary>
    internal readonly struct TierPalette
    {
        internal TierPalette(string name, Color fill, Color border, Color text)
        {
            Name = name;
            Fill = fill;
            Border = border;
            Text = text;
        }

        internal string Name { get; }
        internal Color Fill { get; }
        internal Color Border { get; }
        internal Color Text { get; }
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
        private const float GhostAlpha = 0.5f;
        private const float GhostArtAlpha = 0.85f;

        private static readonly Color PageColor = new Color(0.055f, 0.07f, 0.105f, 0.98f);
        private static readonly Color PanelColor = new Color(0.09f, 0.12f, 0.17f, 0.96f);
        private static readonly Color UnlockedCellColor = new Color(0.18f, 0.23f, 0.29f, 1f);
        private static readonly Color LockedCellColor = new Color(0.055f, 0.065f, 0.08f, 1f);
        private static readonly Color OccupiedCellColor = new Color(0.17f, 0.39f, 0.58f, 1f);
        private static readonly Color ValidPreviewColor = new Color(0.12f, 0.72f, 0.36f, 1f);
        private static readonly Color InvalidPreviewColor = new Color(0.86f, 0.16f, 0.18f, 1f);
        private static readonly Color MergePreviewColor = new Color(0.97f, 0.78f, 0.18f, 1f);

        private static readonly Color CellFloorColor = new Color(0f, 0f, 0f, 0.42f);

        private readonly List<TierPalette> tierPalettes = new List<TierPalette>();
        private readonly Dictionary<string, DeployUnitCard> unitCards =
            new Dictionary<string, DeployUnitCard>(StringComparer.Ordinal);

        private readonly Dictionary<GridCoordinate, CellVisual> cells =
            new Dictionary<GridCoordinate, CellVisual>();
        private readonly List<HandCard> handCards = new List<HandCard>();
        private readonly List<GridCoordinate> mergePulseCells = new List<GridCoordinate>();

        /// <summary>
        /// Cells currently carrying a preview colour. A drag frame restores exactly these and paints
        /// exactly the new footprint, so canvas writes scale with the card, never with the field.
        /// </summary>
        private readonly List<GridCoordinate> highlightedCells = new List<GridCoordinate>();

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
        private RectTransform handPanel;
        private RectTransform gridRoot;
        private RectTransform unitLayer;
        private DeployUnitInfoPanel infoPanel;
        private RectTransform ownedEffectsRoot;
        private readonly List<GameObject> ownedEffectChips = new List<GameObject>();
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

        /// <summary>Gap between grid cells; part of the one shared measure, not a card-only value.</summary>
        public float CellSpacing => metrics.Spacing;

        /// <summary>Edge of one hand-card cell: <see cref="CellEdge"/> times the configured scale.</summary>
        public float HandCellEdge => handMetrics.CellEdge;

        public float HandCellSpacing => handMetrics.Spacing;

        /// <summary>Cursor-to-centre distance, in cell edges, that engages snapping.</summary>
        public float SnapRadiusCells => uiRules == null ? 0f : uiRules.SnapRadiusCells;

        /// <summary>
        /// Grid-cell <c>Image.color</c> writes this screen has actually performed. Every write
        /// dirties the canvas, so this is the cost that matters for drag smoothness. Counting is
        /// deliberate: wall-clock timings on this project have twice been polluted by concurrent
        /// sessions (TECH_DEBT rows 28-29), while a count is machine-independent.
        /// </summary>
        public int CellColorWriteCount { get; private set; }

        /// <summary>
        /// Rule-layer questions this screen has asked (<c>IsUnlocked</c>, <c>TryGetPlacementAt</c>,
        /// <c>Evaluate</c>, <c>EvaluateUnlock</c>). A drag frame must ask O(1) of these, not one per cell.
        /// </summary>
        public int GridQueryCount { get; private set; }

        /// <summary>
        /// Grid cells this screen has walked over while repainting. Unlike
        /// <see cref="CellColorWriteCount"/> this counts cells <i>touched</i>, not cells whose colour
        /// happened to change — so a full-field pass shows up here even when most cells already hold
        /// the right colour. This is the number that must never scale with field size during a drag.
        /// </summary>
        public int CellVisitCount { get; private set; }

        /// <summary>Cells the live preview is currently colouring.</summary>
        public int HighlightedCellCount => highlightedCells.Count;

        /// <summary>Total cells the field renders; the number a drag frame must NOT scale with.</summary>
        public int FieldCellCount => cells.Count;

        /// <summary>Chips shown for effects won this run — one per distinct effect id.</summary>
        public int OwnedEffectChipCount => ownedEffectChips.Count;

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

        /// <summary>Whole cards drawn on the grid — one per deployed unit, not one per occupied cell.</summary>
        public int DeployedUnitCardCount => unitCards.Count;

        /// <summary>Levels the palette defines. Longer than the merge ceiling on purpose.</summary>
        public int TierColorCount => tierPalettes.Count;

        /// <summary>Tiles a deployed unit's card actually draws, so a notch can be asserted.</summary>
        public IReadOnlyList<GridCoordinate> DeployedCardCellOffsets(string deploymentId)
        {
            return unitCards.TryGetValue(deploymentId, out DeployUnitCard card)
                ? card.Visual.CellOffsets
                : Array.Empty<GridCoordinate>();
        }

        /// <summary>Bounding-box size of a deployed unit's card.</summary>
        public Vector2 DeployedCardSize(string deploymentId)
        {
            return unitCards.TryGetValue(deploymentId, out DeployUnitCard card) ? card.Visual.Size : Vector2.zero;
        }

        /// <summary>The name written across a deployed unit's card. One label, not one per cell.</summary>
        public string DeployedCardName(string deploymentId)
        {
            return unitCards.TryGetValue(deploymentId, out DeployUnitCard card) && card.NameLabel != null
                ? card.NameLabel.text
                : string.Empty;
        }

        /// <summary>Text of a deployed unit's level badge.</summary>
        public string DeployedCardLevelBadge(string deploymentId)
        {
            return unitCards.TryGetValue(deploymentId, out DeployUnitCard card) && card.LevelLabel != null
                ? card.LevelLabel.text
                : string.Empty;
        }

        /// <summary>Fill colour a deployed unit's card is painted with, which encodes its level.</summary>
        public Color DeployedCardFillColor(string deploymentId)
        {
            return unitCards.TryGetValue(deploymentId, out DeployUnitCard card) ? card.Fill : Color.clear;
        }

        public Color DeployedCardBorderColor(string deploymentId)
        {
            return unitCards.TryGetValue(deploymentId, out DeployUnitCard card) ? card.Border : Color.clear;
        }

        /// <summary>Palette for a level, clamped to the table. Level 1 is index 0.</summary>
        public Color TierFillColor(int level) => Palette(level).Fill;

        public Color TierBorderColor(int level) => Palette(level).Border;

        public string TierColorName(int level) => Palette(level).Name;

        public bool IsInfoPanelOpen => infoPanel != null && infoPanel.IsOpen;

        public string InfoPanelUnitId => infoPanel == null ? string.Empty : infoPanel.ShownUnitId;

        public int InfoPanelLevel => infoPanel == null ? 0 : infoPanel.ShownLevel;

        /// <summary>Rows the info sheet is showing, "label    value" per entry.</summary>
        public IReadOnlyList<string> InfoPanelRows =>
            infoPanel == null ? Array.Empty<string>() : infoPanel.Rows;

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
            BuildTierPalettes();
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

        /// <summary>
        /// Parses <c>economy.deployUi.tierColors</c> once. The table lives in JSON rather than in a
        /// constant here because red line 3 puts tunable values in data, and because the palette is
        /// the only thing that tells a player a level-3 unit from a level-1 one at a glance.
        /// </summary>
        private void BuildTierPalettes()
        {
            tierPalettes.Clear();
            TierColorDef[] table = uiRules.TierColors;
            if (table == null)
            {
                return;
            }

            for (int index = 0; index < table.Length; index++)
            {
                TierColorDef entry = table[index];
                tierPalettes.Add(new TierPalette(
                    entry.Name, ParseColor(entry.Fill), ParseColor(entry.Border), ParseColor(entry.Text)));
            }
        }

        private static Color ParseColor(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color parsed) ? parsed : Color.magenta;
        }

        /// <summary>
        /// Palette for a level. Clamped rather than throwing: the merge ceiling is a rule and the
        /// palette length is presentation, so a unit arriving from somewhere the table has not caught
        /// up with should still render — in the nearest colour — not crash the deploy screen.
        /// </summary>
        /// <summary>
        /// What sits behind the artwork. Darker than the level colour on purpose: it is a bed for a
        /// picture that may be transparent or sparse, not the card's identity. The level reads from
        /// the border, the badge and the wash — the three places the reference art puts it.
        /// </summary>
        private static Color CardBedColor(Color fill)
        {
            return new Color(fill.r * 0.42f, fill.g * 0.42f, fill.b * 0.42f, 1f);
        }

        /// <summary>
        /// Whether a card of this footprint writes its unit's name.
        ///
        /// <para>The artwork in this game <i>is</i> the character the unit is named after, so once
        /// WO-C10 made the picture the card's body, the label started printing the same word twice:
        /// on 铁甲兵 at two different sizes, and on 弩车 with the two sets of strokes interleaved
        /// until neither could be read. A 1x1 keeps its label because there the text covers the card
        /// and almost no artwork survives behind it.</para>
        ///
        /// <para>Judged on <b>footprint</b>, never on rendered pixel size: hand cards are drawn at
        /// 70% scale, so a pixel rule would silently give the hand and the board different answers
        /// for the same unit.</para>
        /// </summary>
        private bool ShouldWriteName(UnitFootprint footprint)
        {
            return footprint == null
                   || footprint.OccupiedCellCount <= uiRules.NameLabelMaxFootprintCells;
        }

        /// <summary>How one hand card presents itself: its palette, its big name and its corner mark.</summary>
        private readonly struct CardFace
        {
            internal CardFace(TierPalette palette, string name, string badge)
            {
                Palette = palette;
                Name = name;
                Badge = badge;
            }

            internal TierPalette Palette { get; }
            internal string Name { get; }
            internal string Badge { get; }
        }

        /// <summary>
        /// One place that decides what a hand card looks like.
        ///
        /// <para>A unit's mark is its level. An effect's mark is its kind — 增 for a buff, 令 for a
        /// global, 锁 for an unlock card — because effects have no level and printing a "1" on them
        /// would state something untrue. Rarity is carried by the palette instead, on the same five
        /// colours units use, so a rare buff and a level-3 unit share a purple the player has
        /// already learned to read as "better than blue".</para>
        /// </summary>
        private CardFace ResolveCardFace(CardOfferItem card)
        {
            switch (card.Category)
            {
                case CardCategory.Unit:
                {
                    UnitDef unit = config.GetUnit(card.ContentId);
                    int level = (int)unit.Tier;
                    return new CardFace(Palette(level), unit.Name, level.ToString());
                }
                case CardCategory.Unlock:
                    return new CardFace(
                        new TierPalette(
                            "解锁",
                            CardColor(CardCategory.Unlock),
                            new Color(0.92f, 0.72f, 0.34f),
                            new Color(1f, 0.96f, 0.88f)),
                        UnlockShapeLabel(card.Unlock),
                        "锁");
                default:
                {
                    EffectDef effect = config.GetEffect(card.ContentId);
                    return new CardFace(
                        RarityPalette(effect.Rarity),
                        effect.Name,
                        card.Category == CardCategory.Buff ? "增" : "令");
                }
            }
        }

        /// <summary>Palette for an effect card's rarity, mapped onto the shared tier colours.</summary>
        private TierPalette RarityPalette(string rarity)
        {
            switch (rarity)
            {
                case "Common": return Palette(1);
                case "Uncommon": return Palette(2);
                case "Rare": return Palette(3);
                case "Epic": return Palette(4);
                case "Commander": return Palette(5);
                default: return Palette(1);
            }
        }

        private TierPalette Palette(int level)
        {
            if (tierPalettes.Count == 0)
            {
                return new TierPalette("—", OccupiedCellColor, UnlockedCellColor, Color.white);
            }
            int index = Mathf.Clamp(level - 1, 0, tierPalettes.Count - 1);
            return tierPalettes[index];
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
            // Semi-transparent so the landing preview underneath stays readable.
            drag.Ghost.SetAlpha(GhostAlpha);
            // The ghost carries the card's own artwork, not just a coloured block — otherwise the
            // picture appears to vanish the instant you pick a card up.
            CardFace ghostFace = ResolveCardFace(card);
            drag.Ghost.AddArtwork(ResolveCardSprite(card), 0f);
            drag.Ghost.AddTintWash(ghostFace.Palette.Fill, uiRules.UnitCardTintAlpha);
            drag.Ghost.AddOutline(
                ghostFace.Palette.Border, Mathf.Max(1f, uiRules.UnitCardOutlineRatio * metrics.CellEdge));
            if (card.Category != CardCategory.Unit || ShouldWriteName(footprint))
            {
                drag.Ghost.AddBigName(font, ghostFace.Name, ghostFace.Palette.Text);
            }

            // The badge rides along too. Once WO-C11 takes the name off a big card, the badge is the
            // only mark left on it — a ghost without one would be an anonymous coloured shape.
            drag.Ghost.AddLevelBadge(
                font, ghostFace.Badge, ghostFace.Palette.Border, Color.black,
                uiRules.LevelBadgeRatio * metrics.CellEdge);
            HideSourceForDrag(source);

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
            drag.Ghost.SetAlpha(GhostAlpha);
            TierPalette carried = Palette(placement.Level);
            drag.Ghost.AddArtwork(artSource.Find($"unit/{placement.UnitId}/idle"), 0f);
            drag.Ghost.AddTintWash(carried.Fill, uiRules.UnitCardTintAlpha);
            drag.Ghost.AddOutline(
                carried.Border, Mathf.Max(1f, uiRules.UnitCardOutlineRatio * metrics.CellEdge));
            if (ShouldWriteName(placement.Footprint))
            {
                drag.Ghost.AddBigName(font, config.GetUnit(placement.UnitId).Name, carried.Text);
            }
            drag.Ghost.AddLevelBadge(
                font, placement.Level, carried.Border, Color.black,
                uiRules.LevelBadgeRatio * metrics.CellEdge);
            FadeUnitCardForDrag(deploymentId);

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

            drag.ReleasePoint = screenPoint;
            drag.HasReleasePoint = true;
            Vector2 local = ScreenToLocal(dragLayer, screenPoint);
            Vector2 lifted = local + new Vector2(0f, uiRules.DragLiftCells * metrics.CellEdge);

            bool hadAnchor = TryResolveAnchor(lifted, drag.Footprint, out GridCoordinate anchor, out float distance);
            DeployCellHighlight highlight = DeployCellHighlight.Invalid;
            // Over the hand with a deployed unit in hand is the undo gesture, so say so rather than
            // reporting it as having left the board — the player is not making a mistake.
            string message = !string.IsNullOrEmpty(drag.DeploymentId) && IsReleaseOverHand(drag)
                ? "松开以撤回手牌"
                : "拖出了部署区";
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

            // Incremental only. RefreshCells() here would re-query the rule layer 98 times and
            // rewrite all 49 images every frame, forcing a full canvas rebuild — the WO-C4 regression.
            if (hadAnchor)
            {
                PaintFootprint(drag.Footprint, anchor, highlight);
            }
            else
            {
                ClearHighlights();
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
            if (IsReleaseOverHand(session) && !string.IsNullOrEmpty(session.DeploymentId))
            {
                // Dragging a deployed unit onto the hand is the undo gesture. Checked before the
                // anchor because the hand sits below the grid: a unit dragged that far down has left
                // the board on purpose, and treating it as "dropped outside" would just bounce back.
                outcome = ReturnPlacementToHand(session.DeploymentId);
            }
            else if (session.HasAnchor)
            {
                outcome = Commit(session, session.Anchor);
            }
            else
            {
                SetFeedback("拖出了部署区");
            }

            LastDragOutcome = outcome;
            drag = null;

            if (outcome == DeployDragOutcome.ReturnedToHand)
            {
                dragCue?.Invoke(DeployDragCue.Placed);
                DestroyGhost(session);
                RestoreHandCard(session);
                return outcome;
            }

            if (outcome == DeployDragOutcome.Rejected)
            {
                RestoreUnitCardAfterDrag(session.DeploymentId);
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

        /// <summary>
        /// Dims a unit's card while it is being carried, so the board shows the slot emptying.
        ///
        /// <para>A <see cref="CanvasGroup"/>, never <c>SetActive(false)</c>: the drag handle lives on
        /// this very object, and <c>ExecuteEvents</c> silently skips components on inactive objects —
        /// which is exactly how WO-C4's drag froze mid-gesture.</para>
        /// </summary>
        private void FadeUnitCardForDrag(string deploymentId)
        {
            if (!unitCards.TryGetValue(deploymentId, out DeployUnitCard card) || card.Visual == null)
            {
                return;
            }

            var group = card.Visual.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = card.Visual.gameObject.AddComponent<CanvasGroup>();
            }
            group.alpha = 0.22f;
        }

        /// <summary>Brings a carried card back to full strength when the drag ends without a commit.</summary>
        private void RestoreUnitCardAfterDrag(string deploymentId)
        {
            if (string.IsNullOrEmpty(deploymentId)
                || !unitCards.TryGetValue(deploymentId, out DeployUnitCard card)
                || card.Visual == null)
            {
                return;
            }

            var group = card.Visual.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
            }
        }

        /// <summary>
        /// Was the drag released over the hand panel? Screen space rather than the grid's own
        /// coordinates, because the question is "did the player let go on the hand", which the grid
        /// has no opinion about.
        /// </summary>
        private bool IsReleaseOverHand(DragSession session)
        {
            if (handPanel == null || !session.HasReleasePoint)
            {
                return false;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(handPanel, session.ReleasePoint, camera);
        }

        /// <summary>
        /// Undoes a placement through the economy, which owns the rule. The view only reports what it
        /// was told — including the refusal to dissolve a merged unit.
        /// </summary>
        private DeployDragOutcome ReturnPlacementToHand(string deploymentId)
        {
            if (economy.TryReturnUnitToHand(deploymentId, out string reason))
            {
                SetFeedback("已撤回到手牌");
                deploymentCommitted?.Invoke(DeploymentActionKind.Place);
                RefreshAll();
                return DeployDragOutcome.ReturnedToHand;
            }

            SetFeedback(reason);
            return DeployDragOutcome.Rejected;
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
            RestoreUnitCardAfterDrag(session.DeploymentId);
            ClearHighlights();
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
            GridQueryCount++;
            DeploymentEvaluation evaluation = economy.Grid.Evaluate(preview, anchor);
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

            GridQueryCount++;
            UnlockEvaluation evaluation = economy.Grid.EvaluateUnlock(card.Unlock, anchor);
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

            GridQueryCount++;
            SetFeedback(DescribeUnlock(economy.Grid.EvaluateUnlock(card.Unlock, anchor)));
            PaintFootprint(card.Unlock.Footprint, anchor, DeployCellHighlight.Invalid);
            return false;
        }

        public bool CommitCard(CardOfferItem card, GridCoordinate anchor)
        {
            DeploymentUnit preview = CreatePreviewUnit(card);
            GridQueryCount++;
            DeploymentEvaluation evaluation = economy.Grid.Evaluate(preview, anchor);
            if (!evaluation.IsValid)
            {
                SetFeedback(DescribeEvaluation(evaluation));
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
                PaintFootprint(preview.Footprint, anchor, DeployCellHighlight.Invalid);
                return false;
            }
            catch (ArgumentException exception)
            {
                SetFeedback(exception.Message);
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
            GridQueryCount++;
            DeploymentEvaluation evaluation = economy.Grid.Evaluate(preview, anchor);
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
                ClearHighlights();
                PaintSingleCell(anchor, InvalidPreviewColor);
                return false;
            }
        }

        public void CancelPreview()
        {
            ClearHighlights();
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

                // Tapping a unit card is a request to read it, not to place it — placing is a drag.
                UnitDef definition = config.GetUnit(card.ContentId);
                ShowUnitInfo(definition.Id, (int)definition.Tier);
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
            RefreshOwnedEffects();
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
                GridQueryCount++;
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
                GridQueryCount++;
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

            // The one rule-layer question a drag frame is allowed to ask.
            GridQueryCount++;
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

        /// <summary>
        /// Makes the source card invisible and click-through for the duration of the drag, while
        /// keeping its GameObject active.
        ///
        /// <para><b>It must stay active.</b> Unity routes OnDrag/OnEndDrag to the object that
        /// accepted OnBeginDrag (<c>PointerEventData.pointerDrag</c>), and <c>ExecuteEvents</c>
        /// silently skips components on an inactive object. Deactivating the card here — which is
        /// what the previous pass did — dropped every subsequent drag event including OnEndDrag, so
        /// the drag session never ended and the ghost froze mid-flight.</para>
        /// </summary>
        private static void HideSourceForDrag(HandCard source)
        {
            if (source?.Root == null)
            {
                return;
            }

            // Not `GetComponent() ?? AddComponent()`: a missing Unity component is a fake-null that
            // `??` does not treat as null, so that form silently hands back the missing component.
            CanvasGroup group = source.Root.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = source.Root.gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = 0f;
            group.blocksRaycasts = false;
        }

        private void RestoreHandCard(DragSession session)
        {
            RectTransform root = session.SourceHandCard?.Root;
            if (root == null)
            {
                return;
            }

            // A hand rebuild during the drag parks the card off the hand row instead of destroying
            // it, so the drag handle stays alive. Once the drag is over it has no owner: bin it.
            if (session.SourceDetached)
            {
                Destroy(root.gameObject);
                return;
            }

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.blocksRaycasts = true;
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
            // A card parked by a mid-drag rebuild no longer has a slot to fly back to, so aim at the
            // hand row itself rather than at wherever the parked object happens to sit.
            Transform target = session.SourceDetached
                ? handRoot
                : session.SourceHandCard?.Root;
            if (target != null)
            {
                Vector3 world = target.TransformPoint(Vector3.zero);
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
            ClearHighlights();
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
            Text title = RuntimeUiFactory.CreateText("Grid Title", panel, font, 26, FontStyle.Bold, TextAnchor.MiddleLeft);
            title.text = "部署阵地 · 拖动兵器卡落位";
            RuntimeUiFactory.SetAnchors(title.rectTransform, new Vector2(0.025f, 0.9f), new Vector2(0.40f, 0.985f));

            // Effects won this run live in the title row's spare width, so adding them costs the
            // grid no height and leaves the shared cell metric untouched.
            var effectsObject = new GameObject("Owned Effects", typeof(RectTransform));
            ownedEffectsRoot = effectsObject.GetComponent<RectTransform>();
            ownedEffectsRoot.SetParent(panel, false);
            RuntimeUiFactory.SetAnchors(ownedEffectsRoot, new Vector2(0.41f, 0.895f), new Vector2(0.975f, 0.99f));

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

                    // The slot's floor, inset so the tile's own rim always shows. That rim is what
                    // makes an empty cell read as a socket rather than as background, and it is what
                    // stays visible as a coloured ring around a unit card or a drag preview.
                    Image floor = RuntimeUiFactory.CreateImage("Cell Floor", image.transform, CellFloorColor);
                    floor.raycastTarget = false;
                    float inset = uiRules.CellInsetRatio * metrics.CellEdge;
                    RuntimeUiFactory.Stretch(floor.rectTransform);
                    floor.rectTransform.offsetMin = new Vector2(inset, inset);
                    floor.rectTransform.offsetMax = new Vector2(-inset, -inset);

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

            // Deployed units live above the slots, so a unit reads as one card lying on the board
            // rather than as a set of recoloured cells. Same rect as the field, so the two share the
            // one metric and cannot drift apart.
            var unitObject = new GameObject("Deployed Units", typeof(RectTransform));
            unitLayer = unitObject.GetComponent<RectTransform>();
            unitLayer.SetParent(gridRoot.parent, false);
            unitLayer.anchorMin = gridRoot.anchorMin;
            unitLayer.anchorMax = gridRoot.anchorMax;
            unitLayer.offsetMin = gridRoot.offsetMin;
            unitLayer.offsetMax = gridRoot.offsetMax;
        }

        private void BuildHand(RectTransform root)
        {
            RectTransform panel = RuntimeUiFactory.CreatePanel("Hand Panel", root, PanelColor);
            handPanel = panel;
            RuntimeUiFactory.SetAnchors(panel, new Vector2(0.035f, 0.13f), new Vector2(0.965f, 0.335f));
            Text title = RuntimeUiFactory.CreateText("Hand Title", panel, font, 28, FontStyle.Bold, TextAnchor.MiddleLeft);
            title.text = "手 牌 · 拖回此处可撤回部署";
            RuntimeUiFactory.SetAnchors(title.rectTransform, new Vector2(0.025f, 0.82f), new Vector2(0.86f, 0.98f));

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
            // The info sheet is built first so the ghost still passes over it during a drag.
            infoPanel = DeployUnitInfoPanel.Create(root, font, () => SetFeedback("已关闭资料"));

            var layerObject = new GameObject("Drag Layer", typeof(RectTransform));
            dragLayer = layerObject.GetComponent<RectTransform>();
            dragLayer.SetParent(root, false);
            RuntimeUiFactory.Stretch(dragLayer);
            dragLayer.SetAsLastSibling();
        }

        /// <summary>Opens the stat sheet for a unit id at a level. Every number comes from GameConfig.</summary>
        public bool ShowUnitInfo(string unitId, int level)
        {
            if (infoPanel == null || string.IsNullOrEmpty(unitId))
            {
                return false;
            }

            UnitDef definition = config.GetUnit(unitId);
            infoPanel.Show(definition, Mathf.Max(1, level));
            SetFeedback($"{definition.Name} 资料");
            return true;
        }

        /// <summary>Opens the stat sheet for a deployed unit, at the level it actually stands at.</summary>
        public bool ShowDeployedUnitInfo(string deploymentId)
        {
            if (!economy.Grid.TryGetPlacement(deploymentId, out DeploymentPlacement placement))
            {
                return false;
            }
            return ShowUnitInfo(placement.UnitId, placement.Level);
        }

        public void CloseUnitInfo()
        {
            infoPanel?.Close();
        }

        private void Subscribe()
        {
            economy.CoinsChanged += HandleCoinsChanged;
            economy.HandChanged += HandleHandChanged;
            economy.DeploymentsChanged += RefreshCells;
            economy.EffectsChanged += HandleEffectsChanged;
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
            economy.EffectsChanged -= HandleEffectsChanged;
        }

        private void HandleCoinsChanged(int value)
        {
            RefreshHeader();
        }

        private void HandleHandChanged(CardOffer value)
        {
            RebuildHand();
        }

        private void HandleEffectsChanged(IReadOnlyList<string> value)
        {
            RefreshOwnedEffects();
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

        /// <summary>
        /// Rebuilds every cell's cached base appearance from the rule layer. This is the expensive
        /// pass — it asks the grid about all 49 cells and can rewrite all 49 images.
        ///
        /// <para><b>It must never run inside a drag frame.</b> Call it only when the board actually
        /// changed: placement, merge, unlock, hand refresh, or entering the deploy phase. WO-C4's
        /// first pass called it from <c>DragTo</c>, which forced a full canvas rebuild every frame
        /// and is what made dragging unusable.</para>
        /// </summary>
        private void RefreshCells()
        {
            ClearHighlights();
            foreach (KeyValuePair<GridCoordinate, CellVisual> pair in cells)
            {
                GridCoordinate coordinate = pair.Key;
                CellVisual cell = pair.Value;
                cell.Highlight = DeployCellHighlight.None;
                CellVisitCount++;
                GridQueryCount++;
                cell.IsUnlocked = economy.Grid.IsUnlocked(coordinate);
                if (!cell.IsUnlocked)
                {
                    // Locked cells are plain dark tiles. The old per-cell "锁" glyph put 40 labels on
                    // screen and drowned the playable centre (see 部署页实测-2026-08-15 item 1).
                    cell.BaseColor = LockedCellColor;
                    cell.BaseLabel = string.Empty;
                    ApplyBaseAppearance(cell);
                    cell.DragHandle?.Clear();
                    continue;
                }

                // Occupancy no longer changes how the cell itself looks. The slot stays a slot; the
                // unit that stands in it is a card on the layer above, drawn once across its whole
                // footprint. Painting occupancy here is what wrote a 2x2 unit's name four times and
                // gave every unit the same blue.
                cell.BaseColor = UnlockedCellColor;
                cell.BaseLabel = string.Empty;
                ApplyBaseAppearance(cell);
                cell.DragHandle?.Clear();
            }

            RefreshUnitCards();
        }

        /// <summary>
        /// Draws one card per deployed unit, spanning its whole footprint.
        ///
        /// <para>Deliberately outside the per-cell path: cards are rebuilt only when the deployment
        /// set changes, never on a drag frame, so the WO-C4 bound — a drag frame touches at most
        /// twice the dragged card's footprint — still holds with nothing added to it.</para>
        /// </summary>
        private void RefreshUnitCards()
        {
            if (unitLayer == null)
            {
                return;
            }

            foreach (KeyValuePair<string, DeployUnitCard> pair in unitCards)
            {
                GameObject host = pair.Value.Visual == null ? null : pair.Value.Visual.gameObject;
                if (host == null)
                {
                    continue;
                }

                // Play-mode Destroy() runs at end of frame, so a card discarded here would keep
                // rendering and keep answering pointer events until then. Detach first.
                host.transform.SetParent(null, false);
                host.SetActive(false);
                Destroy(host);
            }
            unitCards.Clear();

            IReadOnlyList<DeploymentPlacement> placements = economy.Grid.Placements;
            for (int index = 0; index < placements.Count; index++)
            {
                CreateUnitCard(placements[index]);
            }
        }

        private void CreateUnitCard(DeploymentPlacement placement)
        {
            UnitDef definition = config.GetUnit(placement.UnitId);
            TierPalette palette = Palette(placement.Level);
            float inset = uiRules.UnitCardInsetRatio * metrics.CellEdge;
            float outline = Mathf.Max(1f, uiRules.UnitCardOutlineRatio * metrics.CellEdge);
            float badge = uiRules.LevelBadgeRatio * metrics.CellEdge;

            // Layer order is the whole point of WO-C10, and sibling order is what enforces it:
            // fill → artwork → level wash → border → name → badge. The artwork is the card's body
            // and the level colour survives as border, badge and a thin wash, which is how the
            // reference art reads. WO-C9 had the fill on top of the art and the picture vanished.
            DeployCardVisual visual = DeployCardVisual.CreateSolid(
                $"Unit {placement.DeploymentId}", unitLayer, placement.Footprint, metrics,
                CardBedColor(palette.Fill), inset);
            visual.Rect.anchoredPosition = metrics.FootprintCentreInField(
                placement.Footprint, placement.Anchor, economy.Grid.Width, economy.Grid.Height);
            visual.SetRaycastTarget(true);
            visual.AddArtwork(artSource.Find($"unit/{placement.UnitId}/idle"), 0f);
            visual.AddTintWash(palette.Fill, uiRules.UnitCardTintAlpha);
            visual.AddOutline(palette.Border, outline);
            Text name = ShouldWriteName(placement.Footprint)
                ? visual.AddBigName(font, definition.Name, palette.Text)
                : null;
            Text level = visual.AddLevelBadge(font, placement.Level, palette.Border, Color.black, badge);

            var handle = visual.gameObject.AddComponent<DeployPlacementDragHandle>();
            handle.Initialize(this, placement.DeploymentId);
            unitCards.Add(
                placement.DeploymentId,
                new DeployUnitCard(visual, name, level, palette.Fill, palette.Border));
        }

        private void ApplyBaseAppearance(CellVisual cell)
        {
            SetCellColor(cell, cell.BaseColor);
            if (!string.Equals(cell.Label.text, cell.BaseLabel, StringComparison.Ordinal))
            {
                cell.Label.text = cell.BaseLabel;
            }
        }

        /// <summary>
        /// The single funnel for grid-cell colour writes. Skipping no-op writes keeps the canvas
        /// clean and makes <see cref="CellColorWriteCount"/> an honest measure of real repaint cost.
        /// </summary>
        private void SetCellColor(CellVisual cell, Color color)
        {
            if (cell.Image == null || cell.Image.color == color)
            {
                return;
            }

            cell.Image.color = color;
            CellColorWriteCount++;
        }

        /// <summary>Restores the cached base colour of every previously highlighted cell.</summary>
        private void ClearHighlights()
        {
            for (int index = 0; index < highlightedCells.Count; index++)
            {
                if (cells.TryGetValue(highlightedCells[index], out CellVisual cell))
                {
                    CellVisitCount++;
                    cell.Highlight = DeployCellHighlight.None;
                    SetCellColor(cell, cell.BaseColor);
                }
            }
            highlightedCells.Clear();
            mergePulseCells.Clear();
        }

        /// <summary>
        /// Shows the effects won so far this run. The run state already carried them across minor
        /// stages; the deploy screen simply never displayed them, so a player could not tell what
        /// they had picked at settlement. Duplicates of a stacking effect collapse into one chip
        /// with a count, and tapping a chip prints its description into the feedback line.
        /// </summary>
        private void RefreshOwnedEffects()
        {
            if (ownedEffectsRoot == null)
            {
                return;
            }

            for (int index = 0; index < ownedEffectChips.Count; index++)
            {
                if (ownedEffectChips[index] != null)
                {
                    ownedEffectChips[index].transform.SetParent(null, false);
                    Destroy(ownedEffectChips[index]);
                }
            }
            ownedEffectChips.Clear();

            string[] owned = economy.State.OwnedEffects ?? Array.Empty<string>();
            var order = new List<string>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < owned.Length; index++)
            {
                string id = owned[index];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                if (counts.TryGetValue(id, out int seen))
                {
                    counts[id] = seen + 1;
                }
                else
                {
                    counts[id] = 1;
                    order.Add(id);
                }
            }

            if (order.Count == 0)
            {
                return;
            }

            ownedEffectsRoot.ForceUpdateRectTransforms();
            float height = ownedEffectsRoot.rect.height;
            if (height <= 1f)
            {
                height = metrics.CellEdge * 0.62f;
            }
            float chipEdge = Mathf.Max(24f, height);
            float gap = chipEdge * 0.18f;
            float total = (order.Count * chipEdge) + (Mathf.Max(0, order.Count - 1) * gap);
            float cursor = (total * 0.5f) - (chipEdge * 0.5f);

            for (int index = 0; index < order.Count; index++)
            {
                CreateOwnedEffectChip(order[index], counts[order[index]], chipEdge, cursor);
                cursor -= chipEdge + gap;
            }
        }

        private void CreateOwnedEffectChip(string effectId, int count, float chipEdge, float centreX)
        {
            EffectDef definition = config.GetEffect(effectId);
            Sprite icon = artSource.Find($"effect/{effectId}/icon");

            Image chip = RuntimeUiFactory.CreateImage(
                $"Owned Effect {effectId}", ownedEffectsRoot, EffectChipColor(icon != null));
            chip.raycastTarget = true;
            RectTransform rect = chip.rectTransform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(chipEdge, chipEdge);
            rect.anchoredPosition = new Vector2(-centreX - (chipEdge * 0.5f), 0f);

            if (icon != null)
            {
                Image art = RuntimeUiFactory.CreateImage("Icon", chip.transform, Color.white);
                RuntimeUiFactory.SetAnchors(art.rectTransform, new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.9f));
                art.preserveAspect = true;
                art.sprite = icon;
            }
            else
            {
                // No icon imported yet: fall back to the effect's own first character so the slot
                // still identifies itself rather than showing an anonymous block.
                Text glyph = RuntimeUiFactory.CreateText(
                    "Glyph", chip.transform, font, Mathf.Max(12, Mathf.RoundToInt(chipEdge * 0.5f)),
                    FontStyle.Bold, TextAnchor.MiddleCenter);
                RuntimeUiFactory.Stretch(glyph.rectTransform);
                glyph.text = string.IsNullOrEmpty(definition.Name)
                    ? "?"
                    : definition.Name.Substring(0, 1);
            }

            if (count > 1)
            {
                Text badge = RuntimeUiFactory.CreateText(
                    "Count", chip.transform, font, Mathf.Max(10, Mathf.RoundToInt(chipEdge * 0.34f)),
                    FontStyle.Bold, TextAnchor.LowerRight);
                RuntimeUiFactory.Stretch(badge.rectTransform);
                badge.text = $"×{count}";
            }

            var button = chip.gameObject.AddComponent<Button>();
            string describe = string.IsNullOrWhiteSpace(definition.Desc)
                ? definition.Name
                : $"{definition.Name}：{definition.Desc}";
            button.onClick.AddListener(() => SetFeedback(describe));
            ownedEffectChips.Add(chip.gameObject);
        }

        private static Color EffectChipColor(bool hasIcon)
        {
            return hasIcon
                ? new Color(0.16f, 0.22f, 0.30f, 1f)
                : new Color(0.20f, 0.42f, 0.32f, 1f);
        }

        private void RebuildHand()
        {
            if (handRoot == null)
            {
                return;
            }
            for (int index = 0; index < handCards.Count; index++)
            {
                RectTransform root = handCards[index].Root;
                if (root == null)
                {
                    continue;
                }

                // The card currently under the pointer must survive this rebuild: destroying it
                // would invalidate PointerEventData.pointerDrag and kill the rest of the drag, the
                // same way deactivating it used to. Park it on the drag layer, still active, and let
                // the drag's own teardown dispose of it.
                if (drag != null && ReferenceEquals(handCards[index], drag.SourceHandCard))
                {
                    root.SetParent(dragLayer, false);
                    drag.SourceDetached = true;
                    continue;
                }

                // Destroy() is deferred to end of frame in play mode, so a card discarded here is
                // still parented, still rendering and still holding a live drag handle until then.
                // Two refreshes in one frame therefore stacked overlapping cards on top of each
                // other. Detaching and deactivating takes effect immediately, so the old row is gone
                // the moment it is replaced.
                root.SetParent(null, false);
                root.gameObject.SetActive(false);
                Destroy(root.gameObject);
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
            float cardWidth = 0f;
            for (int index = 0; index < offer.Cards.Count; index++)
            {
                UnitFootprint footprint = ResolveFootprint(offer.Cards[index]);
                widths[index] = footprint == null
                    ? handMetrics.CellEdge
                    : handMetrics.FootprintSize(footprint).x;
                cardWidth += widths[index];
            }

            // The row was centred but bunched: three small cards clustered mid-panel with the rest
            // of the width empty, which is what read as "偏左、右侧留白". The gap now opens up to
            // spread the same cards across most of the panel, bounded so two cards never drift into
            // opposite corners. Equal gaps and a zero midpoint are unchanged, so the WO-C4 layout
            // gate still holds.
            int gaps = Mathf.Max(0, offer.Cards.Count - 1);
            float gap = handMetrics.Step * 0.5f;
            if (gaps > 0)
            {
                handRoot.ForceUpdateRectTransforms();
                float available = handRoot.rect.width;
                if (available > 1f)
                {
                    float spread = ((available * 0.92f) - cardWidth) / gaps;
                    gap = Mathf.Clamp(spread, gap, handMetrics.Step * 2.2f);
                }
            }
            float totalWidth = cardWidth + (gap * gaps);

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

            // Every hand card is built the same way now, including effect cards: bed → artwork →
            // colour wash → border → name → badge. Before WO-C10 a buff was a bare purple rectangle
            // with no border and no badge sitting next to fully dressed unit cards — two visual
            // languages in one row, and nothing to say how good the buff was.
            //
            // Effect cards have no footprint because they never touch the grid; the 1x1 shape here
            // is presentation only. ResolveFootprint still returns null for them, so the placement
            // and drag rules are unchanged and a buff still cannot be dragged onto the board.
            UnitFootprint shape = footprint ?? UnitFootprint.FromDefinition(config.GetUnit("zu"));
            CardFace face = ResolveCardFace(card);

            visual = DeployCardVisual.Create(
                $"Card {index} {card.ContentId}", handRoot, shape, handMetrics, CardBedColor(face.Palette.Fill));
            visual.SetRaycastTarget(true);
            visual.AddArtwork(ResolveCardSprite(card), 0f);
            visual.AddTintWash(face.Palette.Fill, uiRules.UnitCardTintAlpha);
            visual.AddOutline(
                face.Palette.Border, Mathf.Max(1f, uiRules.UnitCardOutlineRatio * handMetrics.CellEdge));

            // Same footprint rule as the board, so a unit looks the same in hand as it does deployed.
            // Only unit cards duplicate themselves this way: an unlock card's "3x1" and an effect's
            // name appear nowhere in their artwork, so those keep their labels at every size.
            if (card.Category != CardCategory.Unit || ShouldWriteName(footprint))
            {
                visual.AddBigName(font, face.Name, face.Palette.Text);
            }
            visual.AddLevelBadge(
                font, face.Badge, face.Palette.Border, Color.black,
                uiRules.LevelBadgeRatio * handMetrics.CellEdge);
            rootRect = visual.Rect;

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

        /// <summary>
        /// Highlights every cell the footprint covers — notched shapes skip their gap. Incremental
        /// by construction: it first restores only the cells the previous preview touched, so a drag
        /// frame writes at most (previous footprint + new footprint) cells regardless of field size.
        /// </summary>
        private void PaintFootprint(UnitFootprint footprint, GridCoordinate anchor, DeployCellHighlight highlight)
        {
            ClearHighlights();
            Color color = HighlightColor(highlight);
            bool painted = false;
            for (int index = 0; index < footprint.OccupiedOffsets.Count; index++)
            {
                GridCoordinate coordinate = anchor + footprint.OccupiedOffsets[index];
                if (cells.TryGetValue(coordinate, out CellVisual cell))
                {
                    CellVisitCount++;
                    SetCellColor(cell, color);
                    cell.Highlight = highlight;
                    highlightedCells.Add(coordinate);
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
                SetCellColor(cell, color);
                highlightedCells.Add(coordinate);
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
                BaseColor = image.color;
                BaseLabel = string.Empty;
            }

            internal Image Image { get; }
            internal Text Label { get; }
            internal bool IsUnlocked { get; set; }
            internal DeployCellHighlight Highlight { get; set; }
            internal DeployPlacementDragHandle DragHandle { get; set; }

            /// <summary>
            /// Colour this cell shows when nothing is previewing over it. Cached by
            /// <see cref="RefreshCells"/> so a drag frame can restore it without re-asking the
            /// rule layer what is standing here.
            /// </summary>
            internal Color BaseColor { get; set; }

            internal string BaseLabel { get; set; }
        }

        /// <summary>One deployed unit's card and the parts of it a test can read back.</summary>
        private sealed class DeployUnitCard
        {
            internal DeployUnitCard(
                DeployCardVisual visual, Text nameLabel, Text levelLabel, Color fill, Color border)
            {
                Visual = visual;
                NameLabel = nameLabel;
                LevelLabel = levelLabel;
                Fill = fill;
                Border = border;
            }

            internal DeployCardVisual Visual { get; }
            internal Text NameLabel { get; }
            internal Text LevelLabel { get; }
            internal Color Fill { get; }
            internal Color Border { get; }
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

            /// <summary>Set when a hand rebuild parked this card outside the hand mid-drag.</summary>
            internal bool SourceDetached;

            /// <summary>Last pointer position in screen space, so release can be located on the UI.</summary>
            internal Vector2 ReleasePoint;

            internal bool HasReleasePoint;
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
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
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

        /// <summary>A tap that never became a drag opens the unit's stat sheet.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!dragging && owner != null && !string.IsNullOrEmpty(deploymentId))
            {
                owner.ShowDeployedUnitInfo(deploymentId);
            }
        }
    }
}
