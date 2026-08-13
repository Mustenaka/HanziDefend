using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HanziDefend.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HanziDefend.Editor
{
    public sealed class DataEditorValidationError
    {
        public DataEditorValidationError(string fileName, string path, string message)
        {
            FileName = fileName ?? string.Empty;
            Path = string.IsNullOrWhiteSpace(path) ? "$" : path;
            Message = message ?? string.Empty;
        }

        public string FileName { get; }

        public string Path { get; }

        public string Message { get; }

        public override string ToString()
        {
            return $"{FileName}:{Path}: {Message}";
        }
    }

    public sealed class DataEditorWorkspace
    {
        private static readonly string[] RequiredFiles =
        {
            "units.json", "commanders.json", "waves.json",
            "levels.json", "effects.json", "economy.json"
        };

        private readonly string rootDirectory;
        private readonly List<DataEditorDocument> documents = new List<DataEditorDocument>();

        public DataEditorWorkspace(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("GameData directory cannot be empty.", nameof(rootDirectory));
            }

            this.rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public IReadOnlyList<DataEditorDocument> Documents => documents;

        public DataEditorDocument CurrentDocument { get; private set; }

        public void DiscoverFiles()
        {
            documents.Clear();
            foreach (string filePath in Directory.GetFiles(rootDirectory, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                documents.Add(new DataEditorDocument(filePath));
            }

            if (CurrentDocument != null)
            {
                CurrentDocument = documents.FirstOrDefault(value =>
                    string.Equals(value.FileName, CurrentDocument.FileName, StringComparison.Ordinal));
            }
        }

        public DataEditorDocument Open(string fileName)
        {
            DataEditorDocument document = documents.FirstOrDefault(value =>
                string.Equals(value.FileName, fileName, StringComparison.Ordinal));
            if (document == null)
            {
                throw new FileNotFoundException($"Data editor cannot find '{fileName}'.", fileName);
            }

            CurrentDocument = document;
            return document;
        }

        public void SaveCurrent()
        {
            if (CurrentDocument == null)
            {
                throw new InvalidOperationException("No JSON document is selected.");
            }

            CurrentDocument.Save();
        }

        public void ReloadCurrent()
        {
            if (CurrentDocument == null)
            {
                throw new InvalidOperationException("No JSON document is selected.");
            }

            CurrentDocument.Reload();
        }

        public IReadOnlyList<DataEditorValidationError> ValidateAll()
        {
            var errors = new List<DataEditorValidationError>();
            var roots = new Dictionary<string, JObject>(StringComparer.Ordinal);
            var documentsByName = documents.ToDictionary(value => value.FileName, StringComparer.Ordinal);

            foreach (string fileName in RequiredFiles)
            {
                if (!documentsByName.TryGetValue(fileName, out DataEditorDocument document))
                {
                    errors.Add(new DataEditorValidationError(fileName, "$", "Required file is missing."));
                    continue;
                }

                try
                {
                    roots[fileName] = JObject.Parse(document.CurrentText);
                }
                catch (JsonException exception)
                {
                    errors.Add(new DataEditorValidationError(fileName, "$", $"Invalid JSON: {exception.Message}"));
                }
            }

            if (errors.Count > 0)
            {
                return errors;
            }

            ValidateRequiredSchema(roots, errors);
            if (errors.Count > 0)
            {
                return errors;
            }

            ValidateNumericBounds(roots, errors);
            if (errors.Count > 0)
            {
                return errors;
            }

            try
            {
                GameConfig.Load(new MemoryConfigSource(documentsByName));
            }
            catch (Exception exception)
            {
                AddMappedSemanticError(roots, exception.Message, errors);
            }

            return errors;
        }

        private static void ValidateRequiredSchema(
            IReadOnlyDictionary<string, JObject> roots,
            ICollection<DataEditorValidationError> errors)
        {
            JObject unitsRoot = roots["units.json"];
            JArray units = RequireArray(unitsRoot, "units.json", "units", errors);
            JArray bosses = RequireArray(unitsRoot, "units.json", "bosses", errors);
            RequireArray(unitsRoot, "units.json", "commanderIds", errors);
            if (units != null)
            {
                string[] scalarFields =
                {
                    "id", "name", "displayName", "faction", "tier", "gridW", "gridH",
                    "layer", "spawnMode", "targeting", "effects"
                };
                string[] curveFields =
                {
                    "hp", "atk", "range", "atkSpeed", "cooldown", "armor", "pierce", "moveSpeed"
                };

                for (int index = 0; index < units.Count; index++)
                {
                    JObject unit = RequireObject(units[index], "units.json", $"units[{index}]", errors);
                    if (unit == null)
                    {
                        continue;
                    }

                    foreach (string field in scalarFields)
                    {
                        RequireToken(unit, "units.json", $"units[{index}]", field, errors);
                    }

                    foreach (string field in curveFields)
                    {
                        JObject curve = RequireObject(unit[field], "units.json", $"units[{index}].{field}", errors);
                        if (curve != null)
                        {
                            RequireToken(curve, "units.json", $"units[{index}].{field}", "base", errors);
                            RequireToken(curve, "units.json", $"units[{index}].{field}", "growth", errors);
                        }
                    }
                }
            }

            if (bosses != null)
            {
                string[] bossFields =
                {
                    "id", "name", "displayName", "hp", "armor", "atk", "range", "atkSpeed", "pierce", "effects"
                };
                for (int index = 0; index < bosses.Count; index++)
                {
                    JObject boss = RequireObject(bosses[index], "units.json", $"bosses[{index}]", errors);
                    if (boss == null)
                    {
                        continue;
                    }

                    foreach (string field in bossFields)
                    {
                        RequireToken(boss, "units.json", $"bosses[{index}]", field, errors);
                    }
                }
            }

            ValidateSimpleArrayRoot(roots["commanders.json"], "commanders.json", "commanders",
                new[] { "id", "name", "passiveEffectId", "activeEffectId", "activeCooldown" }, errors);
            ValidateSimpleArrayRoot(roots["levels.json"], "levels.json", "levels",
                new[] { "id", "stageIndex", "waveSetId", "baseHp", "startCoins", "gridCols", "gridRows", "gridMaxCols", "gridMaxRows" }, errors);
            RequireToken(roots["levels.json"], "levels.json", "$", "bases", errors);

            JArray waveSets = RequireArray(roots["waves.json"], "waves.json", "waveSets", errors);
            if (waveSets != null)
            {
                for (int setIndex = 0; setIndex < waveSets.Count; setIndex++)
                {
                    JObject set = RequireObject(waveSets[setIndex], "waves.json", $"waveSets[{setIndex}]", errors);
                    if (set == null) continue;
                    RequireToken(set, "waves.json", $"waveSets[{setIndex}]", "id", errors);
                    JArray waves = RequireArray(set, "waves.json", $"waveSets[{setIndex}].waves", errors, true);
                    if (waves == null) continue;
                    for (int waveIndex = 0; waveIndex < waves.Count; waveIndex++)
                    {
                        JObject wave = RequireObject(waves[waveIndex], "waves.json", $"waveSets[{setIndex}].waves[{waveIndex}]", errors);
                        if (wave == null) continue;
                        foreach (string field in new[] { "index", "rewardRank", "delaySec", "spawns" })
                        {
                            RequireToken(wave, "waves.json", $"waveSets[{setIndex}].waves[{waveIndex}]", field, errors);
                        }

                        if (!(wave["spawns"] is JArray spawns)) continue;
                        for (int spawnIndex = 0; spawnIndex < spawns.Count; spawnIndex++)
                        {
                            JObject spawn = RequireObject(
                                spawns[spawnIndex],
                                "waves.json",
                                $"waveSets[{setIndex}].waves[{waveIndex}].spawns[{spawnIndex}]",
                                errors);
                            if (spawn == null) continue;
                            foreach (string field in new[] { "unitId", "level", "count", "spreadX", "intervalSec" })
                            {
                                RequireToken(
                                    spawn,
                                    "waves.json",
                                    $"waveSets[{setIndex}].waves[{waveIndex}].spawns[{spawnIndex}]",
                                    field,
                                    errors);
                            }
                        }
                    }
                }
            }

            JArray effects = RequireArray(roots["effects.json"], "effects.json", "effects", errors);
            if (effects != null)
            {
                for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
                {
                    JObject effect = RequireObject(effects[effectIndex], "effects.json", $"effects[{effectIndex}]", errors);
                    if (effect == null) continue;
                    foreach (string field in new[] { "id", "name", "desc", "rarity", "trigger", "stacking", "ops" })
                    {
                        RequireToken(effect, "effects.json", $"effects[{effectIndex}]", field, errors);
                    }
                    if (!(effect["ops"] is JArray ops)) continue;
                    for (int opIndex = 0; opIndex < ops.Count; opIndex++)
                    {
                        JObject op = RequireObject(ops[opIndex], "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", errors);
                        if (op == null) continue;
                        RequireToken(op, "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", "op", errors);
                        RequireToken(op, "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", "target", errors);
                        RequireToken(op, "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", "duration", errors);

                        string opCode = (string)op["op"];
                        if (opCode == "AddStat")
                        {
                            RequireToken(op, "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", "stat", errors);
                            RequireToken(op, "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", "mode", errors);
                            RequireToken(op, "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", "value", errors);
                        }
                        else if (opCode == "SpawnUnit")
                        {
                            RequireToken(op, "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", "unitId", errors);
                            RequireToken(op, "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", "count", errors);
                        }
                        else
                        {
                            RequireToken(op, "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", "value", errors);
                        }

                        if (string.Equals((string)op["target"], "EnemyInRadius", StringComparison.Ordinal))
                        {
                            RequireToken(op, "effects.json", $"effects[{effectIndex}].ops[{opIndex}]", "radius", errors);
                        }
                    }
                }
            }

            foreach (string field in new[] { "refreshBaseCost", "refreshCostGrowth", "dropCoins", "damage", "cardWeights", "battle" })
            {
                RequireToken(roots["economy.json"], "economy.json", "$", field, errors);
            }

            JObject bases = RequireObject(roots["levels.json"]["bases"], "levels.json", "bases", errors);
            if (bases != null)
            {
                foreach (string side in new[] { "ally", "enemy" })
                {
                    JObject baseDef = RequireObject(bases[side], "levels.json", $"bases.{side}", errors);
                    if (baseDef == null) continue;
                    RequireToken(baseDef, "levels.json", $"bases.{side}", "hp", errors);
                    RequireToken(baseDef, "levels.json", $"bases.{side}", "armor", errors);
                }
            }

            JObject economy = roots["economy.json"];
            RequireObjectFields(economy["dropCoins"] as JObject, "economy.json", "dropCoins",
                new[] { "normal", "elite", "boss" }, errors);
            RequireObjectFields(economy["damage"] as JObject, "economy.json", "damage",
                new[] { "armorScale", "minimumDamage" }, errors);
            RequireObjectFields(economy["cardWeights"] as JObject, "economy.json", "cardWeights",
                new[] { "unit", "expand", "buff", "global" }, errors);
            RequireObjectFields(economy["battle"] as JObject, "economy.json", "battle",
                new[]
                {
                    "tickRateHz", "retargetInterval", "targetSearchRadius", "colliderRadius",
                    "sameColumnTolerance", "separationDistance", "allyBasePosition",
                    "enemyBasePosition", "enemySpawnCenter"
                },
                errors);
            JObject battle = economy["battle"] as JObject;
            if (battle != null)
            {
                foreach (string field in new[] { "allyBasePosition", "enemyBasePosition", "enemySpawnCenter" })
                {
                    RequireObjectFields(
                        battle[field] as JObject,
                        "economy.json",
                        "battle." + field,
                        new[] { "x", "y" },
                        errors);
                }
            }
        }

        private static void RequireObjectFields(
            JObject value,
            string fileName,
            string path,
            IEnumerable<string> fields,
            ICollection<DataEditorValidationError> errors)
        {
            if (value == null)
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Required object is missing."));
                return;
            }

            foreach (string field in fields)
            {
                RequireToken(value, fileName, path, field, errors);
            }
        }

        private static void ValidateNumericBounds(
            IReadOnlyDictionary<string, JObject> roots,
            ICollection<DataEditorValidationError> errors)
        {
            JArray units = (JArray)roots["units.json"]["units"];
            var unitIds = new HashSet<string>(
                units.Select(value => value["id"].Value<string>()),
                StringComparer.Ordinal);
            string[] curves = { "hp", "atk", "range", "atkSpeed", "cooldown", "armor", "pierce", "moveSpeed" };
            for (int index = 0; index < units.Count; index++)
            {
                JObject unit = (JObject)units[index];
                RequirePositiveNumber(unit["gridW"], "units.json", $"units[{index}].gridW", errors);
                RequirePositiveNumber(unit["gridH"], "units.json", $"units[{index}].gridH", errors);
                foreach (string curveName in curves)
                {
                    JObject curve = (JObject)unit[curveName];
                    double baseValue = curve["base"].Value<double>();
                    double growth = curve["growth"].Value<double>();
                    if ((curveName == "hp" && baseValue <= 0d) || (curveName != "hp" && baseValue < 0d))
                    {
                        errors.Add(new DataEditorValidationError(
                            "units.json",
                            $"units[{index}].{curveName}.base",
                            curveName == "hp" ? "Value must be positive." : "Value must be non-negative."));
                    }

                    if (baseValue * (1d + growth * 3d) < 0d)
                    {
                        errors.Add(new DataEditorValidationError(
                            "units.json",
                            $"units[{index}].{curveName}.growth",
                            "Level 4 value cannot be negative."));
                    }
                }
            }

            JArray bosses = (JArray)roots["units.json"]["bosses"];
            var bossIds = new HashSet<string>(
                bosses.Select(value => value["id"].Value<string>()),
                StringComparer.Ordinal);
            for (int index = 0; index < bosses.Count; index++)
            {
                JObject boss = (JObject)bosses[index];
                RequirePositiveNumber(boss["hp"], "units.json", $"bosses[{index}].hp", errors);
                foreach (string field in new[] { "armor", "atk", "range", "atkSpeed", "pierce" })
                {
                    RequireNonNegativeNumber(boss[field], "units.json", $"bosses[{index}].{field}", errors);
                }
            }

            JArray commanders = (JArray)roots["commanders.json"]["commanders"];
            for (int index = 0; index < commanders.Count; index++)
            {
                RequireNonNegativeNumber(
                    commanders[index]["activeCooldown"],
                    "commanders.json",
                    $"commanders[{index}].activeCooldown",
                    errors);
            }

            JArray effects = (JArray)roots["effects.json"]["effects"];
            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                JObject effect = (JObject)effects[effectIndex];
                string effectPath = $"effects[{effectIndex}]";
                RequireEnumString(
                    effect["trigger"],
                    "effects.json",
                    effectPath + ".trigger",
                    errors,
                    "Manual", "BattleStart", "UnitSpawn", "SuicideContact");
                RequireEnumString(
                    effect["stacking"],
                    "effects.json",
                    effectPath + ".stacking",
                    errors,
                    "Stack", "Refresh");

                JArray ops = (JArray)effect["ops"];
                for (int opIndex = 0; opIndex < ops.Count; opIndex++)
                {
                    JToken duration = ops[opIndex]["duration"];
                    string durationPath = effectPath + $".ops[{opIndex}].duration";
                    RequireFiniteNumber(duration, "effects.json", durationPath, errors);
                    if (IsNumber(duration) && duration.Value<double>() < -1d)
                    {
                        errors.Add(new DataEditorValidationError(
                            "effects.json",
                            durationPath,
                            "Duration must be -1 or non-negative."));
                    }
                }
            }

            JArray waveSets = (JArray)roots["waves.json"]["waveSets"];
            for (int setIndex = 0; setIndex < waveSets.Count; setIndex++)
            {
                JArray waves = (JArray)waveSets[setIndex]["waves"];
                for (int waveIndex = 0; waveIndex < waves.Count; waveIndex++)
                {
                    JObject wave = (JObject)waves[waveIndex];
                    string wavePath = $"waveSets[{setIndex}].waves[{waveIndex}]";
                    RequirePositiveNumber(wave["index"], "waves.json", wavePath + ".index", errors);
                    RequireEnumString(
                        wave["rewardRank"],
                        "waves.json",
                        wavePath + ".rewardRank",
                        errors,
                        "Normal", "Elite", "Boss");
                    RequireNonNegativeNumber(wave["delaySec"], "waves.json", wavePath + ".delaySec", errors);
                    JArray spawns = (JArray)wave["spawns"];
                    for (int spawnIndex = 0; spawnIndex < spawns.Count; spawnIndex++)
                    {
                        JObject spawn = (JObject)spawns[spawnIndex];
                        string spawnPath = wavePath + $".spawns[{spawnIndex}]";
                        RequireIntegerInRange(spawn["level"], "waves.json", spawnPath + ".level", 1, 4, errors);
                        string rewardRank = wave["rewardRank"].Value<string>();
                        string unitId = spawn["unitId"].Value<string>();
                        if (string.Equals(rewardRank, "Boss", StringComparison.Ordinal)
                            && unitIds.Contains(unitId))
                        {
                            errors.Add(new DataEditorValidationError(
                                "waves.json",
                                spawnPath + ".unitId",
                                "Boss rewardRank requires a BossDef reference."));
                        }
                        else if ((string.Equals(rewardRank, "Normal", StringComparison.Ordinal)
                                  || string.Equals(rewardRank, "Elite", StringComparison.Ordinal))
                                 && bossIds.Contains(unitId))
                        {
                            errors.Add(new DataEditorValidationError(
                                "waves.json",
                                spawnPath + ".unitId",
                                rewardRank + " rewardRank requires a UnitDef reference."));
                        }
                        RequirePositiveNumber(spawn["count"], "waves.json", spawnPath + ".count", errors);
                        RequireNonNegativeNumber(spawn["spreadX"], "waves.json", spawnPath + ".spreadX", errors);
                        RequireNonNegativeNumber(spawn["intervalSec"], "waves.json", spawnPath + ".intervalSec", errors);
                    }
                }
            }

            JArray levels = (JArray)roots["levels.json"]["levels"];
            for (int index = 0; index < levels.Count; index++)
            {
                JObject level = (JObject)levels[index];
                string path = $"levels[{index}]";
                RequirePositiveNumber(level["stageIndex"], "levels.json", path + ".stageIndex", errors);
                RequirePositiveNumber(level["baseHp"], "levels.json", path + ".baseHp", errors);
                RequireNonNegativeNumber(level["startCoins"], "levels.json", path + ".startCoins", errors);
                RequirePositiveNumber(level["gridCols"], "levels.json", path + ".gridCols", errors);
                RequirePositiveNumber(level["gridRows"], "levels.json", path + ".gridRows", errors);
                RequirePositiveNumber(level["gridMaxCols"], "levels.json", path + ".gridMaxCols", errors);
                RequirePositiveNumber(level["gridMaxRows"], "levels.json", path + ".gridMaxRows", errors);
                if (level["gridMaxCols"].Value<int>() < level["gridCols"].Value<int>())
                {
                    errors.Add(new DataEditorValidationError("levels.json", path + ".gridMaxCols", "Maximum columns cannot be smaller than starting columns."));
                }
                if (level["gridMaxRows"].Value<int>() < level["gridRows"].Value<int>())
                {
                    errors.Add(new DataEditorValidationError("levels.json", path + ".gridMaxRows", "Maximum rows cannot be smaller than starting rows."));
                }
            }

            JObject bases = (JObject)roots["levels.json"]["bases"];
            foreach (string side in new[] { "ally", "enemy" })
            {
                RequirePositiveNumber(bases[side]["hp"], "levels.json", $"bases.{side}.hp", errors);
                RequireNonNegativeNumber(bases[side]["armor"], "levels.json", $"bases.{side}.armor", errors);
            }

            JObject economy = roots["economy.json"];
            RequireNonNegativeNumber(economy["refreshBaseCost"], "economy.json", "refreshBaseCost", errors);
            RequireNonNegativeNumber(economy["refreshCostGrowth"], "economy.json", "refreshCostGrowth", errors);
            foreach (string field in new[] { "normal", "elite", "boss" })
            {
                RequirePositiveNumber(economy["dropCoins"][field], "economy.json", "dropCoins." + field, errors);
            }
            RequirePositiveNumber(economy["damage"]["armorScale"], "economy.json", "damage.armorScale", errors);
            RequirePositiveNumber(economy["damage"]["minimumDamage"], "economy.json", "damage.minimumDamage", errors);
            foreach (string field in new[] { "unit", "expand", "buff", "global" })
            {
                RequireNonNegativeNumber(economy["cardWeights"][field], "economy.json", "cardWeights." + field, errors);
            }

            JObject battle = (JObject)economy["battle"];
            foreach (string field in new[]
                     {
                         "tickRateHz", "retargetInterval", "targetSearchRadius", "colliderRadius",
                         "separationDistance"
                     })
            {
                RequirePositiveNumber(battle[field], "economy.json", "battle." + field, errors);
            }
            RequireNonNegativeNumber(
                battle["sameColumnTolerance"],
                "economy.json",
                "battle.sameColumnTolerance",
                errors);
            foreach (string positionName in new[] { "allyBasePosition", "enemyBasePosition", "enemySpawnCenter" })
            {
                JObject position = (JObject)battle[positionName];
                RequireFiniteNumber(position["x"], "economy.json", $"battle.{positionName}.x", errors);
                RequireFiniteNumber(position["y"], "economy.json", $"battle.{positionName}.y", errors);
            }

            JObject allyBase = (JObject)battle["allyBasePosition"];
            JObject enemyBase = (JObject)battle["enemyBasePosition"];
            if (IsNumber(allyBase["x"])
                && IsNumber(allyBase["y"])
                && IsNumber(enemyBase["x"])
                && IsNumber(enemyBase["y"])
                && allyBase["x"].Value<double>() == enemyBase["x"].Value<double>()
                && allyBase["y"].Value<double>() == enemyBase["y"].Value<double>())
            {
                errors.Add(new DataEditorValidationError(
                    "economy.json",
                    "battle.enemyBasePosition",
                    "Enemy base position must differ from ally base position."));
            }
        }

        private static bool IsNumber(JToken value)
        {
            return value.Type == JTokenType.Integer || value.Type == JTokenType.Float;
        }

        private static void RequireEnumString(
            JToken value,
            string fileName,
            string path,
            ICollection<DataEditorValidationError> errors,
            params string[] allowedValues)
        {
            if (value.Type != JTokenType.String
                || !allowedValues.Contains(value.Value<string>(), StringComparer.Ordinal))
            {
                string actual = value.Type == JTokenType.String
                    ? value.Value<string>()
                    : value.Type.ToString();
                errors.Add(new DataEditorValidationError(
                    fileName,
                    path,
                    $"Value '{actual}' must be one of: {string.Join(", ", allowedValues)}."));
            }
        }

        private static void RequireIntegerInRange(
            JToken value,
            string fileName,
            string path,
            int minimum,
            int maximum,
            ICollection<DataEditorValidationError> errors)
        {
            if (value.Type != JTokenType.Integer)
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Value must be an integer."));
                return;
            }

            long number = value.Value<long>();
            if (number < minimum || number > maximum)
            {
                errors.Add(new DataEditorValidationError(
                    fileName,
                    path,
                    $"Value must be in [{minimum},{maximum}]."));
            }
        }

        private static void RequireFiniteNumber(
            JToken value,
            string fileName,
            string path,
            ICollection<DataEditorValidationError> errors)
        {
            if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Value must be a number."));
                return;
            }

            double number = value.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Value must be finite."));
            }
        }

        private static void RequirePositiveNumber(
            JToken value,
            string fileName,
            string path,
            ICollection<DataEditorValidationError> errors)
        {
            if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Value must be a number."));
            }
            else
            {
                double number = value.Value<double>();
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    errors.Add(new DataEditorValidationError(fileName, path, "Value must be finite."));
                }
                else if (number <= 0d)
                {
                    errors.Add(new DataEditorValidationError(fileName, path, "Value must be positive."));
                }
            }
        }

        private static void RequireNonNegativeNumber(
            JToken value,
            string fileName,
            string path,
            ICollection<DataEditorValidationError> errors)
        {
            if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Value must be a number."));
            }
            else
            {
                double number = value.Value<double>();
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    errors.Add(new DataEditorValidationError(fileName, path, "Value must be finite."));
                }
                else if (number < 0d)
                {
                    errors.Add(new DataEditorValidationError(fileName, path, "Value must be non-negative."));
                }
            }
        }

        private static void ValidateSimpleArrayRoot(
            JObject root,
            string fileName,
            string arrayName,
            IEnumerable<string> fields,
            ICollection<DataEditorValidationError> errors)
        {
            JArray array = RequireArray(root, fileName, arrayName, errors);
            if (array == null) return;
            for (int index = 0; index < array.Count; index++)
            {
                JObject item = RequireObject(array[index], fileName, $"{arrayName}[{index}]", errors);
                if (item == null) continue;
                foreach (string field in fields)
                {
                    RequireToken(item, fileName, $"{arrayName}[{index}]", field, errors);
                }
            }
        }

        private static JArray RequireArray(
            JObject owner,
            string fileName,
            string field,
            ICollection<DataEditorValidationError> errors,
            bool fieldIsPath = false)
        {
            string path = fieldIsPath ? field : field;
            JToken token = fieldIsPath ? owner.SelectToken(field.Substring(field.LastIndexOf('.') + 1)) : owner[field];
            if (!(token is JArray result))
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Required array is missing."));
                return null;
            }

            return result;
        }

        private static JObject RequireObject(
            JToken token,
            string fileName,
            string path,
            ICollection<DataEditorValidationError> errors)
        {
            if (!(token is JObject value))
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Required object is missing."));
                return null;
            }

            return value;
        }

        private static void RequireToken(
            JObject owner,
            string fileName,
            string ownerPath,
            string field,
            ICollection<DataEditorValidationError> errors)
        {
            if (owner[field] == null || owner[field].Type == JTokenType.Null)
            {
                string prefix = ownerPath == "$" ? string.Empty : ownerPath + ".";
                errors.Add(new DataEditorValidationError(fileName, prefix + field, "Required field is missing."));
            }
        }

        private static void AddMappedSemanticError(
            IReadOnlyDictionary<string, JObject> roots,
            string message,
            ICollection<DataEditorValidationError> errors)
        {
            string fileName = FindMentionedFile(message) ?? "GameData";
            string path = "$";

            if (message.Contains("Pirate", StringComparison.Ordinal))
                path = "units[0].faction";
            else if (message.Contains("Mythic", StringComparison.Ordinal))
                path = "units[0].tier";
            else if (message.Contains("Teleport", StringComparison.Ordinal) && fileName == "units.json")
                path = "units[0].spawnMode";
            else if (message.Contains("Random", StringComparison.Ordinal))
                path = "units[0].targeting";
            else if (message.Contains("Teleport", StringComparison.Ordinal) && fileName == "effects.json")
                path = "effects[0].ops[0].op";
            else if (message.Contains("Everybody", StringComparison.Ordinal))
                path = "effects[0].ops[0].target";
            else if (message.Contains("trigger", StringComparison.OrdinalIgnoreCase)
                     || message.Contains("stacking rule", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "effects.json";
                path = FindMentionedEffectLifecyclePath(roots["effects.json"], message);
            }
            else if (message.Contains("unknown unit", StringComparison.OrdinalIgnoreCase))
            {
                fileName = message.Contains("Wave set", StringComparison.Ordinal) ? "waves.json" : "effects.json";
                path = fileName == "waves.json"
                    ? FindFirstUnknownReferencePath(roots["waves.json"], "unitId")
                    : FindFirstUnknownReferencePath(roots["effects.json"], "unitId");
            }
            else if (message.Contains("unknown effect", StringComparison.OrdinalIgnoreCase))
            {
                fileName = message.Contains("Commander", StringComparison.Ordinal) ? "commanders.json" : "units.json";
                path = FindEffectReferencePath(roots[fileName]);
            }
            else if (message.Contains("hp.base", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "units.json";
                path = "units[0].hp.base";
            }
            else if (message.Contains("grid size", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "units.json";
                path = "units[0].gridW";
            }
            else
            {
                (string inferredFile, string inferredPath) = InferDeserializerPath(message);
                if (inferredFile != null) fileName = inferredFile;
                if (inferredPath != null) path = inferredPath;
            }

            errors.Add(new DataEditorValidationError(fileName, path, message));
        }

        private static string FindMentionedEffectLifecyclePath(JObject root, string message)
        {
            JArray effects = root?["effects"] as JArray;
            if (effects == null)
            {
                return "effects";
            }

            for (int index = 0; index < effects.Count; index++)
            {
                string effectId = effects[index]?["id"]?.Value<string>();
                if (!string.IsNullOrEmpty(effectId)
                    && message.Contains($"'{effectId}'", StringComparison.Ordinal))
                {
                    string field = message.Contains("stacking", StringComparison.OrdinalIgnoreCase)
                        ? "stacking"
                        : "trigger";
                    return $"effects[{index}].{field}";
                }
            }

            return "effects";
        }

        private static (string fileName, string path) InferDeserializerPath(string message)
        {
            int marker = message.IndexOf("Path '", StringComparison.Ordinal);
            if (marker < 0) return (null, null);
            int start = marker + 6;
            int end = message.IndexOf('\'', start);
            if (end < 0) return (null, null);
            string path = message.Substring(start, end - start);
            if (path.StartsWith("units", StringComparison.Ordinal) || path.StartsWith("bosses", StringComparison.Ordinal))
                return ("units.json", path);
            if (path.StartsWith("effects", StringComparison.Ordinal)) return ("effects.json", path);
            if (path.StartsWith("commanders", StringComparison.Ordinal)) return ("commanders.json", path);
            if (path.StartsWith("waveSets", StringComparison.Ordinal)) return ("waves.json", path);
            if (path.StartsWith("levels", StringComparison.Ordinal) || path.StartsWith("bases", StringComparison.Ordinal)) return ("levels.json", path);
            return ("economy.json", path);
        }

        private static string FindMentionedFile(string message)
        {
            foreach (string file in RequiredFiles)
            {
                if (message.Contains(file, StringComparison.Ordinal)) return file;
            }

            (string inferred, _) = InferDeserializerPath(message);
            return inferred;
        }

        private static string FindFirstUnknownReferencePath(JObject root, string propertyName)
        {
            JProperty property = root.Descendants().OfType<JProperty>().FirstOrDefault(value =>
                value.Name == propertyName
                && value.Value.Type == JTokenType.String
                && ((string)value.Value).StartsWith("missing", StringComparison.Ordinal));
            return property?.Path ?? "$";
        }

        private static string FindEffectReferencePath(JObject root)
        {
            JProperty property = root.Descendants().OfType<JProperty>().FirstOrDefault(value =>
                (value.Name == "passiveEffectId" || value.Name == "activeEffectId")
                && value.Value.Type == JTokenType.String
                && ((string)value.Value).StartsWith("missing", StringComparison.Ordinal));
            return property?.Path ?? "$";
        }

        private sealed class MemoryConfigSource : IConfigSource
        {
            private readonly IReadOnlyDictionary<string, DataEditorDocument> documents;

            public MemoryConfigSource(IReadOnlyDictionary<string, DataEditorDocument> documents)
            {
                this.documents = documents;
            }

            public string ReadText(string fileName)
            {
                return documents[fileName].CurrentText;
            }
        }
    }
}
