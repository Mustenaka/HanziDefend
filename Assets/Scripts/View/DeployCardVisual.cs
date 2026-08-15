using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HanziDefend.Gameplay.Deploy;
using UnityEngine;
using UnityEngine.UI;

namespace HanziDefend.View
{
    /// <summary>
    /// A card drawn as its actual footprint: one tile per occupied cell, laid out on the shared
    /// <see cref="DeployGridMetrics"/>. Because the tiles come straight from
    /// <see cref="UnitFootprint.OccupiedOffsets"/>, an L-shaped unit is genuinely notched rather
    /// than a 2x2 rectangle with a painted-on corner, and the card's bounding box always matches the
    /// footprint's aspect ratio.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class DeployCardVisual : MonoBehaviour
    {
        private readonly List<Image> tiles = new List<Image>();
        private readonly List<GridCoordinate> offsets = new List<GridCoordinate>();
        private ReadOnlyCollection<GridCoordinate> readOnlyOffsets;
        private Color baseColor;

        internal RectTransform Rect { get; private set; }

        internal UnitFootprint Footprint { get; private set; }

        /// <summary>The occupied offsets this card actually rendered, in footprint order.</summary>
        internal IReadOnlyList<GridCoordinate> CellOffsets =>
            readOnlyOffsets ?? (readOnlyOffsets = offsets.AsReadOnly());

        internal Vector2 Size => Rect == null ? Vector2.zero : Rect.sizeDelta;

        internal static DeployCardVisual Create(
            string name,
            Transform parent,
            UnitFootprint footprint,
            DeployGridMetrics metrics,
            Color color)
        {
            return Create(name, parent, footprint, metrics, color, 0f, false);
        }

        /// <summary>
        /// Builds the card as one continuous shape: tiles span the full cell pitch so neighbours butt
        /// together with no seam, and only the silhouette's outer edges are pulled in by
        /// <paramref name="inset"/>. That is what turns "three tiles in a row" into one long card,
        /// while leaving the grid slot's rim showing around it.
        /// </summary>
        internal static DeployCardVisual CreateSolid(
            string name,
            Transform parent,
            UnitFootprint footprint,
            DeployGridMetrics metrics,
            Color color,
            float inset)
        {
            return Create(name, parent, footprint, metrics, color, inset, true);
        }

        private static DeployCardVisual Create(
            string name,
            Transform parent,
            UnitFootprint footprint,
            DeployGridMetrics metrics,
            Color color,
            float inset,
            bool solid)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));

            var host = new GameObject(name, typeof(RectTransform), typeof(DeployCardVisual));
            host.transform.SetParent(parent, false);
            DeployCardVisual visual = host.GetComponent<DeployCardVisual>();
            visual.Build(footprint, metrics, color, inset, solid);
            return visual;
        }

        private void Build(
            UnitFootprint footprint, DeployGridMetrics metrics, Color color, float inset, bool solid)
        {
            Footprint = footprint;
            baseColor = color;
            Rect = GetComponent<RectTransform>();
            Rect.anchorMin = new Vector2(0.5f, 0.5f);
            Rect.anchorMax = new Vector2(0.5f, 0.5f);
            Rect.pivot = new Vector2(0.5f, 0.5f);
            Rect.sizeDelta = metrics.FootprintSize(footprint);

            for (int index = 0; index < footprint.OccupiedOffsets.Count; index++)
            {
                GridCoordinate offset = footprint.OccupiedOffsets[index];
                offsets.Add(offset);
                Image tile = RuntimeUiFactory.CreateImage($"Tile {offset.Column},{offset.Row}", transform, color);
                RectTransform tileRect = tile.rectTransform;
                tileRect.anchorMin = new Vector2(0.5f, 0.5f);
                tileRect.anchorMax = new Vector2(0.5f, 0.5f);
                tileRect.pivot = new Vector2(0.5f, 0.5f);

                if (solid)
                {
                    ShapeSolidTile(tileRect, offset, metrics, inset);
                }
                else
                {
                    tileRect.sizeDelta = new Vector2(metrics.CellEdge, metrics.CellEdge);
                    tileRect.anchoredPosition = metrics.CellCentreInFootprint(footprint, offset);
                }

                tiles.Add(tile);
            }
        }

        /// <summary>
        /// Sizes one tile of a continuous card. A side facing another tile of the same unit grows to
        /// the full pitch so the seam closes; a side facing the outside world pulls back by the
        /// inset. The rect is then re-centred by half the asymmetry it just picked up.
        /// </summary>
        private void ShapeSolidTile(
            RectTransform tileRect, GridCoordinate offset, DeployGridMetrics metrics, float inset)
        {
            float left = HasNeighbour(offset, -1, 0) ? 0f : inset;
            float right = HasNeighbour(offset, 1, 0) ? 0f : inset;
            float bottom = HasNeighbour(offset, 0, -1) ? 0f : inset;
            float top = HasNeighbour(offset, 0, 1) ? 0f : inset;

            tileRect.sizeDelta = new Vector2(
                Mathf.Max(1f, metrics.Step - left - right),
                Mathf.Max(1f, metrics.Step - bottom - top));
            tileRect.anchoredPosition = metrics.CellCentreInFootprint(Footprint, offset)
                                        + new Vector2((left - right) * 0.5f, (bottom - top) * 0.5f);
        }

        private bool HasNeighbour(GridCoordinate offset, int columnStep, int rowStep)
        {
            var neighbour = new GridCoordinate(offset.Column + columnStep, offset.Row + rowStep);
            for (int index = 0; index < Footprint.OccupiedOffsets.Count; index++)
            {
                if (Footprint.OccupiedOffsets[index].Equals(neighbour))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Adds a label centred on the whole card, sized against the shared cell edge.</summary>
        internal Text AddLabel(Font font, string content, float cellEdge)
        {
            Text label = RuntimeUiFactory.CreateText(
                "Card Label", transform, font, Mathf.Max(10, Mathf.RoundToInt(cellEdge * 0.26f)),
                FontStyle.Bold, TextAnchor.MiddleCenter);
            RuntimeUiFactory.Stretch(label.rectTransform);
            label.text = content;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        /// <summary>
        /// The unit's name written large across the whole card, the way the reference art does it.
        ///
        /// <para>Direction follows the card's own shape: a 3x1 reads left to right, a 1x2 stacks its
        /// characters down the column. Writing a tall card horizontally would either shrink the text
        /// to nothing or spill it across the neighbours — and unreadable names on identical blue
        /// tiles is exactly the complaint this replaces.</para>
        /// </summary>
        internal Text AddBigName(Font font, string content, Color color)
        {
            string text = content ?? string.Empty;
            int characters = Mathf.Max(1, text.Length);
            Vector2 size = Size;
            bool vertical = size.y > size.x * 1.2f;
            float along = vertical ? size.y : size.x;
            float across = vertical ? size.x : size.y;

            // A notched card has a whole cell of its bounding box missing, so the name has to sit
            // smaller than the box suggests or it runs out over the empty corner.
            bool notched = offsets.Count < Footprint.Width * Footprint.Height;
            float budget = notched ? 0.64f : 0.84f;

            // Fit along the writing direction, then cap by the other axis so one character on a long
            // card does not grow taller than the card is deep.
            float fitted = Mathf.Min((along * budget) / characters, across * (notched ? 0.5f : 0.66f));
            Text label = RuntimeUiFactory.CreateText(
                "Unit Name", transform, font, Mathf.Max(9, Mathf.RoundToInt(fitted)),
                FontStyle.Bold, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.sizeDelta = size;

            // Centred on the tiles that exist, not on the bounding box. On 冲车 and 弩车 those differ
            // by half a cell, and centring on the box hangs the name over the missing corner.
            label.rectTransform.anchoredPosition = TileCentroid();
            label.text = vertical ? string.Join("\n", text.ToCharArray()) : text;
            label.color = color;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.lineSpacing = 0.84f;
            return label;
        }

        /// <summary>Average centre of the tiles the card actually draws.</summary>
        private Vector2 TileCentroid()
        {
            if (tiles.Count == 0)
            {
                return Vector2.zero;
            }

            Vector2 total = Vector2.zero;
            for (int index = 0; index < tiles.Count; index++)
            {
                total += tiles[index].rectTransform.anchoredPosition;
            }
            return total / tiles.Count;
        }

        /// <summary>
        /// Traces the card's silhouette, drawing a strip only on sides with no neighbouring tile.
        /// Interior sides get nothing, so a 3x1 reads as one long card rather than three boxes, and a
        /// notched L gets a real outline around its missing corner — which a bounding-box border
        /// could not express.
        /// </summary>
        internal void AddOutline(Color color, float thickness)
        {
            var edges = new GameObject("Outline", typeof(RectTransform));
            RectTransform edgeRoot = edges.GetComponent<RectTransform>();
            edgeRoot.SetParent(transform, false);
            RuntimeUiFactory.Stretch(edgeRoot);

            for (int index = 0; index < offsets.Count; index++)
            {
                GridCoordinate offset = offsets[index];
                RectTransform tile = tiles[index].rectTransform;
                Vector2 centre = tile.anchoredPosition;
                Vector2 half = tile.sizeDelta * 0.5f;
                Vector2 vertical = new Vector2(thickness, tile.sizeDelta.y + thickness);
                Vector2 horizontal = new Vector2(tile.sizeDelta.x + thickness, thickness);

                AddEdge(edgeRoot, offset, -1, 0, centre + new Vector2(-half.x, 0f), vertical, color);
                AddEdge(edgeRoot, offset, 1, 0, centre + new Vector2(half.x, 0f), vertical, color);
                AddEdge(edgeRoot, offset, 0, -1, centre + new Vector2(0f, -half.y), horizontal, color);
                AddEdge(edgeRoot, offset, 0, 1, centre + new Vector2(0f, half.y), horizontal, color);
            }
        }

        private void AddEdge(
            RectTransform parent,
            GridCoordinate offset,
            int columnStep,
            int rowStep,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            if (HasNeighbour(offset, columnStep, rowStep))
            {
                return;
            }

            Image strip = RuntimeUiFactory.CreateImage(
                $"Edge {offset.Column},{offset.Row} {columnStep},{rowStep}", parent, color);
            strip.raycastTarget = false;
            RectTransform rect = strip.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        /// <summary>
        /// The level badge, pinned to the bottom-left <b>occupied</b> tile rather than to the
        /// bounding box. 冲车's missing corner is its lower left, so a bounding-box badge would hang
        /// in the notch with nothing behind it.
        /// </summary>
        internal Text AddLevelBadge(Font font, int level, Color fill, Color textColor, float edge)
        {
            int cornerIndex = 0;
            for (int index = 1; index < offsets.Count; index++)
            {
                GridCoordinate candidate = offsets[index];
                GridCoordinate best = offsets[cornerIndex];
                if (candidate.Row < best.Row
                    || (candidate.Row == best.Row && candidate.Column < best.Column))
                {
                    cornerIndex = index;
                }
            }

            RectTransform corner = tiles[cornerIndex].rectTransform;
            Vector2 half = corner.sizeDelta * 0.5f;
            Image badge = RuntimeUiFactory.CreateImage("Level Badge", transform, fill);
            badge.raycastTarget = false;
            RectTransform rect = badge.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(edge, edge);
            rect.anchoredPosition = corner.anchoredPosition
                                    + new Vector2(-half.x + (edge * 0.5f), -half.y + (edge * 0.5f));

            Text label = RuntimeUiFactory.CreateText(
                "Level", badge.transform, font, Mathf.Max(9, Mathf.RoundToInt(edge * 0.72f)),
                FontStyle.Bold, TextAnchor.MiddleCenter);
            RuntimeUiFactory.Stretch(label.rectTransform);
            label.text = level.ToString();
            label.color = textColor;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        /// <summary>Adds artwork clipped to the card's bounding box.</summary>
        internal Image AddArt(Sprite sprite)
        {
            Image art = RuntimeUiFactory.CreateImage("Art", transform, Color.white);
            RuntimeUiFactory.SetAnchors(art.rectTransform, new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.98f));
            art.preserveAspect = true;
            art.sprite = sprite;
            art.color = sprite == null ? new Color(1f, 1f, 1f, 0.1f) : Color.white;
            return art;
        }

        internal void SetTint(Color color)
        {
            for (int index = 0; index < tiles.Count; index++)
            {
                tiles[index].color = color;
            }
        }

        internal void SetAlpha(float alpha)
        {
            for (int index = 0; index < tiles.Count; index++)
            {
                Color color = tiles[index].color;
                tiles[index].color = new Color(color.r, color.g, color.b, alpha);
            }
        }

        internal void ResetTint()
        {
            SetTint(baseColor);
        }

        internal void SetRaycastTarget(bool value)
        {
            for (int index = 0; index < tiles.Count; index++)
            {
                tiles[index].raycastTarget = value;
            }
        }
    }
}
