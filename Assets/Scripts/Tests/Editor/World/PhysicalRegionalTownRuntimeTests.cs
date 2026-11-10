using LandLedgers.Economy.Transport;
using LandLedgers.World.Regional;
using NUnit.Framework;
using UnityEngine;
using LandLedgers.World;
using System.Collections.Generic;
using System.Reflection;

namespace LandLedgers.Tests.Editor.World
{
    public sealed class PhysicalRegionalTownRuntimeTests
    {
        [Test]
        public void ReinitializationMovesExistingDestinationsIntoTheRestoredWorldFrame()
        {
            var host = new GameObject("Regional restore test");
            var worldObject = new GameObject("Regional world frame test");
            worldObject.SetActive(false);
            var settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            var runtime = host.AddComponent<PhysicalRegionalTownRuntime>();
            try
            {
                settings.generateRegionalFoundation = true;
                settings.centerTownGridOnRegionalAnchor = true;
                settings.keepOpeningTownNearSceneOrigin = true;
                var world = worldObject.AddComponent<TownWorldController>();
                world.Configure(settings, null, null);
                typeof(TownWorldController).GetField("regionalWorld", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(world, RegionalWorldGenerator.Generate(123, settings.BuildRegionalWorldGenerationSettings()));
                var assets = Resources.Load<OpeningTransportPresentationAssets>("OpeningTransportPresentationAssets");
                Assert.IsNotNull(assets);
                Assert.IsNotNull(assets.RegionalTownPrefab);
                Assert.IsTrue(runtime.Initialize(assets.RegionalTownPrefab, world));
                string location = RegionalTownRegistry.SiteLocationId(RegionalTownRegistry.Ashford, RegionalTownRegistry.Ashford.Sites[0]);
                Assert.IsTrue(runtime.TryGetDestination(location, out Transform before, out string business));
                Vector3 previousPosition = before.position;
                Vector3 shift = new Vector3(120f, 0f, -40f);
                settings.openingTownSceneCenter += shift;

                Assert.IsTrue(runtime.Initialize(assets.RegionalTownPrefab, world));
                Assert.IsTrue(runtime.TryGetDestination(location, out Transform after, out string restoredBusiness));
                Assert.AreSame(before, after, "Restore must retain runtime destination identity.");
                Assert.AreEqual(business, restoredBusiness);
                Assert.Less(Vector3.Distance(previousPosition + shift, after.position), 0.02f);
            }
            finally
            {
                var instances = (Dictionary<string, GameObject>)typeof(PhysicalRegionalTownRuntime)
                    .GetField("townInstances", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(runtime);
                foreach (var instance in instances.Values) if (instance != null) Object.DestroyImmediate(instance);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void MillbrookSiteBindingKeepsStableJourneyIdentityAndAuthoredOffset()
        {
            RegionalTownRegistry.RegionalTownDefinition town = RegionalTownRegistry.Ashford;
            RegionalTownRegistry.RegionalBusinessSite millbrook = town.Sites[0];

            Assert.IsTrue(PhysicalRegionalTownRuntime.TryGetSiteBinding(
                town, millbrook, out string locationId, out Vector3 localPosition));

            Assert.AreEqual("ashford-millbrook_grain_co", locationId);
            Assert.AreEqual(0.30f * PhysicalRegionalTownRuntime.MetresPerMile,
                localPosition.x, 0.01f);
            Assert.AreEqual(0.10f * PhysicalRegionalTownRuntime.MetresPerMile,
                localPosition.z, 0.01f);
        }

        [Test]
        public void RegionalCoordinatesPreserveFiftyAndHundredMileWorldDistances()
        {
            Vector3 ashford = PhysicalRegionalTownRuntime.RegionalMilesToWorldPoint(
                null, RegionalTownRegistry.Ashford.CentreEastMiles,
                RegionalTownRegistry.Ashford.CentreNorthMiles);
            Vector3 redfield = PhysicalRegionalTownRuntime.RegionalMilesToWorldPoint(
                null, RegionalTownRegistry.Redfield.CentreEastMiles,
                RegionalTownRegistry.Redfield.CentreNorthMiles);

            Assert.AreEqual(50f * PhysicalRegionalTownRuntime.MetresPerMile,
                new Vector2(ashford.x, ashford.z).magnitude, 0.1f);
            Assert.AreEqual(100f * PhysicalRegionalTownRuntime.MetresPerMile,
                new Vector2(redfield.x, redfield.z).magnitude, 0.2f);
        }
    }
}
