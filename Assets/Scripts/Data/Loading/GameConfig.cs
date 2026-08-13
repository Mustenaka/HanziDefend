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
                Require(unit.GridW > 0 && unit.GridH > 0, $"Unit '{unit.Id}' grid size must be positive.");
                Require(unit.Effects != null, $"Unit '{unit.Id}' effects must be an array.");
                ValidateCurve(unit.Hp, unit.Id, "hp", true);
                ValidateCurve(unit.Atk, unit.Id, "atk", false);
                ValidateCurve(unit.Range, unit.Id, "range", false);
                ValidateCurve(unit.AtkSpeed, unit.Id, "atkSpeed", false);
                ValidateCurve(unit.Cooldown, unit.Id, "cooldown", false);
                ValidateCurve(unit.Armor, unit.Id, "armor", false);
                ValidateCurve(unit.Pierce, unit.Id, "pierce", false);
                ValidateCurve(unit.MoveSpeed, unit.Id, "moveSpeed", false);
                ValidateEffectReferences(unit.Effects, $"Unit '{unit.Id}'");
            }

            foreach (BossDef boss in Bosses)
            {
                Require(boss != null, "units.json contains a null boss entry.");
                RequireId(boss.Id, "boss id");
                RequireText(boss.Name, $"Boss '{boss.Id}' name");
                RequireText(boss.DisplayName, $"Boss '{boss.Id}' displayName");
                ValidateBase(boss, $"Boss '{boss.Id}'");
                RequireFiniteNonNegative(boss.Atk, $"Boss '{boss.Id}' atk");
                RequireFiniteNonNegative(boss.Range, $"Boss '{boss.Id}' range");
                RequireFiniteNonNegative(boss.AtkSpeed, $"Boss '{boss.Id}' atkSpeed");
                RequireFiniteNonNegative(boss.Pierce, $"Boss '{boss.Id}' pierce");
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
                RequireEffect(commander.PassiveEffectId, $"Commander '{commander.Id}' passiveEffectId");
                RequireEffect(commander.ActiveEffectId, $"Commander '{commander.Id}' activeEffectId");
                RequireFiniteNonNegative(commander.ActiveCooldown, $"Commander '{commander.Id}' activeCooldown");
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
                EffectDef passive = EffectsById[commander.PassiveEffectId];
                EffectDef active = EffectsById[commander.ActiveEffectId];
                Require(passive.Trigger == EffectTrigger.BattleStart,
                    $"Commander '{commander.Id}' passive effect '{passive.Id}' must use trigger BattleStart.");
                Require(active.Trigger == EffectTrigger.Manual,
                    $"Commander '{commander.Id}' active effect '{active.Id}' must use trigger Manual.");
                commanderPassiveEffects.Add(passive.Id);
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

                foreach (WaveDef wave in waveSet.Waves)
                {
                    Require(wave != null, $"Wave set '{waveSet.Id}' contains a null wave.");
                    Require(wave.Index > 0, $"Wave set '{waveSet.Id}' has a non-positive wave index.");
                    Require(indexes.Add(wave.Index), $"Wave set '{waveSet.Id}' duplicates wave {wave.Index}.");
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
            }
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
                Require(level.GridCols > 0 && level.GridRows > 0,
                    $"Level '{level.Id}' starting grid must be positive.");
                Require(level.GridMaxCols >= level.GridCols && level.GridMaxRows >= level.GridRows,
                    $"Level '{level.Id}' max grid cannot be smaller than its starting grid.");
            }
        }

        private void ValidateEconomy()
        {
            Require(Economy != null, "economy.json must contain an object.");
            Require(Economy.RefreshBaseCost >= 0, "economy.refreshBaseCost cannot be negative.");
            Require(Economy.RefreshCostGrowth >= 0, "economy.refreshCostGrowth cannot be negative.");
            Require(Economy.DropCoins != null, "economy.dropCoins is required.");
            Require(Economy.Damage != null, "economy.damage is required.");
            Require(Economy.CardWeights != null, "economy.cardWeights is required.");
            Require(Economy.Battle != null, "economy.battle is required.");

            DropCoinsDef drops = Economy.DropCoins;
            Require(drops.Normal > 0, "economy.dropCoins.normal must be positive.");
            Require(drops.Elite >= drops.Normal, "economy.dropCoins.elite must be at least normal.");
            Require(drops.Boss >= drops.Elite, "economy.dropCoins.boss must be at least elite.");
            RequireFinitePositive(Economy.Damage.ArmorScale, "economy.damage.armorScale");
            Require(Economy.Damage.MinimumDamage >= 1, "economy.damage.minimumDamage must be positive.");

            CardWeightsDef weights = Economy.CardWeights;
            Require(weights.Unit >= 0 && weights.Expand >= 0 && weights.Buff >= 0 && weights.Global >= 0,
                "economy.cardWeights values cannot be negative.");
            int total = checked(weights.Unit + weights.Expand + weights.Buff + weights.Global);
            Require(total == 100, $"economy.cardWeights must total 100, but total {total}.");

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
            Require(battle.AllyBasePosition.X != battle.EnemyBasePosition.X
                    || battle.AllyBasePosition.Y != battle.EnemyBasePosition.Y,
                "economy.battle allyBasePosition and enemyBasePosition must be different.");
        }

        private static void ValidatePosition(Position2Def position, string context)
        {
            Require(position != null, $"{context} is required.");
            RequireFinite(position.X, $"{context}.x");
            RequireFinite(position.Y, $"{context}.y");
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
            RequireFinitePositive(value.Hp, $"{context} hp");
            RequireFiniteNonNegative(value.Armor, $"{context} armor");
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
