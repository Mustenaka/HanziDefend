using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HanziDefend.Data;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode
{
    public sealed class GameConfigTests
    {
        private static readonly string[] ExpectedAllyIds =
        {
            "dun", "gong", "huo", "mao", "nu", "qi"
        };

        private static readonly string[] ExpectedEnemyIds =
        {
            "e_jia", "e_lang", "e_she", "e_zu"
        };

        [Test]
        public void Load_DefaultSource_LoadsRequiredDefinitionCounts()
        {
            GameConfig config = GameConfig.Load();

            Assert.That(config.AllyUnits.Count, Is.EqualTo(6), "ally unit count");
            Assert.That(config.EnemyUnits.Count, Is.EqualTo(4), "enemy unit count");
            Assert.That(config.Bosses.Count, Is.EqualTo(1), "boss count");
            Assert.That(config.Commanders.Count, Is.EqualTo(1), "commander count");
        }

        [Test]
        public void Load_DefaultSource_LoadsExactLockedIds()
        {
            GameConfig config = GameConfig.Load();

            Assert.That(
                config.AllyUnits.Select(value => value.Id).OrderBy(value => value).ToArray(),
                Is.EqualTo(ExpectedAllyIds));
            Assert.That(
                config.EnemyUnits.Select(value => value.Id).OrderBy(value => value).ToArray(),
                Is.EqualTo(ExpectedEnemyIds));
            Assert.That(config.Bosses.Select(value => value.Id).ToArray(), Is.EqualTo(new[] { "boss_lv" }));
            Assert.That(config.Commanders.Select(value => value.Id).ToArray(), Is.EqualTo(new[] { "cmd_bei" }));
        }

        [Test]
        public void Load_DefaultSource_IndexesEveryDefinitionById()
        {
            GameConfig config = GameConfig.Load();

            Assert.That(config.GetUnit("gong").DisplayName, Is.EqualTo("弓箭手"));
            Assert.That(config.GetUnit("e_lang").Faction, Is.EqualTo(UnitFaction.Enemy));
            Assert.That(config.GetBoss("boss_lv").Name, Is.EqualTo("吕"));
            Assert.That(config.GetBoss("boss_lv").DisplayName, Is.EqualTo("吕布"));
            Assert.That(config.GetCommander("cmd_bei").Name, Is.EqualTo("刘备"));
            Assert.That(config.GetWaveSet("main_20").Waves.Length, Is.EqualTo(20));
            Assert.That(config.GetLevel("level_1_5").StageIndex, Is.EqualTo(5));
            Assert.That(config.GetEffect("cmd_bei_active").Ops[0].Op, Is.EqualTo(EffectOpCode.Heal));
        }

        [Test]
        public void Load_DefaultSource_HasTwentyOrderedWavesAndBossFinale()
        {
            WaveDef[] waves = GameConfig.Load().GetWaveSet("main_20").Waves;

            Assert.That(waves.Length, Is.EqualTo(20));
            Assert.That(waves.Select(value => value.Index).ToArray(), Is.EqualTo(Enumerable.Range(1, 20).ToArray()));
            Assert.That(waves[19].Spawns.Length, Is.EqualTo(1));
            Assert.That(waves[19].Spawns[0].UnitId, Is.EqualTo("boss_lv"));
            Assert.That(waves[19].Spawns[0].Count, Is.EqualTo(1));
            Assert.That(waves.Take(18).All(value => value.RewardRank == EnemyRank.Normal), Is.True);
            Assert.That(waves[18].RewardRank, Is.EqualTo(EnemyRank.Elite));
            Assert.That(waves[19].RewardRank, Is.EqualTo(EnemyRank.Boss));
            Assert.That(waves.SelectMany(value => value.Spawns).All(value => value.Level == 1), Is.True);
        }

        [Test]
        public void Load_DefaultSource_HasOneMajorLevelWithFiveStagesAndLockedGridBounds()
        {
            GameConfig config = GameConfig.Load();

            Assert.That(config.WaveSets.Count, Is.EqualTo(1));
            Assert.That(config.Levels.Count, Is.EqualTo(5));
            Assert.That(config.Levels.Select(value => value.StageIndex).ToArray(), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(config.Levels[0].GridCols, Is.EqualTo(3));
            Assert.That(config.Levels[0].GridRows, Is.EqualTo(3));
            Assert.That(config.Levels.All(value => value.GridCols == 3), Is.True);
            Assert.That(config.Levels.All(value => value.StartCoins == 45), Is.True);
            Assert.That(config.Levels.All(value => value.GridMaxCols == 7), Is.True);
            Assert.That(config.Levels.All(value => value.GridMaxRows == 3), Is.True);
        }

        [Test]
        public void Load_DefaultSource_ContainsOnlySupportedEffectOpsAndTargets()
        {
            GameConfig config = GameConfig.Load();
            EffectOpCode[] actualOps = config.Effects
                .SelectMany(value => value.Ops)
                .Select(value => value.Op)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            EffectOpCode[] expectedOps =
            {
                EffectOpCode.AddStat,
                EffectOpCode.Heal,
                EffectOpCode.Damage,
                EffectOpCode.GrantShield,
                EffectOpCode.SpawnUnit,
                EffectOpCode.ModifyCoins
            };
            EffectTarget[] expectedTargets =
            {
                EffectTarget.SelfUnit,
                EffectTarget.AllyAll,
                EffectTarget.AllyAdjacent,
                EffectTarget.EnemyNearest,
                EffectTarget.EnemyInRadius,
                EffectTarget.EnemyBase
            };

            Assert.That(actualOps, Is.EquivalentTo(expectedOps));
            Assert.That(
                config.Effects.SelectMany(value => value.Ops).Select(value => value.Target).Distinct(),
                Is.EquivalentTo(expectedTargets));
        }

        [Test]
        public void Load_DefaultSource_HasExplicitEffectLifecycleAndLockedOwners()
        {
            GameConfig config = GameConfig.Load();

            Assert.That(config.Effects.All(value => value.Trigger != EffectTrigger.Unknown), Is.True);
            Assert.That(config.Effects.All(value => value.Stacking != EffectStackingRule.Unknown), Is.True);
            Assert.That(config.GetEffect("buff_atk_up").Trigger, Is.EqualTo(EffectTrigger.Manual));
            Assert.That(config.GetEffect("buff_atk_up").Stacking, Is.EqualTo(EffectStackingRule.Stack));
            Assert.That(config.GetEffect("buff_front_shield").Trigger, Is.EqualTo(EffectTrigger.Manual));
            Assert.That(config.GetEffect("cmd_bei_passive").Trigger, Is.EqualTo(EffectTrigger.BattleStart));
            Assert.That(config.GetEffect("cmd_bei_passive").Stacking, Is.EqualTo(EffectStackingRule.Refresh));
            Assert.That(config.GetEffect("cmd_bei_active").Trigger, Is.EqualTo(EffectTrigger.Manual));
            Assert.That(config.GetEffect("unit_huo_blast").Trigger, Is.EqualTo(EffectTrigger.SuicideContact));
            Assert.That(config.GetEffect("boss_lv_fury").Trigger, Is.EqualTo(EffectTrigger.UnitSpawn));
        }

        [TestCase("\"trigger\": \"Manual\"", "\"trigger\": \"EveryTick\"", "EveryTick")]
        [TestCase("\"stacking\": \"Stack\"", "\"stacking\": \"ReplaceAll\"", "ReplaceAll")]
        public void Load_InvalidEffectLifecycleEnum_ReportsSourceAndBadValue(
            string original,
            string replacement,
            string badValue)
        {
            string effects = ReplaceFirst(new JsonConfigSource().ReadText("effects.json"), original, replacement);
            var source = new OverrideSource(new JsonConfigSource(), "effects.json", effects);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("effects.json"));
            Assert.That(exception.Message, Does.Contain(badValue));
        }

        [Test]
        public void Load_MissingEffectTrigger_ReportsEffectAndField()
        {
            string effects = new JsonConfigSource().ReadText("effects.json")
                .Replace("      \"trigger\": \"Manual\",\r\n", string.Empty)
                .Replace("      \"trigger\": \"Manual\",\n", string.Empty);
            var source = new OverrideSource(new JsonConfigSource(), "effects.json", effects);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("buff_atk_up"));
            Assert.That(exception.Message, Does.Contain("invalid trigger"));
        }

        [TestCase("\"trigger\": \"BattleStart\"", "\"trigger\": \"Manual\"", "passive effect", "BattleStart")]
        [TestCase("\"trigger\": \"SuicideContact\"", "\"trigger\": \"Manual\"", "unit_huo_blast", "SuicideContact")]
        [TestCase("\"trigger\": \"UnitSpawn\"", "\"trigger\": \"Manual\"", "boss_lv_fury", "UnitSpawn")]
        public void Load_EffectTriggerOwnerMismatch_ReportsExpectedOwnership(
            string original,
            string replacement,
            string ownerFragment,
            string expectedTrigger)
        {
            string effects = ReplaceFirst(new JsonConfigSource().ReadText("effects.json"), original, replacement);
            var source = new OverrideSource(new JsonConfigSource(), "effects.json", effects);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain(ownerFragment));
            Assert.That(exception.Message, Does.Contain(expectedTrigger));
        }

        [Test]
        public void Load_EffectDurationBelowPermanentSentinel_IsRejected()
        {
            string effects = ReplaceFirst(
                new JsonConfigSource().ReadText("effects.json"),
                "\"duration\": -1.0",
                "\"duration\": -2.0");
            var source = new OverrideSource(new JsonConfigSource(), "effects.json", effects);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("duration must be -1 or non-negative"));
        }

        [Test]
        public void Formula_ConsumesEconomyJsonAsSingleNumericSource()
        {
            EconomyDef economy = GameConfig.Load().Economy;

            Assert.That(Formula.Damage(100f, 100f, 0f, economy), Is.EqualTo(50));
            Assert.That(Formula.RefreshCost(4, economy), Is.EqualTo(35));
            Assert.That(Formula.DropCoins(EnemyRank.Normal, economy), Is.EqualTo(1));
            Assert.That(Formula.DropCoins(EnemyRank.Elite, economy), Is.EqualTo(3));
            Assert.That(Formula.DropCoins(EnemyRank.Boss, economy), Is.EqualTo(20));
        }

        [Test]
        public void Load_DefaultSource_LoadsBattleRulesFromEconomyJson()
        {
            BattleRulesDef battle = GameConfig.Load().Economy.Battle;

            Assert.That(battle.TickRateHz, Is.EqualTo(30));
            Assert.That(battle.RetargetInterval, Is.EqualTo(0.2f));
            Assert.That(battle.TargetSearchRadius, Is.EqualTo(24f));
            Assert.That(battle.ColliderRadius, Is.EqualTo(0.35f));
            Assert.That(battle.SameColumnTolerance, Is.EqualTo(0.6f));
            Assert.That(battle.SeparationDistance, Is.EqualTo(0.8f));
            Assert.That((battle.AllyBasePosition.X, battle.AllyBasePosition.Y), Is.EqualTo((0f, -8f)));
            Assert.That((battle.EnemyBasePosition.X, battle.EnemyBasePosition.Y), Is.EqualTo((0f, 8f)));
            Assert.That((battle.EnemySpawnCenter.X, battle.EnemySpawnCenter.Y), Is.EqualTo((0f, 6f)));
        }

        [Test]
        public void Load_MissingWaveRewardRank_ReportsWaveAndField()
        {
            string waves = new JsonConfigSource().ReadText("waves.json")
                .Replace("          \"rewardRank\": \"Normal\",\r\n", string.Empty)
                .Replace("          \"rewardRank\": \"Normal\",\n", string.Empty);
            var source = new OverrideSource(new JsonConfigSource(), "waves.json", waves);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("wave 1"));
            Assert.That(exception.Message, Does.Contain("rewardRank"));
        }

        [Test]
        public void Load_InvalidWaveRewardRank_ReportsSourceAndBadValue()
        {
            string waves = new JsonConfigSource().ReadText("waves.json")
                .Replace("\"rewardRank\": \"Normal\"", "\"rewardRank\": \"Legendary\"");
            var source = new OverrideSource(new JsonConfigSource(), "waves.json", waves);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("waves.json"));
            Assert.That(exception.Message, Does.Contain("Legendary"));
        }

        [Test]
        public void Load_BossRankWithUnitDef_ReportsRankContract()
        {
            string waves = ReplaceFirst(
                new JsonConfigSource().ReadText("waves.json"),
                "\"rewardRank\": \"Normal\"",
                "\"rewardRank\": \"Boss\"");
            var source = new OverrideSource(new JsonConfigSource(), "waves.json", waves);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("rewardRank Boss requires a BossDef"));
            Assert.That(exception.Message, Does.Contain("e_zu"));
        }

        [TestCase(0)]
        [TestCase(5)]
        public void Load_WaveSpawnLevelOutsideContract_ReportsSpawnAndBounds(int invalidLevel)
        {
            string waves = ReplaceFirst(
                new JsonConfigSource().ReadText("waves.json"),
                "\"level\": 1",
                $"\"level\": {invalidLevel}");
            var source = new OverrideSource(new JsonConfigSource(), "waves.json", waves);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("spawn 'e_zu' level"));
            Assert.That(exception.Message, Does.Contain("[1,4]"));
        }

        [Test]
        public void Load_MissingBattleLayout_ReportsExactField()
        {
            string economy = new JsonConfigSource().ReadText("economy.json")
                .Replace("\"allyBasePosition\": {", "\"allyBasePosition\": null,")
                .Replace("      \"x\": 0.0,\r\n      \"y\": -8.0\r\n    },\r\n", string.Empty)
                .Replace("      \"x\": 0.0,\n      \"y\": -8.0\n    },\n", string.Empty);
            var source = new OverrideSource(new JsonConfigSource(), "economy.json", economy);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("economy.battle.allyBasePosition is required"));
        }

        [Test]
        public void Load_CoincidentBaseLayout_ReportsSeparationContract()
        {
            string economy = new JsonConfigSource().ReadText("economy.json")
                .Replace("\"y\": 8.0", "\"y\": -8.0");
            var source = new OverrideSource(new JsonConfigSource(), "economy.json", economy);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("allyBasePosition and enemyBasePosition must be different"));
        }

        [TestCase("\"tickRateHz\": 30", "\"tickRateHz\": 0", "tickRateHz must be positive")]
        [TestCase("\"retargetInterval\": 0.2", "\"retargetInterval\": -0.2", "retargetInterval must be positive")]
        [TestCase("\"targetSearchRadius\": 24.0", "\"targetSearchRadius\": NaN", "targetSearchRadius must be finite")]
        [TestCase("\"sameColumnTolerance\": 0.6", "\"sameColumnTolerance\": -0.1", "sameColumnTolerance cannot be negative")]
        public void Load_InvalidBattleRules_ReportsInvalidField(
            string original,
            string replacement,
            string expectedMessage)
        {
            string economy = new JsonConfigSource().ReadText("economy.json").Replace(original, replacement);
            var source = new OverrideSource(new JsonConfigSource(), "economy.json", economy);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain(expectedMessage));
        }

        [TestCase("units.json")]
        [TestCase("commanders.json")]
        [TestCase("waves.json")]
        [TestCase("levels.json")]
        [TestCase("effects.json")]
        [TestCase("economy.json")]
        public void JsonRoundTrip_PreservesEveryConfigDocument(string fileName)
        {
            var source = new JsonConfigSource();
            string input = source.ReadText(fileName);

            string firstSerialization = RoundTripOnce(fileName, input);
            string secondSerialization = RoundTripOnce(fileName, firstSerialization);

            Assert.That(secondSerialization, Is.EqualTo(firstSerialization));
        }

        [Test]
        public void JsonRoundTrip_PreservesRngState()
        {
            var rng = new Rng(0xCAFEBABEu);
            rng.NextUInt();
            RngState before = rng.SaveState();

            string json = JsonCodec.Serialize(before);
            RngState after = JsonCodec.Deserialize<RngState>(json);

            Assert.That(after.state, Is.EqualTo(before.state));
            Assert.That(new Rng(after).NextUInt(), Is.EqualTo(rng.NextUInt()));
        }

        [Test]
        public void Load_ReadsEachRequiredFileExactlyOnce()
        {
            var source = new RecordingSource(new JsonConfigSource());

            GameConfig.Load(source);

            Assert.That(source.ReadFiles, Is.EquivalentTo(new[]
            {
                "units.json", "commanders.json", "waves.json",
                "levels.json", "effects.json", "economy.json"
            }));
            Assert.That(source.ReadFiles.Count, Is.EqualTo(6));
        }

        [Test]
        public void Load_MalformedJson_ReportsTheSourceFile()
        {
            var source = new OverrideSource(new JsonConfigSource(), "units.json", "{ not-json }");

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("units.json"));
            Assert.That(exception.Message, Does.Contain("Failed to parse"));
        }

        [Test]
        public void Load_UnknownEnum_ReportsTheSourceFileAndBadValue()
        {
            string units = new JsonConfigSource().ReadText("units.json")
                .Replace("\"faction\": \"Ally\"", "\"faction\": \"Pirate\"");
            var source = new OverrideSource(new JsonConfigSource(), "units.json", units);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("units.json"));
            Assert.That(exception.Message, Does.Contain("Pirate"));
        }

        [Test]
        public void Load_BossMissingDisplayName_ReportsBossAndField()
        {
            string units = new JsonConfigSource().ReadText("units.json")
                .Replace("\"displayName\": \"吕布\"", "\"displayName\": \"\"");
            var source = new OverrideSource(new JsonConfigSource(), "units.json", units);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("Boss 'boss_lv' displayName"));
        }

        [Test]
        public void Load_DuplicateId_ReportsTheDuplicate()
        {
            string units = new JsonConfigSource().ReadText("units.json")
                .Replace("\"id\": \"dun\"", "\"id\": \"gong\"");
            var source = new OverrideSource(new JsonConfigSource(), "units.json", units);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("Duplicate unit id 'gong'"));
        }

        [Test]
        public void Load_DanglingWaveUnit_ReportsOwnerAndBadId()
        {
            string waves = new JsonConfigSource().ReadText("waves.json")
                .Replace("\"unitId\": \"e_zu\"", "\"unitId\": \"missing_unit\"");
            var source = new OverrideSource(new JsonConfigSource(), "waves.json", waves);

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("unknown unit 'missing_unit'"));
            Assert.That(exception.Message, Does.Contain("Wave set 'main_20'"));
        }

        [Test]
        public void Load_MissingFile_ReportsTheRequestedFile()
        {
            var source = new MissingFileSource("effects.json");

            ConfigLoadException exception = Assert.Throws<ConfigLoadException>(() => GameConfig.Load(source));

            Assert.That(exception.Message, Does.Contain("effects.json"));
        }

        private static string RoundTripOnce(string fileName, string json)
        {
            switch (fileName)
            {
                case "units.json":
                    return JsonCodec.Serialize(JsonCodec.Deserialize<UnitCatalog>(json));
                case "commanders.json":
                    return JsonCodec.Serialize(JsonCodec.Deserialize<CommanderCatalog>(json));
                case "waves.json":
                    return JsonCodec.Serialize(JsonCodec.Deserialize<WaveCatalog>(json));
                case "levels.json":
                    return JsonCodec.Serialize(JsonCodec.Deserialize<LevelCatalog>(json));
                case "effects.json":
                    return JsonCodec.Serialize(JsonCodec.Deserialize<EffectCatalog>(json));
                case "economy.json":
                    return JsonCodec.Serialize(JsonCodec.Deserialize<EconomyDef>(json));
                default:
                    throw new ArgumentOutOfRangeException(nameof(fileName), fileName, "Unknown config file.");
            }
        }

        private static string ReplaceFirst(string source, string original, string replacement)
        {
            int index = source.IndexOf(original, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"Fixture fragment '{original}' was not found.");
            return source.Substring(0, index)
                   + replacement
                   + source.Substring(index + original.Length);
        }

        private sealed class RecordingSource : IConfigSource
        {
            private readonly IConfigSource inner;

            public RecordingSource(IConfigSource inner)
            {
                this.inner = inner;
            }

            public List<string> ReadFiles { get; } = new List<string>();

            public string ReadText(string fileName)
            {
                ReadFiles.Add(fileName);
                return inner.ReadText(fileName);
            }
        }

        private sealed class OverrideSource : IConfigSource
        {
            private readonly IConfigSource inner;
            private readonly string overrideFile;
            private readonly string overrideText;

            public OverrideSource(IConfigSource inner, string overrideFile, string overrideText)
            {
                this.inner = inner;
                this.overrideFile = overrideFile;
                this.overrideText = overrideText;
            }

            public string ReadText(string fileName)
            {
                return fileName == overrideFile ? overrideText : inner.ReadText(fileName);
            }
        }

        private sealed class MissingFileSource : IConfigSource
        {
            private readonly string missingFile;
            private readonly IConfigSource inner = new JsonConfigSource();

            public MissingFileSource(string missingFile)
            {
                this.missingFile = missingFile;
            }

            public string ReadText(string fileName)
            {
                if (fileName == missingFile)
                {
                    throw new FileNotFoundException("Deliberately missing test config.", fileName);
                }

                return inner.ReadText(fileName);
            }
        }
    }
}
