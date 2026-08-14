using System;
using System.Linq;
using HanziDefend.Data;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode
{
    public sealed class DamageTypeMultiplierLookupTests
    {
        [Test]
        public void LoadedLookup_ReturnsEveryJsonMatrixValue()
        {
            EconomyDef economy = GameConfig.Load().Economy;

            foreach (TypeMultiplierDef entry in economy.Damage.TypeMultipliers)
            {
                Assert.That(
                    Formula.TypeMultiplier(entry.AtkType, entry.ArmorType, economy),
                    Is.EqualTo(entry.Value),
                    $"{entry.ArmorType}/{entry.AtkType}");
            }
        }

        [Test]
        public void GameConfigLoad_EagerlyBuildsLookupSnapshot()
        {
            EconomyDef economy = GameConfig.Load().Economy;
            TypeMultiplierDef entry = economy.Damage.TypeMultipliers
                .Single(value => value.ArmorType == ArmorType.Heavy && value.AtkType == AttackType.Arrow);
            float jsonValue = entry.Value;

            entry.Value = jsonValue + 100f;

            Assert.That(
                Formula.TypeMultiplier(AttackType.Arrow, ArmorType.Heavy, economy),
                Is.EqualTo(jsonValue),
                "A loaded config must use its precomputed snapshot instead of rescanning the JSON entries.");
        }

        [Test]
        public void DirectEconomy_LazilyBuildsLookupAndArrayReplacementInvalidatesIt()
        {
            EconomyDef economy = CreateEconomy(1.25f);

            Assert.That(
                Formula.TypeMultiplier(AttackType.Blunt, ArmorType.Light, economy),
                Is.EqualTo(1.25f));

            economy.Damage.TypeMultipliers = new[]
            {
                new TypeMultiplierDef
                {
                    ArmorType = ArmorType.Light,
                    AtkType = AttackType.Blunt,
                    Value = 1.75f
                }
            };

            Assert.That(
                Formula.TypeMultiplier(AttackType.Blunt, ArmorType.Light, economy),
                Is.EqualTo(1.75f));
        }

        [Test]
        public void BuildingLookup_DoesNotChangeSerializedJson()
        {
            EconomyDef economy = CreateEconomy(1.25f);
            string before = JsonCodec.Serialize(economy);

            Formula.TypeMultiplier(AttackType.Blunt, ArmorType.Light, economy);

            string after = JsonCodec.Serialize(economy);
            Assert.That(after, Is.EqualTo(before));
            Assert.That(after, Does.Not.Contain("typeMultiplierLookup"));
        }

        [Test]
        public void LazyLookup_PreservesMissingPairAndFirstDuplicateBehavior()
        {
            EconomyDef economy = CreateEconomy(1.25f);
            economy.Damage.TypeMultipliers = new[]
            {
                new TypeMultiplierDef
                {
                    ArmorType = ArmorType.Light,
                    AtkType = AttackType.Blunt,
                    Value = 1.25f
                },
                new TypeMultiplierDef
                {
                    ArmorType = ArmorType.Light,
                    AtkType = AttackType.Blunt,
                    Value = 9f
                }
            };

            Assert.That(
                Formula.TypeMultiplier(AttackType.Blunt, ArmorType.Light, economy),
                Is.EqualTo(1.25f));
            Assert.Throws<ArgumentException>(
                () => Formula.TypeMultiplier(AttackType.Siege, ArmorType.Building, economy));
        }

        private static EconomyDef CreateEconomy(float multiplier)
        {
            return new EconomyDef
            {
                Damage = new DamageFormulaDef
                {
                    NeutralTypeMultiplier = 1f,
                    TypeMultipliers = new[]
                    {
                        new TypeMultiplierDef
                        {
                            ArmorType = ArmorType.Light,
                            AtkType = AttackType.Blunt,
                            Value = multiplier
                        }
                    }
                },
                DropCoins = new DropCoinsDef()
            };
        }
    }
}
