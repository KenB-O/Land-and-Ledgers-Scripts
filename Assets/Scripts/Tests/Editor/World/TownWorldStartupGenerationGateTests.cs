using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    [TestFixture]
    public sealed class TownWorldStartupGenerationGateTests
    {
        [Test]
        public void BootstrapOwnedGenerationRequiresConfigurationAndNoStartupFailure()
        {
            Assert.IsFalse(TownWorldController.ShouldBlockRuntimeGeneration(false, false, true),
                "Without a bootstrapper, existing standalone/editor generation behavior is unchanged.");
            Assert.IsTrue(TownWorldController.ShouldBlockRuntimeGeneration(true, false, false),
                "A bootstrapper that has not configured the world keeps fallback callers from generating it.");
            Assert.IsFalse(TownWorldController.ShouldBlockRuntimeGeneration(true, true, false),
                "A configured, healthy bootstrapper may generate the world during its startup flow.");
            Assert.IsTrue(TownWorldController.ShouldBlockRuntimeGeneration(true, true, true),
                "A visual shell must not be generated after startup validation failed.");
        }
    }
}
