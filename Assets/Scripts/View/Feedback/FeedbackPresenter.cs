using System;
using System.Collections.Generic;
using HanziDefend.Data;
using HanziDefend.Gameplay;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Deploy;
using UnityEngine;

namespace HanziDefend.View.Feedback
{
    public interface IFeedbackSink
    {
        bool PlayCue(FeedbackCue cue);

        void NotifyDeployment(DeploymentActionKind action);

        void NotifySettlement();

        void SetMasterVolume(float volume);
    }

    /// <summary>
    /// Presentation-only feedback. It never pauses Time.timeScale or BattleSystem.Tick; hit-stop
    /// temporarily suspends only the injected presentation clock (normally BattleView.Update).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FeedbackPresenter : MonoBehaviour,
        IFeedbackSink,
        IBattleEncounterEvents,
        IBattleEffectEvents
    {
        private readonly Dictionary<FeedbackCue, FeedbackCueDef> cues =
            new Dictionary<FeedbackCue, FeedbackCueDef>();
        private readonly Dictionary<FeedbackCue, AudioClip> resolvedClips =
            new Dictionary<FeedbackCue, AudioClip>();

        private FeedbackConfig config;
        private AudioSourcePool audioPool;
        private Transform shakeTarget;
        private Behaviour presentationClock;
        private GameFlow flow;
        private Vector3 shakeRestLocalPosition;
        private float hitStopRemaining;
        private float hitStopCooldownRemaining;
        private float shakeRemaining;
        private float shakeCooldownRemaining;
        private float shakePhase;
        private bool ownsPresentationClockDisable;
        private bool initialized;

        public bool IsInitialized => initialized;

        public bool HitStopActive => hitStopRemaining > 0f;

        public bool ShakeActive => shakeRemaining > 0f;

        public Vector3 CurrentShakeOffset => shakeTarget == null
            ? Vector3.zero
            : shakeTarget.localPosition - shakeRestLocalPosition;

        public int PlayRequestCount { get; private set; }

        public FeedbackCue LastRequestedCue { get; private set; }

        public AudioSourcePool AudioPool => audioPool;

        public static FeedbackPresenter Attach(
            GameObject host,
            Transform battleRoot,
            Behaviour battlePresentationClock)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            FeedbackPresenter presenter = host.GetComponent<FeedbackPresenter>();
            if (presenter == null)
            {
                presenter = host.AddComponent<FeedbackPresenter>();
            }
            presenter.Initialize(
                FeedbackRuntimeAssets.LoadConfigOrDisabled(),
                FeedbackRuntimeAssets.LoadClipSourceOrEmpty(),
                battleRoot,
                battlePresentationClock);
            return presenter;
        }

        public void Initialize(
            FeedbackConfig feedbackConfig,
            IFeedbackAudioClipSource audioClipSource,
            Transform battleRoot,
            Behaviour battlePresentationClock)
        {
            if (initialized)
            {
                throw new InvalidOperationException("FeedbackPresenter has already been initialized.");
            }

            config = feedbackConfig ?? FeedbackConfig.Disabled();
            IFeedbackAudioClipSource clips = audioClipSource ?? EmptyFeedbackAudioClipSource.Instance;
            shakeTarget = battleRoot;
            presentationClock = battlePresentationClock == this ? null : battlePresentationClock;
            shakeRestLocalPosition = shakeTarget == null ? Vector3.zero : shakeTarget.localPosition;

            cues.Clear();
            resolvedClips.Clear();
            FeedbackCueDef[] definitions = config.Audio?.Cues ?? Array.Empty<FeedbackCueDef>();
            for (var index = 0; index < definitions.Length; index++)
            {
                FeedbackCueDef definition = definitions[index];
                if (definition != null && definition.Cue != FeedbackCue.Unknown)
                {
                    cues[definition.Cue] = definition;
                    if (clips.TryGetClip(definition.ClipId, out AudioClip clip) && clip != null)
                    {
                        resolvedClips[definition.Cue] = clip;
                    }
                }
            }

            if (config.Audio != null && config.Audio.PoolSize > 0)
            {
                audioPool = GetComponent<AudioSourcePool>();
                if (audioPool == null)
                {
                    audioPool = gameObject.AddComponent<AudioSourcePool>();
                }
                audioPool.Initialize(config.Audio.PoolSize, config.Audio.MasterVolume);
            }

            initialized = true;
        }

        public void BindFlow(GameFlow gameFlow)
        {
            if (ReferenceEquals(flow, gameFlow))
            {
                return;
            }

            if (flow != null)
            {
                flow.PhaseChanged -= HandlePhaseChanged;
            }
            flow = gameFlow;
            if (flow != null)
            {
                flow.PhaseChanged += HandlePhaseChanged;
            }
        }

        public bool PlayCue(FeedbackCue cue)
        {
            PlayRequestCount++;
            LastRequestedCue = cue;
            if (audioPool == null
                || !cues.TryGetValue(cue, out FeedbackCueDef definition)
                || !resolvedClips.TryGetValue(cue, out AudioClip clip))
            {
                return false;
            }

            return audioPool.TryPlay(clip, definition.Volume, definition.Pitch);
        }

        public void NotifyDeployment(DeploymentActionKind action)
        {
            PlayCue(action == DeploymentActionKind.Merge ? FeedbackCue.Merge : FeedbackCue.Deploy);
        }

        public void NotifySettlement()
        {
            PlayCue(FeedbackCue.Settlement);
        }

        public void SetMasterVolume(float volume)
        {
            audioPool?.SetMasterVolume(volume);
        }

        public void Advance(float unscaledDeltaTime)
        {
            float deltaTime = Mathf.Max(0f, unscaledDeltaTime);
            hitStopCooldownRemaining = Mathf.Max(0f, hitStopCooldownRemaining - deltaTime);
            shakeCooldownRemaining = Mathf.Max(0f, shakeCooldownRemaining - deltaTime);

            if (hitStopRemaining > 0f)
            {
                hitStopRemaining = Mathf.Max(0f, hitStopRemaining - deltaTime);
                if (hitStopRemaining <= 0f)
                {
                    RestorePresentationClock();
                }
            }

            AdvanceShake(deltaTime);
        }

        public void UnitSpawned(UnitSpawnedEvent eventData)
        {
            PlayCue(FeedbackCue.Spawn);
        }

        public void UnitAttacked(UnitAttackedEvent eventData)
        {
        }

        public void DamageDealt(DamageDealtEvent eventData)
        {
            PlayCue(FeedbackCue.Hit);
            BeginImpactFeedback(eventData.Sequence);
        }

        public void UnitDied(UnitDiedEvent eventData)
        {
        }

        public void CoinDropped(CoinDroppedEvent eventData)
        {
        }

        public void WaveStarted(WaveStartedEvent eventData)
        {
        }

        public void BossSpawned(BossSpawnedEvent eventData)
        {
        }

        public void BaseDamaged(BaseDamagedEvent eventData)
        {
            PlayCue(FeedbackCue.Hit);
            BeginImpactFeedback(eventData.Sequence);
        }

        public void BattleSettled(BattleSettledEvent eventData)
        {
            if (eventData.Result == BattleResult.Win)
            {
                PlayCue(FeedbackCue.Victory);
            }
            else if (eventData.Result == BattleResult.Lose)
            {
                PlayCue(FeedbackCue.Defeat);
            }
        }

        public void EffectApplied(EffectAppliedEvent eventData)
        {
        }

        public void EffectExpired(EffectExpiredEvent eventData)
        {
        }

        public void UnitHealed(UnitHealedEvent eventData)
        {
        }

        public void ShieldChanged(ShieldChangedEvent eventData)
        {
        }

        public void CoinsModified(CoinsModifiedEvent eventData)
        {
        }

        private void Update()
        {
            Advance(Time.unscaledDeltaTime);
        }

        private void HandlePhaseChanged(GameFlowPhase phase)
        {
            if (phase == GameFlowPhase.Reward)
            {
                NotifySettlement();
            }
        }

        private void BeginImpactFeedback(long eventSequence)
        {
            FeedbackHitStopDef hitStop = config?.HitStop;
            if (hitStop != null
                && hitStop.Enabled
                && hitStopCooldownRemaining <= 0f
                && presentationClock != null)
            {
                hitStopRemaining = hitStop.DurationSeconds;
                hitStopCooldownRemaining = hitStop.CooldownSeconds;
                if (presentationClock.enabled)
                {
                    presentationClock.enabled = false;
                    ownsPresentationClockDisable = true;
                }
            }

            FeedbackShakeDef shake = config?.Shake;
            if (shake != null
                && shake.Enabled
                && shakeCooldownRemaining <= 0f
                && shakeTarget != null)
            {
                if (shakeRemaining <= 0f)
                {
                    shakeRestLocalPosition = shakeTarget.localPosition;
                }
                shakeRemaining = shake.DurationSeconds;
                shakeCooldownRemaining = shake.CooldownSeconds;
                shakePhase = eventSequence * shake.PhaseStepRadians;
                ApplyShakeOffset();
            }
        }

        private void AdvanceShake(float deltaTime)
        {
            if (shakeRemaining <= 0f || shakeTarget == null)
            {
                return;
            }

            shakeRemaining = Mathf.Max(0f, shakeRemaining - deltaTime);
            if (shakeRemaining <= 0f)
            {
                shakeTarget.localPosition = shakeRestLocalPosition;
                return;
            }

            ApplyShakeOffset();
        }

        private void ApplyShakeOffset()
        {
            FeedbackShakeDef shake = config.Shake;
            float elapsed = shake.DurationSeconds - shakeRemaining;
            float decay = shakeRemaining / shake.DurationSeconds;
            float angularTime = elapsed * shake.FrequencyHz * Mathf.PI * 2f;
            float horizontal = Mathf.Sin(angularTime + shakePhase);
            float vertical = Mathf.Cos(
                angularTime * shake.VerticalFrequencyMultiplier + shakePhase);
            shakeTarget.localPosition = shakeRestLocalPosition
                                        + new Vector3(horizontal, vertical, 0f)
                                        * (shake.Amplitude * decay);
        }

        private void RestorePresentationClock()
        {
            if (ownsPresentationClockDisable && presentationClock != null)
            {
                presentationClock.enabled = true;
            }
            ownsPresentationClockDisable = false;
        }

        private void OnDestroy()
        {
            if (flow != null)
            {
                flow.PhaseChanged -= HandlePhaseChanged;
            }
            RestorePresentationClock();
            if (shakeTarget != null && shakeRemaining > 0f)
            {
                shakeTarget.localPosition = shakeRestLocalPosition;
            }
        }
    }
}
