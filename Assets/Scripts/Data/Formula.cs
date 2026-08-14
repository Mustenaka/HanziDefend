using System;
using System.Collections.Generic;

namespace HanziDefend.Data
{
    public enum EnemyRank
    {
        Normal,
        Elite,
        Boss
    }

    /// <summary>
    /// M1-00 section 3.3 formulas. Changes require a contract revision.
    /// </summary>
    public static class Formula
    {
        public static float EffectiveArmor(float armor, float pierce)
        {
            return Math.Max(0f, armor - pierce);
        }

        public static float Mitigation(float armor, float pierce, float armorScale)
        {
            if (armorScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(armorScale), "Armor scale must be positive.");
            }

            float effectiveArmor = EffectiveArmor(armor, pierce);
            return effectiveArmor / (effectiveArmor + armorScale);
        }

        public static float StatAtLevel(float baseValue, float growth, int level)
        {
            if (level < 1 || level > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(level), "Level must be in [1, 4].");
            }

            return baseValue * (1f + growth * (level - 1));
        }

        public static int RefreshCost(int refreshCount, int baseCost, int growthPerRefresh)
        {
            if (refreshCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(refreshCount),
                    "Refresh count must be non-negative.");
            }

            if (baseCost < 0 || growthPerRefresh < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(baseCost),
                    "Refresh cost parameters must be non-negative.");
            }

            return checked(baseCost + growthPerRefresh * refreshCount);
        }

        public static int DropCoins(
            EnemyRank rank,
            int normalCoins,
            int eliteCoins,
            int bossCoins)
        {
            switch (rank)
            {
                case EnemyRank.Normal:
                    return normalCoins;
                case EnemyRank.Elite:
                    return eliteCoins;
                case EnemyRank.Boss:
                    return bossCoins;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rank), rank, "Unknown enemy rank.");
            }
        }

        public static float Mitigation(float armor, float pierce, EconomyDef economy)
        {
            RequireEconomy(economy);
            return Mitigation(armor, pierce, economy.Damage.ArmorScale);
        }

        public static int Damage(float atk, float armor, float pierce, EconomyDef economy)
        {
            return Damage(
                atk,
                AttackType.None,
                Array.Empty<BonusVsDef>(),
                armor,
                ArmorType.Unarmored,
                UnitType.Infantry,
                pierce,
                economy);
        }

        /// <summary>
        /// M1-04 two-layer damage: armor/attack multiplier, then additive matchup bonus,
        /// then numeric armor mitigation.
        /// </summary>
        public static int Damage(
            float atk,
            AttackType atkType,
            IReadOnlyList<BonusVsDef> bonusVs,
            float armor,
            ArmorType armorType,
            UnitType targetUnitType,
            float pierce,
            EconomyDef economy)
        {
            RequireEconomy(economy);
            if (bonusVs == null)
            {
                throw new ArgumentNullException(nameof(bonusVs));
            }

            float typeMultiplier = TypeMultiplier(atkType, armorType, economy);
            double bonus = 0d;
            for (int index = 0; index < bonusVs.Count; index++)
            {
                BonusVsDef entry = bonusVs[index]
                    ?? throw new ArgumentException("A bonusVs entry cannot be null.", nameof(bonusVs));
                if (Matches(entry.Target, armorType, targetUnitType))
                {
                    bonus += entry.Value;
                }
            }

            double rawDamage = (atk * typeMultiplier + bonus)
                               * (1d - Mitigation(armor, pierce, economy));
            int roundedDamage = (int)Math.Round(rawDamage, MidpointRounding.AwayFromZero);
            return Math.Max(economy.Damage.MinimumDamage, roundedDamage);
        }

        public static float TypeMultiplier(AttackType atkType, ArmorType armorType, EconomyDef economy)
        {
            RequireEconomy(economy);
            if (atkType == AttackType.None)
            {
                return economy.Damage.NeutralTypeMultiplier;
            }

            if (atkType == AttackType.Unknown)
            {
                throw new ArgumentOutOfRangeException(nameof(atkType), atkType, "Attack type is unknown.");
            }

            if (armorType == ArmorType.Unknown)
            {
                throw new ArgumentOutOfRangeException(nameof(armorType), armorType, "Armor type is unknown.");
            }

            if (economy.Damage.TryGetTypeMultiplier(atkType, armorType, out float multiplier))
            {
                return multiplier;
            }

            throw new ArgumentException(
                $"No type multiplier is configured for {armorType} armor versus {atkType} attack.",
                nameof(economy));
        }

        public static int RefreshCost(int refreshCount, EconomyDef economy)
        {
            RequireEconomy(economy);
            return RefreshCost(refreshCount, economy.RefreshBaseCost, economy.RefreshCostGrowth);
        }

        /// <summary>Price of the next coin-bought unlock card. Same linear shape as the refresh cost.</summary>
        public static int UnlockPurchaseCost(int purchaseCount, int baseCost, int growthPerPurchase)
        {
            if (purchaseCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(purchaseCount),
                    "Unlock purchase count must be non-negative.");
            }

            if (baseCost < 0 || growthPerPurchase < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(baseCost),
                    "Unlock purchase cost parameters must be non-negative.");
            }

            return checked(baseCost + growthPerPurchase * purchaseCount);
        }

        public static int UnlockPurchaseCost(int purchaseCount, EconomyDef economy)
        {
            RequireEconomy(economy);
            if (economy.GridUnlock == null)
            {
                throw new ArgumentException("economy.gridUnlock is required.", nameof(economy));
            }

            return UnlockPurchaseCost(
                purchaseCount,
                economy.GridUnlock.PurchaseBaseCost,
                economy.GridUnlock.PurchaseCostGrowth);
        }

        public static int DropCoins(EnemyRank rank, EconomyDef economy)
        {
            RequireEconomy(economy);
            return DropCoins(
                rank,
                economy.DropCoins.Normal,
                economy.DropCoins.Elite,
                economy.DropCoins.Boss);
        }

        private static void RequireEconomy(EconomyDef economy)
        {
            if (economy == null)
            {
                throw new ArgumentNullException(nameof(economy));
            }

            if (economy.Damage == null || economy.DropCoins == null)
            {
                throw new ArgumentException("Economy formula configuration is incomplete.", nameof(economy));
            }

            if (economy.Damage.TypeMultipliers == null)
            {
                throw new ArgumentException("Economy damage type multipliers are missing.", nameof(economy));
            }
        }

        private static bool Matches(BonusTarget target, ArmorType armorType, UnitType unitType)
        {
            switch (target)
            {
                case BonusTarget.Cavalry:
                    return unitType == UnitType.Cavalry;
                case BonusTarget.HeavyArmor:
                    return armorType == ArmorType.Heavy;
                case BonusTarget.Building:
                    return armorType == ArmorType.Building || unitType == UnitType.Building;
                case BonusTarget.Unknown:
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown bonus target.");
            }
        }

    }
}
