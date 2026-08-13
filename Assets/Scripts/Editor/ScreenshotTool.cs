using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor
{
    internal static class ScreenshotTool
    {
        private const int Width = 1080;
        private const int Height = 1920;

        [MenuItem("HanziDefend/Screenshot")]
        public static void Capture()
        {
            Camera camera = Camera.main ??
                UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                    .FirstOrDefault(candidate => candidate.enabled);
            if (camera == null)
            {
                throw new InvalidOperationException("HanziDefend/Screenshot requires an enabled camera.");
            }

            string fileName = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png";
            string assetPath = "Assets/Screenshots/" + fileName;
            string outputDirectory = Path.Combine(Application.dataPath, "Screenshots");
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
            List<CanvasState> canvasStates = CaptureOverlayCanvasStates(camera);

            try
            {
                Canvas.ForceUpdateCanvases();
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
                image.Apply(false, false);
                File.WriteAllBytes(absolutePath, image.EncodeToPNG());
            }
            finally
            {
                RestoreCanvasStates(canvasStates);
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(renderTexture);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            Debug.Log($"HanziDefend Screenshot: {assetPath} ({Width}x{Height})");
        }

        private static List<CanvasState> CaptureOverlayCanvasStates(Camera camera)
        {
            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            List<CanvasState> states = new List<CanvasState>();

            foreach (Canvas canvas in canvases)
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    continue;
                }

                states.Add(new CanvasState(canvas));
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = Mathf.Max(1f, camera.nearClipPlane + 0.01f);
            }

            return states;
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

            public CanvasState(Canvas canvas)
            {
                this.canvas = canvas;
                renderMode = canvas.renderMode;
                worldCamera = canvas.worldCamera;
                planeDistance = canvas.planeDistance;
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
            }
        }
    }
}
