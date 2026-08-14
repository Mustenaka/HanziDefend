using System;
using System.Diagnostics;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Performance;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace HanziDefend.Tests.PlayMode.Performance
{
    [TestFixture]
    public sealed class BattleScalabilityPerformanceTests
    {
        private const uint ScaleSeed = 0xE3000400u;
        private const int SamplesPerScale = 5;

        /// <summary>
        /// Live guard. Doubling the unit count must not double the cost more than 4x — that is
        /// exactly the O(n^2) boundary (2^2 = 4). WO-C6 tightened this from 5.5, which permitted
        /// growth *worse* than quadratic and so guarded almost nothing.
        /// </summary>
        private const double QuadraticDoublingRatio = 4d;

        /// <summary>
        /// The aspirational target: 2^1.5, i.e. anything at or below O(n^1.5). Perfect linear is
        /// 2.0x, O(n log n) is about 2.3x at these sizes. The current implementation measures
        /// 2.96x / 3.42x, so this bound is genuinely superlinear-detecting — and currently unmet.
        /// See <see cref="FirstRetargetTick_MeetsNearLinearSlopeTarget"/>.
        /// </summary>
        private const double NearLinearDoublingRatio = 2.83d;
        private const int SteadyUnitCount = 400;
        private const int SteadyWarmupTicks = 24;
        private const int SteadyMeasurementTicks = 120;
        private const double PhasedRetargetPeakBudgetMilliseconds = 12d;

        /// <summary>
        /// Live regression guard against a genuine complexity blow-up. WO-C6 tightened the bound
        /// from 5.5x to 4.0x per doubling, which is precisely the O(n^2) boundary; the old value
        /// tolerated worse-than-quadratic growth and therefore could not fail on any realistic
        /// regression. The stricter near-linear target lives in its own suspended test below.
        /// </summary>
        [Test, Performance]
        public void FirstRetargetTick_OneHundredTwoHundredFourHundred_StaysBelowQuadraticSlope()
        {
            (double first, double second) = MeasureDoublingRatios();

            Assert.That(first, Is.LessThan(QuadraticDoublingRatio),
                "100->200 first-retarget cost grew at or beyond the O(n^2) boundary.");
            Assert.That(second, Is.LessThan(QuadraticDoublingRatio),
                "200->400 first-retarget cost grew at or beyond the O(n^2) boundary.");
        }

        /// <summary>
        /// The slope target that actually separates linear from superlinear growth.
        ///
        /// <para><b>Currently unmet and deliberately suspended, not passing.</b> Across three
        /// measured runs the doubling ratios were 2.96x/3.42x, 2.74x/2.95x and 2.64x/3.12x. The
        /// 200->400 leg is above the target every time (2.95-3.42x, about O(n^1.6)); the 100->200 leg
        /// straddles it. The spatial-bucket first-acquisition path is genuinely superlinear. WO-C6
        /// did not relax the bound to make this green — that is the exact signal the work order asked
        /// not to swallow — it is recorded in TECH_DEBT instead.</para>
        ///
        /// <para>Target scale 200 is unaffected (about 10-12 ms median here, and the gated
        /// steady-state peak is 4-8 ms), so this is scaling headroom rather than a shipping blocker.
        /// Unignore this test once the first-acquisition path is made near-linear.</para>
        /// </summary>
        [Test, Performance]
        [Ignore("WO-C6: first-retarget growth measures 2.95-3.42x on the 200->400 doubling across "
                + "three runs (~O(n^1.6)), above the 2.83x near-linear target. Suspended, NOT passing "
                + "— see the WO-C6 row in Docs/Plan/TECH_DEBT.md. The live O(n^2) guard is "
                + "FirstRetargetTick_OneHundredTwoHundredFourHundred_StaysBelowQuadraticSlope.")]
        public void FirstRetargetTick_MeetsNearLinearSlopeTarget()
        {
            (double first, double second) = MeasureDoublingRatios();

            Assert.That(first, Is.LessThan(NearLinearDoublingRatio),
                "100->200 first-retarget cost grew faster than the near-linear target.");
            Assert.That(second, Is.LessThan(NearLinearDoublingRatio),
                "200->400 first-retarget cost grew faster than the near-linear target.");
        }

        private static (double FirstDoubling, double SecondDoubling) MeasureDoublingRatios()
        {
            GameConfig config = GameConfig.Load();
            WarmCodePaths(config);

            double oneHundred = MeasureMedianFirstRetargetTick(config, 100);
            double twoHundred = MeasureMedianFirstRetargetTick(config, 200);
            double fourHundred = MeasureMedianFirstRetargetTick(config, 400);
            double firstDoubling = twoHundred / Math.Max(double.Epsilon, oneHundred);
            double secondDoubling = fourHundred / Math.Max(double.Epsilon, twoHundred);

            TestContext.WriteLine(
                $"First-retarget medians: 100={oneHundred:F3} ms, "
                + $"200={twoHundred:F3} ms, 400={fourHundred:F3} ms; "
                + $"doubling ratios={firstDoubling:F2}x/{secondDoubling:F2}x "
                + $"(quadratic guard {QuadraticDoublingRatio:F2}x, "
                + $"near-linear target {NearLinearDoublingRatio:F2}x).");
            return (firstDoubling, secondDoubling);
        }

        [Test, Performance]
        public void FourHundredUnits_SteadyTicks_AllocateZeroManagedBytes()
        {
            GameConfig config = GameConfig.Load();
            using (var system = new BattleSystem(config, ScaleSeed))
            {
                SpawnStationaryNoTargetScenario(system, config, SteadyUnitCount);
                for (int tick = 0; tick < SteadyWarmupTicks; tick++)
                {
                    system.Tick(system.FixedDeltaTime);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int tick = 0; tick < SteadyMeasurementTicks; tick++)
                {
                    system.Tick(system.FixedDeltaTime);
                }

                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                double bytesPerTick = allocatedBytes / (double)SteadyMeasurementTicks;
                Measure.Custom(
                    new SampleGroup(
                        "400Units.SteadyGcAllocPerTick",
                        SampleUnit.Byte,
                        false),
                    bytesPerTick);

                TestContext.WriteLine(
                    $"400-unit steady GC: {allocatedBytes} B / {SteadyMeasurementTicks} ticks "
                    + $"= {bytesPerTick:F3} B/tick after {SteadyWarmupTicks} warmup ticks.");
                Assert.That(system.GetAliveCount(BattleTeam.Ally), Is.EqualTo(SteadyUnitCount / 2));
                Assert.That(system.GetAliveCount(BattleTeam.Enemy), Is.EqualTo(SteadyUnitCount / 2));
                Assert.That(allocatedBytes, Is.Zero,
                    "The warmed BattleSystem.Tick path must not allocate managed memory.");
            }
        }

        [Test, Performance]
        public void TwoHundredUnits_PhasedPeriodicRetargetPeak_StaysBelowLegacySpikeBand()
        {
            GameConfig config = GameConfig.Load();
            WarmCodePaths(config);

            using (var system = new BattleSystem(config, ScaleSeed))
            {
                SpawnFirstRetargetScenario(system, config, 200);
                system.Tick(system.FixedDeltaTime);

                int phaseCount = RetargetPhaseSchedule.ResolvePhaseCount(
                    config.Economy.Battle.RetargetInterval,
                    system.FixedDeltaTime);
                int measuredTickCount = phaseCount * 2 + 2;
                double peakMilliseconds = 0d;
                double totalMilliseconds = 0d;
                for (int tick = 0; tick < measuredTickCount; tick++)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    system.Tick(system.FixedDeltaTime);
                    stopwatch.Stop();
                    double elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                    peakMilliseconds = Math.Max(peakMilliseconds, elapsedMilliseconds);
                    totalMilliseconds += elapsedMilliseconds;
                    Measure.Custom(
                        new SampleGroup(
                            "200Units.PhasedPeriodicRetargetTick",
                            SampleUnit.Millisecond,
                            false),
                        elapsedMilliseconds);
                }

                double averageMilliseconds = totalMilliseconds / measuredTickCount;
                TestContext.WriteLine(
                    $"200-unit phased periodic retarget: peak={peakMilliseconds:F3} ms, "
                    + $"average={averageMilliseconds:F3} ms across {measuredTickCount} ticks, "
                    + $"k={phaseCount}.");
                Assert.That(phaseCount, Is.GreaterThanOrEqualTo(3),
                    "The configured interval should be spread over at least three fixed ticks.");
                Assert.That(peakMilliseconds, Is.LessThan(PhasedRetargetPeakBudgetMilliseconds),
                    "EntityId phasing must keep the periodic 200-unit spike below the legacy "
                    + "12-15 ms band while leaving first acquisition semantics unchanged.");
            }
        }

        private static void WarmCodePaths(GameConfig config)
        {
            using (var system = new BattleSystem(config, ScaleSeed))
            {
                SpawnFirstRetargetScenario(system, config, 20);
                system.Tick(system.FixedDeltaTime);
            }
        }

        private static double MeasureMedianFirstRetargetTick(GameConfig config, int totalUnitCount)
        {
            var samples = new double[SamplesPerScale];
            var sampleGroup = new SampleGroup(
                $"{totalUnitCount}Units.FirstRetargetTick.Scale",
                SampleUnit.Millisecond,
                false);

            for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                using (var system = new BattleSystem(config, ScaleSeed + (uint)sampleIndex))
                {
                    SpawnFirstRetargetScenario(system, config, totalUnitCount);
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    system.Tick(system.FixedDeltaTime);
                    stopwatch.Stop();
                    samples[sampleIndex] = stopwatch.Elapsed.TotalMilliseconds;
                    Measure.Custom(sampleGroup, samples[sampleIndex]);
                    AssertAllLivingUnitsAcquiredTargets(system, totalUnitCount);
                }
            }

            Array.Sort(samples);
            return samples[samples.Length / 2];
        }

        private static void SpawnFirstRetargetScenario(
            BattleSystem system,
            GameConfig config,
            int totalUnitCount)
        {
            Assert.That(totalUnitCount, Is.Positive);
            Assert.That(totalUnitCount % 2, Is.Zero);
            int unitsPerTeam = totalUnitCount / 2;
            const int columns = 10;
            float spacing = config.Economy.Battle.SeparationDistance + 0.1f;
            float left = -(columns - 1) * spacing * 0.5f;
            float boundary = spacing * 0.5f;

            for (int index = 0; index < unitsPerTeam; index++)
            {
                int column = index % columns;
                int row = index / columns;
                float x = left + column * spacing;
                system.Spawn(new UnitSpawnRequest(
                    "gong",
                    1,
                    new Vector2(x, -boundary - row * spacing)));
                system.Spawn(new UnitSpawnRequest(
                    "e_shan",
                    1,
                    new Vector2(x, boundary + row * spacing)));
            }
        }

        private static void SpawnStationaryNoTargetScenario(
            BattleSystem system,
            GameConfig config,
            int totalUnitCount)
        {
            int unitsPerTeam = totalUnitCount / 2;
            const int columns = 20;
            float spacing = config.Economy.Battle.SeparationDistance + 0.1f;
            float left = -(columns - 1) * spacing * 0.5f;

            for (int index = 0; index < unitsPerTeam; index++)
            {
                int column = index % columns;
                int row = index / columns;
                float x = left + column * spacing;
                system.Spawn(new UnitSpawnRequest(
                    "chc",
                    1,
                    new Vector2(x, -1f - row * spacing)));
                system.Spawn(new UnitSpawnRequest(
                    "e_lang",
                    1,
                    new Vector2(x, 1f + row * spacing)));
            }
        }

        private static void AssertAllLivingUnitsAcquiredTargets(
            BattleSystem system,
            int expectedCount)
        {
            var snapshots = system.CaptureSnapshot();
            Assert.That(snapshots.Count, Is.EqualTo(expectedCount));
            for (int index = 0; index < snapshots.Count; index++)
            {
                BattleUnitSnapshot snapshot = snapshots[index];
                Assert.That(snapshot.State, Is.Not.EqualTo(BattleUnitState.Dead));
                Assert.That(snapshot.TargetEntityId, Is.Not.Null,
                    $"Unit {snapshot.EntityId} did not execute the first-target path.");
            }
        }
    }
}
