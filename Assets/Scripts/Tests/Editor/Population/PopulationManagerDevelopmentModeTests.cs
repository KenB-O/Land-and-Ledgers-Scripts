// Unity-only: PopulationManager is '!'-excluded from the Roslyn harness
// (sources.txt line 399) because it pulls in TownWorldController and
// SharedBusinessRuntimeManager. This test runs in the Unity Test Runner only.
// The pure-math work-capacity tests stay in WorkCapacityFactorTests in the
// harness.
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class PopulationManagerDevelopmentModeTests
    {
        [Test]
        public void PopulationMode_DevelopmentConfigurationControlsDebuffSuppression()
        {
            var host = new GameObject("Population mode test");
            DevelopmentTownConfiguration configuration = null;
            try
            {
                PopulationManager manager = host.AddComponent<PopulationManager>();
                Assert.IsFalse(manager.ConditionDebuffsSuppressed,
                    "ordinary gameplay defaults to mortality enabled and keeps condition penalties");

                configuration = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
                manager.ApplyDevelopmentPopulationSettings(configuration);
                Assert.IsFalse(manager.MortalityEnabled);
                Assert.IsFalse(manager.FertilityEnabled);
                Assert.IsTrue(manager.ConditionDebuffsSuppressed,
                    "the development configuration's mortality-off mode enables condition-debuff suppression");

                manager.MortalityEnabled = true;
                Assert.IsFalse(manager.ConditionDebuffsSuppressed,
                    "normal mortality mode restores ordinary condition penalties");
            }
            finally
            {
                if (configuration != null) UnityObject.DestroyImmediate(configuration);
                UnityObject.DestroyImmediate(host);
            }
        }
    }
}
