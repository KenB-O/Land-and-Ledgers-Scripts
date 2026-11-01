using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Pathing
{
    public sealed class BuildingDoorAnimatorTimeScaleTests
    {
        [TestCase(1f, 1f, 1f)]
        [TestCase(1.25f, 4f, 5f)]
        [TestCase(0.8f, 10f, 8f)]
        public void SimulationAnimationTracksAuthoredSpeedAndSimulationRate(float authoredSpeed, float simulationRate, float expected)
        {
            Assert.AreEqual(expected,
                LandLedgers.Time.SimulationAnimationScale.Resolve(authoredSpeed, false, simulationRate),
                0.0001f);
        }

        [Test]
        public void PausedSimulationStopsDoorAnimation()
        {
            Assert.AreEqual(0f, LandLedgers.Time.SimulationAnimationScale.Resolve(1.25f, true, 10f));
        }

        [TestCase(1f, 1f, 1f)]
        [TestCase(1.25f, 4f, 5f)]
        [TestCase(0.8f, 10f, 8f)]
        public void MovingActorAnimationTracksSimulationTime(float authoredSpeed, float simulationRate, float expected)
        {
            Assert.AreEqual(expected,
                LandLedgers.Time.SimulationAnimationScale.ResolveMovingActorSpeed(authoredSpeed, true, simulationRate),
                0.0001f);
        }

        [Test]
        public void MovingActorAnimationStopsWhenSimulationIsPaused()
        {
            Assert.AreEqual(0f,
                LandLedgers.Time.SimulationAnimationScale.ResolveMovingActorSpeed(1.25f, true, 0f));
        }

        [Test]
        public void StationaryActorAnimationKeepsAuthoredIdleRate()
        {
            Assert.AreEqual(1f,
                LandLedgers.Time.SimulationAnimationScale.ResolveMovingActorSpeed(1.25f, false, 10f));
        }
    }
}
