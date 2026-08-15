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
            foreach (string fileName in RequiredFiles.OrderBy(value => value, StringComparer.Ordinal))
            {
                string filePath = Path.Combine(rootDirectory, fileName);
                if (File.Exists(filePath))
                {
                    documents.Add(new DataEditorDocument(filePath));
                }
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
                    "id", "name", "displayName", "faction", "tier", "gridW", "gridH", "footprint",
                    "layer", "spawnMode", "targeting", "unitType", "armorType", "atkType",
                    "bonusVs", "traits", "effects"
                };
                string[] curveFields =
                {
                    "hp", "atk", "range", "minRange", "atkSpeed", "cooldown", "armor", "pierce", "moveSpeed"
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

                    if (unit["bonusVs"] is JArray bonuses)
                    {
                        for (int bonusIndex = 0; bonusIndex < bonuses.Count; bonusIndex++)
                        {
                            JObject bonus = RequireObject(bonuses[bonusIndex], "units.json",
                                $"units[{index}].bonusVs[{bonusIndex}]", errors);
                            if (bonus == null) continue;
                            RequireToken(bonus, "units.json", $"units[{index}].bonusVs[{bonusIndex}]", "target", errors);
                            RequireToken(bonus, "units.json", $"units[{index}].bonusVs[{bonusIndex}]", "value", errors);
                        }
                    }
                    if (unit["traits"] is JArray traits)
                    {
                        for (int traitIndex = 0; traitIndex < traits.Count; traitIndex++)
                        {
                            JObject trait = RequireObject(traits[traitIndex], "units.json",
                                $"units[{index}].traits[{traitIndex}]", errors);
                            if (trait != null)
                                RequireToken(trait, "units.json", $"units[{index}].traits[{traitIndex}]", "type", errors);
                        }
                    }
                }
            }

            if (bosses != null)
            {
                string[] bossFields =
                {
                    "id", "name", "displayName", "hp", "armor", "unitType", "armorType",
                    "atk", "range", "atkSpeed", "pierce", "atkType", "bonusVs", "effects"
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
                new[] { "id", "name", "displayName", "faction", "passiveEffectId", "activeEffectId", "activeCooldown" }, errors);
            ValidateSimpleArrayRoot(roots["levels.json"], "levels.json", "levels",
                new[] { "id", "stageIndex", "waveSetId", "baseHp", "startCoins", "gridWidth", "gridHeight", "initialUnlock" }, errors);
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

            foreach (string field in new[]
                     {
                         "refreshBaseCost", "refreshCostGrowth", "gridUnlock", "deployUi", "dropCoins", "damage",
                         "cardWeights", "cardOffer", "cardPool", "settlementReward", "battle"
                     })
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
                    foreach (string field in new[] { "id", "name", "displayName", "unitType", "armorType" })
                        RequireToken(baseDef, "levels.json", $"bases.{side}", field, errors);
                }
            }

            JObject economy = roots["economy.json"];
            RequireObjectFields(economy["dropCoins"] as JObject, "economy.json", "dropCoins",
                new[] { "normal", "elite", "boss" }, errors);
            RequireObjectFields(economy["damage"] as JObject, "economy.json", "damage",
                new[] { "armorScale", "minimumDamage", "neutralTypeMultiplier", "typeMultipliers" }, errors);
            RequireObjectFields(economy["gridUnlock"] as JObject, "economy.json", "gridUnlock",
                new[] { "purchaseBaseCost", "purchaseCostGrowth", "baseAnchorRowOffset", "autoUnlockPerMinorStage" }, errors);
            RequireObjectFields(economy["deployUi"] as JObject, "economy.json", "deployUi",
                new[] { "cellSpacingRatio", "handCardScale", "snapRadiusCells", "dragLiftCells" }, errors);
            RequireObjectFields(economy["cardWeights"] as JObject, "economy.json", "cardWeights",
                new[] { "unit", "unlock", "buff", "global" }, errors);
            RequireObjectFields(economy["cardOffer"] as JObject, "economy.json", "cardOffer",
                new[] { "baseCount", "luckyExtraCount", "luckyChance" }, errors);
            RequireObjectFields(economy["cardPool"] as JObject, "economy.json", "cardPool",
                new[]
                {
                    "shapeUnlocks", "guaranteeBeforeStageIndex", "guaranteeAttackType",
                    "buffEffectIds", "globalEffectIds"
                 }, errors);
            RequireObjectFields(economy["settlementReward"] as JObject, "economy.json", "settlementReward",
                new[]
                {
                    "slotCount", "buffWeight", "activeSkillWeight", "buffEffectIds",
                    "activeSkillEffectIds", "uniqueEffectIds"
                }, errors);
            if (economy["damage"]?["typeMultipliers"] is JArray multipliers)
            {
                for (int index = 0; index < multipliers.Count; index++)
                {
                    JObject entry = RequireObject(multipliers[index], "economy.json",
                        $"damage.typeMultipliers[{index}]", errors);
                    if (entry == null) continue;
                    foreach (string field in new[] { "armorType", "atkType", "value" })
                        RequireToken(entry, "economy.json", $"damage.typeMultipliers[{index}]", field, errors);
                }
            }
            if (economy["cardPool"]?["shapeUnlocks"] is JArray unlocks)
            {
                for (int index = 0; index < unlocks.Count; index++)
                {
                    JObject entry = RequireObject(unlocks[index], "economy.json",
                        $"cardPool.shapeUnlocks[{index}]", errors);
                    if (entry == null) continue;
                    foreach (string field in new[] { "gridW", "gridH" })
                        RequireToken(entry, "economy.json", $"cardPool.shapeUnlocks[{index}]", field, errors);
                }
            }
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
            string[] curves = { "hp", "atk", "range", "minRange", "atkSpeed", "cooldown", "armor", "pierce", "moveSpeed" };
            for (int index = 0; index < units.Count; index++)
            {
                JObject unit = (JObject)units[index];
                RequirePositiveNumber(unit["gridW"], "units.json", $"units[{index}].gridW", errors);
                RequirePositiveNumber(unit["gridH"], "units.json", $"units[{index}].gridH", errors);
                RequireEnumString(unit["footprint"], "units.json", $"units[{index}].footprint", errors,
                    "Rectangle", "MissingUpperRight", "MissingLowerLeft");
                string footprint = unit["footprint"]?.Value<string>();
                if ((string.Equals(footprint, "MissingUpperRight", StringComparison.Ordinal)
                     || string.Equals(footprint, "MissingLowerLeft", StringComparison.Ordinal))
                    && (unit["gridW"]?.Value<int>() != 2 || unit["gridH"]?.Value<int>() != 2))
                {
                    errors.Add(new DataEditorValidationError(
                        "units.json",
                        $"units[{index}].footprint",
                        $"{footprint} requires a 2x2 bounding box."));
                }
                RequireEnumString(unit["unitType"], "units.json", $"units[{index}].unitType", errors,
                    "Infantry", "Cavalry", "Naval", "Air", "Building", "Special");
                RequireEnumString(unit["armorType"], "units.json", $"units[{index}].armorType", errors,
                    "Unarmored", "Light", "Heavy", "Building");
                RequireEnumString(unit["atkType"], "units.json", $"units[{index}].atkType", errors,
                    "None", "Slash", "Blunt", "Arrow", "Siege");
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
                if (IsNumber(unit["minRange"]?["base"])
                    && IsNumber(unit["range"]?["base"])
                    && unit["minRange"]["base"].Value<double>() > unit["range"]["base"].Value<double>())
                {
                    errors.Add(new DataEditorValidationError(
                        "units.json", $"units[{index}].minRange.base", "Minimum range cannot exceed range."));
                }

                if (unit["bonusVs"] is JArray bonuses)
                {
                    var bonusTargets = new HashSet<string>(StringComparer.Ordinal);
                    for (int bonusIndex = 0; bonusIndex < bonuses.Count; bonusIndex++)
                    {
                        string path = $"units[{index}].bonusVs[{bonusIndex}]";
                        RequireEnumString(bonuses[bonusIndex]["target"], "units.json", path + ".target", errors,
                            "Cavalry", "HeavyArmor", "Building");
                        RequireNonNegativeNumber(bonuses[bonusIndex]["value"], "units.json", path + ".value", errors);
                        string target = bonuses[bonusIndex]["target"].Value<string>();
                        if (!bonusTargets.Add(target))
                            errors.Add(new DataEditorValidationError("units.json", path + ".target",
                                $"Duplicate bonus target '{target}'."));
                    }
                }
                if (unit["traits"] is JArray traits)
                {
                    var traitTypes = new HashSet<string>(StringComparer.Ordinal);
                    for (int traitIndex = 0; traitIndex < traits.Count; traitIndex++)
                    {
                        string path = $"units[{index}].traits[{traitIndex}]";
                        RequireEnumString(traits[traitIndex]["type"], "units.json", path + ".type", errors,
                            "Charge", "Trample", "PiercingShot", "FireAura", "IceAura", "DeathSpawn");
                        string type = traits[traitIndex]["type"].Value<string>();
                        if (!traitTypes.Add(type))
                            errors.Add(new DataEditorValidationError("units.json", path + ".type",
                                $"Duplicate trait '{type}'."));
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
                RequireEnumString(boss["unitType"], "units.json", $"bosses[{index}].unitType", errors, "Building");
                RequireEnumString(boss["armorType"], "units.json", $"bosses[{index}].armorType", errors, "Building");
                RequireEnumString(boss["atkType"], "units.json", $"bosses[{index}].atkType", errors,
                    "Slash", "Blunt", "Arrow", "Siege");
                RequirePositiveNumber(boss["hp"], "units.json", $"bosses[{index}].hp", errors);
                foreach (string field in new[] { "armor", "atk", "range", "atkSpeed", "pierce" })
                {
                    RequireNonNegativeNumber(boss[field], "units.json", $"bosses[{index}].{field}", errors);
                }
            }

            JArray commanders = (JArray)roots["commanders.json"]["commanders"];
            for (int index = 0; index < commanders.Count; index++)
            {
                RequireEnumString(commanders[index]["faction"], "commanders.json",
                    $"commanders[{index}].faction", errors, "Ally", "Enemy");
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
                RequirePositiveNumber(level["gridWidth"], "levels.json", path + ".gridWidth", errors);
                RequirePositiveNumber(level["gridHeight"], "levels.json", path + ".gridHeight", errors);
                ValidateInitialUnlockRect(level, path, errors);
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
            JObject gridUnlock = (JObject)economy["gridUnlock"];
            RequireNonNegativeNumber(gridUnlock["purchaseBaseCost"], "economy.json", "gridUnlock.purchaseBaseCost", errors);
            RequireNonNegativeNumber(gridUnlock["purchaseCostGrowth"], "economy.json", "gridUnlock.purchaseCostGrowth", errors);
            RequireNonNegativeNumber(gridUnlock["autoUnlockPerMinorStage"], "economy.json", "gridUnlock.autoUnlockPerMinorStage", errors);
            JObject deployUi = (JObject)economy["deployUi"];
            RequireNonNegativeNumber(deployUi["cellSpacingRatio"], "economy.json", "deployUi.cellSpacingRatio", errors);
            RequirePositiveNumber(deployUi["handCardScale"], "economy.json", "deployUi.handCardScale", errors);
            RequirePositiveNumber(deployUi["snapRadiusCells"], "economy.json", "deployUi.snapRadiusCells", errors);
            RequireNonNegativeNumber(deployUi["dragLiftCells"], "economy.json", "deployUi.dragLiftCells", errors);
            foreach (string field in new[] { "normal", "elite", "boss" })
            {
                RequirePositiveNumber(economy["dropCoins"][field], "economy.json", "dropCoins." + field, errors);
            }
            RequirePositiveNumber(economy["damage"]["armorScale"], "economy.json", "damage.armorScale", errors);
            RequirePositiveNumber(economy["damage"]["minimumDamage"], "economy.json", "damage.minimumDamage", errors);
            RequirePositiveNumber(economy["damage"]["neutralTypeMultiplier"], "economy.json", "damage.neutralTypeMultiplier", errors);
            JArray typeMultipliers = (JArray)economy["damage"]["typeMultipliers"];
            var multiplierKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < typeMultipliers.Count; index++)
            {
                JObject entry = (JObject)typeMultipliers[index];
                string path = $"damage.typeMultipliers[{index}]";
                RequireEnumString(entry["armorType"], "economy.json", path + ".armorType", errors,
                    "Unarmored", "Light", "Heavy", "Building");
                RequireEnumString(entry["atkType"], "economy.json", path + ".atkType", errors,
                    "Slash", "Blunt", "Arrow", "Siege");
                RequirePositiveNumber(entry["value"], "economy.json", path + ".value", errors);
                string key = entry["armorType"].Value<string>() + "/" + entry["atkType"].Value<string>();
                if (!multiplierKeys.Add(key))
                    errors.Add(new DataEditorValidationError("economy.json", path,
                        $"Duplicate type multiplier '{key}'."));
            }
            if (multiplierKeys.Count != 16)
                errors.Add(new DataEditorValidationError("economy.json", "damage.typeMultipliers",
                    "All 16 armor/attack combinations are required exactly once."));
            foreach (string field in new[] { "unit", "unlock", "buff", "global" })
            {
                RequireNonNegativeNumber(economy["cardWeights"][field], "economy.json", "cardWeights." + field, errors);
            }
            JObject cardOffer = (JObject)economy["cardOffer"];
            RequirePositiveNumber(cardOffer["baseCount"], "economy.json", "cardOffer.baseCount", errors);
            RequirePositiveNumber(cardOffer["luckyExtraCount"], "economy.json", "cardOffer.luckyExtraCount", errors);
            RequireNumberInRange(
                cardOffer["luckyChance"],
                "economy.json",
                "cardOffer.luckyChance",
                0d,
                1d,
                errors);
            JObject cardPool = (JObject)economy["cardPool"];
            RequirePositiveNumber(cardPool["guaranteeBeforeStageIndex"], "economy.json",
                "cardPool.guaranteeBeforeStageIndex", errors);
            RequireEnumString(cardPool["guaranteeAttackType"], "economy.json",
                "cardPool.guaranteeAttackType", errors, "Slash", "Blunt", "Arrow", "Siege");
            foreach (JObject unlock in (JArray)cardPool["shapeUnlocks"])
            {
                int unlockIndex = ((JArray)cardPool["shapeUnlocks"]).IndexOf(unlock);
                string path = $"cardPool.shapeUnlocks[{unlockIndex}]";
                RequirePositiveNumber(unlock["gridW"], "economy.json", path + ".gridW", errors);
                RequirePositiveNumber(unlock["gridH"], "economy.json", path + ".gridH", errors);
            }
            ValidateNonEmptyUniqueStringArray(
                cardPool["buffEffectIds"],
                "economy.json",
                "cardPool.buffEffectIds",
                errors);
            ValidateNonEmptyUniqueStringArray(
                cardPool["globalEffectIds"],
                "economy.json",
                "cardPool.globalEffectIds",
                errors);

            JObject settlementReward = (JObject)economy["settlementReward"];
            RequirePositiveNumber(settlementReward["slotCount"], "economy.json",
                "settlementReward.slotCount", errors);
            RequireNonNegativeNumber(settlementReward["buffWeight"], "economy.json",
                "settlementReward.buffWeight", errors);
            RequireNonNegativeNumber(settlementReward["activeSkillWeight"], "economy.json",
                "settlementReward.activeSkillWeight", errors);
            ValidateNonEmptyUniqueStringArray(
                settlementReward["buffEffectIds"],
                "economy.json",
                "settlementReward.buffEffectIds",
                errors);
            ValidateNonEmptyUniqueStringArray(
                settlementReward["activeSkillEffectIds"],
                "economy.json",
                "settlementReward.activeSkillEffectIds",
                errors);
            ValidateNonEmptyUniqueStringArray(
                settlementReward["uniqueEffectIds"],
                "economy.json",
                "settlementReward.uniqueEffectIds",
                errors);

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

        private static void RequireNumberInRange(
            JToken value,
            string fileName,
            string path,
            double minimum,
            double maximum,
            ICollection<DataEditorValidationError> errors)
        {
            if (!IsNumber(value))
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Value must be a number."));
                return;
            }

            double number = value.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number) || number < minimum || number > maximum)
            {
                errors.Add(new DataEditorValidationError(
                    fileName,
                    path,
                    $"Value must be finite and in [{minimum},{maximum}]."));
            }
        }

        private static void ValidateNonEmptyUniqueStringArray(
            JToken value,
            string fileName,
            string path,
            ICollection<DataEditorValidationError> errors)
        {
            if (!(value is JArray array))
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Value must be an array."));
                return;
            }
            if (array.Count == 0)
            {
                errors.Add(new DataEditorValidationError(fileName, path, "Array must not be empty."));
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < array.Count; index++)
            {
                string itemPath = $"{path}[{index}]";
                if (array[index].Type != JTokenType.String || string.IsNullOrWhiteSpace(array[index].Value<string>()))
                {
                    errors.Add(new DataEditorValidationError(fileName, itemPath, "Effect id must be non-empty text."));
                    continue;
                }
                string effectId = array[index].Value<string>();
                if (!seen.Add(effectId))
                {
                    errors.Add(new DataEditorValidationError(fileName, itemPath, $"Duplicate effect id '{effectId}'."));
                }
            }
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

        /// <summary>
        /// Checks a level's initial unlock rect: it must unlock at least one cell and fit entirely
        /// inside the fixed playfield, because the field never grows once a run has started.
        /// </summary>
        private static void ValidateInitialUnlockRect(
            JObject level,
            string path,
            ICollection<DataEditorValidationError> errors)
        {
            string rectPath = path + ".initialUnlock";
            JObject rect = RequireObject(level["initialUnlock"], "levels.json", rectPath, errors);
            if (rect == null)
            {
                return;
            }

            foreach (string field in new[] { "col", "row", "width", "height" })
            {
                RequireToken(rect, "levels.json", rectPath, field, errors);
            }

            if (rect["col"] == null || rect["row"] == null || rect["width"] == null || rect["height"] == null)
            {
                return;
            }

            RequireNonNegativeNumber(rect["col"], "levels.json", rectPath + ".col", errors);
            RequireNonNegativeNumber(rect["row"], "levels.json", rectPath + ".row", errors);
            RequirePositiveNumber(rect["width"], "levels.json", rectPath + ".width", errors);
            RequirePositiveNumber(rect["height"], "levels.json", rectPath + ".height", errors);

            if (level["gridWidth"].Type != JTokenType.Integer || level["gridHeight"].Type != JTokenType.Integer)
            {
                return;
            }

            if (rect["col"].Value<int>() + rect["width"].Value<int>() > level["gridWidth"].Value<int>())
            {
                errors.Add(new DataEditorValidationError(
                    "levels.json", rectPath + ".width", "Initial unlock rect exceeds gridWidth."));
            }

            if (rect["row"].Value<int>() + rect["height"].Value<int>() > level["gridHeight"].Value<int>())
            {
                errors.Add(new DataEditorValidationError(
                    "levels.json", rectPath + ".height", "Initial unlock rect exceeds gridHeight."));
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
