using System.Collections.Generic;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// NX-2C: weather and route conditions. Canon 4.4 — road surface, mud, snow,
    /// river ice/high/low water, ferry availability and weather alter travel
    /// time/capacity or close a route, without inventing or deleting cargo.
    /// </summary>
    [TestFixture]
    public sealed class RouteConditionTests
    {
        private JourneyModel TwoTownsWithFord()
        {
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "Town A", 0f, 0f));
            model.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "Town B", 8f, 0f));
            model.AddEdge("town-a", "town-b", 8f, "river road");
            return model;
        }

        private RouteConditionService WeatherService(float precipitation01)
        {
            var service = new RouteConditionService();
            var diag = new List<string>();
            service.SetEdgeAttributes(new JourneyEdgeAttributes
            {
                FromLocationId = "town-a",
                ToLocationId = "town-b",
                Unpaved = true,
                HasRiverCrossing = true,
            }, diag);
            // Direct weather control for determinism.
            service.Current.Season = LandLedgers.Economy.Farming.Integration.FarmSeason.Summer;
            service.Current.TempF = 75f;
            service.Current.Precipitation01 = precipitation01;
            service.Current.IsSnow = false;
            service.Current.WindMph = 10f;
            return service;
        }

        private RouteConditionService MuddyService() => WeatherService(0.5f); // muddy rain, below high-water

        [Test]
        public void Mud_SlowsWagons_MoreThanFoot()
        {
            var service = MuddyService();
            Assert.IsTrue(service.TryGetEdgeCondition("town-a", "town-b", TravelMode.Wagon,
                out float wagonMult, out string wagonClosed));
            Assert.IsTrue(service.TryGetEdgeCondition("town-a", "town-b", TravelMode.Foot,
                out float footMult, out string footClosed));
            Assert.IsEmpty(wagonClosed);
            Assert.IsEmpty(footClosed);
            Assert.AreEqual(RouteConditionService.MudWagonMultiplier, wagonMult, 0.001f);
            Assert.AreEqual(RouteConditionService.MudOtherMultiplier, footMult, 0.001f);
            Assert.Greater(wagonMult, footMult, "Mud punishes wagons hardest (Canon 4.4).");
        }

        [Test]
        public void ConditionedRoute_InflatesMinutes_NotMiles()
        {
            var model = TwoTownsWithFord();
            var service = MuddyService();
            model.ConditionProvider = service;

            JourneyRoute route = model.FindRoute("town-a", "town-b", TravelMode.Wagon);
            Assert.IsTrue(route.Found);
            Assert.AreEqual(8f, route.TotalMiles, 0.01f, "Conditions change time, not distance.");
            // 8 mi at 4 mph = 120 min × 1.6 mud = 192 min.
            Assert.AreEqual(192, route.TotalMinutes);
            Assert.IsNotEmpty(route.ConditionNote);
        }

        [Test]
        public void HighWater_ClosesFord_RoutesAround()
        {
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "A", 0f, 0f));
            model.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "B", 8f, 0f));
            model.RegisterLocation(new JourneyLocation("bridge", JourneyLocationKind.Waypoint, "Bridge", 4f, 3f));
            model.AddEdge("town-a", "town-b", 8f, "ford road");
            model.AddEdge("town-a", "bridge", 5f, "pike");
            model.AddEdge("bridge", "town-b", 5f, "pike");

            var service = WeatherService(0.8f); // heavy rain → high water
            // The ford road has a river crossing; the pike/bridge does not.
            service.SetEdgeAttributes(new JourneyEdgeAttributes
            {
                FromLocationId = "town-a", ToLocationId = "bridge", Unpaved = false,
            }, new List<string>());
            service.SetEdgeAttributes(new JourneyEdgeAttributes
            {
                FromLocationId = "bridge", ToLocationId = "town-b", Unpaved = false,
            }, new List<string>());
            model.ConditionProvider = service;

            JourneyRoute route = model.FindRoute("town-a", "town-b", TravelMode.Wagon);
            Assert.IsTrue(route.Found, "The closure is routed AROUND — cargo is never invented or deleted (Canon 4.4).");
            CollectionAssert.AreEqual(new[] { "town-a", "bridge", "town-b" }, route.LegLocationIds);
        }

        [Test]
        public void Blizzard_ClosesEverything()
        {
            var model = TwoTownsWithFord();
            var service = new RouteConditionService();
            var diag = new List<string>();
            service.SetEdgeAttributes(new JourneyEdgeAttributes
            {
                FromLocationId = "town-a", ToLocationId = "town-b", Unpaved = true,
            }, diag);
            // Force blizzard conditions directly.
            service.Current.Season = LandLedgers.Economy.Farming.Integration.FarmSeason.Winter;
            service.Current.TempF = 10f;
            service.Current.Precipitation01 = 0.9f;
            service.Current.IsSnow = true;
            service.Current.WindMph = 30f;
            model.ConditionProvider = service;

            JourneyRoute route = model.FindRoute("town-a", "town-b", TravelMode.Wagon);
            Assert.IsFalse(route.Found, "Blizzards close roads — the honest Dakota hazard.");
            Assert.IsTrue(route.Diagnostic.Contains("blizzard") || route.Diagnostic.Contains("no open route"));
        }

        [Test]
        public void FrozenRiver_FerryClosed_FordBecomesIceRoad()
        {
            var service = new RouteConditionService();
            var diag = new List<string>();
            service.SetEdgeAttributes(new JourneyEdgeAttributes
            {
                FromLocationId = "a", ToLocationId = "b", IsFerry = true,
            }, diag);
            service.SetEdgeAttributes(new JourneyEdgeAttributes
            {
                FromLocationId = "c", ToLocationId = "d", HasRiverCrossing = true,
            }, diag);
            service.Current.Season = LandLedgers.Economy.Farming.Integration.FarmSeason.Winter;
            service.Current.TempF = 10f;
            service.Current.SnowCover01 = 0.5f;
            service.Current.Precipitation01 = 0f;

            Assert.IsTrue(service.TryGetEdgeCondition("a", "b", TravelMode.Wagon,
                out _, out string ferryClosed));
            Assert.IsNotEmpty(ferryClosed, "Frozen river: ferry not running.");

            Assert.IsTrue(service.TryGetEdgeCondition("c", "d", TravelMode.Wagon,
                out float fordMult, out string fordClosed));
            Assert.IsEmpty(fordClosed, "Frozen rivers served as winter ice roads (historical).");
            Assert.AreEqual(1f, fordMult, 0.001f);
        }

        [Test]
        public void FireWeather_ExposesWindAndDryness()
        {
            var service = new RouteConditionService();
            var diag = new List<string>();
            WeatherState weather = service.AdvanceDay(200, 42, diag); // summer day, seeded
            var (windMph, dryness01) = service.GetFireWeather();
            Assert.AreEqual(weather.WindMph, windMph, 0.001f);
            Assert.AreEqual(weather.Dryness01, dryness01, 0.001f);
            Assert.GreaterOrEqual(windMph, 0f);
        }

        [Test]
        public void Weather_IsSeededDeterministic()
        {
            var diag = new List<string>();
            var a = new RouteConditionService();
            var b = new RouteConditionService();
            WeatherState wa = a.AdvanceDay(200, 42, diag);
            WeatherState wb = b.AdvanceDay(200, 42, diag);
            Assert.AreEqual(wa.TempF, wb.TempF, 0.001f);
            Assert.AreEqual(wa.Precipitation01, wb.Precipitation01, 0.001f);
            Assert.AreEqual(wa.WindMph, wb.WindMph, 0.001f);
        }

        [Test]
        public void SaveRoundTrip_PreservesConditions()
        {
            var service = MuddyService();
            var diag = new List<string>();
            service.AdvanceDay(200, 42, diag);

            RouteConditionSaveDto dto = service.CaptureSaveDto();
            var restored = new RouteConditionService();
            restored.LoadFromSaveDto(dto);

            Assert.IsTrue(restored.TryGetEdgeCondition("town-a", "town-b", TravelMode.Wagon,
                out float mult, out _));
            Assert.AreEqual(RouteConditionService.MudWagonMultiplier, mult, 0.001f);
            Assert.AreEqual(service.Current.DayIndex, restored.Current.DayIndex);
        }
    }
}
