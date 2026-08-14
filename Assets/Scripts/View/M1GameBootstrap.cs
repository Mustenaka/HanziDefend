using System;
using HanziDefend.Data;
using HanziDefend.Gameplay;
using HanziDefend.Gameplay.Battle;
using HanziDefend.Gameplay.Reward;
using HanziDefend.View.Feedback;
using UnityEngine;
using UnityEngine.UI;

namespace HanziDefend.View
{
    /// <summary>Runtime single-scene projection of GameFlow's three-stage state machine.</summary>
    [DisallowMultipleComponent]
    public sealed class M1GameBootstrap : MonoBehaviour, IBattleViewCommands
    {
        private const string CommanderId = "cmd_bei";

        private GameObject deployRoot;
        private GameObject battleRoot;
        private GameObject rewardRoot;
        private Font font;
        private IBattleArtSource artSource;
        private BattleView battleView;
        private BattleHudCanvas battleHud;
        private FeedbackPresenter feedback;
        private RewardScreen rewardScreen;
        private float accumulator;

        public GameConfig Config { get; private set; }

        public GameFlow Flow { get; private set; }

        public DeployScreen DeployScreen { get; private set; }

        public GameObject DeployRoot => deployRoot;

        public GameObject BattleRoot => battleRoot;

        public GameObject RewardRoot => rewardRoot;

        public bool AutoRun { get; set; } = true;

        public float SimulationSpeed { get; set; } = 8f;

        public float CommanderCooldownRemaining => Flow?.Battle == null
            ? 0f
            : Flow.Battle.GetCommanderCooldownRemaining(CommanderId);

        private void Awake()
        {
            Initialize();
        }

        public void Initialize(uint? seed = null)
        {
            if (Flow != null)
            {
                return;
            }

            Application.runInBackground = true;
            Config = RuntimeGameConfigLoader.Load();
            font = RuntimeUiFactory.LoadFont();
            artSource = new ResourcesBattleArtSource();

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                throw new InvalidOperationException("M1GameBootstrap requires the Battle scene Canvas.");
            }
            RuntimeUiFactory.EnsureEventSystem(canvas.transform);

            deployRoot = CreateRoot("DeployRoot", canvas.transform);
            battleRoot = CreateWorldRoot("BattleRoot");
            rewardRoot = CreateRoot("RewardRoot", canvas.transform);

            battleView = battleRoot.AddComponent<BattleView>();
            battleView.Initialize(Config, artSource, this);
            feedback = FeedbackPresenter.Attach(gameObject, battleRoot.transform, battleView);
            var presentationEvents = new FeedbackEventRouter(battleView, feedback);
            var hudObject = new GameObject("Battle HUD Presentation", typeof(RectTransform));
            hudObject.transform.SetParent(canvas.transform, false);
            battleHud = hudObject.AddComponent<BattleHudCanvas>();
            battleHud.Initialize(battleView, this, artSource);

            Flow = new GameFlow(
                Config,
                seed ?? unchecked((uint)DateTime.UtcNow.Ticks),
                presentationEvents,
                new MockAdService());
            feedback.BindFlow(Flow);
            Flow.PhaseChanged += HandlePhaseChanged;
            BuildDeployScreen();
            HandlePhaseChanged(Flow.Phase);
        }

        public bool TryActivateCommander()
        {
            if (Flow?.Battle == null)
            {
                return false;
            }
            try
            {
                return Flow.Battle.TryActivateCommander(CommanderId);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public void StepOnce()
        {
            if (Flow?.Phase != GameFlowPhase.Battle || Flow.Battle == null)
            {
                return;
            }
            Flow.Tick(Flow.Battle.FixedDeltaTime);
            battleView.SyncFromBoundSystem();
        }

        public void StartBattle()
        {
            battleView.PrepareForEncounter();
            Flow.StartBattle();
            battleView.BindSystem(Flow.Battle);
            battleView.SyncFromBoundSystem();
        }

        public void ShowDeploy()
        {
            if (Flow.Phase != GameFlowPhase.Deploy)
            {
                throw new InvalidOperationException("GameFlow owns phase changes; deploy cannot be shown directly.");
            }
            HandlePhaseChanged(Flow.Phase);
        }

        private void Update()
        {
            if (Flow == null || Flow.Phase != GameFlowPhase.Battle || !AutoRun || Flow.Battle == null)
            {
                return;
            }

            accumulator += Time.unscaledDeltaTime * Mathf.Max(0f, SimulationSpeed);
            while (Flow.Phase == GameFlowPhase.Battle && accumulator >= Flow.Battle.FixedDeltaTime)
            {
                accumulator -= Flow.Battle.FixedDeltaTime;
                Flow.Tick(Flow.Battle.FixedDeltaTime);
            }
            battleView.SyncFromBoundSystem();
        }

        private void BuildDeployScreen()
        {
            if (DeployScreen != null)
            {
                Destroy(DeployScreen.gameObject);
            }

            var screenObject = new GameObject("Deploy Screen", typeof(RectTransform));
            screenObject.transform.SetParent(deployRoot.transform, false);
            RuntimeUiFactory.Stretch(screenObject.GetComponent<RectTransform>());
            DeployScreen = screenObject.AddComponent<DeployScreen>();
            DeployScreen.Initialize(
                Config,
                Flow.Economy,
                artSource,
                StartBattle,
                TryMockRewardedRefresh,
                feedback.NotifyDeployment,
                TryMockRewardedUnlock);
        }

        private bool TryMockRewardedRefresh()
        {
            return Flow.Economy.TryRewardedRefresh(out _);
        }

        private bool TryMockRewardedUnlock()
        {
            return Flow.TryWatchAdForUnlockCard();
        }

        private void HandlePhaseChanged(GameFlowPhase phase)
        {
            bool deploy = phase == GameFlowPhase.Deploy;
            bool battle = phase != GameFlowPhase.Deploy;
            bool reward = phase == GameFlowPhase.Reward
                          || phase == GameFlowPhase.MajorVictory
                          || phase == GameFlowPhase.Defeat;

            deployRoot.SetActive(deploy);
            battleRoot.SetActive(battle);
            battleHud.gameObject.SetActive(battle);
            rewardRoot.SetActive(reward);

            if (phase == GameFlowPhase.Deploy)
            {
                if (DeployScreen == null || !ReferenceEquals(DeployScreen.Economy, Flow.Economy))
                {
                    BuildDeployScreen();
                }
                DeployScreen.RefreshAll();
            }
            else if (reward)
            {
                BuildRewardScreen(phase);
            }
        }

        private void BuildRewardScreen(GameFlowPhase phase)
        {
            if (rewardScreen == null)
            {
                rewardScreen = rewardRoot.AddComponent<RewardScreen>();
            }
            rewardScreen.Show(Flow, Config, artSource, phase);
        }

        private static GameObject CreateRoot(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            RuntimeUiFactory.Stretch(root.GetComponent<RectTransform>());
            return root;
        }

        private GameObject CreateWorldRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            return root;
        }

        private void OnDestroy()
        {
            feedback?.BindFlow(null);
            if (Flow != null)
            {
                Flow.PhaseChanged -= HandlePhaseChanged;
                Flow.Dispose();
            }
        }
    }
}
