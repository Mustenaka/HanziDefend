using System;
using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Deploy
{
    /// <summary>
    /// The single mapping from a deployment-grid anchor to the battlefield position that anchor
    /// spawns at.
    ///
    /// <para>It exists because there used to be two. The live flow fanned the unlocked columns
    /// across <c>deploymentSpreadWidth</c> world units, while the balance harness spaced them by
    /// <c>deploymentCellSize.x</c> — so a lineup that reads as a wide line in the game formed a
    /// narrow column in the simulation, and every measured number described a formation the player
    /// never sees. WO-F1 §C is about the reference model matching the real game, and a spawn map
    /// that disagrees with the game defeats that before any number is measured.</para>
    /// </summary>
    public static class DeploymentSpawnMap
    {
        /// <summary>
        /// Leftmost and rightmost unlocked columns of a row-major unlock mask. Falls back to the
        /// whole field width when the mask is missing or malformed.
        /// </summary>
        public static void ResolveUnlockedColumnSpan(
            bool[] unlockedCells,
            int width,
            int height,
            out int firstColumn,
            out int lastColumn)
        {
            firstColumn = int.MaxValue;
            lastColumn = int.MinValue;

            if (unlockedCells != null
                && width > 0
                && height > 0
                && unlockedCells.Length == checked(width * height))
            {
                for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    if (!unlockedCells[(row * width) + column])
                    {
                        continue;
                    }

                    if (column < firstColumn) firstColumn = column;
                    if (column > lastColumn) lastColumn = column;
                }
            }

            if (firstColumn > lastColumn)
            {
                firstColumn = 0;
                lastColumn = Math.Max(0, width - 1);
            }
        }

        /// <summary>
        /// Battlefield position for one deployed unit.
        ///
        /// <para>X is a mapping, not a scatter: put a unit on the left of the grid and it comes out
        /// on the left of the field, which is a strategic dimension worth keeping. The unlocked
        /// column span is stretched to fill <c>deploymentSpreadWidth</c> whatever its width, so
        /// three unlocked columns spread wide and seven spread tight — either way the deployment
        /// reads as a line across the base rather than a single point.</para>
        /// </summary>
        public static Vector2 Resolve(
            BattleRulesDef rules,
            int firstUnlockedColumn,
            int lastUnlockedColumn,
            int column,
            int row)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            Position2Def basePosition = rules.AllyBasePosition;
            Position2Def originOffset = rules.DeploymentOriginOffset;
            Position2Def cellSize = rules.DeploymentCellSize;
            float x = basePosition.X
                      + originOffset.X
                      + SpreadOffset(
                          column,
                          firstUnlockedColumn,
                          lastUnlockedColumn,
                          rules.DeploymentSpreadWidth);
            float y = basePosition.Y + originOffset.Y + row * cellSize.Y;
            return new Vector2(x, y);
        }

        private static float SpreadOffset(int column, int firstColumn, int lastColumn, float spreadWidth)
        {
            int span = lastColumn - firstColumn;
            if (span <= 0)
            {
                return 0f;
            }

            float normalized = Mathf.Clamp01((column - firstColumn) / (float)span);
            return (normalized - 0.5f) * spreadWidth;
        }
    }
}
