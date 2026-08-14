using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HanziDefend.Tests.PlayMode
{
    [TestFixture]
    public sealed class BattleEncounterTests
    {
        private const string LevelId = "level_1_1";
        private const uint Seed = 0xB2002026u;
        private readonly List<BattleSystem> systems = new List<BattleSystem>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = systems.Count - 1; index >= 0; index--)
            {
                systems[index].Dispose();
            }

            systems.Clear();
            yield return null;
        }

        [Test]
        public void CreateEncounter_RejectsUnknownLevel()
        {
            GameConfig config = GameConfig.Load();

            Assert.That(
                () => BattleSystem.CreateEncounter(config, "missing_level", Seed),
                Throws.TypeOf<KeyNotFoundException>().With.Message.Contains("missing_level"));
        }

        [Test]
        public void CreateEncounter_BasesUseSelectedLevelHpAndCatalogArmor()
        {
            GameConfig config = GameConfig.Load();
            LevelDef level = config.GetLevel(LevelId);
            BattleSystem system = CreateEncounter(config);

            BattleBaseSnapshot ally = system.GetBaseSnapshot(system.AllyBaseEntityId);
            BattleBaseSnapshot enemy = system.GetBaseSnapshot(system.EnemyBaseEntityId);
            Assert.That(ally.Team, Is.EqualTo(BattleTeam.Ally));
            Assert.That(ally.DefinitionId, Is.EqualTo(config.Bases.Ally.Id));
            Assert.That(ally.UnitType, Is.EqualTo(UnitType.Building));
            Assert.That(ally.ArmorType, Is.EqualTo(ArmorType.Building));
            Assert.That(ally.MaxHp, Is.EqualTo(level.BaseHp));
            Assert.That(ally.CurrentHp, Is.EqualTo(level.BaseHp));
            Assert.That(ally.Armor, Is.EqualTo(config.Bases.Ally.Armor));
            Assert.That(ally.Position, Is.EqualTo(new Vector2(
                config.Economy.Battle.AllyBasePosition.X,
                config.Economy.Battle.AllyBasePosition.Y)));
            Assert.That(enemy.Team, Is.EqualTo(BattleTeam.Enemy));
            Assert.That(enemy.DefinitionId, Is.EqualTo(config.Bases.Enemy.Id));
            Assert.That(enemy.UnitType, Is.EqualTo(UnitType.Building));
            Assert.That(enemy.ArmorType, Is.EqualTo(ArmorType.Building));
            Assert.That(enemy.MaxHp, Is.EqualTo(config.Bases.Enemy.Hp));
            Assert.That(enemy.CurrentHp, Is.EqualTo(config.Bases.Enemy.Hp));
            Assert.That(enemy.Armor, Is.EqualTo(config.Bases.Enemy.Armor));
            Assert.That(enemy.Position, Is.EqualTo(new Vector2(
                config.Economy.Battle.EnemyBasePosition.X,
                config.Economy.Battle.EnemyBasePosition.Y)));
        }

        [Test]
        public void CreateEncounter_ExposesAllTwentyConfiguredWaves()
        {
            BattleSystem system = CreateEncounter(GameConfig.Load());

            Assert.That(system.TotalWaveCount, Is.EqualTo(20));
            Assert.That(system.CurrentWaveIndex, Is.Zero);
            Assert.That(system.Result, Is.EqualTo(BattleResult.None));
            Assert.That(system.IsSettled, Is.False);
        }

        [Test]
        public void Scheduler_WaitsForFirstWaveDelayBeforePublishingOrSpawning()
        {
            GameConfig config = GameConfig.Load();
            DisableCombat(config);
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);
            // Wave 1's shape is read back from waves.json rather than hard-coded. WO-E1 rebuilt the
            // timeline, so this fixture has to track the data source instead of a snapshot of it.
            WaveDef firstWave = config.GetWaveSet(config.GetLevel(LevelId).WaveSetId).Waves[0];
            string[] expectedSpawnIds = firstWave.Spawns.Select(value => value.UnitId).ToArray();
            Assert.That(firstWave.DelaySec, Is.GreaterThan(0.05f),
                "The first wave needs a measurable delay for this boundary to mean anything.");

            system.Tick(firstWave.DelaySec - 0.01f);
            Assert.That(recorder.Waves, Is.Empty);
            Assert.That(recorder.Spawns, Is.Empty);

            system.Tick(0.02f);
            Assert.That(recorder.Waves.Select(value => value.WaveIndex),
                Is.EqualTo(new[] { firstWave.Index }));
            // Groups run in parallel: every group emits its ordinal 0 at the wave start, and any
            // further ordinals wait out intervalSec, so exactly one spawn per group is due here.
            Assert.That(recorder.Spawns, Has.Count.EqualTo(expectedSpawnIds.Length));
            Assert.That(recorder.Spawns.Select(value => value.DefinitionId),
                Is.EqualTo(expectedSpawnIds));
            Assert.That(recorder.Spawns.Select(value => value.Team),
                Is.All.EqualTo(BattleTeam.Enemy),
                "Shared-pool definitions spawned by a wave must use the wave's enemy team.");
            Assert.That(recorder.Kinds.Take(1 + expectedSpawnIds.Length),
                Is.EqualTo(new[] { "Wave" }.Concat(expectedSpawnIds.Select(_ => "Spawn"))));
        }

        [Test]
        public void Scheduler_DelayIsRelativeToPreviousWaveStart()
        {
            GameConfig config = GameConfig.Load();
            DisableCombat(config);
            SetWaves(config,
                Wave(1, 0.1f, Spawn("zu")),
                Wave(2, 0.2f, Spawn("zu")),
                Wave(3, 0.3f, Spawn("zu")));
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);

            TickUntilTime(system, 0.7d);

            Assert.That(recorder.Waves.Select(value => value.WaveIndex), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(recorder.Waves[0].SimulatedTimeSeconds, Is.EqualTo(0.1d).Within(system.FixedDeltaTime));
            Assert.That(recorder.Waves[1].SimulatedTimeSeconds, Is.EqualTo(0.3d).Within(system.FixedDeltaTime));
            Assert.That(recorder.Waves[2].SimulatedTimeSeconds, Is.EqualTo(0.6d).Within(system.FixedDeltaTime));
            Assert.That(recorder.Waves.Select(value => value.ScheduledTimeSeconds).ToArray(),
                Is.EqualTo(new[] { 0.1d, 0.3d, 0.6d }).Within(0.000001d));
        }

        [Test]
        public void Scheduler_IntervalSchedulesEachBatchMemberIndividually()
        {
            GameConfig config = GameConfig.Load();
            DisableCombat(config);
            SetWaves(config, Wave(1, 0f, Spawn("zu", 3, 0f, 0.5f)));
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);

            system.Tick(0.01f);
            Assert.That(recorder.Spawns, Has.Count.EqualTo(1));
            system.Tick(0.50f);
            Assert.That(recorder.Spawns, Has.Count.EqualTo(2));
            system.Tick(0.50f);
            Assert.That(recorder.Spawns, Has.Count.EqualTo(3));
            Assert.That(recorder.Spawns.Select(value => value.SimulatedTimeSeconds), Is.Ordered);
        }

        [Test]
        public void Scheduler_SpreadXDistributesWholeBatchInsideConfiguredWidth()
        {
            const float spreadX = 2f;
            GameConfig config = GameConfig.Load();
            DisableCombat(config);
            SetWaves(config, Wave(1, 0f, Spawn("zu", 5, spreadX, 0f)));
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);

            system.Tick(system.FixedDeltaTime);

            float[] x = recorder.Spawns.Select(value => value.Position.x).OrderBy(value => value).ToArray();
            float centerX = config.Economy.Battle.EnemySpawnCenter.X;
            Assert.That(x, Has.Length.EqualTo(5));
            Assert.That(x.Distinct().Count(), Is.EqualTo(5));
            Assert.That(x.All(value => !float.IsNaN(value) && !float.IsInfinity(value)), Is.True);
            Assert.That(x, Has.All.InRange(centerX - spreadX, centerX + spreadX));
        }

        [Test]
        public void Scheduler_SpreadXUsesBattleSeedDeterministically()
        {
            GameConfig firstConfig = GameConfig.Load();
            GameConfig secondConfig = GameConfig.Load();
            DisableCombat(firstConfig);
            DisableCombat(secondConfig);
            SetWaves(firstConfig, Wave(1, 0f, Spawn("zu", 5, 2f, 0f)));
            SetWaves(secondConfig, Wave(1, 0f, Spawn("zu", 5, 2f, 0f)));
            var firstRecorder = new RecordingEncounterEvents();
            var secondRecorder = new RecordingEncounterEvents();
            BattleSystem first = BattleSystem.CreateEncounter(firstConfig, LevelId, Seed, firstRecorder);
            BattleSystem second = BattleSystem.CreateEncounter(secondConfig, LevelId, Seed + 1u, secondRecorder);
            systems.Add(first);
            systems.Add(second);

            first.Tick(first.FixedDeltaTime);
            second.Tick(second.FixedDeltaTime);

            Assert.That(firstRecorder.Spawns.Select(value => value.Position.x).ToArray(),
                Is.Not.EqualTo(secondRecorder.Spawns.Select(value => value.Position.x).ToArray()));
        }

        [Test]
        public void Scheduler_RushBaseEnemyTargetsAllyBaseAndBypassesUnits()
        {
            GameConfig config = GameConfig.Load();
            DisableCombat(config);
            SetWaves(config, Wave(1, 0f, Spawn("e_lang")));
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);
            Position2Def spawn = config.Economy.Battle.EnemySpawnCenter;
            int blockerId = system.Spawn(new UnitSpawnRequest(
                "dun", 1, new Vector2(spawn.X, spawn.Y - 0.25f)));

            Assert.That(() => system.Tick(system.FixedDeltaTime), Throws.Nothing);
            BattleUnitSnapshot wolf = system.CaptureSnapshot().Single(value => value.DefinitionId == "e_lang");
            Assert.That(wolf.Targeting, Is.EqualTo(TargetingMode.RushBase));
            Assert.That(wolf.TargetEntityId, Is.EqualTo(system.AllyBaseEntityId));
            Assert.That(system.GetUnitSnapshot(blockerId).CurrentHp,
                Is.EqualTo(system.GetUnitSnapshot(blockerId).Stats.MaxHp));
        }

        [Test]
        public void BossSpawn_UsesBossDefinitionStatsAndPublishesBossEvent()
        {
            GameConfig config = GameConfig.Load();
            BossDef definition = config.GetBoss("bld_cheng");
            definition.AtkSpeed = 0f;
            SetWaves(config, Wave(20, 0f, Spawn("bld_cheng")));
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);

            system.Tick(system.FixedDeltaTime);

            BossSpawnedEvent spawned = recorder.Bosses.Single();
            Assert.That(system.BossEntityId, Is.EqualTo(spawned.EntityId));
            Assert.That(spawned.DefinitionId, Is.EqualTo(definition.Id));
            Assert.That(spawned.Stats.MaxHp, Is.EqualTo(definition.Hp));
            Assert.That(spawned.Stats.Atk, Is.EqualTo(definition.Atk));
            Assert.That(spawned.Stats.Range, Is.EqualTo(definition.Range));
            Assert.That(spawned.Stats.AtkSpeed, Is.EqualTo(definition.AtkSpeed));
            Assert.That(spawned.Stats.Armor, Is.EqualTo(definition.Armor));
            Assert.That(spawned.Stats.Pierce, Is.EqualTo(definition.Pierce));
            Assert.That(spawned.Position, Is.EqualTo(new Vector2(
                config.Economy.Battle.EnemySpawnCenter.X,
                config.Economy.Battle.EnemySpawnCenter.Y)));
            BattleUnitSnapshot boss = system.GetUnitSnapshot(spawned.EntityId);
            Assert.That(boss.Team, Is.EqualTo(BattleTeam.Enemy));
            Assert.That(boss.UnitType, Is.EqualTo(UnitType.Building));
            Assert.That(boss.ArmorType, Is.EqualTo(ArmorType.Building));
            Assert.That(boss.AttackType, Is.EqualTo(definition.AtkType));
        }

        [Test]
        public void EnemyAttack_BaseIsLegalTargetAndDamageUsesFormula()
        {
            GameConfig config = GameConfig.Load();
            UnitDef enemy = config.GetUnit("zu");
            SetBaseKiller(enemy, config);
            SetWaves(config, Wave(1, 0f, Spawn(enemy.Id)));
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);

            system.Tick(system.FixedDeltaTime);

            BaseDamagedEvent damage = recorder.BaseDamage.Single();
            Assert.That(damage.BaseEntityId, Is.EqualTo(system.AllyBaseEntityId));
            Assert.That(damage.BaseTeam, Is.EqualTo(BattleTeam.Ally));
            Assert.That(damage.Amount,
                Is.EqualTo(Formula.Damage(
                    enemy.Atk.Base,
                    enemy.AtkType,
                    enemy.BonusVs,
                    config.Bases.Ally.Armor,
                    config.Bases.Ally.ArmorType,
                    config.Bases.Ally.UnitType,
                    enemy.Pierce.Base,
                    config.Economy)));
            Assert.That(damage.HpAfter, Is.EqualTo(damage.HpBefore - damage.Amount));
        }

        [Test]
        public void Settlement_BossDeathImmediatelyProducesWin()
        {
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateAnchorDuel(allyBaseHp: 1000f, allyAttack: 100f, bossHp: 1f, bossAttack: 1f, recorder);

            AdvanceAnchorDuel(system);

            Assert.That(system.Result, Is.EqualTo(BattleResult.Win));
            Assert.That(system.IsSettled, Is.True);
            Assert.That(recorder.Settlements.Select(value => value.Result), Is.EqualTo(new[] { BattleResult.Win }));
        }

        [Test]
        public void Settlement_AllyBaseDeathImmediatelyProducesLose()
        {
            GameConfig config = GameConfig.Load();
            config.GetLevel(LevelId).BaseHp = 1f;
            config.Bases.Ally.Armor = 0f;
            BossDef boss = config.GetBoss("bld_cheng");
            boss.Atk = 100f;
            boss.Range = 100f;
            boss.AtkSpeed = 30f;
            boss.Pierce = 100f;
            SetWaves(config, Wave(20, 0f, Spawn(boss.Id)));
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);

            system.Tick(system.FixedDeltaTime);

            Assert.That(system.GetBaseSnapshot(system.AllyBaseEntityId).CurrentHp, Is.Zero);
            Assert.That(system.Result, Is.EqualTo(BattleResult.Lose));
            Assert.That(recorder.Settlements.Select(value => value.Result), Is.EqualTo(new[] { BattleResult.Lose }));
        }

        [Test]
        public void Settlement_SameTickAllyBaseAndBossReachZero_WinPriorityAndOneResult()
        {
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateAnchorDuel(allyBaseHp: 1f, allyAttack: 100f, bossHp: 1f, bossAttack: 100f, recorder);

            AdvanceAnchorDuel(system);

            Assert.That(system.GetBaseSnapshot(system.AllyBaseEntityId).CurrentHp, Is.Zero);
            Assert.That(system.GetUnitSnapshot(system.BossEntityId.Value).CurrentHp, Is.Zero);
            Assert.That(system.Result, Is.EqualTo(BattleResult.Win));
            Assert.That(recorder.Settlements, Has.Count.EqualTo(1));
        }

        [Test]
        public void Settlement_AfterResultFurtherTicksFreezeAllObservableState()
        {
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateAnchorDuel(allyBaseHp: 1000f, allyAttack: 100f, bossHp: 1f, bossAttack: 1f, recorder);
            AdvanceAnchorDuel(system);
            string before = CaptureState(system, recorder);

            for (int index = 0; index < 30; index++)
            {
                system.Tick(system.FixedDeltaTime);
            }

            Assert.That(CaptureState(system, recorder), Is.EqualTo(before));
        }

        [Test]
        public void TrySettle_IsIdempotentAndNeverPublishesSecondResult()
        {
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateAnchorDuel(allyBaseHp: 1000f, allyAttack: 100f, bossHp: 1f, bossAttack: 1f, recorder);
            AdvanceAnchorDuel(system);

            BattleResult first = system.TrySettle();
            BattleResult second = system.TrySettle();

            Assert.That(first, Is.EqualTo(BattleResult.Win));
            Assert.That(second, Is.EqualTo(BattleResult.Win));
            Assert.That(recorder.Settlements, Has.Count.EqualTo(1));
        }

        [Test]
        public void EncounterEvents_KeepOneContiguousSequenceAcrossLifecycleExtensions()
        {
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateAnchorDuel(allyBaseHp: 1000f, allyAttack: 100f, bossHp: 1f, bossAttack: 1f, recorder);

            AdvanceAnchorDuel(system);

            Assert.That(recorder.Kinds, Does.Contain("Wave"));
            Assert.That(recorder.Kinds, Does.Contain("Boss"));
            Assert.That(recorder.Kinds, Does.Contain("Settled"));
            Assert.That(recorder.Sequences, Is.EqualTo(Enumerable.Range(1, recorder.Sequences.Count).Select(value => (long)value)));
            Assert.That(recorder.Kinds.Last(), Is.EqualTo("Settled"));
        }

        [Test]
        public void FullMain20_CompletesUnderTwoSecondsWithTimelineAndRewardRanks()
        {
            GameConfig config = GameConfig.Load();
            WaveDef[] configuredWaves = config
                .GetWaveSet(config.GetLevel(LevelId).WaveSetId)
                .Waves;
            var waveStarts = new Dictionary<int, double>();
            double waveStart = 0d;
            foreach (WaveDef wave in configuredWaves)
            {
                waveStart += wave.DelaySec;
                waveStarts.Add(wave.Index, waveStart);
            }

            double bossStart = waveStarts[configuredWaves.Single(value => value.RewardRank == EnemyRank.Boss).Index];
            var expectedSpawns = new Dictionary<EnemyRank, int>();
            foreach (WaveDef wave in configuredWaves)
            foreach (WaveSpawnDef spawn in wave.Spawns)
            for (int ordinal = 0; ordinal < spawn.Count; ordinal++)
            {
                double scheduledTime = waveStarts[wave.Index] + ordinal * spawn.IntervalSec;
                if (scheduledTime <= bossStart + 0.000001d)
                {
                    expectedSpawns.TryGetValue(wave.RewardRank, out int count);
                    expectedSpawns[wave.RewardRank] = count + 1;
                }
            }
            ConfigureFastFullEncounter(config);
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);
            SpawnFastAllies(system, 3);
            var stopwatch = Stopwatch.StartNew();

            TickUntilSettled(system, SettleTickBudget(config, bossStart));
            stopwatch.Stop();

            string timeline = string.Join(", ", recorder.Waves.Select(value => $"W{value.WaveIndex}@{value.SimulatedTimeSeconds:F3}s"));
            TestContext.WriteLine($"{timeline}, Boss@{recorder.Bosses.Single().SimulatedTimeSeconds:F3}s, "
                                  + $"Settle@{recorder.Settlements.Single().SimulatedTimeSeconds:F3}s={system.Result}, "
                                  + $"wall={stopwatch.Elapsed.TotalSeconds:F3}s");
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(2d));
            Assert.That(system.Result, Is.EqualTo(BattleResult.Win));
            Assert.That(recorder.Waves.Select(value => value.WaveIndex), Is.EqualTo(Enumerable.Range(1, 20)));
            Assert.That(recorder.Bosses, Has.Count.EqualTo(1));
            foreach (KeyValuePair<EnemyRank, int> pair in expectedSpawns)
            {
                Assert.That(recorder.Spawns.Count(value =>
                        value.WaveContext.HasValue && value.WaveContext.Value.RewardRank == pair.Key),
                    Is.EqualTo(pair.Value), pair.Key.ToString());
            }

            Assert.That(recorder.Deaths.Count(value => value.RewardRank == EnemyRank.Boss), Is.EqualTo(1));
        }

        [Test]
        public void FullMain20_SameSeedProducesIdenticalEventStreamEntryByEntry()
        {
            IReadOnlyList<string> first = RunFullMain20(0x1A2B3C4Du);
            IReadOnlyList<string> second = RunFullMain20(0x1A2B3C4Du);

            Assert.That(first.Count, Is.GreaterThan(500));
            Assert.That(second, Is.EqualTo(first));
        }

        private BattleSystem CreateEncounter(GameConfig config, IBattleEvents battleEvents = null)
        {
            BattleSystem system = BattleSystem.CreateEncounter(config, LevelId, Seed, battleEvents);
            systems.Add(system);
            return system;
        }

        private BattleSystem CreateAnchorDuel(
            float allyBaseHp,
            float allyAttack,
            float bossHp,
            float bossAttack,
            RecordingEncounterEvents recorder)
        {
            GameConfig config = GameConfig.Load();
            config.Economy.Battle.TargetSearchRadius = 200f;
            config.GetLevel(LevelId).BaseHp = allyBaseHp;
            config.Bases.Ally.Armor = 0f;
            config.Bases.Enemy.Hp = 1f;
            config.Bases.Enemy.Armor = 0f;

            UnitDef ally = config.GetUnit("gong");
            SetCurve(ally.Hp, 1000f);
            SetCurve(ally.Atk, allyAttack);
            SetCurve(ally.Range, 200f);
            SetCurve(ally.AtkSpeed, 30f);
            SetCurve(ally.Armor, 0f);
            SetCurve(ally.Pierce, 100f);
            SetCurve(ally.MoveSpeed, 0f);

            BossDef boss = config.GetBoss("bld_cheng");
            boss.Hp = bossHp;
            boss.Armor = 0f;
            boss.Atk = bossAttack;
            boss.Range = 200f;
            boss.AtkSpeed = 30f;
            boss.Pierce = 100f;
            SetWaves(config, Wave(20, 0.2f, Spawn(boss.Id)));

            BattleSystem system = CreateEncounter(config, recorder);
            Vector2 basePosition = system.GetBaseSnapshot(system.AllyBaseEntityId).Position;
            system.Spawn(new UnitSpawnRequest(ally.Id, 1, basePosition));
            return system;
        }

        private static void AdvanceAnchorDuel(BattleSystem system)
        {
            system.Tick(0.01f);
            Assert.That(system.GetBaseSnapshot(system.EnemyBaseEntityId).IsDestroyed, Is.True,
                "The setup shot must remove the enemy base before the boss appears.");
            system.Tick(0.20f);
        }

        private IReadOnlyList<string> RunFullMain20(uint seed)
        {
            GameConfig config = GameConfig.Load();
            ConfigureFastFullEncounter(config);
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = BattleSystem.CreateEncounter(config, LevelId, seed, recorder);
            systems.Add(system);
            SpawnFastAllies(system, 3);
            TickUntilSettled(system, SettleTickBudget(config, BossStartSeconds(config)));
            Assert.That(system.Result, Is.EqualTo(BattleResult.Win));
            string[] result = recorder.CanonicalEvents.ToArray();
            system.Dispose();
            systems.Remove(system);
            return result;
        }

        private static void ConfigureFastFullEncounter(GameConfig config)
        {
            config.Economy.Battle.TargetSearchRadius = 200f;
            config.GetLevel(LevelId).BaseHp = 1000000f;
            config.Bases.Ally.Armor = 0f;
            config.Bases.Enemy.Hp = 1f;
            config.Bases.Enemy.Armor = 0f;
            HashSet<string> waveUnitIds = config.GetWaveSet(config.GetLevel(LevelId).WaveSetId)
                .Waves
                .SelectMany(value => value.Spawns)
                .Select(value => value.UnitId)
                .Where(value => config.UnitsById.ContainsKey(value))
                .ToHashSet();
            foreach (string unitId in waveUnitIds)
            {
                UnitDef enemy = config.GetUnit(unitId);
                SetCurve(enemy.Hp, 1f);
                SetCurve(enemy.Armor, 0f);
                SetCurve(enemy.AtkSpeed, 0f);
                SetCurve(enemy.MoveSpeed, 0f);
            }

            UnitDef ally = config.GetUnit("nuc");
            SetCurve(ally.Hp, 1000000f);
            SetCurve(ally.Atk, 1000000f);
            SetCurve(ally.Range, 200f);
            SetCurve(ally.MinRange, 0f);
            SetCurve(ally.AtkSpeed, 30f);
            SetCurve(ally.Pierce, 100f);
            SetCurve(ally.MoveSpeed, 0f);

            BossDef boss = config.GetBoss("bld_cheng");
            boss.Hp = 1f;
            boss.Armor = 0f;
            boss.AtkSpeed = 0f;
        }

        private static void SpawnFastAllies(BattleSystem system, int count)
        {
            Vector2 basePosition = system.GetBaseSnapshot(system.AllyBaseEntityId).Position;
            for (int index = 0; index < count; index++)
            {
                system.Spawn(new UnitSpawnRequest("nuc", 1, basePosition + new Vector2(index * 0.7f, 0f)));
            }
        }

        /// <summary>
        /// Simulated seconds the encounter is allowed to take after the boss has appeared. The
        /// tick ceiling is derived from waves.json rather than hard-coded, because WO-E1 moved the
        /// main_20 boss from 62s to 94.4s and silently made the old fixed 2100-tick (70s) budget
        /// mathematically unreachable. Only this grace window is a judgement call; the rest of the
        /// budget is whatever the data source says.
        /// </summary>
        private const double SettleGraceSeconds = 15d;

        private static double BossStartSeconds(GameConfig config)
        {
            double elapsed = 0d;
            double bossStart = 0d;
            foreach (WaveDef wave in config.GetWaveSet(config.GetLevel(LevelId).WaveSetId).Waves)
            {
                elapsed += wave.DelaySec;
                if (wave.RewardRank == EnemyRank.Boss)
                {
                    bossStart = elapsed;
                }
            }

            return bossStart;
        }

        private static int SettleTickBudget(GameConfig config, double bossStartSeconds)
        {
            return checked((int)Math.Ceiling(
                (bossStartSeconds + SettleGraceSeconds) * config.Economy.Battle.TickRateHz));
        }

        private static void TickUntilSettled(BattleSystem system, int maximumTicks)
        {
            for (int index = 0; index < maximumTicks && !system.IsSettled; index++)
            {
                system.Tick(system.FixedDeltaTime);
            }

            Assert.That(system.IsSettled, Is.True, $"Encounter did not settle after {maximumTicks} ticks.");
        }

        private static void TickUntilTime(BattleSystem system, double targetSeconds)
        {
            while (system.SimulatedTimeSeconds + 0.0000001d < targetSeconds)
            {
                system.Tick(system.FixedDeltaTime);
            }
        }

        private static void DisableCombat(GameConfig config)
        {
            config.GetLevel(LevelId).BaseHp = 1000000f;
            foreach (UnitDef enemy in config.Units)
            {
                SetCurve(enemy.AtkSpeed, 0f);
                SetCurve(enemy.MoveSpeed, 0f);
            }

            config.GetBoss("bld_cheng").AtkSpeed = 0f;
        }

        private static void SetBaseKiller(UnitDef enemy, GameConfig config)
        {
            config.Economy.Battle.TargetSearchRadius = 200f;
            config.GetLevel(LevelId).BaseHp = 1000f;
            SetCurve(enemy.Atk, 100f);
            SetCurve(enemy.Range, 200f);
            SetCurve(enemy.AtkSpeed, 30f);
            SetCurve(enemy.Pierce, 100f);
            SetCurve(enemy.MoveSpeed, 0f);
        }

        private static void SetWaves(GameConfig config, params WaveDef[] waves)
        {
            config.GetWaveSet(config.GetLevel(LevelId).WaveSetId).Waves = waves;
        }

        private static WaveDef Wave(int index, float delaySec, params WaveSpawnDef[] spawns)
        {
            EnemyRank rewardRank = index == 20
                ? EnemyRank.Boss
                : index == 19 ? EnemyRank.Elite : EnemyRank.Normal;
            return new WaveDef
            {
                Index = index,
                RewardRank = rewardRank,
                DelaySec = delaySec,
                Spawns = spawns
            };
        }

        private static WaveSpawnDef Spawn(
            string definitionId,
            int count = 1,
            float spreadX = 0f,
            float intervalSec = 0f)
        {
            return new WaveSpawnDef
            {
                UnitId = definitionId,
                Level = 1,
                Count = count,
                SpreadX = spreadX,
                IntervalSec = intervalSec
            };
        }

        private static void SetCurve(StatCurve curve, float baseValue)
        {
            curve.Base = baseValue;
            curve.Growth = 0f;
        }

        private static string CaptureState(BattleSystem system, RecordingEncounterEvents recorder)
        {
            string units = string.Join(";", system.CaptureSnapshot().Select(value => string.Join(",",
                value.EntityId,
                value.DefinitionId,
                value.State,
                F(value.CurrentHp),
                V(value.Position),
                value.TargetEntityId?.ToString(CultureInfo.InvariantCulture) ?? "-")));
            BattleBaseSnapshot ally = system.GetBaseSnapshot(system.AllyBaseEntityId);
            BattleBaseSnapshot enemy = system.GetBaseSnapshot(system.EnemyBaseEntityId);
            RngStreamsState rng = system.SaveRngStreamsState();
            return string.Join("|",
                system.Result,
                system.TickIndex,
                D(system.SimulatedTimeSeconds),
                system.CurrentWaveIndex,
                system.DroppedCoins,
                rng.battle.state,
                rng.cardDraw.state,
                rng.settlement.state,
                F(ally.CurrentHp),
                F(enemy.CurrentHp),
                units,
                string.Join("\n", recorder.CanonicalEvents));
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string D(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string V(Vector2 value)
        {
            return string.Concat(F(value.x), ",", F(value.y));
        }

        private sealed class RecordingEncounterEvents : IBattleEncounterEvents, IBattleEffectEvents
        {
            internal List<UnitSpawnedEvent> Spawns { get; } = new List<UnitSpawnedEvent>();
            internal List<UnitAttackedEvent> Attacks { get; } = new List<UnitAttackedEvent>();
            internal List<DamageDealtEvent> Damage { get; } = new List<DamageDealtEvent>();
            internal List<UnitDiedEvent> Deaths { get; } = new List<UnitDiedEvent>();
            internal List<CoinDroppedEvent> Coins { get; } = new List<CoinDroppedEvent>();
            internal List<WaveStartedEvent> Waves { get; } = new List<WaveStartedEvent>();
            internal List<BossSpawnedEvent> Bosses { get; } = new List<BossSpawnedEvent>();
            internal List<BaseDamagedEvent> BaseDamage { get; } = new List<BaseDamagedEvent>();
            internal List<BattleSettledEvent> Settlements { get; } = new List<BattleSettledEvent>();
            internal List<string> Kinds { get; } = new List<string>();
            internal List<long> Sequences { get; } = new List<long>();
            internal List<string> CanonicalEvents { get; } = new List<string>();

            public void UnitSpawned(UnitSpawnedEvent value)
            {
                Spawns.Add(value);
                Add("Spawn", value.Sequence, string.Join("|", value.TickIndex, D(value.SimulatedTimeSeconds),
                    value.EntityId, value.DefinitionId, value.Team, value.Level, Stats(value.Stats),
                    F(value.CurrentHp), V(value.Position), V(value.Facing), value.State,
                    Context(value.WaveContext)));
            }

            public void UnitAttacked(UnitAttackedEvent value)
            {
                Attacks.Add(value);
                Add("Attack", value.Sequence, string.Join("|", value.TickIndex, D(value.SimulatedTimeSeconds),
                    value.AttackId, value.AttackerEntityId, value.TargetEntityId, value.AttackerDefinitionId,
                    value.TargetDefinitionId, value.AttackerTeam, value.TargetTeam,
                    V(value.AttackerPosition), V(value.TargetPosition), V(value.Facing)));
            }

            public void DamageDealt(DamageDealtEvent value)
            {
                Damage.Add(value);
                Add("Damage", value.Sequence, string.Join("|", value.TickIndex, D(value.SimulatedTimeSeconds),
                    value.AttackId, value.SourceEntityId, value.TargetEntityId, value.Amount,
                    F(value.HpBefore), F(value.HpAfter), F(value.MaxHp), V(value.HitPosition), value.IsLethal));
            }

            public void UnitDied(UnitDiedEvent value)
            {
                Deaths.Add(value);
                Add("Died", value.Sequence, string.Join("|", value.TickIndex, D(value.SimulatedTimeSeconds),
                    value.EntityId, value.DefinitionId, value.Team, value.KillerEntityId,
                    V(value.Position), F(value.FinalHp), F(value.MaxHp), value.RewardRank?.ToString() ?? "-"));
            }

            public void CoinDropped(CoinDroppedEvent value)
            {
                Coins.Add(value);
                Add("Coin", value.Sequence, string.Join("|", value.TickIndex, D(value.SimulatedTimeSeconds),
                    value.SourceEntityId, value.SourceDefinitionId, value.Rank, value.Amount,
                    value.TotalCoins, V(value.Position)));
            }

            public void WaveStarted(WaveStartedEvent value)
            {
                Waves.Add(value);
                Add("Wave", value.Sequence, string.Join("|", value.TickIndex, D(value.SimulatedTimeSeconds),
                    D(value.ScheduledTimeSeconds), value.WaveSetId, value.WaveIndex, value.RewardRank));
            }

            public void BossSpawned(BossSpawnedEvent value)
            {
                Bosses.Add(value);
                Add("Boss", value.Sequence, string.Join("|", value.TickIndex, D(value.SimulatedTimeSeconds),
                    value.EntityId, value.DefinitionId, value.Level, Stats(value.Stats), V(value.Position),
                    Context(value.WaveContext)));
            }

            public void BaseDamaged(BaseDamagedEvent value)
            {
                BaseDamage.Add(value);
                Add("BaseDamage", value.Sequence, string.Join("|", value.TickIndex, D(value.SimulatedTimeSeconds),
                    value.AttackId, value.SourceEntityId, value.BaseEntityId, value.BaseTeam, value.Amount,
                    F(value.HpBefore), F(value.HpAfter), F(value.MaxHp), V(value.Position)));
            }

            public void BattleSettled(BattleSettledEvent value)
            {
                Settlements.Add(value);
                Add("Settled", value.Sequence, string.Join("|", value.TickIndex, D(value.SimulatedTimeSeconds),
                    value.Result, value.AllyBaseEntityId, value.BossEntityId?.ToString() ?? "-",
                    F(value.AllyBaseHp), F(value.BossHp), value.AllyBaseDestroyed, value.BossDestroyed));
            }

            public void EffectApplied(EffectAppliedEvent value)
            {
                Add("EffectApplied", value.Sequence, string.Join("|", value.TickIndex,
                    D(value.SimulatedTimeSeconds), value.InstanceId?.ToString() ?? "-", value.EffectId,
                    value.OpIndex, value.SourceEntityId?.ToString() ?? "-", value.TargetEntityId,
                    value.Op, value.Stat, value.Mode, F(value.Value), F(value.DurationSeconds)));
            }

            public void EffectExpired(EffectExpiredEvent value)
            {
                Add("EffectExpired", value.Sequence, string.Join("|", value.TickIndex,
                    D(value.SimulatedTimeSeconds), value.InstanceId, value.EffectId, value.OpIndex,
                    value.SourceEntityId?.ToString() ?? "-", value.TargetEntityId, value.Op,
                    value.Stat, value.Mode, F(value.Value)));
            }

            public void UnitHealed(UnitHealedEvent value)
            {
                Add("Heal", value.Sequence, string.Join("|", value.TickIndex,
                    D(value.SimulatedTimeSeconds), value.EffectId, value.OpIndex,
                    value.SourceEntityId?.ToString() ?? "-", value.TargetEntityId,
                    F(value.AppliedAmount), F(value.HpBefore), F(value.HpAfter)));
            }

            public void ShieldChanged(ShieldChangedEvent value)
            {
                Add("Shield", value.Sequence, string.Join("|", value.TickIndex,
                    D(value.SimulatedTimeSeconds), value.InstanceId, value.EffectId, value.OpIndex,
                    value.SourceEntityId?.ToString() ?? "-", value.TargetEntityId,
                    F(value.Delta), F(value.ShieldBefore), F(value.ShieldAfter)));
            }

            public void CoinsModified(CoinsModifiedEvent value)
            {
                Add("CoinsModified", value.Sequence, string.Join("|", value.TickIndex,
                    D(value.SimulatedTimeSeconds), value.EffectId, value.OpIndex,
                    value.SourceEntityId?.ToString() ?? "-", value.TargetTeam,
                    value.Delta, value.CoinsBefore, value.CoinsAfter));
            }

            private void Add(string kind, long sequence, string payload)
            {
                Kinds.Add(kind);
                Sequences.Add(sequence);
                CanonicalEvents.Add(string.Concat(kind, "|", sequence.ToString(CultureInfo.InvariantCulture), "|", payload));
            }

            private static string Stats(BattleStats value)
            {
                return string.Join(",", F(value.MaxHp), F(value.Atk), F(value.Range), F(value.AtkSpeed),
                    F(value.Cooldown), F(value.Armor), F(value.Pierce), F(value.MoveSpeed));
            }

            private static string Context(WaveSpawnContext? value)
            {
                if (!value.HasValue)
                {
                    return "-";
                }

                WaveSpawnContext context = value.Value;
                return string.Join(",", context.WaveSetId, context.WaveIndex, context.GroupIndex,
                    context.Ordinal, D(context.ScheduledTimeSeconds), context.RewardRank);
            }

            private static string Context(WaveSpawnContext value)
            {
                return string.Join(",", value.WaveSetId, value.WaveIndex, value.GroupIndex,
                    value.Ordinal, D(value.ScheduledTimeSeconds), value.RewardRank);
            }
        }
    }
}
