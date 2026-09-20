using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Time
{
    public sealed class SimulationTimeSettingsSpeedTests
    {
        [Test]
        public void GetMultiplierReturnsConfigured100xSpeed()
        {
            SimulationTimeSettings settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            try
            {
                settings.speed100xMultiplier = 100f;

                Assert.AreEqual(100f, settings.GetMultiplier(SimulationSpeed.Speed100x));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SanitizeClamps100xSpeedToNonNegative()
        {
            SimulationTimeSettings settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            try
            {
                settings.speed100xMultiplier = -25f;

                settings.Sanitize();

                Assert.AreEqual(0f, settings.speed100xMultiplier);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
