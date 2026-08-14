using System;
using System.Collections.Generic;
using HanziDefend.Data;

namespace HanziDefend.Gameplay.Deploy
{
    /// <summary>
    /// Mints the seven cell-unlock card shapes. All three unlock channels (rewarded ad, card pool
    /// draw and coin purchase) go through <see cref="Draw"/>, so a card can only ever come from a
    /// seeded <see cref="Rng"/> stream and the same seed always yields the same sequence of cards.
    /// </summary>
    public static class UnlockCardFactory
    {
        private static readonly UnlockCardShape[] DrawableShapes =
        {
            UnlockCardShape.OneByOne,
            UnlockCardShape.TwoByOne,
            UnlockCardShape.OneByTwo,
            UnlockCardShape.TwoByTwo,
            UnlockCardShape.TwoByTwoNotched,
            UnlockCardShape.ThreeByOne,
            UnlockCardShape.OneByThree
        };

        private static readonly GridCorner[] Corners =
        {
            GridCorner.LowerLeft,
            GridCorner.LowerRight,
            GridCorner.UpperLeft,
            GridCorner.UpperRight
        };

        private static readonly IReadOnlyList<UnlockCardShape> ShapeView = Array.AsReadOnly(DrawableShapes);
        private static readonly IReadOnlyList<GridCorner> CornerView = Array.AsReadOnly(Corners);

        /// <summary>The seven shapes an unlock card can take, in a stable order.</summary>
        public static IReadOnlyList<UnlockCardShape> Shapes => ShapeView;

        public static IReadOnlyList<GridCorner> NotchCorners => CornerView;

        /// <summary>Draws one uniformly random card; a notched shape also rolls its corner.</summary>
        public static UnlockCard Draw(Rng rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            UnlockCardShape shape = DrawableShapes[rng.NextInt(DrawableShapes.Length)];
            GridCorner notch = shape == UnlockCardShape.TwoByTwoNotched
                ? Corners[rng.NextInt(Corners.Length)]
                : GridCorner.LowerLeft;
            return Create(shape, notch);
        }

        public static UnlockCard Create(UnlockCardShape shape, GridCorner notch = GridCorner.LowerLeft)
        {
            return new UnlockCard(shape, notch, BuildFootprint(shape, notch));
        }

        /// <summary>Rebuilds a card from the id emitted by <see cref="UnlockCard.ContentId"/>.</summary>
        public static bool TryParse(string contentId, out UnlockCard card)
        {
            card = null;
            if (string.IsNullOrWhiteSpace(contentId))
            {
                return false;
            }

            for (int shapeIndex = 0; shapeIndex < DrawableShapes.Length; shapeIndex++)
            {
                UnlockCardShape shape = DrawableShapes[shapeIndex];
                if (shape != UnlockCardShape.TwoByTwoNotched)
                {
                    if (string.Equals(contentId, $"unlock_{shape}", StringComparison.Ordinal))
                    {
                        card = Create(shape);
                        return true;
                    }

                    continue;
                }

                for (int cornerIndex = 0; cornerIndex < Corners.Length; cornerIndex++)
                {
                    GridCorner corner = Corners[cornerIndex];
                    if (string.Equals(contentId, $"unlock_{shape}_{corner}", StringComparison.Ordinal))
                    {
                        card = Create(shape, corner);
                        return true;
                    }
                }
            }

            return false;
        }

        private static UnitFootprint BuildFootprint(UnlockCardShape shape, GridCorner notch)
        {
            switch (shape)
            {
                case UnlockCardShape.OneByOne:
                    return UnitFootprint.Rectangle(1, 1);
                case UnlockCardShape.TwoByOne:
                    return UnitFootprint.Rectangle(2, 1);
                case UnlockCardShape.OneByTwo:
                    return UnitFootprint.Rectangle(1, 2);
                case UnlockCardShape.TwoByTwo:
                    return UnitFootprint.Rectangle(2, 2);
                case UnlockCardShape.ThreeByOne:
                    return UnitFootprint.Rectangle(3, 1);
                case UnlockCardShape.OneByThree:
                    return UnitFootprint.Rectangle(1, 3);
                case UnlockCardShape.TwoByTwoNotched:
                    return BuildNotchedFootprint(notch);
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown unlock card shape.");
            }
        }

        private static UnitFootprint BuildNotchedFootprint(GridCorner notch)
        {
            GridCoordinate removed = NotchOffset(notch);
            var occupied = new List<GridCoordinate>(3);
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < 2; column++)
                {
                    var cell = new GridCoordinate(column, row);
                    if (cell != removed)
                    {
                        occupied.Add(cell);
                    }
                }
            }

            return new UnitFootprint(occupied);
        }

        private static GridCoordinate NotchOffset(GridCorner notch)
        {
            switch (notch)
            {
                case GridCorner.LowerLeft:
                    return new GridCoordinate(0, 0);
                case GridCorner.LowerRight:
                    return new GridCoordinate(1, 0);
                case GridCorner.UpperLeft:
                    return new GridCoordinate(0, 1);
                case GridCorner.UpperRight:
                    return new GridCoordinate(1, 1);
                default:
                    throw new ArgumentOutOfRangeException(nameof(notch), notch, "Unknown grid corner.");
            }
        }
    }
}
