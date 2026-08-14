using System;

namespace HanziDefend.Gameplay.Performance
{
    /// <summary>
    /// Builds a deterministic retarget deadline from an entity id. The schedule is pure:
    /// it does not read or advance any RNG stream, so replay event order remains reproducible.
    /// </summary>
    public static class RetargetPhaseSchedule
    {
        /// <summary>
        /// Resolves one phase slot per fixed tick in the retarget interval. This keeps the
        /// phase count derived from GameData timing rather than introducing another tuning value.
        /// </summary>
        public static int ResolvePhaseCount(
            double retargetIntervalSeconds,
            double fixedDeltaTimeSeconds)
        {
            RequirePositiveFinite(retargetIntervalSeconds, nameof(retargetIntervalSeconds));
            RequirePositiveFinite(fixedDeltaTimeSeconds, nameof(fixedDeltaTimeSeconds));

            double phaseCount = Math.Round(
                retargetIntervalSeconds / fixedDeltaTimeSeconds,
                MidpointRounding.AwayFromZero);
            if (phaseCount >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return Math.Max(1, (int)phaseCount);
        }

        public static int GetPhaseSlot(int entityId, int phaseCount)
        {
            if (entityId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entityId),
                    "Entity id must be positive.");
            }

            if (phaseCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(phaseCount),
                    "Phase count must be positive.");
            }

            return entityId % phaseCount;
        }

        public static double GetPhaseOffsetSeconds(
            int entityId,
            double retargetIntervalSeconds,
            int phaseCount)
        {
            RequirePositiveFinite(retargetIntervalSeconds, nameof(retargetIntervalSeconds));
            int slot = GetPhaseSlot(entityId, phaseCount);
            return slot * (retargetIntervalSeconds / phaseCount);
        }

        /// <summary>
        /// Returns now + interval + the EntityId-derived phase offset requested by WO-E3.
        /// </summary>
        public static double GetNextDeadline(
            double nowSeconds,
            int entityId,
            double retargetIntervalSeconds,
            int phaseCount)
        {
            RequireNonNegativeFinite(nowSeconds, nameof(nowSeconds));
            return nowSeconds
                   + retargetIntervalSeconds
                   + GetPhaseOffsetSeconds(entityId, retargetIntervalSeconds, phaseCount);
        }

        /// <summary>
        /// Convenience overload for BattleSystem: derives k from the configured retarget
        /// interval and fixed tick duration, then returns the phased deadline.
        /// </summary>
        public static double GetNextDeadline(
            double nowSeconds,
            int entityId,
            double retargetIntervalSeconds,
            double fixedDeltaTimeSeconds)
        {
            int phaseCount = ResolvePhaseCount(
                retargetIntervalSeconds,
                fixedDeltaTimeSeconds);
            return GetNextDeadline(
                nowSeconds,
                entityId,
                retargetIntervalSeconds,
                phaseCount);
        }

        private static void RequirePositiveFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Value must be finite and positive.");
            }
        }

        private static void RequireNonNegativeFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Value must be finite and non-negative.");
            }
        }
    }
}
