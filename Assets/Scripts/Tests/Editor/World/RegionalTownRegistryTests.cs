using System.Collections.Generic;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Tests.World
{
    /// <summary>Slice 12: Ashford and Redfield topology on the journey model.</summary>
    public sealed class RegionalTownRegistryTests
    {
        private static JourneyModel ModelWithHomeCentre()
        {
            var model = new JourneyModel();
            string problem = model.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f));
            Assert.IsNull(problem);
            return model;
        }

        [Test]
        public void ApplyAll_RegistersBothTowns_WithCentresGatewaysAndSites()
        {
            JourneyModel model = ModelWithHomeCentre();
            var diag = new List<string>();
            int registered = RegionalTownRegistry.ApplyAll(model, diag);
            // Ashford: centre + gateway + 8 sites = 10; Redfield: same = 10.
            Assert.AreEqual(20, registered);
            Assert.IsNotNull(model.GetLocation("ashford"));
            Assert.IsNotNull(model.GetLocation("ashford-gateway"));
            Assert.IsNotNull(model.GetLocation("ashford-millbrook_grain_co"));
            Assert.IsNotNull(model.GetLocation("redfield"));
            Assert.IsNotNull(model.GetLocation("redfield-gateway"));
            Assert.IsNotNull(model.GetLocation("redfield-redfield_grain_feed"));
        }

        [Test]
        public void HomeToAshfordCentre_IsExactlyFiftyMiles_ByWagonRoute()
        {
            JourneyModel model = ModelWithHomeCentre();
            RegionalTownRegistry.ApplyAll(model, new List<string>());
            JourneyRoute route = model.FindRoute("town-center", "ashford", TravelMode.Wagon);
            Assert.IsTrue(route.Found, route.Diagnostic);
            Assert.AreEqual(50f, route.TotalMiles, 0.001f);
            // 50 miles at 4 mph = 750 minutes.
            Assert.AreEqual(750, route.TotalMinutes);
        }

        [Test]
        public void HomeToRedfieldCentre_IsExactlyOneHundredMiles_ByWagonRoute()
        {
            JourneyModel model = ModelWithHomeCentre();
            RegionalTownRegistry.ApplyAll(model, new List<string>());
            JourneyRoute route = model.FindRoute("town-center", "redfield", TravelMode.Wagon);
            Assert.IsTrue(route.Found, route.Diagnostic);
            Assert.AreEqual(100f, route.TotalMiles, 0.001f);
            Assert.AreEqual(1500, route.TotalMinutes);
        }

        [Test]
        public void Towns_AreOnOppositeBearings_WithNoDirectEdge()
        {
            // Ashford NE (45 deg), Redfield SW (225 deg): exactly opposite.
            Assert.Greater(RegionalTownRegistry.Ashford.CentreEastMiles, 0f);
            Assert.Greater(RegionalTownRegistry.Ashford.CentreNorthMiles, 0f);
            Assert.Less(RegionalTownRegistry.Redfield.CentreEastMiles, 0f);
            Assert.Less(RegionalTownRegistry.Redfield.CentreNorthMiles, 0f);
            Assert.AreNotEqual(RegionalTownRegistry.Ashford.RoadName, RegionalTownRegistry.Redfield.RoadName);

            JourneyModel model = ModelWithHomeCentre();
            RegionalTownRegistry.ApplyAll(model, new List<string>());
            // No direct Ashford-Redfield route: the only path goes via home.
            JourneyRoute route = model.FindRoute("ashford", "redfield", TravelMode.Wagon);
            Assert.IsTrue(route.Found, route.Diagnostic);
            Assert.AreEqual(150f, route.TotalMiles, 0.001f);
            Assert.IsTrue(route.LegLocationIds.Contains("town-center"),
                "Ashford-Redfield must route via the home town centre.");
        }

        [Test]
        public void MillbrookSite_PreservesBusinessId_AndSitsOnMillStreet()
        {
            RegionalTownRegistry.RegionalBusinessSite millbrook = null;
            foreach (RegionalTownRegistry.RegionalBusinessSite site in RegionalTownRegistry.Ashford.Sites)
                if (site.BusinessId == "millbrook_grain_co") millbrook = site;
            Assert.IsNotNull(millbrook, "Millbrook Grain Co. must be an Ashford site.");
            Assert.AreEqual("Mill Street", millbrook.Street);
            Assert.AreEqual(JourneyLocationKind.Mill, millbrook.LocationKind);

            JourneyModel model = ModelWithHomeCentre();
            RegionalTownRegistry.ApplyAll(model, new List<string>());
            JourneyLocation location = model.GetLocation("ashford-millbrook_grain_co");
            Assert.IsNotNull(location);
            Assert.AreEqual("millbrook_grain_co", location.BusinessId);
            Assert.IsTrue(location.PositionAuthored);
            JourneyRoute route = model.FindRoute("town-center", "ashford-millbrook_grain_co", TravelMode.Wagon);
            Assert.IsTrue(route.Found, route.Diagnostic);
            Assert.AreEqual(50f, route.TotalMiles, 0.35f,
                "Millbrook is reached by the 50-mile road plus a short local street leg.");
        }

        [Test]
        public void Sites_HaveDistinctDeterministicPositions()
        {
            foreach (RegionalTownRegistry.RegionalTownDefinition town in RegionalTownRegistry.All)
            {
                var seen = new HashSet<string>();
                var positions = new HashSet<string>();
                foreach (RegionalTownRegistry.RegionalBusinessSite site in town.Sites)
                {
                    Assert.IsTrue(seen.Add(site.BusinessId), $"Duplicate business id {site.BusinessId} in {town.TownId}.");
                    string pos = site.LocalEastMiles.ToString("F4") + "," + site.LocalNorthMiles.ToString("F4");
                    Assert.IsTrue(positions.Add(pos), $"Two sites share position {pos} in {town.TownId}.");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(site.Street));
                }
                Assert.AreEqual(8, town.Sites.Length, $"{town.TownId} must have 8 business sites.");
            }
        }

        [Test]
        public void ApplyTown_IsIdempotent_AndRefusesWithoutHomeCentre()
        {
            JourneyModel model = ModelWithHomeCentre();
            var diag = new List<string>();
            Assert.AreEqual(10, RegionalTownRegistry.ApplyTown(model, RegionalTownRegistry.Ashford, diag));
            // Second apply: skipped, zero new locations.
            diag.Clear();
            Assert.AreEqual(0, RegionalTownRegistry.ApplyTown(model, RegionalTownRegistry.Ashford, diag));
            Assert.AreEqual(1, diag.Count);

            // No home centre at all: refusal, nothing registered.
            var bare = new JourneyModel();
            diag.Clear();
            Assert.AreEqual(-1, RegionalTownRegistry.ApplyTown(bare, RegionalTownRegistry.Redfield, diag));
            Assert.IsNull(bare.GetLocation("redfield"));
        }

        [Test]
        public void TownRebuild_PreservesRegionalLegs()
        {
            JourneyModel model = ModelWithHomeCentre();
            RegionalTownRegistry.ApplyAll(model, new List<string>());
            // Simulate a town rebuild: replace the town subgraph with a fresh
            // town-center location only. Regional locations and the legs
            // attached to town-center must survive.
            model.ReplaceTownWorldSubgraph(
                new List<JourneyLocation> { new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f) },
                new List<JourneyEdge>());
            Assert.IsNotNull(model.GetLocation("ashford"));
            Assert.IsNotNull(model.GetLocation("ashford-gateway"));
            Assert.IsNotNull(model.GetLocation("redfield"));
            JourneyRoute route = model.FindRoute("town-center", "ashford", TravelMode.Wagon);
            Assert.IsTrue(route.Found, route.Diagnostic);
            Assert.AreEqual(50f, route.TotalMiles, 0.001f);
        }
    }
}
