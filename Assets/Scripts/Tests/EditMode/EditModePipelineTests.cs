using HanziDefend.Data;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode
{
    public sealed class EditModePipelineTests
    {
        [Test]
        public void DataAssembly_IsolatedAndLoadable()
        {
            Assert.That(
                typeof(DataAssemblyMarker).Assembly.GetName().Name,
                Is.EqualTo("HanziDefend.Data"));
        }
    }
}
