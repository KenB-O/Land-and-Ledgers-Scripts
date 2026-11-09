using LandLedgers.Economy.Transport;
using LandLedgers.World.Regional;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Tests.Editor.World
{
    public sealed class PhysicalRegionalTownRuntimeTests
    {
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
