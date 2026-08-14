using System;
using System.IO;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Tests.EditMode
{
    public sealed class ArtPipelineEditorTests
    {
        private string testFolderAssetPath;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(testFolderAssetPath))
            {
                AssetDatabase.DeleteAsset(testFolderAssetPath);
                testFolderAssetPath = null;
            }
        }

        // Baseline updated for WO-E3: `SpriteAtlasSourcePostprocessor` (order 1000) runs after
        // `ArtPostprocessor` and forces every Units/UI/Icons PNG to import losslessly, because
        // SpriteAtlas packing reads the source pixels. Final page encoding is owned by
        // SpriteAtlasGenerator's platform settings, not by the source importer, so asserting
        // `Compressed` here now contradicts the shipping pipeline.
        [Test]
        public void UnitPng_ImportsAsBottomCenteredAtlasSourceSprite()
        {
            string folderName = "__ArtPipelineTests_" + Guid.NewGuid().ToString("N");
            testFolderAssetPath = "Assets/Art/Units/" + folderName;
            AssetDatabase.CreateFolder("Assets/Art/Units", folderName);
            string textureAssetPath = testFolderAssetPath + "/idle.png";
            WriteTestPng(textureAssetPath);

            AssetDatabase.ImportAsset(textureAssetPath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(textureAssetPath);
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(ArtPostprocessor.PixelsPerUnit));
            var spriteSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(spriteSettings);
            Assert.That(spriteSettings.spriteAlignment, Is.EqualTo((int)SpriteAlignment.Custom));
            Assert.That(spriteSettings.spritePivot.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(spriteSettings.spritePivot.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.maxTextureSize, Is.LessThanOrEqualTo(1024));
            // Atlas sources stay lossless and un-crunched so packing cannot inherit block artefacts.
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.crunchedCompression, Is.False);

            TextureImporterPlatformSettings webGlSettings =
                importer.GetPlatformTextureSettings("WebGL");
            Assert.That(webGlSettings.overridden, Is.True);
            Assert.That(webGlSettings.maxTextureSize, Is.LessThanOrEqualTo(1024));
            Assert.That(
                webGlSettings.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(webGlSettings.crunchedCompression, Is.False);
        }

        [Test]
        public void NonAtlasArtPng_KeepsCompressedImportOutsideTheAtlasRoots()
        {
            // Guards the other half of the WO-E3 split: only the three atlas source roots are
            // exempt. Anything else under Assets/Art must still import compressed.
            string folderName = "__ArtPipelineTests_" + Guid.NewGuid().ToString("N");
            testFolderAssetPath = "Assets/Art/FX/" + folderName;
            AssetDatabase.CreateFolder("Assets/Art/FX", folderName);
            string textureAssetPath = testFolderAssetPath + "/spark.png";
            WriteTestPng(textureAssetPath);

            AssetDatabase.ImportAsset(textureAssetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)TextureImporter.GetAtPath(textureAssetPath);
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.maxTextureSize, Is.LessThanOrEqualTo(1024));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Compressed));
            Assert.That(
                importer.GetPlatformTextureSettings("WebGL").textureCompression,
                Is.EqualTo(TextureImporterCompression.Compressed));
        }

        [Test]
        public void Profiles_KeepUiCenteredAndBackgroundTilesRepeatable()
        {
            ArtImportProfile ui = ArtPostprocessor.ResolveProfile("Assets/Art/UI/panel.png");
            ArtImportProfile tile =
                ArtPostprocessor.ResolveProfile("Assets\\Art\\Background\\zhan_chang_tile.png");

            Assert.That(ui.Pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(ui.Alignment, Is.EqualTo((int)SpriteAlignment.Center));
            Assert.That(ui.WrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(tile.Pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(tile.WrapMode, Is.EqualTo(TextureWrapMode.Repeat));
        }

        [Test]
        public void Manifest_IsDeterministicAndPreservesLogicalIdsAndVariants()
        {
            string[] paths =
            {
                "Assets/Art/UI/card_bg_green.png",
                "Assets/Art/Units/zqi/idle.png",
                "Assets/Art/Commanders/cmd_bei/card.png",
                "Assets/Art/Background/zhan_chang_tile.png",
                "Assets/Art/Units/zqi/readme.txt",
                "Docs/Art/UI/ignored.png"
            };

            string forward = ArtManifestGenerator.BuildJson(paths);
            string reverse = ArtManifestGenerator.BuildJson(paths.Reverse());
            ArtManifestDocument manifest = JsonCodec.Deserialize<ArtManifestDocument>(forward);

            Assert.That(reverse, Is.EqualTo(forward));
            Assert.That(manifest.SchemaVersion, Is.EqualTo(1));
            Assert.That(manifest.Entries, Has.Length.EqualTo(4));
            AssertEntry(
                manifest,
                "unit/zqi/idle",
                "zqi",
                "unit",
                "idle",
                "Assets/Art/Units/zqi/idle.png");
            AssertEntry(
                manifest,
                "commander/cmd_bei/card",
                "cmd_bei",
                "commander",
                "card",
                "Assets/Art/Commanders/cmd_bei/card.png");
            AssertEntry(
                manifest,
                "background/zhan_chang_tile",
                "zhan_chang_tile",
                "background",
                string.Empty,
                "Assets/Art/Background/zhan_chang_tile.png");
        }

        [Test]
        public void Manifest_DuplicateCanonicalKeysReportBothAssets()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ArtManifestGenerator.BuildManifest(new[]
                {
                    "Assets/Art/Background/zhan_chang_tile.png",
                    "Assets/Art/Background/ZHAN_CHANG_TILE.png"
                }));

            Assert.That(exception.Message, Does.Contain("background/zhan_chang_tile"));
            Assert.That(exception.Message, Does.Contain("Assets/Art/Background/zhan_chang_tile.png"));
            Assert.That(exception.Message, Does.Contain("Assets/Art/Background/ZHAN_CHANG_TILE.png"));
        }

        private static void WriteTestPng(string assetPath)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                image.SetPixels(new[]
                {
                    Color.clear,
                    Color.red,
                    Color.green,
                    Color.blue
                });
                image.Apply(false, false);
                string absolutePath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    assetPath.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllBytes(absolutePath, image.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static void AssertEntry(
            ArtManifestDocument manifest,
            string key,
            string id,
            string category,
            string variant,
            string path)
        {
            ArtManifestEntry entry = manifest.Entries.Single(candidate => candidate.Key == key);
            Assert.That(entry.Id, Is.EqualTo(id));
            Assert.That(entry.Category, Is.EqualTo(category));
            Assert.That(entry.Variant, Is.EqualTo(variant));
            Assert.That(entry.Path, Is.EqualTo(path));
        }
    }
}
