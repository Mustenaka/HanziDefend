using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace HanziDefend.Data
{
    /// <summary>
    /// Immutable-by-convention aggregate and indexed entry point for all M1 JSON configuration.
    /// </summary>
    public sealed class GameConfig
    {
        private const string UnitsFile = "units.json";
        private const string CommandersFile = "commanders.json";
        private const string WavesFile = "waves.json";
        private const string LevelsFile = "levels.json";
        private const string EffectsFile = "effects.json";
        private const string EconomyFile = "economy.json";

        private GameConfig(
            UnitCatalog unitCatalog,
            CommanderCatalog commanderCatalog,
            WaveCatalog waveCatalog,
            LevelCatalog levelCatalog,
            EffectCatalog effectCatalog,
            EconomyDef economy)
        {
            UnitCatalog = unitCatalog;
            CommanderCatalog = commanderCatalog;
            WaveCatalog = waveCatalog;
            LevelCatalog = levelCatalog;
            EffectCatalog = effectCatalog;
            Economy = economy;

            UnitsById = CreateIndex(unitCatalog.Units, value => value.Id, "unit");
            BossesById = CreateIndex(unitCatalog.Bosses, value => value.Id, "boss");
            CommandersById = CreateIndex(commanderCatalog.Commanders, value => value.Id, "commander");
            WaveSetsById = CreateIndex(waveCatalog.WaveSets, value => value.Id, "wave set");
            LevelsById = CreateIndex(levelCatalog.Levels, value => value.Id, "level");
            EffectsById = CreateIndex(effectCatalog.Effects, value => value.Id, "effect");

            Units = unitCatalog.Units;
            AllyUnits = unitCatalog.Units.Where(value => value.Faction == UnitFaction.Ally).ToArray();
            EnemyUnits = unitCatalog.Units.Where(value => value.Faction == UnitFaction.Enemy).ToArray();
            Bosses = unitCatalog.Bosses;
            Commanders = commanderCatalog.Commanders;
            WaveSets = waveCatalog.WaveSets;
            Levels = levelCatalog.Levels;
            Effects = effectCatalog.Effects;
            Bases = levelCatalog.Bases;

            Validate();
            Economy.Damage.BuildTypeMultiplierLookup();
        }

        public UnitCatalog UnitCatalog { get; }

        public CommanderCatalog CommanderCatalog { get; }

        public WaveCatalog WaveCatalog { get; }

        public LevelCatalog LevelCatalog { get; }

        public EffectCatalog EffectCatalog { get; }

        public IReadOnlyList<UnitDef> Units { get; }

        public IReadOnlyList<UnitDef> AllyUnits { get; }

        public IReadOnlyList<UnitDef> EnemyUnits { get; }

        public IReadOnlyList<BossDef> Bosses { get; }

        public IReadOnlyList<CommanderDef> Commanders { get; }

        public IReadOnlyList<WaveSetDef> WaveSets { get; }

        public IReadOnlyList<LevelDef> Levels { get; }

        public IReadOnlyList<EffectDef> Effects { get; }

        public IReadOnlyDictionary<string, UnitDef> UnitsById { get; }

        public IReadOnlyDictionary<string, BossDef> BossesById { get; }

        public IReadOnlyDictionary<string, CommanderDef> CommandersById { get; }

        public IReadOnlyDictionary<string, WaveSetDef> WaveSetsById { get; }

        public IReadOnlyDictionary<string, LevelDef> LevelsById { get; }

        public IReadOnlyDictionary<string, EffectDef> EffectsById { get; }

        public BaseCatalog Bases { get; }

        public EconomyDef Economy { get; }

        public static GameConfig Load()
        {
            return Load(new JsonConfigSource());
        }

        public static GameConfig Load(IConfigSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            UnitCatalog units = Read<UnitCatalog>(source, UnitsFile);
            CommanderCatalog commanders = Read<CommanderCatalog>(source, CommandersFile);
            WaveCatalog waves = Read<WaveCatalog>(source, WavesFile);
            LevelCatalog levels = Read<LevelCatalog>(source, LevelsFile);
            EffectCatalog effects = Read<EffectCatalog>(source, EffectsFile);
            EconomyDef economy = Read<EconomyDef>(source, EconomyFile);

            try
            {
                return new GameConfig(units, commanders, waves, levels, effects, economy);
            }
            catch (ConfigLoadException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ConfigLoadException($"Failed to build GameConfig: {exception.Message}", exception);
            }
        }

        public UnitDef GetUnit(string id)
        {
            return GetRequired(UnitsById, id, "unit");
        }

        public BossDef GetBoss(string id)
        {
            return GetRequired(BossesById, id, "boss");
        }

        public CommanderDef GetCommander(string id)
        {
            return GetRequired(CommandersById, id, "commander");
        }

        public WaveSetDef GetWaveSet(string id)
        {
            return GetRequired(WaveSetsById, id, "wave set");
        }

        public LevelDef GetLevel(string id)
        {
            return GetRequired(LevelsById, id, "level");
        }

        public EffectDef GetEffect(string id)
        {
            return GetRequired(EffectsById, id, "effect");
        }

        private static T Read<T>(IConfigSource source, string fileName)
        {
            string json;
            try
            {
                json = source.ReadText(fileName);
            }
            catch (ConfigLoadException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ConfigLoadException(
                    $"Failed to read config '{fileName}': {exception.Message}",
                    exception);
            }

            try
            {
                return JsonCodec.Deserialize<T>(json);
            }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
            {
                throw new ConfigLoadException(
                    $"Failed to parse config '{fileName}' as {typeof(T).Name}: {exception.Message}",
                    exception);
            }
        }

        private void Validate()
        {
            ValidateCatalogShapes();
            ValidateIdentityPartitions();
            ValidateEffects();
            ValidateUnitsAndBosses();
            ValidateCommanders();
            ValidateEffectTriggerOwnership();
            ValidateWaves();
            ValidateLevelsAndBases();
            ValidateEconomy();
        }

        private void ValidateCatalogShapes()
        {
            Require(UnitCatalog.Units != null, "units.json: 'units' must be an array.");
            Require(UnitCatalog.Bosses != null, "units.json: 'bosses' must be an array.");
            Require(UnitCatalog.CommanderIds != null, "units.json: 'commanderIds' must be an array.");
            Require(CommanderCatalog.Commanders != null, "commanders.json: 'commanders' must be an array.");
            Require(WaveCatalog.WaveSets != null, "waves.json: 'waveSets' must be an array.");
            Require(LevelCatalog.Levels != null, "levels.json: 'levels' must be an array.");
            Require(LevelCatalog.Bases != null, "levels.json: 'bases' is required.");
            Require(EffectCatalog.Effects != null, "effects.json: 'effects' must be an array.");
        }

        private void ValidateIdentityPartitions()
        {
            var combatIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in UnitsById.Keys)
            {
                Require(combatIds.Add(id), $"Duplicate combat definition id '{id}'.");
            }

            foreach (string id in BossesById.Keys)
            {
                Require(combatIds.Add(id), $"Definition id '{id}' is used by both a unit and a boss.");
            }

            var listedCommanderIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string commanderId in UnitCatalog.CommanderIds)
            {
                RequireId(commanderId, "units.json commander id");
                Require(listedCommanderIds.Add(commanderId), $"Duplicate commander id '{commanderId}' in units.json.");
                Require(CommandersById.ContainsKey(commanderId),
                    $"units.json references commander '{commanderId}', but commanders.json does not define it.");
                Require(!combatIds.Contains(commanderId),
                    $"Commander id '{commanderId}' must not also be a UnitDef or BossDef.");
            }

            foreach (string commanderId in CommandersById.Keys)
            {
                Require(listedCommanderIds.Contains(commanderId),
                    $"Commander '{commanderId}' is missing from units.json commanderIds.");
            }
        }

        private void ValidateUnitsAndBosses()
        {
            foreach (UnitDef unit in Units)
            {
                Require(unit != null, "units.json contains a null unit entry.");
                RequireId(unit.Id, "unit id");
                RequireText(unit.Name, $"Unit '{unit.Id}' name");
                RequireText(unit.DisplayName, $"Unit '{unit.Id}' displayName");
                Require(unit.Faction == UnitFaction.Ally || unit.Faction == UnitFaction.Enemy,
                    $"Unit '{unit.Id}' must be Ally or Enemy; bosses belong in bosses[].");
                Require(unit.Tier >= UnitTier.Green && unit.Tier <= UnitTier.Gold,
                    $"Unit '{unit.Id}' has invalid tier.");
                Require(unit.Layer != UnitLayer.Unknown, $"Unit '{unit.Id}' has invalid layer.");
                Require(unit.SpawnMode != UnitSpawnMode.Unknown, $"Unit '{unit.Id}' has invalid spawnMode.");
                Require(unit.Targeting != TargetingMode.Unknown, $"Unit '{unit.Id}' has invalid targeting.");
                Require(unit.UnitType != UnitType.Unknown, $"Unit '{unit.Id}' has invalid unitType.");
                Require(unit.ArmorType != ArmorType.Unknown, $"Unit '{unit.Id}' has invalid armorType.");
                Require(unit.AtkType != AttackType.Unknown, $"Unit '{unit.Id}' has invalid atkType.");
                Require(unit.GridW > 0 && unit.GridH > 0, $"Unit '{unit.Id}' grid size must be positive.");
                Require(unit.Footprint == UnitFootprintShape.Rectangle
                        || unit.Footprint == UnitFootprintShape.MissingUpperRight
                        || unit.Footprint == UnitFootprintShape.MissingLowerLeft,
                    $"Unit '{unit.Id}' has invalid footprint.");
                if (unit.Footprint == UnitFootprintShape.MissingUpperRight ||
                    unit.Footprint == UnitFootprintShape.MissingLowerLeft)
                {
                    Require(unit.GridW == 2 && unit.GridH == 2,
                        $"Unit '{unit.Id}' footprint '{unit.Footprint}' requires a 2x2 bounding box.");
                }
                Require(unit.Effects != null, $"Unit '{unit.Id}' effects must be an array.");
                Require(unit.BonusVs != null, $"Unit '{unit.Id}' bonusVs must be an array.");
                Require(unit.Traits != null, $"Unit '{unit.Id}' traits must be an array.");
                ValidateCurve(unit.Hp, unit.Id, "hp", true);
                ValidateCurve(unit.Atk, unit.Id, "atk", false);
                ValidateCurve(unit.Range, unit.Id, "range", false);
                ValidateCurve(unit.MinRange, unit.Id, "minRange", false);
                ValidateCurve(unit.AtkSpeed, unit.Id, "atkSpeed", false);
                ValidateCurve(unit.Cooldown, unit.Id, "cooldown", false);
                ValidateCurve(unit.Armor, unit.Id, "armor", false);
                ValidateCurve(unit.Pierce, unit.Id, "pierce", false);
                ValidateCurve(unit.MoveSpeed, unit.Id, "moveSpeed", false);
                for (int level = 1; level <= 4; level++)
                {
                    Require(Formula.StatAtLevel(unit.MinRange.Base, unit.MinRange.Growth, level)
                            <= Formula.StatAtLevel(unit.Range.Base, unit.Range.Growth, level),
                        $"Unit '{unit.Id}' minRange cannot exceed range at level {level}.");
                }
                ValidateBonuses(unit.BonusVs, $"Unit '{unit.Id}'");
                ValidateTraits(unit);
                ValidateEffectReferences(unit.Effects, $"Unit '{unit.Id}'");
            }

            foreach (BossDef boss in Bosses)
            {
                Require(boss != null, "units.json contains a null boss entry.");
                RequireId(boss.Id, "boss id");
                RequireText(boss.Name, $"Boss '{boss.Id}' name");
                RequireText(boss.DisplayName, $"Boss '{boss.Id}' displayName");
                ValidateBase(boss, $"Boss '{boss.Id}'");
                Require(boss.AtkType != AttackType.Unknown && boss.AtkType != AttackType.None,
                    $"Boss '{boss.Id}' has invalid atkType.");
                RequireFiniteNonNegative(boss.Atk, $"Boss '{boss.Id}' atk");
                RequireFiniteNonNegative(boss.Range, $"Boss '{boss.Id}' range");
                RequireFiniteNonNegative(boss.AtkSpeed, $"Boss '{boss.Id}' atkSpeed");
                RequireFiniteNonNegative(boss.Pierce, $"Boss '{boss.Id}' pierce");
                Require(boss.BonusVs != null, $"Boss '{boss.Id}' bonusVs must be an array.");
                ValidateBonuses(boss.BonusVs, $"Boss '{boss.Id}'");
                Require(boss.Effects != null, $"Boss '{boss.Id}' effects must be an array.");
                ValidateEffectReferences(boss.Effects, $"Boss '{boss.Id}'");
            }
        }

        private void ValidateCommanders()
        {
            foreach (CommanderDef commander in Commanders)
            {
                Require(commander != null, "commanders.json contains a null commander entry.");
                RequireId(commander.Id, "commander id");
                RequireText(commander.Name, $"Commander '{commander.Id}' name");
                RequireText(commander.DisplayName, $"Commander '{commander.Id}' displayName");
                Require(commander.Faction == UnitFaction.Ally || commander.Faction == UnitFaction.Enemy,
                    $"Commander '{commander.Id}' faction must be Ally or Enemy.");
                if (!string.IsNullOrEmpty(commander.PassiveEffectId))
                {
                    RequireEffect(commander.PassiveEffectId, $"Commander '{commander.Id}' passiveEffectId");
                }
                if (!string.IsNullOrEmpty(commander.ActiveEffectId))
                {
                    RequireEffect(commander.ActiveEffectId, $"Commander '{commander.Id}' activeEffectId");
                }
                RequireFiniteNonNegative(commander.ActiveCooldown, $"Commander '{commander.Id}' activeCooldown");
                Require(!string.IsNullOrEmpty(commander.ActiveEffectId) || commander.ActiveCooldown == 0f,
                    $"Commander '{commander.Id}' without an active effect must have zero activeCooldown.");
            }
        }

        private void ValidateEffects()
        {
            var knownCombatants = new HashSet<string>(UnitsById.Keys, StringComparer.Ordinal);
            knownCombatants.UnionWith(BossesById.Keys);

            foreach (EffectDef effect in Effects)
            {
                Require(effect != null, "effects.json contains a null effect entry.");
                RequireId(effect.Id, "effect id");
                RequireText(effect.Name, $"Effect '{effect.Id}' name");
                RequireText(effect.Desc, $"Effect '{effect.Id}' desc");
                RequireText(effect.Rarity, $"Effect '{effect.Id}' rarity");
                Require(effect.Trigger == EffectTrigger.Manual
                        || effect.Trigger == EffectTrigger.BattleStart
                        || effect.Trigger == EffectTrigger.UnitSpawn
                        || effect.Trigger == EffectTrigger.SuicideContact,
                    $"Effect '{effect.Id}' has an invalid trigger.");
                Require(effect.Stacking == EffectStackingRule.Stack
                        || effect.Stacking == EffectStackingRule.Refresh,
                    $"Effect '{effect.Id}' has an invalid stacking rule.");
                Require(effect.Ops != null && effect.Ops.Length > 0,
                    $"Effect '{effect.Id}' must contain at least one op.");

                for (int index = 0; index < effect.Ops.Length; index++)
                {
                    EffectOpDef op = effect.Ops[index];
                    string context = $"Effect '{effect.Id}' op[{index}]";
                    Require(op != null, $"{context} cannot be null.");
                    Require(op.Op != EffectOpCode.Unknown, $"{context} has an invalid op.");
                    Require(op.Target != EffectTarget.Unknown, $"{context} has an invalid target.");
                    RequireFinite(op.Value, $"{context} value");
                    RequireFinite(op.Duration, $"{context} duration");
                    Require(op.Duration == -1f || op.Duration >= 0f,
                        $"{context} duration must be -1 or non-negative.");
                    RequireFiniteNonNegative(op.Radius, $"{context} radius");

                    if (op.Op == EffectOpCode.AddStat)
                    {
                        RequireText(op.Stat, $"{context} stat");
                        Require(op.Mode == StatModifierMode.Add || op.Mode == StatModifierMode.Mul,
                            $"{context} AddStat requires mode Add or Mul.");
                    }

                    if (op.Op == EffectOpCode.SpawnUnit)
                    {
                        RequireId(op.UnitId, $"{context} unitId");
                        Require(knownCombatants.Contains(op.UnitId),
                            $"{context} references unknown unit '{op.UnitId}'.");
                        Require(op.Count > 0, $"{context} SpawnUnit count must be positive.");
                    }

                    if (op.Target == EffectTarget.EnemyInRadius)
                    {
                        Require(op.Radius > 0f, $"{context} EnemyInRadius requires a positive radius.");
                    }
                }
            }
        }

        private void ValidateEffectTriggerOwnership()
        {
            var unitSpawnEffects = new HashSet<string>(StringComparer.Ordinal);
            var suicideContactEffects = new HashSet<string>(StringComparer.Ordinal);
            var commanderPassiveEffects = new HashSet<string>(StringComparer.Ordinal);

            foreach (UnitDef unit in Units)
            {
                foreach (string effectId in unit.Effects)
                {
                    EffectDef effect = EffectsById[effectId];
                    if (effect.Trigger == EffectTrigger.SuicideContact)
                    {
                        Require(unit.Targeting == TargetingMode.Suicide,
                            $"Effect '{effect.Id}' trigger SuicideContact requires a Suicide unit owner, "
                            + $"but unit '{unit.Id}' uses {unit.Targeting}.");
                        suicideContactEffects.Add(effect.Id);
                    }
                    else
                    {
                        Require(effect.Trigger == EffectTrigger.UnitSpawn,
                            $"Unit '{unit.Id}' effect '{effect.Id}' must use trigger UnitSpawn or, "
                            + "for Suicide units, SuicideContact.");
                        unitSpawnEffects.Add(effect.Id);
                    }
                }
            }

            foreach (BossDef boss in Bosses)
            {
                foreach (string effectId in boss.Effects)
                {
                    EffectDef effect = EffectsById[effectId];
                    Require(effect.Trigger == EffectTrigger.UnitSpawn,
                        $"Boss '{boss.Id}' effect '{effect.Id}' must use trigger UnitSpawn.");
                    unitSpawnEffects.Add(effect.Id);
                }
            }

            foreach (CommanderDef commander in Commanders)
            {
                if (!string.IsNullOrEmpty(commander.PassiveEffectId))
                {
                    EffectDef passive = EffectsById[commander.PassiveEffectId];
                    Require(passive.Trigger == EffectTrigger.BattleStart,
                        $"Commander '{commander.Id}' passive effect '{passive.Id}' must use trigger BattleStart.");
                    commanderPassiveEffects.Add(passive.Id);
                }
                if (!string.IsNullOrEmpty(commander.ActiveEffectId))
                {
                    EffectDef active = EffectsById[commander.ActiveEffectId];
                    Require(active.Trigger == EffectTrigger.Manual,
                        $"Commander '{commander.Id}' active effect '{active.Id}' must use trigger Manual.");
                }
            }

            foreach (EffectDef effect in Effects)
            {
                switch (effect.Trigger)
                {
                    case EffectTrigger.Manual:
                        break;
                    case EffectTrigger.BattleStart:
                        Require(commanderPassiveEffects.Contains(effect.Id),
                            $"Effect '{effect.Id}' trigger BattleStart requires a commander passive owner.");
                        break;
                    case EffectTrigger.UnitSpawn:
                        Require(unitSpawnEffects.Contains(effect.Id),
                            $"Effect '{effect.Id}' trigger UnitSpawn requires a unit or boss owner.");
                        break;
                    case EffectTrigger.SuicideContact:
                        Require(suicideContactEffects.Contains(effect.Id),
                            $"Effect '{effect.Id}' trigger SuicideContact requires a Suicide unit owner.");
                        Require(effect.Ops.Any(op => op.Op == EffectOpCode.Damage
                                                     && op.Target == EffectTarget.EnemyInRadius),
                            $"Effect '{effect.Id}' trigger SuicideContact requires a Damage/EnemyInRadius op.");
                        break;
                }
            }
        }

        private void ValidateWaves()
        {
            foreach (WaveSetDef waveSet in WaveSets)
            {
                Require(waveSet != null, "waves.json contains a null wave set entry.");
                RequireId(waveSet.Id, "wave set id");
                Require(waveSet.Waves != null && waveSet.Waves.Length > 0,
                    $"Wave set '{waveSet.Id}' must contain waves.");
                var indexes = new HashSet<int>();
                int previousAct = 0;

                foreach (WaveDef wave in waveSet.Waves)
                {
                    Require(wave != null, $"Wave set '{waveSet.Id}' contains a null wave.");
                    Require(wave.Index > 0, $"Wave set '{waveSet.Id}' has a non-positive wave index.");
                    Require(indexes.Add(wave.Index), $"Wave set '{waveSet.Id}' duplicates wave {wave.Index}.");
                    Require(wave.Act >= WaveActs.First && wave.Act <= WaveActs.Third,
                        $"Wave set '{waveSet.Id}' wave {wave.Index} has act {wave.Act}; "
                        + $"acts run 1..{WaveActs.Third}.");
                    Require(wave.Act >= previousAct,
                        $"Wave set '{waveSet.Id}' wave {wave.Index} drops back to act {wave.Act} "
                        + $"after act {previousAct}; acts must run forward.");
                    previousAct = wave.Act;
                    Require(wave.RewardRank == EnemyRank.Normal
                            || wave.RewardRank == EnemyRank.Elite
                            || wave.RewardRank == EnemyRank.Boss,
                        $"Wave set '{waveSet.Id}' wave {wave.Index} has invalid rewardRank '{wave.RewardRank}'.");
                    RequireFiniteNonNegative(wave.DelaySec, $"Wave {wave.Index} delaySec");
                    Require(wave.Spawns != null && wave.Spawns.Length > 0,
                        $"Wave set '{waveSet.Id}' wave {wave.Index} must contain spawns.");

                    foreach (WaveSpawnDef spawn in wave.Spawns)
                    {
                        Require(spawn != null, $"Wave set '{waveSet.Id}' wave {wave.Index} contains a null spawn.");
                        RequireId(spawn.UnitId, $"Wave {wave.Index} spawn unitId");
                        Require(UnitsById.ContainsKey(spawn.UnitId) || BossesById.ContainsKey(spawn.UnitId),
                            $"Wave set '{waveSet.Id}' wave {wave.Index} references unknown unit '{spawn.UnitId}'.");
                        if (wave.RewardRank == EnemyRank.Boss)
                        {
                            Require(BossesById.ContainsKey(spawn.UnitId),
                                $"Wave set '{waveSet.Id}' wave {wave.Index} rewardRank Boss requires a BossDef, but '{spawn.UnitId}' is not a boss.");
                        }
                        else
                        {
                            Require(UnitsById.ContainsKey(spawn.UnitId),
                                $"Wave set '{waveSet.Id}' wave {wave.Index} rewardRank {wave.RewardRank} requires a UnitDef, but '{spawn.UnitId}' is not a unit.");
                        }
                        Require(spawn.Level >= 1 && spawn.Level <= 4,
                            $"Wave set '{waveSet.Id}' wave {wave.Index} spawn '{spawn.UnitId}' level must be in [1,4].");
                        Require(spawn.Count > 0, $"Wave {wave.Index} spawn count must be positive.");
                        RequireFiniteNonNegative(spawn.SpreadX, $"Wave {wave.Index} spreadX");
                        RequireFiniteNonNegative(spawn.IntervalSec, $"Wave {wave.Index} intervalSec");
                    }
                }

                ValidateThreeActShape(waveSet);
            }
        }

        /// <summary>
        /// The WO-F1 §A shape every wave set must have: three acts, and the castle standing at the
        /// head of the third one rather than behind all of it. A castle that only appears after the
        /// last guard turns act three into "clear the field, then grind a wall", which is exactly the
        /// pacing this structure replaced.
        /// </summary>
        private void ValidateThreeActShape(WaveSetDef waveSet)
        {
            var acts = new HashSet<int>();
            WaveDef bossWave = null;
            foreach (WaveDef wave in waveSet.Waves)
            {
                acts.Add(wave.Act);
                if (wave.RewardRank != EnemyRank.Boss)
                {
                    continue;
                }

                Require(bossWave == null,
                    $"Wave set '{waveSet.Id}' declares more than one boss wave.");
                bossWave = wave;
            }

            for (int act = WaveActs.First; act <= WaveActs.Third; act++)
            {
                Require(acts.Contains(act),
                    $"Wave set '{waveSet.Id}' has no wave in act {act}; a battle is three acts.");
            }

            Require(bossWave != null, $"Wave set '{waveSet.Id}' has no boss wave.");
            Require(bossWave.Act == WaveActs.Third,
                $"Wave set '{waveSet.Id}' puts its boss wave in act {bossWave.Act}; "
                + "the castle belongs to act 3.");

            int firstThirdActWave = int.MaxValue;
            foreach (WaveDef wave in waveSet.Waves)
            {
                if (wave.Act == WaveActs.Third && wave.Index < firstThirdActWave)
                {
                    firstThirdActWave = wave.Index;
                }
            }

            Require(bossWave.Index == firstThirdActWave,
                $"Wave set '{waveSet.Id}' opens act 3 with wave {firstThirdActWave} but spawns the "
                + $"castle at wave {bossWave.Index}; the castle must arrive with the act, not after it.");
        }

        private void ValidateLevelsAndBases()
        {
            Require(Bases.Ally != null, "levels.json bases.ally is required.");
            Require(Bases.Enemy != null, "levels.json bases.enemy is required.");
            ValidateBase(Bases.Ally, "Ally base");
            ValidateBase(Bases.Enemy, "Enemy base");

            var stageIndexes = new HashSet<int>();
            foreach (LevelDef level in Levels)
            {
                Require(level != null, "levels.json contains a null level entry.");
                RequireId(level.Id, "level id");
                Require(level.StageIndex > 0, $"Level '{level.Id}' stageIndex must be positive.");
                Require(stageIndexes.Add(level.StageIndex),
                    $"Duplicate stageIndex {level.StageIndex} in levels.json.");
                RequireId(level.WaveSetId, $"Level '{level.Id}' waveSetId");
                Require(WaveSetsById.ContainsKey(level.WaveSetId),
                    $"Level '{level.Id}' references unknown wave set '{level.WaveSetId}'.");
                RequireFinitePositive(level.BaseHp, $"Level '{level.Id}' baseHp");
                Require(level.StartCoins >= 0, $"Level '{level.Id}' startCoins cannot be negative.");
                Require(level.GridWidth > 0 && level.GridHeight > 0,
                    $"Level '{level.Id}' grid size must be positive.");
                Require(level.InitialUnlock != null,
                    $"Level '{level.Id}' initialUnlock is required.");
                Require(level.InitialUnlock.Width > 0 && level.InitialUnlock.Height > 0,
                    $"Level '{level.Id}' initialUnlock must unlock at least one cell.");
                Require(level.InitialUnlock.Col >= 0 && level.InitialUnlock.Row >= 0
                        && level.InitialUnlock.Col + level.InitialUnlock.Width <= level.GridWidth
                        && level.InitialUnlock.Row + level.InitialUnlock.Height <= level.GridHeight,
                    $"Level '{level.Id}' initialUnlock falls outside its {level.GridWidth}x{level.GridHeight} grid.");
            }
        }

        private void ValidateEconomy()
        {
            Require(Economy != null, "economy.json must contain an object.");
            Require(Economy.RefreshBaseCost >= 0, "economy.refreshBaseCost cannot be negative.");
            Require(Economy.RefreshCostGrowth >= 0, "economy.refreshCostGrowth cannot be negative.");
            Require(Economy.GridUnlock != null, "economy.gridUnlock is required.");
            Require(Economy.GridUnlock.PurchaseBaseCost >= 0,
                "economy.gridUnlock.purchaseBaseCost cannot be negative.");
            Require(Economy.GridUnlock.PurchaseCostGrowth >= 0,
                "economy.gridUnlock.purchaseCostGrowth cannot be negative.");
            Require(Economy.GridUnlock.AutoUnlockPerMinorStage >= 0,
                "economy.gridUnlock.autoUnlockPerMinorStage cannot be negative.");
            Require(Economy.DeployUi != null, "economy.deployUi is required.");
            RequireFinitePositive(Economy.DeployUi.HandCardScale, "economy.deployUi.handCardScale");
            RequireFinitePositive(Economy.DeployUi.SnapRadiusCells, "economy.deployUi.snapRadiusCells");
            RequireFinite(Economy.DeployUi.CellSpacingRatio, "economy.deployUi.cellSpacingRatio");
            RequireFinite(Economy.DeployUi.DragLiftCells, "economy.deployUi.dragLiftCells");
            Require(Economy.DeployUi.CellSpacingRatio >= 0f,
                "economy.deployUi.cellSpacingRatio cannot be negative.");
            Require(Economy.DeployUi.DragLiftCells >= 0f,
                "economy.deployUi.dragLiftCells cannot be negative.");
            ValidateTierColors(Economy.DeployUi);
            Require(Economy.DropCoins != null, "economy.dropCoins is required.");
            Require(Economy.Damage != null, "economy.damage is required.");
            Require(Economy.CardWeights != null, "economy.cardWeights is required.");
            Require(Economy.CardOffer != null, "economy.cardOffer is required.");
            Require(Economy.CardPool != null, "economy.cardPool is required.");
            Require(Economy.SettlementReward != null, "economy.settlementReward is required.");
            Require(Economy.Battle != null, "economy.battle is required.");

            DropCoinsDef drops = Economy.DropCoins;
            Require(drops.Normal > 0, "economy.dropCoins.normal must be positive.");
            Require(drops.Elite >= drops.Normal, "economy.dropCoins.elite must be at least normal.");
            Require(drops.Boss >= drops.Elite, "economy.dropCoins.boss must be at least elite.");
            RequireFinitePositive(Economy.Damage.ArmorScale, "economy.damage.armorScale");
            Require(Economy.Damage.MinimumDamage >= 1, "economy.damage.minimumDamage must be positive.");
            RequireFinitePositive(Economy.Damage.NeutralTypeMultiplier, "economy.damage.neutralTypeMultiplier");
            ValidateTypeMultipliers(Economy.Damage.TypeMultipliers);

            CardWeightsDef weights = Economy.CardWeights;
            Require(weights.Unit >= 0 && weights.Unlock >= 0 && weights.Buff >= 0 && weights.Global >= 0,
                "economy.cardWeights values cannot be negative.");
            int total = checked(weights.Unit + weights.Unlock + weights.Buff + weights.Global);
            Require(total == 100, $"economy.cardWeights must total 100, but total {total}.");

            CardOfferRulesDef cardOffer = Economy.CardOffer;
            Require(cardOffer.BaseCount > 0, "economy.cardOffer.baseCount must be positive.");
            Require(cardOffer.LuckyExtraCount > 0,
                "economy.cardOffer.luckyExtraCount must be positive.");
            RequireFinite(cardOffer.LuckyChance, "economy.cardOffer.luckyChance");
            Require(cardOffer.LuckyChance >= 0f && cardOffer.LuckyChance <= 1f,
                "economy.cardOffer.luckyChance must be in [0,1].");
            Require(cardOffer.FreeOffersPerMinorStage != null,
                "economy.cardOffer.freeOffersPerMinorStage must be an array.");
            Require(cardOffer.FreeOffersPerMinorStage.Length >= Levels.Count,
                $"economy.cardOffer.freeOffersPerMinorStage has "
                + $"{cardOffer.FreeOffersPerMinorStage.Length} entries for {Levels.Count} minor "
                + "stages; every stage must name its own number of free hands.");
            for (int index = 0; index < cardOffer.FreeOffersPerMinorStage.Length; index++)
            {
                Require(cardOffer.FreeOffersPerMinorStage[index] > 0,
                    $"economy.cardOffer.freeOffersPerMinorStage[{index}] must be positive — a minor "
                    + "stage with no free hand cannot be played.");
                if (index > 0)
                {
                    Require(cardOffer.FreeOffersPerMinorStage[index]
                            >= cardOffer.FreeOffersPerMinorStage[index - 1],
                        "economy.cardOffer.freeOffersPerMinorStage must not decrease: later minor "
                        + "stages have more unlocked cells to fill, not fewer.");
                }
            }

            ValidateDeployment(Economy.Deployment);
            ValidateCardPool(Economy.CardPool);
            ValidateSettlementReward(Economy.SettlementReward);

            BattleRulesDef battle = Economy.Battle;
            Require(battle.TickRateHz > 0, "economy.battle.tickRateHz must be positive.");
            RequireFinitePositive(battle.RetargetInterval, "economy.battle.retargetInterval");
            RequireFinitePositive(battle.TargetSearchRadius, "economy.battle.targetSearchRadius");
            RequireFinitePositive(battle.ColliderRadius, "economy.battle.colliderRadius");
            RequireFiniteNonNegative(battle.SameColumnTolerance, "economy.battle.sameColumnTolerance");
            RequireFinitePositive(battle.SeparationDistance, "economy.battle.separationDistance");
            ValidatePosition(battle.AllyBasePosition, "economy.battle.allyBasePosition");
            ValidatePosition(battle.EnemyBasePosition, "economy.battle.enemyBasePosition");
            ValidatePosition(battle.EnemySpawnCenter, "economy.battle.enemySpawnCenter");
            ValidatePosition(battle.DeploymentOriginOffset, "economy.battle.deploymentOriginOffset");
            ValidatePosition(battle.DeploymentCellSize, "economy.battle.deploymentCellSize");
            Require(battle.DeploymentCellSize.X > 0f && battle.DeploymentCellSize.Y > 0f,
                "economy.battle.deploymentCellSize values must be positive.");
            RequireFinitePositive(battle.DeploymentSpreadWidth, "economy.battle.deploymentSpreadWidth");
            RequireFinite(battle.AllyAdvanceLimitY, "economy.battle.allyAdvanceLimitY");
            Require(battle.AllyAdvanceLimitY < battle.EnemySpawnCenter.Y,
                "economy.battle.allyAdvanceLimitY must sit below the enemy spawn centre; a limit at "
                + "or beyond it is the fight-at-their-door behaviour it exists to prevent.");
            Require(battle.AllyAdvanceLimitY > battle.AllyBasePosition.Y,
                "economy.battle.allyAdvanceLimitY must sit above the ally camp, or the line can "
                + "never leave home.");
            Require(battle.AllyBasePosition.X != battle.EnemyBasePosition.X
                    || battle.AllyBasePosition.Y != battle.EnemyBasePosition.Y,
                "economy.battle allyBasePosition and enemyBasePosition must be different.");
        }

        /// <summary>
        /// WO-F3: a deployment cell produces units forever, so both of its bounds have to be real
        /// numbers somebody chose. A missing or non-positive cap is an unbounded spawner.
        /// </summary>
        private void ValidateDeployment(DeploymentRulesDef deployment)
        {
            Require(deployment != null, "economy.deployment is required.");
            Require(deployment.AllyFieldUnitLimit > 0,
                "economy.deployment.allyFieldUnitLimit must be positive — deployment cells produce "
                + "units for the whole battle, so the field needs a declared ceiling.");
            Require(deployment.LiveCapByFootprintCells != null
                    && deployment.LiveCapByFootprintCells.Length > 0,
                "economy.deployment.liveCapByFootprintCells must declare at least one tier.");

            var seenCells = new HashSet<int>();
            int previousCells = 0;
            int previousCap = int.MaxValue;
            foreach (FootprintLiveCapDef entry in deployment.LiveCapByFootprintCells)
            {
                Require(entry != null, "economy.deployment.liveCapByFootprintCells has a null entry.");
                Require(entry.Cells > 0,
                    "economy.deployment.liveCapByFootprintCells cells must be positive.");
                Require(entry.Cap > 0,
                    $"economy.deployment.liveCapByFootprintCells cap for {entry.Cells} cells must be "
                    + "positive; zero would make the cell produce nothing at all.");
                Require(seenCells.Add(entry.Cells),
                    $"economy.deployment.liveCapByFootprintCells repeats {entry.Cells} cells.");
                Require(entry.Cells > previousCells,
                    "economy.deployment.liveCapByFootprintCells must be ordered by ascending cells.");
                Require(entry.Cap <= previousCap,
                    $"economy.deployment.liveCapByFootprintCells gives {entry.Cells} cells a higher "
                    + "cap than a smaller footprint; a bigger unit cannot stack deeper than a small one.");
                previousCells = entry.Cells;
                previousCap = entry.Cap;
            }

            foreach (UnitDef unit in AllyUnits)
            {
                int cells = UnitFootprintCellCount(unit);
                Require(deployment.LiveCapForCells(cells) > 0,
                    $"economy.deployment.liveCapByFootprintCells has no usable cap for ally unit "
                    + $"'{unit.Id}' ({cells} cells).");
            }
        }

        /// <summary>Occupied cells of a unit footprint; the two L-shapes cover three, not four.</summary>
        private static int UnitFootprintCellCount(UnitDef unit)
        {
            int bounding = unit.GridW * unit.GridH;
            return unit.Footprint == UnitFootprintShape.Rectangle ? bounding : bounding - 1;
        }

        private static void ValidatePosition(Position2Def position, string context)
        {
            Require(position != null, $"{context} is required.");
            RequireFinite(position.X, $"{context}.x");
            RequireFinite(position.Y, $"{context}.y");
        }

        private void ValidateSettlementReward(SettlementRewardRulesDef rules)
        {
            Require(rules.SlotCount > 0, "economy.settlementReward.slotCount must be positive.");
            Require(rules.BuffWeight >= 0 && rules.ActiveSkillWeight >= 0,
                "economy.settlementReward weights cannot be negative.");
            int totalWeight = checked(rules.BuffWeight + rules.ActiveSkillWeight);
            Require(totalWeight == 100,
                $"economy.settlementReward weights must total 100, but total {totalWeight}.");

            var pooled = new HashSet<string>(StringComparer.Ordinal);
            ValidateSettlementEffectPool(
                rules.BuffEffectIds,
                "buffEffectIds",
                pooled);
            ValidateSettlementEffectPool(
                rules.ActiveSkillEffectIds,
                "activeSkillEffectIds",
                pooled);

            Require(rules.UniqueEffectIds != null,
                "economy.settlementReward.uniqueEffectIds must be an array.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string effectId in rules.UniqueEffectIds)
            {
                RequireEffect(effectId, "economy.settlementReward.uniqueEffectIds effect id");
                Require(unique.Add(effectId),
                    $"economy.settlementReward.uniqueEffectIds repeats effect '{effectId}'.");
                Require(pooled.Contains(effectId),
                    $"economy.settlementReward unique effect '{effectId}' is not in a reward pool.");
            }
        }

        private void ValidateSettlementEffectPool(
            string[] effectIds,
            string fieldName,
            ISet<string> pooled)
        {
            Require(effectIds != null && effectIds.Length > 0,
                $"economy.settlementReward.{fieldName} must contain at least one effect id.");
            foreach (string effectId in effectIds)
            {
                RequireEffect(effectId, $"economy.settlementReward.{fieldName} effect id");
                Require(pooled.Add(effectId),
                    $"economy.settlementReward effect '{effectId}' is duplicated across reward pools.");
            }
        }

        private void ValidateEffectReferences(IEnumerable<string> effectIds, string owner)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string effectId in effectIds)
            {
                RequireEffect(effectId, $"{owner} effect id");
                Require(seen.Add(effectId), $"{owner} repeats effect '{effectId}'.");
            }
        }

        private void RequireEffect(string effectId, string context)
        {
            RequireId(effectId, context);
            Require(EffectsById.ContainsKey(effectId), $"{context} references unknown effect '{effectId}'.");
        }

        /// <summary>
        /// The per-level palette. Levels must run 1..N with no gaps so the view can index it
        /// directly, and every colour must be a literal <c>#RRGGBB</c> — the view parses these and a
        /// silent parse failure would show up as a black card rather than as a config error.
        ///
        /// <para>N is deliberately not tied to the merge ceiling. The table may hold more levels than
        /// merging can reach; what it may not do is hold fewer than the ceiling, or the top merge
        /// would have no colour.</para>
        /// </summary>
        private static void ValidateTierColors(DeployUiRulesDef deployUi)
        {
            RequireFinitePositive(deployUi.CellInsetRatio, "economy.deployUi.cellInsetRatio");
            RequireFinitePositive(deployUi.UnitCardInsetRatio, "economy.deployUi.unitCardInsetRatio");
            RequireFinitePositive(deployUi.UnitCardOutlineRatio, "economy.deployUi.unitCardOutlineRatio");
            RequireFinitePositive(deployUi.LevelBadgeRatio, "economy.deployUi.levelBadgeRatio");
            RequireFinitePositive(deployUi.UnitCardTintAlpha, "economy.deployUi.unitCardTintAlpha");
            Require(deployUi.UnitCardTintAlpha < 0.6f,
                "economy.deployUi.unitCardTintAlpha must stay below 0.6 or the wash hides the artwork.");
            Require(deployUi.NameLabelMaxFootprintCells >= 1,
                "economy.deployUi.nameLabelMaxFootprintCells must be at least 1 — a 1x1 card has no "
                + "room for artwork behind its label, so it always needs the written name.");
            Require(deployUi.CellInsetRatio < 0.5f, "economy.deployUi.cellInsetRatio must be below 0.5.");
            Require(deployUi.UnitCardInsetRatio < 0.5f, "economy.deployUi.unitCardInsetRatio must be below 0.5.");

            TierColorDef[] colors = deployUi.TierColors;
            Require(colors != null && colors.Length > 0, "economy.deployUi.tierColors is required.");
            for (int index = 0; index < colors.Length; index++)
            {
                TierColorDef entry = colors[index];
                Require(entry != null, $"economy.deployUi.tierColors[{index}] is null.");
                Require(entry.Level == index + 1,
                    $"economy.deployUi.tierColors must be ordered 1..N; entry {index} has level {entry.Level}.");
                RequireText(entry.Name, $"economy.deployUi.tierColors[{index}].name");
                RequireHexColor(entry.Fill, $"economy.deployUi.tierColors[{index}].fill");
                RequireHexColor(entry.Border, $"economy.deployUi.tierColors[{index}].border");
                RequireHexColor(entry.Text, $"economy.deployUi.tierColors[{index}].text");
            }
        }

        private static void RequireHexColor(string value, string context)
        {
            Require(!string.IsNullOrEmpty(value) && value.Length == 7 && value[0] == '#',
                $"{context} must be a #RRGGBB colour.");
            for (int index = 1; index < value.Length; index++)
            {
                char digit = value[index];
                bool hex = (digit >= '0' && digit <= '9')
                           || (digit >= 'a' && digit <= 'f')
                           || (digit >= 'A' && digit <= 'F');
                Require(hex, $"{context} has a non-hex digit '{digit}'.");
            }
        }

        private static void ValidateCurve(StatCurve curve, string unitId, string stat, bool mustBePositive)
        {
            Require(curve != null, $"Unit '{unitId}' stat '{stat}' is required.");
            RequireFinite(curve.Base, $"Unit '{unitId}' {stat}.base");
            RequireFinite(curve.Growth, $"Unit '{unitId}' {stat}.growth");
            Require(mustBePositive ? curve.Base > 0f : curve.Base >= 0f,
                $"Unit '{unitId}' {stat}.base must be {(mustBePositive ? "positive" : "non-negative")}.");
            Require(curve.Base * (1f + curve.Growth * 3f) >= 0f,
                $"Unit '{unitId}' {stat} becomes negative at level 4.");
        }

        private static void ValidateBase(BaseDef value, string context)
        {
            RequireId(value.Id, $"{context} id");
            RequireText(value.Name, $"{context} name");
            RequireText(value.DisplayName, $"{context} displayName");
            RequireFinitePositive(value.Hp, $"{context} hp");
            RequireFiniteNonNegative(value.Armor, $"{context} armor");
            Require(value.UnitType == UnitType.Building, $"{context} unitType must be Building.");
            Require(value.ArmorType == ArmorType.Building, $"{context} armorType must be Building.");
        }

        private static void ValidateBonuses(IEnumerable<BonusVsDef> bonuses, string context)
        {
            var targets = new HashSet<BonusTarget>();
            foreach (BonusVsDef bonus in bonuses)
            {
                Require(bonus != null, $"{context} bonusVs contains a null entry.");
                Require(bonus.Target != BonusTarget.Unknown, $"{context} bonusVs has an invalid target.");
                Require(targets.Add(bonus.Target), $"{context} repeats bonus target '{bonus.Target}'.");
                RequireFiniteNonNegative(bonus.Value, $"{context} bonusVs {bonus.Target} value");
            }
        }

        private void ValidateTraits(UnitDef unit)
        {
            var types = new HashSet<UnitTraitType>();
            foreach (UnitTraitDef trait in unit.Traits)
            {
                Require(trait != null, $"Unit '{unit.Id}' traits contains a null entry.");
                Require(trait.Type != UnitTraitType.Unknown, $"Unit '{unit.Id}' has an invalid trait type.");
                Require(types.Add(trait.Type), $"Unit '{unit.Id}' repeats trait '{trait.Type}'.");
                RequireFiniteNonNegative(trait.Multiplier, $"Unit '{unit.Id}' trait {trait.Type} multiplier");
                RequireFiniteNonNegative(trait.DecayRate, $"Unit '{unit.Id}' trait {trait.Type} decayRate");
                RequireFiniteNonNegative(trait.MinimumMultiplier,
                    $"Unit '{unit.Id}' trait {trait.Type} minimumMultiplier");

                switch (trait.Type)
                {
                    case UnitTraitType.Charge:
                        Require(trait.Multiplier > 0f,
                            $"Unit '{unit.Id}' Charge multiplier must be positive.");
                        break;
                    case UnitTraitType.PiercingShot:
                        Require(trait.Multiplier > 0f,
                            $"Unit '{unit.Id}' PiercingShot multiplier must be positive.");
                        Require(trait.DecayRate > 0f && trait.DecayRate < 1f,
                            $"Unit '{unit.Id}' PiercingShot decayRate must be in (0,1).");
                        Require(trait.MinimumMultiplier > 0f,
                            $"Unit '{unit.Id}' PiercingShot minimumMultiplier must be positive.");
                        break;
                    case UnitTraitType.FireAura:
                        Require(trait.Multiplier > 0f && trait.Multiplier <= 1f,
                            $"Unit '{unit.Id}' FireAura multiplier must be in (0,1].");
                        Require(trait.MinimumMultiplier > 0f,
                            $"Unit '{unit.Id}' FireAura radius must be positive.");
                        break;
                    case UnitTraitType.IceAura:
                        Require(trait.Multiplier > 0f && trait.Multiplier < 1f,
                            $"Unit '{unit.Id}' IceAura multiplier must be in (0,1).");
                        Require(trait.DecayRate > 0f,
                            $"Unit '{unit.Id}' IceAura duration must be positive.");
                        Require(trait.MinimumMultiplier > 0f,
                            $"Unit '{unit.Id}' IceAura radius must be positive.");
                        break;
                    case UnitTraitType.DeathSpawn:
                        RequireId(trait.UnitId, $"Unit '{unit.Id}' DeathSpawn unitId");
                        Require(UnitsById.ContainsKey(trait.UnitId),
                            $"Unit '{unit.Id}' DeathSpawn references unknown unit '{trait.UnitId}'.");
                        Require(trait.Count > 0, $"Unit '{unit.Id}' DeathSpawn count must be positive.");
                        break;
                }
            }
        }

        private static void ValidateTypeMultipliers(TypeMultiplierDef[] entries)
        {
            Require(entries != null, "economy.damage.typeMultipliers is required.");
            var seen = new HashSet<(ArmorType armor, AttackType attack)>();
            foreach (TypeMultiplierDef entry in entries)
            {
                Require(entry != null, "economy.damage.typeMultipliers contains a null entry.");
                Require(entry.ArmorType == ArmorType.Unarmored
                        || entry.ArmorType == ArmorType.Light
                        || entry.ArmorType == ArmorType.Heavy
                        || entry.ArmorType == ArmorType.Building,
                    $"economy.damage.typeMultipliers has invalid armorType '{entry.ArmorType}'.");
                Require(entry.AtkType == AttackType.Slash
                        || entry.AtkType == AttackType.Blunt
                        || entry.AtkType == AttackType.Arrow
                        || entry.AtkType == AttackType.Siege,
                    $"economy.damage.typeMultipliers has invalid atkType '{entry.AtkType}'.");
                Require(seen.Add((entry.ArmorType, entry.AtkType)),
                    $"economy.damage.typeMultipliers repeats {entry.ArmorType}/{entry.AtkType}.");
                RequireFinitePositive(entry.Value,
                    $"economy.damage.typeMultipliers {entry.ArmorType}/{entry.AtkType} value");
            }

            ArmorType[] armorTypes = { ArmorType.Unarmored, ArmorType.Light, ArmorType.Heavy, ArmorType.Building };
            AttackType[] attackTypes = { AttackType.Slash, AttackType.Blunt, AttackType.Arrow, AttackType.Siege };
            foreach (ArmorType armorType in armorTypes)
            foreach (AttackType attackType in attackTypes)
            {
                Require(seen.Contains((armorType, attackType)),
                    $"economy.damage.typeMultipliers is missing {armorType}/{attackType}.");
            }
            Require(seen.Count == 16, "economy.damage.typeMultipliers must contain exactly 16 entries.");
        }

        private void ValidateCardPool(CardPoolRulesDef cardPool)
        {
            Require(cardPool.ShapeUnlocks != null, "economy.cardPool.shapeUnlocks is required.");
            var expected = new HashSet<(int width, int height)>
            {
                (1, 1), (2, 1), (1, 2), (2, 2), (3, 1)
            };
            var seen = new HashSet<(int width, int height)>();
            foreach (FootprintUnlockDef unlock in cardPool.ShapeUnlocks)
            {
                Require(unlock != null, "economy.cardPool.shapeUnlocks contains a null entry.");
                Require(unlock.GridW > 0 && unlock.GridH > 0,
                    "economy.cardPool.shapeUnlocks grid dimensions must be positive.");
                Require(expected.Contains((unlock.GridW, unlock.GridH)),
                    $"economy.cardPool.shapeUnlocks contains unsupported shape {unlock.GridW}x{unlock.GridH}.");
                Require(seen.Add((unlock.GridW, unlock.GridH)),
                    $"economy.cardPool.shapeUnlocks repeats shape {unlock.GridW}x{unlock.GridH}.");
            }
            Require(seen.SetEquals(expected),
                "economy.cardPool.shapeUnlocks must define 1x1, 2x1, 1x2, 2x2 and 3x1 exactly once.");
            foreach (UnitDef unit in AllyUnits)
            {
                Require(seen.Contains((unit.GridW, unit.GridH)),
                    $"economy.cardPool.shapeUnlocks does not cover ally unit '{unit.Id}' shape {unit.GridW}x{unit.GridH}.");
            }
            Require(cardPool.GuaranteeBeforeStageIndex > 1,
                "economy.cardPool.guaranteeBeforeStageIndex must be greater than 1.");
            Require(cardPool.GuaranteeAttackType != AttackType.Unknown
                    && cardPool.GuaranteeAttackType != AttackType.None,
                "economy.cardPool.guaranteeAttackType must be a combat attack type.");
            Require(AllyUnits.Any(unit => unit.AtkType == cardPool.GuaranteeAttackType),
                $"economy.cardPool guarantee attack type '{cardPool.GuaranteeAttackType}' has no ally unit.");

            var reservedEffectIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (UnitDef unit in Units)
            {
                reservedEffectIds.UnionWith(unit.Effects);
            }
            foreach (BossDef boss in Bosses)
            {
                reservedEffectIds.UnionWith(boss.Effects);
            }
            foreach (CommanderDef commander in Commanders)
            {
                if (!string.IsNullOrEmpty(commander.PassiveEffectId))
                {
                    reservedEffectIds.Add(commander.PassiveEffectId);
                }
                if (!string.IsNullOrEmpty(commander.ActiveEffectId))
                {
                    reservedEffectIds.Add(commander.ActiveEffectId);
                }
            }

            var pooledEffectIds = new HashSet<string>(StringComparer.Ordinal);
            ValidateEffectPool(cardPool.BuffEffectIds, "buffEffectIds", reservedEffectIds, pooledEffectIds);
            ValidateEffectPool(cardPool.GlobalEffectIds, "globalEffectIds", reservedEffectIds, pooledEffectIds);
        }

        private void ValidateEffectPool(
            string[] effectIds,
            string fieldName,
            ISet<string> reservedEffectIds,
            ISet<string> pooledEffectIds)
        {
            Require(effectIds != null && effectIds.Length > 0,
                $"economy.cardPool.{fieldName} must contain at least one effect id.");
            foreach (string effectId in effectIds)
            {
                RequireId(effectId, $"economy.cardPool.{fieldName} effect id");
                Require(EffectsById.ContainsKey(effectId),
                    $"economy.cardPool.{fieldName} references unknown effect '{effectId}'.");
                Require(!reservedEffectIds.Contains(effectId),
                    $"economy.cardPool.{fieldName} effect '{effectId}' is reserved by a unit, boss or commander.");
                Require(pooledEffectIds.Add(effectId),
                    $"economy.cardPool effect '{effectId}' is duplicated across card effect pools.");
            }
        }

        private static IReadOnlyDictionary<string, T> CreateIndex<T>(
            IEnumerable<T> values,
            Func<T, string> idSelector,
            string kind)
            where T : class
        {
            if (values == null)
            {
                throw new ConfigLoadException($"The {kind} collection is missing.");
            }

            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T value in values)
            {
                Require(value != null, $"The {kind} collection contains a null entry.");
                string id = idSelector(value);
                RequireId(id, $"{kind} id");
                Require(result.TryAdd(id, value), $"Duplicate {kind} id '{id}'.");
            }

            return result;
        }

        private static T GetRequired<T>(IReadOnlyDictionary<string, T> index, string id, string kind)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException($"{kind} id cannot be empty.", nameof(id));
            }

            if (!index.TryGetValue(id, out T value))
            {
                throw new KeyNotFoundException($"Unknown {kind} id '{id}'.");
            }

            return value;
        }

        private static void RequireId(string value, string context)
        {
            RequireText(value, context);
            Require(value.Trim().Equals(value, StringComparison.Ordinal), $"{context} cannot have surrounding whitespace.");
        }

        private static void RequireText(string value, string context)
        {
            Require(!string.IsNullOrWhiteSpace(value), $"{context} is required.");
        }

        private static void RequireFinite(float value, string context)
        {
            Require(!float.IsNaN(value) && !float.IsInfinity(value), $"{context} must be finite.");
        }

        private static void RequireFinitePositive(float value, string context)
        {
            RequireFinite(value, context);
            Require(value > 0f, $"{context} must be positive.");
        }

        private static void RequireFiniteNonNegative(float value, string context)
        {
            RequireFinite(value, context);
            Require(value >= 0f, $"{context} cannot be negative.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ConfigLoadException($"Invalid configuration: {message}");
            }
        }
    }
}
