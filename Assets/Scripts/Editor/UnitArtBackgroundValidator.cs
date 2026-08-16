using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor
{
    /// <summary>What one sprite's alpha looks like, and whether that is a problem.</summary>
    public readonly struct UnitArtAlphaReport
    {
        public UnitArtAlphaReport(
            string unitId, string assetPath, int width, int height, float borderOpaque, float coverage)
        {
            UnitId = unitId;
            AssetPath = assetPath;
            Width = width;
            Height = height;
            BorderOpaqueFraction = borderOpaque;
            OpaqueCoverage = coverage;
        }

        public string UnitId { get; }

        public string AssetPath { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Fraction of the outermost pixel ring that is opaque.</summary>
        public float BorderOpaqueFraction { get; }

        /// <summary>Fraction of the whole image that is opaque.</summary>
        public float OpaqueCoverage { get; }

        /// <summary>
        /// A genuine opaque backing plate: the sprite's outer ring is filled in, so the card's bed
        /// colour cannot show anywhere and the level tint is lost entirely.
        /// </summary>
        public bool HasOpaqueBackground =>
            BorderOpaqueFraction >= UnitArtBackgroundValidator.OpaqueBorderThreshold;

        /// <summary>
        /// No backing plate, but so much of the frame is painted that the artwork still decides the
        /// card's dominant colour. This is the case that actually shipped — see the class remarks.
        /// </summary>
        public bool DominatesCardColour =>
            !HasOpaqueBackground
            && OpaqueCoverage >= UnitArtBackgroundValidator.DominantCoverageThreshold;

        public bool IsClean => !HasOpaqueBackground && !DominatesCardColour;
    }

    /// <summary>
    /// Checks that unit artwork is transparent-backed, so the card's level colour shows through the
    /// way WO-C10's layering intends.
    ///
    /// <para><b>It reports two measurements, not one, and the reason is worth keeping.</b> WO-C11
    /// asked for this check because 弩车's card reads dark red instead of its level-2 blue, on the
    /// stated grounds that its sprite "自带不透明暗红背景". Measured, that is not what happened:
    /// 弩车's outer ring is <b>0.0%</b> opaque — the background really is transparent. What is true
    /// is that <b>75%</b> of the frame is painted, in a mean colour of roughly (158,70,53), so the
    /// bed survives only in the gaps and the card still reads as the artwork's colour.</para>
    ///
    /// <para>A border-only check would therefore have passed the exact sprite that prompted it. Both
    /// numbers are reported so the two failure modes stay distinguishable: an opaque backing plate is
    /// an art-pipeline defect, while heavy coverage is a legitimate art choice whose consequence the
    /// card design has to absorb.</para>
    ///
    /// <para>Warnings only, never errors: the placeholder art set still has non-compliant images and
    /// blocking the import would stop the project from opening.</para>
    /// </summary>
    public static class UnitArtBackgroundValidator
    {
        /// <summary>Alpha at or above which a pixel counts as opaque (out of 255).</summary>
        public const byte OpaqueAlpha = 16;

        /// <summary>Border-ring opacity that means the sprite carries a backing plate.</summary>
        public const float OpaqueBorderThreshold = 0.5f;

        /// <summary>
        /// Painted fraction at which the artwork, not the bed, sets the card's colour.
        ///
        /// <para>Calibrated against the sprite that prompted the check rather than guessed: 弩车
        /// paints <b>58.3%</b> of its frame and visibly turns its card dark red, while 重骑兵 (48.4%)
        /// and 铁甲兵 (44.7%) leave enough bed showing to read as their level colour. The line sits
        /// between those two groups. A first pass at 0.70 flagged nothing at all — including 弩车 —
        /// which would have been a check that could never fire.</para>
        /// </summary>
        public const float DominantCoverageThreshold = 0.55f;

        private const string UnitsRoot = "Assets/Art/Units";

        [MenuItem("HanziDefend/Validate Unit Art Backgrounds")]
        public static void ValidateAndLog()
        {
            IReadOnlyList<UnitArtAlphaReport> reports = ScanUnitArt();
            int flagged = 0;

            for (int index = 0; index < reports.Count; index++)
            {
                UnitArtAlphaReport report = reports[index];
                if (report.IsClean)
                {
                    continue;
                }

                flagged++;
                Debug.LogWarning(Describe(report));
            }

            Debug.Log(
                $"Unit art backgrounds: {reports.Count} sprites checked, {flagged} flagged. "
                + $"Thresholds: border ≥ {OpaqueBorderThreshold:P0} = opaque backing plate, "
                + $"coverage ≥ {DominantCoverageThreshold:P0} = artwork sets the card colour.");
        }

        /// <summary>The warning text for one non-compliant sprite. Names the unit and the file.</summary>
        public static string Describe(UnitArtAlphaReport report)
        {
            if (report.HasOpaqueBackground)
            {
                return $"[unit art] '{report.UnitId}' has an opaque background: "
                       + $"{report.BorderOpaqueFraction:P1} of its outer ring is filled in, so the card's "
                       + $"level colour cannot show at all. Export with a transparent background. "
                       + $"({report.AssetPath}, {report.Width}x{report.Height})";
            }

            return $"[unit art] '{report.UnitId}' is transparent-backed but paints "
                   + $"{report.OpaqueCoverage:P1} of its frame, so the artwork rather than the level "
                   + $"tint sets the card's dominant colour. Not a defect on its own — flagged so the "
                   + $"card design can account for it. ({report.AssetPath}, {report.Width}x{report.Height})";
        }

        /// <summary>Measures every unit idle sprite under <c>Assets/Art/Units</c>.</summary>
        public static IReadOnlyList<UnitArtAlphaReport> ScanUnitArt()
        {
            var reports = new List<UnitArtAlphaReport>();
            if (!Directory.Exists(UnitsRoot))
            {
                return reports;
            }

            string[] paths = Directory.GetFiles(UnitsRoot, "*.png", SearchOption.AllDirectories);
            Array.Sort(paths, StringComparer.Ordinal);
            for (int index = 0; index < paths.Length; index++)
            {
                string assetPath = paths[index].Replace('\\', '/');
                if (TryMeasure(assetPath, out UnitArtAlphaReport report))
                {
                    reports.Add(report);
                }
            }

            return reports;
        }

        private static bool TryMeasure(string assetPath, out UnitArtAlphaReport report)
        {
            report = default;

            // Decoded straight from the file rather than through the imported Sprite: the art
            // pipeline sets isReadable=false, so the imported texture's pixels are not accessible.
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(assetPath);
            }
            catch (IOException)
            {
                return false;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(bytes))
                {
                    return false;
                }
                report = Measure(UnitIdFor(assetPath), assetPath, texture);
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Measures one decoded texture. Separated so tests can drive it with fixtures.</summary>
        public static UnitArtAlphaReport Measure(string unitId, string assetPath, Texture2D texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));

            int width = texture.width;
            int height = texture.height;
            Color32[] pixels = texture.GetPixels32();

            int borderCount = 0;
            int borderOpaque = 0;
            int opaque = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isOpaque = pixels[(y * width) + x].a >= OpaqueAlpha;
                    if (isOpaque)
                    {
                        opaque++;
                    }

                    if (x != 0 && y != 0 && x != width - 1 && y != height - 1)
                    {
                        continue;
                    }

                    borderCount++;
                    if (isOpaque)
                    {
                        borderOpaque++;
                    }
                }
            }

            return new UnitArtAlphaReport(
                unitId,
                assetPath,
                width,
                height,
                borderCount == 0 ? 0f : borderOpaque / (float)borderCount,
                pixels.Length == 0 ? 0f : opaque / (float)pixels.Length);
        }

        /// <summary>`Assets/Art/Units/&lt;unitId&gt;/idle.png` — the folder is the unit id.</summary>
        private static string UnitIdFor(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath) ?? string.Empty;
            string name = Path.GetFileName(directory);
            return string.IsNullOrEmpty(name) ? assetPath : name;
        }
    }
}
