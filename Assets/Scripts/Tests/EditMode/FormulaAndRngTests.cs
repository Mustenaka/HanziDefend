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
            Assert.That(Formula.Damage(atk, armor, pierce, 100f, 1), Is.EqualTo(expected));
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
            Assert.That(Formula.RefreshCost(2, 100, 7), Is.EqualTo(114));
            Assert.That(Formula.Damage(100f, 100f, 0f, 200f, 1), Is.EqualTo(67));
            Assert.That(Formula.Damage(0f, 0f, 0f, 100f, 4), Is.EqualTo(4));
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
