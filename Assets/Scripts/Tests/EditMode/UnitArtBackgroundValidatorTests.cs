using System.Collections.Generic;
using System.Linq;
using HanziDefend.Editor;
using NUnit.Framework;
using UnityEngine;

namespace HanziDefend.Tests.EditMode
{
    /// <summary>
    /// WO-C11's art-pipeline guard. The content check is advisory by design — the placeholder set
    /// still has non-compliant sprites — so what these tests pin is the <b>detector</b>: given a
    /// sprite with a known alpha profile, does it classify it correctly and say something a person
    /// could act on?
    ///
    /// <para>A test that merely ran the scan and passed would be worthless, because the scan passes
    /// by construction.</para>
    /// </summary>
    public sealed class UnitArtBackgroundValidatorTests
    {
        private readonly List<Texture2D> cleanup = new List<Texture2D>();

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < cleanup.Count; index++)
            {
                if (cleanup[index] != null)
                {
                    Object.DestroyImmediate(cleanup[index]);
                }
            }
            cleanup.Clear();
        }

        /// <summary>A backing plate is the defect the work order asked for: every pixel opaque.</summary>
        [Test]
        public void AnOpaqueBackingPlate_IsFlaggedAsAnOpaqueBackground()
        {
            UnitArtAlphaReport report = Measure("probe", Fill(32, 32, 255));

            Assert.That(report.BorderOpaqueFraction, Is.EqualTo(1f).Within(0.001f));
            Assert.That(report.HasOpaqueBackground, Is.True);
            Assert.That(report.IsClean, Is.False);
            Assert.That(UnitArtBackgroundValidator.Describe(report),
                Does.Contain("probe").And.Contain("opaque background"),
                "the warning must name the unit and say what is wrong");
        }

        /// <summary>Ordinary artwork: transparent edges, a shape in the middle, nothing to report.</summary>
        [Test]
        public void ATransparentBackedSpriteWithAModestShape_IsClean()
        {
            UnitArtAlphaReport report = Measure("probe", CentredBlock(32, 32, 12, 12));

            Assert.That(report.BorderOpaqueFraction, Is.EqualTo(0f).Within(0.001f));
            Assert.That(report.HasOpaqueBackground, Is.False);
            Assert.That(report.DominatesCardColour, Is.False);
            Assert.That(report.IsClean, Is.True);
        }

        /// <summary>
        /// The case that actually shipped, and the reason this validator reports two numbers. 弩车
        /// has a fully transparent border yet paints three quarters of its frame, so a border-only
        /// check — the one the work order specified — would have passed the very sprite that
        /// prompted it. This fixture reproduces that profile.
        /// </summary>
        [Test]
        public void ATransparentBackedSpriteThatPaintsMostOfItsFrame_IsStillFlagged()
        {
            // 30x30 painted inside 32x32 = 87.9% coverage, but the outer ring stays clear.
            UnitArtAlphaReport report = Measure("probe", CentredBlock(32, 32, 30, 30));

            Assert.That(report.BorderOpaqueFraction, Is.EqualTo(0f).Within(0.001f),
                "the border is clear, exactly as 弩车's is");
            Assert.That(report.HasOpaqueBackground, Is.False,
                "so the check the work order asked for would say this sprite is fine");
            Assert.That(report.OpaqueCoverage, Is.GreaterThan(0.7f));
            Assert.That(report.DominatesCardColour, Is.True,
                "but the artwork still decides the card's colour, and that is worth saying");
            Assert.That(UnitArtBackgroundValidator.Describe(report),
                Does.Contain("transparent-backed").And.Contain("dominant colour"),
                "and the two cases must read differently so nobody chases the wrong fix");
        }

        /// <summary>Every warning has to point at a file, or it cannot be acted on.</summary>
        [Test]
        public void EveryWarning_NamesBothTheUnitAndTheFile()
        {
            UnitArtAlphaReport report = new UnitArtAlphaReport(
                "nuc", "Assets/Art/Units/nuc/idle.png", 512, 512, 0f, 0.75f);

            string message = UnitArtBackgroundValidator.Describe(report);

            Assert.That(message, Does.Contain("nuc"));
            Assert.That(message, Does.Contain("Assets/Art/Units/nuc/idle.png"));
            Assert.That(message, Does.Contain("512x512"));
        }

        /// <summary>
        /// The real scan runs and reads every unit sprite. Deliberately asserts nothing about how
        /// many are flagged: this is a pipeline advisory, and pinning today's count would turn every
        /// new piece of art into a failing build.
        /// </summary>
        [Test]
        public void TheScanReadsTheProjectsUnitArtWithoutNeedingReadableTextures()
        {
            IReadOnlyList<UnitArtAlphaReport> reports = UnitArtBackgroundValidator.ScanUnitArt();

            Assert.That(reports, Is.Not.Empty, "there is unit art in the project to check");
            Assert.That(reports.All(value => value.Width > 0 && value.Height > 0), Is.True,
                "every sprite decoded, despite the art pipeline importing with isReadable=false");
            Assert.That(reports.Any(value => value.UnitId == "nuc"), Is.True);
            Assert.That(
                reports.All(value => value.OpaqueCoverage >= 0f && value.OpaqueCoverage <= 1f), Is.True);
        }

        /// <summary>
        /// The measured fact behind the WO-C11 note: 弩车's background is transparent, contradicting
        /// the premise that it carries an opaque dark-red plate. If some future export really does
        /// add one, this goes red and the note needs rewriting.
        /// </summary>
        [Test]
        public void NucIsTransparentBacked_ContradictingTheOpaqueBackgroundDiagnosis()
        {
            UnitArtAlphaReport nuc = UnitArtBackgroundValidator.ScanUnitArt()
                .First(value => value.UnitId == "nuc");

            Assert.That(nuc.BorderOpaqueFraction, Is.LessThan(0.01f),
                "弩车's outer ring is clear — its dark red is painted artwork, not a backing plate");
            Assert.That(nuc.OpaqueCoverage, Is.GreaterThan(0.5f),
                "what is true is that it paints most of its frame");

            // The calibration that makes the check real. A threshold the motivating sprite slips
            // under is a check that can never fire, which is how the first pass at 0.70 behaved.
            Assert.That(nuc.HasOpaqueBackground, Is.False);
            Assert.That(nuc.DominatesCardColour, Is.True,
                "the shipping thresholds must actually flag the sprite this check was written for");
        }

        /// <summary>
        /// The line has to separate, not just fire. 重骑兵 and 铁甲兵 paint less than half their
        /// frames and read as their level colour on the board, so flagging them too would make the
        /// warning noise that everyone learns to ignore.
        /// </summary>
        [TestCase("zqi")]
        [TestCase("tie")]
        public void SpritesThatLeaveEnoughBedShowing_AreNotFlagged(string unitId)
        {
            UnitArtAlphaReport report = UnitArtBackgroundValidator.ScanUnitArt()
                .First(value => value.UnitId == unitId);

            Assert.That(report.OpaqueCoverage, Is.LessThan(UnitArtBackgroundValidator.DominantCoverageThreshold));
            Assert.That(report.IsClean, Is.True);
        }

        private UnitArtAlphaReport Measure(string unitId, Texture2D texture)
        {
            return UnitArtBackgroundValidator.Measure(unitId, $"Assets/Art/Units/{unitId}/idle.png", texture);
        }

        private Texture2D Fill(int width, int height, byte alpha)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cleanup.Add(texture);
            var pixels = new Color32[width * height];
            for (int index = 0; index < pixels.Length; index++)
            {
                pixels[index] = new Color32(120, 40, 30, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private Texture2D CentredBlock(int width, int height, int blockWidth, int blockHeight)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cleanup.Add(texture);
            var pixels = new Color32[width * height];
            int x0 = (width - blockWidth) / 2;
            int y0 = (height - blockHeight) / 2;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool inside = x >= x0 && x < x0 + blockWidth && y >= y0 && y < y0 + blockHeight;
                    pixels[(y * width) + x] = new Color32(120, 40, 30, inside ? (byte)255 : (byte)0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
