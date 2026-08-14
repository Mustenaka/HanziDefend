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
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));

            var host = new GameObject(name, typeof(RectTransform), typeof(DeployCardVisual));
            host.transform.SetParent(parent, false);
            DeployCardVisual visual = host.GetComponent<DeployCardVisual>();
            visual.Build(footprint, metrics, color);
            return visual;
        }

        private void Build(UnitFootprint footprint, DeployGridMetrics metrics, Color color)
        {
            Footprint = footprint;
            baseColor = color;
            Rect = GetComponent<RectTransform>();
            Rect.anchorMin = new Vector2(0.5f, 0.5f);
            Rect.anchorMax = new Vector2(0.5f, 0.5f);
            Rect.pivot = new Vector2(0.5f, 0.5f);
            Rect.sizeDelta = metrics.FootprintSize(footprint);

            var tileSize = new Vector2(metrics.CellEdge, metrics.CellEdge);
            for (int index = 0; index < footprint.OccupiedOffsets.Count; index++)
            {
                GridCoordinate offset = footprint.OccupiedOffsets[index];
                Image tile = RuntimeUiFactory.CreateImage($"Tile {offset.Column},{offset.Row}", transform, color);
                RectTransform tileRect = tile.rectTransform;
                tileRect.anchorMin = new Vector2(0.5f, 0.5f);
                tileRect.anchorMax = new Vector2(0.5f, 0.5f);
                tileRect.pivot = new Vector2(0.5f, 0.5f);
                tileRect.sizeDelta = tileSize;
                tileRect.anchoredPosition = metrics.CellCentreInFootprint(footprint, offset);
                tiles.Add(tile);
                offsets.Add(offset);
            }
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
