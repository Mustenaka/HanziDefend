using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.U2D;

namespace HanziDefend.Editor.Performance
{
    /// <summary>
    /// Generates reviewable SpriteAtlas assets from convention paths. Run the menu command
    /// instead of hand-editing serialized atlas YAML.
    /// </summary>
    public static class SpriteAtlasGenerator
    {
        public const string UnitAtlasPath = "Assets/Art/Atlases/BattleUnits.spriteatlas";
        public const string UiAtlasPath = "Assets/Art/Atlases/GameUI.spriteatlas";
        public const string RenderPipelineAssetPath =
            "Assets/Settings/HanziDefend_RPAsset.asset";

        private const string AtlasDirectory = "Assets/Art/Atlases";
        private const string UnitRoot = "Assets/Art/Units";
        private const string UiRoot = "Assets/Art/UI";
        private const string IconRoot = "Assets/Art/Icons";
        private const int AtlasMaxTextureSize = 1024;

        [MenuItem("HanziDefend/Performance/Rebuild Sprite Atlases")]
        public static void Rebuild()
        {
            EnsureAssetDirectory(AtlasDirectory);
            EnsureSourceSpritesPackable(UnitRoot, UiRoot, IconRoot);

            RebuildAtlas(UnitAtlasPath, UnitRoot);
            RebuildAtlas(UiAtlasPath, UiRoot, IconRoot);
            EnableDynamicBatching();
            AssetDatabase.SaveAssets();

            SpriteAtlasCoverageReport report = ValidateCoverage();
            if (!report.IsComplete)
            {
                throw new InvalidOperationException(report.ToString());
            }

            Debug.Log($"[WO-E3] {report}");
        }

        /// <summary>
        /// Enables URP dynamic batching on the render-pipeline asset used by every quality level.
        /// This keeps the setting reproducible through the same Editor workflow as the atlases.
        /// </summary>
        public static void EnableDynamicBatching()
        {
            UniversalRenderPipelineAsset pipelineAsset = LoadRenderPipelineAsset();
            if (pipelineAsset.supportsDynamicBatching)
            {
                return;
            }

            pipelineAsset.supportsDynamicBatching = true;
            EditorUtility.SetDirty(pipelineAsset);
        }

        public static bool IsDynamicBatchingEnabled()
        {
            UniversalRenderPipelineAsset pipelineAsset =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                    RenderPipelineAssetPath);
            return pipelineAsset != null && pipelineAsset.supportsDynamicBatching;
        }

        public static SpriteAtlasCoverageReport ValidateCoverage()
        {
            SpriteAtlas unitAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(UnitAtlasPath);
            SpriteAtlas uiAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(UiAtlasPath);
            var missing = new List<string>();

            int unitCount = CountCoveredSprites(unitAtlas, missing, UnitRoot);
            int uiCount = CountCoveredSprites(uiAtlas, missing, UiRoot, IconRoot);
            return new SpriteAtlasCoverageReport(unitCount, uiCount, missing.ToArray());
        }

        private static SpriteAtlas RebuildAtlas(string atlasPath, params string[] roots)
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, atlasPath);
            }

            UnityEngine.Object[] previousPackables = atlas.GetPackables();
            if (previousPackables.Length > 0)
            {
                atlas.Remove(previousPackables);
            }

            var packables = new List<UnityEngine.Object>(roots.Length);
            for (int index = 0; index < roots.Length; index++)
            {
                DefaultAsset folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(roots[index]);
                if (folder == null)
                {
                    throw new DirectoryNotFoundException(
                        $"SpriteAtlas source directory does not exist: {roots[index]}");
                }

                packables.Add(folder);
            }

            atlas.Add(packables.ToArray());
            atlas.SetIncludeInBuild(true);
            atlas.SetPackingSettings(new SpriteAtlasPackingSettings
            {
                blockOffset = 1,
                enableRotation = false,
                enableTightPacking = false,
                padding = 4
            });
            atlas.SetTextureSettings(new SpriteAtlasTextureSettings
            {
                readable = false,
                generateMipMaps = false,
                sRGB = true,
                filterMode = FilterMode.Bilinear
            });

            SetPlatformSettings(atlas, "DefaultTexturePlatform", false);
            SetPlatformSettings(atlas, "WebGL", true);
            EditorUtility.SetDirty(atlas);
            return atlas;
        }

        private static UniversalRenderPipelineAsset LoadRenderPipelineAsset()
        {
            UniversalRenderPipelineAsset pipelineAsset =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                    RenderPipelineAssetPath);
            if (pipelineAsset == null)
            {
                throw new InvalidOperationException(
                    $"URP asset does not exist or has an incompatible type: "
                    + RenderPipelineAssetPath);
            }

            return pipelineAsset;
        }

        private static void SetPlatformSettings(
            SpriteAtlas atlas,
            string platformName,
            bool overridden)
        {
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings
            {
                name = platformName,
                overridden = overridden,
                maxTextureSize = AtlasMaxTextureSize,
                format = TextureImporterFormat.RGBA32,
                textureCompression = TextureImporterCompression.Uncompressed,
                compressionQuality = 0,
                crunchedCompression = false,
                allowsAlphaSplitting = false
            });
        }

        private static void EnsureSourceSpritesPackable(params string[] roots)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", roots);
            for (int index = 0; index < guids.Length; index++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[index]);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null
                    || (importer.textureCompression == TextureImporterCompression.Uncompressed
                        && !importer.crunchedCompression))
                {
                    continue;
                }

                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.crunchedCompression = false;
                importer.SaveAndReimport();
            }
        }

        private static int CountCoveredSprites(
            SpriteAtlas atlas,
            List<string> missing,
            params string[] roots)
        {
            if (atlas == null)
            {
                for (int index = 0; index < roots.Length; index++)
                {
                    missing.Add($"missing atlas for {roots[index]}");
                }

                return 0;
            }

            UnityEngine.Object[] packables = atlas.GetPackables();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                DefaultAsset expectedRoot =
                    AssetDatabase.LoadAssetAtPath<DefaultAsset>(roots[rootIndex]);
                bool containsRoot = false;
                for (int packableIndex = 0; packableIndex < packables.Length; packableIndex++)
                {
                    if (packables[packableIndex] == expectedRoot)
                    {
                        containsRoot = true;
                        break;
                    }
                }

                if (!containsRoot)
                {
                    missing.Add($"atlas does not include root {roots[rootIndex]}");
                }
            }

            int sourceSpriteCount = 0;
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", roots);
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                {
                    if (!(assets[assetIndex] is Sprite))
                    {
                        continue;
                    }

                    sourceSpriteCount++;
                }
            }

            // A newly-created atlas is packed by Unity's normal import/build pipeline.
            // When a cache is already present, also guard against a partial packed result.
            if (atlas.spriteCount > 0 && atlas.spriteCount != sourceSpriteCount)
            {
                missing.Add(
                    $"{atlas.name} packed {atlas.spriteCount}/{sourceSpriteCount} source sprites");
            }

            return sourceSpriteCount;
        }

        private static void EnsureAssetDirectory(string assetDirectory)
        {
            if (AssetDatabase.IsValidFolder(assetDirectory))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetDirectory)?.Replace('\\', '/');
            string name = Path.GetFileName(assetDirectory);
            if (string.IsNullOrEmpty(parent)
                || string.IsNullOrEmpty(name)
                || !AssetDatabase.IsValidFolder(parent))
            {
                throw new DirectoryNotFoundException(
                    $"Cannot create SpriteAtlas asset directory: {assetDirectory}");
            }

            AssetDatabase.CreateFolder(parent, name);
        }
    }

    public readonly struct SpriteAtlasCoverageReport
    {
        public SpriteAtlasCoverageReport(
            int unitSpriteCount,
            int uiSpriteCount,
            string[] missingAssetPaths)
        {
            UnitSpriteCount = unitSpriteCount;
            UiSpriteCount = uiSpriteCount;
            MissingAssetPaths = missingAssetPaths ?? Array.Empty<string>();
        }

        public int UnitSpriteCount { get; }
        public int UiSpriteCount { get; }
        public string[] MissingAssetPaths { get; }
        public bool IsComplete => UnitSpriteCount > 0
                                  && UiSpriteCount > 0
                                  && MissingAssetPaths.Length == 0;

        public override string ToString()
        {
            string missing = MissingAssetPaths.Length == 0
                ? "none"
                : string.Join(", ", MissingAssetPaths);
            return $"SpriteAtlas coverage: units={UnitSpriteCount}, UI/icons={UiSpriteCount}, "
                   + $"missing={missing}.";
        }
    }
}
