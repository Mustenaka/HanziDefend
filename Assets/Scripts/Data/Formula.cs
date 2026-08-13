using System;

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

        public static int Damage(
            float atk,
            float armor,
            float pierce,
            float armorScale,
            int minimumDamage)
        {
            if (minimumDamage < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumDamage), "Minimum damage must be positive.");
            }

            double rawDamage = atk * (1d - Mitigation(armor, pierce, armorScale));
            int roundedDamage = (int)Math.Round(rawDamage, MidpointRounding.AwayFromZero);
            return Math.Max(minimumDamage, roundedDamage);
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
            RequireEconomy(economy);
            return Damage(
                atk,
                armor,
                pierce,
                economy.Damage.ArmorScale,
                economy.Damage.MinimumDamage);
        }

        public static int RefreshCost(int refreshCount, EconomyDef economy)
        {
            RequireEconomy(economy);
            return RefreshCost(refreshCount, economy.RefreshBaseCost, economy.RefreshCostGrowth);
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
        }

    }
}
