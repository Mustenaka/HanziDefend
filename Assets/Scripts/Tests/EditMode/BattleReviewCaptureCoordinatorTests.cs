using System;
using System.Reflection;
using HanziDefend.Editor;
using NUnit.Framework;
using UnityEngine;

namespace HanziDefend.Tests.EditMode
{
    [TestFixture]
    public sealed class BattleReviewCaptureCoordinatorTests
    {
        private static MethodInfo validationMethod;
        private static object openingStage;
        private static object midStage;
        private static MethodInfo scaleMethod;
        private static object matchWidthOrHeightMode;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Type coordinator = typeof(DataEditorWorkspace).Assembly.GetType(
                "HanziDefend.Editor.BattleReviewCaptureCoordinator",
                true);
            validationMethod = coordinator.GetMethod(
                "ValidateStageWaveIndex",
                BindingFlags.NonPublic | BindingFlags.Static);
            Type stageType = validationMethod.GetParameters()[0].ParameterType;
            openingStage = Enum.Parse(stageType, "Opening");
            midStage = Enum.Parse(stageType, "Mid");

            Type screenshotTool = typeof(DataEditorWorkspace).Assembly.GetType(
                "HanziDefend.Editor.ScreenshotTool",
                true);
            scaleMethod = screenshotTool.GetMethod(
                "CalculateScaleWithScreenSizeFactor",
                BindingFlags.NonPublic | BindingFlags.Static);
            Type screenMatchType = scaleMethod.GetParameters()[2].ParameterType;
            matchWidthOrHeightMode = Enum.Parse(screenMatchType, "MatchWidthOrHeight");
        }

        [Test]
        public void OpeningStage_AcceptsOnlyWaveZero()
        {
            Assert.DoesNotThrow(() =>
                validationMethod.Invoke(null, new[] { openingStage, (object)0 }));

            TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
                validationMethod.Invoke(null, new[] { openingStage, (object)10 }));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(error.InnerException.Message, Does.Contain("WAVE 10"));
        }

        [Test]
        public void MidStage_DoesNotApplyOpeningWaveGate()
        {
            Assert.DoesNotThrow(() =>
                validationMethod.Invoke(null, new[] { midStage, (object)10 }));
        }

        [Test]
        public void ScreenshotScale_ReferenceResolutionProducesUnitScale()
        {
            float result = InvokeScale(
                new Vector2(1080f, 1920f),
                new Vector2(1080f, 1920f),
                0.5f);

            Assert.That(result, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void ScreenshotScale_MatchModeUsesLogarithmicBlend()
        {
            float result = InvokeScale(
                new Vector2(2160f, 1920f),
                new Vector2(1080f, 1920f),
                0.5f);

            Assert.That(result, Is.EqualTo(Mathf.Sqrt(2f)).Within(0.0001f));
        }

        private static float InvokeScale(
            Vector2 targetResolution,
            Vector2 referenceResolution,
            float match)
        {
            return (float)scaleMethod.Invoke(
                null,
                new[]
                {
                    (object)targetResolution,
                    referenceResolution,
                    matchWidthOrHeightMode,
                    match
                });
        }
    }
}
