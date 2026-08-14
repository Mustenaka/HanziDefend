using System;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using NUnit.Framework;
using UnityEngine;

namespace HanziDefend.Tests.PlayMode
{
    [TestFixture]
    public sealed class BattleTraitTests
    {
        private const uint Seed = 0xB6000001u;
        private readonly List<BattleSystem> systems = new List<BattleSystem>();
        private GameConfig config;

        [SetUp]
        public void SetUp()
        {
            config = GameConfig.Load();
            config.Economy.Battle.ColliderRadius = 0.1f;
            config.Economy.Battle.SameColumnTolerance = 0.01f;
            config.Economy.Battle.SeparationDistance = 0.01f;
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = systems.Count - 1; index >= 0; index--)
            {
                systems[index].Dispose();
            }

            systems.Clear();
        }

        [Test]
        public void Charge_FirstStrikeUsesConfiguredMultiplier()
        {
            PrepareUnit("qqi", 100f, 10f, 2f, 30f, 3f);
            PrepareUnit("e_shan", 1000f, 0f, 0.1f, 0f, 0f);
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            int attacker = system.Spawn(new UnitSpawnRequest("qqi", 1, Vector2.zero));
            int target = system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.up));

            system.Tick(system.FixedDeltaTime);

            UnitTraitDef trait = Trait("qqi", UnitTraitType.Charge);
            int expected = Formula.Damage(10f * trait.Multiplier, 0f, 0f, config.Economy);
            Assert.That(DamageFrom(recorder, attacker).Single().TargetEntityId, Is.EqualTo(target));
            Assert.That(DamageFrom(recorder, attacker).Single().Amount, Is.EqualTo(expected));
        }

        [Test]
        public void Charge_SecondStrikeReturnsToOrdinaryAttack()
        {
            PrepareUnit("qqi", 100f, 10f, 2f, 30f, 3f);
            PrepareUnit("e_shan", 1000f, 0f, 0.1f, 0f, 0f);
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            int attacker = system.Spawn(new UnitSpawnRequest("qqi", 1, Vector2.zero));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.up));

            system.Tick(system.FixedDeltaTime);
            system.Tick(system.FixedDeltaTime);

            DamageDealtEvent[] hits = DamageFrom(recorder, attacker).ToArray();
            UnitTraitDef trait = Trait("qqi", UnitTraitType.Charge);
            Assert.That(hits, Has.Length.EqualTo(2));
            Assert.That(hits[0].Amount,
                Is.EqualTo(Formula.Damage(10f * trait.Multiplier, 0f, 0f, config.Economy)));
            Assert.That(hits[1].Amount,
                Is.EqualTo(Formula.Damage(10f, 0f, 0f, config.Economy)));
        }

        [Test]
        public void Charge_AfterLethalFirstStrikeMovesAtOrdinarySpeed()
        {
            PrepareUnit("qqi", 100f, 100f, 1f, 30f, 3f);
            PrepareUnit("e_shan", 100f, 0f, 0.1f, 0f, 0f);
            BattleSystem system = CreateSystem();
            int attacker = system.Spawn(new UnitSpawnRequest("qqi", 1, Vector2.zero));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, 0.5f)));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, 10f)));

            system.Tick(system.FixedDeltaTime);
            system.Tick(system.FixedDeltaTime);

            float expectedTravel = 3f * system.FixedDeltaTime;
            Assert.That(system.GetUnitSnapshot(attacker).Position.y,
                Is.EqualTo(expectedTravel).Within(0.0001f));
        }

        [Test]
        public void Trample_PathDamagesEveryUnitExactlyOnce()
        {
            PrepareUnit("zqi", 100f, 10f, 0.5f, 0f, 300f);
            PrepareUnit("e_shan", 100f, 0f, 0.1f, 0f, 0f);
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            int attacker = system.Spawn(new UnitSpawnRequest("zqi", 1, Vector2.zero));
            int[] targets = SpawnLine(system, "e_shan", 2f, 4f, 6f);

            system.Tick(system.FixedDeltaTime);

            DamageDealtEvent[] hits = DamageFrom(recorder, attacker).ToArray();
            Assert.That(hits.Select(value => value.TargetEntityId), Is.EqualTo(targets));
            Assert.That(hits.Select(value => value.Amount),
                Is.All.EqualTo(Formula.Damage(10f, 0f, 0f, config.Economy)));
        }

        [Test]
        public void Trample_UnitOutsideConfiguredPathWidthIsUntouched()
        {
            PrepareUnit("zqi", 100f, 10f, 0.5f, 0f, 300f);
            PrepareUnit("e_shan", 100f, 0f, 0.1f, 0f, 0f);
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            int attacker = system.Spawn(new UnitSpawnRequest("zqi", 1, Vector2.zero));
            SpawnLine(system, "e_shan", 2f, 6f);
            int outside = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0.5f, 4f)));

            system.Tick(system.FixedDeltaTime);

            Assert.That(DamageFrom(recorder, attacker).Select(value => value.TargetEntityId),
                Has.None.EqualTo(outside));
            Assert.That(system.GetUnitSnapshot(outside).CurrentHp, Is.EqualTo(100f));
        }

        [Test]
        public void Trample_CompletedRushDoesNotDamagePathTwice()
        {
            PrepareUnit("zqi", 100f, 10f, 0.5f, 0f, 300f);
            PrepareUnit("e_shan", 100f, 0f, 0.1f, 0f, 0f);
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            int attacker = system.Spawn(new UnitSpawnRequest("zqi", 1, Vector2.zero));
            SpawnLine(system, "e_shan", 2f, 4f, 6f);

            system.Tick(system.FixedDeltaTime);
            int firstPassCount = DamageFrom(recorder, attacker).Count();
            system.Tick(system.FixedDeltaTime);

            Assert.That(firstPassCount, Is.EqualTo(3));
            Assert.That(DamageFrom(recorder, attacker), Has.Count.EqualTo(firstPassCount));
        }

        [Test]
        public void PiercingShot_UsesConfiguredTwoTimesThirtyPercentDecaySequence()
        {
            PrepareUnit("nub", 100f, 10f, 10f, 30f, 0f);
            PrepareUnit("e_shan", 1000f, 0f, 0.1f, 0f, 0f);
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            int attacker = system.Spawn(new UnitSpawnRequest("nub", 1, Vector2.zero));
            SpawnLine(system, "e_shan", 2f, 4f, 6f);

            system.Tick(system.FixedDeltaTime);

            UnitTraitDef trait = Trait("nub", UnitTraitType.PiercingShot);
            DamageDealtEvent[] hits = DamageFrom(recorder, attacker).ToArray();
            float second = trait.Multiplier * (1f - trait.DecayRate);
            float third = second * (1f - trait.DecayRate);
            Assert.That(hits.Select(value => value.Amount), Is.EqualTo(new[]
            {
                Formula.Damage(10f * trait.Multiplier, 0f, 0f, config.Economy),
                Formula.Damage(10f * second, 0f, 0f, config.Economy),
                Formula.Damage(10f * third, 0f, 0f, config.Economy)
            }));
        }

        [Test]
        public void PiercingShot_OffAxisUnitIsNotHit()
        {
            PrepareUnit("nuc", 100f, 10f, 10f, 30f, 0f);
            PrepareUnit("e_shan", 1000f, 0f, 0.1f, 0f, 0f);
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            int attacker = system.Spawn(new UnitSpawnRequest("nuc", 1, Vector2.zero));
            SpawnLine(system, "e_shan", 2f, 6f);
            int outside = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0.5f, 4f)));

            system.Tick(system.FixedDeltaTime);

            Assert.That(DamageFrom(recorder, attacker).Select(value => value.TargetEntityId),
                Has.None.EqualTo(outside));
        }

        [Test]
        public void PiercingShot_ExcludesMainBaseWhenItIsThePrimaryTarget()
        {
            PrepareUnit("nuc", 100f, 10f, 10f, 30f, 0f);
            config.GetUnit("nuc").Targeting = TargetingMode.RushBase;
            LevelDef level = config.GetLevel("level_1_1");
            config.WaveSetsById[level.WaveSetId].Waves = Array.Empty<WaveDef>();
            config.Economy.Battle.EnemyBasePosition.X = 0f;
            config.Economy.Battle.EnemyBasePosition.Y = 2f;
            BattleSystem system = CreateEncounter();
            system.Spawn(new UnitSpawnRequest("nuc", 1, Vector2.zero));
            float before = system.GetBaseSnapshot(system.EnemyBaseEntityId).CurrentHp;

            system.Tick(system.FixedDeltaTime);

            Assert.That(Trait("nuc", UnitTraitType.PiercingShot).ExcludesMainBase, Is.True);
            Assert.That(system.GetBaseSnapshot(system.EnemyBaseEntityId).CurrentHp, Is.EqualTo(before));
        }

        [Test]
        public void FireAura_AttackInsideRadiusAddsConfiguredPercentDamage()
        {
            PrepareAuraAttackers();
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            int attacker = system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            int fireSource = system.Spawn(new UnitSpawnRequest("huo", 1, Vector2.right));
            int target = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, 4f)));

            system.Tick(system.FixedDeltaTime);

            UnitTraitDef aura = Trait("huo", UnitTraitType.FireAura);
            DamageDealtEvent burn = recorder.Damage.Single(value =>
                value.SourceEntityId == fireSource && value.TargetEntityId == target);
            Assert.That(burn.Amount,
                Is.EqualTo(Formula.Damage(1000f * aura.Multiplier, 0f, 0f, config.Economy)));
            Assert.That(DamageFrom(recorder, attacker), Has.Count.EqualTo(1));
        }

        [Test]
        public void FireAura_AttackOutsideRadiusAddsNoBurn()
        {
            PrepareAuraAttackers();
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            int attacker = system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            UnitTraitDef aura = Trait("huo", UnitTraitType.FireAura);
            int fireSource = system.Spawn(new UnitSpawnRequest(
                "huo", 1, new Vector2(aura.MinimumMultiplier + 0.25f, 0f)));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, 4f)));

            system.Tick(system.FixedDeltaTime);

            Assert.That(DamageFrom(recorder, attacker), Has.Count.EqualTo(1));
            Assert.That(DamageFrom(recorder, fireSource), Is.Empty);
        }

        [Test]
        public void IceAura_AttackInsideRadiusSlowsTargetByConfiguredAmount()
        {
            PrepareAuraAttackers();
            config.GetUnit("e_shan").MoveSpeed.Base = 3f;
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            system.Spawn(new UnitSpawnRequest("bing", 1, Vector2.right));
            int target = system.Spawn(new UnitSpawnRequest("e_shan", 1, new Vector2(0f, 4f)));

            system.Tick(system.FixedDeltaTime);

            UnitTraitDef aura = Trait("bing", UnitTraitType.IceAura);
            float expectedTravel = 3f * (1f - aura.Multiplier) * system.FixedDeltaTime;
            Assert.That(4f - system.GetUnitSnapshot(target).Position.y,
                Is.EqualTo(expectedTravel).Within(0.0001f));
        }

        [Test]
        public void IceBearer_IsImmuneToFireAuraDamage()
        {
            PrepareAuraAttackers();
            config.GetUnit("bing").Faction = UnitFaction.Enemy;
            config.GetUnit("bing").Hp.Base = 1000f;
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            int attacker = system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            int fireSource = system.Spawn(new UnitSpawnRequest("huo", 1, Vector2.right));
            system.Spawn(new UnitSpawnRequest("bing", 1, new Vector2(0f, 4f)));

            system.Tick(system.FixedDeltaTime);

            Assert.That(DamageFrom(recorder, attacker), Has.Count.EqualTo(1));
            Assert.That(DamageFrom(recorder, fireSource), Is.Empty);
        }

        [Test]
        public void FireBearer_IsImmuneToIceAuraSlow()
        {
            PrepareAuraAttackers();
            config.GetUnit("huo").Faction = UnitFaction.Enemy;
            config.GetUnit("huo").Hp.Base = 1000f;
            config.GetUnit("huo").MoveSpeed.Base = 3f;
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));
            system.Spawn(new UnitSpawnRequest("bing", 1, Vector2.right));
            int target = system.Spawn(new UnitSpawnRequest("huo", 1, new Vector2(0f, 4f)));

            system.Tick(system.FixedDeltaTime);

            float expectedTravel = 3f * system.FixedDeltaTime;
            Assert.That(4f - system.GetUnitSnapshot(target).Position.y,
                Is.EqualTo(expectedTravel).Within(0.0001f));
        }

        [Test]
        public void DeathSpawn_CreatesConfiguredThreeZuChildren()
        {
            Recorder recorder = KillAlliedRam(1, out _);

            UnitTraitDef trait = Trait("chc", UnitTraitType.DeathSpawn);
            UnitSpawnedEvent[] children = recorder.Spawns
                .Where(value => value.DefinitionId == trait.UnitId).ToArray();
            Assert.That(trait.Count, Is.EqualTo(3));
            Assert.That(children, Has.Length.EqualTo(trait.Count));
        }

        [Test]
        public void DeathSpawn_ChildrenPreserveParentTeamLevelAndPosition()
        {
            const int parentLevel = 2;
            Recorder recorder = KillAlliedRam(parentLevel, out Vector2 parentPosition);

            UnitSpawnedEvent[] children = recorder.Spawns
                .Where(value => value.DefinitionId == "zu").ToArray();
            Assert.That(children.Select(value => value.Team), Is.All.EqualTo(BattleTeam.Ally));
            Assert.That(children.Select(value => value.Level), Is.All.EqualTo(parentLevel));
            Assert.That(children.Select(value => value.Position), Is.All.EqualTo(parentPosition));
        }

        [Test]
        public void DeathSpawn_EnemyChildrenPreserveTeamButDoNotDropExtraCoins()
        {
            PrepareUnit("gong", 100f, 100f, 10f, 30f, 0f);
            PrepareUnit("chc", 1f, 0f, 0.1f, 0f, 0f);
            PrepareUnit("zu", 1f, 0f, 0.1f, 0f, 0f);
            config.Economy.Battle.EnemySpawnCenter.X = 0f;
            config.Economy.Battle.EnemySpawnCenter.Y = 1f;
            LevelDef level = config.GetLevel("level_1_1");
            config.WaveSetsById[level.WaveSetId].Waves = new[]
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
                            UnitId = "chc",
                            Level = 1,
                            Count = 1,
                            SpreadX = 0f,
                            IntervalSec = 0f
                        }
                    }
                }
            };
            var recorder = new Recorder();
            BattleSystem system = CreateEncounter(recorder);
            system.Spawn(new UnitSpawnRequest("gong", 1, Vector2.zero));

            for (int index = 0; index < 5; index++)
            {
                system.Tick(system.FixedDeltaTime);
            }

            UnitSpawnedEvent parent = recorder.Spawns.Single(value => value.DefinitionId == "chc");
            UnitSpawnedEvent[] children = recorder.Spawns
                .Where(value => value.DefinitionId == "zu").ToArray();
            Assert.That(parent.Team, Is.EqualTo(BattleTeam.Enemy));
            Assert.That(children, Has.Length.EqualTo(3));
            Assert.That(children.Select(value => value.Team), Is.All.EqualTo(BattleTeam.Enemy));
            Assert.That(recorder.Coins, Has.Count.EqualTo(1));
            Assert.That(recorder.Coins[0].SourceEntityId, Is.EqualTo(parent.EntityId));
        }

        private void PrepareAuraAttackers()
        {
            PrepareUnit("gong", 100f, 10f, 5f, 30f, 0f);
            PrepareUnit("huo", 100f, 0f, 0.1f, 0f, 0f);
            PrepareUnit("bing", 100f, 0f, 0.1f, 0f, 0f);
            PrepareUnit("e_shan", 1000f, 0f, 0.1f, 0f, 0f);
        }

        private Recorder KillAlliedRam(int level, out Vector2 position)
        {
            PrepareUnit("chc", 1f, 0f, 0.1f, 0f, 0f);
            PrepareUnit("zu", 100f, 0f, 0.1f, 0f, 0f);
            PrepareUnit("e_shan", 100f, 100f, 5f, 30f, 0f);
            position = new Vector2(0f, 1f);
            var recorder = new Recorder();
            BattleSystem system = CreateSystem(recorder);
            system.Spawn(new UnitSpawnRequest("chc", level, position));
            system.Spawn(new UnitSpawnRequest("e_shan", 1, Vector2.zero));
            system.Tick(system.FixedDeltaTime);
            return recorder;
        }

        private void PrepareUnit(
            string id,
            float hp,
            float attack,
            float range,
            float attackSpeed,
            float moveSpeed)
        {
            UnitDef definition = config.GetUnit(id);
            SetCurve(definition.Hp, hp);
            SetCurve(definition.Atk, attack);
            SetCurve(definition.Range, range);
            SetCurve(definition.MinRange, 0f);
            SetCurve(definition.AtkSpeed, attackSpeed);
            SetCurve(definition.Cooldown, 0f);
            SetCurve(definition.Armor, 0f);
            SetCurve(definition.Pierce, 0f);
            SetCurve(definition.MoveSpeed, moveSpeed);
            definition.Targeting = TargetingMode.Nearest;
            definition.AtkType = AttackType.None;
            definition.ArmorType = ArmorType.Unarmored;
            definition.BonusVs = Array.Empty<BonusVsDef>();
        }

        private static void SetCurve(StatCurve curve, float value)
        {
            curve.Base = value;
            curve.Growth = 0f;
        }

        private UnitTraitDef Trait(string unitId, UnitTraitType type)
        {
            return config.GetUnit(unitId).Traits.Single(value => value.Type == type);
        }

        private BattleSystem CreateSystem(IBattleEvents events = null)
        {
            var system = new BattleSystem(config, Seed, events, simulatePhysics: false);
            systems.Add(system);
            return system;
        }

        private BattleSystem CreateEncounter(IBattleEvents events = null)
        {
            BattleSystem system = BattleSystem.CreateEncounter(
                config, "level_1_1", Seed, events, simulatePhysics: false);
            systems.Add(system);
            return system;
        }

        private static int[] SpawnLine(BattleSystem system, string id, params float[] yPositions)
        {
            return yPositions.Select(y =>
                system.Spawn(new UnitSpawnRequest(id, 1, new Vector2(0f, y)))).ToArray();
        }

        // Materialised on purpose: a lazy Where() sequence has no Count property, so every
        // `Has.Count` assertion against it threw ArgumentException instead of comparing anything.
        private static List<DamageDealtEvent> DamageFrom(Recorder recorder, int entityId)
        {
            return recorder.Damage.Where(value => value.SourceEntityId == entityId).ToList();
        }

        private sealed class Recorder : IBattleEvents
        {
            internal List<UnitSpawnedEvent> Spawns { get; } = new List<UnitSpawnedEvent>();
            internal List<DamageDealtEvent> Damage { get; } = new List<DamageDealtEvent>();
            internal List<UnitDiedEvent> Deaths { get; } = new List<UnitDiedEvent>();
            internal List<CoinDroppedEvent> Coins { get; } = new List<CoinDroppedEvent>();

            public void UnitSpawned(UnitSpawnedEvent eventData) => Spawns.Add(eventData);

            public void UnitAttacked(UnitAttackedEvent eventData)
            {
            }

            public void DamageDealt(DamageDealtEvent eventData) => Damage.Add(eventData);

            public void UnitDied(UnitDiedEvent eventData) => Deaths.Add(eventData);

            public void CoinDropped(CoinDroppedEvent eventData) => Coins.Add(eventData);
        }
    }
}
