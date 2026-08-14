using HanziDefend.Data;
using HanziDefend.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HanziDefend.Tests.PlayMode
{
    [TestFixture]
    public sealed class BattleHudCanvasTests
    {
        private GameObject root;
        private Canvas canvas;
        private BattleHudCanvas hud;
        private BattleView view;
        private RejectingCommands commands;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Battle HUD Test Root");
            if (EventSystem.current != null)
            {
                Object.DestroyImmediate(EventSystem.current.gameObject);
            }

            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(root.transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var viewObject = new GameObject("Battle View");
            viewObject.transform.SetParent(root.transform, false);
            view = viewObject.AddComponent<BattleView>();
            commands = new RejectingCommands();
            var art = new NullArtSource();
            view.Initialize(GameConfig.Load(), art, commands);

            var hudObject = new GameObject("Battle HUD", typeof(RectTransform));
            hudObject.transform.SetParent(root.transform, false);
            hud = hudObject.AddComponent<BattleHudCanvas>();
            hud.Initialize(view, commands, art);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void HudCanvas_OverridesBattlefieldSortingAtMaximumOrder()
        {
            Canvas hudSortingCanvas = hud.GetComponent<Canvas>();

            Assert.That(hud.transform.IsChildOf(canvas.transform), Is.True);
            Assert.That(hudSortingCanvas, Is.Not.Null);
            Assert.That(hudSortingCanvas.overrideSorting, Is.True);
            Assert.That(hudSortingCanvas.sortingOrder, Is.EqualTo(BattleHudCanvas.HudSortingOrder));
        }

        [Test]
        public void HudEventSystem_UsesOnlyTheNewInputSystemModule()
        {
            BaseInputModule[] modules = EventSystem.current.GetComponents<BaseInputModule>();
            int enabledInputSystemModules = 0;
            int enabledLegacyModules = 0;
            for (var index = 0; index < modules.Length; index++)
            {
                BaseInputModule module = modules[index];
                if (!module.enabled)
                {
                    continue;
                }

                if (module.GetType().FullName ==
                    "UnityEngine.InputSystem.UI.InputSystemUIInputModule")
                {
                    enabledInputSystemModules++;
                }
                else if (module.GetType().FullName ==
                         "UnityEngine.EventSystems.StandaloneInputModule")
                {
                    enabledLegacyModules++;
                }
            }

            Assert.That(enabledInputSystemModules, Is.EqualTo(1));
            Assert.That(enabledLegacyModules, Is.Zero);
        }

        [Test]
        public void SkillButton_HandlesRejectedPostSettlementCommandWithoutException()
        {
            Button skill = hud.transform.Find("Top HUD/Commander Skill").GetComponent<Button>();

            Assert.DoesNotThrow(() => skill.onClick.Invoke());
            Assert.That(commands.ActivationRequests, Is.EqualTo(1));
        }

        [Test]
        public void OpeningPresentationAdvance_DoesNotAdvanceBattleWave()
        {
            float before = view.PresentationTime;

            view.AdvancePresentation(0.25f);

            Assert.That(view.PresentationTime, Is.EqualTo(before + 0.25f).Within(0.0001f));
            Assert.That(view.HudState.WaveIndex, Is.Zero);
        }

        private sealed class RejectingCommands : IBattleViewCommands
        {
            public bool AutoRun { get; set; } = true;
            public float CommanderCooldownRemaining => 0f;
            public int ActivationRequests { get; private set; }

            public bool TryActivateCommander()
            {
                ActivationRequests++;
                return false;
            }

            public void StepOnce()
            {
            }
        }

        private sealed class NullArtSource : IBattleArtSource
        {
            public Sprite Find(string key)
            {
                return null;
            }
        }
    }
}
