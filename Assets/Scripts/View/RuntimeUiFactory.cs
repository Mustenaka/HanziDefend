using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HanziDefend.View
{
    internal static class RuntimeUiFactory
    {
        internal static Font LoadFont()
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        internal static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            return CreateImage(name, parent, color).rectTransform;
        }

        internal static Image CreateImage(string name, Transform parent, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        internal static Text CreateText(
            string name,
            Transform parent,
            Font font,
            int size,
            FontStyle style,
            TextAnchor alignment)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
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

        internal static Button CreateButton(
            string name,
            Transform parent,
            Color color,
            Font font,
            string label,
            int fontSize = 26)
        {
            Image image = CreateImage(name, parent, color);
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            Text text = CreateText("Label", button.transform, font, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
            text.text = label;
            Stretch(text.rectTransform);
            return button;
        }

        internal static void SetAnchors(RectTransform value, Vector2 minimum, Vector2 maximum)
        {
            value.anchorMin = minimum;
            value.anchorMax = maximum;
            value.offsetMin = Vector2.zero;
            value.offsetMax = Vector2.zero;
        }

        internal static void Stretch(RectTransform value)
        {
            SetAnchors(value, Vector2.zero, Vector2.one);
        }

        internal static void EnsureEventSystem(Transform parent)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                var eventObject = new GameObject("M1 EventSystem", typeof(RectTransform), typeof(EventSystem));
                eventObject.transform.SetParent(parent, false);
                eventSystem = eventObject.GetComponent<EventSystem>();
            }

            InputSystemUIInputModule inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputSystemModule == null)
            {
                inputSystemModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
            for (int index = 0; index < modules.Length; index++)
            {
                if (modules[index] != inputSystemModule)
                {
                    modules[index].enabled = false;
                }
            }
        }
    }
}
