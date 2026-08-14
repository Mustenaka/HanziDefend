using System.Collections;
using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Deploy;
using HanziDefend.View;
using HanziDefend.View.Feedback;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HanziDefend.Tests.PlayMode
{
    [TestFixture]
    public sealed class FeedbackTests
    {
        private const float MissingAudioSmokeDurationSeconds = 30f;

        private FeedbackConfig config;
        private GameObject root;
        private GameObject shakeObject;
        private BattleView presentationClock;
        private FeedbackPresenter presenter;
        private AudioClip clip;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            config = FeedbackConfig.Load(new JsonConfigSource());
        }

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Feedback Test Root");
            shakeObject = new GameObject("Feedback Shake Target");
            shakeObject.transform.SetParent(root.transform, false);
            shakeObject.transform.localPosition = new Vector3(2f, -3f, 0f);
            presentationClock = root.AddComponent<BattleView>();
            presenter = root.AddComponent<FeedbackPresenter>();
            clip = AudioClip.Create("Feedback Test Clip", 4096, 1, config.Audio.SampleRateHz, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
            if (clip != null)
            {
                Object.DestroyImmediate(clip);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void FeedbackJson_DefinesEveryCueAndPresentationTuning()
        {
            Assert.That(config.SchemaVersion, Is.EqualTo(1));
            Assert.That(config.Audio.Cues, Has.Length.EqualTo(7));
            Assert.That(config.Audio.PoolSize, Is.GreaterThan(0));
            Assert.That(config.HitStop.Enabled, Is.True);
            Assert.That(config.HitStop.DurationSeconds, Is.GreaterThan(0f));
            Assert.That(config.Shake.Enabled, Is.True);
            Assert.That(config.Shake.Amplitude, Is.GreaterThan(0f));

            foreach (FeedbackCue cue in new[]
                     {
                         FeedbackCue.Deploy,
                         FeedbackCue.Merge,
                         FeedbackCue.Spawn,
                         FeedbackCue.Hit,
                         FeedbackCue.Victory,
                         FeedbackCue.Defeat,
                         FeedbackCue.Settlement
                     })
            {
                Assert.That(config.FindCue(cue), Is.Not.Null, cue.ToString());
            }
        }

        [Test]
        public void AudioSourcePool_UsesConfiguredCapacityAndMasterVolumeEntry()
        {
            presenter.Initialize(config, new SingleClipSource(clip), shakeObject.transform, presentationClock);

            Assert.That(presenter.AudioPool.Capacity, Is.EqualTo(config.Audio.PoolSize));
            Assert.That(presenter.AudioPool.CreatedSourceCount, Is.EqualTo(config.Audio.PoolSize));
            for (var index = 0; index < config.Audio.PoolSize + 3; index++)
            {
                Assert.That(presenter.PlayCue(FeedbackCue.Deploy), Is.True);
            }
            Assert.That(presenter.AudioPool.CreatedSourceCount, Is.EqualTo(config.Audio.PoolSize),
                "Dense cues must steal deterministically instead of allocating more AudioSources.");

            FeedbackCueDef deploy = config.FindCue(FeedbackCue.Deploy);
            Assert.That(presenter.AudioPool.GetSourceVolume(0),
                Is.EqualTo(deploy.Volume * config.Audio.MasterVolume).Within(0.0001f));

            presenter.SetMasterVolume(0.25f);
            Assert.That(presenter.AudioPool.MasterVolume, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(presenter.AudioPool.GetSourceVolume(0),
                Is.EqualTo(deploy.Volume * 0.25f).Within(0.0001f));
        }

        [Test]
        public void MissingAudioFiles_AllCallPointsDegradeWithoutError()
        {
            presenter.Initialize(
                config,
                EmptyFeedbackAudioClipSource.Instance,
                shakeObject.transform,
                presentationClock);

            Assert.That(presenter.PlayCue(FeedbackCue.Deploy), Is.False);
            presenter.NotifyDeployment(DeploymentActionKind.Merge);
            presenter.UnitSpawned(SpawnEvent());
            presenter.DamageDealt(DamageEvent(10));
            presenter.BattleSettled(SettledEvent(BattleResult.Win));
            presenter.BattleSettled(SettledEvent(BattleResult.Lose));
            presenter.NotifySettlement();

            Assert.That(presenter.LastRequestedCue, Is.EqualTo(FeedbackCue.Settlement));
            Assert.That(presenter.PlayRequestCount, Is.EqualTo(7));
        }

        [UnityTest]
        public IEnumerator MissingAudioFiles_ThirtySecondRuntimeSmoke()
        {
            presentationClock.enabled = false;
            presenter.Initialize(
                config,
                EmptyFeedbackAudioClipSource.Instance,
                shakeObject.transform,
                null);

            Assert.That(presenter.PlayCue(FeedbackCue.Deploy), Is.False);
            presenter.NotifyDeployment(DeploymentActionKind.Merge);
            presenter.UnitSpawned(SpawnEvent());
            presenter.DamageDealt(DamageEvent(11));
            presenter.BattleSettled(SettledEvent(BattleResult.Win));
            presenter.BattleSettled(SettledEvent(BattleResult.Lose));
            presenter.NotifySettlement();

            double deadline = Time.realtimeSinceStartupAsDouble
                              + MissingAudioSmokeDurationSeconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            Assert.That(presenter.PlayRequestCount, Is.EqualTo(7));
            Assert.That(presenter.AudioPool.ActiveSourceCount, Is.Zero);
            Assert.That(presenter.LastRequestedCue, Is.EqualTo(FeedbackCue.Settlement));
        }

        [Test]
        public void HitStop_DisablesOnlyPresentationClock_AndNeverChangesGlobalTimeScale()
        {
            presenter.Initialize(
                config,
                EmptyFeedbackAudioClipSource.Instance,
                shakeObject.transform,
                presentationClock);
            float timeScaleBefore = Time.timeScale;

            presenter.DamageDealt(DamageEvent(20));

            Assert.That(presenter.HitStopActive, Is.True);
            Assert.That(presentationClock.enabled, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(timeScaleBefore));

            presenter.Advance(config.HitStop.DurationSeconds * 2f);

            Assert.That(presenter.HitStopActive, Is.False);
            Assert.That(presentationClock.enabled, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(timeScaleBefore));
        }

        [Test]
        public void Shake_IsSequenceDeterministic_AndRestoresExactLocalPosition()
        {
            presenter.Initialize(
                config,
                EmptyFeedbackAudioClipSource.Instance,
                shakeObject.transform,
                presentationClock);
            Vector3 rest = shakeObject.transform.localPosition;

            presenter.DamageDealt(DamageEvent(37));
            Vector3 firstOffset = presenter.CurrentShakeOffset;
            Assert.That(firstOffset.sqrMagnitude, Is.GreaterThan(0f));

            presenter.Advance(config.Shake.DurationSeconds * 2f);
            Assert.That(shakeObject.transform.localPosition, Is.EqualTo(rest));

            presenter.DamageDealt(DamageEvent(37));
            Assert.That(presenter.CurrentShakeOffset.x, Is.EqualTo(firstOffset.x).Within(0.000001f));
            Assert.That(presenter.CurrentShakeOffset.y, Is.EqualTo(firstOffset.y).Within(0.000001f));
        }

        [Test]
        public void DeploymentResult_SelectsDeployOrMergeCueWithoutReevaluatingRules()
        {
            presenter.Initialize(
                config,
                EmptyFeedbackAudioClipSource.Instance,
                shakeObject.transform,
                presentationClock);

            presenter.NotifyDeployment(DeploymentActionKind.Place);
            Assert.That(presenter.LastRequestedCue, Is.EqualTo(FeedbackCue.Deploy));

            presenter.NotifyDeployment(DeploymentActionKind.Merge);
            Assert.That(presenter.LastRequestedCue, Is.EqualTo(FeedbackCue.Merge));
        }

        [Test]
        public void Router_ForwardsBattleViewFirst_ThenStartsPresentationOnlyFeedback()
        {
            presenter.Initialize(
                config,
                EmptyFeedbackAudioClipSource.Instance,
                shakeObject.transform,
                presentationClock);
            var primary = new RecordingEvents(presentationClock);
            var router = new FeedbackEventRouter(primary, presenter);

            router.DamageDealt(DamageEvent(99));

            Assert.That(primary.DamageCount, Is.EqualTo(1));
            Assert.That(primary.ClockWasEnabledDuringDamage, Is.True);
            Assert.That(presentationClock.enabled, Is.False);
            Assert.That(presenter.HitStopActive, Is.True);
        }

        [Test]
        public void GeneratedRuntimeCatalog_ContainsEveryConfiguredPlaceholderClip()
        {
            IFeedbackAudioClipSource source = FeedbackRuntimeAssets.LoadClipSourceOrEmpty();
            foreach (FeedbackCueDef cue in config.Audio.Cues)
            {
                Assert.That(source.TryGetClip(cue.ClipId, out AudioClip loaded), Is.True, cue.ClipId);
                Assert.That(loaded, Is.Not.Null, cue.ClipId);
            }
        }

        private static DamageDealtEvent DamageEvent(long sequence)
        {
            return new DamageDealtEvent(
                sequence,
                sequence,
                sequence * 0.01d,
                1,
                10,
                20,
                8,
                100f,
                92f,
                100f,
                Vector2.zero,
                false);
        }

        private static UnitSpawnedEvent SpawnEvent()
        {
            var stats = new BattleStats(100f, 10f, 1f, 0f, 1f, 1f, 1f, 1f, 1f);
            return new UnitSpawnedEvent(
                1,
                1,
                0d,
                10,
                "gong",
                BattleTeam.Ally,
                1,
                stats,
                stats.MaxHp,
                Vector2.zero,
                Vector2.up,
                BattleUnitState.Idle);
        }

        private static BattleSettledEvent SettledEvent(BattleResult result)
        {
            return new BattleSettledEvent(
                1,
                1,
                1d,
                result,
                10,
                20,
                result == BattleResult.Lose ? 0f : 100f,
                result == BattleResult.Win ? 0f : 100f,
                result == BattleResult.Lose,
                result == BattleResult.Win);
        }

        private sealed class SingleClipSource : IFeedbackAudioClipSource
        {
            private readonly AudioClip value;

            internal SingleClipSource(AudioClip clipValue)
            {
                value = clipValue;
            }

            public bool TryGetClip(string clipId, out AudioClip resolved)
            {
                resolved = value;
                return resolved != null;
            }
        }

        private sealed class RecordingEvents : IBattleEncounterEvents, IBattleEffectEvents
        {
            private readonly Behaviour clock;

            internal RecordingEvents(Behaviour presentationClock)
            {
                clock = presentationClock;
            }

            internal int DamageCount { get; private set; }

            internal bool ClockWasEnabledDuringDamage { get; private set; }

            public void UnitSpawned(UnitSpawnedEvent eventData) { }
            public void UnitAttacked(UnitAttackedEvent eventData) { }

            public void DamageDealt(DamageDealtEvent eventData)
            {
                DamageCount++;
                ClockWasEnabledDuringDamage = clock.enabled;
            }

            public void UnitDied(UnitDiedEvent eventData) { }
            public void CoinDropped(CoinDroppedEvent eventData) { }
            public void WaveStarted(WaveStartedEvent eventData) { }
            public void BossSpawned(BossSpawnedEvent eventData) { }
            public void BaseDamaged(BaseDamagedEvent eventData) { }
            public void BattleSettled(BattleSettledEvent eventData) { }
            public void EffectApplied(EffectAppliedEvent eventData) { }
            public void EffectExpired(EffectExpiredEvent eventData) { }
            public void UnitHealed(UnitHealedEvent eventData) { }
            public void ShieldChanged(ShieldChangedEvent eventData) { }
            public void CoinsModified(CoinsModifiedEvent eventData) { }
        }
    }
}
