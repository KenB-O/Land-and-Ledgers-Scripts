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
        public void NightContrastAttenuatesMonotonicallyAcrossConfiguredPresets()
        {
            float at1x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed1x, 1f);
            float at2x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed2x, 2f);
            float at10x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed10x, 10f);
            float at25x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed25x, 25f);
            float at50x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed50x, 50f);
            float at100x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed100x, 100f);
            float at250x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed250x, 250f);
            float at500x = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed500x, 500f);
            float observe = DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Observe, 10f);

            Assert.AreEqual(1f, at1x, 0.0001f, "The 24-minute day is the full-contrast benchmark.");
            Assert.That(at1x, Is.GreaterThan(at2x));
            Assert.That(at2x, Is.GreaterThan(at10x));
            Assert.That(at10x, Is.GreaterThan(at25x));
            Assert.That(at25x, Is.GreaterThan(at50x));
            Assert.That(at50x, Is.GreaterThan(at100x));
            Assert.That(at100x, Is.GreaterThan(at250x));
            Assert.That(at250x, Is.GreaterThan(at500x));
            Assert.That(at500x, Is.InRange(0.04f, 0.06f));
            Assert.AreEqual(1f, observe, 0.0001f);
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

            float normalNight = DayNightVisualController.ResolveNightExposureStops(
                0f, SimulationSpeed.Speed1x, 1f);
            float experimentalNight = DayNightVisualController.ResolveNightExposureStops(
                0f, SimulationSpeed.Speed500x, 500f);

            Assert.AreEqual(1.2f, realTimeNight, 0.0001f);
            Assert.AreEqual(0.5f, normalNight, 0.0001f);
            Assert.AreEqual(0.025f, experimentalNight, 0.0001f);
            Assert.That(realTimeNight, Is.GreaterThan(normalNight));
            Assert.That(normalNight, Is.GreaterThan(experimentalNight));
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
            float expected = speed == SimulationSpeed.RealTime ? 2.4f : 1f;
            Assert.AreEqual(expected,
                DayNightVisualController.ResolveNightLevel(0f, speed, multiplier),
                0.0001f);
        }

        [Test]
        public void PerPresetExposureCanBeAdjustedWithoutChangingClockMultipliers()
        {
            var profile = new[]
            {
                new DayNightVisualController.SpeedNightExposurePreset(SimulationSpeed.Speed1x, 1.6f),
                new DayNightVisualController.SpeedNightExposurePreset(SimulationSpeed.Speed10x, 0.7f),
                new DayNightVisualController.SpeedNightExposurePreset(SimulationSpeed.Speed500x, 0.08f)
            };

            Assert.AreEqual(0.7f,
                DayNightVisualController.ResolveNightExposureStops(0f, SimulationSpeed.Speed10x, 10f, profile),
                0.0001f);
            Assert.AreEqual(0.05f,
                DayNightVisualController.ResolveSpeedAttenuation(SimulationSpeed.Speed500x, 500f, profile),
                0.0001f);
        }

        [Test]
        public void SpeedChangesBlendLightingWithoutAnImmediateJump()
        {
            float current = 1f;
            float blended = DayNightVisualController.SmoothNightLevel(current, 0.0625f, 0.1f, 2.5f);

            Assert.That(blended, Is.LessThan(current));
            Assert.That(blended, Is.GreaterThan(0.9f));
            Assert.That(blended, Is.GreaterThan(0.0625f));
        }
    }
}
