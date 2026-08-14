using System;
using System.Collections.Generic;
using System.Globalization;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using HanziDefend.View.Pooling;
using UnityEngine;

namespace HanziDefend.View
{
    public enum BattleReviewStage
    {
        None,
        Opening,
        Mid,
        Castle,
        Settlement
    }

    public interface IBattleViewCommands
    {
        bool AutoRun { get; set; }

        float CommanderCooldownRemaining { get; }

        bool TryActivateCommander();

        void StepOnce();
    }

    public sealed class BattleHudState
    {
        public int WaveIndex { get; internal set; }
        public int WaveTotal { get; internal set; }
        public int Coins { get; internal set; }
        public float CommanderCooldown { get; internal set; }
        public float AllyBaseHealthRatio { get; internal set; } = 1f;
        public string CommanderId { get; internal set; } = string.Empty;
        public string Status { get; internal set; } = "整军待发";
        public bool AutoRun { get; internal set; } = true;
        public IReadOnlyList<string> DeployedUnitIds => deployedUnitIds;

        internal readonly List<string> deployedUnitIds = new List<string>();
    }

    public readonly struct BattleVisualDebug
    {
        public BattleVisualDebug(
            int entityId,
            string definitionId,
            BattleTeam team,
            Vector2 displaySize,
            Vector2 position,
            Vector2 targetPosition,
            int sortingOrder,
            float healthRatio,
            float healthBarAlpha,
            bool alwaysShowHealth,
            bool isDying,
            bool isFlashing,
            Sprite sprite)
        {
            EntityId = entityId;
            DefinitionId = definitionId;
            Team = team;
            DisplaySize = displaySize;
            Position = position;
            TargetPosition = targetPosition;
            SortingOrder = sortingOrder;
            HealthRatio = healthRatio;
            HealthBarAlpha = healthBarAlpha;
            AlwaysShowHealth = alwaysShowHealth;
            IsDying = isDying;
            IsFlashing = isFlashing;
            Sprite = sprite;
        }

        public int EntityId { get; }
        public string DefinitionId { get; }
        public BattleTeam Team { get; }
        public Vector2 DisplaySize { get; }
        public Vector2 Position { get; }
        public Vector2 TargetPosition { get; }
        public int SortingOrder { get; }
        public float HealthRatio { get; }
        public float HealthBarAlpha { get; }
        public bool AlwaysShowHealth { get; }
        public bool IsDying { get; }
        public bool IsFlashing { get; }
        public Sprite Sprite { get; }
    }

    public readonly struct CombatNumberDebug
    {
        public CombatNumberDebug(
            int targetEntityId,
            long accumulatedAmount,
            bool isHealing,
            Vector2 position,
            Vector2 halfExtents,
            int glyphCount,
            int sortingOrder)
        {
            TargetEntityId = targetEntityId;
            AccumulatedAmount = accumulatedAmount;
            IsHealing = isHealing;
            Position = position;
            HalfExtents = halfExtents;
            GlyphCount = glyphCount;
            SortingOrder = sortingOrder;
        }

        public int TargetEntityId { get; }
        public long AccumulatedAmount { get; }
        public bool IsHealing { get; }
        public Vector2 Position { get; }
        public Vector2 HalfExtents { get; }
        public int GlyphCount { get; }
        public int SortingOrder { get; }
    }

    /// <summary>
    /// Event-driven presentation for the deterministic battle kernel. This class contains
    /// interpolation and transient visual timing only; combat authority remains in BattleSystem.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleView : MonoBehaviour, IBattleEncounterEvents, IBattleEffectEvents
    {
        public const float GridCellWorldSize = 0.72f;
        public const float HealthBarVisibleSeconds = 2f;
        public const float BattlefieldWidth = 8.8f;
        public const float BattlefieldHeight = 16.4f;
        public const float ReviewCaptureFallbackSeconds = 0.5f;
        public const int MaxActiveCombatNumbers = 8;
        public const int MaxBattleSortingOrder = 30000;

        private const float PositionSharpness = 18f;
        private const float FlashSeconds = 0.09f;
        private const float DeathFadeSeconds = 0.42f;
        private const float CombatNumberSeconds = 0.28f;
        private const float CombatNumberAggregationSeconds = 0.10f;
        private const float HealthFadeSeconds = 0.35f;
        private const float HealthBarHeadroom = 0.22f;
        private const int EntitySortingBase = 1000;
        private const int EntitySortingStride = 4;
        private const int CombatNumberSortingBase = 20000;
        private const int MidReviewWave = 10;

        private static Sprite whiteSprite;
        private readonly Dictionary<int, EntityVisual> visuals = new Dictionary<int, EntityVisual>();
        private readonly List<int> trackedUnitEntityIds = new List<int>();
        private readonly Stack<EntityVisual> visualPool = new Stack<EntityVisual>();
        private readonly List<EntityVisual> recycleBuffer = new List<EntityVisual>();
        private readonly List<EntityVisual> sortingBuffer = new List<EntityVisual>();
        private readonly List<CombatNumberVisual> combatNumbers = new List<CombatNumberVisual>();
        private readonly Stack<CombatNumberVisual> combatNumberPool = new Stack<CombatNumberVisual>();
        private readonly Dictionary<CombatNumberKey, CombatNumberVisual> combatNumbersByTarget =
            new Dictionary<CombatNumberKey, CombatNumberVisual>();
        private readonly Queue<BattleReviewStage> pendingReviewStages = new Queue<BattleReviewStage>();
        private readonly HashSet<BattleReviewStage> queuedReviewStages = new HashSet<BattleReviewStage>();
        private readonly Dictionary<string, UnitDef> unitDefinitions = new Dictionary<string, UnitDef>(StringComparer.Ordinal);

        private GameConfig config;
        private IBattleArtSource artSource;
        private IBattleViewCommands commands;
        private BattleSystem system;
        private Transform battlefieldRoot;
        private Transform effectRoot;
        private SpriteFxPool spriteFxPool;
        private Sprite hitFxSprite;
        private Sprite deathFxSprite;
        private Sprite freezeFxSprite;
        private float presentationTime;
        private long presentationAdvanceIndex;
        private long lastCastleAdvanceIndex = -1;
        private float pendingSettlementReleaseTime;
        private int createdVisualCount;
        private int createdCombatNumberCount;
        private Sprite commanderAvatar;
        private GUIStyle headerStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;
        private BattleReviewStage currentReviewStage;
        private bool settlementStageWaitingForAdvance;
        private string pendingSettlementStatus;
        private bool canvasHudAttached;

        public int ActiveVisualCount => visuals.Count;
        public int CreatedVisualCount => createdVisualCount;
        public int PooledVisualCount => visualPool.Count;
        public int ActiveCombatNumberCount => combatNumbers.Count;
        public int CreatedCombatNumberCount => createdCombatNumberCount;
        public int ActiveFxCount => spriteFxPool?.ActiveCount ?? 0;
        public int CreatedFxCount => spriteFxPool?.CreatedCount ?? 0;
        public BattleHudState HudState { get; } = new BattleHudState();
        public BattleReviewStage CurrentReviewStage => currentReviewStage;
        public float PresentationTime => presentationTime;
        public static Rect BattlefieldWorldBounds => new Rect(
            -BattlefieldWidth * 0.5f,
            -BattlefieldHeight * 0.5f,
            BattlefieldWidth,
            BattlefieldHeight);

        public void SetCanvasHudAttached(bool value)
        {
            canvasHudAttached = value;
        }

        public void Initialize(GameConfig gameConfig, IBattleArtSource battleArtSource, IBattleViewCommands commandSink)
        {
            config = gameConfig ?? throw new ArgumentNullException(nameof(gameConfig));
            artSource = battleArtSource ?? throw new ArgumentNullException(nameof(battleArtSource));
            commands = commandSink;

            unitDefinitions.Clear();
            for (var index = 0; index < config.Units.Count; index++)
            {
                UnitDef definition = config.Units[index];
                unitDefinitions[definition.Id] = definition;
            }

            battlefieldRoot = CreateChild("Battlefield Views");
            effectRoot = CreateChild("Battle Effects");
            int fxCapacity = MaxActiveCombatNumbers * MaxActiveCombatNumbers;
            spriteFxPool = new SpriteFxPool(effectRoot, fxCapacity, fxCapacity);
            hitFxSprite = artSource.Find("fx/ci_bao");
            deathFxSprite = artSource.Find("fx/bao_zha_03");
            freezeFxSprite = artSource.Find("fx/shuang_dong_03");
            BuildBattlefieldBackdrop();

            HudState.WaveTotal = config.GetWaveSet(config.GetLevel("level_1_1").WaveSetId).Waves.Length;
            HudState.Coins = config.GetLevel("level_1_1").StartCoins;
            if (config.Commanders.Count > 0)
            {
                CommanderDef commander = config.Commanders[0];
                HudState.CommanderId = commander.Id;
                commanderAvatar = artSource.Find($"commander/{commander.Id}/avatar");
            }

            SetReviewStage(BattleReviewStage.Opening);
        }

        public void BindSystem(BattleSystem battleSystem)
        {
            system = battleSystem ?? throw new ArgumentNullException(nameof(battleSystem));
            HudState.WaveTotal = system.TotalWaveCount;
            HudState.Coins = system.Coins;
            AddBase(system.GetBaseSnapshot(system.AllyBaseEntityId), true);
            AddBase(system.GetBaseSnapshot(system.EnemyBaseEntityId), false);
        }

        /// <summary>
        /// Recycles presentation state before gameplay publishes the next encounter's spawn
        /// events. Entity ids restart per BattleSystem, so stale visuals must never survive
        /// across minor stages.
        /// </summary>
        public void PrepareForEncounter()
        {
            system = null;
            recycleBuffer.Clear();
            foreach (EntityVisual visual in visuals.Values)
            {
                recycleBuffer.Add(visual);
            }
            for (var index = 0; index < recycleBuffer.Count; index++)
            {
                Recycle(recycleBuffer[index]);
            }
            recycleBuffer.Clear();
            trackedUnitEntityIds.Clear();

            for (var index = combatNumbers.Count - 1; index >= 0; index--)
            {
                RecycleCombatNumberAt(index);
            }
            combatNumbersByTarget.Clear();
            spriteFxPool?.RecycleAll();

            HudState.WaveIndex = 0;
            HudState.WaveTotal = 0;
            HudState.AllyBaseHealthRatio = 1f;
            HudState.Status = "整军待发";
            HudState.deployedUnitIds.Clear();
            pendingReviewStages.Clear();
            queuedReviewStages.Clear();
            settlementStageWaitingForAdvance = false;
            pendingSettlementStatus = null;
            pendingSettlementReleaseTime = 0f;
            lastCastleAdvanceIndex = -1;
            SetReviewStage(BattleReviewStage.Opening);
        }

        public static Vector2 CalculateDisplaySize(UnitDef definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return new Vector2(definition.GridW * GridCellWorldSize, definition.GridH * GridCellWorldSize);
        }

        public Vector2 GetDefinitionDisplaySize(string definitionId)
        {
            if (!unitDefinitions.TryGetValue(definitionId, out UnitDef definition))
            {
                throw new KeyNotFoundException($"Unknown unit definition '{definitionId}'.");
            }

            return CalculateDisplaySize(definition);
        }

        public bool TryGetVisualDebug(int entityId, out BattleVisualDebug debug)
        {
            if (visuals.TryGetValue(entityId, out EntityVisual visual))
            {
                debug = visual.ToDebug();
                return true;
            }

            debug = default;
            return false;
        }

        public bool TryGetCombatNumberDebug(
            int targetEntityId,
            bool isHealing,
            out CombatNumberDebug debug)
        {
            var key = new CombatNumberKey(targetEntityId, isHealing);
            if (combatNumbersByTarget.TryGetValue(key, out CombatNumberVisual visual))
            {
                debug = visual.ToDebug();
                return true;
            }

            debug = default;
            return false;
        }

        public bool TryDequeueReviewStage(out BattleReviewStage stage)
        {
            if (pendingReviewStages.Count > 0)
            {
                stage = pendingReviewStages.Dequeue();
                return true;
            }

            stage = BattleReviewStage.None;
            return false;
        }

        public void AcknowledgeReviewStage(BattleReviewStage stage)
        {
            if (stage == BattleReviewStage.Castle && settlementStageWaitingForAdvance)
            {
                PublishPendingSettlementStage();
            }
        }

        public void SyncSnapshots(IEnumerable<BattleUnitSnapshot> snapshots)
        {
            if (snapshots == null)
            {
                throw new ArgumentNullException(nameof(snapshots));
            }

            foreach (BattleUnitSnapshot snapshot in snapshots)
            {
                if (!visuals.TryGetValue(snapshot.EntityId, out EntityVisual visual))
                {
                    continue;
                }

                visual.SetTargetPosition(snapshot.Position);
                visual.SetFacing(snapshot.Facing);
                visual.SetHealth(snapshot.CurrentHp, snapshot.Stats.MaxHp, false, presentationTime);
            }
        }

        public void SyncFromBoundSystem()
        {
            if (system == null)
            {
                return;
            }

            for (var index = 0; index < trackedUnitEntityIds.Count; index++)
            {
                int entityId = trackedUnitEntityIds[index];
                BattleUnitSnapshot snapshot = system.GetUnitSnapshot(entityId);
                if (visuals.TryGetValue(entityId, out EntityVisual visual))
                {
                    visual.SetTargetPosition(snapshot.Position);
                    visual.SetFacing(snapshot.Facing);
                    visual.SetHealth(snapshot.CurrentHp, snapshot.Stats.MaxHp, false, presentationTime);
                }
            }
        }

        public bool RequestCommanderActivation()
        {
            return commands != null && commands.TryActivateCommander();
        }

        public void ToggleAutoRun()
        {
            if (commands != null)
            {
                commands.AutoRun = !commands.AutoRun;
                HudState.AutoRun = commands.AutoRun;
            }
        }

        public void RequestSingleStep()
        {
            commands?.StepOnce();
        }

        public void AdvancePresentation(float dt)
        {
            float safeDelta = Mathf.Max(0f, dt);
            presentationTime += safeDelta;
            presentationAdvanceIndex++;
            if (settlementStageWaitingForAdvance
                && presentationTime >= pendingSettlementReleaseTime)
            {
                PublishPendingSettlementStage();
            }

            if (commands != null)
            {
                HudState.CommanderCooldown = commands.CommanderCooldownRemaining;
                HudState.AutoRun = commands.AutoRun;
            }

            recycleBuffer.Clear();
            foreach (EntityVisual visual in visuals.Values)
            {
                visual.Advance(safeDelta, presentationTime);
                if (visual.ReadyToRecycle)
                {
                    recycleBuffer.Add(visual);
                }
            }

            for (var index = 0; index < recycleBuffer.Count; index++)
            {
                Recycle(recycleBuffer[index]);
            }

            ApplyDeterministicSorting();

            for (var index = combatNumbers.Count - 1; index >= 0; index--)
            {
                CombatNumberVisual combatNumber = combatNumbers[index];
                combatNumber.Advance(safeDelta);
                if (combatNumber.RemainingSeconds <= 0f)
                {
                    RecycleCombatNumberAt(index);
                }
            }

            spriteFxPool?.Advance(safeDelta);
        }

        public void UnitSpawned(UnitSpawnedEvent eventData)
        {
            Vector2 size = ResolveDisplaySize(eventData.DefinitionId);
            Sprite sprite = ResolveUnitSprite(eventData.DefinitionId, eventData.Team);
            EntityVisual visual = Rent(
                eventData.EntityId,
                eventData.DefinitionId,
                eventData.Team,
                sprite,
                size,
                eventData.Position,
                eventData.CurrentHp,
                eventData.Stats.MaxHp,
                false);
            visual.SetFacing(eventData.Facing);
            trackedUnitEntityIds.Add(eventData.EntityId);

            if (eventData.Team == BattleTeam.Ally)
            {
                HudState.deployedUnitIds.Add(eventData.DefinitionId);
            }
        }

        public void UnitAttacked(UnitAttackedEvent eventData)
        {
            if (visuals.TryGetValue(eventData.AttackerEntityId, out EntityVisual attacker))
            {
                attacker.SetFacing(eventData.Facing);
            }
        }

        public void DamageDealt(DamageDealtEvent eventData)
        {
            if (visuals.TryGetValue(eventData.TargetEntityId, out EntityVisual target))
            {
                target.SetHealth(eventData.HpAfter, eventData.MaxHp, true, presentationTime);
                target.Flash(FlashSeconds);
            }

            ShowCombatNumber(
                eventData.TargetEntityId,
                eventData.Amount,
                false,
                eventData.Sequence,
                eventData.HitPosition,
                new Color(1f, 0.82f, 0.25f));

            PlayFx(hitFxSprite, eventData.HitPosition, FlashSeconds);
        }

        public void UnitDied(UnitDiedEvent eventData)
        {
            if (visuals.TryGetValue(eventData.EntityId, out EntityVisual visual))
            {
                visual.BeginDeath(DeathFadeSeconds);
            }

            PlayFx(deathFxSprite, eventData.Position, DeathFadeSeconds);
        }

        public void CoinDropped(CoinDroppedEvent eventData)
        {
            HudState.Coins = system == null ? eventData.TotalCoins : system.Coins;
        }

        public void WaveStarted(WaveStartedEvent eventData)
        {
            HudState.WaveIndex = eventData.WaveIndex;
            HudState.Status = $"第 {eventData.WaveIndex} 波";
            if (eventData.WaveIndex == MidReviewWave)
            {
                SetReviewStage(BattleReviewStage.Mid);
            }
        }

        public void BossSpawned(BossSpawnedEvent eventData)
        {
            if (visuals.TryGetValue(eventData.EntityId, out EntityVisual visual))
            {
                Sprite castle = artSource.Find($"building/{eventData.DefinitionId}");
                visual.ConfigureArtwork(castle ?? visual.Sprite, BuildingDisplaySize(), whiteSprite);
                visual.AlwaysShowHealth = true;
            }

            HudState.Status = "敌方城堡登场";
            lastCastleAdvanceIndex = presentationAdvanceIndex;
            SetReviewStage(BattleReviewStage.Castle);
        }

        public void BaseDamaged(BaseDamagedEvent eventData)
        {
            if (visuals.TryGetValue(eventData.BaseEntityId, out EntityVisual visual))
            {
                visual.SetHealth(eventData.HpAfter, eventData.MaxHp, true, presentationTime);
                visual.Flash(FlashSeconds);
            }

            if (eventData.BaseTeam == BattleTeam.Ally)
            {
                HudState.AllyBaseHealthRatio = NormalizeHealth(eventData.HpAfter, eventData.MaxHp);
            }

            ShowCombatNumber(
                eventData.BaseEntityId,
                eventData.Amount,
                false,
                eventData.Sequence,
                eventData.Position,
                new Color(1f, 0.62f, 0.25f));
        }

        public void BattleSettled(BattleSettledEvent eventData)
        {
            string settlementStatus = $"战斗结果：{eventData.Result}";
            bool deferForCastleCapture = presentationAdvanceIndex == lastCastleAdvanceIndex;
            if (deferForCastleCapture)
            {
                pendingSettlementStatus = settlementStatus;
                settlementStageWaitingForAdvance = true;
                pendingSettlementReleaseTime = presentationTime + ReviewCaptureFallbackSeconds;
            }
            else
            {
                HudState.Status = settlementStatus;
                SetReviewStage(BattleReviewStage.Settlement);
            }
        }

        public void EffectApplied(EffectAppliedEvent eventData)
        {
            if (visuals.TryGetValue(eventData.TargetEntityId, out EntityVisual visual))
            {
                visual.Pulse(new Color(0.5f, 0.9f, 1f), FlashSeconds);
                if (string.Equals(
                        eventData.EffectId,
                        BattleSystem.IceAuraPresentationEffectId,
                        StringComparison.Ordinal))
                {
                    PlayFx(freezeFxSprite, visual.ToDebug().Position, DeathFadeSeconds);
                }
            }
        }

        public void EffectExpired(EffectExpiredEvent eventData)
        {
        }

        public void UnitHealed(UnitHealedEvent eventData)
        {
            if (visuals.TryGetValue(eventData.TargetEntityId, out EntityVisual visual))
            {
                visual.SetHealth(eventData.HpAfter, eventData.MaxHp, true, presentationTime);
                visual.Pulse(new Color(0.35f, 1f, 0.55f), FlashSeconds);
            }

            ShowCombatNumber(
                eventData.TargetEntityId,
                Mathf.RoundToInt(eventData.AppliedAmount),
                true,
                eventData.Sequence,
                ResolveEntityPosition(eventData.TargetEntityId),
                new Color(0.35f, 1f, 0.55f));
        }

        public void ShieldChanged(ShieldChangedEvent eventData)
        {
            if (visuals.TryGetValue(eventData.TargetEntityId, out EntityVisual visual))
            {
                visual.Pulse(new Color(0.35f, 0.8f, 1f), FlashSeconds);
            }
        }

        public void CoinsModified(CoinsModifiedEvent eventData)
        {
            HudState.Coins = eventData.TotalCoins;
        }

        private void Update()
        {
            AdvancePresentation(Time.deltaTime);
        }

        private void OnGUI()
        {
            if (canvasHudAttached || config == null)
            {
                return;
            }

            EnsureGuiStyles();
            DrawTopHud();
            DrawBottomHud();
        }

        private void OnDestroy()
        {
            spriteFxPool?.Dispose();
            spriteFxPool = null;
            visuals.Clear();
            visualPool.Clear();
            combatNumbers.Clear();
            combatNumberPool.Clear();
            combatNumbersByTarget.Clear();
            pendingReviewStages.Clear();
            queuedReviewStages.Clear();
        }

        private void PlayFx(Sprite sprite, Vector2 position, float lifetimeSeconds)
        {
            if (sprite == null || spriteFxPool == null)
            {
                return;
            }

            spriteFxPool.Play(new SpriteFxRequest(
                sprite,
                ClampToBattlefield(position, Vector2.zero),
                lifetimeSeconds,
                Color.white,
                Color.clear,
                Vector2.one,
                Vector2.one,
                CombatNumberSortingBase - EntitySortingStride));
        }

        private Transform CreateChild(string childName)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            return child.transform;
        }

        private void BuildBattlefieldBackdrop()
        {
            SpriteRenderer backdrop = CreateRenderer("Placeholder Battlefield", battlefieldRoot);
            backdrop.sprite = WhiteSprite();
            backdrop.color = new Color(0.11f, 0.13f, 0.16f, 1f);
            backdrop.transform.localScale = new Vector3(10.8f, 19.2f, 1f);
            backdrop.sortingOrder = -10000;

            SpriteRenderer lane = CreateRenderer("Battle Lane", battlefieldRoot);
            lane.sprite = WhiteSprite();
            lane.color = new Color(0.35f, 0.31f, 0.22f, 0.55f);
            lane.transform.localScale = new Vector3(BattlefieldWidth, BattlefieldHeight, 1f);
            lane.sortingOrder = -9999;
        }

        private void AddBase(BattleBaseSnapshot snapshot, bool isCamp)
        {
            Vector2 size = isCamp ? BuildingDisplaySize() : Vector2.one * GridCellWorldSize;
            Sprite sprite = artSource.Find($"building/{snapshot.DefinitionId}");
            EntityVisual visual = Rent(
                snapshot.EntityId,
                snapshot.DefinitionId,
                snapshot.Team,
                sprite,
                size,
                snapshot.Position,
                snapshot.CurrentHp,
                snapshot.MaxHp,
                false);
            if (isCamp)
            {
                HudState.AllyBaseHealthRatio = NormalizeHealth(snapshot.CurrentHp, snapshot.MaxHp);
            }

            visual.SetFacing(snapshot.Team == BattleTeam.Ally ? Vector2.up : Vector2.down);
        }

        private EntityVisual Rent(
            int entityId,
            string definitionId,
            BattleTeam team,
            Sprite sprite,
            Vector2 displaySize,
            Vector2 position,
            float currentHp,
            float maxHp,
            bool alwaysShowHealth)
        {
            EntityVisual visual;
            if (visualPool.Count > 0)
            {
                visual = visualPool.Pop();
            }
            else
            {
                visual = EntityVisual.Create(battlefieldRoot, WhiteSprite());
                createdVisualCount++;
            }

            visual.Activate(
                entityId,
                definitionId,
                team,
                sprite ?? WhiteSprite(),
                displaySize,
                position,
                NormalizeHealth(currentHp, maxHp),
                alwaysShowHealth,
                presentationTime);
            visuals[entityId] = visual;
            return visual;
        }

        private void Recycle(EntityVisual visual)
        {
            visuals.Remove(visual.EntityId);
            trackedUnitEntityIds.Remove(visual.EntityId);
            visual.GameObject.SetActive(false);
            visualPool.Push(visual);
        }

        private void ShowCombatNumber(
            int targetEntityId,
            int amount,
            bool isHealing,
            long eventSequence,
            Vector2 position,
            Color color)
        {
            var key = new CombatNumberKey(targetEntityId, isHealing);
            if (combatNumbersByTarget.TryGetValue(key, out CombatNumberVisual existing)
                && existing.CanAggregate(presentationTime))
            {
                existing.Accumulate(amount, position, color, presentationTime, CombatNumberSeconds);
                return;
            }

            if (combatNumbers.Count >= MaxActiveCombatNumbers)
            {
                RecycleCombatNumberAt(0);
            }

            CombatNumberVisual visual;
            if (combatNumberPool.Count > 0)
            {
                visual = combatNumberPool.Pop();
            }
            else
            {
                visual = CombatNumberVisual.Create(
                    effectRoot,
                    createdCombatNumberCount,
                    CombatNumberSortingBase + createdCombatNumberCount);
                createdCombatNumberCount++;
            }

            visual.Activate(
                key,
                amount,
                eventSequence,
                position,
                color,
                presentationTime,
                CombatNumberSeconds,
                CombatNumberAggregationSeconds);
            combatNumbers.Add(visual);
            combatNumbersByTarget[key] = visual;
        }

        private void RecycleCombatNumberAt(int index)
        {
            CombatNumberVisual visual = combatNumbers[index];
            combatNumbers.RemoveAt(index);
            if (combatNumbersByTarget.TryGetValue(visual.Key, out CombatNumberVisual mapped)
                && ReferenceEquals(mapped, visual))
            {
                combatNumbersByTarget.Remove(visual.Key);
            }

            visual.GameObject.SetActive(false);
            combatNumberPool.Push(visual);
        }

        private Vector2 ResolveEntityPosition(int entityId)
        {
            return visuals.TryGetValue(entityId, out EntityVisual visual)
                ? (Vector2)visual.GameObject.transform.position
                : Vector2.zero;
        }

        private Vector2 ResolveDisplaySize(string definitionId)
        {
            return unitDefinitions.TryGetValue(definitionId, out UnitDef definition)
                ? CalculateDisplaySize(definition)
                : BuildingDisplaySize();
        }

        private Sprite ResolveUnitSprite(string definitionId, BattleTeam team)
        {
            string suffix = team == BattleTeam.Enemy
                            && unitDefinitions.TryGetValue(definitionId, out UnitDef definition)
                            && definition.Faction == UnitFaction.Ally
                ? "_enemy"
                : string.Empty;
            Sprite result = artSource.Find($"unit/{definitionId}{suffix}/idle");
            return result ?? artSource.Find($"building/{definitionId}");
        }

        private static Vector2 BuildingDisplaySize()
        {
            return new Vector2(GridCellWorldSize * 4f, GridCellWorldSize * 2f);
        }

        public static Vector2 ClampToBattlefield(Vector2 position, Vector2 halfExtents)
        {
            Rect bounds = BattlefieldWorldBounds;
            float safeHalfWidth = Mathf.Clamp(halfExtents.x, 0f, bounds.width * 0.5f);
            float safeHalfHeight = Mathf.Clamp(halfExtents.y, 0f, bounds.height * 0.5f);
            return new Vector2(
                Mathf.Clamp(position.x, bounds.xMin + safeHalfWidth, bounds.xMax - safeHalfWidth),
                Mathf.Clamp(position.y, bounds.yMin + safeHalfHeight, bounds.yMax - safeHalfHeight));
        }

        private static Vector2 ClampEntityPosition(Vector2 position, Vector2 displaySize)
        {
            return ClampToBattlefield(
                position,
                new Vector2(displaySize.x * 0.5f, displaySize.y * 0.5f + HealthBarHeadroom));
        }

        private static float NormalizeHealth(float current, float maximum)
        {
            return Mathf.Clamp01(current / Mathf.Max(Mathf.Epsilon, maximum));
        }

        private void SetReviewStage(BattleReviewStage stage, bool enqueue = true)
        {
            bool changed = currentReviewStage != stage;
            currentReviewStage = stage;
            if (enqueue)
            {
                EnqueueReviewStage(stage);
            }

            if (changed)
            {
                Debug.Log($"[WO-B4 Review] {stage} @ presentation {presentationTime:F2}s");
            }
        }

        private void EnqueueReviewStage(BattleReviewStage stage)
        {
            if (stage == BattleReviewStage.None || !queuedReviewStages.Add(stage))
            {
                return;
            }

            pendingReviewStages.Enqueue(stage);
        }

        private void PublishPendingSettlementStage()
        {
            if (!settlementStageWaitingForAdvance)
            {
                return;
            }

            settlementStageWaitingForAdvance = false;
            HudState.Status = pendingSettlementStatus ?? HudState.Status;
            pendingSettlementStatus = null;
            SetReviewStage(BattleReviewStage.Settlement);
        }

        private void ApplyDeterministicSorting()
        {
            sortingBuffer.Clear();
            foreach (EntityVisual visual in visuals.Values)
            {
                sortingBuffer.Add(visual);
            }

            sortingBuffer.Sort(EntityVisual.CompareBackToFront);
            for (var index = 0; index < sortingBuffer.Count; index++)
            {
                int order = Mathf.Min(
                    EntitySortingBase + index * EntitySortingStride,
                    MaxBattleSortingOrder - EntitySortingStride);
                sortingBuffer[index].SetSortingOrder(order);
            }
        }

        private static SpriteRenderer CreateRenderer(string objectName, Transform parent)
        {
            var gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            return gameObject.AddComponent<SpriteRenderer>();
        }

        private static Sprite WhiteSprite()
        {
            if (whiteSprite != null)
            {
                return whiteSprite;
            }

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "HanziDefend Runtime White",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            whiteSprite.name = "HanziDefend Runtime White";
            whiteSprite.hideFlags = HideFlags.HideAndDontSave;
            return whiteSprite;
        }

        private void EnsureGuiStyles()
        {
            if (headerStyle != null)
            {
                return;
            }

            int baseSize = Mathf.Max(14, Screen.height / 58);
            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = baseSize + 4,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = baseSize,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            smallStyle = new GUIStyle(bodyStyle)
            {
                fontSize = Mathf.Max(12, baseSize - 4),
                alignment = TextAnchor.MiddleCenter
            };
        }

        private void DrawTopHud()
        {
            float margin = Screen.width * 0.025f;
            float height = Screen.height * 0.105f;
            var panel = new Rect(margin, margin, Screen.width - margin * 2f, height);
            DrawPanel(panel, new Color(0.04f, 0.05f, 0.07f, 0.88f));

            float iconSize = height * 0.72f;
            var avatarRect = new Rect(panel.x + 12f, panel.y + (height - iconSize) * 0.5f, iconSize, iconSize);
            DrawSprite(avatarRect, commanderAvatar, new Color(0.2f, 0.42f, 0.75f));

            float labelX = avatarRect.xMax + 10f;
            float contentWidth = panel.width - iconSize - 190f;
            GUI.Label(new Rect(labelX, panel.y + 5f, contentWidth, height * 0.46f),
                $"波次 {HudState.WaveIndex}/{HudState.WaveTotal}    金币 {HudState.Coins}", headerStyle);
            GUI.Label(new Rect(labelX, panel.y + height * 0.49f, contentWidth, height * 0.42f), HudState.Status, smallStyle);

            var skillRect = new Rect(panel.xMax - 164f, panel.y + 10f, 74f, height - 20f);
            string cooldown = HudState.CommanderCooldown > 0.05f
                ? $"{HudState.CommanderCooldown:F1}s"
                : "主动";
            if (GUI.Button(skillRect, cooldown) && commands != null)
            {
                RequestCommanderActivation();
            }

            var autoRect = new Rect(panel.xMax - 84f, panel.y + 10f, 74f, (height - 24f) * 0.5f);
            if (GUI.Button(autoRect, HudState.AutoRun ? "自动出兵" : "手动出兵") && commands != null)
            {
                ToggleAutoRun();
            }

            var stepRect = new Rect(autoRect.x, autoRect.yMax + 4f, autoRect.width, autoRect.height);
            if (GUI.Button(stepRect, "单步") && commands != null)
            {
                RequestSingleStep();
            }
        }

        private void DrawBottomHud()
        {
            float margin = Screen.width * 0.025f;
            float height = Screen.height * 0.095f;
            var panel = new Rect(margin, Screen.height - height - margin, Screen.width - margin * 2f, height);
            DrawPanel(panel, new Color(0.04f, 0.05f, 0.07f, 0.88f));

            GUI.Label(new Rect(panel.x + 12f, panel.y + 4f, 120f, 26f), "我方营地", bodyStyle);
            var barRect = new Rect(panel.x + 12f, panel.y + 34f, 154f, 18f);
            GUI.DrawTexture(barRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.08f, 0.1f, 0.13f), 0f, 4f);
            var fillRect = new Rect(barRect.x, barRect.y, barRect.width * HudState.AllyBaseHealthRatio, barRect.height);
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.18f, 0.55f, 1f), 0f, 4f);

            float thumbnailSize = height * 0.62f;
            float x = panel.x + 190f;
            for (var index = 0; index < HudState.deployedUnitIds.Count && x + thumbnailSize < panel.xMax - 8f; index++)
            {
                string definitionId = HudState.deployedUnitIds[index];
                Sprite thumbnail = artSource.Find($"unit/{definitionId}/idle");
                DrawSprite(new Rect(x, panel.y + (height - thumbnailSize) * 0.5f, thumbnailSize, thumbnailSize), thumbnail,
                    new Color(0.18f, 0.45f, 0.78f));
                x += thumbnailSize + 6f;
            }
        }

        private static void DrawPanel(Rect rect, Color color)
        {
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, 8f);
        }

        private static void DrawSprite(Rect rect, Sprite sprite, Color fallbackColor)
        {
            if (sprite == null || sprite.texture == null)
            {
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, fallbackColor, 0f, 5f);
                return;
            }

            Rect textureRect = sprite.textureRect;
            var uv = new Rect(
                textureRect.x / sprite.texture.width,
                textureRect.y / sprite.texture.height,
                textureRect.width / sprite.texture.width,
                textureRect.height / sprite.texture.height);
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv, true);
        }

        private sealed class EntityVisual
        {
            private readonly SpriteRenderer artwork;
            private readonly SpriteRenderer flash;
            private readonly SpriteRenderer barBackground;
            private readonly SpriteRenderer barFill;
            private Vector2 displaySize;
            private float healthRatio;
            private float healthBarAlpha;
            private float healthVisibleUntil;
            private float flashRemaining;
            private float deathRemaining;
            private float deathDuration;
            private bool alwaysShowHealth;
            private Color flashColor = Color.white;

            private EntityVisual(
                GameObject gameObject,
                SpriteRenderer artwork,
                SpriteRenderer flash,
                SpriteRenderer barBackground,
                SpriteRenderer barFill)
            {
                GameObject = gameObject;
                this.artwork = artwork;
                this.flash = flash;
                this.barBackground = barBackground;
                this.barFill = barFill;
            }

            public GameObject GameObject { get; }
            public int EntityId { get; private set; }
            public string DefinitionId { get; private set; }
            public BattleTeam Team { get; private set; }
            public Vector2 TargetPosition { get; private set; }
            public bool AlwaysShowHealth
            {
                get => alwaysShowHealth;
                set
                {
                    alwaysShowHealth = value;
                    if (value)
                    {
                        healthBarAlpha = 1f;
                        SetHealthAlpha(1f);
                    }
                }
            }
            public bool ReadyToRecycle { get; private set; }
            public Sprite Sprite => artwork.sprite;

            public static EntityVisual Create(Transform parent, Sprite white)
            {
                var root = new GameObject("Pooled Battle Entity");
                root.transform.SetParent(parent, false);

                SpriteRenderer artwork = CreateRenderer("Artwork", root.transform);
                SpriteRenderer flash = CreateRenderer("Hit Flash", root.transform);
                flash.sprite = white;
                flash.enabled = false;

                SpriteRenderer background = CreateRenderer("Health Background", root.transform);
                background.sprite = white;
                SpriteRenderer fill = CreateRenderer("Health Fill", root.transform);
                fill.sprite = white;
                return new EntityVisual(root, artwork, flash, background, fill);
            }

            public void Activate(
                int entityId,
                string definitionId,
                BattleTeam team,
                Sprite sprite,
                Vector2 size,
                Vector2 position,
                float normalizedHealth,
                bool alwaysShowHealth,
                float now)
            {
                EntityId = entityId;
                DefinitionId = definitionId ?? string.Empty;
                Team = team;
                AlwaysShowHealth = alwaysShowHealth;
                ReadyToRecycle = false;
                deathRemaining = 0f;
                deathDuration = 0f;
                flashRemaining = 0f;
                healthVisibleUntil = now;
                healthBarAlpha = alwaysShowHealth ? 1f : 0f;
                GameObject.name = $"Battle Entity {entityId} [{DefinitionId}]";
                GameObject.SetActive(true);
                ConfigureArtwork(sprite, size, whiteSprite);
                TargetPosition = ClampEntityPosition(position, displaySize);
                GameObject.transform.position = TargetPosition;
                healthRatio = normalizedHealth;
                UpdateHealthGeometry();
                SetHealthAlpha(healthBarAlpha);
            }

            public void ConfigureArtwork(Sprite sprite, Vector2 size, Sprite white)
            {
                artwork.sprite = sprite ?? white;
                displaySize = size;
                Bounds spriteBounds = artwork.sprite.bounds;
                var artworkScale = new Vector3(
                    size.x / Mathf.Max(Mathf.Epsilon, spriteBounds.size.x),
                    size.y / Mathf.Max(Mathf.Epsilon, spriteBounds.size.y),
                    1f);
                artwork.transform.localScale = artworkScale;
                artwork.transform.localPosition = -Vector3.Scale(spriteBounds.center, artworkScale);
                artwork.color = Color.white;

                flash.sprite = white;
                flash.transform.localScale = new Vector3(size.x, size.y, 1f);
                float barY = size.y * 0.5f + 0.12f;
                barBackground.transform.localPosition = new Vector3(0f, barY, 0f);
                barFill.transform.localPosition = new Vector3(0f, barY, 0f);
                TargetPosition = ClampEntityPosition(TargetPosition, displaySize);
                GameObject.transform.position = ClampEntityPosition(GameObject.transform.position, displaySize);
                UpdateHealthGeometry();
            }

            public void SetTargetPosition(Vector2 position)
            {
                TargetPosition = ClampEntityPosition(position, displaySize);
            }

            public void SetFacing(Vector2 facing)
            {
                artwork.flipX = facing.x < 0f;
            }

            public void SetHealth(float current, float maximum, bool reveal, float now)
            {
                healthRatio = NormalizeHealth(current, maximum);
                UpdateHealthGeometry();
                if (reveal)
                {
                    healthVisibleUntil = now + HealthBarVisibleSeconds;
                }
            }

            public void Flash(float duration)
            {
                Pulse(Color.white, duration);
            }

            public void Pulse(Color color, float duration)
            {
                flashColor = color;
                flashRemaining = Mathf.Max(flashRemaining, duration);
                flash.color = new Color(color.r, color.g, color.b, 0.76f);
                flash.enabled = true;
            }

            public void BeginDeath(float duration)
            {
                deathDuration = Mathf.Max(Mathf.Epsilon, duration);
                deathRemaining = deathDuration;
                flash.enabled = false;
            }

            public void Advance(float dt, float now)
            {
                float blend = 1f - Mathf.Exp(-PositionSharpness * dt);
                Vector2 interpolated = Vector2.Lerp(GameObject.transform.position, TargetPosition, blend);
                GameObject.transform.position = ClampEntityPosition(interpolated, displaySize);

                flashRemaining = Mathf.Max(0f, flashRemaining - dt);
                flash.enabled = flashRemaining > 0f;
                if (flash.enabled)
                {
                    flash.color = new Color(flashColor.r, flashColor.g, flashColor.b,
                        Mathf.Clamp01(flashRemaining / FlashSeconds) * 0.76f);
                }

                float targetBarAlpha = AlwaysShowHealth || now < healthVisibleUntil ? 1f : 0f;
                healthBarAlpha = Mathf.MoveTowards(healthBarAlpha, targetBarAlpha, dt / HealthFadeSeconds);
                SetHealthAlpha(healthBarAlpha);

                if (deathRemaining > 0f)
                {
                    deathRemaining = Mathf.Max(0f, deathRemaining - dt);
                    float alpha = deathRemaining / deathDuration;
                    Color artworkColor = artwork.color;
                    artwork.color = new Color(artworkColor.r, artworkColor.g, artworkColor.b, alpha);
                    SetHealthAlpha(Mathf.Min(healthBarAlpha, alpha));
                    ReadyToRecycle = deathRemaining <= 0f;
                }
            }

            public static int CompareBackToFront(EntityVisual left, EntityVisual right)
            {
                int yOrder = right.GameObject.transform.position.y.CompareTo(left.GameObject.transform.position.y);
                return yOrder != 0 ? yOrder : right.EntityId.CompareTo(left.EntityId);
            }

            public void SetSortingOrder(int order)
            {
                artwork.sortingOrder = order;
                flash.sortingOrder = order + 1;
                barBackground.sortingOrder = order + 2;
                barFill.sortingOrder = order + 3;
            }

            public BattleVisualDebug ToDebug()
            {
                return new BattleVisualDebug(
                    EntityId,
                    DefinitionId,
                    Team,
                    displaySize,
                    GameObject.transform.position,
                    TargetPosition,
                    artwork.sortingOrder,
                    healthRatio,
                    healthBarAlpha,
                    AlwaysShowHealth,
                    deathRemaining > 0f,
                    flashRemaining > 0f,
                    artwork.sprite);
            }

            private void UpdateHealthGeometry()
            {
                float fullWidth = Mathf.Max(0.44f, displaySize.x * 0.82f);
                const float barHeight = 0.075f;
                barBackground.transform.localScale = new Vector3(fullWidth, barHeight, 1f);
                float fillWidth = fullWidth * healthRatio;
                barFill.transform.localScale = new Vector3(fillWidth, barHeight * 0.72f, 1f);
                float barY = displaySize.y * 0.5f + 0.12f;
                barFill.transform.localPosition = new Vector3(-(fullWidth - fillWidth) * 0.5f, barY, 0f);
                barBackground.color = new Color(0.04f, 0.05f, 0.07f, healthBarAlpha * 0.88f);
                Color teamColor = Team == BattleTeam.Ally
                    ? new Color(0.15f, 0.55f, 1f)
                    : new Color(1f, 0.25f, 0.2f);
                barFill.color = new Color(teamColor.r, teamColor.g, teamColor.b, healthBarAlpha);
            }

            private void SetHealthAlpha(float alpha)
            {
                Color backgroundColor = barBackground.color;
                barBackground.color = new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, alpha * 0.88f);
                Color fillColor = barFill.color;
                barFill.color = new Color(fillColor.r, fillColor.g, fillColor.b, alpha);
                barBackground.enabled = alpha > 0f;
                barFill.enabled = alpha > 0f;
            }
        }

        private readonly struct CombatNumberKey : IEquatable<CombatNumberKey>
        {
            public CombatNumberKey(int targetEntityId, bool isHealing)
            {
                TargetEntityId = targetEntityId;
                IsHealing = isHealing;
            }

            public int TargetEntityId { get; }
            public bool IsHealing { get; }

            public bool Equals(CombatNumberKey other)
            {
                return TargetEntityId == other.TargetEntityId && IsHealing == other.IsHealing;
            }

            public override bool Equals(object obj)
            {
                return obj is CombatNumberKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (TargetEntityId * 397) ^ (IsHealing ? 1 : 0);
                }
            }
        }

        private sealed class CombatNumberVisual
        {
            private const int MaxGlyphCount = 11;
            private const float GlyphAdvance = 0.21f;
            private const float GlyphHeight = 0.294f;
            private const float RiseSpeed = 0.9f;

            private readonly SpriteRenderer[] glyphRenderers;
            private readonly int sortingOrder;
            private Color color;
            private float duration;
            private float aggregationWindow;
            private Vector2 deterministicOffset;
            private int glyphCount;

            private CombatNumberVisual(
                GameObject gameObject,
                SpriteRenderer[] glyphRenderers,
                int sortingOrder)
            {
                GameObject = gameObject;
                this.glyphRenderers = glyphRenderers;
                this.sortingOrder = sortingOrder;
            }

            public GameObject GameObject { get; }
            public CombatNumberKey Key { get; private set; }
            public long AccumulatedAmount { get; private set; }
            public float RemainingSeconds { get; private set; }
            private float AggregateUntil { get; set; }

            public static CombatNumberVisual Create(Transform parent, int slot, int sortingOrder)
            {
                var gameObject = new GameObject($"Pooled Combat Number {slot}");
                gameObject.transform.SetParent(parent, false);
                var renderers = new SpriteRenderer[MaxGlyphCount];
                for (var index = 0; index < renderers.Length; index++)
                {
                    renderers[index] = CreateRenderer($"Glyph {index}", gameObject.transform);
                    renderers[index].sortingOrder = Mathf.Min(sortingOrder, MaxBattleSortingOrder);
                    renderers[index].enabled = false;
                }

                gameObject.SetActive(false);
                return new CombatNumberVisual(gameObject, renderers, sortingOrder);
            }

            public void Activate(
                CombatNumberKey key,
                int amount,
                long eventSequence,
                Vector2 position,
                Color displayColor,
                float now,
                float seconds,
                float aggregationSeconds)
            {
                Key = key;
                AccumulatedAmount = Math.Max(0, amount);
                color = displayColor;
                duration = Mathf.Max(Mathf.Epsilon, seconds);
                aggregationWindow = Mathf.Max(0f, aggregationSeconds);
                RemainingSeconds = duration;
                AggregateUntil = now + aggregationWindow;
                deterministicOffset = CalculateOffset(key, eventSequence);
                GameObject.SetActive(true);
                UpdateGlyphs();
                SetPosition(position);
                SetAlpha(1f);
            }

            public bool CanAggregate(float now)
            {
                return GameObject.activeSelf && now <= AggregateUntil;
            }

            public void Accumulate(
                int amount,
                Vector2 position,
                Color displayColor,
                float now,
                float seconds)
            {
                AccumulatedAmount = Math.Min(long.MaxValue, AccumulatedAmount + Math.Max(0, amount));
                color = displayColor;
                duration = Mathf.Max(Mathf.Epsilon, seconds);
                RemainingSeconds = duration;
                AggregateUntil = now + aggregationWindow;
                UpdateGlyphs();
                SetPosition(position);
                SetAlpha(1f);
            }

            public void Advance(float dt)
            {
                RemainingSeconds = Mathf.Max(0f, RemainingSeconds - dt);
                Vector2 next = GameObject.transform.position + Vector3.up * (RiseSpeed * dt);
                GameObject.transform.position = ClampToBattlefield(next, CurrentHalfExtents());
                float normalized = Mathf.Clamp01(RemainingSeconds / duration);
                SetAlpha(normalized * normalized);
            }

            public CombatNumberDebug ToDebug()
            {
                return new CombatNumberDebug(
                    Key.TargetEntityId,
                    AccumulatedAmount,
                    Key.IsHealing,
                    GameObject.transform.position,
                    CurrentHalfExtents(),
                    glyphCount,
                    sortingOrder);
            }

            private void UpdateGlyphs()
            {
                string digits = AccumulatedAmount.ToString(CultureInfo.InvariantCulture);
                string content = Key.IsHealing ? "+" + digits : digits;
                glyphCount = Mathf.Min(content.Length, glyphRenderers.Length);
                float startX = -(glyphCount - 1) * GlyphAdvance * 0.5f;
                for (var index = 0; index < glyphRenderers.Length; index++)
                {
                    SpriteRenderer renderer = glyphRenderers[index];
                    bool visible = index < glyphCount;
                    renderer.enabled = visible;
                    if (!visible)
                    {
                        continue;
                    }

                    renderer.sprite = CombatGlyphAtlas.Get(content[index]);
                    renderer.transform.localPosition = new Vector3(startX + index * GlyphAdvance, 0f, 0f);
                    renderer.transform.localScale = Vector3.one * 0.42f;
                    renderer.color = color;
                }
            }

            private void SetPosition(Vector2 eventPosition)
            {
                Vector2 desired = eventPosition + Vector2.up * 0.24f + deterministicOffset;
                GameObject.transform.position = ClampToBattlefield(desired, CurrentHalfExtents());
            }

            private Vector2 CurrentHalfExtents()
            {
                return new Vector2(Mathf.Max(GlyphAdvance, glyphCount * GlyphAdvance) * 0.5f, GlyphHeight * 0.5f);
            }

            private void SetAlpha(float alpha)
            {
                for (var index = 0; index < glyphCount; index++)
                {
                    glyphRenderers[index].color = new Color(color.r, color.g, color.b, alpha);
                }
            }

            private static Vector2 CalculateOffset(CombatNumberKey key, long eventSequence)
            {
                unchecked
                {
                    uint hash = (uint)key.TargetEntityId * 2654435761u;
                    hash ^= key.IsHealing ? 0x9E3779B9u : 0x85EBCA6Bu;
                    hash ^= (uint)eventSequence * 2246822519u;
                    hash ^= (uint)(eventSequence >> 32) * 3266489917u;
                    float x = ((hash % 5u) - 2f) * 0.08f;
                    float y = ((hash >> 4) % 3u) * 0.04f;
                    return new Vector2(x, y);
                }
            }
        }

        private static class CombatGlyphAtlas
        {
            private const int GlyphWidth = 5;
            private const int GlyphHeight = 7;
            private const string Characters = "0123456789+-";

            private static readonly string[][] Patterns =
            {
                new[] { "11111", "10001", "10011", "10101", "11001", "10001", "11111" },
                new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
                new[] { "11110", "00001", "00001", "11110", "10000", "10000", "11111" },
                new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
                new[] { "10010", "10010", "10010", "11111", "00010", "00010", "00010" },
                new[] { "11111", "10000", "10000", "11110", "00001", "00001", "11110" },
                new[] { "01111", "10000", "10000", "11110", "10001", "10001", "01110" },
                new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
                new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
                new[] { "01110", "10001", "10001", "01111", "00001", "00001", "11110" },
                new[] { "00000", "00100", "00100", "11111", "00100", "00100", "00000" },
                new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" }
            };

            private static Sprite[] sprites;

            public static Sprite Get(char character)
            {
                EnsureCreated();
                int index = Characters.IndexOf(character);
                return sprites[index >= 0 ? index : 0];
            }

            private static void EnsureCreated()
            {
                if (sprites != null)
                {
                    return;
                }

                int textureWidth = GlyphWidth * Characters.Length;
                var texture = new Texture2D(textureWidth, GlyphHeight, TextureFormat.RGBA32, false)
                {
                    name = "HanziDefend Combat Number Atlas",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                var pixels = new Color32[textureWidth * GlyphHeight];
                for (var glyphIndex = 0; glyphIndex < Patterns.Length; glyphIndex++)
                {
                    string[] pattern = Patterns[glyphIndex];
                    for (var row = 0; row < GlyphHeight; row++)
                    {
                        for (var column = 0; column < GlyphWidth; column++)
                        {
                            if (pattern[GlyphHeight - 1 - row][column] == '1')
                            {
                                pixels[row * textureWidth + glyphIndex * GlyphWidth + column] =
                                    new Color32(255, 255, 255, 255);
                            }
                        }
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                sprites = new Sprite[Characters.Length];
                for (var index = 0; index < sprites.Length; index++)
                {
                    sprites[index] = Sprite.Create(
                        texture,
                        new Rect(index * GlyphWidth, 0f, GlyphWidth, GlyphHeight),
                        new Vector2(0.5f, 0.5f),
                        10f);
                    sprites[index].name = $"Combat Glyph {Characters[index]}";
                    sprites[index].hideFlags = HideFlags.HideAndDontSave;
                }
            }
        }
    }
}
