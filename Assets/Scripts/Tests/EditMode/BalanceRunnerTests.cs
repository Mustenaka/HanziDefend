using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HanziDefend.Data;
using HanziDefend.Editor.Balance;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Deploy;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode
{
    [TestFixture]
    public sealed class BalanceRunnerTests
    {
        private readonly List<string> temporaryDirectories = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < temporaryDirectories.Count; index++)
            {
                string path = temporaryDirectories[index];
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }

            temporaryDirectories.Clear();
        }

        [Test]
        public void ReferenceLineups_AreTheFourAccumulatedBoardsPairedWithTheirOwnStage()
        {
            GameConfig config = GameConfig.Load();

            Assert.That(BalanceReferenceLineups.All.Select(value => value.Id),
                Is.EqualTo(new[]
                {
                    "A_real_s1", "A_real_s5", "B_real_s1_nosiege", "B_real_s5_nosiege"
                }));

            foreach (BalanceLineup lineup in BalanceReferenceLineups.All)
            {
                Assert.That(lineup.LevelIds, Is.Not.Null,
                    $"{lineup.Id} must name the stage its board belongs to");
                Assert.That(lineup.LevelIds, Has.Count.EqualTo(1), lineup.Id);
                AssertLineupFitsItsMask(config, lineup);

                bool containsSiege = lineup.Spawns.Any(value =>
                    config.GetUnit(value.UnitId).AtkType == AttackType.Siege);
                Assert.That(containsSiege, Is.EqualTo(lineup.ExpectedToContainSiege), lineup.Id);
            }
        }

        /// <summary>
        /// WO-F1 §C in assertions: the stage-five reference board has to be what a player who
        /// actually reached stage five is holding, not six level-1 units on three columns.
        /// </summary>
        [Test]
        public void StageFiveReferenceBoard_HasTenPlusUnitsOnFifteenPlusCellsIncludingMerges()
        {
            BalanceLineup lineup = BalanceReferenceLineups.All
                .Single(value => value.Id == BalanceReferenceLineups.StageFiveGateLineupId);

            Assert.That(lineup.Spawns, Has.Count.GreaterThanOrEqualTo(10),
                "a stage-five board carries at least ten units");
            Assert.That(lineup.DeclaredUnlockedCellCount, Is.GreaterThanOrEqualTo(15),
                "a stage-five board is spread over at least fifteen unlocked cells");
            Assert.That(lineup.Spawns.Count(value => value.Level >= 2), Is.GreaterThanOrEqualTo(3),
                "a stage-five board carries merge results, not only fresh level-1 cards");
            Assert.That(lineup.ExpectedToContainSiege, Is.True);
            Assert.That(lineup.LevelIds, Is.EqualTo(new[] { "level_1_5" }));

            BalanceLineup noSiege = BalanceReferenceLineups.All
                .Single(value => value.Id == "B_real_s5_nosiege");
            Assert.That(noSiege.ExpectedToContainSiege, Is.False);
            Assert.That(noSiege.DeclaredUnlockedCellCount,
                Is.EqualTo(lineup.DeclaredUnlockedCellCount),
                "declining siege changes what is deployed, not how the grid grew");
        }

        /// <summary>
        /// The stage-one board is only a fair reference if the opening resources really produce it:
        /// the untouched 3x3 and nothing beyond it.
        /// </summary>
        [Test]
        public void StageOneReferenceBoards_StandOnTheUntouchedStartingRect()
        {
            GameConfig config = GameConfig.Load();
            LevelDef level = config.GetLevel("level_1_1");
            var startingRect = new HashSet<GridCoordinate>(
                DeploymentGrid.EnumerateRect(level.InitialUnlock));

            foreach (string id in new[] { "A_real_s1", "B_real_s1_nosiege" })
            {
                BalanceLineup lineup = BalanceReferenceLineups.All.Single(value => value.Id == id);
                Assert.That(lineup.UnlockedCells, Is.EquivalentTo(startingRect), id);
                Assert.That(lineup.DeclaredUnlockedCellCount, Is.EqualTo(9), id);
                Assert.That(lineup.Spawns.All(value => value.Level <= 2), Is.True,
                    $"{id}: stage one cannot have reached level 3");
            }
        }

        [Test]
        public void LineupUnlockMask_MustBeAStateAnUnlockCardCouldHaveGrown()
        {
            GameConfig config = GameConfig.Load();
            LevelDef level = config.GetLevel("level_1_1");
            var island = new BalanceLineup(
                "probe_island",
                "probe",
                false,
                new[] { new BalanceLineupSpawn("zu", 1, 2, 2) },
                new List<GridCoordinate>(DeploymentGrid.EnumerateRect(level.InitialUnlock))
                {
                    new GridCoordinate(0, 6)
                });
            var missingStart = new BalanceLineup(
                "probe_missing_start",
                "probe",
                false,
                new[] { new BalanceLineupSpawn("zu", 1, 2, 2) },
                new[] { new GridCoordinate(2, 2), new GridCoordinate(3, 2) });
            var outside = new BalanceLineup(
                "probe_outside",
                "probe",
                false,
                new[] { new BalanceLineupSpawn("zu", 1, 2, 2) },
                new List<GridCoordinate>(DeploymentGrid.EnumerateRect(level.InitialUnlock))
                {
                    new GridCoordinate(9, 9)
                });

            Assert.That(() => BalanceRunner.ResolveUnlockedCells(level, island),
                Throws.ArgumentException, "an unlock card cannot create a disconnected island");
            Assert.That(() => BalanceRunner.ResolveUnlockedCells(level, missingStart),
                Throws.ArgumentException, "a run can only add cells to the starting rect");
            Assert.That(() => BalanceRunner.ResolveUnlockedCells(level, outside),
                Throws.ArgumentException, "cells must stay inside the playfield");
        }

        // ---------------------------------------------------------------- WO-F1 §A: three acts

        [Test]
        public void EveryWaveSet_RunsThreeActsSeparatedByReadableGaps()
        {
            GameConfig config = GameConfig.Load();

            foreach (WaveSetDef waveSet in config.WaveSets)
            {
                WaveTimeline timeline = WaveTimeline.Compile(config, waveSet);
                Assert.That(timeline.Acts.Select(value => value.Act),
                    Is.EqualTo(new[] { WaveActs.First, WaveActs.Second, WaveActs.Third }),
                    waveSet.Id);

                Assert.That(timeline.GetAct(WaveActs.Second).GapBeforeSeconds,
                    Is.GreaterThanOrEqualTo(4d),
                    $"{waveSet.Id}: act 2 must open after a visible pause");
                Assert.That(timeline.GetAct(WaveActs.Third).GapBeforeSeconds,
                    Is.GreaterThanOrEqualTo(4d),
                    $"{waveSet.Id}: act 3 must open after a visible pause");
            }
        }

        [Test]
        public void EveryWaveSet_KeepsTheStreamRunningInsideEachAct()
        {
            GameConfig config = GameConfig.Load();

            foreach (WaveSetDef waveSet in config.WaveSets)
            {
                WaveTimeline timeline = WaveTimeline.Compile(config, waveSet);
                foreach (WaveActSummary act in timeline.Acts)
                {
                    Assert.That(act.CombatantCount, Is.GreaterThan(0), $"{waveSet.Id} act {act.Act}");
                    Assert.That(act.DurationSeconds, Is.GreaterThanOrEqualTo(28d),
                        $"{waveSet.Id} act {act.Act} is too short to read as an act");
                    Assert.That(act.LongestInternalGapSeconds, Is.LessThan(act.GapBeforeSeconds > 0d
                            ? act.GapBeforeSeconds
                            : 6d),
                        $"{waveSet.Id} act {act.Act}: a hole inside the act must stay shorter than "
                        + "the pause between acts, or the player cannot tell them apart");
                }
            }
        }

        [Test]
        public void EveryWaveSet_OpensActThreeWithTheCastleAndKeepsSendingGuardsAfterIt()
        {
            GameConfig config = GameConfig.Load();

            foreach (WaveSetDef waveSet in config.WaveSets)
            {
                WaveTimeline timeline = WaveTimeline.Compile(config, waveSet);
                WaveActSummary third = timeline.GetAct(WaveActs.Third);

                Assert.That(timeline.BossSpawnSeconds, Is.EqualTo(third.FirstSpawnSeconds).Within(0.001d),
                    $"{waveSet.Id}: the castle is act 3's opening beat, not its reward");

                WaveSpawnPoint[] guardsAfterCastle = timeline.Spawns
                    .Where(value => value.Act == WaveActs.Third
                                    && !value.IsBoss
                                    && value.TimeSeconds > timeline.BossSpawnSeconds)
                    .ToArray();
                Assert.That(guardsAfterCastle, Is.Not.Empty, waveSet.Id);
                Assert.That(guardsAfterCastle.Max(value => value.TimeSeconds),
                    Is.GreaterThan(timeline.BossSpawnSeconds + 30d),
                    $"{waveSet.Id}: guards must keep arriving well after the castle appears");
            }
        }

        [Test]
        public void EveryWaveSet_HoldsTheArmourMixAndFieldsEveryEnemyUnit()
        {
            GameConfig config = GameConfig.Load();
            var rushShareByStage = new List<double>();

            foreach (LevelDef level in config.Levels.OrderBy(value => value.StageIndex))
            {
                WaveSetDef waveSet = config.GetWaveSet(level.WaveSetId);
                WaveTimeline timeline = WaveTimeline.Compile(config, waveSet);

                Assert.That(timeline.GetAct(WaveActs.First).HeavyRate, Is.EqualTo(0d).Within(0.001d),
                    $"{waveSet.Id}: act 1 teaches, so no heavy armour");
                Assert.That(timeline.GetAct(WaveActs.Second).HeavyRate,
                    Is.GreaterThanOrEqualTo(0.10d), waveSet.Id);
                Assert.That(timeline.GetAct(WaveActs.Third).HeavyRate,
                    Is.GreaterThanOrEqualTo(0.25d), waveSet.Id);

                int heavy = timeline.Acts.Sum(value => value.Heavy);
                int combatants = timeline.TotalCombatantCount;
                Assert.That((double)heavy / combatants, Is.GreaterThanOrEqualTo(0.15d),
                    $"{waveSet.Id}: below a 15% heavy floor 链甲兵 and 弩兵 stop having a reason to exist");

                string[] unitIds = timeline.Spawns
                    .Where(value => !value.IsBoss)
                    .Select(value => value.UnitId)
                    .Distinct()
                    .ToArray();
                Assert.That(unitIds, Does.Contain("e_shan"),
                    $"{waveSet.Id}: 山贼 exists and must be used");
                Assert.That(unitIds, Does.Contain("e_lang"), waveSet.Id);

                int rush = timeline.Spawns.Count(value => value.UnitId == "e_lang");
                rushShareByStage.Add((double)rush / combatants);
            }

            Assert.That(rushShareByStage.Last(), Is.GreaterThan(rushShareByStage.First()),
                "e_lang is the leak unit; its share must rise with the stage so late camps take damage");
        }

        // ------------------------------------------------- WO-F1 §B: entry timing by cooldown

        [Test]
        public void Deploy_HoldsADelayedUnitOffTheFieldForItsOwnCooldown()
        {
            GameConfig config = GameConfig.Load();
            UnitDef ram = config.GetUnit("chc");
            Assert.That(ram.SpawnMode, Is.EqualTo(UnitSpawnMode.Delayed));
            float cooldown = ram.Cooldown.Base;
            Assert.That(cooldown, Is.GreaterThan(8f), "chc is the slowest unit to arrive");

            using (BattleSystem system = BattleSystem.CreateEncounter(
                       config, "level_1_1", 0xF1000001u, simulatePhysics: false))
            {
                DeploymentEntry entry = system.Deploy(
                    new UnitSpawnRequest("chc", 1, new UnityEngine.Vector2(0f, -7f)));

                Assert.That(entry.IsOnField, Is.False);
                Assert.That(entry.EntryTimeSeconds, Is.EqualTo(cooldown).Within(0.001d));
                Assert.That(system.PendingDeploymentCount, Is.EqualTo(1));
                Assert.That(system.GetAliveCount(BattleTeam.Ally), Is.Zero,
                    "a queued unit is not on the battlefield in any sense");

                TickTo(system, cooldown - 0.5d);
                Assert.That(system.GetAliveCount(BattleTeam.Ally), Is.Zero,
                    "still queued half a second before its cooldown expires");
                Assert.That(system.CapturePendingDeployments().Single().RemainingSeconds,
                    Is.GreaterThan(0f));

                TickTo(system, cooldown + 0.2d);
                Assert.That(system.GetAliveCount(BattleTeam.Ally), Is.EqualTo(1));
                Assert.That(system.PendingDeploymentCount, Is.Zero);
            }
        }

        [Test]
        public void Deploy_LetsTheShortCooldownUnitsFormTheLineBeforeTheHeavyOnesArrive()
        {
            GameConfig config = GameConfig.Load();
            var recorder = new RecordingDeploymentEvents();

            using (BattleSystem system = BattleSystem.CreateEncounter(
                       config, "level_1_1", 0xF1000002u, recorder, simulatePhysics: false))
            {
                foreach (string unitId in new[] { "chc", "zu", "nuc", "gong" })
                {
                    system.Deploy(new UnitSpawnRequest(unitId, 1, new UnityEngine.Vector2(0f, -7f)));
                }

                Assert.That(system.GetAliveCount(BattleTeam.Ally), Is.Zero,
                    "nothing is on the field at t=0 — that whole-wall opening is what WO-F1 §B removes");

                TickTo(system, 12d);
            }

            Assert.That(recorder.EntryOrder, Is.EqualTo(new[] { "zu", "gong", "nuc", "chc" }),
                "entry order follows the cooldown ladder already in units.json");
            Assert.That(recorder.EntrySeconds["zu"], Is.EqualTo(1.2d).Within(0.05d));
            Assert.That(recorder.EntrySeconds["chc"], Is.EqualTo(9.0d).Within(0.05d));
        }

        [Test]
        public void Deploy_QueuedUnitTakesNoAreaDamageAndIsNotAValidTarget()
        {
            GameConfig config = GameConfig.Load();

            using (BattleSystem system = BattleSystem.CreateEncounter(
                       config, "level_1_1", 0xF1000003u, simulatePhysics: false))
            {
                var position = new UnityEngine.Vector2(0f, -7f);
                system.Deploy(new UnitSpawnRequest("chc", 1, position));
                int enemy = system.Spawn(new UnitSpawnRequest("e_lang", 1, position));

                system.Tick(system.FixedDeltaTime);

                Assert.That(system.GetUnitSnapshot(enemy).TargetEntityId, Is.Not.EqualTo(0));
                Assert.That(system.GetAliveCount(BattleTeam.Ally), Is.Zero);
                Assert.That(
                    system.CaptureSnapshot().Any(value => value.DefinitionId == "chc"),
                    Is.False,
                    "the queued ram has no battle entity at all, so nothing can hit it");
            }
        }

        // ------------------------------------------------------------------- harness plumbing

        [Test]
        public void Run_FastFixture_WritesAllCsvReportsAndCapturesDpsSurvivalCoinsAndArmor()
        {
            GameConfig config = CreateFastConfig();
            string output = CreateTemporaryDirectory();
            BalanceLineup lineup = FastLineup();
            var request = new BalanceRunRequest(
                2,
                new[] { "level_1_1" },
                new[] { lineup },
                0xE1000001u,
                5d,
                false,
                output);

            BalanceRunReport report = new BalanceRunner().Run(config, request);

            Assert.That(report.Battles, Has.Count.EqualTo(2));
            Assert.That(report.Battles, Has.All.Matches<BalanceBattleResult>(value =>
                value.Result == BattleResult.Win && !value.TimedOut));
            Assert.That(report.Units.Single(value =>
                    value.Team == BattleTeam.Ally && value.UnitId == "nub").Dps,
                Is.GreaterThan(0d));
            Assert.That(report.Units, Has.Some.Matches<BalanceUnitSummary>(value =>
                value.MeanSurvivalSeconds > 0d));
            Assert.That(report.Battles.SelectMany(value => value.CoinCurve),
                Has.Some.Matches<BalanceCoinPoint>(value => value.EventKind == "Start"));
            Assert.That(report.ArmorDistributions, Is.Not.Empty);
            Assert.That(report.Battles, Has.All.Matches<BalanceBattleResult>(value =>
                value.PeakConcurrentUnits > 0));

            string[] expectedFiles =
            {
                "summary.csv",
                "battles.csv",
                "unit_metrics.csv",
                "coin_curve.csv",
                "armor_distribution.csv",
                "report.md"
            };
            Assert.That(expectedFiles.All(value => File.Exists(Path.Combine(output, value))), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(output, "summary.csv")),
                Does.Contain("win_rate").And.Contain("A_fast_siege"));
            Assert.That(File.ReadAllText(Path.Combine(output, "summary.csv")),
                Does.Contain("enemy_death_y_p90").And.Contain("peak_concurrent_units"));
            Assert.That(File.ReadAllText(Path.Combine(output, "unit_metrics.csv")),
                Does.Contain("mean_survival_s").And.Contain("nub"));
            Assert.That(File.ReadAllText(Path.Combine(output, "coin_curve.csv")),
                Does.Contain("total_coins").And.Contain("Start"));
        }

        [Test]
        public void NoPhysicsStep_SameSeedMatchesPhysicsStepForBodyFreeFastFixture()
        {
            GameConfig config = CreateFastConfig();
            BalanceLineup lineup = FastLineup();
            var noPhysics = new BalanceRunRequest(
                1,
                new[] { "level_1_1" },
                new[] { lineup },
                0xE1000002u,
                5d,
                false);
            var withPhysics = new BalanceRunRequest(
                1,
                new[] { "level_1_1" },
                new[] { lineup },
                0xE1000002u,
                5d,
                true);

            BalanceBattleResult first = new BalanceRunner().Run(config, noPhysics).Battles.Single();
            BalanceBattleResult second = new BalanceRunner().Run(config, withPhysics).Battles.Single();

            Assert.That(second.Seed, Is.EqualTo(first.Seed));
            Assert.That(second.Result, Is.EqualTo(first.Result));
            Assert.That(second.DurationSeconds, Is.EqualTo(first.DurationSeconds));
            Assert.That(second.TickCount, Is.EqualTo(first.TickCount));
            Assert.That(second.EndCoins, Is.EqualTo(first.EndCoins));
            Assert.That(
                second.Entities.Select(value =>
                    $"{value.EntityId}:{value.UnitId}:{value.Team}:{value.Damage}:{value.SurvivalSeconds:R}:{value.Survived}"),
                Is.EqualTo(first.Entities.Select(value =>
                    $"{value.EntityId}:{value.UnitId}:{value.Team}:{value.Damage}:{value.SurvivalSeconds:R}:{value.Survived}")));
        }

        [Test, Timeout(60000)]
        public void HundredGameHeadlessPath_CompletesWithinSixtySeconds()
        {
            GameConfig config = CreateFastConfig();
            var request = new BalanceRunRequest(
                100,
                new[] { "level_1_1" },
                new[] { FastLineup() },
                0xE1000100u,
                5d,
                false);

            BalanceRunReport report = new BalanceRunner().Run(request: request, config: config);

            Assert.That(report.Battles, Has.Count.EqualTo(100));
            Assert.That(report.Battles, Has.All.Matches<BalanceBattleResult>(value => !value.TimedOut));
            Assert.That(report.WallClockSeconds, Is.LessThan(60d));
        }

        [Test]
        public void NoPhysicsBattleSystem_CanRunOnThreadPoolWithoutTouchingPhysicsGlobals()
        {
            GameConfig config = CreateFastConfig();

            BattleResult result = Task.Run(() =>
            {
                BattleSystem system = BattleSystem.CreateEncounter(
                    config,
                    "level_1_1",
                    0xE1000101u,
                    simulatePhysics: false);
                try
                {
                    system.Spawn(new UnitSpawnRequest("nub", 1, new UnityEngine.Vector2(0f, -7f)));
                    while (!system.IsSettled && system.TickIndex < 150)
                    {
                        system.Tick(system.FixedDeltaTime);
                    }

                    return system.Result;
                }
                finally
                {
                    system.Dispose();
                }
            }).GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo(BattleResult.Win));
        }

        [Test]
        public void ParallelNoPhysics_SameRequestTwiceAndForcedSerialProduceSameOrderedResults()
        {
            GameConfig config = CreateFastConfig();
            BalanceLineup lineup = FastLineup();
            var parallelRequest = new BalanceRunRequest(
                12,
                new[] { "level_1_1" },
                new[] { lineup },
                0xE1000102u,
                5d,
                false,
                maxDegreeOfParallelism: 4);
            var serialRequest = new BalanceRunRequest(
                12,
                new[] { "level_1_1" },
                new[] { lineup },
                0xE1000102u,
                5d,
                false,
                maxDegreeOfParallelism: 1);

            BalanceRunReport first = new BalanceRunner().Run(config, parallelRequest);
            BalanceRunReport second = new BalanceRunner().Run(config, parallelRequest);
            BalanceRunReport serial = new BalanceRunner().Run(config, serialRequest);

            Assert.That(first.Request.UsesParallelExecution, Is.True);
            Assert.That(serial.Request.UsesParallelExecution, Is.False);
            Assert.That(first.Battles.Select(BattleSignature),
                Is.EqualTo(second.Battles.Select(BattleSignature)));
            Assert.That(first.Battles.Select(BattleSignature),
                Is.EqualTo(serial.Battles.Select(BattleSignature)));
        }

        [Test]
        public void DefaultRequest_PairsEachAccumulatedBoardWithItsOwnStage()
        {
            BalanceRunRequest request = BalanceRunRequest.CreateDefault(25);

            Assert.That(request.CohortCount, Is.EqualTo(4),
                "four boards, each on the one stage it belongs to");
            Assert.That(request.TotalGames, Is.EqualTo(100));
            Assert.That(request.Cohorts.Select(value => value.Lineup.Id + "@" + value.LevelId),
                Is.EquivalentTo(new[]
                {
                    "A_real_s1@level_1_1",
                    "A_real_s5@level_1_5",
                    "B_real_s1_nosiege@level_1_1",
                    "B_real_s5_nosiege@level_1_5"
                }));
            Assert.That(request.SimulatePhysics, Is.False);
        }

        [Test]
        public void AcceptanceEvaluator_PassingBoundaryFixtureKeepsLockedThresholds()
        {
            var metrics = new BalanceAcceptanceMetrics(
                59.999d,
                0,
                0.55d,
                0.50d,
                90d,
                150d,
                0.15d,
                0.15d);

            BalanceAcceptanceResult result = BalanceAcceptanceEvaluator.Evaluate(metrics);

            Assert.That(BalanceAcceptanceEvaluator.MaximumHundredGameWallClockSeconds, Is.EqualTo(60d));
            Assert.That(BalanceAcceptanceEvaluator.StageOneMinimumWinRate, Is.EqualTo(0.55d));
            Assert.That(BalanceAcceptanceEvaluator.StageOneMaximumWinRate, Is.EqualTo(0.75d));
            Assert.That(BalanceAcceptanceEvaluator.StageFiveMinimumWinRate, Is.EqualTo(0.30d));
            Assert.That(BalanceAcceptanceEvaluator.StageFiveMaximumWinRate, Is.EqualTo(0.50d));
            Assert.That(BalanceAcceptanceEvaluator.MinimumMeanDurationSeconds, Is.EqualTo(90d));
            Assert.That(BalanceAcceptanceEvaluator.MaximumMeanDurationSeconds, Is.EqualTo(150d));
            Assert.That(BalanceAcceptanceEvaluator.MinimumHeavyArmorRate, Is.EqualTo(0.15d));
            Assert.That(result.Passed, Is.True);
            Assert.That(result.Checks, Has.All.Matches<BalanceAcceptanceCheck>(value => value.Passed));
        }

        [Test]
        public void AcceptanceEvaluator_FailingFixtureRejectsEveryThresholdViolation()
        {
            var metrics = new BalanceAcceptanceMetrics(
                60d,
                1,
                0.549d,
                0.501d,
                89.9d,
                150.1d,
                0.149d,
                0.149d);

            BalanceAcceptanceResult result = BalanceAcceptanceEvaluator.Evaluate(metrics);

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Checks, Has.Count.EqualTo(8));
            Assert.That(result.Checks, Has.All.Matches<BalanceAcceptanceCheck>(value => !value.Passed));
            Assert.That(result.FailureSummary, Does.Contain("100 games < 60s"));
            Assert.That(result.FailureSummary, Does.Contain("A win rate, stage 1"));
            Assert.That(result.FailureSummary, Does.Contain("Heavy armor share main_20_stage_5"));
        }

        /// <summary>
        /// The real gate: a hundred games of live content against the locked WO-E1 targets. WO-F1
        /// removed the <c>[Ignore]</c> this carried while the targets were unmet.
        /// </summary>
        [Test, Timeout(180000)]
        public void DefaultHundredGameContent_WritesReviewArtifactsAndEvaluatesLockedTargets()
        {
            string output = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Docs",
                "Plan",
                "REVIEW",
                "WO-F1-Results");
            BalanceRunRequest request = BalanceRunRequest.CreateDefault(25, output);
            BalanceRunReport report = new BalanceRunner().Run(request);
            BalanceCohortSummary stageOne = report.Cohorts.Single(value =>
                value.LineupId == BalanceReferenceLineups.StageOneGateLineupId && value.StageIndex == 1);
            BalanceCohortSummary stageFive = report.Cohorts.Single(value =>
                value.LineupId == BalanceReferenceLineups.StageFiveGateLineupId && value.StageIndex == 5);
            BalanceArmorDistribution[] targetArmor = report.ArmorDistributions
                .Where(value =>
                    value.Phase == "ALL"
                    && (value.WaveSetId == "main_20" || value.WaveSetId == "main_20_stage_5"))
                .ToArray();
            BalanceAcceptanceResult acceptance = BalanceAcceptanceEvaluator.Evaluate(report);

            Assert.That(report.Battles, Has.Count.EqualTo(request.TotalGames));
            Assert.That(targetArmor, Has.Length.EqualTo(2));
            Assert.That(stageOne, Is.Not.Null);
            Assert.That(stageFive, Is.Not.Null);
            Assert.That(acceptance.Passed, Is.True, acceptance.FailureSummary);

            // WO-F1 §C: the no-siege route must be possible but clearly worse, not impossible.
            BalanceCohortSummary noSiege = report.Cohorts.Single(value =>
                value.LineupId == "B_real_s5_nosiege");
            Assert.That(noSiege.WinRate, Is.GreaterThan(0d),
                "a siege-free stage-five board must be able to win at all");
            Assert.That(noSiege.WinRate, Is.LessThan(stageFive.WinRate),
                "…and must still be clearly worse than the board that brought siege");
        }

        private static void TickTo(BattleSystem system, double targetSeconds)
        {
            while (!system.IsSettled && system.SimulatedTimeSeconds < targetSeconds)
            {
                system.Tick(system.FixedDeltaTime);
            }
        }

        private string CreateTemporaryDirectory()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "HanziDefend-WO-F1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            temporaryDirectories.Add(path);
            return path;
        }

        private static BalanceLineup FastLineup()
        {
            return new BalanceLineup(
                "A_fast_siege",
                "A fast siege fixture",
                true,
                new[]
                {
                    new BalanceLineupSpawn("nub", 1, 2, 2)
                });
        }

        private static GameConfig CreateFastConfig()
        {
            GameConfig config = GameConfig.Load();
            LevelDef level = config.GetLevel("level_1_1");
            level.BaseHp = 1000000f;
            config.Bases.Ally.Armor = 0f;
            config.Bases.Enemy.Hp = 1f;
            config.Bases.Enemy.Armor = 0f;
            config.Economy.Battle.TargetSearchRadius = 200f;
            config.GetWaveSet(level.WaveSetId).Waves = new[]
            {
                new WaveDef
                {
                    Index = 1,
                    Act = WaveActs.Third,
                    RewardRank = EnemyRank.Boss,
                    DelaySec = 0.1f,
                    Spawns = new[]
                    {
                        new WaveSpawnDef
                        {
                            UnitId = "bld_cheng",
                            Level = 1,
                            Count = 1,
                            SpreadX = 0f,
                            IntervalSec = 0f
                        }
                    }
                }
            };

            UnitDef ally = config.GetUnit("nub");
            SetCurve(ally.Hp, 1000000f);
            SetCurve(ally.Atk, 1000000f);
            SetCurve(ally.Range, 200f);
            SetCurve(ally.MinRange, 0f);
            SetCurve(ally.AtkSpeed, 30f);
            SetCurve(ally.Pierce, 100f);
            SetCurve(ally.MoveSpeed, 0f);
            // The fast fixture measures harness plumbing, not entry pacing: an entry cooldown here
            // would just add dead ticks to every one of the hundred games.
            SetCurve(ally.Cooldown, 0f);
            ally.Traits = Array.Empty<UnitTraitDef>();

            BossDef boss = config.GetBoss("bld_cheng");
            boss.Hp = 1f;
            boss.Armor = 0f;
            boss.AtkSpeed = 0f;
            return config;
        }

        private static void AssertLineupFitsItsMask(GameConfig config, BalanceLineup lineup)
        {
            LevelDef level = config.GetLevel(lineup.LevelIds[0]);
            IReadOnlyList<GridCoordinate> cells = BalanceRunner.ResolveUnlockedCells(level, lineup);
            var grid = new DeploymentGrid(
                level.GridWidth,
                level.GridHeight,
                cells,
                DeploymentGridOrientation.ColumnsHorizontal,
                config.Economy.CardPool,
                config.Economy.GridUnlock.BaseAnchorRowOffset);
            int occupiedCells = 0;
            for (int index = 0; index < lineup.Spawns.Count; index++)
            {
                BalanceLineupSpawn spawn = lineup.Spawns[index];
                UnitFootprint footprint = UnitFootprint.FromDefinition(config.GetUnit(spawn.UnitId));
                var incoming = new DeploymentUnit(
                    $"test:{lineup.Id}:{index}",
                    spawn.UnitId,
                    spawn.Level,
                    footprint);
                DeploymentEvaluation evaluation = grid.Evaluate(incoming, spawn.Anchor);
                Assert.That(evaluation.IsValid, Is.True, $"{lineup.Id}: {evaluation.Message}");
                Assert.That(evaluation.Action, Is.EqualTo(DeploymentActionKind.Place), lineup.Id);
                grid.Apply(incoming, spawn.Anchor);
                occupiedCells += footprint.OccupiedCellCount;
            }

            Assert.That(occupiedCells, Is.LessThanOrEqualTo(cells.Count), lineup.Id);
        }

        private static void SetCurve(StatCurve curve, float value)
        {
            curve.Base = value;
            curve.Growth = 0f;
        }

        private static string BattleSignature(BalanceBattleResult battle)
        {
            string entities = string.Join(";", battle.Entities.Select(value => string.Join(":",
                value.EntityId,
                value.UnitId,
                value.Team,
                value.Damage,
                value.SpawnTimeSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                value.EndTimeSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                value.Survived)));
            string coins = string.Join(";", battle.CoinCurve.Select(value => string.Join(":",
                value.TimeSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                value.EventKind,
                value.SourceUnitId,
                value.Delta,
                value.TotalCoins)));
            return string.Join("|",
                battle.LineupId,
                battle.LevelId,
                battle.GameIndex,
                battle.Seed,
                battle.Result,
                battle.DurationSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                battle.TickCount,
                battle.EndCoins,
                battle.AllyBaseHp.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                battle.BossHp.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                entities,
                coins);
        }

        private sealed class RecordingDeploymentEvents : IBattleEncounterEvents, IBattleDeploymentEvents
        {
            internal List<string> EntryOrder { get; } = new List<string>();

            internal Dictionary<string, double> EntrySeconds { get; } =
                new Dictionary<string, double>(StringComparer.Ordinal);

            public void AllyDeploymentQueued(AllyDeploymentQueuedEvent eventData)
            {
            }

            public void AllyDeploymentEntered(AllyDeploymentEnteredEvent eventData)
            {
                EntryOrder.Add(eventData.DefinitionId);
                EntrySeconds[eventData.DefinitionId] = eventData.SimulatedTimeSeconds;
            }

            public void UnitSpawned(UnitSpawnedEvent eventData)
            {
            }

            public void UnitAttacked(UnitAttackedEvent eventData)
            {
            }

            public void DamageDealt(DamageDealtEvent eventData)
            {
            }

            public void UnitDied(UnitDiedEvent eventData)
            {
            }

            public void CoinDropped(CoinDroppedEvent eventData)
            {
            }

            public void WaveStarted(WaveStartedEvent eventData)
            {
            }

            public void BossSpawned(BossSpawnedEvent eventData)
            {
            }

            public void BaseDamaged(BaseDamagedEvent eventData)
            {
            }

            public void BattleSettled(BattleSettledEvent eventData)
            {
            }
        }
    }
}
