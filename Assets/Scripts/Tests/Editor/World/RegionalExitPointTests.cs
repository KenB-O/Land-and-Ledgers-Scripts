// Slice S7 (2026-11-05, Objective 24): the regional exit-point interface.
// Harness-tested here is the pure resolver seam
// (RegionalExitPointResolver.TryResolve) — gateway selection, leg split,
// bearing, day counts, round-trip offset, and the refusal contract.
//
// SCOPE NOTE — read before citing these tests as coverage of the production
// wiring: SimulationDrivers.cs is harness-EXCLUDED, so the
// TryRegisterAcceptedFreightShipment exit-point resolution, the extended
// registration log line, and the non-gating warning are source-reviewed
// only; no test in this file invokes them. What IS harness-tested here is
// the exact resolver seam that wiring calls, plus the pinned fact that the
// journey resolver never fills the grid-exit slot (HasGridExit stays
// false — Objective 25 owns that cell).
using System.Collections.Generic;
using LandLedgers.World;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    [TestFixture]
    public sealed class RegionalExitPointTests
    {
        private const string TownCenter = "town-center";
        private const string MillbrookSite = "ashford-millbrook_grain_co";
        private const string RedfieldSite = "redfield-redfield_grain_feed";

        private static JourneyModel ModelWithTowns()
        {
            var model = new JourneyModel();
            string problem = model.RegisterLocation(new JourneyLocation(TownCenter, JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f));
            Assert.IsNull(problem);
            Assert.AreEqual(20, RegionalTownRegistry.ApplyAll(model, new List<string>()));
            return model;
        }

        private static RegionalExitPoint ResolveMillbrook(JourneyModel model, out string reason)
        {
            Assert.IsTrue(RegionalExitPointResolver.TryResolve(
                model, TownCenter, MillbrookSite, out RegionalExitPoint exitPoint, out reason), reason);
            Assert.IsNotNull(exitPoint);
            return exitPoint;
        }

        [Test]
        public void Resolve_MillbrookHaul_FindsAshfordGateway()
        {
            JourneyModel model = ModelWithTowns();
            RegionalExitPoint exitPoint = ResolveMillbrook(model, out string reason);
            Assert.AreEqual(string.Empty, reason);
            Assert.AreEqual("ashford-gateway", exitPoint.GatewayLocationId);
            Assert.AreEqual(49.5f, exitPoint.GatewayRouteMiles, 0.01f);
            // Asserted via the conversion, not a literal, so the .5 rounding
            // rule is the authority's, not the test's (742 under round-half-to-even).
            Assert.AreEqual(JourneyModel.MinutesForMiles(49.5f, TravelMode.Wagon), exitPoint.GatewayRouteMinutes);
            Assert.AreEqual(1, exitPoint.GatewayTravelDays);
            Assert.AreEqual("Millbrook Grain Co.", exitPoint.DestinationLabel);
            Assert.AreEqual(TownCenter, exitPoint.OriginLocationId);
            Assert.AreEqual(MillbrookSite, exitPoint.DestinationLocationId);
        }

        [Test]
        public void Resolve_MillbrookHaul_SplitsLegsAtGateway()
        {
            JourneyModel model = ModelWithTowns();
            RegionalExitPoint exitPoint = ResolveMillbrook(model, out _);
            Assert.AreEqual(0.82f, exitPoint.OffMapLegMiles, 0.01f);
            Assert.AreEqual(12, exitPoint.OffMapLegMinutes);
            Assert.AreEqual(0, exitPoint.OffMapLegDays);
            // S4 cross-check: the resolver's full route agrees with S4's duration source.
            Assert.AreEqual(50.32f, exitPoint.RouteMiles, 0.01f);
            Assert.AreEqual(755, exitPoint.RouteMinutes);
            Assert.AreEqual(2, exitPoint.DueReturnDayOffset);
            // Note-7: per-leg minutes sum to 742 + 12 = 754, not 755 — per-leg
            // rounding, not an error; both figures are carried, neither corrected.
            Assert.AreEqual(754, exitPoint.GatewayRouteMinutes + exitPoint.OffMapLegMinutes);
            string[] expectedLegs = { TownCenter, "ashford-gateway", "ashford", MillbrookSite };
            Assert.AreEqual(expectedLegs.Length, exitPoint.RouteLegLocationIds.Count);
            for (int i = 0; i < expectedLegs.Length; i++)
                Assert.AreEqual(expectedLegs[i], exitPoint.RouteLegLocationIds[i]);
        }

        [Test]
        public void Resolve_MillbrookHaul_BearingIsNortheast()
        {
            JourneyModel model = ModelWithTowns();
            RegionalExitPoint exitPoint = ResolveMillbrook(model, out _);
            Assert.AreEqual(35.00f, exitPoint.GatewayEastMiles, 0.01f);
            Assert.AreEqual(35.00f, exitPoint.GatewayNorthMiles, 0.01f);
            Assert.IsTrue(exitPoint.GatewayEastMiles > 0f);
            Assert.IsTrue(exitPoint.GatewayNorthMiles > 0f);
        }

        [Test]
        public void Resolve_RedfieldHaul_FindsRedfieldGateway()
        {
            JourneyModel model = ModelWithTowns();
            Assert.IsTrue(RegionalExitPointResolver.TryResolve(
                model, TownCenter, RedfieldSite, out RegionalExitPoint exitPoint, out string reason), reason);
            Assert.IsNotNull(exitPoint);
            Assert.AreEqual("redfield-gateway", exitPoint.GatewayLocationId);
            Assert.AreEqual(99.5f, exitPoint.GatewayRouteMiles, 0.01f);
            Assert.AreEqual(1, exitPoint.GatewayTravelDays);
            Assert.AreEqual("Redfield Grain & Feed Co.", exitPoint.DestinationLabel);
            Assert.IsTrue(exitPoint.GatewayEastMiles < 0f, "Redfield gateway is southwest of town");
            Assert.IsTrue(exitPoint.GatewayNorthMiles < 0f, "Redfield gateway is southwest of town");
        }

        [Test]
        public void Resolve_UnknownDestination_Refuses()
        {
            JourneyModel model = ModelWithTowns();
            Assert.IsTrue(!RegionalExitPointResolver.TryResolve(
                model, TownCenter, "ashford-no-such-site", out RegionalExitPoint exitPoint, out string reason));
            Assert.IsNull(exitPoint);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(reason), "refusal must name the unknown destination");
            Assert.IsTrue(reason.Contains("ashford-no-such-site"), reason);
        }

        [Test]
        public void Resolve_NoGateway_Refuses()
        {
            // Town centre + a site, but no OffMapGateway location registered.
            var model = new JourneyModel();
            Assert.IsNull(model.RegisterLocation(new JourneyLocation(TownCenter, JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f)));
            Assert.IsNull(model.RegisterLocation(new JourneyLocation(MillbrookSite, JourneyLocationKind.Mill, "Millbrook Grain Co.", 35.65f, 35.45f)));
            Assert.IsTrue(!RegionalExitPointResolver.TryResolve(
                model, TownCenter, MillbrookSite, out RegionalExitPoint exitPoint, out string reason));
            Assert.IsNull(exitPoint);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(reason), "refusal must name the missing gateway");
            Assert.IsTrue(reason.Contains("gateway"), reason);
        }

        [Test]
        public void Resolve_NullModelAndEmptyIds_Refuse()
        {
            JourneyModel model = ModelWithTowns();
            Assert.IsTrue(!RegionalExitPointResolver.TryResolve(
                null, TownCenter, MillbrookSite, out RegionalExitPoint nullModelPoint, out string nullReason));
            Assert.IsNull(nullModelPoint);
            Assert.IsTrue(nullReason.Contains("journey model"), nullReason);
            Assert.IsTrue(!RegionalExitPointResolver.TryResolve(
                model, "", MillbrookSite, out _, out string emptyOriginReason));
            Assert.IsTrue(emptyOriginReason.Contains("origin"), emptyOriginReason);
            Assert.IsTrue(!RegionalExitPointResolver.TryResolve(
                model, TownCenter, "", out _, out string emptyDestinationReason));
            Assert.IsTrue(emptyDestinationReason.Contains("destination"), emptyDestinationReason);
            // Unknown origin refuses naming the unknown id.
            Assert.IsTrue(!RegionalExitPointResolver.TryResolve(
                model, "no-such-origin", MillbrookSite, out _, out string unknownOriginReason));
            Assert.IsTrue(unknownOriginReason.Contains("no-such-origin"), unknownOriginReason);
        }

        [Test]
        public void Resolve_GridExit_UnsetByJourneyResolver()
        {
            JourneyModel model = ModelWithTowns();
            RegionalExitPoint exitPoint = ResolveMillbrook(model, out _);
            Assert.IsTrue(!exitPoint.HasGridExit);
            Assert.AreEqual(default(GridCoord), exitPoint.GridExitCoord);
        }

        [Test]
        public void Resolve_IsDeterministicAcrossRegistrationOrder()
        {
            var ashfordFirst = new JourneyModel();
            Assert.IsNull(ashfordFirst.RegisterLocation(new JourneyLocation(TownCenter, JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f)));
            Assert.AreEqual(10, RegionalTownRegistry.ApplyTown(ashfordFirst, RegionalTownRegistry.Ashford, new List<string>()));
            Assert.AreEqual(10, RegionalTownRegistry.ApplyTown(ashfordFirst, RegionalTownRegistry.Redfield, new List<string>()));

            var redfieldFirst = new JourneyModel();
            Assert.IsNull(redfieldFirst.RegisterLocation(new JourneyLocation(TownCenter, JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f)));
            Assert.AreEqual(10, RegionalTownRegistry.ApplyTown(redfieldFirst, RegionalTownRegistry.Redfield, new List<string>()));
            Assert.AreEqual(10, RegionalTownRegistry.ApplyTown(redfieldFirst, RegionalTownRegistry.Ashford, new List<string>()));

            RegionalExitPoint first = ResolveMillbrook(ashfordFirst, out _);
            RegionalExitPoint second = ResolveMillbrook(redfieldFirst, out _);
            Assert.AreEqual(first.GatewayLocationId, second.GatewayLocationId);
            Assert.AreEqual(first.GatewayRouteMiles, second.GatewayRouteMiles, 0.001f);
            Assert.AreEqual(first.GatewayRouteMinutes, second.GatewayRouteMinutes);
            Assert.AreEqual(first.RouteMiles, second.RouteMiles, 0.001f);
            Assert.AreEqual(first.RouteMinutes, second.RouteMinutes);
        }
    }
}
