using LandLedgers.Presentation;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Time
{
    public sealed class DayNightVisualControllerTests
    {
        [Test]
        public void NightEnvelopeIsDarkAtMidnightAndDaylightAtNoon()
        {
            Assert.AreEqual(1f, DayNightVisualController.ResolveNightEnvelope(0f), 0.0001f);
            Assert.AreEqual(0f, DayNightVisualController.ResolveNightEnvelope(0.5f), 0.0001f);
        }

        [Test]
        public void DawnAndDuskTransitionSmoothly()
        {
            float beforeDawn = DayNightVisualController.ResolveNightEnvelope(0.22f);
            float dawn = DayNightVisualController.ResolveNightEnvelope(0.26f);
            float afterDawn = DayNightVisualController.ResolveNightEnvelope(0.30f);
            float beforeDusk = DayNightVisualController.ResolveNightEnvelope(0.70f);
            float dusk = DayNightVisualController.ResolveNightEnvelope(0.74f);
            float afterDusk = DayNightVisualController.ResolveNightEnvelope(0.78f);

            Assert.That(beforeDawn, Is.GreaterThan(dawn));
            Assert.That(dawn, Is.GreaterThan(afterDawn));
            Assert.That(beforeDusk, Is.LessThan(dusk));
            Assert.That(dusk, Is.LessThan(afterDusk));
        }

        [Test]
        public void AcceleratedSpeedsMonotonicallyAttenuateNight()
        {
            float at1x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed1x, 1f);
            float at10x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed10x, 10f);
            float at25x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed25x, 25f);
            float at50x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed50x, 50f);
            float at100x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed100x, 100f);

            Assert.AreEqual(1f, at1x, 0.0001f);
            Assert.That(at10x, Is.LessThan(at1x));
            Assert.That(at25x, Is.LessThan(at10x));
            Assert.That(at50x, Is.LessThan(at25x));
            Assert.That(at100x, Is.LessThan(at50x));
            Assert.That(at100x, Is.GreaterThan(0f));
        }

        [Test]
        public void NightExposureStaysVisibleAtRealTimeAndAcceleratedSpeeds()
        {
            float realTimeNight = DayNightVisualController.ResolveNightExposureStops(
                0f, SimulationSpeed.RealTime, 0.0007f);
            float at25xNight = DayNightVisualController.ResolveNightExposureStops(
                0f, SimulationSpeed.Speed25x, 25f);
            float at100xNight = DayNightVisualController.ResolveNightExposureStops(
                0f, SimulationSpeed.Speed100x, 100f);

            Assert.That(realTimeNight, Is.LessThanOrEqualTo(-3.9f));
            Assert.That(at25xNight, Is.LessThan(0f));
            Assert.That(Mathf.Abs(at25xNight), Is.GreaterThan(2f));
            Assert.That(Mathf.Abs(at100xNight), Is.GreaterThan(2f));
            Assert.That(Mathf.Abs(at100xNight), Is.LessThan(Mathf.Abs(at25xNight)));
        }

        [Test]
        public void DaytimeDoesNotApplyNightExposureAtAnySpeed()
        {
            Assert.AreEqual(0f,
                DayNightVisualController.ResolveNightExposureStops(
                    0.5f, SimulationSpeed.RealTime, 0.0007f),
                0.0001f);
        }

        [TestCase(SimulationSpeed.RealTime, 0.0007f)]
        [TestCase(SimulationSpeed.Paused, 0f)]
        public void RealTimeAndPauseKeepFullNightPresentation(SimulationSpeed speed, float multiplier)
        {
            Assert.AreEqual(1f,
                DayNightVisualController.ResolveNightLevel(0f, speed, multiplier),
                0.0001f);
        }
    }
}
