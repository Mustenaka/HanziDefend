using System;

namespace HanziDefend.Data
{
    [Serializable]
    public sealed class CommanderDef
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public UnitFaction Faction { get; set; }

        public string PassiveEffectId { get; set; } = string.Empty;

        public string ActiveEffectId { get; set; } = string.Empty;

        public float ActiveCooldown { get; set; }
    }

    [Serializable]
    public sealed class WaveSpawnDef
    {
        public string UnitId { get; set; } = string.Empty;

        public int Level { get; set; }

        public int Count { get; set; }

        public float SpreadX { get; set; }

        public float IntervalSec { get; set; }
    }

    [Serializable]
    public sealed class WaveDef
    {
        public int Index { get; set; }

        /// <summary>
        /// Which of the three acts this wave belongs to (WO-F1 §A). Acts are the readable unit of
        /// a battle — inside one the enemy stream never stops, and between two there is a visible
        /// gap. The field is only a label: the gap itself is expressed as a longer
        /// <see cref="DelaySec"/> on the act's first wave, so the timeline stays one flat list.
        /// </summary>
        public int Act { get; set; } = WaveActs.First;

        public EnemyRank RewardRank { get; set; } = (EnemyRank)(-1);

        public float DelaySec { get; set; }

        public WaveSpawnDef[] Spawns { get; set; } = Array.Empty<WaveSpawnDef>();
    }

    /// <summary>The three-act structure WO-F1 §A locks in. Named so no caller writes a bare 3.</summary>
    public static class WaveActs
    {
        public const int First = 1;
        public const int Second = 2;
        public const int Third = 3;
        public const int Count = 3;
    }

    [Serializable]
    public sealed class WaveSetDef
    {
        public string Id { get; set; } = string.Empty;

        public WaveDef[] Waves { get; set; } = Array.Empty<WaveDef>();
    }

    /// <summary>An axis-aligned rectangle of grid cells, addressed by its lower-left corner.</summary>
    [Serializable]
    public sealed class GridRectDef
    {
        public int Col { get; set; }

        public int Row { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }
    }

    [Serializable]
    public sealed class LevelDef
    {
        public string Id { get; set; } = string.Empty;

        public int StageIndex { get; set; }

        public string WaveSetId { get; set; } = string.Empty;

        public float BaseHp { get; set; }

        public int StartCoins { get; set; }

        /// <summary>Total playfield width. The field never grows; cells are unlocked instead.</summary>
        public int GridWidth { get; set; }

        public int GridHeight { get; set; }

        /// <summary>Cells unlocked at the start of a major stage. Everything else starts locked.</summary>
        public GridRectDef InitialUnlock { get; set; } = new GridRectDef();
    }

    public enum EffectOpCode
    {
        Unknown = 0,
        AddStat,
        Heal,
        Damage,
        GrantShield,
        SpawnUnit,
        ModifyCoins
    }

    public enum EffectTarget
    {
        Unknown = 0,
        SelfUnit,
        AllyAll,
        AllyAdjacent,
        EnemyNearest,
        EnemyInRadius,
        EnemyBase
    }

    public enum StatModifierMode
    {
        Unknown = 0,
        Add,
        Mul
    }

    public enum EffectTrigger
    {
        Unknown = 0,
        Manual,
        BattleStart,
        UnitSpawn,
        SuicideContact
    }

    public enum EffectStackingRule
    {
        Unknown = 0,

        /// <summary>Every application creates an independent active instance.</summary>
        Stack,

        /// <summary>
        /// Reapplication refreshes the instance keyed by effect, source entity,
        /// target entity and op index without accumulating its magnitude again.
        /// </summary>
        Refresh
    }

    [Serializable]
    public sealed class EffectOpDef
    {
        public EffectOpCode Op { get; set; }

        public string Stat { get; set; } = string.Empty;

        public StatModifierMode Mode { get; set; }

        public float Value { get; set; }

        public EffectTarget Target { get; set; }

        /// <summary>-1 is permanent, 0 is instant, and positive values are seconds.</summary>
        public float Duration { get; set; }

        public string UnitId { get; set; } = string.Empty;

        public int Count { get; set; }

        public float Radius { get; set; }
    }

    [Serializable]
    public sealed class EffectDef
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Desc { get; set; } = string.Empty;

        public string Rarity { get; set; } = string.Empty;

        public EffectTrigger Trigger { get; set; }

        public EffectStackingRule Stacking { get; set; }

        public EffectOpDef[] Ops { get; set; } = Array.Empty<EffectOpDef>();
    }

    [Serializable]
    public sealed class DropCoinsDef
    {
        public int Normal { get; set; }

        public int Elite { get; set; }

        public int Boss { get; set; }
    }

    [Serializable]
    public sealed class DamageFormulaDef
    {
        private const int AttackTypeSlotCount = (int)AttackType.Siege + 1;
        private const int ArmorTypeSlotCount = (int)ArmorType.Building + 1;

        private TypeMultiplierDef[] typeMultipliers = Array.Empty<TypeMultiplierDef>();

        [NonSerialized]
        private float[] typeMultiplierLookup;

        public float ArmorScale { get; set; }

        public int MinimumDamage { get; set; }

        public float NeutralTypeMultiplier { get; set; }

        public TypeMultiplierDef[] TypeMultipliers
        {
            get => typeMultipliers;
            set
            {
                typeMultipliers = value;
                typeMultiplierLookup = null;
            }
        }

        internal void BuildTypeMultiplierLookup()
        {
            var lookup = new float[ArmorTypeSlotCount * AttackTypeSlotCount];
            for (int index = 0; index < lookup.Length; index++)
            {
                lookup[index] = float.NaN;
            }

            TypeMultiplierDef[] entries = typeMultipliers;
            if (entries != null)
            {
                for (int index = 0; index < entries.Length; index++)
                {
                    TypeMultiplierDef entry = entries[index];
                    if (entry == null)
                    {
                        continue;
                    }

                    int armorIndex = (int)entry.ArmorType;
                    int attackIndex = (int)entry.AtkType;
                    if (armorIndex <= (int)ArmorType.Unknown
                        || armorIndex >= ArmorTypeSlotCount
                        || attackIndex < (int)AttackType.Slash
                        || attackIndex >= AttackTypeSlotCount)
                    {
                        continue;
                    }

                    int lookupIndex = armorIndex * AttackTypeSlotCount + attackIndex;
                    if (float.IsNaN(lookup[lookupIndex]))
                    {
                        // GameConfig rejects duplicates. Keeping the first entry here preserves
                        // Formula's historical behavior for directly constructed test configs.
                        lookup[lookupIndex] = entry.Value;
                    }
                }
            }

            typeMultiplierLookup = lookup;
        }

        internal bool TryGetTypeMultiplier(AttackType atkType, ArmorType armorType, out float value)
        {
            if (typeMultiplierLookup == null)
            {
                BuildTypeMultiplierLookup();
            }

            int armorIndex = (int)armorType;
            int attackIndex = (int)atkType;
            if (armorIndex <= (int)ArmorType.Unknown
                || armorIndex >= ArmorTypeSlotCount
                || attackIndex < (int)AttackType.Slash
                || attackIndex >= AttackTypeSlotCount)
            {
                value = default;
                return false;
            }

            value = typeMultiplierLookup[armorIndex * AttackTypeSlotCount + attackIndex];
            return !float.IsNaN(value);
        }
    }

    [Serializable]
    public sealed class TypeMultiplierDef
    {
        public ArmorType ArmorType { get; set; }

        public AttackType AtkType { get; set; }

        public float Value { get; set; }
    }

    /// <summary>
    /// A footprint the unit card pool knows about. Availability is no longer a column threshold:
    /// a shape is offered exactly while the unlocked region still has a legal placement for it.
    /// </summary>
    [Serializable]
    public sealed class FootprintUnlockDef
    {
        public int GridW { get; set; }

        public int GridH { get; set; }
    }

    [Serializable]
    public sealed class CardPoolRulesDef
    {
        public FootprintUnlockDef[] ShapeUnlocks { get; set; } = Array.Empty<FootprintUnlockDef>();

        public int GuaranteeBeforeStageIndex { get; set; }

        public AttackType GuaranteeAttackType { get; set; }

        public string[] BuffEffectIds { get; set; } = Array.Empty<string>();

        public string[] GlobalEffectIds { get; set; } = Array.Empty<string>();
    }

    [Serializable]
    public sealed class CardOfferRulesDef
    {
        public int BaseCount { get; set; }

        public int LuckyExtraCount { get; set; }

        public float LuckyChance { get; set; }

        /// <summary>
        /// Free hands dealt in each minor stage, indexed by <c>stageIndex - 1</c> (WO-F1 §D).
        ///
        /// <para>Before this existed a minor stage got exactly one free hand and every further hand
        /// was a coin refresh, which made the number of preparation rounds a by-product of the coin
        /// balance — measured at 3/2/2/1/1, i.e. falling exactly where the field is widening. The
        /// curve makes the round count a value somebody chose. Stages past the array's end reuse its
        /// last entry, so a sixth minor stage cannot silently drop to zero hands.</para>
        /// </summary>
        public int[] FreeOffersPerMinorStage { get; set; } = Array.Empty<int>();

        /// <summary>Free hands for a minor stage, clamping past the end of the curve.</summary>
        public int FreeOffersForStage(int stageIndex)
        {
            int[] curve = FreeOffersPerMinorStage;
            if (curve == null || curve.Length == 0)
            {
                return 1;
            }

            int index = stageIndex - 1;
            if (index < 0)
            {
                index = 0;
            }
            else if (index >= curve.Length)
            {
                index = curve.Length - 1;
            }

            return curve[index];
        }
    }

    [Serializable]
    public sealed class CardWeightsDef
    {
        public int Unit { get; set; }

        /// <summary>Weight of the cell-unlock card category (formerly the column-expansion card).</summary>
        public int Unlock { get; set; }

        public int Buff { get; set; }

        public int Global { get; set; }
    }

    /// <summary>
    /// Deployment-screen presentation and interaction tuning. These values drive layout and drag
    /// feel only — no placement, merge or unlock rule reads them. Lengths are expressed in grid
    /// cell edges so the whole screen scales from one base measure.
    /// </summary>
    [Serializable]
    public sealed class DeployUiRulesDef
    {
        /// <summary>Gap between grid cells, as a fraction of one cell edge.</summary>
        public float CellSpacingRatio { get; set; }

        /// <summary>Hand card size relative to the grid's own cell edge.</summary>
        public float HandCardScale { get; set; }

        /// <summary>Cursor distance to a cell centre, in cell edges, that snaps the drag ghost.</summary>
        public float SnapRadiusCells { get; set; }

        /// <summary>How far above the cursor the drag ghost sits, in cell edges.</summary>
        public float DragLiftCells { get; set; }

        /// <summary>
        /// How far a cell's floor is inset inside its own tile, as a fraction of one cell edge. The
        /// tile rim left showing is what makes an empty slot read as a slot rather than as flat
        /// background, and it is also what stays visible around a unit card sitting in that slot.
        /// </summary>
        public float CellInsetRatio { get; set; }

        /// <summary>How far a deployed unit's card sits inside its footprint, in cell edges.</summary>
        public float UnitCardInsetRatio { get; set; }

        /// <summary>Thickness of a unit card's outline, in cell edges.</summary>
        public float UnitCardOutlineRatio { get; set; }

        /// <summary>Edge of the level badge in a card's bottom-left corner, in cell edges.</summary>
        public float LevelBadgeRatio { get; set; }

        /// <summary>
        /// Opacity of the level-coloured wash laid over a card's artwork. The artwork is the card's
        /// body, so this only has to be strong enough to read the level at a glance — push it up and
        /// the picture disappears again, which is the bug it replaced.
        /// </summary>
        public float UnitCardTintAlpha { get; set; }

        /// <summary>
        /// Largest footprint, in occupied cells, that still gets a written name on its card.
        ///
        /// <para>This unit's artwork <i>is</i> the character it is named after, so on a card big
        /// enough to show the artwork properly the label prints the same word twice — on 弩车 the
        /// gold strokes of the picture and the white strokes of the label interleave until neither
        /// reads. A 1x1 is the exception: the label covers the whole card and only a sliver of
        /// artwork survives behind it, so there the text is what identifies the unit.</para>
        /// </summary>
        public int NameLabelMaxFootprintCells { get; set; }

        /// <summary>
        /// Per-level palette, ordered by level. Deliberately longer than the merge ceiling: level 5
        /// is reserved for units obtained some other way later, so the table can grow without the
        /// merge rules moving. See <c>DeploymentGrid.MaximumUnitLevel</c> for the ceiling that
        /// actually binds.
        /// </summary>
        public TierColorDef[] TierColors { get; set; } = Array.Empty<TierColorDef>();
    }

    /// <summary>
    /// One level's colours, as <c>#RRGGBB</c> strings. They stay strings here because
    /// <c>Assets/Scripts/Data/</c> is engine-free by architecture rule; the view parses them.
    /// </summary>
    [Serializable]
    public sealed class TierColorDef
    {
        public int Level { get; set; }

        /// <summary>Display name of the colour, e.g. 绿 / 蓝 / 紫 / 金 / 红.</summary>
        public string Name { get; set; } = string.Empty;

        public string Fill { get; set; } = string.Empty;

        public string Border { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Rules for the three cell-unlock channels and for the per-stage automatic unlock.</summary>
    [Serializable]
    public sealed class GridUnlockRulesDef
    {
        public int PurchaseBaseCost { get; set; }

        public int PurchaseCostGrowth { get; set; }

        /// <summary>
        /// Row of the ally base anchor used to rank locked cells, expressed relative to grid row 0.
        /// Negative values sit in front of the grid, on the base's side.
        /// </summary>
        public float BaseAnchorRowOffset { get; set; }

        /// <summary>Cells unlocked automatically when a minor stage is cleared.</summary>
        public int AutoUnlockPerMinorStage { get; set; }
    }

    [Serializable]
    public sealed class SettlementRewardRulesDef
    {
        public int SlotCount { get; set; }

        public int BuffWeight { get; set; }

        public int ActiveSkillWeight { get; set; }

        public string[] BuffEffectIds { get; set; } = Array.Empty<string>();

        public string[] ActiveSkillEffectIds { get; set; } = Array.Empty<string>();

        public string[] UniqueEffectIds { get; set; } = Array.Empty<string>();
    }

    [Serializable]
    public sealed class Position2Def
    {
        public float X { get; set; }

        public float Y { get; set; }
    }

    [Serializable]
    public sealed class BattleRulesDef
    {
        public int TickRateHz { get; set; }

        public float RetargetInterval { get; set; }

        public float TargetSearchRadius { get; set; }

        public float ColliderRadius { get; set; }

        public float SameColumnTolerance { get; set; }

        public float SeparationDistance { get; set; }

        public Position2Def AllyBasePosition { get; set; }

        public Position2Def EnemyBasePosition { get; set; }

        public Position2Def EnemySpawnCenter { get; set; }

        public Position2Def DeploymentOriginOffset { get; set; }

        public Position2Def DeploymentCellSize { get; set; }

        /// <summary>
        /// World width the unlocked deployment columns fan out across at spawn time. Independent of
        /// how many columns are unlocked, so the front line always fills the base.
        /// </summary>
        public float DeploymentSpreadWidth { get; set; }

        /// <summary>
        /// How far up the field allies will advance while the castle has not appeared yet
        /// (WO-F1 §B). Zero disables the limit entirely.
        ///
        /// <para>Without it the whole ally line walks to the enemy's spawn door and fights there:
        /// measured enemy death height sat at Y=6.8 with a spawn centre of Y=6, i.e. enemies were
        /// dying essentially where they appeared, and the midfield the three-act pacing is built
        /// around never existed. The limit lifts the moment the castle spawns, because act three is
        /// supposed to be an assault; and <c>RushBase</c> units ignore it, because charging past the
        /// line is the entire point of a battering ram.</para>
        /// </summary>
        public float AllyAdvanceLimitY { get; set; }
    }

    /// <summary>How many of one unit a single deployment cell may keep on the field at once.</summary>
    [Serializable]
    public sealed class FootprintLiveCapDef
    {
        /// <summary>Occupied cells of the unit's footprint (L-shapes count 3, not 4).</summary>
        public int Cells { get; set; }

        public int Cap { get; set; }
    }

    /// <summary>
    /// Rules for treating a deployment cell as a barracks (WO-F3).
    ///
    /// <para>拆解文档 §36 asks for units to spawn "according to deployment and cooldown", and §38
    /// calls the intended feel bullet-hell with units dying easily. M1-00 §3.1 compressed that into
    /// "Delayed spawns when its cooldown expires", which reads as one-shot, and WO-F1 §B implemented
    /// the compressed sentence. These rules restore the missing half: a cell keeps producing.</para>
    /// </summary>
    [Serializable]
    public sealed class DeploymentRulesDef
    {
        /// <summary>
        /// Hard ceiling on ally units alive on the field at once, whatever the cells would produce.
        /// The performance gate is written against 200 units total and both sides now stream, so a
        /// board of barracks needs a ceiling that is a decision rather than an emergent accident.
        /// </summary>
        public int AllyFieldUnitLimit { get; set; }

        /// <summary>
        /// Simultaneous-alive cap per cell, by footprint size. Without it the units that never die
        /// because they stand at the back — 弓, 弩车 — stack without bound. With it, a big unit
        /// (cap 1) makes cooldown-driven and death-driven respawn the same thing.
        /// </summary>
        public FootprintLiveCapDef[] LiveCapByFootprintCells { get; set; }
            = Array.Empty<FootprintLiveCapDef>();

        /// <summary>Live cap for a footprint of the given size; falls back to the largest declared.</summary>
        public int LiveCapForCells(int cells)
        {
            FootprintLiveCapDef[] entries = LiveCapByFootprintCells;
            if (entries == null || entries.Length == 0)
            {
                return 1;
            }

            int best = entries[0].Cap;
            int bestCells = entries[0].Cells;
            for (int index = 0; index < entries.Length; index++)
            {
                FootprintLiveCapDef entry = entries[index];
                if (entry.Cells == cells)
                {
                    return entry.Cap;
                }

                if (entry.Cells < cells && entry.Cells >= bestCells)
                {
                    bestCells = entry.Cells;
                    best = entry.Cap;
                }
            }

            return best;
        }
    }

    [Serializable]
    public sealed class EconomyDef
    {
        public DeploymentRulesDef Deployment { get; set; } = new DeploymentRulesDef();

        public int RefreshBaseCost { get; set; }

        public int RefreshCostGrowth { get; set; }

        public GridUnlockRulesDef GridUnlock { get; set; } = new GridUnlockRulesDef();

        public DeployUiRulesDef DeployUi { get; set; } = new DeployUiRulesDef();

        public DropCoinsDef DropCoins { get; set; } = new DropCoinsDef();

        public DamageFormulaDef Damage { get; set; } = new DamageFormulaDef();

        public CardWeightsDef CardWeights { get; set; } = new CardWeightsDef();

        public CardOfferRulesDef CardOffer { get; set; } = new CardOfferRulesDef();

        public CardPoolRulesDef CardPool { get; set; } = new CardPoolRulesDef();

        public SettlementRewardRulesDef SettlementReward { get; set; } = new SettlementRewardRulesDef();

        public BattleRulesDef Battle { get; set; } = new BattleRulesDef();
    }
}
