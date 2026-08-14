using System;
using System.Linq;
using HanziDefend.Data;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode
{
    public sealed class FormulaTests
    {
        [TestCase(100f, 0f, 0f, 100)]
        [TestCase(100f, 20f, 50f, 100)]
        [TestCase(100f, 100f, 0f, 50)]
        [TestCase(100f, 50f, 20f, 77)]
        [TestCase(2.5f, 0f, 0f, 3)]
        [TestCase(5f, 100f, 0f, 3)]
        [TestCase(0f, 0f, 0f, 1)]
        [TestCase(1f, 900f, 0f, 1)]
        public void Damage_FollowsLockedFormula(
            float atk,
            float armor,
            float pierce,
            int expected)
        {
            EconomyDef economy = GameConfig.Load().Economy;
            Assert.That(
                Formula.Damage(
                    atk,
                    AttackType.None,
                    Array.Empty<BonusVsDef>(),
                    armor,
                    ArmorType.Unarmored,
                    UnitType.Infantry,
                    pierce,
                    economy),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// Every locked attacker/defender pair from M1-04. Expected damage is derived by hand from
        /// M1-04 §2.1 (counter matrix), §2.2 (bonusVs) and §2.3 (damage formula) against the stat
        /// tables in M1-04 §3 — never read back from this implementation.
        ///
        /// The first nine rows are the original M1-04 §3.4 counter relationships. The rest exist so
        /// that <b>every combatant that owns an atk value appears at least once as the ATTACKER</b>.
        /// That invariant is enforced by
        /// <see cref="Damage_EveryAttackCapableCombatantIsCoveredAsAnAttacker"/>: before WO-C6 the
        /// nine rows only ever used zqi as a defender, so changing zqi's atk from 105 to 45 turned
        /// nothing red. Any atk edit must now break a test.
        /// </summary>
        private static readonly (string Attacker, string Defender, int Damage, float Net)[]
            LockedCounterRelationships =
            {
                // --- M1-04 §3.4, the nine original relationships (values unchanged) ---
                ("gong", "mao", 42, 2.000f),
                ("gong", "tie", 8, 0.500f),
                ("nub", "tie", 297, 4.471f),
                ("nub", "zqi", 322, 4.471f),
                ("nuc", "tie", 904, 4.000f),
                ("qqi", "gong", 124, 2.000f),
                ("mao", "qqi", 104, 3.318f),
                ("mao", "zqi", 61, 2.318f),
                ("gong", "nub", 43, 2.000f),

                // --- WO-C6: attacker-side coverage for everything the nine rows missed ---
                ("zu", "gong", 27, 2.000f),          // Slash vs Unarmored 2.0x
                ("dun", "zqi", 37, 2.000f),          // Blunt vs Heavy 2.0x
                ("dao", "qqi", 140, 1.500f),         // Slash vs Light 1.5x
                ("zqi", "gong", 90, 2.000f),         // the blind spot WO-C6 closes
                ("tie", "zqi", 59, 0.500f),          // Slash vs Heavy 0.5x
                ("lia", "tie", 217, 2.000f),         // Blunt vs Heavy 2.0x
                ("nuc", "bld_cheng", 800, 3.538f),   // Siege 2.0x + 400 vs Building
                ("chc", "bld_cheng", 1500, 5.000f),  // Siege 2.0x + 900 vs Building
                ("e_lang", "gong", 294, 2.000f),
                ("e_liu", "mao", 47, 2.000f),
                ("e_shan", "zqi", 54, 2.000f),
                ("bld_cheng", "nuc", 30, 0.500f)     // the castle's own return fire
            };

        /// <summary>
        /// Aura units carry no atk at all (M1-04 §3.1 lists their ATK as "—", and units.json stores
        /// 0), so they have no attacker-side damage to lock. They are the only exemptions.
        /// </summary>
        private static readonly string[] AtkFreeAuraUnits = { "huo", "bing" };

        private static System.Collections.Generic.IEnumerable<TestCaseData> CounterRelationshipCases()
        {
            foreach ((string attacker, string defender, int damage, float net) in LockedCounterRelationships)
            {
                yield return new TestCaseData(attacker, defender, damage, net)
                    .SetName($"Damage_M104LockedCounter({attacker}->{defender})");
            }
        }

        [TestCaseSource(nameof(CounterRelationshipCases))]
        public void Damage_M104LockedCounterRelationships(
            string attackerId,
            string defenderId,
            int expectedDamage,
            float expectedNetMultiplier)
        {
            GameConfig config = GameConfig.Load();
            Combatant attacker = ResolveCombatant(config, attackerId);
            Combatant defender = ResolveCombatant(config, defenderId);

            int actual = Formula.Damage(
                attacker.Atk,
                attacker.AtkType,
                attacker.BonusVs,
                defender.Armor,
                defender.ArmorType,
                defender.UnitType,
                attacker.Pierce,
                config.Economy);
            float netMultiplier = NetCounterMultiplier(attacker, defender, config.Economy);

            Assert.That(actual, Is.EqualTo(expectedDamage),
                $"{attackerId}->{defenderId} damage drifted from the M1-04 derivation.");
            Assert.That(netMultiplier, Is.EqualTo(expectedNetMultiplier).Within(0.01f),
                $"{attackerId}->{defenderId} net counter multiplier drifted from M1-04 §2.1/§2.2.");
        }

        /// <summary>
        /// The coverage guard itself: if a new unit or boss gains an atk value, or an existing one
        /// is retuned, it must be represented on the attacker side of the locked table above.
        /// Without this, the locked table can silently stop covering the roster.
        /// </summary>
        [Test]
        public void Damage_EveryAttackCapableCombatantIsCoveredAsAnAttacker()
        {
            GameConfig config = GameConfig.Load();
            var covered = LockedCounterRelationships
                .Select(value => value.Attacker)
                .ToHashSet(StringComparer.Ordinal);

            var attackCapable = config.Units
                .Where(unit => unit.Atk.Base > 0f)
                .Select(unit => unit.Id)
                .Concat(config.Bosses.Where(boss => boss.Atk > 0f).Select(boss => boss.Id))
                .ToArray();

            Assert.That(attackCapable, Is.Not.Empty);
            Assert.That(
                attackCapable.Where(id => !covered.Contains(id)),
                Is.Empty,
                "Every combatant with an atk value must appear as an attacker in "
                + "LockedCounterRelationships, otherwise retuning its atk turns no test red.");

            foreach (string auraId in AtkFreeAuraUnits)
            {
                Assert.That(config.GetUnit(auraId).Atk.Base, Is.Zero,
                    $"'{auraId}' is exempt from attacker coverage only while it carries no atk.");
            }

            Assert.That(covered.Count, Is.EqualTo(attackCapable.Length),
                "The locked table must not name an attacker that no longer exists.");
        }

        [Test]
        public void Damage_NoneAttackType_UsesJsonNeutralMultiplier()
        {
            EconomyDef economy = GameConfig.Load().Economy;
            Assert.That(
                Formula.Damage(100f, AttackType.None, Array.Empty<BonusVsDef>(),
                    0f, ArmorType.Building, UnitType.Building, 0f, economy),
                Is.EqualTo(100));
        }

        [TestCase(1, 100f)]
        [TestCase(2, 125f)]
        [TestCase(3, 150f)]
        [TestCase(4, 175f)]
        public void StatAtLevel_ReturnsAllFourLockedLevels(int level, float expected)
        {
            Assert.That(Formula.StatAtLevel(100f, 0.25f, level), Is.EqualTo(expected).Within(0.0001f));
        }

        [TestCase(0, 15)]
        [TestCase(1, 20)]
        [TestCase(4, 35)]
        public void RefreshCost_FollowsLockedLinearCurve(int refreshCount, int expected)
        {
            Assert.That(Formula.RefreshCost(refreshCount, 15, 5), Is.EqualTo(expected));
        }

        [TestCase(EnemyRank.Normal, 1)]
        [TestCase(EnemyRank.Elite, 3)]
        [TestCase(EnemyRank.Boss, 20)]
        public void DropCoins_FollowsLockedRankValues(EnemyRank rank, int expected)
        {
            Assert.That(Formula.DropCoins(rank, 1, 3, 20), Is.EqualTo(expected));
        }

        [TestCase(50f, 20f, 30f)]
        [TestCase(20f, 50f, 0f)]
        public void EffectiveArmor_NeverDropsBelowZero(float armor, float pierce, float expected)
        {
            Assert.That(Formula.EffectiveArmor(armor, pierce), Is.EqualTo(expected));
        }

        [Test]
        public void Formula_UsesProvidedEconomyConstants()
        {
            EconomyDef economy = GameConfig.Load().Economy;

            Assert.That(Formula.RefreshCost(2, 100, 7), Is.EqualTo(114));

            economy.Damage.ArmorScale = 200f;
            economy.Damage.MinimumDamage = 1;
            Assert.That(
                Formula.Damage(
                    100f,
                    AttackType.None,
                    Array.Empty<BonusVsDef>(),
                    100f,
                    ArmorType.Unarmored,
                    UnitType.Infantry,
                    0f,
                    economy),
                Is.EqualTo(67));

            economy.Damage.ArmorScale = 100f;
            economy.Damage.MinimumDamage = 4;
            Assert.That(
                Formula.Damage(
                    0f,
                    AttackType.None,
                    Array.Empty<BonusVsDef>(),
                    0f,
                    ArmorType.Unarmored,
                    UnitType.Infantry,
                    0f,
                    economy),
                Is.EqualTo(4));
            Assert.That(Formula.DropCoins(EnemyRank.Normal, 9, 11, 27), Is.EqualTo(9));
        }

        [TestCase(0)]
        [TestCase(5)]
        public void StatAtLevel_RejectsLevelsOutsideContract(int level)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Formula.StatAtLevel(100f, 0.25f, level));
        }

        [Test]
        public void RefreshCost_RejectsNegativeRefreshCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Formula.RefreshCost(-1, 15, 5));
        }

        /// <summary>
        /// Flattens <see cref="UnitDef"/> and <see cref="BossDef"/> into the fields the damage
        /// formula actually reads, so the castle can be locked as both attacker and defender.
        /// </summary>
        private readonly struct Combatant
        {
            internal Combatant(
                float atk,
                AttackType atkType,
                float pierce,
                BonusVsDef[] bonusVs,
                float armor,
                ArmorType armorType,
                UnitType unitType)
            {
                Atk = atk;
                AtkType = atkType;
                Pierce = pierce;
                BonusVs = bonusVs ?? Array.Empty<BonusVsDef>();
                Armor = armor;
                ArmorType = armorType;
                UnitType = unitType;
            }

            internal float Atk { get; }
            internal AttackType AtkType { get; }
            internal float Pierce { get; }
            internal BonusVsDef[] BonusVs { get; }
            internal float Armor { get; }
            internal ArmorType ArmorType { get; }
            internal UnitType UnitType { get; }
        }

        private static Combatant ResolveCombatant(GameConfig config, string id)
        {
            if (config.UnitsById.TryGetValue(id, out UnitDef unit))
            {
                return new Combatant(
                    unit.Atk.Base,
                    unit.AtkType,
                    unit.Pierce.Base,
                    unit.BonusVs,
                    unit.Armor.Base,
                    unit.ArmorType,
                    unit.UnitType);
            }

            BossDef boss = config.GetBoss(id);
            return new Combatant(
                boss.Atk,
                boss.AtkType,
                boss.Pierce,
                boss.BonusVs,
                boss.Armor,
                boss.ArmorType,
                boss.UnitType);
        }

        private static float NetCounterMultiplier(Combatant attacker, Combatant defender, EconomyDef economy)
        {
            float bonus = attacker.BonusVs
                .Where(value => value.Target == BonusTarget.Cavalry && defender.UnitType == UnitType.Cavalry
                                || value.Target == BonusTarget.HeavyArmor && defender.ArmorType == ArmorType.Heavy
                                || value.Target == BonusTarget.Building
                                   && (defender.UnitType == UnitType.Building || defender.ArmorType == ArmorType.Building))
                .Sum(value => value.Value);
            return (attacker.Atk * Formula.TypeMultiplier(attacker.AtkType, defender.ArmorType, economy) + bonus)
                   / attacker.Atk;
        }
    }

    public sealed class RngTests
    {
        [Test]
        public void Xorshift32_MatchesGoldenSequence()
        {
            uint[] expected =
            {
                270369u,
                67634689u,
                2647435461u,
                307599695u,
                2398689233u,
                745495504u,
                632435482u,
                435756210u
            };
            var rng = new Rng(1u);

            uint[] actual = Enumerable.Range(0, expected.Length)
                .Select(_ => rng.NextUInt())
                .ToArray();

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void SameSeed_ProducesIdenticalSequence()
        {
            var first = new Rng(0x12345678u);
            var second = new Rng(0x12345678u);

            for (int index = 0; index < 128; index++)
            {
                Assert.That(first.NextUInt(), Is.EqualTo(second.NextUInt()), $"sample {index}");
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequence()
        {
            var first = new Rng(1u);
            var second = new Rng(2u);

            uint[] firstSequence = Enumerable.Range(0, 16).Select(_ => first.NextUInt()).ToArray();
            uint[] secondSequence = Enumerable.Range(0, 16).Select(_ => second.NextUInt()).ToArray();

            Assert.That(firstSequence, Is.Not.EqualTo(secondSequence));
        }

        [Test]
        public void ZeroSeed_MapsToDocumentedNonZeroFallback()
        {
            var zeroSeed = new Rng(0u);
            var fallbackSeed = new Rng(0x6D2B79F5u);

            Assert.That(zeroSeed.NextUInt(), Is.EqualTo(fallbackSeed.NextUInt()));
            Assert.That(zeroSeed.State, Is.Not.Zero);
        }

        [Test]
        public void SavedState_RestoresExactContinuation()
        {
            var rng = new Rng(42u);
            for (int index = 0; index < 7; index++)
            {
                rng.NextUInt();
            }

            RngState saved = rng.SaveState();
            uint[] expected = Enumerable.Range(0, 16).Select(_ => rng.NextUInt()).ToArray();

            for (int index = 0; index < 5; index++)
            {
                rng.NextUInt();
            }

            rng.RestoreState(saved);
            uint[] actual = Enumerable.Range(0, 16).Select(_ => rng.NextUInt()).ToArray();

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ConstructingFromState_RestoresExactContinuation()
        {
            var original = new Rng(99u);
            original.NextUInt();
            original.NextUInt();
            RngState saved = original.SaveState();

            var restored = new Rng(saved);

            Assert.That(restored.NextUInt(), Is.EqualTo(original.NextUInt()));
        }

        [Test]
        public void NextIntAndFloat_StayWithinHalfOpenRanges()
        {
            var rng = new Rng(123u);

            for (int index = 0; index < 10000; index++)
            {
                Assert.That(rng.NextFloat(), Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                Assert.That(rng.NextInt(-7, 13), Is.InRange(-7, 12));
                Assert.That(rng.NextInt(1), Is.Zero);
            }
        }

        [Test]
        public void WeightedSampling_LandsInExpectedDistributionIntervals()
        {
            var rng = new Rng(0x12345678u);
            float[] weights = { 70f, 15f, 12f, 3f };
            var counts = new int[weights.Length];

            for (int index = 0; index < 10000; index++)
            {
                counts[rng.NextWeightedIndex(weights)]++;
            }

            Assert.That(counts[0], Is.InRange(6800, 7200));
            Assert.That(counts[1], Is.InRange(1350, 1650));
            Assert.That(counts[2], Is.InRange(1050, 1350));
            Assert.That(counts[3], Is.InRange(200, 400));
        }

        [Test]
        public void WeightedSampling_SelectsOnlyPositiveEntry()
        {
            var rng = new Rng(77u);
            float[] weights = { 0f, 0f, 1f, 0f };

            for (int index = 0; index < 100; index++)
            {
                Assert.That(rng.NextWeightedIndex(weights), Is.EqualTo(2));
            }
        }

        [Test]
        public void InvalidRangesAndWeights_ReportClearErrors()
        {
            var rng = new Rng(1u);

            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(4, 4));
            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(Array.Empty<float>()));
            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(new[] { 0f, 0f }));
            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(new[] { 1f, -1f }));
            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(new[] { 1f, float.NaN }));
            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(new[] { 1f, float.PositiveInfinity }));
            Assert.Throws<ArgumentException>(
                () => rng.NextWeighted(new[] { "a" }, new[] { 1f, 2f }));
            Assert.Throws<ArgumentNullException>(() => rng.RestoreState(null));
            Assert.Throws<ArgumentException>(() => rng.RestoreState(new RngState()));
        }
    }

    public sealed class RngStreamsTests
    {
        [Test]
        public void DerivationAndXorshiftSequences_MatchGoldenContract()
        {
            var streams = new RngStreams(0x12345678u);

            Assert.That(streams.MasterSeed, Is.EqualTo(0x12345678u));
            Assert.That(streams.BattleSeed, Is.EqualTo(0xCA877B8Fu));
            Assert.That(streams.CardDrawSeed, Is.EqualTo(0xC6F93098u));
            Assert.That(streams.SettlementSeed, Is.EqualTo(0xAE573238u));

            Assert.That(Take(streams.Battle, 6), Is.EqualTo(new uint[]
            {
                2603067380u,
                2499482392u,
                630547156u,
                2956681299u,
                1511827243u,
                298691255u
            }));
            Assert.That(Take(streams.CardDraw, 6), Is.EqualTo(new uint[]
            {
                4255276365u,
                2004840264u,
                3360842339u,
                131485164u,
                52359673u,
                2031505993u
            }));
            Assert.That(Take(streams.Settlement, 6), Is.EqualTo(new uint[]
            {
                1242746928u,
                311418746u,
                3313341066u,
                2461125276u,
                3617714893u,
                3606896657u
            }));
        }

        [Test]
        public void SameMasterSeed_ProducesIdenticalStreams()
        {
            var first = new RngStreams(0xDEADBEEFu);
            var second = new RngStreams(0xDEADBEEFu);

            Assert.That(Take(first.Battle, 64), Is.EqualTo(Take(second.Battle, 64)));
            Assert.That(Take(first.CardDraw, 64), Is.EqualTo(Take(second.CardDraw, 64)));
            Assert.That(Take(first.Settlement, 64), Is.EqualTo(Take(second.Settlement, 64)));
        }

        [Test]
        public void DifferentMasterSeeds_ProduceDifferentStreams()
        {
            var first = new RngStreams(100u);
            var second = new RngStreams(101u);

            Assert.That(Take(first.Battle, 16), Is.Not.EqualTo(Take(second.Battle, 16)));
            Assert.That(Take(first.CardDraw, 16), Is.Not.EqualTo(Take(second.CardDraw, 16)));
            Assert.That(Take(first.Settlement, 16), Is.Not.EqualTo(Take(second.Settlement, 16)));
        }

        [Test]
        public void ConsumingBattleStream_DoesNotShiftCardDrawOrSettlement()
        {
            var consumed = new RngStreams(42u);
            var untouched = new RngStreams(42u);

            Take(consumed.Battle, 1024);

            Assert.That(Take(consumed.CardDraw, 64), Is.EqualTo(Take(untouched.CardDraw, 64)));
            Assert.That(Take(consumed.Settlement, 64), Is.EqualTo(Take(untouched.Settlement, 64)));
        }

        [Test]
        public void ConsumingCardDrawAndSettlement_DoesNotShiftBattle()
        {
            var consumed = new RngStreams(42u);
            var untouched = new RngStreams(42u);

            Take(consumed.CardDraw, 257);
            Take(consumed.Settlement, 511);

            Assert.That(Take(consumed.Battle, 64), Is.EqualTo(Take(untouched.Battle, 64)));
        }

        [Test]
        public void ZeroMasterSeed_ReusesRngDocumentedFallbackBeforeDerivation()
        {
            var zero = new RngStreams(0u);
            var fallback = new RngStreams(0x6D2B79F5u);

            Assert.That(zero.MasterSeed, Is.EqualTo(0x6D2B79F5u));
            Assert.That(zero.BattleSeed, Is.EqualTo(fallback.BattleSeed));
            Assert.That(zero.CardDrawSeed, Is.EqualTo(fallback.CardDrawSeed));
            Assert.That(zero.SettlementSeed, Is.EqualTo(fallback.SettlementSeed));
            Assert.That(Take(zero.Battle, 32), Is.EqualTo(Take(fallback.Battle, 32)));
            Assert.That(Take(zero.CardDraw, 32), Is.EqualTo(Take(fallback.CardDraw, 32)));
            Assert.That(Take(zero.Settlement, 32), Is.EqualTo(Take(fallback.Settlement, 32)));
        }

        [Test]
        public void AggregateState_RestoresEveryStreamExactContinuation()
        {
            var streams = new RngStreams(987654321u);
            Take(streams.Battle, 3);
            Take(streams.CardDraw, 5);
            Take(streams.Settlement, 7);
            RngStreamsState saved = streams.SaveState();

            uint[] expectedBattle = Take(streams.Battle, 32);
            uint[] expectedCardDraw = Take(streams.CardDraw, 32);
            uint[] expectedSettlement = Take(streams.Settlement, 32);
            Take(streams.Battle, 11);
            Take(streams.CardDraw, 13);
            Take(streams.Settlement, 17);

            streams.RestoreState(saved);

            Assert.That(Take(streams.Battle, 32), Is.EqualTo(expectedBattle));
            Assert.That(Take(streams.CardDraw, 32), Is.EqualTo(expectedCardDraw));
            Assert.That(Take(streams.Settlement, 32), Is.EqualTo(expectedSettlement));
        }

        [Test]
        public void InvalidAggregateState_IsRejectedBeforeAnyStreamChanges()
        {
            var streams = new RngStreams(123u);
            RngStreamsState before = streams.SaveState();
            var missingCardDraw = new RngStreamsState
            {
                battle = new RngState(1u),
                cardDraw = null,
                settlement = new RngState(3u)
            };
            var zeroSettlement = new RngStreamsState
            {
                battle = new RngState(1u),
                cardDraw = new RngState(2u),
                settlement = new RngState()
            };

            Assert.Throws<ArgumentNullException>(() => streams.RestoreState(null));
            Assert.Throws<ArgumentException>(() => streams.RestoreState(missingCardDraw));
            Assert.Throws<ArgumentException>(() => streams.RestoreState(zeroSettlement));

            RngStreamsState after = streams.SaveState();
            Assert.That(after.battle.state, Is.EqualTo(before.battle.state));
            Assert.That(after.cardDraw.state, Is.EqualTo(before.cardDraw.state));
            Assert.That(after.settlement.state, Is.EqualTo(before.settlement.state));
        }

        private static uint[] Take(Rng rng, int count)
        {
            return Enumerable.Range(0, count).Select(_ => rng.NextUInt()).ToArray();
        }
    }
}
