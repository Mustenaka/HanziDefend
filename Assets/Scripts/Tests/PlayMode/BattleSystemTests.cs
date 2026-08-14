using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class BattleSystemTests
    {
        private const uint Seed = 0xB17B17u;
        private readonly List<BattleSystem> systems = new List<BattleSystem>();
        private GameConfig config;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            config = GameConfig.Load();
        }

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
        public void Constructor_UsesConfiguredThirtyHertz_AndRestoresPhysicsMode()
        {
            SimulationMode2D originalMode = Physics2D.simulationMode;
            BattleSystem system = CreateSystem();

            Assert.That(Physics2D.simulationMode, Is.EqualTo(SimulationMode2D.Script));
            Assert.That(system.FixedDeltaTime, Is.EqualTo(1f / config.Economy.Battle.TickRateHz));
            Assert.That(system.FixedDeltaTime, Is.EqualTo(1f / 30f).Within(0.000001f));

            system.Dispose();
            Assert.That(Physics2D.simulationMode, Is.EqualTo(originalMode));
        }

        [Test]
        public void Spawn_LevelOne_UsesDefinitionBaseStats()
        {
            BattleSystem system = CreateSystem();
            UnitDef definition = config.GetUnit("gong");
            int entityId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(2f, -3f)));

            BattleUnitSnapshot unit = system.GetUnitSnapshot(entityId);
            Assert.That(unit.DefinitionId, Is.EqualTo("gong"));
            Assert.That(unit.Team, Is.EqualTo(BattleTeam.Ally));
            Assert.That(unit.Level, Is.EqualTo(1));
            Assert.That(unit.Stats.MaxHp, Is.EqualTo(definition.Hp.Base));
            Assert.That(unit.Stats.Atk, Is.EqualTo(definition.Atk.Base));
            Assert.That(unit.Stats.Range, Is.EqualTo(definition.Range.Base));
            Assert.That(unit.Stats.MinRange, Is.EqualTo(definition.MinRange.Base));
            Assert.That(unit.Stats.AtkSpeed, Is.EqualTo(definition.AtkSpeed.Base));
            Assert.That(unit.Stats.Cooldown, Is.EqualTo(definition.Cooldown.Base));
            Assert.That(unit.Stats.Armor, Is.EqualTo(definition.Armor.Base));
            Assert.That(unit.Stats.Pierce, Is.EqualTo(definition.Pierce.Base));
            Assert.That(unit.Stats.MoveSpeed, Is.EqualTo(definition.MoveSpeed.Base));
            Assert.That(unit.CurrentHp, Is.EqualTo(unit.Stats.MaxHp));
            Assert.That(unit.Position, Is.EqualTo(new Vector2(2f, -3f)));
            Assert.That(unit.Facing, Is.EqualTo(Vector2.up));
            Assert.That(unit.State, Is.EqualTo(BattleUnitState.Idle));
        }

        [Test]
        public void Spawn_LevelFour_UsesFormulaStatAtLevel_ForEveryCurrentStat()
        {
            BattleSystem system = CreateSystem();
            UnitDef definition = config.GetUnit("e_shan");
            int entityId = system.Spawn(new UnitSpawnRequest("e_shan", 4, new Vector2(1f, 6f)));

            BattleUnitSnapshot unit = system.GetUnitSnapshot(entityId);
            Assert.That(unit.Team, Is.EqualTo(BattleTeam.Enemy));
            Assert.That(unit.Facing, Is.EqualTo(Vector2.down));
            Assert.That(unit.Stats.MaxHp,
                Is.EqualTo(Formula.StatAtLevel(definition.Hp.Base, definition.Hp.Growth, 4)));
            Assert.That(unit.Stats.Atk,
                Is.EqualTo(Formula.StatAtLevel(definition.Atk.Base, definition.Atk.Growth, 4)));
            Assert.That(unit.Stats.Range,
                Is.EqualTo(Formula.StatAtLevel(definition.Range.Base, definition.Range.Growth, 4)));
            Assert.That(unit.Stats.MinRange,
                Is.EqualTo(Formula.StatAtLevel(definition.MinRange.Base, definition.MinRange.Growth, 4)));
            Assert.That(unit.Stats.AtkSpeed,
                Is.EqualTo(Formula.StatAtLevel(definition.AtkSpeed.Base, definition.AtkSpeed.Growth, 4)));
            Assert.That(unit.Stats.Cooldown,
                Is.EqualTo(Formula.StatAtLevel(definition.Cooldown.Base, definition.Cooldown.Growth, 4)));
            Assert.That(unit.Stats.Armor,
                Is.EqualTo(Formula.StatAtLevel(definition.Armor.Base, definition.Armor.Growth, 4)));
            Assert.That(unit.Stats.Pierce,
                Is.EqualTo(Formula.StatAtLevel(definition.Pierce.Base, definition.Pierce.Growth, 4)));
            Assert.That(unit.Stats.MoveSpeed,
                Is.EqualTo(Formula.StatAtLevel(definition.MoveSpeed.Base, definition.MoveSpeed.Growth, 4)));
        }

        [Test]
        public void Spawn_RejectsEmptyDefinitionId()
        {
            BattleSystem system = CreateSystem();

            Assert.That(
                () => system.Spawn(new UnitSpawnRequest(" ", 1, Vector2.zero)),
                Throws.TypeOf<ArgumentException>().With.Message.Contains("Definition id"));
        }

        [Test]
        public void Spawn_RejectsUnknownDefinitionId()
        {
            BattleSystem system = CreateSystem();

            Assert.That(
                () => system.Spawn(new UnitSpawnRequest("missing_unit", 1, Vector2.zero)),
                Throws.TypeOf<KeyNotFoundException>().With.Message.Contains("missing_unit"));
        }

        [TestCase(0)]
        [TestCase(5)]
        public void Spawn_RejectsLevelOutsideContractRange(int level)
        {
            BattleSystem system = CreateSystem();

            Assert.That(
                () => system.Spawn(new UnitSpawnRequest("gong", level, Vector2.zero)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Spawn_AcceptsTargetingModesImplementedByWoB3()
        {
            BattleSystem system = CreateSystem();

            int entityId = 0;
            Assert.That(
                () => entityId = system.Spawn(new UnitSpawnRequest("chc", 1, Vector2.zero)),
                Throws.Nothing);
            Assert.That(system.GetUnitSnapshot(entityId).Targeting, Is.EqualTo(TargetingMode.RushBase));
        }

        [TestCase(0f)]
        [TestCase(-0.01f)]
        public void Tick_RejectsNonPositiveDuration(float dt)
        {
            BattleSystem system = CreateSystem();

            Assert.That(() => system.Tick(dt), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Tick_RejectsNaNDuration()
        {
            BattleSystem system = CreateSystem();

            Assert.That(() => system.Tick(float.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Tick_RejectsInfiniteDuration()
        {
            BattleSystem system = CreateSystem();

            Assert.That(() => system.Tick(float.PositiveInfinity), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void DisposedSystem_RejectsFurtherCommands()
        {
            BattleSystem system = CreateSystem();
            system.Dispose();

            Assert.That(() => system.Tick(1f / 30f), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () => system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero)),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void Tick_OutOfRangeUnit_MovesByConfiguredSpeedWithoutOvershooting()
        {
            BattleSystem system = CreateSystem();
            int allyId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -10f)));
            system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(0f, 10f)));

            system.Tick(system.FixedDeltaTime);

            BattleUnitSnapshot ally = system.GetUnitSnapshot(allyId);
            float expectedTravel = ally.Stats.MoveSpeed * system.FixedDeltaTime;
            Assert.That(ally.State, Is.EqualTo(BattleUnitState.Move));
            Assert.That(ally.Position.x, Is.EqualTo(0f).Within(0.00001f));
            Assert.That(ally.Position.y, Is.EqualTo(-10f + expectedTravel).Within(0.00001f));
            Assert.That(ally.Position.y, Is.LessThan(10f - ally.Stats.Range));
        }

        [Test]
        public void Tick_TargetExactlyOnRangeBoundary_Attacks()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            int allyId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -4.5f)));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));

            system.Tick(system.FixedDeltaTime);

            Assert.That(recorder.Attacks.Count(value => value.AttackerEntityId == allyId), Is.EqualTo(1));
            Assert.That(system.GetUnitSnapshot(allyId).State, Is.EqualTo(BattleUnitState.Attack));
        }

        [Test]
        public void Tick_TargetJustOutsideRange_MovesButDoesNotAttackThatTick()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            int allyId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -4.5001f)));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));

            system.Tick(system.FixedDeltaTime);

            BattleUnitSnapshot ally = system.GetUnitSnapshot(allyId);
            Assert.That(recorder.Attacks.Count(value => value.AttackerEntityId == allyId), Is.Zero);
            Assert.That(ally.State, Is.EqualTo(BattleUnitState.Move));
            Assert.That(ally.Position.y, Is.GreaterThan(-4.5001f));
            Assert.That(-ally.Position.y, Is.EqualTo(ally.Stats.Range).Within(0.00001f));
        }

        [TestCase(3.5f, true)]
        [TestCase(3.5001f, false)]
        public void TargetSearch_UsesConfiguredCirclePlusColliderBoundary(
            float targetDistance,
            bool expectsTarget)
        {
            GameConfig localConfig = GameConfig.Load();
            localConfig.Economy.Battle.TargetSearchRadius = 3f;
            localConfig.Economy.Battle.ColliderRadius = 0.5f;
            var system = new BattleSystem(localConfig, Seed);
            systems.Add(system);
            int allyId = system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            int enemyId = system.Spawn(new UnitSpawnRequest(
                "e_shan", 1, new Vector2(0f, targetDistance)));

            system.Tick(system.FixedDeltaTime);

            Assert.That(system.GetUnitSnapshot(allyId).TargetEntityId,
                expectsTarget ? Is.EqualTo(enemyId) : Is.Null);
        }

        [Test]
        public void MinRange_TargetExactlyOnInnerBoundary_Attacks()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            UnitDef definition = config.GetUnit("nuc");
            int attackerId = system.Spawn(new UnitSpawnRequest(
                definition.Id, 1, new Vector2(0f, -definition.MinRange.Base)));
            int targetId = system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));

            system.Tick(system.FixedDeltaTime);

            Assert.That(system.GetUnitSnapshot(attackerId).TargetEntityId, Is.EqualTo(targetId));
            Assert.That(recorder.Attacks.Any(value => value.AttackerEntityId == attackerId), Is.True);
            Assert.That(system.GetUnitSnapshot(attackerId).State, Is.EqualTo(BattleUnitState.Attack));
        }

        [Test]
        public void MinRange_TargetJustInsideInnerBoundary_IsIgnoredAndUnitDoesNotRetreat()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            UnitDef definition = config.GetUnit("nuc");
            Vector2 start = new Vector2(0f, -(definition.MinRange.Base - 0.01f));
            int attackerId = system.Spawn(new UnitSpawnRequest(definition.Id, 1, start));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));

            system.Tick(system.FixedDeltaTime);

            BattleUnitSnapshot attacker = system.GetUnitSnapshot(attackerId);
            Assert.That(attacker.TargetEntityId, Is.Null);
            Assert.That(attacker.State, Is.EqualTo(BattleUnitState.Idle));
            Assert.That(attacker.Position, Is.EqualTo(start), "Minimum range does not introduce retreat movement.");
            Assert.That(recorder.Attacks.Any(value => value.AttackerEntityId == attackerId), Is.False);
        }

        [Test]
        public void MinRange_CloserInnerCandidateIsSkippedForLegalAnnulusCandidate()
        {
            BattleSystem system = CreateSystem();
            int attackerId = system.Spawn(new UnitSpawnRequest("nuc", 1, Vector2.zero));
            int innerId = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, 2f)));
            int legalId = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, 3f)));

            system.Tick(system.FixedDeltaTime);

            Assert.That(system.GetUnitSnapshot(attackerId).TargetEntityId, Is.EqualTo(legalId));
            Assert.That(system.GetUnitSnapshot(attackerId).TargetEntityId, Is.Not.EqualTo(innerId));
        }

        [Test]
        public void Targeting_BeforeRetargetIntervalExpires_KeepsLivingTargetWhenCloserEnemyAppears()
        {
            BattleSystem system = CreateSystem();
            int allyId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -4.5f)));
            int originalTargetId = system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));

            system.Tick(system.FixedDeltaTime);
            Assert.That(system.GetUnitSnapshot(allyId).TargetEntityId, Is.EqualTo(originalTargetId));

            int closerTargetId = system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(0f, -3.5f)));
            system.Tick(system.FixedDeltaTime);

            BattleUnitSnapshot ally = system.GetUnitSnapshot(allyId);
            Assert.That(system.SimulatedTimeSeconds, Is.LessThan(config.Economy.Battle.RetargetInterval));
            Assert.That(ally.TargetEntityId, Is.EqualTo(originalTargetId));
            Assert.That(ally.TargetEntityId, Is.Not.EqualTo(closerTargetId));
        }

        [Test]
        public void Targeting_WhenCurrentTargetDies_IgnoresThrottleAndLocksAnotherEnemyNextTick()
        {
            BattleSystem system = CreateSystem();
            int allyId = system.Spawn(new UnitSpawnRequest("dao", 1, new Vector2(0f, -0.9f)));
            int firstTargetId = system.Spawn(new UnitSpawnRequest("e_liu", 1, Vector2.zero));
            int backupTargetId = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, 20f)));

            TickUntilDead(system, firstTargetId, 120);
            Assert.That(system.GetUnitSnapshot(allyId).TargetEntityId, Is.EqualTo(firstTargetId));

            system.Tick(system.FixedDeltaTime);

            BattleUnitSnapshot ally = system.GetUnitSnapshot(allyId);
            Assert.That(ally.TargetEntityId, Is.EqualTo(backupTargetId));
            Assert.That(ally.State, Is.EqualTo(BattleUnitState.Move));
        }

        [Test]
        public void Attack_FirstShotFiresOnFirstEligibleTick()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            int allyId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -4.5f)));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));

            system.Tick(system.FixedDeltaTime);

            UnitAttackedEvent attack = recorder.Attacks.Single(value => value.AttackerEntityId == allyId);
            Assert.That(attack.TickIndex, Is.EqualTo(1));
            Assert.That(attack.SimulatedTimeSeconds,
                Is.EqualTo(system.FixedDeltaTime).Within(0.0000001d));
        }

        [Test]
        public void Attack_OneAttackPerSecond_FiresThreeTimesThroughTickSixtyOne()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            int allyId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -4.5f)));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));

            Tick(system, 61);

            UnitAttackedEvent[] attacks = recorder.Attacks
                .Where(value => value.AttackerEntityId == allyId)
                .ToArray();
            Assert.That(attacks, Has.Length.EqualTo(3));
            Assert.That(attacks.Select(value => value.TickIndex), Is.EqualTo(new long[] { 1, 31, 61 }));
        }

        [Test]
        public void Damage_UsesDataFormulaWithCurrentAttackerAndTargetStats()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            int allyId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -4.5f)));
            int enemyId = system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));
            BattleUnitSnapshot ally = system.GetUnitSnapshot(allyId);
            BattleUnitSnapshot enemy = system.GetUnitSnapshot(enemyId);

            system.Tick(system.FixedDeltaTime);

            DamageDealtEvent damage = recorder.Damage.Single(value => value.SourceEntityId == allyId);
            int expected = Formula.Damage(
                ally.Stats.Atk,
                ally.AttackType,
                ally.BonusVs,
                enemy.Stats.Armor,
                enemy.ArmorType,
                enemy.UnitType,
                ally.Stats.Pierce,
                config.Economy);
            Assert.That(damage.Amount, Is.EqualTo(expected));
            Assert.That(damage.HpBefore, Is.EqualTo(enemy.Stats.MaxHp));
            Assert.That(damage.HpAfter, Is.EqualTo(enemy.Stats.MaxHp - expected));
            Assert.That(damage.IsLethal, Is.False);
        }

        [Test]
        public void Duel_DaoKillsEnemyCavalryOnLockedTickOneHundredOne()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            int allyId = system.Spawn(new UnitSpawnRequest("dao", 1, new Vector2(0f, -0.9f)));
            int enemyId = system.Spawn(new UnitSpawnRequest("e_liu", 1, Vector2.zero));

            TickUntilDead(system, enemyId, 120);

            BattleUnitSnapshot enemy = system.GetUnitSnapshot(enemyId);
            UnitDiedEvent death = recorder.Deaths.Single(value => value.EntityId == enemyId);
            Assert.That(enemy.State, Is.EqualTo(BattleUnitState.Dead));
            Assert.That(enemy.CurrentHp, Is.Zero);
            Assert.That(death.KillerEntityId, Is.EqualTo(allyId));
            Assert.That(death.TickIndex, Is.EqualTo(101));
            Assert.That(system.TickIndex, Is.EqualTo(101));
            Assert.That(death.SimulatedTimeSeconds,
                Is.EqualTo((double)system.FixedDeltaTime * 101d).Within(0.000001d));
        }

        [Test]
        public void Death_IsPublishedOnlyOnce_AndDeadUnitNeverAttacksAgain()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            system.Spawn(new UnitSpawnRequest("dao", 1, new Vector2(0f, -0.9f)));
            int enemyId = system.Spawn(new UnitSpawnRequest("e_liu", 1, Vector2.zero));

            TickUntilDead(system, enemyId, 120);
            int enemyAttacksAtDeath = recorder.Attacks.Count(value => value.AttackerEntityId == enemyId);
            Tick(system, 120);

            Assert.That(recorder.Deaths.Count(value => value.EntityId == enemyId), Is.EqualTo(1));
            Assert.That(recorder.Attacks.Count(value => value.AttackerEntityId == enemyId),
                Is.EqualTo(enemyAttacksAtDeath));
        }

        [Test]
        public void EnemyDeath_AccumulatesNormalCoinDropAndPublishesTotal()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            system.Spawn(new UnitSpawnRequest("dao", 1, new Vector2(0f, -0.9f)));
            int enemyId = system.Spawn(new UnitSpawnRequest("e_liu", 1, Vector2.zero));

            TickUntilDead(system, enemyId, 120);

            int expected = Formula.DropCoins(EnemyRank.Normal, config.Economy);
            CoinDroppedEvent coin = recorder.Coins.Single();
            Assert.That(coin.SourceEntityId, Is.EqualTo(enemyId));
            Assert.That(coin.Rank, Is.EqualTo(EnemyRank.Normal));
            Assert.That(coin.Amount, Is.EqualTo(expected));
            Assert.That(coin.TotalCoins, Is.EqualTo(expected));
            Assert.That(system.DroppedCoins, Is.EqualTo(expected));
        }

        [Test]
        public void EnemyDeath_UsesRequestedEliteCoinRank()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            system.Spawn(new UnitSpawnRequest("dao", 1, new Vector2(0f, -0.9f)));
            int enemyId = system.Spawn(
                new UnitSpawnRequest("e_liu", 1, Vector2.zero, EnemyRank.Elite));

            TickUntilDead(system, enemyId, 120);

            int expected = Formula.DropCoins(EnemyRank.Elite, config.Economy);
            Assert.That(system.DroppedCoins, Is.EqualTo(expected));
            Assert.That(recorder.Coins.Single().Amount, Is.EqualTo(expected));
            Assert.That(recorder.Coins.Single().Rank, Is.EqualTo(EnemyRank.Elite));
        }

        [Test]
        public void AllyDeath_DoesNotDropCoins()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));
            int allyId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -0.9f)));

            TickUntilDead(system, allyId, 600);

            Assert.That(system.GetUnitSnapshot(allyId).State, Is.EqualTo(BattleUnitState.Dead));
            Assert.That(system.DroppedCoins, Is.Zero);
            Assert.That(recorder.Coins, Is.Empty);
        }

        [Test]
        public void Events_HaveCompletePayloadContiguousSequenceAndAttackDamageDeathCoinOrder()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            int allyId = system.Spawn(new UnitSpawnRequest("dao", 1, new Vector2(0f, -0.9f)));
            int enemyId = system.Spawn(new UnitSpawnRequest("e_liu", 1, Vector2.zero));

            TickUntilDead(system, enemyId, 120);

            UnitSpawnedEvent spawned = recorder.Spawns[0];
            Assert.That(spawned.EntityId, Is.EqualTo(allyId));
            Assert.That(spawned.DefinitionId, Is.EqualTo("dao"));
            Assert.That(spawned.Team, Is.EqualTo(BattleTeam.Ally));
            Assert.That(spawned.Level, Is.EqualTo(1));
            Assert.That(spawned.Stats.MaxHp, Is.GreaterThan(0f));
            Assert.That(spawned.CurrentHp, Is.EqualTo(spawned.Stats.MaxHp));
            Assert.That(spawned.Position, Is.EqualTo(new Vector2(0f, -0.9f)));
            Assert.That(spawned.Facing, Is.EqualTo(Vector2.up));
            Assert.That(spawned.State, Is.EqualTo(BattleUnitState.Idle));

            UnitAttackedEvent firstAttack = recorder.Attacks[0];
            DamageDealtEvent firstDamage = recorder.Damage[0];
            Assert.That(firstAttack.AttackId, Is.EqualTo(firstDamage.AttackId));
            Assert.That(firstAttack.AttackerEntityId, Is.EqualTo(firstDamage.SourceEntityId));
            Assert.That(firstAttack.TargetEntityId, Is.EqualTo(firstDamage.TargetEntityId));
            Assert.That(firstAttack.AttackerDefinitionId, Is.EqualTo("dao"));
            Assert.That(firstAttack.TargetDefinitionId, Is.EqualTo("e_liu"));
            Assert.That(firstAttack.AttackerTeam, Is.EqualTo(BattleTeam.Ally));
            Assert.That(firstAttack.TargetTeam, Is.EqualTo(BattleTeam.Enemy));

            for (int index = 0; index < recorder.Sequences.Count; index++)
            {
                Assert.That(recorder.Sequences[index], Is.EqualTo(index + 1L), $"event[{index}]");
            }

            int deathIndex = recorder.Kinds.FindIndex(value => value == "Died");
            Assert.That(deathIndex, Is.GreaterThanOrEqualTo(2));
            Assert.That(recorder.Kinds[deathIndex - 2], Is.EqualTo("Attack"));
            Assert.That(recorder.Kinds[deathIndex - 1], Is.EqualTo("Damage"));
            Assert.That(recorder.Kinds[deathIndex], Is.EqualTo("Died"));
            Assert.That(recorder.Kinds[deathIndex + 1], Is.EqualTo("Coin"));
            Assert.That(recorder.Deaths.Single().EntityId, Is.EqualTo(enemyId));
            Assert.That(recorder.Deaths.Single().FinalHp, Is.Zero);
            Assert.That(recorder.Coins.Single().Position,
                Is.EqualTo(recorder.Deaths.Single().Position));
        }

        [Test]
        public void Separation_SameColumnAlliesRemainAtConfiguredMinimumDistance()
        {
            BattleSystem system = CreateSystem();
            int firstId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -1f)));
            int secondId = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0.2f, -0.9f)));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, 8f)));

            system.Tick(system.FixedDeltaTime);

            BattleUnitSnapshot first = system.GetUnitSnapshot(firstId);
            BattleUnitSnapshot second = system.GetUnitSnapshot(secondId);
            Assert.That(Mathf.Abs(first.Position.y - second.Position.y),
                Is.GreaterThanOrEqualTo(config.Economy.Battle.SeparationDistance - 0.00001f));
            Assert.That(Mathf.Abs(first.Position.x - second.Position.x),
                Is.LessThanOrEqualTo(config.Economy.Battle.SameColumnTolerance));
        }

        [Test]
        public void SameSeed_ProducesIdenticalCompleteEventStreamEntryByEntry()
        {
            IReadOnlyList<string> first = RunDeterministicEventScenario(0xC0FFEEu);
            IReadOnlyList<string> second = RunDeterministicEventScenario(0xC0FFEEu);

            Assert.That(first.Count, Is.GreaterThan(10));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void CaptureSnapshot_PreservesStableEntityOrder()
        {
            BattleSystem system = CreateSystem();
            int first = system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -5f)));
            int second = system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(0f, 5f)));

            IReadOnlyList<BattleUnitSnapshot> snapshot = system.CaptureSnapshot();

            Assert.That(snapshot.Select(value => value.EntityId), Is.EqualTo(new[] { first, second }));
            Assert.That(system.GetAliveCount(BattleTeam.Ally), Is.EqualTo(1));
            Assert.That(system.GetAliveCount(BattleTeam.Enemy), Is.EqualTo(1));
        }

        [Test]
        public void ThreeGongVersusFiveEnemyCavalry_ManuallyTicksToOneSideEliminated()
        {
            var recorder = new RecordingBattleEvents();
            BattleSystem system = CreateSystem(recorder);
            system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(-4f, -8f)));
            system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(0f, -8f)));
            system.Spawn(new UnitSpawnRequest("gong", 1, new Vector2(4f, -8f)));
            system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(-8f, 0f)));
            system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(-4f, 0f)));
            system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(0f, 0f)));
            system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(4f, 0f)));
            system.Spawn(new UnitSpawnRequest("e_liu", 1, new Vector2(8f, 0f)));

            int safetyTicks = config.Economy.Battle.TickRateHz * 30;
            while (system.GetAliveCount(BattleTeam.Ally) > 0
                   && system.GetAliveCount(BattleTeam.Enemy) > 0
                   && system.TickIndex < safetyTicks)
            {
                system.Tick(system.FixedDeltaTime);
            }

            Assert.That(
                system.GetAliveCount(BattleTeam.Ally) == 0 || system.GetAliveCount(BattleTeam.Enemy) == 0,
                Is.True);
            Assert.That(system.TickIndex, Is.LessThan(safetyTicks));
            int enemyDeaths = recorder.Deaths.Count(value => value.Team == BattleTeam.Enemy);
            Assert.That(system.DroppedCoins,
                Is.EqualTo(enemyDeaths * Formula.DropCoins(EnemyRank.Normal, config.Economy)));
        }

        private BattleSystem CreateSystem(IBattleEvents events = null)
        {
            var system = new BattleSystem(config, Seed, events);
            systems.Add(system);
            return system;
        }

        private static void Tick(BattleSystem system, int count)
        {
            for (int index = 0; index < count; index++)
            {
                system.Tick(system.FixedDeltaTime);
            }
        }

        private static void TickUntilDead(BattleSystem system, int entityId, int maximumTicks)
        {
            for (int index = 0;
                 index < maximumTicks && system.GetUnitSnapshot(entityId).State != BattleUnitState.Dead;
                 index++)
            {
                system.Tick(system.FixedDeltaTime);
            }

            Assert.That(system.GetUnitSnapshot(entityId).State, Is.EqualTo(BattleUnitState.Dead),
                $"Entity {entityId} was still alive after {maximumTicks} ticks.");
        }

        private IReadOnlyList<string> RunDeterministicEventScenario(uint seed)
        {
            var recorder = new RecordingBattleEvents();
            var system = new BattleSystem(config, seed, recorder);
            try
            {
                system.Spawn(new UnitSpawnRequest("gong", 2, new Vector2(0f, -4f)));
                system.Spawn(new UnitSpawnRequest("e_liu", 2, new Vector2(1f, 0f)));
                system.Spawn(new UnitSpawnRequest("e_liu", 2, new Vector2(-1f, 0f)));

                int maximumTicks = config.Economy.Battle.TickRateHz * 30;
                while (system.GetAliveCount(BattleTeam.Ally) > 0
                       && system.GetAliveCount(BattleTeam.Enemy) > 0
                       && system.TickIndex < maximumTicks)
                {
                    system.Tick(system.FixedDeltaTime);
                }

                Assert.That(
                    system.GetAliveCount(BattleTeam.Ally) == 0
                    || system.GetAliveCount(BattleTeam.Enemy) == 0,
                    Is.True,
                    "Determinism scenario did not terminate.");
                return recorder.CanonicalEvents.ToArray();
            }
            finally
            {
                system.Dispose();
            }
        }

        private sealed class RecordingBattleEvents : IBattleEvents
        {
            private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

            internal List<UnitSpawnedEvent> Spawns { get; } = new List<UnitSpawnedEvent>();

            internal List<UnitAttackedEvent> Attacks { get; } = new List<UnitAttackedEvent>();

            internal List<DamageDealtEvent> Damage { get; } = new List<DamageDealtEvent>();

            internal List<UnitDiedEvent> Deaths { get; } = new List<UnitDiedEvent>();

            internal List<CoinDroppedEvent> Coins { get; } = new List<CoinDroppedEvent>();

            internal List<string> Kinds { get; } = new List<string>();

            internal List<long> Sequences { get; } = new List<long>();

            internal List<string> CanonicalEvents { get; } = new List<string>();

            public void UnitSpawned(UnitSpawnedEvent eventData)
            {
                Spawns.Add(eventData);
                Add(
                    "Spawn",
                    eventData.Sequence,
                    string.Join("|",
                        eventData.TickIndex,
                        D(eventData.SimulatedTimeSeconds),
                        eventData.EntityId,
                        eventData.DefinitionId,
                        eventData.Team,
                        eventData.Level,
                        Stats(eventData.Stats),
                        F(eventData.CurrentHp),
                        V(eventData.Position),
                        V(eventData.Facing),
                        eventData.State));
            }

            public void UnitAttacked(UnitAttackedEvent eventData)
            {
                Attacks.Add(eventData);
                Add(
                    "Attack",
                    eventData.Sequence,
                    string.Join("|",
                        eventData.TickIndex,
                        D(eventData.SimulatedTimeSeconds),
                        eventData.AttackId,
                        eventData.AttackerEntityId,
                        eventData.TargetEntityId,
                        eventData.AttackerDefinitionId,
                        eventData.TargetDefinitionId,
                        eventData.AttackerTeam,
                        eventData.TargetTeam,
                        V(eventData.AttackerPosition),
                        V(eventData.TargetPosition),
                        V(eventData.Facing)));
            }

            public void DamageDealt(DamageDealtEvent eventData)
            {
                Damage.Add(eventData);
                Add(
                    "Damage",
                    eventData.Sequence,
                    string.Join("|",
                        eventData.TickIndex,
                        D(eventData.SimulatedTimeSeconds),
                        eventData.AttackId,
                        eventData.SourceEntityId,
                        eventData.TargetEntityId,
                        eventData.Amount,
                        F(eventData.HpBefore),
                        F(eventData.HpAfter),
                        F(eventData.MaxHp),
                        V(eventData.HitPosition),
                        eventData.IsLethal));
            }

            public void UnitDied(UnitDiedEvent eventData)
            {
                Deaths.Add(eventData);
                Add(
                    "Died",
                    eventData.Sequence,
                    string.Join("|",
                        eventData.TickIndex,
                        D(eventData.SimulatedTimeSeconds),
                        eventData.EntityId,
                        eventData.DefinitionId,
                        eventData.Team,
                        eventData.KillerEntityId,
                        V(eventData.Position),
                        F(eventData.FinalHp),
                        F(eventData.MaxHp),
                        eventData.RewardRank?.ToString() ?? "-"));
            }

            public void CoinDropped(CoinDroppedEvent eventData)
            {
                Coins.Add(eventData);
                Add(
                    "Coin",
                    eventData.Sequence,
                    string.Join("|",
                        eventData.TickIndex,
                        D(eventData.SimulatedTimeSeconds),
                        eventData.SourceEntityId,
                        eventData.SourceDefinitionId,
                        eventData.Rank,
                        eventData.Amount,
                        eventData.TotalCoins,
                        V(eventData.Position)));
            }

            private void Add(string kind, long sequence, string payload)
            {
                Kinds.Add(kind);
                Sequences.Add(sequence);
                CanonicalEvents.Add(string.Concat(kind, "|", sequence.ToString(Invariant), "|", payload));
            }

            private static string Stats(BattleStats value)
            {
                return string.Join(",",
                    F(value.MaxHp),
                    F(value.Atk),
                    F(value.Range),
                    F(value.AtkSpeed),
                    F(value.Cooldown),
                    F(value.Armor),
                    F(value.Pierce),
                    F(value.MoveSpeed));
            }

            private static string V(Vector2 value)
            {
                return string.Concat(F(value.x), ",", F(value.y));
            }

            private static string F(float value)
            {
                return value.ToString("R", Invariant);
            }

            private static string D(double value)
            {
                return value.ToString("R", Invariant);
            }
        }
    }
}
