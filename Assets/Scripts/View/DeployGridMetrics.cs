using System;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using UnityEngine;

namespace HanziDefend.View
{
    /// <summary>
    /// The single measure the deployment screen is built from. Grid cells, hand cards and the drag
    /// ghost all derive their size from <see cref="CellEdge"/>, so a card that reads as "2x2" is
    /// literally two cells wide by two cells tall. Nothing in the deploy screen may invent its own
    /// size constants — that is what made hand cards uniform strips before WO-C4.
    /// </summary>
    internal readonly struct DeployGridMetrics
    {
        internal DeployGridMetrics(float cellEdge, float spacing)
        {
            if (cellEdge <= 0f) throw new ArgumentOutOfRangeException(nameof(cellEdge));
            if (spacing < 0f) throw new ArgumentOutOfRangeException(nameof(spacing));
            CellEdge = cellEdge;
            Spacing = spacing;
        }

        /// <summary>Edge of one grid cell. Square by construction, so footprint aspect ratios hold.</summary>
        internal float CellEdge { get; }

        internal float Spacing { get; }

        /// <summary>Centre-to-centre distance between neighbouring cells.</summary>
        internal float Step => CellEdge + Spacing;

        /// <summary>
        /// Fits a square cell edge into the available rect for a columns x rows field. The smaller
        /// of the two axes wins so the whole field stays inside the panel.
        /// </summary>
        internal static DeployGridMetrics Fit(Vector2 available, int columns, int rows, float spacingRatio)
        {
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            if (spacingRatio < 0f) throw new ArgumentOutOfRangeException(nameof(spacingRatio));

            float horizontalUnits = columns + ((columns - 1) * spacingRatio);
            float verticalUnits = rows + ((rows - 1) * spacingRatio);
            float edge = Mathf.Min(available.x / horizontalUnits, available.y / verticalUnits);
            edge = Mathf.Max(1f, edge);
            return new DeployGridMetrics(edge, edge * spacingRatio);
        }

        /// <summary>Scales the metric for hand cards, which are drawn smaller than the live grid.</summary>
        internal DeployGridMetrics Scaled(float scale)
        {
            if (scale <= 0f) throw new ArgumentOutOfRangeException(nameof(scale));
            return new DeployGridMetrics(CellEdge * scale, Spacing * scale);
        }

        /// <summary>Total size of a footprint's bounding box, gaps included.</summary>
        internal Vector2 FootprintSize(UnitFootprint footprint)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            return new Vector2(SpanLength(footprint.Width), SpanLength(footprint.Height));
        }

        /// <summary>Length occupied by <paramref name="cellCount"/> cells including inner gaps.</summary>
        internal float SpanLength(int cellCount)
        {
            if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
            return (cellCount * CellEdge) + ((cellCount - 1) * Spacing);
        }

        /// <summary>
        /// Centre of one footprint cell relative to the bounding box centre. Row 0 is the bottom
        /// row, matching <see cref="GridCoordinate"/>, so a notched corner lands where the data says.
        /// </summary>
        internal Vector2 CellCentreInFootprint(UnitFootprint footprint, GridCoordinate offset)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            float x = (offset.Column - footprint.MinColumnOffset - ((footprint.Width - 1) * 0.5f)) * Step;
            float y = (offset.Row - footprint.MinRowOffset - ((footprint.Height - 1) * 0.5f)) * Step;
            return new Vector2(x, y);
        }

        /// <summary>Centre of a field cell relative to the field's centre.</summary>
        internal Vector2 CellCentreInField(GridCoordinate cell, int columns, int rows)
        {
            float x = (cell.Column - ((columns - 1) * 0.5f)) * Step;
            float y = (cell.Row - ((rows - 1) * 0.5f)) * Step;
            return new Vector2(x, y);
        }

        /// <summary>
        /// Inverse of <see cref="CellCentreInField"/>: the nearest cell to a field-local point, plus
        /// how far off centre that point was, measured in cell edges. Purely geometric — legality is
        /// never decided here, only which cell the cursor is pointing at.
        /// </summary>
        internal GridCoordinate NearestCell(Vector2 fieldLocalPoint, int columns, int rows, out float distanceInCells)
        {
            float columnFloat = (fieldLocalPoint.x / Step) + ((columns - 1) * 0.5f);
            float rowFloat = (fieldLocalPoint.y / Step) + ((rows - 1) * 0.5f);
            int column = Mathf.RoundToInt(columnFloat);
            int row = Mathf.RoundToInt(rowFloat);
            distanceInCells = new Vector2(columnFloat - column, rowFloat - row).magnitude;
            return new GridCoordinate(column, row);
        }
    }
}
