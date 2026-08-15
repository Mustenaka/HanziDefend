using System;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Deploy;
using HanziDefend.Gameplay.Reward;
using UnityEngine;

namespace HanziDefend.Gameplay
{
    public enum GameFlowPhase
    {
        Deploy,
        Battle,
        Reward,
        MajorVictory,
        Defeat
    }

    /// <summary>
    /// Rules owner for the single-scene five-stage loop. It exposes phase changes as commands;
    /// a view decides only which roots to project from the already-decided phase.
    /// </summary>
    public sealed class GameFlow : IDisposable, IBattleEncounterEvents, IBattleEffectEvents
    {
        private readonly GameConfig config;
        private readonly LevelDef[] levels;
        private readonly IBattleEvents presentationEvents;
        private readonly IBattleEncounterEvents presentationEncounterEvents;
        private readonly IBattleEffectEvents presentationEffectEvents;
        private readonly IAdService adService;
        private RngStreams streams;
        private bool disposed;

        public GameFlow(
            GameConfig gameConfig,
            uint seed,
            IBattleEvents battlePresentationEvents = null,
            IAdService rewardedAdService = null)
        {
            config = gameConfig ?? throw new ArgumentNullException(nameof(gameConfig));
            levels = config.Levels.OrderBy(level => level.StageIndex).ToArray();
            if (levels.Length == 0)
            {
                throw new ArgumentException("At least one level is required.", nameof(gameConfig));
            }

            presentationEvents = battlePresentationEvents ?? NullBattleEvents.Instance;
            presentationEncounterEvents = battlePresentationEvents as IBattleEncounterEvents ?? NullBattleEvents.Instance;
            presentationEffectEvents = battlePresentationEvents as IBattleEffectEvents ?? NullBattleEffectEvents.Instance;
            adService = rewardedAdService ?? new MockAdService();
            ResetRun(seed);
        }

        public event Action<GameFlowPhase> PhaseChanged;

        public event Action RunStateChanged;

        public GameFlowPhase Phase { get; private set; }

        public RunState RunState => Economy.State;

        public CardEconomy Economy { get; private set; }

        public BattleSystem Battle { get; private set; }

        public SettlementRewardSystem Reward { get; private set; }

        public int StageNumber => RunState.StageIndex;

        public int StageCount => levels.Length;

        public string CurrentLevelId => CurrentLevel().Id;

        /// <summary>
        /// The rewarded-ad unlock channel: shows the ad first and only grants the random unlock
        /// card when the service reports success, so the ad gate stays outside the economy rules.
        /// </summary>
        public bool TryWatchAdForUnlockCard()
        {
            ThrowIfDisposed();
            RequirePhase(GameFlowPhase.Deploy);
            if (!adService.ShowRewarded())
            {
                return false;
            }

            bool granted = Economy.TryGrantUnlockCard(out _);
            if (granted)
            {
                RunStateChanged?.Invoke();
            }
            return granted;
        }

        public void StartBattle()
        {
            ThrowIfDisposed();
            RequirePhase(GameFlowPhase.Deploy);
            Economy.SnapshotToRunState();
            RestoreStreamsFromRunState();

            DisposeBattle();
            Battle = BattleSystem.CreateEncounter(
                config,
                CurrentLevel().Id,
                RunState.Seed,
                streams.SaveState(),
                this);
            SpawnDeployment(Battle, RunState.DeployedGrid);
            if (config.Commanders.Count > 0)
            {
                Battle.ConfigureCommander(config.Commanders[0].Id);
            }
            ApplyPersistentBuffs();
            ChangePhase(GameFlowPhase.Battle);
        }

        public void Tick(float dt)
        {
            ThrowIfDisposed();
            if (Phase == GameFlowPhase.Battle)
            {
                Battle.Tick(dt);
            }
        }

        public void BeginNextStage()
        {
            ThrowIfDisposed();
            RequirePhase(GameFlowPhase.Reward);
            if (Reward == null || !Reward.IsSelectionComplete)
            {
                throw new InvalidOperationException("Select a settlement reward before continuing.");
            }

            streams.RestoreState(RunState.RngStreamsState);
            if (RunState.StageIndex >= levels.Length)
            {
                ChangePhase(GameFlowPhase.MajorVictory);
                return;
            }

            LevelDef next = levels[RunState.StageIndex];
            Economy.RandomStreams.RestoreState(RunState.RngStreamsState);
            Economy.AdvanceToMinorStage(next);
            Reward = null;
            DisposeBattle();
            ChangePhase(GameFlowPhase.Deploy);
            RunStateChanged?.Invoke();
        }

        public void RestartMajorStage()
        {
            ThrowIfDisposed();
            uint nextSeed = unchecked(RunState.Seed + 1u);
            ResetRun(nextSeed);
        }

        public void CompleteMajorVictoryAndRestart()
        {
            ThrowIfDisposed();
            RequirePhase(GameFlowPhase.MajorVictory);
            RestartMajorStage();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            DisposeBattle();
        }

        public void UnitSpawned(UnitSpawnedEvent eventData)
        {
            presentationEvents.UnitSpawned(eventData);
        }

        public void UnitAttacked(UnitAttackedEvent eventData)
        {
            presentationEvents.UnitAttacked(eventData);
        }

        public void DamageDealt(DamageDealtEvent eventData)
        {
            presentationEvents.DamageDealt(eventData);
        }

        public void UnitDied(UnitDiedEvent eventData)
        {
            presentationEvents.UnitDied(eventData);
        }

        public void CoinDropped(CoinDroppedEvent eventData)
        {
            Economy.CreditBattleDrops(eventData.Amount);
            presentationEvents.CoinDropped(eventData);
            RunStateChanged?.Invoke();
        }

        public void WaveStarted(WaveStartedEvent eventData)
        {
            presentationEncounterEvents.WaveStarted(eventData);
        }

        public void BossSpawned(BossSpawnedEvent eventData)
        {
            presentationEncounterEvents.BossSpawned(eventData);
        }

        public void BaseDamaged(BaseDamagedEvent eventData)
        {
            presentationEncounterEvents.BaseDamaged(eventData);
        }

        public void BattleSettled(BattleSettledEvent eventData)
        {
            if (Phase != GameFlowPhase.Battle)
            {
                return;
            }

            presentationEncounterEvents.BattleSettled(eventData);
            RngStreamsState battleState = Battle.SaveRngStreamsState();

            // Clearing a minor stage opens the locked cell nearest the ally base. It runs before
            // the run-state snapshot so the inherited mask already carries the new cell, and before
            // the reward offer so the next deployment sees the wider field. The pick consumes no
            // RNG, so it cannot disturb the stream merge below.
            if (eventData.Result != BattleResult.Lose)
            {
                Economy.AutoUnlockForClearedMinorStage();
            }

            Economy.SnapshotToRunState();
            RestoreStreamsFromRunState();
            streams.Battle.RestoreState(battleState.battle);
            RunState.RngStreamsState = streams.SaveState();

            if (eventData.Result == BattleResult.Lose)
            {
                ChangePhase(GameFlowPhase.Defeat);
                return;
            }

            Reward = new SettlementRewardSystem(config, RunState, streams, adService);
            Reward.CreateOffer();
            ChangePhase(GameFlowPhase.Reward);
        }

        public void EffectApplied(EffectAppliedEvent eventData)
        {
            presentationEffectEvents.EffectApplied(eventData);
        }

        public void EffectExpired(EffectExpiredEvent eventData)
        {
            presentationEffectEvents.EffectExpired(eventData);
        }

        public void UnitHealed(UnitHealedEvent eventData)
        {
            presentationEffectEvents.UnitHealed(eventData);
        }

        public void ShieldChanged(ShieldChangedEvent eventData)
        {
            presentationEffectEvents.ShieldChanged(eventData);
        }

        public void CoinsModified(CoinsModifiedEvent eventData)
        {
            presentationEffectEvents.CoinsModified(eventData);
        }

        private void ResetRun(uint seed)
        {
            DisposeBattle();
            streams = new RngStreams(seed);
            Economy = CardEconomy.StartNew(config, levels[0], streams.MasterSeed);
            Reward = null;
            // Through ChangePhase so a fresh run is dealt its first hand by the same single owner.
            ChangePhase(GameFlowPhase.Deploy);
            RunStateChanged?.Invoke();
        }

        private void RestoreStreamsFromRunState()
        {
            streams = new RngStreams(RunState.Seed);
            if (RunState.RngStreamsState != null)
            {
                streams.RestoreState(RunState.RngStreamsState);
            }
        }

        private LevelDef CurrentLevel()
        {
            LevelDef level = levels.FirstOrDefault(value => value.StageIndex == RunState.StageIndex);
            return level ?? throw new InvalidOperationException($"No level exists for stage {RunState.StageIndex}.");
        }

        private void SpawnDeployment(BattleSystem battle, IReadOnlyList<DeployedUnitState> deployed)
        {
            IReadOnlyList<DeployedUnitState> placements = deployed ?? Array.Empty<DeployedUnitState>();
            BattleRulesDef battleRules = config.Economy.Battle;
            Position2Def basePosition = battleRules.AllyBasePosition;
            Position2Def originOffset = battleRules.DeploymentOriginOffset;
            Position2Def cellSize = battleRules.DeploymentCellSize;
            ResolveUnlockedColumnSpan(out int firstColumn, out int lastColumn);
            for (int index = 0; index < placements.Count; index++)
            {
                DeployedUnitState value = placements[index];
                float x = basePosition.X
                          + originOffset.X
                          + SpreadOffset(value.Col, firstColumn, lastColumn, battleRules.DeploymentSpreadWidth);
                float y = basePosition.Y + originOffset.Y + value.Row * cellSize.Y;
                battle.Spawn(new UnitSpawnRequest(value.UnitId, value.Level, new Vector2(x, y)));
            }
        }

        /// <summary>
        /// Leftmost and rightmost unlocked columns. Spawns fan out across this range rather than
        /// across the whole 7-wide field, so the line always fills the base however much is unlocked.
        /// </summary>
        private void ResolveUnlockedColumnSpan(out int firstColumn, out int lastColumn)
        {
            firstColumn = int.MaxValue;
            lastColumn = int.MinValue;

            bool[] mask = RunState.UnlockedCells;
            int width = RunState.GridWidth;
            int height = RunState.GridHeight;
            if (mask != null && width > 0 && height > 0 && mask.Length == checked(width * height))
            {
                for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    if (!mask[(row * width) + column])
                    {
                        continue;
                    }

                    if (column < firstColumn) firstColumn = column;
                    if (column > lastColumn) lastColumn = column;
                }
            }

            if (firstColumn > lastColumn)
            {
                firstColumn = 0;
                lastColumn = Math.Max(0, width - 1);
            }
        }

        /// <summary>
        /// Maps a grid column onto the battlefield so the unlocked range spans
        /// <paramref name="spreadWidth"/> world units, centred on the base.
        ///
        /// <para>Deliberately a mapping and not a random scatter: "put it on the left and it comes
        /// out on the left" is a strategic dimension, and randomising would throw it away. Three
        /// unlocked columns spread wide, seven spread tight — either way the units read as a line
        /// across the base instead of a single point.</para>
        /// </summary>
        private static float SpreadOffset(int column, int firstColumn, int lastColumn, float spreadWidth)
        {
            int span = lastColumn - firstColumn;
            if (span <= 0)
            {
                return 0f;
            }

            float normalized = Mathf.Clamp01((column - firstColumn) / (float)span);
            return (normalized - 0.5f) * spreadWidth;
        }

        private void ApplyPersistentBuffs()
        {
            string[] effects = RunState.OwnedEffects ?? Array.Empty<string>();
            Position2Def basePosition = config.Economy.Battle.AllyBasePosition;
            var context = new EffectExecutionContext(
                null,
                BattleTeam.Ally,
                new Vector2(basePosition.X, basePosition.Y),
                1);
            for (int index = 0; index < effects.Length; index++)
            {
                if (effects[index].StartsWith("buff_", StringComparison.Ordinal))
                {
                    Battle.ApplyEffect(effects[index], context);
                }
            }
        }

        /// <summary>
        /// Every minor stage is dealt exactly one free hand, and this is the only place that
        /// happens.
        ///
        /// <para>It used to live in <c>DeployScreen.Initialize</c>, which runs once. The deploy
        /// screen is reused across minor stages, so from stage 2 onward nobody dealt the hand:
        /// the hand rendered empty and refresh/unlock threw "Draw the minor stage's free offer
        /// before refreshing". Dealing a hand is a game rule, so it belongs to the flow rather than
        /// to whoever happens to be looking at the screen — and this way a headless run gets one too.</para>
        /// </summary>
        private void EnsureMinorStageHandDealt()
        {
            if (Economy != null && Economy.CanDrawFreeOffer)
            {
                Economy.DrawOffer();
            }
        }

        private void ChangePhase(GameFlowPhase phase)
        {
            if (phase == GameFlowPhase.Deploy)
            {
                // Before PhaseChanged fires, so the view already sees a populated hand.
                EnsureMinorStageHandDealt();
            }

            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        private void RequirePhase(GameFlowPhase expected)
        {
            if (Phase != expected)
            {
                throw new InvalidOperationException($"GameFlow requires {expected} but is {Phase}.");
            }
        }

        private void DisposeBattle()
        {
            Battle?.Dispose();
            Battle = null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(GameFlow));
            }
        }
    }
}
