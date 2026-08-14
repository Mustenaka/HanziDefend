using System;
using System.Collections.Generic;
using HanziDefend.Data;

namespace HanziDefend.Gameplay.Deploy
{
    /// <summary>
    /// Data-driven unit-card eligibility and the one-time pre-boss equipment guarantee.
    /// A unit is offered exactly while the unlocked region can still geometrically hold its
    /// footprint, so shape availability follows from the unlock mask instead of a column threshold.
    /// </summary>
    public static class UnitCardPoolPolicy
    {
        public static IReadOnlyList<UnitDef> GetEligibleUnits(GameConfig config, DeploymentGrid grid)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            RequireRules(config);
            var eligible = new List<UnitDef>(config.AllyUnits.Count);

            for (int unitIndex = 0; unitIndex < config.AllyUnits.Count; unitIndex++)
            {
                UnitDef unit = config.AllyUnits[unitIndex];
                if (grid.HasUnlockedPlacement(UnitFootprint.FromDefinition(unit)))
                {
                    eligible.Add(unit);
                }
            }

            return eligible.ToArray();
        }

        /// <summary>
        /// Records a naturally generated unit offer so the pre-boss guarantee is
        /// suppressed when the player has already seen a qualifying equipment card.
        /// </summary>
        public static void RecordOfferedUnit(GameConfig config, RunState runState, UnitDef unit)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (runState == null)
            {
                throw new ArgumentNullException(nameof(runState));
            }

            if (unit == null)
            {
                throw new ArgumentNullException(nameof(unit));
            }

            CardPoolRulesDef rules = RequireRules(config);
            if (unit.AtkType == rules.GuaranteeAttackType)
            {
                runState.HasOfferedGuaranteedMachineryCard = true;
            }
        }

        /// <summary>
        /// Draws the reserved equipment-card slot on the stage immediately before
        /// the configured boss stage. Returns false when no guarantee is due, or when the
        /// unlocked region cannot hold any equipment footprint yet.
        /// </summary>
        public static bool TryDrawGuaranteedCard(
            GameConfig config,
            RunState runState,
            DeploymentGrid grid,
            Rng cardDrawRng,
            out UnitDef unit)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (runState == null)
            {
                throw new ArgumentNullException(nameof(runState));
            }

            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (cardDrawRng == null)
            {
                throw new ArgumentNullException(nameof(cardDrawRng));
            }

            unit = null;
            CardPoolRulesDef rules = RequireRules(config);
            bool isPreBossStage = runState.StageIndex + 1 == rules.GuaranteeBeforeStageIndex;
            if (!isPreBossStage || runState.HasOfferedGuaranteedMachineryCard)
            {
                return false;
            }

            IReadOnlyList<UnitDef> eligible = GetEligibleUnits(config, grid);
            var guaranteedCandidates = new List<UnitDef>();
            for (int index = 0; index < eligible.Count; index++)
            {
                UnitDef candidate = eligible[index];
                if (candidate.AtkType == rules.GuaranteeAttackType)
                {
                    guaranteedCandidates.Add(candidate);
                }
            }

            // The unlock mask can legitimately be too small for every equipment footprint on an
            // early stage. That is a game state, not a data error, so the guarantee simply waits.
            if (guaranteedCandidates.Count == 0)
            {
                return false;
            }

            int selectedIndex = guaranteedCandidates.Count == 1
                ? 0
                : cardDrawRng.NextInt(guaranteedCandidates.Count);
            unit = guaranteedCandidates[selectedIndex];
            RecordOfferedUnit(config, runState, unit);
            return true;
        }

        private static CardPoolRulesDef RequireRules(GameConfig config)
        {
            CardPoolRulesDef rules = config.Economy?.CardPool;
            if (rules == null)
            {
                throw new InvalidOperationException("economy.cardPool is required.");
            }

            if (rules.ShapeUnlocks == null || rules.ShapeUnlocks.Length == 0)
            {
                throw new InvalidOperationException("economy.cardPool.shapeUnlocks must contain entries.");
            }

            return rules;
        }
    }
}
