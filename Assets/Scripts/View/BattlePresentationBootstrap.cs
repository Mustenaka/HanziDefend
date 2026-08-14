using System.Collections.Generic;
using HanziDefend.Data;
using HanziDefend.Gameplay;
using HanziDefend.Gameplay.Battle;
using UnityEngine;

namespace HanziDefend.View
{
    /// <summary>
    /// Runtime-only scene composition. It decorates the existing Bootstrap object, leaving the
    /// authored Battle scene at its three-root contract and keeping BattleView free of rules.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattlePresentationBootstrap : MonoBehaviour, IBattleViewCommands
    {
        private const string DemoLevelId = "level_1_1";
        private const string AllyCommanderId = "cmd_bei";
        private const uint DemoSeed = 0xB4A771Eu;
        private const float PerformanceSampleWindowSeconds = 1f;
        private const int HeavyPresentationUnitCount = 200;
        private const int HeavyPresentationSampleFrames = 30;

        private float accumulator;
        private float performanceSampleTime;
        private int performanceSampleFrames;
        private float heavyPresentationSampleTime;
        private int heavyPresentationFrames;
        private bool initialized;
        private bool performanceSummaryLogged;

        public BattleSystem System { get; private set; }
        public BattleView View { get; private set; }
        public bool AutoRun { get; set; } = true;
        public float SimulationSpeed { get; set; } = 6f;
        public float MeasuredFramesPerSecond { get; private set; }
        public int PeakActiveVisualCount { get; private set; }
        public float MinimumFramesPerSecondAtOrAboveTwoHundred { get; private set; }
        public int TwoHundredUnitFrameRateSampleCount { get; private set; }
        public float CommanderCooldownRemaining => System == null
            ? 0f
            : System.GetCommanderCooldownRemaining(AllyCommanderId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AttachToSceneBootstrap()
        {
            BattleBootstrap sceneBootstrap = FindFirstObjectByType<BattleBootstrap>();
            if (sceneBootstrap != null
                && sceneBootstrap.GetComponent<M1GameBootstrap>() == null)
            {
                sceneBootstrap.gameObject.AddComponent<M1GameBootstrap>();
            }
        }

        private void Awake()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            Application.runInBackground = true;
            GameConfig config = RuntimeGameConfigLoader.Load();
            View = GetComponent<BattleView>();
            if (View == null)
            {
                View = gameObject.AddComponent<BattleView>();
            }

            View.Initialize(config, new ResourcesBattleArtSource(), this);
            var hudObject = new GameObject("Battle HUD Presentation", typeof(RectTransform));
            hudObject.transform.SetParent(transform, false);
            BattleHudCanvas canvasHud = hudObject.AddComponent<BattleHudCanvas>();
            canvasHud.Initialize(View, this, new ResourcesBattleArtSource());
            View.PrepareForEncounter();
            System = BattleSystem.CreateEncounter(config, DemoLevelId, DemoSeed, View);
            View.BindSystem(System);
            SpawnReferenceDeployment(System, config);
            System.ConfigureCommander(AllyCommanderId);
            View.SyncFromBoundSystem();
            initialized = true;
        }

        public bool TryActivateCommander()
        {
            if (System == null)
            {
                return false;
            }

            try
            {
                return System.TryActivateCommander(AllyCommanderId);
            }
            catch (global::System.InvalidOperationException)
            {
                // Gameplay owns command legality. A rejected post-settlement click is translated
                // to the view command contract without duplicating settlement rules here.
                return false;
            }
        }

        public void StepOnce()
        {
            if (System == null)
            {
                return;
            }

            System.Tick(System.FixedDeltaTime);
            View.SyncFromBoundSystem();
            LogPerformanceSummaryIfSettled();
        }

        private void Update()
        {
            SamplePresentationFrameRate();
            if (!initialized || !AutoRun || System == null || View == null)
            {
                return;
            }

            accumulator += Time.unscaledDeltaTime * Mathf.Max(0f, SimulationSpeed);
            while (accumulator >= System.FixedDeltaTime)
            {
                accumulator -= System.FixedDeltaTime;
                System.Tick(System.FixedDeltaTime);
            }

            View.SyncFromBoundSystem();
            LogPerformanceSummaryIfSettled();
        }

        private void SamplePresentationFrameRate()
        {
            if (!AutoRun)
            {
                // Review screenshots synchronously render and encode a 1080x1920 PNG. Discard the
                // paused sampling window so that stall cannot be reported as 200-unit frame time.
                performanceSampleTime = 0f;
                performanceSampleFrames = 0;
                heavyPresentationSampleTime = 0f;
                heavyPresentationFrames = 0;
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            performanceSampleTime += deltaTime;
            performanceSampleFrames++;

            int activeVisualCount = View == null ? 0 : View.ActiveVisualCount;
            PeakActiveVisualCount = Mathf.Max(PeakActiveVisualCount, activeVisualCount);
            if (activeVisualCount >= HeavyPresentationUnitCount)
            {
                heavyPresentationSampleTime += deltaTime;
                heavyPresentationFrames++;
                if (heavyPresentationFrames >= HeavyPresentationSampleFrames)
                {
                    float heavyFps = heavyPresentationFrames /
                                     Mathf.Max(Mathf.Epsilon, heavyPresentationSampleTime);
                    MinimumFramesPerSecondAtOrAboveTwoHundred =
                        TwoHundredUnitFrameRateSampleCount == 0
                            ? heavyFps
                            : Mathf.Min(MinimumFramesPerSecondAtOrAboveTwoHundred, heavyFps);
                    TwoHundredUnitFrameRateSampleCount++;
                    heavyPresentationSampleTime = 0f;
                    heavyPresentationFrames = 0;
                }
            }
            else
            {
                heavyPresentationSampleTime = 0f;
                heavyPresentationFrames = 0;
            }

            if (performanceSampleTime < PerformanceSampleWindowSeconds)
            {
                return;
            }

            MeasuredFramesPerSecond = performanceSampleFrames / performanceSampleTime;
            performanceSampleTime = 0f;
            performanceSampleFrames = 0;
        }

        private void LogPerformanceSummaryIfSettled()
        {
            if (performanceSummaryLogged || View == null ||
                View.CurrentReviewStage != BattleReviewStage.Settlement)
            {
                return;
            }

            performanceSummaryLogged = true;
            string heavySample = TwoHundredUnitFrameRateSampleCount > 0
                ? $"min >=200-unit FPS={MinimumFramesPerSecondAtOrAboveTwoHundred:F1} " +
                  $"({TwoHundredUnitFrameRateSampleCount} x {HeavyPresentationSampleFrames}-frame samples)"
                : "no stable >=200-unit sample observed";
            Debug.Log($"[WO-B4 Performance] peak active views={PeakActiveVisualCount}; " + heavySample + ".");
        }

        private void OnDestroy()
        {
            System?.Dispose();
            System = null;
        }

        private static void SpawnReferenceDeployment(BattleSystem system, GameConfig config)
        {
            const int rows = 3;
            IReadOnlyList<UnitDef> roster = config.AllyUnits;
            for (var copy = 0; copy < rows; copy++)
            {
                for (var index = 0; index < roster.Count; index++)
                {
                    float x = -4.2f + index % 7 * 1.4f;
                    float y = -7.1f + (index / 7 + copy * 2) * 0.72f;
                    system.Spawn(new UnitSpawnRequest(roster[index].Id, 1, new Vector2(x, y)));
                }
            }
        }
    }
}
