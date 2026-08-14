using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HanziDefend.View
{
    /// <summary>
    /// Screen-space projection of BattleView's event-built HUD state. Buttons emit
    /// commands only; this component never decides whether a battle action is legal.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleHudCanvas : MonoBehaviour
    {
        public const int HudSortingOrder = short.MaxValue;

        private static readonly Color PanelColor = new Color(0.035f, 0.045f, 0.065f, 0.9f);
        private BattleView battleView;
        private IBattleViewCommands commands;
        private IBattleArtSource artSource;
        private Font font;
        private Text waveText;
        private Text coinText;
        private Text statusText;
        private Text skillText;
        private Text autoText;
        private Image commanderImage;
        private Image campFill;
        private RectTransform thumbnailRow;
        private Canvas hostCanvas;
        private Canvas hudCanvas;
        private readonly List<GameObject> thumbnails = new List<GameObject>();
        private int renderedThumbnailCount = -1;
        private string renderedCommanderId = string.Empty;

        public void Initialize(
            BattleView view,
            IBattleViewCommands commandSink,
            IBattleArtSource battleArtSource)
        {
            battleView = view ?? throw new ArgumentNullException(nameof(view));
            commands = commandSink;
            artSource = battleArtSource ?? throw new ArgumentNullException(nameof(battleArtSource));
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildHierarchy();
            battleView.SetCanvasHudAttached(true);
            RefreshState();
        }

        private void Update()
        {
            RefreshState();
        }

        private void LateUpdate()
        {
            EnsureHudSorting();
        }

        private void BuildHierarchy()
        {
            hostCanvas = FindFirstObjectByType<Canvas>();
            if (hostCanvas == null)
            {
                throw new InvalidOperationException("Battle HUD requires the Battle scene Canvas.");
            }

            transform.SetParent(hostCanvas.transform, false);
            RectTransform root = gameObject.GetComponent<RectTransform>();
            if (root == null)
            {
                root = gameObject.AddComponent<RectTransform>();
            }
            Stretch(root);

            hudCanvas = gameObject.GetComponent<Canvas>();
            if (hudCanvas == null)
            {
                hudCanvas = gameObject.AddComponent<Canvas>();
            }

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            EnsureHudSorting();

            EnsureEventSystem(hostCanvas.transform);
            BuildTopPanel(root);
            BuildBottomPanel(root);
        }

        private void EnsureHudSorting()
        {
            if (hudCanvas == null)
            {
                return;
            }

            if (!hudCanvas.overrideSorting)
            {
                hudCanvas.overrideSorting = true;
            }

            if (hudCanvas.sortingOrder != HudSortingOrder)
            {
                hudCanvas.sortingOrder = HudSortingOrder;
            }
        }

        private void BuildTopPanel(RectTransform root)
        {
            RectTransform panel = CreatePanel("Top HUD", root, PanelColor);
            SetAnchors(panel, new Vector2(0.025f, 0.885f), new Vector2(0.975f, 0.98f));

            commanderImage = CreateImage("Commander Avatar", panel, new Color(0.16f, 0.38f, 0.66f));
            SetAnchors(commanderImage.rectTransform, new Vector2(0.02f, 0.16f), new Vector2(0.12f, 0.84f));
            commanderImage.preserveAspect = true;

            waveText = CreateText("Wave", panel, 34, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetAnchors(waveText.rectTransform, new Vector2(0.145f, 0.54f), new Vector2(0.48f, 0.92f));
            coinText = CreateText("Coins", panel, 32, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetAnchors(coinText.rectTransform, new Vector2(0.49f, 0.54f), new Vector2(0.68f, 0.92f));
            statusText = CreateText("Status", panel, 25, FontStyle.Normal, TextAnchor.MiddleLeft);
            SetAnchors(statusText.rectTransform, new Vector2(0.145f, 0.08f), new Vector2(0.68f, 0.52f));

            Button skill = CreateButton("Commander Skill", panel, new Color(0.72f, 0.43f, 0.12f));
            SetAnchors(skill.GetComponent<RectTransform>(), new Vector2(0.70f, 0.14f), new Vector2(0.815f, 0.86f));
            skillText = CreateButtonLabel(skill, "SKILL");
            skill.onClick.AddListener(() => commands?.TryActivateCommander());

            Button auto = CreateButton("Auto Spawn Toggle", panel, new Color(0.12f, 0.46f, 0.58f));
            SetAnchors(auto.GetComponent<RectTransform>(), new Vector2(0.83f, 0.54f), new Vector2(0.98f, 0.86f));
            autoText = CreateButtonLabel(auto, "AUTO");
            auto.onClick.AddListener(() =>
            {
                if (commands != null)
                {
                    commands.AutoRun = !commands.AutoRun;
                }
            });

            Button step = CreateButton("Manual Spawn Step", panel, new Color(0.22f, 0.27f, 0.34f));
            SetAnchors(step.GetComponent<RectTransform>(), new Vector2(0.83f, 0.14f), new Vector2(0.98f, 0.46f));
            CreateButtonLabel(step, "STEP");
            step.onClick.AddListener(() => commands?.StepOnce());
        }

        private void BuildBottomPanel(RectTransform root)
        {
            RectTransform panel = CreatePanel("Bottom HUD", root, PanelColor);
            SetAnchors(panel, new Vector2(0.025f, 0.02f), new Vector2(0.975f, 0.115f));

            Text label = CreateText("Camp Label", panel, 25, FontStyle.Bold, TextAnchor.MiddleLeft);
            label.text = "CAMP";
            SetAnchors(label.rectTransform, new Vector2(0.02f, 0.56f), new Vector2(0.18f, 0.9f));

            Image background = CreateImage("Camp HP Background", panel, new Color(0.06f, 0.08f, 0.11f));
            SetAnchors(background.rectTransform, new Vector2(0.02f, 0.22f), new Vector2(0.2f, 0.48f));
            campFill = CreateImage("Camp HP Fill", background.rectTransform, new Color(0.16f, 0.52f, 1f));
            Stretch(campFill.rectTransform);
            campFill.type = Image.Type.Filled;
            campFill.fillMethod = Image.FillMethod.Horizontal;
            campFill.fillOrigin = 0;

            var rowObject = new GameObject("Deployed Unit Thumbnails", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            thumbnailRow = rowObject.GetComponent<RectTransform>();
            thumbnailRow.SetParent(panel, false);
            SetAnchors(thumbnailRow, new Vector2(0.23f, 0.12f), new Vector2(0.98f, 0.88f));
            HorizontalLayoutGroup layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
        }

        private void RefreshState()
        {
            if (battleView == null || waveText == null)
            {
                return;
            }

            BattleHudState state = battleView.HudState;
            waveText.text = $"WAVE  {state.WaveIndex}/{state.WaveTotal}";
            coinText.text = $"COIN  {state.Coins}";
            statusText.text = state.Status;
            skillText.text = state.CommanderCooldown > 0.05f
                ? $"{state.CommanderCooldown:F1}s"
                : "SKILL";
            autoText.text = state.AutoRun ? "AUTO" : "MANUAL";
            campFill.fillAmount = state.AllyBaseHealthRatio;

            if (!string.Equals(renderedCommanderId, state.CommanderId, StringComparison.Ordinal))
            {
                renderedCommanderId = state.CommanderId;
                commanderImage.sprite = artSource.Find($"commander/{state.CommanderId}/avatar");
            }

            if (renderedThumbnailCount != state.DeployedUnitIds.Count)
            {
                RebuildThumbnails(state.DeployedUnitIds);
            }
        }

        private void RebuildThumbnails(IReadOnlyList<string> definitionIds)
        {
            for (var index = 0; index < thumbnails.Count; index++)
            {
                Destroy(thumbnails[index]);
            }
            thumbnails.Clear();

            for (var index = 0; index < definitionIds.Count; index++)
            {
                string definitionId = definitionIds[index];
                Image image = CreateImage($"Thumbnail {definitionId}", thumbnailRow, new Color(0.14f, 0.35f, 0.6f));
                image.sprite = artSource.Find($"unit/{definitionId}/idle");
                image.preserveAspect = true;
                LayoutElement element = image.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 82f;
                element.preferredHeight = 82f;
                thumbnails.Add(image.gameObject);
            }
            renderedThumbnailCount = definitionIds.Count;
        }

        private Text CreateButtonLabel(Button button, string value)
        {
            Text label = CreateText("Label", button.transform, 23, FontStyle.Bold, TextAnchor.MiddleCenter);
            label.text = value;
            Stretch(label.rectTransform);
            return label;
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int size,
            FontStyle style,
            TextAnchor alignment)
        {
            var gameObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            Text text = gameObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreatePanel(string objectName, Transform parent, Color color)
        {
            return CreateImage(objectName, parent, color).rectTransform;
        }

        private static Image CreateImage(string objectName, Transform parent, Color color)
        {
            var gameObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Button CreateButton(string objectName, Transform parent, Color color)
        {
            Image image = CreateImage(objectName, parent, color);
            image.raycastTarget = true;
            return image.gameObject.AddComponent<Button>();
        }

        private static void SetAnchors(RectTransform value, Vector2 minimum, Vector2 maximum)
        {
            value.anchorMin = minimum;
            value.anchorMax = maximum;
            value.offsetMin = Vector2.zero;
            value.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform value)
        {
            SetAnchors(value, Vector2.zero, Vector2.one);
        }

        private static void EnsureEventSystem(Transform canvas)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                var eventObject = new GameObject(
                    "Battle HUD EventSystem",
                    typeof(RectTransform),
                    typeof(EventSystem));
                eventObject.transform.SetParent(canvas, false);
                eventSystem = eventObject.GetComponent<EventSystem>();
            }

            InputSystemUIInputModule inputSystemModule =
                eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputSystemModule == null)
            {
                inputSystemModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
            for (var index = 0; index < modules.Length; index++)
            {
                BaseInputModule module = modules[index];
                if (module != inputSystemModule)
                {
                    module.enabled = false;
                }
            }
        }
    }
}
