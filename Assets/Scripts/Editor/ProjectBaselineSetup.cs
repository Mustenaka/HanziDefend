using System;
using HanziDefend.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HanziDefend.Editor
{
    internal static class ProjectBaselineSetup
    {
        private const string RendererAssetPath = "Assets/Settings/HanziDefend_2DRenderer.asset";
        private const string PipelineAssetPath = "Assets/Settings/HanziDefend_RPAsset.asset";
        private const string BattleScenePath = "Assets/Scenes/Battle.unity";

        private static readonly string[] RequiredFolders =
        {
            "Assets/Scripts/Data",
            "Assets/Scripts/Gameplay",
            "Assets/Scripts/View",
            "Assets/Scripts/Editor",
            "Assets/Scripts/Tests/EditMode",
            "Assets/Scripts/Tests/PlayMode",
            "Assets/GameData",
            "Assets/Art/Units",
            "Assets/Art/UI",
            "Assets/Art/Battlefield",
            "Assets/Scenes",
            "Assets/Screenshots"
        };

        [MenuItem("HanziDefend/Setup/Apply WO-00 Baseline")]
        public static void Apply()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isDirty)
            {
                throw new InvalidOperationException(
                    $"Save or discard changes in '{activeScene.path}' before applying the WO-00 baseline.");
            }

            EnsureFolders();
            Renderer2DData rendererData = GetOrCreateRendererData();
            UniversalRenderPipelineAsset pipelineAsset = GetOrCreatePipelineAsset(rendererData);
            ConfigureRendering(rendererData, pipelineAsset);
            ConfigurePlayerSettings();
            CreateBattleScene();
            RemoveTemplateAssets();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                $"WO-00 baseline applied. Renderer='{RendererAssetPath}', pipeline='{PipelineAssetPath}', scene='{BattleScenePath}'.");
        }

        private static void EnsureFolders()
        {
            foreach (string path in RequiredFolders)
            {
                EnsureFolder(path);
            }
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];

            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        private static Renderer2DData GetOrCreateRendererData()
        {
            Renderer2DData rendererData = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererAssetPath);
            if (rendererData != null)
            {
                return rendererData;
            }

            if (AssetDatabase.LoadMainAssetAtPath(RendererAssetPath) != null)
            {
                throw new InvalidOperationException($"An incompatible asset already exists at '{RendererAssetPath}'.");
            }

            rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
            rendererData.name = "HanziDefend 2D Renderer";
            AssetDatabase.CreateAsset(rendererData, RendererAssetPath);
            return rendererData;
        }

        private static UniversalRenderPipelineAsset GetOrCreatePipelineAsset(Renderer2DData rendererData)
        {
            UniversalRenderPipelineAsset pipelineAsset =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipelineAsset != null)
            {
                return pipelineAsset;
            }

            if (AssetDatabase.LoadMainAssetAtPath(PipelineAssetPath) != null)
            {
                throw new InvalidOperationException($"An incompatible asset already exists at '{PipelineAssetPath}'.");
            }

            pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
            pipelineAsset.name = "HanziDefend Game RP Asset";
            AssetDatabase.CreateAsset(pipelineAsset, PipelineAssetPath);
            return pipelineAsset;
        }

        private static void ConfigureRendering(
            Renderer2DData rendererData,
            UniversalRenderPipelineAsset pipelineAsset)
        {
            SerializedObject serializedRenderer = new SerializedObject(rendererData);
            SetSerializedInteger(serializedRenderer, "m_DefaultMaterialType", 1);
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rendererData);

            pipelineAsset.supportsHDR = false;
            pipelineAsset.shadowDistance = 0f;
            pipelineAsset.volumeProfile = null;

            SerializedObject serializedPipeline = new SerializedObject(pipelineAsset);
            SetSerializedInteger(serializedPipeline, "m_MainLightRenderingMode", 0);
            SetSerializedInteger(serializedPipeline, "m_AdditionalLightsRenderingMode", 0);
            SetSerializedBoolean(serializedPipeline, "m_MainLightShadowsSupported", false);
            SetSerializedBoolean(serializedPipeline, "m_AdditionalLightShadowsSupported", false);
            SetSerializedBoolean(serializedPipeline, "m_AnyShadowsSupported", false);
            SetSerializedBoolean(serializedPipeline, "m_SoftShadowsSupported", false);
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipelineAsset);

            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            AssignPipelineToEveryQualityLevel(pipelineAsset);
        }

        private static void AssignPipelineToEveryQualityLevel(RenderPipelineAsset pipelineAsset)
        {
            int originalQualityLevel = QualitySettings.GetQualityLevel();

            for (int qualityLevel = 0; qualityLevel < QualitySettings.names.Length; qualityLevel++)
            {
                QualitySettings.SetQualityLevel(qualityLevel, false);
                QualitySettings.renderPipeline = pipelineAsset;
            }

            QualitySettings.SetQualityLevel(originalQualityLevel, false);
        }

        private static void SetSerializedBoolean(
            SerializedObject serializedObject,
            string propertyName,
            bool value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Required serialized property '{propertyName}' was not found on '{serializedObject.targetObject.name}'.");
            }

            property.boolValue = value;
        }

        private static void SetSerializedInteger(
            SerializedObject serializedObject,
            string propertyName,
            int value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Required serialized property '{propertyName}' was not found on '{serializedObject.targetObject.name}'.");
            }

            property.intValue = value;
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.defaultScreenWidth = 1080;
            PlayerSettings.defaultScreenHeight = 1920;
            PlayerSettings.defaultWebScreenWidth = 1080;
            PlayerSettings.defaultWebScreenHeight = 1920;
            PlayerSettings.allowHDRDisplaySupport = false;
        }

        private static void CreateBattleScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 9.6f;
            camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraObject.AddComponent<AudioListener>();

            UniversalAdditionalCameraData cameraData =
                cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;
            cameraData.allowHDROutput = false;

            GameObject canvasObject = new GameObject("Canvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject bootstrapObject = new GameObject("Bootstrap");
            bootstrapObject.AddComponent<BattleBootstrap>();

            if (!EditorSceneManager.SaveScene(scene, BattleScenePath))
            {
                throw new InvalidOperationException($"Failed to save scene '{BattleScenePath}'.");
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BattleScenePath, true)
            };
        }

        private static void RemoveTemplateAssets()
        {
            AssetDatabase.DeleteAsset("Assets/Readme.asset");

            // Delete editor scripts last because their removal can trigger a domain reload.
            if (AssetDatabase.IsValidFolder("Assets/TutorialInfo"))
            {
                AssetDatabase.DeleteAsset("Assets/TutorialInfo");
            }
        }
    }
}
