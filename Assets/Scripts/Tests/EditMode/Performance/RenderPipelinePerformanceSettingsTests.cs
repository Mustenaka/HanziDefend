using HanziDefend.Editor.Performance;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode.Performance
{
    public sealed class RenderPipelinePerformanceSettingsTests
    {
        [Test]
        public void HanziDefendRenderPipeline_HasDynamicBatchingEnabled()
        {
            Assert.That(
                SpriteAtlasGenerator.IsDynamicBatchingEnabled(),
                Is.True,
                "Run 'HanziDefend/Performance/Rebuild Sprite Atlases' to enable "
                + "dynamic batching on "
                + SpriteAtlasGenerator.RenderPipelineAssetPath
                + ".");
        }
    }
}
