using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Editor;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode
{
    public sealed class DataEditorTests
    {
        private static readonly string[] ExpectedFiles =
        {
            "commanders.json",
            "economy.json",
            "effects.json",
            "levels.json",
            "units.json",
            "waves.json"
        };

        private string tempRoot;
        private DataEditorWorkspace workspace;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(
                Path.GetTempPath(),
                "HanziDefend-DataEditorTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            string projectDataRoot = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "GameData");
            foreach (string fileName in ExpectedFiles)
            {
                File.Copy(
                    Path.Combine(projectDataRoot, fileName),
                    Path.Combine(tempRoot, fileName),
                    false);
            }

            workspace = new DataEditorWorkspace(tempRoot);
            workspace.DiscoverFiles();
        }

        [TearDown]
        public void TearDown()
        {
            workspace = null;
            if (!string.IsNullOrWhiteSpace(tempRoot) && Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void DiscoverFiles_FindsExactlyTheSixEditableDocuments()
        {
            string[] actual = workspace.Documents
                .Select(document => document.FileName)
                .OrderBy(fileName => fileName, StringComparer.Ordinal)
                .ToArray();

            Assert.That(actual, Is.EqualTo(ExpectedFiles));
        }

        [Test]
        public void SetOneUnitCurveValue_SaveChangesOnlyItsSourceLine_AndReloadsValue()
        {
            DataEditorDocument document = workspace.Open("units.json");
            string original = File.ReadAllText(document.FilePath);

            document.SetUnitCurveValue("gong", "atk", "base", 23d);

            Assert.That(document.IsDirty, Is.True);
            workspace.SaveCurrent();
            string saved = File.ReadAllText(document.FilePath);
            AssertSingleChangedLine(original, saved, "\"atk\": { \"base\": 23.0, \"growth\": 0.35 },");
            Assert.That(document.IsDirty, Is.False);

            workspace.ReloadCurrent();
            GameConfig reloaded = GameConfig.Load(new JsonConfigSource(tempRoot));
            Assert.That(reloaded.GetUnit("gong").Atk.Base, Is.EqualTo(23f));
            Assert.That(document.CurrentText, Is.EqualTo(saved));
        }

        [Test]
        public void SaveWithoutChanges_IsByteIdentical()
        {
            DataEditorDocument document = workspace.Open("units.json");
            byte[] before = File.ReadAllBytes(document.FilePath);

            workspace.SaveCurrent();

            byte[] after = File.ReadAllBytes(document.FilePath);
            Assert.That(after, Is.EqualTo(before));
            Assert.That(document.IsDirty, Is.False);
        }

        [Test]
        public void ReloadCurrent_DiscardsDirtyTextAndRestoresDiskVersion()
        {
            DataEditorDocument document = workspace.Open("units.json");
            string original = document.OriginalText;
            document.ReplaceText(original.Replace("\"弓箭手\"", "\"临时未保存名称\""));

            Assert.That(document.IsDirty, Is.True);
            Assert.That(document.CurrentText, Does.Contain("临时未保存名称"));

            workspace.ReloadCurrent();

            Assert.That(document.IsDirty, Is.False);
            Assert.That(document.CurrentText, Is.EqualTo(original));
            Assert.That(File.ReadAllText(document.FilePath), Is.EqualTo(original));
        }

        [Test]
        public void MalformedJson_ReportsFileAndRootLocation()
        {
            ReplaceDocument("units.json", "{ \"units\": [");

            AssertHasError(workspace.ValidateAll(), "units.json", "$", "JSON");
        }

        [Test]
        public void MissingRequiredField_ReportsExactDocumentPath()
        {
            ReplaceFirst(
                "units.json",
                "      \"displayName\": \"弓箭手\",\r\n",
                string.Empty,
                "      \"displayName\": \"弓箭手\",\n");

            AssertHasError(workspace.ValidateAll(), "units.json", "units[0].displayName", "required");
        }

        [TestCase("faction", "Ally", "Pirate")]
        [TestCase("tier", "Green", "Mythic")]
        [TestCase("spawnMode", "Delayed", "Teleport")]
        [TestCase("targeting", "Nearest", "Random")]
        public void InvalidUnitEnum_ReportsExactField(string field, string validValue, string invalidValue)
        {
            ReplaceFirst(
                "units.json",
                $"\"{field}\": \"{validValue}\"",
                $"\"{field}\": \"{invalidValue}\"");

            AssertHasError(workspace.ValidateAll(), "units.json", $"units[0].{field}", invalidValue);
        }

        [TestCase("op", "AddStat", "Teleport")]
        [TestCase("target", "AllyAll", "Everybody")]
        public void InvalidEffectEnum_ReportsExactField(string field, string validValue, string invalidValue)
        {
            ReplaceFirst(
                "effects.json",
                $"\"{field}\": \"{validValue}\"",
                $"\"{field}\": \"{invalidValue}\"");

            AssertHasError(workspace.ValidateAll(), "effects.json", $"effects[0].ops[0].{field}", invalidValue);
        }

        [TestCase("trigger", "Manual", "EveryTick", "effects[0].trigger")]
        [TestCase("stacking", "Stack", "ReplaceAll", "effects[0].stacking")]
        public void InvalidEffectLifecycleEnum_ReportsExactField(
            string field,
            string validValue,
            string invalidValue,
            string expectedPath)
        {
            ReplaceFirst(
                "effects.json",
                $"\"{field}\": \"{validValue}\"",
                $"\"{field}\": \"{invalidValue}\"");

            AssertHasError(workspace.ValidateAll(), "effects.json", expectedPath, invalidValue);
        }

        [Test]
        public void MissingEffectTrigger_ReportsExactField()
        {
            ReplaceFirst(
                "effects.json",
                "      \"trigger\": \"Manual\",\r\n",
                string.Empty,
                "      \"trigger\": \"Manual\",\n");

            AssertHasError(workspace.ValidateAll(), "effects.json", "effects[0].trigger", "required");
        }

        [Test]
        public void EffectDurationBelowPermanentSentinel_ReportsExactField()
        {
            ReplaceFirst("effects.json", "\"duration\": -1.0", "\"duration\": -2.0");

            AssertHasError(
                workspace.ValidateAll(),
                "effects.json",
                "effects[0].ops[0].duration",
                "-1 or non-negative");
        }

        [Test]
        public void CommanderPassiveWithManualTrigger_ReportsExactEffectField()
        {
            ReplaceFirst(
                "effects.json",
                "\"trigger\": \"BattleStart\"",
                "\"trigger\": \"Manual\"");

            AssertHasError(
                workspace.ValidateAll(),
                "effects.json",
                "effects[2].trigger",
                "BattleStart");
        }

        [TestCase("\"gridW\": 1", "\"gridW\": 0", "units[0].gridW")]
        [TestCase("\"hp\": { \"base\": 90.0", "\"hp\": { \"base\": -1.0", "units[0].hp.base")]
        public void OutOfRangeUnitNumber_ReportsExactField(
            string originalFragment,
            string invalidFragment,
            string expectedPath)
        {
            ReplaceFirst("units.json", originalFragment, invalidFragment);

            AssertHasError(workspace.ValidateAll(), "units.json", expectedPath, "positive");
        }

        [Test]
        public void DanglingWaveUnitId_ReportsExactSpawnLocation()
        {
            ReplaceFirst("waves.json", "\"unitId\": \"e_zu\"", "\"unitId\": \"missing_unit\"");

            AssertHasError(
                workspace.ValidateAll(),
                "waves.json",
                "waveSets[0].waves[0].spawns[0].unitId",
                "missing_unit");
        }

        [Test]
        public void MissingWaveRewardRank_ReportsExactWaveField()
        {
            ReplaceFirst(
                "waves.json",
                "          \"rewardRank\": \"Normal\",\r\n",
                string.Empty,
                "          \"rewardRank\": \"Normal\",\n");

            AssertHasError(
                workspace.ValidateAll(),
                "waves.json",
                "waveSets[0].waves[0].rewardRank",
                "required");
        }

        [Test]
        public void InvalidWaveRewardRank_ReportsExactWaveField()
        {
            ReplaceFirst(
                "waves.json",
                "\"rewardRank\": \"Normal\"",
                "\"rewardRank\": \"Legendary\"");

            AssertHasError(
                workspace.ValidateAll(),
                "waves.json",
                "waveSets[0].waves[0].rewardRank",
                "Normal, Elite, Boss");
        }

        [Test]
        public void BossRewardRankWithUnitDef_ReportsExactReferenceField()
        {
            ReplaceFirst(
                "waves.json",
                "\"rewardRank\": \"Normal\"",
                "\"rewardRank\": \"Boss\"");

            AssertHasError(
                workspace.ValidateAll(),
                "waves.json",
                "waveSets[0].waves[0].spawns[0].unitId",
                "BossDef");
        }

        [TestCase(0)]
        [TestCase(5)]
        public void InvalidWaveSpawnLevel_ReportsExactSpawnField(int invalidLevel)
        {
            ReplaceFirst(
                "waves.json",
                "\"level\": 1",
                $"\"level\": {invalidLevel}");

            AssertHasError(
                workspace.ValidateAll(),
                "waves.json",
                "waveSets[0].waves[0].spawns[0].level",
                "[1,4]");
        }

        [Test]
        public void MissingBattleLayout_ReportsExactEconomyField()
        {
            ReplaceFirst(
                "economy.json",
                "    \"allyBasePosition\": {\r\n      \"x\": 0.0,\r\n      \"y\": -8.0\r\n    },\r\n",
                string.Empty,
                "    \"allyBasePosition\": {\n      \"x\": 0.0,\n      \"y\": -8.0\n    },\n");

            AssertHasError(
                workspace.ValidateAll(),
                "economy.json",
                "battle.allyBasePosition",
                "required");
        }

        [Test]
        public void NonFiniteBattleLayout_ReportsExactCoordinate()
        {
            ReplaceFirst("economy.json", "\"x\": 0.0", "\"x\": NaN");

            AssertHasError(
                workspace.ValidateAll(),
                "economy.json",
                "battle.allyBasePosition.x",
                "finite");
        }

        [Test]
        public void CoincidentBaseLayout_ReportsEnemyBasePosition()
        {
            ReplaceFirst("economy.json", "\"y\": 8.0", "\"y\": -8.0");

            AssertHasError(
                workspace.ValidateAll(),
                "economy.json",
                "battle.enemyBasePosition",
                "differ");
        }

        [Test]
        public void DanglingCommanderEffectId_ReportsExactReferenceLocation()
        {
            ReplaceFirst(
                "commanders.json",
                "\"passiveEffectId\": \"cmd_bei_passive\"",
                "\"passiveEffectId\": \"missing_effect\"");

            AssertHasError(
                workspace.ValidateAll(),
                "commanders.json",
                "commanders[0].passiveEffectId",
                "missing_effect");
        }

        [Test]
        public void MissingBossDisplayName_ReportsExactBossField()
        {
            ReplaceFirst(
                "units.json",
                "      \"displayName\": \"吕布\",",
                string.Empty);

            AssertHasError(workspace.ValidateAll(), "units.json", "bosses[0].displayName", "required");
        }

        [Test]
        public void DanglingSpawnEffectUnitId_ReportsExactOpLocation()
        {
            ReplaceFirst("effects.json", "\"unitId\": \"mao\"", "\"unitId\": \"missing_unit\"");

            AssertHasError(
                workspace.ValidateAll(),
                "effects.json",
                "effects[6].ops[0].unitId",
                "missing_unit");
        }

        [Test]
        public void RealGameData_PassesFullWorkspaceValidation()
        {
            var realWorkspace = new DataEditorWorkspace(
                Path.Combine(Directory.GetCurrentDirectory(), "Assets", "GameData"));
            realWorkspace.DiscoverFiles();

            IReadOnlyList<DataEditorValidationError> errors = realWorkspace.ValidateAll();

            Assert.That(
                errors,
                Is.Empty,
                string.Join(Environment.NewLine, errors.Select(FormatError)));
        }

        private void ReplaceDocument(string fileName, string replacement)
        {
            DataEditorDocument document = workspace.Open(fileName);
            document.ReplaceText(replacement);
            Assert.That(document.IsDirty, Is.True);
        }

        private void ReplaceFirst(
            string fileName,
            string originalFragment,
            string replacement,
            string alternateOriginalFragment = null)
        {
            DataEditorDocument document = workspace.Open(fileName);
            string source = document.CurrentText;
            string actualFragment = source.Contains(originalFragment, StringComparison.Ordinal)
                ? originalFragment
                : alternateOriginalFragment;

            Assert.That(actualFragment, Is.Not.Null, $"Fixture fragment was not found in {fileName}.");
            int index = source.IndexOf(actualFragment, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"Fixture fragment was not found in {fileName}.");

            document.ReplaceText(
                source.Substring(0, index)
                + replacement
                + source.Substring(index + actualFragment.Length));
            Assert.That(document.IsDirty, Is.True);
        }

        private static void AssertSingleChangedLine(string before, string after, string expectedChangedLine)
        {
            string[] beforeLines = SplitLines(before);
            string[] afterLines = SplitLines(after);
            Assert.That(afterLines.Length, Is.EqualTo(beforeLines.Length), "Saving changed the line count.");

            int[] changedIndexes = Enumerable.Range(0, beforeLines.Length)
                .Where(index => !string.Equals(beforeLines[index], afterLines[index], StringComparison.Ordinal))
                .ToArray();

            Assert.That(changedIndexes, Has.Length.EqualTo(1), "Saving should alter exactly one source line.");
            Assert.That(afterLines[changedIndexes[0]].Trim(), Is.EqualTo(expectedChangedLine));
        }

        private static string[] SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Split('\n');
        }

        private static void AssertHasError(
            IReadOnlyList<DataEditorValidationError> errors,
            string fileName,
            string path,
            string messageFragment)
        {
            DataEditorValidationError match = errors.FirstOrDefault(error =>
                string.Equals(error.FileName, fileName, StringComparison.Ordinal)
                && NormalizePath(error.Path).Contains(NormalizePath(path), StringComparison.Ordinal)
                && error.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase));

            Assert.That(
                match,
                Is.Not.Null,
                $"Expected error {fileName}:{path} containing '{messageFragment}'. Actual:{Environment.NewLine}"
                + string.Join(Environment.NewLine, errors.Select(FormatError)));
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).TrimStart('$', '.');
        }

        private static string FormatError(DataEditorValidationError error)
        {
            return $"{error.FileName}:{error.Path}: {error.Message}";
        }
    }
}
