using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor
{
    public readonly struct ArtImportProfile
    {
        public ArtImportProfile(Vector2 pivot, int alignment, TextureWrapMode wrapMode)
        {
            Pivot = pivot;
            Alignment = alignment;
            WrapMode = wrapMode;
        }

        public Vector2 Pivot { get; }

        public int Alignment { get; }

        public TextureWrapMode WrapMode { get; }
    }

    public sealed class ArtPostprocessor : AssetPostprocessor
    {
        public const float PixelsPerUnit = 100f;
        public const int MaximumTextureSize = 1024;

        private const string ArtRoot = "Assets/Art/";
        private const string UnitsRoot = ArtRoot + "Units/";
        private const string BackgroundRoot = ArtRoot + "Background/";
        private const string WebGlPlatform = "WebGL";

        private static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 BottomCenterPivot = new Vector2(0.5f, 0f);

        private void OnPreprocessTexture()
        {
            if (!IsArtPng(assetPath))
            {
                return;
            }

            TextureImporter importer = (TextureImporter)assetImporter;
            ApplySettings(importer, assetPath);
        }

        public static bool IsArtPng(string path)
        {
            string normalizedPath = NormalizePath(path);
            return normalizedPath.StartsWith(ArtRoot, StringComparison.OrdinalIgnoreCase)
                && normalizedPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        public static ArtImportProfile ResolveProfile(string path)
        {
            string normalizedPath = NormalizePath(path);
            bool isUnit = normalizedPath.StartsWith(UnitsRoot, StringComparison.OrdinalIgnoreCase);
            bool isBackground = normalizedPath.StartsWith(
                BackgroundRoot,
                StringComparison.OrdinalIgnoreCase);
            bool isTile = isBackground
                && normalizedPath.EndsWith("_tile.png", StringComparison.OrdinalIgnoreCase);

            return new ArtImportProfile(
                isUnit ? BottomCenterPivot : CenterPivot,
                isUnit ? (int)SpriteAlignment.Custom : (int)SpriteAlignment.Center,
                isTile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
        }

        public static void ApplySettings(TextureImporter importer, string path)
        {
            if (importer == null)
            {
                throw new ArgumentNullException(nameof(importer));
            }

            ArtImportProfile profile = ResolveProfile(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = profile.WrapMode;
            importer.anisoLevel = 0;
            importer.maxTextureSize = MaximumTextureSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.compressionQuality = 50;
            importer.crunchedCompression = false;

            var spriteSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(spriteSettings);
            spriteSettings.spriteAlignment = profile.Alignment;
            spriteSettings.spritePivot = profile.Pivot;
            importer.SetTextureSettings(spriteSettings);

            TextureImporterPlatformSettings webGlSettings =
                importer.GetPlatformTextureSettings(WebGlPlatform);
            webGlSettings.name = WebGlPlatform;
            webGlSettings.overridden = true;
            webGlSettings.maxTextureSize = MaximumTextureSize;
            webGlSettings.format = TextureImporterFormat.Automatic;
            webGlSettings.textureCompression = TextureImporterCompression.Compressed;
            webGlSettings.compressionQuality = 50;
            webGlSettings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(webGlSettings);
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (ContainsArtAsset(importedAssets)
                || ContainsArtAsset(deletedAssets)
                || ContainsArtAsset(movedAssets)
                || ContainsArtAsset(movedFromAssetPaths))
            {
                ArtManifestGenerator.QueueRegeneration();
            }
        }

        private static bool ContainsArtAsset(IEnumerable<string> paths)
        {
            if (paths == null)
            {
                return false;
            }

            foreach (string path in paths)
            {
                string normalizedPath = NormalizePath(path);
                if (normalizedPath.Equals("Assets/Art", StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.StartsWith(ArtRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }
    }
}
