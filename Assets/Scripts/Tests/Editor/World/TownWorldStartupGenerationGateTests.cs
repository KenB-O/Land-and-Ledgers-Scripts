using LandLedgers.World;
using LandLedgers.Orchestration.Systems;
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

        [Test]
        public void SimulationDriversRequireSuccessfulBootstrapWhenOneExists()
        {
            Assert.IsTrue(SimulationDrivers.ShouldAdvanceSimulation(false, false, false),
                "Standalone scenes without a bootstrapper retain their existing driver behavior.");
            Assert.IsFalse(SimulationDrivers.ShouldAdvanceSimulation(true, false, false),
                "A scene with a bootstrapper must not run daily systems before startup completes.");
            Assert.IsTrue(SimulationDrivers.ShouldAdvanceSimulation(true, true, false),
                "A successfully initialized scene may advance its simulation drivers.");
            Assert.IsFalse(SimulationDrivers.ShouldAdvanceSimulation(true, true, true),
                "A startup failure is terminal for this PlayMode session and must hold all simulation drivers.");
        }
    }
}
