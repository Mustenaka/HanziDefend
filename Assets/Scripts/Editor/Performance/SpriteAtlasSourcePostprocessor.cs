using System;
using UnityEditor;

namespace HanziDefend.Editor.Performance
{
    /// <summary>
    /// SpriteAtlas packing reads source pixels, so the three atlas source roots must import
    /// losslessly. This processor runs after the general art profile; atlas page encoding is
    /// owned separately by SpriteAtlasGenerator platform settings.
    /// </summary>
    public sealed class SpriteAtlasSourcePostprocessor : AssetPostprocessor
    {
        private const string UnitRoot = "Assets/Art/Units/";
        private const string UiRoot = "Assets/Art/UI/";
        private const string IconRoot = "Assets/Art/Icons/";
        private const string WebGlPlatform = "WebGL";

        public override int GetPostprocessOrder()
        {
            return 1000;
        }

        private void OnPreprocessTexture()
        {
            if (!IsAtlasSource(assetPath))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;

            TextureImporterPlatformSettings webGlSettings =
                importer.GetPlatformTextureSettings(WebGlPlatform);
            webGlSettings.name = WebGlPlatform;
            webGlSettings.overridden = true;
            webGlSettings.textureCompression = TextureImporterCompression.Uncompressed;
            webGlSettings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(webGlSettings);
        }

        private static bool IsAtlasSource(string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/');
            return normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                   && (normalized.StartsWith(UnitRoot, StringComparison.OrdinalIgnoreCase)
                       || normalized.StartsWith(UiRoot, StringComparison.OrdinalIgnoreCase)
                       || normalized.StartsWith(IconRoot, StringComparison.OrdinalIgnoreCase));
        }
    }
}
