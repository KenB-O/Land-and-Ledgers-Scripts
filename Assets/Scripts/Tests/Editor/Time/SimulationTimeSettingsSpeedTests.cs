using System.Reflection;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Time
{
    public sealed class SimulationTimeSettingsSpeedTests
    {
        private static void SetTimeManagerField(TimeManager manager, string name, object value)
        {
            FieldInfo field = typeof(TimeManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "TimeManager field '" + name + "' must exist");
            field.SetValue(manager, value);
        }
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
        public void GetMultiplierReturnsConfigured250xSpeed()
        {
            SimulationTimeSettings settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            try
            {
                settings.speed250xMultiplier = 250f;
                Assert.AreEqual(250f, settings.GetMultiplier(SimulationSpeed.Speed250x));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GetMultiplierReturnsConfigured5xSpeed()
        {
            SimulationTimeSettings settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            try
            {
                settings.speed5xMultiplier = 5f;
                Assert.AreEqual(5f, settings.GetMultiplier(SimulationSpeed.Speed5x));
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

        [Test]
        public void SanitizeClamps250xSpeedToNonNegative()
        {
            SimulationTimeSettings settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            try
            {
                settings.speed250xMultiplier = -25f;
                settings.Sanitize();
                Assert.AreEqual(0f, settings.speed250xMultiplier);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GetMultiplierCapsConfiguredSpeedsAt250x()
        {
            SimulationTimeSettings settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            try
            {
                settings.speed250xMultiplier = 500f;
                settings.Sanitize();
                Assert.AreEqual(250f, settings.GetMultiplier(SimulationSpeed.Speed250x));
                Assert.AreEqual(250f, settings.speed250xMultiplier);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(60f)]
        [TestCase(30f)]
        [TestCase(120f)]
        public void RealTimeMapsToExactlyOneGameSecondPerRealSecond(float realSecondsPerGameDay)
        {
            SimulationTimeSettings settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            try
            {
                settings.realSecondsPerGameDayAt1x = realSecondsPerGameDay;
                float multiplier = settings.GetMultiplier(SimulationSpeed.RealTime);
                float gameSecondsPerRealSecond = multiplier
                    * settings.GameSecondsPerDay
                    / settings.realSecondsPerGameDayAt1x;

                Assert.AreEqual(1f, gameSecondsPerRealSecond, 0.000001f);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void PauseFromRealTimeResumesToRealTime()
        {
            var manager = new TimeManager();
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            try
            {
                SetTimeManagerField(manager, "settings", settings);

                manager.SetSpeed(SimulationSpeed.RealTime);
                Assert.AreEqual(1f, manager.CurrentGameSecondsPerRealSecond, 0.0001f);

                manager.SetPaused(true);
                Assert.IsTrue(manager.IsPaused);
                Assert.AreEqual(0f, manager.CurrentGameSecondsPerRealSecond, 0.0001f);

                manager.SetPaused(false);
                Assert.AreEqual(SimulationSpeed.RealTime, manager.CurrentSpeed,
                    "unpausing from RealTime must restore RealTime, not 1x");
                Assert.IsTrue(!manager.IsPaused);
                Assert.AreEqual(1f, manager.CurrentGameSecondsPerRealSecond, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AddingRealTimePreservesExistingSerializedSpeedValues()
        {
            Assert.AreEqual(0, (int)SimulationSpeed.Paused);
            Assert.AreEqual(1, (int)SimulationSpeed.Speed1x);
            Assert.AreEqual(5, (int)SimulationSpeed.Speed5x);
            Assert.AreEqual(100, (int)SimulationSpeed.Speed100x);
            Assert.AreEqual(101, (int)SimulationSpeed.RealTime);
            Assert.AreEqual(102, (int)SimulationSpeed.Speed250x);
        }
    }
}
