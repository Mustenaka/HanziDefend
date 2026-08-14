using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HanziDefend.Data;
using HanziDefend.View;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor
{
    [Serializable]
    public sealed class ArtManifestDocument
    {
        public int SchemaVersion { get; set; } = 1;

        public ArtManifestEntry[] Entries { get; set; } = Array.Empty<ArtManifestEntry>();
    }

    [Serializable]
    public sealed class ArtManifestEntry
    {
        public string Key { get; set; } = string.Empty;

        public string Id { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Variant { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;
    }

    [InitializeOnLoad]
    public static class ArtManifestGenerator
    {
        public const string ArtRoot = "Assets/Art";
        public const string ManifestAssetPath = "Assets/GameData/art_manifest.json";
        public const string RuntimeCatalogAssetPath =
            "Assets/Resources/HanziDefendArtCatalog.asset";
        public const string RuntimeConfigAssetPath =
            "Assets/Resources/HanziDefendGameConfig.asset";

        private const string ArtRootWithSlash = ArtRoot + "/";
        private static bool regenerationQueued;

        static ArtManifestGenerator()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            QueueRegeneration();
        }

        [MenuItem("HanziDefend/Art/Regenerate Manifest")]
        public static void RegenerateFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Exit Play Mode before regenerating the art manifest.");
                return;
            }

            ArtManifestDocument manifest = Regenerate();
            Debug.Log(
                $"Art manifest generated: '{ManifestAssetPath}' ({manifest.Entries.Length} entries).");
        }

        public static ArtManifestDocument Regenerate()
        {
            string[] paths = FindArtPngAssetPaths();
            ArtManifestDocument manifest = BuildManifest(paths);
            string json = Serialize(manifest);

            if (WriteIfChanged(ManifestAssetPath, json))
            {
                AssetDatabase.ImportAsset(ManifestAssetPath, ImportAssetOptions.ForceUpdate);
            }

            RegenerateRuntimeCatalog(manifest);
            RegenerateRuntimeConfigBundle();

            return manifest;
        }

        private static void RegenerateRuntimeCatalog(ArtManifestDocument manifest)
        {
            const string resourcesDirectory = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesDirectory))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            BattleArtCatalog catalog =
                AssetDatabase.LoadAssetAtPath<BattleArtCatalog>(RuntimeCatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BattleArtCatalog>();
                AssetDatabase.CreateAsset(catalog, RuntimeCatalogAssetPath);
            }

            catalog.Entries = manifest.Entries
                .Select(entry => new BattleArtCatalog.Entry
                {
                    Key = entry.Key,
                    Sprite = AssetDatabase.LoadAssetAtPath<Sprite>(entry.Path)
                })
                .ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        private static void RegenerateRuntimeConfigBundle()
        {
            const string resourcesDirectory = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesDirectory))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            GameConfigTextBundle bundle =
                AssetDatabase.LoadAssetAtPath<GameConfigTextBundle>(RuntimeConfigAssetPath);
            if (bundle == null)
            {
                bundle = ScriptableObject.CreateInstance<GameConfigTextBundle>();
                AssetDatabase.CreateAsset(bundle, RuntimeConfigAssetPath);
            }

            bundle.Units = LoadRequiredTextAsset("Assets/GameData/units.json");
            bundle.Commanders = LoadRequiredTextAsset("Assets/GameData/commanders.json");
            bundle.Waves = LoadRequiredTextAsset("Assets/GameData/waves.json");
            bundle.Levels = LoadRequiredTextAsset("Assets/GameData/levels.json");
            bundle.Effects = LoadRequiredTextAsset("Assets/GameData/effects.json");
            bundle.Economy = LoadRequiredTextAsset("Assets/GameData/economy.json");
            bundle.Feedback = LoadRequiredTextAsset("Assets/GameData/feedback.json");
            EditorUtility.SetDirty(bundle);
            AssetDatabase.SaveAssetIfDirty(bundle);
        }

        private static TextAsset LoadRequiredTextAsset(string assetPath)
        {
            TextAsset value = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (value == null)
            {
                throw new InvalidOperationException(
                    $"Runtime config source '{assetPath}' is missing or is not a TextAsset.");
            }
            return value;
        }

        public static void QueueRegeneration()
        {
            if (regenerationQueued || AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            regenerationQueued = true;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EditorApplication.delayCall += RunQueuedRegeneration;
        }

        public static ArtManifestDocument BuildManifest(IEnumerable<string> assetPaths)
        {
            if (assetPaths == null)
            {
                throw new ArgumentNullException(nameof(assetPaths));
            }

            ArtManifestEntry[] entries = assetPaths
                .Select(NormalizePath)
                .Where(ArtPostprocessor.IsArtPng)
                .Distinct(StringComparer.Ordinal)
                .Select(CreateEntry)
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ThenBy(entry => entry.Path, StringComparer.Ordinal)
                .ToArray();

            string duplicateKey = entries
                .GroupBy(entry => entry.Key, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (duplicateKey != null)
            {
                string duplicatePaths = string.Join(
                    ", ",
                    entries
                        .Where(entry => string.Equals(entry.Key, duplicateKey, StringComparison.Ordinal))
                        .Select(entry => entry.Path));
                throw new InvalidOperationException(
                    $"Art manifest key '{duplicateKey}' is produced by multiple assets: {duplicatePaths}.");
            }

            return new ArtManifestDocument
            {
                Entries = entries
            };
        }

        public static string BuildJson(IEnumerable<string> assetPaths)
        {
            return Serialize(BuildManifest(assetPaths));
        }

        private static void RunQueuedRegeneration()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            regenerationQueued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueRegeneration();
                return;
            }

            try
            {
                Regenerate();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Art manifest regeneration failed: {exception.Message}");
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && regenerationQueued)
            {
                EditorApplication.delayCall += RunQueuedRegeneration;
            }
        }

        private static string[] FindArtPngAssetPaths()
        {
            return AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(ArtPostprocessor.IsArtPng)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static ArtManifestEntry CreateEntry(string assetPath)
        {
            string relativePath = assetPath.Substring(ArtRootWithSlash.Length);
            string relativeWithoutExtension = relativePath.Substring(
                0,
                relativePath.Length - Path.GetExtension(relativePath).Length);
            string[] segments = relativeWithoutExtension.Split('/');
            string root = segments[0];
            string fileName = segments[segments.Length - 1];
            string category;
            string id;
            string variant;

            if (root.Equals("Units", StringComparison.OrdinalIgnoreCase))
            {
                category = "unit";
                id = segments.Length >= 3 ? segments[1] : fileName;
                variant = segments.Length >= 3 ? fileName : string.Empty;
            }
            else if (root.Equals("Commanders", StringComparison.OrdinalIgnoreCase))
            {
                category = "commander";
                id = segments.Length >= 3 ? segments[1] : fileName;
                variant = segments.Length >= 3 ? fileName : string.Empty;
            }
            else if (root.Equals("Buildings", StringComparison.OrdinalIgnoreCase))
            {
                category = "building";
                id = fileName;
                variant = string.Empty;
            }
            else if (root.Equals("Icons", StringComparison.OrdinalIgnoreCase))
            {
                category = "icon";
                id = fileName;
                variant = string.Empty;
            }
            else if (root.Equals("Background", StringComparison.OrdinalIgnoreCase))
            {
                category = "background";
                id = fileName;
                variant = string.Empty;
            }
            else if (root.Equals("UI", StringComparison.OrdinalIgnoreCase))
            {
                category = "ui";
                id = fileName;
                variant = string.Empty;
            }
            else if (root.Equals("FX", StringComparison.OrdinalIgnoreCase))
            {
                category = "fx";
                id = fileName;
                variant = string.Empty;
            }
            else
            {
                category = "other";
                id = fileName;
                variant = string.Empty;
            }

            category = category.ToLowerInvariant();
            id = id.ToLowerInvariant();
            variant = variant.ToLowerInvariant();
            string key = category == "other"
                ? "other/" + relativeWithoutExtension.ToLowerInvariant()
                : category + "/" + id;
            if (!string.IsNullOrEmpty(variant))
            {
                key += "/" + variant;
            }

            return new ArtManifestEntry
            {
                Key = key,
                Id = id,
                Category = category,
                Variant = variant,
                Path = assetPath
            };
        }

        private static string Serialize(ArtManifestDocument manifest)
        {
            return JsonCodec.Serialize(manifest) + Environment.NewLine;
        }

        private static bool WriteIfChanged(string assetPath, string contents)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            if (File.Exists(absolutePath)
                && string.Equals(File.ReadAllText(absolutePath), contents, StringComparison.Ordinal))
            {
                return false;
            }

            string directory = Path.GetDirectoryName(absolutePath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    $"Manifest path '{assetPath}' has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = absolutePath + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, contents, new UTF8Encoding(false));
                if (File.Exists(absolutePath))
                {
                    File.Replace(temporaryPath, absolutePath, null);
                }
                else
                {
                    File.Move(temporaryPath, absolutePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return true;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }
    }
}
