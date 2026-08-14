using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HanziDefend.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace HanziDefend.Editor
{
    [InitializeOnLoad]
    internal static class ScreenshotTool
    {
        private const int Width = 1080;
        private const int Height = 1920;

        static ScreenshotTool()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += FlushStagedScreenshots;
        }

        [MenuItem("HanziDefend/Screenshot")]
        public static void Capture()
        {
            CaptureInternal(null);
        }

        internal static string CaptureForReview(BattleReviewStage stage)
        {
            return CaptureInternal("b4-" + stage);
        }

        internal static string CaptureForQueue(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException("Queue screenshot label is required.", nameof(label));
            }

            return CaptureInternal("queue-" + label);
        }

        private static string CaptureInternal(string artifactTag)
        {
            Camera camera = Camera.main ??
                UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                    .FirstOrDefault(candidate => candidate.enabled);
            if (camera == null)
            {
                throw new InvalidOperationException("HanziDefend/Screenshot requires an enabled camera.");
            }

            string stageSuffix = string.IsNullOrWhiteSpace(artifactTag)
                ? string.Empty
                : "-" + SanitizeTag(artifactTag);
            string fileName = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + stageSuffix + ".png";
            string assetPath = "Assets/Screenshots/" + fileName;
            bool stageUntilEditMode = EditorApplication.isPlayingOrWillChangePlaymode;
            string outputDirectory = stageUntilEditMode
                ? GetStagingDirectory()
                : Path.Combine(Application.dataPath, "Screenshots");
            string absolutePath = Path.Combine(outputDirectory, fileName);
            Directory.CreateDirectory(outputDirectory);

            RenderTexture renderTexture = RenderTexture.GetTemporary(
                Width,
                Height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            Texture2D image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            var canvasStates = new List<CanvasState>();

            try
            {
                camera.targetTexture = renderTexture;
                CaptureOverlayCanvasStates(camera, canvasStates);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = renderTexture;
                image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
                image.Apply(false, false);
                File.WriteAllBytes(absolutePath, image.EncodeToPNG());
            }
            finally
            {
                try
                {
                    RestoreCanvasStates(canvasStates);
                    camera.targetTexture = previousTarget;
                    Canvas.ForceUpdateCanvases();
                }
                finally
                {
                    RenderTexture.active = previousActive;
                    UnityEngine.Object.DestroyImmediate(image);
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
            }

            if (!stageUntilEditMode)
            {
                ImportAndSelect(assetPath);
            }

            string reviewSuffix = string.IsNullOrEmpty(artifactTag)
                ? string.Empty
                : $" [{artifactTag}]";
            Debug.Log($"HanziDefend Screenshot{reviewSuffix}: {assetPath} ({Width}x{Height})");
            return assetPath;
        }

        private static string SanitizeTag(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(value
                .Trim()
                .ToLowerInvariant()
                .Select(character => invalid.Contains(character) || char.IsWhiteSpace(character)
                    ? '-'
                    : character)
                .ToArray());
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                FlushStagedScreenshots();
            }
        }

        private static void FlushStagedScreenshots()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string stagingDirectory = GetStagingDirectory();
            if (!Directory.Exists(stagingDirectory))
            {
                return;
            }

            string outputDirectory = Path.Combine(Application.dataPath, "Screenshots");
            Directory.CreateDirectory(outputDirectory);
            string lastImportedPath = null;
            foreach (string stagedPath in Directory
                .EnumerateFiles(stagingDirectory, "*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                string fileName = Path.GetFileName(stagedPath);
                string destinationPath = Path.Combine(outputDirectory, fileName);
                File.Copy(stagedPath, destinationPath, true);
                File.Delete(stagedPath);

                string assetPath = "Assets/Screenshots/" + fileName;
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                lastImportedPath = assetPath;
            }

            if (!string.IsNullOrEmpty(lastImportedPath))
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(lastImportedPath);
            }
        }

        private static string GetStagingDirectory()
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Library",
                "HanziDefendScreenshotStaging"));
        }

        private static void ImportAndSelect(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static void CaptureOverlayCanvasStates(Camera camera, List<CanvasState> states)
        {
            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    continue;
                }

                CanvasState state = new CanvasState(canvas);
                states.Add(state);
                state.PrepareForCapture(new Vector2(Width, Height));
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = Mathf.Max(1f, camera.nearClipPlane + 0.01f);

                // ScreenSpaceOverlay normally wins after all cameras. Preserve that guarantee when
                // the screenshot path temporarily projects the HUD through the scene camera.
                BattleHudCanvas battleHud = canvas.GetComponentInChildren<BattleHudCanvas>(true);
                if (battleHud != null)
                {
                    Canvas hudCanvas = battleHud.GetComponent<Canvas>();
                    if (hudCanvas != null && hudCanvas != canvas)
                    {
                        states.Add(new CanvasState(hudCanvas));
                        hudCanvas.overrideSorting = true;
                        hudCanvas.sortingOrder = BattleHudCanvas.HudSortingOrder;
                    }
                }
            }

        }

        private static void RestoreCanvasStates(IEnumerable<CanvasState> states)
        {
            foreach (CanvasState state in states)
            {
                state.Restore();
            }
        }

        private sealed class CanvasState
        {
            private readonly Canvas canvas;
            private readonly RenderMode renderMode;
            private readonly Camera worldCamera;
            private readonly float planeDistance;
            private readonly bool overrideSorting;
            private readonly int sortingOrder;
            private readonly float canvasScaleFactor;
            private readonly CanvasScaler scaler;
            private readonly bool scalerEnabled;
            private readonly CanvasScaler.ScaleMode scalerScaleMode;
            private readonly Vector2 scalerReferenceResolution;
            private readonly CanvasScaler.ScreenMatchMode scalerScreenMatchMode;
            private readonly float scalerMatchWidthOrHeight;
            private readonly float scalerConfiguredScaleFactor;

            public CanvasState(Canvas canvas)
            {
                this.canvas = canvas;
                renderMode = canvas.renderMode;
                worldCamera = canvas.worldCamera;
                planeDistance = canvas.planeDistance;
                overrideSorting = canvas.overrideSorting;
                sortingOrder = canvas.sortingOrder;
                canvasScaleFactor = canvas.scaleFactor;
                scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scalerEnabled = scaler.enabled;
                    scalerScaleMode = scaler.uiScaleMode;
                    scalerReferenceResolution = scaler.referenceResolution;
                    scalerScreenMatchMode = scaler.screenMatchMode;
                    scalerMatchWidthOrHeight = scaler.matchWidthOrHeight;
                    scalerConfiguredScaleFactor = scaler.scaleFactor;
                }
            }

            public void PrepareForCapture(Vector2 targetResolution)
            {
                if (canvas == null || scaler == null)
                {
                    return;
                }

                scaler.enabled = false;
                canvas.scaleFactor = scalerScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                    ? CalculateScaleWithScreenSizeFactor(
                        targetResolution,
                        scalerReferenceResolution,
                        scalerScreenMatchMode,
                        scalerMatchWidthOrHeight)
                    : canvasScaleFactor;
            }

            public void Restore()
            {
                if (canvas == null)
                {
                    return;
                }

                canvas.renderMode = renderMode;
                canvas.worldCamera = worldCamera;
                canvas.planeDistance = planeDistance;
                canvas.overrideSorting = overrideSorting;
                canvas.sortingOrder = sortingOrder;
                canvas.scaleFactor = canvasScaleFactor;
                if (scaler != null)
                {
                    scaler.uiScaleMode = scalerScaleMode;
                    scaler.referenceResolution = scalerReferenceResolution;
                    scaler.screenMatchMode = scalerScreenMatchMode;
                    scaler.matchWidthOrHeight = scalerMatchWidthOrHeight;
                    scaler.scaleFactor = scalerConfiguredScaleFactor;
                    scaler.enabled = scalerEnabled;
                }
            }
        }

        private static float CalculateScaleWithScreenSizeFactor(
            Vector2 targetResolution,
            Vector2 referenceResolution,
            CanvasScaler.ScreenMatchMode screenMatchMode,
            float matchWidthOrHeight)
        {
            float widthRatio = Mathf.Max(Mathf.Epsilon, targetResolution.x) /
                               Mathf.Max(Mathf.Epsilon, referenceResolution.x);
            float heightRatio = Mathf.Max(Mathf.Epsilon, targetResolution.y) /
                                Mathf.Max(Mathf.Epsilon, referenceResolution.y);
            switch (screenMatchMode)
            {
                case CanvasScaler.ScreenMatchMode.Expand:
                    return Mathf.Min(widthRatio, heightRatio);
                case CanvasScaler.ScreenMatchMode.Shrink:
                    return Mathf.Max(widthRatio, heightRatio);
                default:
                    float logWidth = Mathf.Log(widthRatio, 2f);
                    float logHeight = Mathf.Log(heightRatio, 2f);
                    return Mathf.Pow(
                        2f,
                        Mathf.Lerp(logWidth, logHeight, Mathf.Clamp01(matchWidthOrHeight)));
            }
        }
    }
}
