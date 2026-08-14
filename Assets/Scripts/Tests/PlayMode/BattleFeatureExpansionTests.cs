using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HanziDefend.Tests.PlayMode
{
    [TestFixture]
    public sealed class BattleFeatureExpansionTests
    {
        private const string LevelId = "level_1_1";
        private const uint Seed = 0xB3002026u;
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

        [TestCase(EffectOpCode.AddStat)]
        [TestCase(EffectOpCode.Heal)]
        [TestCase(EffectOpCode.Damage)]
        [TestCase(EffectOpCode.GrantShield)]
        [TestCase(EffectOpCode.SpawnUnit)]
        [TestCase(EffectOpCode.ModifyCoins)]
        public void EffectCatalog_ContainsEveryM1Operation(EffectOpCode expected)
        {
            GameConfig config = GameConfig.Load();

            Assert.That(
                config.Effects.SelectMany(effect => effect.Ops).Select(op => op.Op),
                Does.Contain(expected));
        }

        [TestCase(EffectTarget.SelfUnit)]
        [TestCase(EffectTarget.AllyAll)]
        [TestCase(EffectTarget.AllyAdjacent)]
        [TestCase(EffectTarget.EnemyNearest)]
        [TestCase(EffectTarget.EnemyInRadius)]
        [TestCase(EffectTarget.EnemyBase)]
        public void EffectCatalog_ContainsEveryM1Target(EffectTarget expected)
        {
            GameConfig config = GameConfig.Load();

            Assert.That(
                config.Effects.SelectMany(effect => effect.Ops).Select(op => op.Target),
                Does.Contain(expected));
        }

        [Test]
        public void Targeting_Nearest_SelectsTheExactClosestLivingEnemy()
        {
            BattleSystem system = CreateSystem(GameConfig.Load());
            int seekerId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -4f)));
            system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(0f, 2f)));
            int nearestId = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(1f, -1f)));

            system.Tick(system.FixedDeltaTime);

            Assert.That(system.GetUnitSnapshot(seekerId).TargetEntityId, Is.EqualTo(nearestId));
        }

        [Test]
        public void Targeting_Backline_SelectsTheFarthestEnemyRowInsteadOfTheClosestUnit()
        {
            GameConfig config = GameConfig.Load();
            config.GetUnit("nuc").Targeting = TargetingMode.Backline;
            BattleSystem system = CreateSystem(config);
            int seekerId = system.Spawn(new UnitSpawnRequest("nuc", 1, new Vector2(0f, -4f)));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, -1f)));
            int backlineId = system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(1f, 3f)));

            system.Tick(system.FixedDeltaTime);

            Assert.That(system.GetUnitSnapshot(seekerId).TargetEntityId, Is.EqualTo(backlineId));
        }

        [Test]
        public void Targeting_RushBase_SelectsEnemyBaseDespiteACloserBlockingUnit()
        {
            GameConfig config = GameConfig.Load();
            BattleSystem system = CreateEncounter(config);
            int seekerId = system.Spawn(new UnitSpawnRequest("chc", 1, new Vector2(0f, -7f)));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, -6f)));

            system.Tick(system.FixedDeltaTime);

            Assert.That(system.GetUnitSnapshot(seekerId).TargetEntityId,
                Is.EqualTo(system.EnemyBaseEntityId));
        }

        [TestCase("gong", TargetingMode.Nearest, false)]
        [TestCase("nuc", TargetingMode.Nearest, false)]
        [TestCase("chc", TargetingMode.RushBase, false)]
        [TestCase("huo", TargetingMode.Nearest, false)]
        [TestCase("e_lang", TargetingMode.RushBase, false)]
        public void PublicSpawn_AcceptsEveryTargetingAndEffectBearingUnit(
            string definitionId,
            TargetingMode expectedTargeting,
            bool expectedToOwnEffects)
        {
            GameConfig config = GameConfig.Load();
            BattleSystem system = CreateSystem(config);
            UnitDef definition = config.GetUnit(definitionId);

            int entityId = system.Spawn(new UnitSpawnRequest(definitionId, 1, Vector2.zero));

            Assert.That(definition.Targeting, Is.EqualTo(expectedTargeting));
            Assert.That(definition.Effects.Length > 0, Is.EqualTo(expectedToOwnEffects));
            Assert.That(system.GetUnitSnapshot(entityId).DefinitionId, Is.EqualTo(definitionId));
        }

        [Test]
        public void Scheduler_WolfRushBaseBypassesAUnitStandingOnItsSpawnPoint()
        {
            GameConfig config = GameConfig.Load();
            config.GetWaveSet(config.GetLevel(LevelId).WaveSetId).Waves = new[]
            {
                new WaveDef
                {
                    Index = 1,
                    RewardRank = EnemyRank.Normal,
                    DelaySec = 0f,
                    Spawns = new[]
                    {
                        new WaveSpawnDef
                        {
                            UnitId = "e_lang",
                            Level = 1,
                            Count = 1,
                            SpreadX = 0f,
                            IntervalSec = 0f
                        }
                    }
                }
            };
            BattleSystem system = CreateEncounter(config);
            Position2Def spawn = config.Economy.Battle.EnemySpawnCenter;
            int blockerId = system.Spawn(new UnitSpawnRequest(
                "dun",
                1,
                new Vector2(spawn.X, spawn.Y - 0.5f)));

            system.Tick(system.FixedDeltaTime);

            BattleUnitSnapshot wolf = system.CaptureSnapshot().Single(value => value.DefinitionId == "e_lang");
            Assert.That(wolf.TargetEntityId, Is.EqualTo(system.AllyBaseEntityId));
            Assert.That(system.GetUnitSnapshot(blockerId).CurrentHp,
                Is.EqualTo(system.GetUnitSnapshot(blockerId).Stats.MaxHp));
        }

        [Test]
        public void Targeting_SuicideSelectsNearestThenExplodesOnceAndKillsItself()
        {
            GameConfig config = GameConfig.Load();
            MakeTestSuicideUnit(config);
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateSystem(config, recorder);
            int suicideId = system.Spawn(new UnitSpawnRequest("huo", 1, new Vector2(0f, -1f)));
            int nearestId = system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));
            int outsideId = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(3f, 0f)));

            system.Tick(system.FixedDeltaTime);
            Assert.That(system.GetUnitSnapshot(suicideId).TargetEntityId, Is.EqualTo(nearestId));

            for (int index = 0;
                 index < 60 && system.GetUnitSnapshot(suicideId).State != BattleUnitState.Dead;
                 index++)
            {
                system.Tick(system.FixedDeltaTime);
            }

            float blast = config.GetEffect("unit_huo_blast").Ops.Single().Value;
            int expectedDamage = Formula.Damage(
                blast,
                system.GetUnitSnapshot(suicideId).AttackType,
                system.GetUnitSnapshot(suicideId).BonusVs,
                system.GetUnitSnapshot(nearestId).Stats.Armor,
                system.GetUnitSnapshot(nearestId).ArmorType,
                system.GetUnitSnapshot(nearestId).UnitType,
                0f,
                config.Economy);
            Assert.That(system.GetUnitSnapshot(suicideId).State, Is.EqualTo(BattleUnitState.Dead));
            Assert.That(system.GetUnitSnapshot(nearestId).CurrentHp,
                Is.EqualTo(system.GetUnitSnapshot(nearestId).Stats.MaxHp - expectedDamage));
            Assert.That(system.GetUnitSnapshot(outsideId).CurrentHp,
                Is.EqualTo(system.GetUnitSnapshot(outsideId).Stats.MaxHp));
            for (int index = 0; index < 10; index++)
            {
                system.Tick(system.FixedDeltaTime);
            }

            Assert.That(recorder.Deaths.Count(value => value.EntityId == suicideId), Is.EqualTo(1));
            Assert.That(recorder.Effects.Count(value => value.EffectId == "unit_huo_blast"), Is.EqualTo(1));
            Assert.That(recorder.CoinDrops.Any(value => value.SourceEntityId == suicideId), Is.False);
        }

        [Test]
        public void SuicideKillsBoss_SettlementIsLastAndFurtherTicksFreeze()
        {
            GameConfig config = GameConfig.Load();
            MakeTestSuicideUnit(config);
            SetSingleWave(config, "bld_cheng", EnemyRank.Boss);
            EffectOpDef blast = config.GetEffect("unit_huo_blast").Ops.Single();
            BossDef boss = config.GetBoss("bld_cheng");
            boss.Hp = blast.Value;
            boss.Armor = 0f;
            boss.AtkSpeed = 0f;
            Position2Def spawn = config.Economy.Battle.EnemySpawnCenter;
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);
            int suicideId = system.Spawn(new UnitSpawnRequest(
                "huo", 1, new Vector2(spawn.X, spawn.Y)));

            system.Tick(system.FixedDeltaTime);

            Assert.That(system.Result, Is.EqualTo(BattleResult.Win));
            Assert.That(recorder.Settlements, Has.Count.EqualTo(1));
            UnitDiedEvent suicideDeath = recorder.Deaths.Single(value => value.EntityId == suicideId);
            BattleSettledEvent settlement = recorder.Settlements.Single();
            Assert.That(suicideDeath.Sequence, Is.LessThan(settlement.Sequence));
            Assert.That(recorder.LastSequence, Is.EqualTo(settlement.Sequence));
            string frozen = CanonicalFrame(system);
            long frozenSequence = recorder.LastSequence;

            system.Tick(system.FixedDeltaTime);

            Assert.That(CanonicalFrame(system), Is.EqualTo(frozen));
            Assert.That(recorder.LastSequence, Is.EqualTo(frozenSequence));
        }

        [Test]
        public void EnemyInRadius_IncludesExactBoundaryButExcludesOutsideAndFriendlyUnits()
        {
            GameConfig config = GameConfig.Load();
            BattleSystem system = CreateSystem(config);
            EffectOpDef blast = config.GetEffect("unit_huo_blast").Ops.Single();
            int sourceId = system.Spawn(new UnitSpawnRequest("huo", 1, Vector2.zero));
            int boundaryId = system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(blast.Radius, 0f)));
            int outsideId = system.Spawn(new UnitSpawnRequest(
                "e_liu",
                1,
                new Vector2(blast.Radius + 0.001f, 0f)));
            int friendlyId = system.Spawn(new UnitSpawnRequest("dun", 1, new Vector2(blast.Radius * 0.5f, 0f)));
            BattleUnitSnapshot source = system.GetUnitSnapshot(sourceId);
            BattleUnitSnapshot boundaryBefore = system.GetUnitSnapshot(boundaryId);

            EffectExecutionResult result = system.ApplyEffect(
                "unit_huo_blast",
                ContextFor(system, sourceId));

            int expectedDamage = Formula.Damage(
                blast.Value,
                source.AttackType,
                source.BonusVs,
                boundaryBefore.Stats.Armor,
                boundaryBefore.ArmorType,
                boundaryBefore.UnitType,
                0f,
                config.Economy);
            Assert.That(result.AffectedTargetCount, Is.EqualTo(1));
            Assert.That(system.GetUnitSnapshot(boundaryId).CurrentHp,
                Is.EqualTo(Mathf.Max(0f, boundaryBefore.CurrentHp - expectedDamage)));
            Assert.That(system.GetUnitSnapshot(outsideId).CurrentHp,
                Is.EqualTo(system.GetUnitSnapshot(outsideId).Stats.MaxHp));
            Assert.That(system.GetUnitSnapshot(friendlyId).CurrentHp,
                Is.EqualTo(system.GetUnitSnapshot(friendlyId).Stats.MaxHp));
        }

        [Test]
        public void AddStat_AllyAllUsesConfiguredMulOnEveryAllyOnly()
        {
            GameConfig config = GameConfig.Load();
            BattleSystem system = CreateSystem(config);
            int firstId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(-2f, 0f)));
            int secondId = system.Spawn(new UnitSpawnRequest("mao", 1, new Vector2(2f, 0f)));
            int enemyId = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, 5f)));
            float firstBase = system.GetUnitSnapshot(firstId).Stats.Atk;
            float secondBase = system.GetUnitSnapshot(secondId).Stats.Atk;
            float enemyBase = system.GetUnitSnapshot(enemyId).Stats.Atk;
            EffectOpDef op = config.GetEffect("buff_atk_up").Ops.Single();

            EffectExecutionResult result = system.ApplyEffect("buff_atk_up", ContextFor(system, firstId));

            Assert.That(result.AffectedTargetCount, Is.EqualTo(2));
            Assert.That(system.GetUnitSnapshot(firstId).Stats.Atk,
                Is.EqualTo(firstBase * (1f + op.Value)).Within(0.00001f));
            Assert.That(system.GetUnitSnapshot(secondId).Stats.Atk,
                Is.EqualTo(secondBase * (1f + op.Value)).Within(0.00001f));
            Assert.That(system.GetUnitSnapshot(enemyId).Stats.Atk, Is.EqualTo(enemyBase));
        }

        [Test]
        public void Heal_AllyAllUsesConfiguredAmountWithoutExceedingMaxHp()
        {
            GameConfig config = GameConfig.Load();
            BattleSystem system = CreateSystem(config);
            int sourceId = system.Spawn(new UnitSpawnRequest("dun", 1, new Vector2(-0.5f, 0f)));
            int targetId = system.Spawn(new UnitSpawnRequest("dun", 1, new Vector2(0.5f, 0f)));
            system.ApplyEffect("unit_huo_blast", TeamContext(BattleTeam.Enemy, Vector2.zero));
            system.ApplyEffect("unit_huo_blast", TeamContext(BattleTeam.Enemy, Vector2.zero));
            float damagedHp = system.GetUnitSnapshot(targetId).CurrentHp;
            EffectOpDef heal = config.GetEffect("cmd_bei_active").Ops.Single();

            system.ApplyEffect("cmd_bei_active", ContextFor(system, sourceId));

            BattleUnitSnapshot target = system.GetUnitSnapshot(targetId);
            Assert.That(target.CurrentHp,
                Is.EqualTo(Mathf.Min(target.Stats.MaxHp, damagedHp + heal.Value)));
        }

        [Test]
        public void AllyAdjacent_UsesExplicitRelationInsteadOfWorldDistance()
        {
            GameConfig config = GameConfig.Load();
            BattleSystem system = CreateSystem(config);
            int sourceId = system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            int explicitlyAdjacentId = system.Spawn(new UnitSpawnRequest("dun", 1, new Vector2(5f, 0f)));
            int geometricallyNearId = system.Spawn(new UnitSpawnRequest("mao", 1, new Vector2(0.1f, 0f)));
            EffectOpDef shield = config.GetEffect("buff_front_shield").Ops.Single();

            system.ApplyEffect(
                "buff_front_shield",
                ContextFor(system, sourceId, new[] { explicitlyAdjacentId }));

            Assert.That(system.GetUnitSnapshot(explicitlyAdjacentId).Shield, Is.EqualTo(shield.Value));
            Assert.That(system.GetUnitSnapshot(geometricallyNearId).Shield, Is.Zero);
            Assert.That(system.GetUnitSnapshot(sourceId).Shield, Is.Zero);
        }

        [Test]
        public void Damage_EnemyNearestSelectsExactClosestTargetAndUsesDataFormula()
        {
            GameConfig config = GameConfig.Load();
            BattleSystem system = CreateSystem(config);
            int sourceId = system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            int nearestId = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(1f, 0f)));
            int fartherId = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(2f, 0f)));
            EffectOpDef damage = config.GetEffect("skill_breakthrough").Ops.Single();

            system.ApplyEffect("skill_breakthrough", ContextFor(system, sourceId));

            int expected = Formula.Damage(
                damage.Value,
                system.GetUnitSnapshot(sourceId).AttackType,
                system.GetUnitSnapshot(sourceId).BonusVs,
                system.GetUnitSnapshot(nearestId).Stats.Armor,
                system.GetUnitSnapshot(nearestId).ArmorType,
                system.GetUnitSnapshot(nearestId).UnitType,
                0f,
                config.Economy);
            Assert.That(system.GetUnitSnapshot(nearestId).CurrentHp,
                Is.EqualTo(system.GetUnitSnapshot(nearestId).Stats.MaxHp - expected));
            Assert.That(system.GetUnitSnapshot(fartherId).CurrentHp,
                Is.EqualTo(system.GetUnitSnapshot(fartherId).Stats.MaxHp));
        }

        [Test]
        public void Damage_EnemyBaseSelectsExactOpposingBaseAndUsesDataFormula()
        {
            GameConfig config = GameConfig.Load();
            BattleSystem system = CreateEncounter(config);
            int sourceId = system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            float before = system.GetBaseSnapshot(system.EnemyBaseEntityId).CurrentHp;
            EffectOpDef damage = config.GetEffect("skill_breach_base").Ops.Single();

            system.ApplyEffect("skill_breach_base", ContextFor(system, sourceId));

            int expected = Formula.Damage(
                damage.Value,
                system.GetUnitSnapshot(sourceId).AttackType,
                system.GetUnitSnapshot(sourceId).BonusVs,
                config.Bases.Enemy.Armor,
                config.Bases.Enemy.ArmorType,
                config.Bases.Enemy.UnitType,
                0f,
                config.Economy);
            Assert.That(system.GetBaseSnapshot(system.EnemyBaseEntityId).CurrentHp,
                Is.EqualTo(before - expected));
            Assert.That(system.GetBaseSnapshot(system.AllyBaseEntityId).CurrentHp,
                Is.EqualTo(config.GetLevel(LevelId).BaseHp));
        }

        [Test]
        public void SpawnUnit_SelfUnitInheritsSourceLevelAndPositionForConfiguredCount()
        {
            GameConfig config = GameConfig.Load();
            BattleSystem system = CreateSystem(config);
            var sourcePosition = new Vector2(1.25f, -2.5f);
            const int sourceLevel = 3;
            int sourceId = system.Spawn(new UnitSpawnRequest("gong", sourceLevel, sourcePosition));
            EffectOpDef spawn = config.GetEffect("skill_reinforce").Ops.Single();

            EffectExecutionResult result = system.ApplyEffect("skill_reinforce", ContextFor(system, sourceId));

            BattleUnitSnapshot[] reinforcements = system.CaptureSnapshot()
                .Where(value => value.EntityId != sourceId)
                .ToArray();
            Assert.That(result.SpawnedUnitCount, Is.EqualTo(spawn.Count));
            Assert.That(reinforcements, Has.Length.EqualTo(spawn.Count));
            Assert.That(reinforcements.Select(value => value.DefinitionId),
                Is.All.EqualTo(spawn.UnitId));
            Assert.That(reinforcements.Select(value => value.Level), Is.All.EqualTo(sourceLevel));
            Assert.That(reinforcements.Select(value => value.Position), Is.All.EqualTo(sourcePosition));
        }

        [Test]
        public void ModifyCoins_SelfUnitUsesConfiguredDeltaAndPublishesNoFakeDrop()
        {
            GameConfig config = GameConfig.Load();
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateSystem(config, recorder);
            int sourceId = system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            int before = system.Coins;
            EffectOpDef coins = config.GetEffect("reward_coins").Ops.Single();

            EffectExecutionResult result = system.ApplyEffect("reward_coins", ContextFor(system, sourceId));

            Assert.That(result.CoinDelta, Is.EqualTo((int)coins.Value));
            Assert.That(system.Coins, Is.EqualTo(before + (int)coins.Value));
            Assert.That(recorder.CoinChanges.Single().Delta, Is.EqualTo((int)coins.Value));
            Assert.That(recorder.CoinDrops, Is.Empty);
        }

        [Test]
        public void CastleBossSpawn_UsesExactConfiguredCombatStatsWithoutLegacyFury()
        {
            GameConfig config = GameConfig.Load();
            SetSingleWave(config, "bld_cheng", EnemyRank.Boss);
            BattleSystem system = CreateEncounter(config);
            BossDef definition = config.GetBoss("bld_cheng");

            system.Tick(system.FixedDeltaTime);

            BattleUnitSnapshot boss = system.GetUnitSnapshot(system.BossEntityId.Value);
            Assert.That(boss.Stats.Atk, Is.EqualTo(definition.Atk).Within(0.00001f));
            Assert.That(boss.Stats.AtkSpeed, Is.EqualTo(definition.AtkSpeed).Within(0.00001f));
            Assert.That(definition.Effects, Is.Empty,
                "The retired boss fury would silently change the M1-04 castle table.");
        }

        [Test]
        public void CastleTargeting_PrioritizesFartherSiegeUnitOverNearerNonSiegeUnit()
        {
            GameConfig config = GameConfig.Load();
            config.Economy.Battle.TargetSearchRadius = 200f;
            SetSingleWave(config, "bld_cheng", EnemyRank.Boss);
            config.GetBoss("bld_cheng").AtkSpeed = 0f;
            Position2Def spawn = config.Economy.Battle.EnemySpawnCenter;
            BattleSystem system = CreateEncounter(config);
            int nearerId = system.Spawn(new UnitSpawnRequest(
                "gong", 1, new Vector2(spawn.X, spawn.Y - 1f)));
            int siegeId = system.Spawn(new UnitSpawnRequest(
                "nuc", 1, new Vector2(spawn.X, spawn.Y - 4f)));

            system.Tick(system.FixedDeltaTime);

            BattleUnitSnapshot castle = system.GetUnitSnapshot(system.BossEntityId.Value);
            Assert.That(castle.TargetEntityId, Is.EqualTo(siegeId));
            Assert.That(castle.TargetEntityId, Is.Not.EqualTo(nearerId));
        }

        [Test]
        public void RushBase_ChongCheTargetsLiveCastleAndNeverTheCloserCombatUnit()
        {
            GameConfig config = GameConfig.Load();
            config.Economy.Battle.TargetSearchRadius = 200f;
            SetSingleWave(config, "bld_cheng", EnemyRank.Boss);
            config.GetBoss("bld_cheng").AtkSpeed = 0f;
            Position2Def spawn = config.Economy.Battle.EnemySpawnCenter;
            BattleSystem system = CreateEncounter(config);
            int chongCheId = system.Spawn(new UnitSpawnRequest(
                "chc", 1, new Vector2(spawn.X, spawn.Y - 3f)));
            int blockerId = system.Spawn(new UnitSpawnRequest(
                "e_shan", 1, new Vector2(spawn.X, spawn.Y - 2.5f)));

            system.Tick(system.FixedDeltaTime);

            BattleUnitSnapshot chongChe = system.GetUnitSnapshot(chongCheId);
            Assert.That(chongChe.TargetEntityId, Is.EqualTo(system.BossEntityId));
            Assert.That(chongChe.TargetEntityId, Is.Not.EqualTo(blockerId));
            Assert.That(chongChe.TargetEntityId, Is.Not.EqualTo(system.EnemyBaseEntityId));
        }

        [Test]
        public void Stack_CreatesIndependentInstancesAndExpiryRestoresBaseStat()
        {
            GameConfig config = GameConfig.Load();
            EffectDef effect = config.GetEffect("buff_atk_up");
            effect.Stacking = EffectStackingRule.Stack;
            BattleSystem system = CreateSystem(config);
            effect.Ops[0].Duration = system.FixedDeltaTime * 3f;
            int sourceId = system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            float baseAtk = system.GetUnitSnapshot(sourceId).Stats.Atk;

            system.ApplyEffect(effect.Id, ContextFor(system, sourceId));
            system.ApplyEffect(effect.Id, ContextFor(system, sourceId));

            ActiveEffectSnapshot[] active = system.CaptureActiveEffects()
                .Where(value => value.EffectId == effect.Id && value.TargetEntityId == sourceId)
                .ToArray();
            Assert.That(active, Has.Length.EqualTo(2));
            Assert.That(active.Select(value => value.InstanceId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(system.GetUnitSnapshot(sourceId).Stats.Atk,
                Is.EqualTo(baseAtk * (1f + effect.Ops[0].Value * 2f)).Within(0.00001f));

            system.Tick(effect.Ops[0].Duration + system.FixedDeltaTime);

            Assert.That(system.CaptureActiveEffects()
                .Any(value => value.EffectId == effect.Id && value.TargetEntityId == sourceId), Is.False);
            Assert.That(system.GetUnitSnapshot(sourceId).Stats.Atk, Is.EqualTo(baseAtk));
        }

        [Test]
        public void Refresh_KeysBySourceTracksRemainingDurationAndExpiresIndependently()
        {
            GameConfig config = GameConfig.Load();
            EffectDef effect = config.GetEffect("buff_atk_up");
            effect.Stacking = EffectStackingRule.Refresh;
            BattleSystem system = CreateSystem(config);
            effect.Ops[0].Duration = system.FixedDeltaTime * 4f;
            int firstSourceId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(-2f, 0f)));
            int secondSourceId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(2f, 0f)));
            int targetId = system.Spawn(new UnitSpawnRequest("mao", 1, Vector2.zero));

            system.ApplyEffect(effect.Id, ContextFor(system, firstSourceId));
            system.Tick(system.FixedDeltaTime);
            float beforeRefresh = ActiveFor(system, effect.Id, firstSourceId, targetId)
                .Single().RemainingDurationSeconds;
            system.ApplyEffect(effect.Id, ContextFor(system, firstSourceId));
            ActiveEffectSnapshot refreshed = ActiveFor(system, effect.Id, firstSourceId, targetId).Single();
            system.ApplyEffect(effect.Id, ContextFor(system, secondSourceId));

            ActiveEffectSnapshot[] targetEffects = system.CaptureActiveEffects()
                .Where(value => value.EffectId == effect.Id && value.TargetEntityId == targetId)
                .ToArray();
            Assert.That(refreshed.RemainingDurationSeconds, Is.GreaterThan(beforeRefresh));
            Assert.That(targetEffects, Has.Length.EqualTo(2));
            Assert.That(targetEffects.Select(value => value.SourceEntityId),
                Is.EquivalentTo(new int?[] { firstSourceId, secondSourceId }));

            system.Tick(effect.Ops[0].Duration + system.FixedDeltaTime);
            Assert.That(system.CaptureActiveEffects()
                .Any(value => value.EffectId == effect.Id && value.TargetEntityId == targetId), Is.False);
        }

        [Test]
        public void CommanderBei_PassiveActiveAndCooldownAllResolveFromData()
        {
            GameConfig config = GameConfig.Load();
            BattleSystem system = CreateSystem(config);
            CommanderDef commander = config.GetCommander("cmd_bei");
            EffectOpDef passive = config.GetEffect(commander.PassiveEffectId).Ops.Single();
            EffectOpDef active = config.GetEffect(commander.ActiveEffectId).Ops.Single();
            int firstId = system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            float firstBaseAtk = system.GetUnitSnapshot(firstId).Stats.Atk;

            system.ConfigureCommander(commander.Id);
            int laterId = system.Spawn(new UnitSpawnRequest("mao", 1, new Vector2(2f, 0f)));
            float laterBaseAtk = Formula.StatAtLevel(
                config.GetUnit("mao").Atk.Base,
                config.GetUnit("mao").Atk.Growth,
                1);
            Assert.That(system.GetUnitSnapshot(firstId).Stats.Atk,
                Is.EqualTo(firstBaseAtk * (1f + passive.Value)).Within(0.00001f));
            Assert.That(system.GetUnitSnapshot(laterId).Stats.Atk,
                Is.EqualTo(laterBaseAtk * (1f + passive.Value)).Within(0.00001f));

            system.ApplyEffect("unit_huo_blast", TeamContext(BattleTeam.Enemy, Vector2.zero));
            float hpBefore = system.GetUnitSnapshot(firstId).CurrentHp;
            Assert.That(system.TryActivateCommander(commander.Id), Is.True);
            Assert.That(system.GetUnitSnapshot(firstId).CurrentHp,
                Is.EqualTo(Mathf.Min(system.GetUnitSnapshot(firstId).Stats.MaxHp, hpBefore + active.Value)));
            Assert.That(system.GetCommanderCooldownRemaining(commander.Id),
                Is.EqualTo(commander.ActiveCooldown).Within(0.00001f));
            Assert.That(system.TryActivateCommander(commander.Id), Is.False);

            system.Tick(commander.ActiveCooldown - system.FixedDeltaTime);
            Assert.That(system.TryActivateCommander(commander.Id), Is.False);
            system.Tick(system.FixedDeltaTime * 2f);
            Assert.That(system.GetCommanderCooldownRemaining(commander.Id), Is.Zero.Within(0.00001f));
            Assert.That(system.TryActivateCommander(commander.Id), Is.True);
        }

        [Test]
        public void ExpandedBattle_SameSeedProducesIdenticalStateSequenceEntryByEntry()
        {
            IReadOnlyList<string> first = RunDeterministicExpandedScenario(0x5EEDB3u);
            IReadOnlyList<string> second = RunDeterministicExpandedScenario(0x5EEDB3u);

            Assert.That(first, Has.Count.EqualTo(121));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void HonestBalance_RealUnmodifiedTwentyWaveConfig_CompletesExactlyOnceAndPrintsReading()
        {
            GameConfig config = GameConfig.Load();
            var recorder = new RecordingEncounterEvents();
            BattleSystem system = CreateEncounter(config, recorder);

            system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(-2f, -7f)));
            system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -7f)));
            system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(2f, -7f)));
            system.Spawn(new UnitSpawnRequest("dun", 1, new Vector2(-1f, -5.5f)));
            system.Spawn(new UnitSpawnRequest("dun", 1, new Vector2(1f, -5.5f)));
            system.Spawn(new UnitSpawnRequest("mao", 1, new Vector2(0f, -6.2f)));
            system.ConfigureCommander("cmd_bei");

            int safetyTicks = checked(config.Economy.Battle.TickRateHz * 600);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (!system.IsSettled && system.TickIndex < safetyTicks)
            {
                system.Tick(system.FixedDeltaTime);
            }
            stopwatch.Stop();

            WriteBalanceReading(system, recorder);
            TestContext.WriteLine($"Honest real-data encounter wall={stopwatch.Elapsed.TotalSeconds:F3}s");
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(45d),
                "The honest real-data encounter exceeded the WO-B5 wall-clock gate.");
            Assert.That(system.Result, Is.Not.EqualTo(BattleResult.None),
                "The honest balance run did not reach either settlement path within ten simulated minutes.");
            Assert.That(system.TotalWaveCount, Is.EqualTo(20));
            Assert.That(system.CurrentWaveIndex, Is.EqualTo(system.TotalWaveCount),
                "The reference deployment settled before the complete real-data wave timeline was emitted.");
            Assert.That(recorder.Bosses, Has.Count.EqualTo(1),
                "The real-data run must actually emit the configured final castle.");
            Assert.That(recorder.Bosses.Single().DefinitionId, Is.EqualTo("bld_cheng"));
            Assert.That(recorder.Damage, Is.Not.Empty,
                "The balance reading must be based on real damage events, not an empty summary.");
            Assert.That(recorder.Settlements, Has.Count.EqualTo(1),
                "Settlement must be published exactly once.");
        }

        private IReadOnlyList<string> RunDeterministicExpandedScenario(uint seed)
        {
            GameConfig config = GameConfig.Load();
            config.GetWaveSet(config.GetLevel(LevelId).WaveSetId).Waves[0].DelaySec = 1000f;
            BattleSystem system = BattleSystem.CreateEncounter(config, LevelId, seed);
            systems.Add(system);
            int nuId = system.Spawn(new UnitSpawnRequest("nuc", 2, new Vector2(-2f, -3f)));
            int qiId = system.Spawn(new UnitSpawnRequest("chc", 2, new Vector2(2f, -3f)));
            system.Spawn(new UnitSpawnRequest("huo", 2, new Vector2(0f, -1f)));
            system.Spawn(new UnitSpawnRequest("e_shan", 2, new Vector2(0f, 1f)));
            system.Spawn(new UnitSpawnRequest("e_liu", 2, new Vector2(-2f, 2f)));
            system.Spawn(new UnitSpawnRequest("e_lang", 2, new Vector2(2f, 2f)));
            system.ConfigureCommander("cmd_bei");
            system.ApplyEffect("buff_front_shield", ContextFor(system, nuId, new[] { qiId }));
            system.ApplyEffect("reward_coins", ContextFor(system, nuId));

            var frames = new List<string> { CanonicalFrame(system) };
            for (int index = 0; index < 120; index++)
            {
                system.Tick(system.FixedDeltaTime);
                frames.Add(CanonicalFrame(system));
            }

            return frames;
        }

        private static string CanonicalFrame(BattleSystem system)
        {
            var builder = new StringBuilder();
            builder.Append(system.TickIndex).Append('|')
                .Append(D(system.SimulatedTimeSeconds)).Append('|')
                .Append(system.Coins).Append('|')
                .Append(system.Result);
            if (system.IsEncounter)
            {
                builder.Append("|AB:")
                    .Append(F(system.GetBaseSnapshot(system.AllyBaseEntityId).CurrentHp))
                    .Append("|EB:")
                    .Append(F(system.GetBaseSnapshot(system.EnemyBaseEntityId).CurrentHp));
            }

            foreach (BattleUnitSnapshot unit in system.CaptureSnapshot().OrderBy(value => value.EntityId))
            {
                builder.Append("|U:")
                    .Append(unit.EntityId).Append(',')
                    .Append(unit.DefinitionId).Append(',')
                    .Append(unit.State).Append(',')
                    .Append(F(unit.CurrentHp)).Append(',')
                    .Append(F(unit.Shield)).Append(',')
                    .Append(F(unit.Position.x)).Append(',')
                    .Append(F(unit.Position.y)).Append(',')
                    .Append(unit.TargetEntityId?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(',')
                    .Append(F(unit.Stats.Atk)).Append(',')
                    .Append(F(unit.Stats.AtkSpeed));
            }

            foreach (ActiveEffectSnapshot effect in system.CaptureActiveEffects()
                         .OrderBy(value => value.InstanceId))
            {
                builder.Append("|E:")
                    .Append(effect.InstanceId).Append(',')
                    .Append(effect.EffectId).Append(',')
                    .Append(effect.SourceEntityId?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(',')
                    .Append(effect.TargetEntityId).Append(',')
                    .Append(effect.OpIndex).Append(',')
                    .Append(F(effect.RemainingDurationSeconds));
            }

            return builder.ToString();
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string D(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static EffectExecutionContext ContextFor(
            BattleSystem system,
            int sourceEntityId,
            IReadOnlyList<int> adjacentAllyEntityIds = null)
        {
            BattleUnitSnapshot source = system.GetUnitSnapshot(sourceEntityId);
            return new EffectExecutionContext(
                sourceEntityId,
                source.Team,
                source.Position,
                source.Level,
                adjacentAllyEntityIds);
        }

        private static EffectExecutionContext TeamContext(BattleTeam sourceTeam, Vector2 origin)
        {
            return new EffectExecutionContext(null, sourceTeam, origin, 1);
        }

        private static IEnumerable<ActiveEffectSnapshot> ActiveFor(
            BattleSystem system,
            string effectId,
            int sourceEntityId,
            int targetEntityId)
        {
            return system.CaptureActiveEffects().Where(value =>
                value.EffectId == effectId
                && value.SourceEntityId == sourceEntityId
                && value.TargetEntityId == targetEntityId);
        }

        private static void MakeTestSuicideUnit(GameConfig config)
        {
            UnitDef unit = config.GetUnit("huo");
            unit.Targeting = TargetingMode.Suicide;
            unit.Effects = new[] { "unit_huo_blast" };
            config.GetEffect("unit_huo_blast").Trigger = EffectTrigger.SuicideContact;
        }

        private static void SetSingleWave(GameConfig config, string definitionId, EnemyRank rewardRank)
        {
            config.GetWaveSet(config.GetLevel(LevelId).WaveSetId).Waves = new[]
            {
                new WaveDef
                {
                    Index = 1,
                    RewardRank = rewardRank,
                    DelaySec = 0f,
                    Spawns = new[]
                    {
                        new WaveSpawnDef
                        {
                            UnitId = definitionId,
                            Level = 1,
                            Count = 1,
                            SpreadX = 0f,
                            IntervalSec = 0f
                        }
                    }
                }
            };
        }

        private BattleSystem CreateSystem(GameConfig config, IBattleEvents events = null)
        {
            var system = new BattleSystem(config, Seed, events);
            systems.Add(system);
            return system;
        }

        private BattleSystem CreateEncounter(GameConfig config, IBattleEvents events = null)
        {
            BattleSystem system = BattleSystem.CreateEncounter(config, LevelId, Seed, events);
            systems.Add(system);
            return system;
        }

        private static void WriteBalanceReading(BattleSystem system, RecordingEncounterEvents recorder)
        {
            string timeline = recorder.Waves.Count == 0
                ? "(none)"
                : string.Join(", ", recorder.Waves.Select(value => string.Format(
                    CultureInfo.InvariantCulture,
                    "W{0}@{1:0.000}s",
                    value.WaveIndex,
                    value.SimulatedTimeSeconds)));
            Debug.Log("[WO-03 honest] timeline: " + timeline);
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[WO-03 honest] result={0}, t={1:0.000}s, tick={2}, wave={3}, "
                + "allyBaseHp={4:0.###}, enemyBaseHp={5:0.###}, coins={6}, dropped={7}",
                system.Result,
                system.SimulatedTimeSeconds,
                system.TickIndex,
                system.CurrentWaveIndex,
                system.GetBaseSnapshot(system.AllyBaseEntityId).CurrentHp,
                system.GetBaseSnapshot(system.EnemyBaseEntityId).CurrentHp,
                system.Coins,
                system.DroppedCoins));

            BattleUnitSnapshot[] finalUnits = system.CaptureSnapshot().ToArray();
            string survivors = string.Join(", ", finalUnits
                .Where(value => value.State != BattleUnitState.Dead)
                .GroupBy(value => string.Concat(value.Team, "/", value.DefinitionId))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => string.Concat(group.Key, "=", group.Count().ToString(CultureInfo.InvariantCulture))));
            Debug.Log("[WO-03 honest] survivors: "
                      + (survivors.Length == 0 ? "(none)" : survivors));

            Dictionary<int, string> definitionByEntity = recorder.Spawns
                .GroupBy(value => value.EntityId)
                .ToDictionary(group => group.Key, group => group.First().DefinitionId);
            string kills = string.Join(", ", recorder.Deaths
                .GroupBy(value => definitionByEntity.TryGetValue(value.KillerEntityId, out string id)
                    ? id
                    : string.Concat("entity#", value.KillerEntityId.ToString(CultureInfo.InvariantCulture)))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => string.Concat(group.Key, "=", group.Count().ToString(CultureInfo.InvariantCulture))));
            Debug.Log("[WO-03 honest] kills: " + (kills.Length == 0 ? "(none)" : kills));

            Dictionary<int, string> sourceLabelByEntity = recorder.Spawns
                .GroupBy(value => value.EntityId)
                .ToDictionary(
                    group => group.Key,
                    group => string.Concat(group.First().Team, "/", group.First().DefinitionId));
            string damageContributions = string.Join(", ", recorder.Damage
                .GroupBy(value => sourceLabelByEntity.TryGetValue(value.SourceEntityId, out string label)
                    ? label
                    : string.Concat("entity#", value.SourceEntityId.ToString(CultureInfo.InvariantCulture)))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}={1:0.###}",
                    group.Key,
                    group.Sum(value => Math.Max(0f, value.HpBefore - value.HpAfter)))));
            Debug.Log("[WO-03 honest] damage: "
                      + (damageContributions.Length == 0 ? "(none)" : damageContributions));

            Dictionary<int, double> deathTimeByEntity = recorder.Deaths
                .GroupBy(value => value.EntityId)
                .ToDictionary(group => group.Key, group => group.Single().SimulatedTimeSeconds);
            string lifetimes = string.Join("; ", recorder.Spawns
                .GroupBy(value => value.DefinitionId)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    double[] values = group.Select(spawn =>
                        (deathTimeByEntity.TryGetValue(spawn.EntityId, out double deathTime)
                            ? deathTime
                            : system.SimulatedTimeSeconds) - spawn.SimulatedTimeSeconds).ToArray();
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}:n={1},min={2:0.000},avg={3:0.000},max={4:0.000}s",
                        group.Key,
                        values.Length,
                        values.Min(),
                        values.Average(),
                        values.Max());
                }));
            Debug.Log("[WO-03 honest] lifetimes: "
                      + (lifetimes.Length == 0 ? "(none)" : lifetimes));
        }

        private sealed class RecordingEncounterEvents : IBattleEncounterEvents, IBattleEffectEvents
        {
            internal long LastSequence { get; private set; }

            internal List<UnitSpawnedEvent> Spawns { get; } = new List<UnitSpawnedEvent>();

            internal List<UnitDiedEvent> Deaths { get; } = new List<UnitDiedEvent>();

            internal List<DamageDealtEvent> Damage { get; } = new List<DamageDealtEvent>();

            internal List<WaveStartedEvent> Waves { get; } = new List<WaveStartedEvent>();

            internal List<BossSpawnedEvent> Bosses { get; } = new List<BossSpawnedEvent>();

            internal List<BattleSettledEvent> Settlements { get; } = new List<BattleSettledEvent>();

            internal List<EffectAppliedEvent> Effects { get; } = new List<EffectAppliedEvent>();

            internal List<EffectExpiredEvent> ExpiredEffects { get; } = new List<EffectExpiredEvent>();

            internal List<UnitHealedEvent> Heals { get; } = new List<UnitHealedEvent>();

            internal List<ShieldChangedEvent> Shields { get; } = new List<ShieldChangedEvent>();

            internal List<CoinsModifiedEvent> CoinChanges { get; } = new List<CoinsModifiedEvent>();

            internal List<CoinDroppedEvent> CoinDrops { get; } = new List<CoinDroppedEvent>();

            public void UnitSpawned(UnitSpawnedEvent value)
            {
                LastSequence = value.Sequence;
                Spawns.Add(value);
            }

            public void UnitAttacked(UnitAttackedEvent value)
            {
                LastSequence = value.Sequence;
            }

            public void DamageDealt(DamageDealtEvent value)
            {
                LastSequence = value.Sequence;
                Damage.Add(value);
            }

            public void UnitDied(UnitDiedEvent value)
            {
                LastSequence = value.Sequence;
                Deaths.Add(value);
            }

            public void CoinDropped(CoinDroppedEvent value)
            {
                LastSequence = value.Sequence;
                CoinDrops.Add(value);
            }

            public void WaveStarted(WaveStartedEvent value)
            {
                LastSequence = value.Sequence;
                Waves.Add(value);
            }

            public void BossSpawned(BossSpawnedEvent value)
            {
                LastSequence = value.Sequence;
                Bosses.Add(value);
            }

            public void BaseDamaged(BaseDamagedEvent value)
            {
                LastSequence = value.Sequence;
            }

            public void BattleSettled(BattleSettledEvent value)
            {
                LastSequence = value.Sequence;
                Settlements.Add(value);
            }

            public void EffectApplied(EffectAppliedEvent value)
            {
                LastSequence = value.Sequence;
                Effects.Add(value);
            }

            public void EffectExpired(EffectExpiredEvent value)
            {
                LastSequence = value.Sequence;
                ExpiredEffects.Add(value);
            }

            public void UnitHealed(UnitHealedEvent value)
            {
                LastSequence = value.Sequence;
                Heals.Add(value);
            }

            public void ShieldChanged(ShieldChangedEvent value)
            {
                LastSequence = value.Sequence;
                Shields.Add(value);
            }

            public void CoinsModified(CoinsModifiedEvent value)
            {
                LastSequence = value.Sequence;
                CoinChanges.Add(value);
            }
        }
    }
}
