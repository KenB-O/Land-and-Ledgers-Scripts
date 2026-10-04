using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Farming.Delivery;
using LandLedgers.Economy.Postal;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Orchestration.Player;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Persistence;
using LandLedgers.Population;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Primitives;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// P6 — travel &amp; communication journey (First Ledger). Dead ends closed:
    ///   P6-MOVE:  PlayerDirector move intents carried caller-supplied flat
    ///             minutes — the journey model never fed player travel despite
    ///             the TTS-4 docstring promising it. TryIssueMoveIntentViaJourney
    ///             now routes through real miles/mode speed (NX-2C closures
    ///             honored).
    ///   P6-HUB:   No production code owned a JourneyModel; RouteConditionService
    ///             was never advanced or wired as a condition provider — weather
    ///             never closed roads. The hub now owns both (plus money orders,
    ///             registered mail, recruiting) with save/load round-trips.
    ///   P6-MAIL:  PostalService.AdvanceDay had no live caller — posted letters
    ///             never dispatched or arrived. Now driven daily from
    ///             SimulationDrivers.OnDayChanged (NX-2A).
    ///   P6-LETTR: RecruitmentService.AdvanceDay had no live caller — inquiries
    ///             never accrued and letters never resolved. Now driven daily
    ///             when a population exists (T2B); postal-routed letters still
    ///             resolve only on the mail item's real arrival.
    ///   P6-HAUL:  BuildFreightShipment left transitDurationGameSeconds at the
    ///             120-game-second default — every freight-company haul finished
    ///             in two minutes regardless of distance. Now set from the real
    ///             drive estimate (P2 travel-time gating holds on this path).
    /// Recorded (not faked): no production issuer of player move intents (map
    /// UI is scene work); no post offices registered in First Ledger (authored
    /// at bootstrap); no live FreightCompanyRuntime instance/driver/draft
    /// lists (scene wiring per DRIVERS.md); no newspaper business bootstrap
    /// (paper/ink/press/weekly schedule); no price-history capture callers
    /// (information source for the market-report column); CompleteDelivery /
    /// CollectFreightCharge / CollectPostageRevenue have no live callers.
    /// </summary>
    [TestFixture]
    public sealed class P6TravelCommsJourneyTests
    {
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
        }

        private static EntityId Player(int id) => EntityId.For(EntityKind.Person, id);

        private static JourneyModel TwoTownJourney()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("town", JourneyLocationKind.TownBuilding, "Town", 8f, 0f));
            journeys.AddEdge("home", "town", 8.0f, "river road");
            return journeys;
        }

        private static WorkTimeBudgetStore FreshBudgets()
        {
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            return budgets;
        }

        /// <summary>Test fake: closes one named edge in either direction (NX-2C).</summary>
        private sealed class ClosedEdgeProvider : IJourneyConditionProvider
        {
            private readonly string a;
            private readonly string b;

            public ClosedEdgeProvider(string a, string b)
            {
                this.a = a;
                this.b = b;
            }

            public bool TryGetEdgeCondition(
                string fromLocationId, string toLocationId, TravelMode mode,
                out float timeMultiplier, out string closureReason)
            {
                timeMultiplier = 1f;
                closureReason = string.Empty;
                bool match =
                    (string.Equals(fromLocationId, a, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(toLocationId, b, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(fromLocationId, b, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(toLocationId, a, StringComparison.OrdinalIgnoreCase));
                if (match)
                {
                    closureReason = "test closure";
                    return true;
                }
                return false;
            }
        }

        private sealed class TestLot : ITransitLot
        {
            public string TransitLotId => "lot-1";
            public int TransitQuantityUnits => 100;
            public void AgeInTransit(int transitDays) { }
            public int TransitSaleableUnits(int dayIndex) => 100;
            public void MarkTransitConsumed() { }
        }

        private static PopulationState ThreePersonPopulation()
        {
            var population = new PopulationState();
            population.people.Add(new PersonState
            {
                id = 1, firstName = "Employer", lastName = "Ems", age = 30,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = 1, professionId = "merchant",
            });
            population.people.Add(new PersonState
            {
                id = 3, firstName = "Stranger", lastName = "Sue", age = 30,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = 2, professionId = "laborer",
            });
            return population;
        }

        // ------------------------------------------------------------------
        // P6-MOVE: player travel fed by the journey model
        // ------------------------------------------------------------------

        [Test]
        public void PlayerMove_ViaJourney_UsesRealRoutedMinutes()
        {
            var journeys = TwoTownJourney();
            var director = new PlayerDirector(Player(1), "home");
            var budgets = FreshBudgets();

            string reason;
            Assert.IsTrue(
                director.TryIssueMoveIntentViaJourney(journeys, "town", TravelMode.Horseback, budgets, null, out reason),
                reason);

            // 8 miles at 7 mph = 68.57… min → TTS-1 whole-minute quantum.
            int expected = JourneyModel.MinutesForMiles(8f, TravelMode.Horseback);
            Assert.AreEqual(69, expected, "sanity: 8 mi horseback rounds to 69 whole minutes");
            Assert.AreEqual(expected, director.TravelMinutesRemaining);
            Assert.IsTrue(director.IsTravelling);
            Assert.AreEqual(600 - expected, budgets.GetOrCreate(Player(1)).MinutesRemaining,
                "real routed minutes gate on the TTS-1 budget (P2)");

            director.RecordMovementProgress(expected, budgets);
            Assert.IsFalse(director.IsTravelling);
            Assert.AreEqual("town", director.CurrentLocationId);
        }

        [Test]
        public void PlayerMove_ViaJourney_UnreachableDestination_Refused()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("far", JourneyLocationKind.TownBuilding, "Far", 50f, 0f));
            // No edge — no road.
            var director = new PlayerDirector(Player(2), "home");
            var budgets = FreshBudgets();
            int before = budgets.GetOrCreate(Player(2)).MinutesRemaining;

            string reason;
            Assert.IsFalse(
                director.TryIssueMoveIntentViaJourney(journeys, "far", TravelMode.Foot, budgets, null, out reason));
            Assert.IsTrue(reason.Contains("no road"), $"expected the model's diagnostic, got: {reason}");
            Assert.IsFalse(director.IsTravelling, "refused travel never starts");
            Assert.AreEqual(before, budgets.GetOrCreate(Player(2)).MinutesRemaining,
                "refused travel books no budget");
        }

        [Test]
        public void PlayerMove_ViaJourney_SamePlace_ArrivesInstantly()
        {
            var journeys = TwoTownJourney();
            var director = new PlayerDirector(Player(3), "home");
            var budgets = FreshBudgets();
            int before = budgets.GetOrCreate(Player(3)).MinutesRemaining;

            string reason;
            Assert.IsTrue(
                director.TryIssueMoveIntentViaJourney(journeys, "home", TravelMode.Foot, budgets, null, out reason),
                reason);
            Assert.IsFalse(director.IsTravelling, "no travel needed");
            Assert.AreEqual("home", director.CurrentLocationId);
            Assert.AreEqual(before, budgets.GetOrCreate(Player(3)).MinutesRemaining,
                "zero travel books zero budget");
        }

        [Test]
        public void PlayerMove_ViaJourney_ClosedEdge_RoutedAround()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("town", JourneyLocationKind.TownBuilding, "Town", 8f, 0f));
            journeys.RegisterLocation(new JourneyLocation("mill", JourneyLocationKind.Mill, "Mill", 4f, 6f));
            journeys.AddEdge("home", "town", 8.0f, "river road");
            journeys.AddEdge("home", "mill", 6.0f, "mill lane");
            journeys.AddEdge("mill", "town", 6.0f, "mill road");
            journeys.ConditionProvider = new ClosedEdgeProvider("home", "town");

            var director = new PlayerDirector(Player(4), "home");
            var budgets = FreshBudgets();

            string reason;
            Assert.IsTrue(
                director.TryIssueMoveIntentViaJourney(journeys, "town", TravelMode.Horseback, budgets, null, out reason),
                reason);

            // Closed direct edge: 12 mi the long way — never through the closure.
            int expected = JourneyModel.MinutesForMiles(12f, TravelMode.Horseback);
            Assert.AreEqual(expected, director.TravelMinutesRemaining,
                "travel routes AROUND the closed edge (NX-2C)");
        }

        // ------------------------------------------------------------------
        // P6-HUB: hub ownership + save/load of travel & comms authorities
        // ------------------------------------------------------------------

        [Test]
        public void Hub_OwnsTravelCommsAuthorities_WithConditionsAttached()
        {
            var hubObject = new GameObject("P6 Hub");
            try
            {
                var hub = hubObject.AddComponent<SimulationSystemsHub>();
                Assert.IsNotNull(hub.Journeys, "hub owns the journey model");
                Assert.IsNotNull(hub.RouteConditions, "hub owns the route-condition service");
                Assert.IsNotNull(hub.MoneyOrders, "hub owns the money-order books");
                Assert.IsNotNull(hub.RegisteredMail, "hub owns the registered-mail chain");
                Assert.IsNotNull(hub.Recruiting, "hub owns the recruitment service");
                Assert.AreSame(hub.RouteConditions, hub.Journeys.ConditionProvider,
                    "the condition provider is wired — weather closes roads");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hubObject);
            }
        }

        [Test]
        public void Hub_SaveLoad_RoundTripsJourneysConditionsAndSeed()
        {
            var hubObject = new GameObject("P6 Hub Save");
            var hub2Object = new GameObject("P6 Hub Load");
            try
            {
                var hub = hubObject.AddComponent<SimulationSystemsHub>();
                hub.Journeys.RegisterLocation(new JourneyLocation("a", JourneyLocationKind.TownBuilding, "A", 0f, 0f));
                hub.Journeys.RegisterLocation(new JourneyLocation("b", JourneyLocationKind.TownBuilding, "B", 8f, 0f));
                Assert.IsNull(hub.Journeys.AddEdge("a", "b", 8f, "road"), "edge setup failed");
                hub.RouteConditions.SetEdgeAttributes(new JourneyEdgeAttributes
                {
                    FromLocationId = "a",
                    ToLocationId = "b",
                    Unpaved = true,
                }, diag);
                hub.RouteConditions.AdvanceDay(10, 4242, diag);
                hub.WeatherSeed = 4242;

                SystemsSaveDto dto = hub.CaptureSaveDto();
                Assert.IsNotNull(dto.journeys);
                Assert.IsNotNull(dto.routeConditions);

                var hub2 = hub2Object.AddComponent<SimulationSystemsHub>();
                hub2.LoadFromSaveDto(dto, 10);

                Assert.IsNotNull(hub2.Journeys.GetLocation("a"));
                Assert.IsNotNull(hub2.Journeys.GetLocation("b"));
                JourneyRoute route = hub2.Journeys.FindRoute("a", "b", TravelMode.Wagon);
                Assert.IsTrue(route.Found, "road network survives save/load");
                Assert.AreEqual(10, hub2.RouteConditions.Current.DayIndex, "weather day survives save/load");
                Assert.AreEqual(4242, hub2.WeatherSeed, "weather seed survives save/load");
                Assert.AreSame(hub2.RouteConditions, hub2.Journeys.ConditionProvider,
                    "condition provider re-attached after load");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hubObject);
                UnityEngine.Object.DestroyImmediate(hub2Object);
            }
        }

        // ------------------------------------------------------------------
        // P6-MAIL / P6-LETTR: mail moves; recruitment letters resolve
        // ------------------------------------------------------------------

        private static void RegisterTwoOffices(PostalService postal, List<string> diagnostics)
        {
            var allWeek = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-po-a", allWeek, 7, diagnostics));
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-po-b", allWeek, 9, diagnostics));
        }

        private static JourneyModel TwoOfficeJourney()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "Town A", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "Town B", 8f, 0f));
            journeys.AddEdge("town-a", "town-b", 8.0f, "river road");
            return journeys;
        }

        [Test]
        public void Postal_LetterTravelsThroughHubJourneys()
        {
            var hubObject = new GameObject("P6 Postal Hub");
            try
            {
                var hub = hubObject.AddComponent<SimulationSystemsHub>();
                RegisterTwoOffices(hub.Postal, diag);
                MailItem item = hub.Postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                    "po-a", "po-b", 0, diag);
                Assert.IsNotNull(item);
                Assert.AreEqual(3, item.PostageCents, "1870s letter rate");

                // P6: the daily driver call — mail dispatches and arrives on
                // real schedules through the hub's journey model.
                for (int day = 0; day <= 6 && item.Status != MailStatus.Arrived; day++)
                    hub.Postal.AdvanceDay(hub.Journeys, day, diag);
                Assert.AreEqual(MailStatus.Arrived, item.Status,
                    "posted → dispatched → arrived with no teleporting");

                var collected = hub.Postal.CollectMail("po-b", 1, 6, diag);
                Assert.AreEqual(1, collected.Count, "in-person collection (Canon §7.2)");
                Assert.AreEqual(MailStatus.Collected, item.Status);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hubObject);
            }
        }

        [Test]
        public void Recruitment_PostalLetter_ResolvesOnRealArrival_NotBefore()
        {
            var hubObject = new GameObject("P6 Recruit Hub");
            try
            {
                var hub = hubObject.AddComponent<SimulationSystemsHub>();
                RegisterTwoOffices(hub.Postal, diag);
                var journeys = TwoOfficeJourney();
                var population = ThreePersonPopulation();
                var employment = new EmploymentRelationshipRegistry();

                var effort = hub.Recruiting.OpenEffort(
                    "biz-1", "clerk", RecruitmentChannel.Correspondence, 0, 5, diag);
                Assert.IsNotNull(effort);
                var letter = hub.Recruiting.SendLetter(
                    effort.EffortId, 3, "town-a", "town-b", journeys, 0, diag, hub.Postal);
                Assert.IsNotNull(letter);
                Assert.IsFalse(string.IsNullOrEmpty(letter.PostalMailId),
                    "the letter rides the real mail stream (NX-2A)");
                Assert.IsFalse(letter.Resolved);

                // Before the mail item arrives: the reply cannot exist yet.
                hub.Postal.AdvanceDay(journeys, 0, diag);
                hub.Recruiting.AdvanceDay(population, employment, 0, diag, hub.Postal);
                MailItem item = hub.Postal.GetMailItem(letter.PostalMailId);
                if (item.Status != MailStatus.Arrived && item.Status != MailStatus.Collected)
                    Assert.IsFalse(letter.Resolved, "no instant information transfer");

                // Drive the mail until it arrives, then resolve.
                for (int day = 1; day <= 10 && !letter.Resolved; day++)
                {
                    hub.Postal.AdvanceDay(journeys, day, diag);
                    hub.Recruiting.AdvanceDay(population, employment, day, diag, hub.Postal);
                }
                Assert.IsTrue(letter.Resolved, "the letter resolves on real postal arrival");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hubObject);
            }
        }

        // ------------------------------------------------------------------
        // P6-HAUL: freight shipments run on real travel time
        // ------------------------------------------------------------------

        [Test]
        public void BuildFreightShipment_UsesRealTransitDuration()
        {
            var lots = new List<ITransitLot> { new TestLot() };
            DeliveryJob job = DeliveryService.PlanDelivery(
                "agree-f", "flour", lots, "farm-1", "farm-biz-1", "mill-1", "Mill",
                DeliveryHaulerOption.FreightCompany, 8f, 0, "mill-1", null, diag);
            Assert.IsNotNull(job, "plan failed: " + string.Join(" | ", diag));

            LogisticsShipmentState shipment = DeliveryService.BuildFreightShipment(job, diag);
            Assert.IsNotNull(shipment);

            // 8 miles at the delivery wagon speed (3 mph) = 160 min → game seconds.
            float expected = DeliveryService.EstimateDriveMinutesOneWay(8f) * 60f;
            Assert.AreEqual(160 * 60f, expected, "sanity: 8 mi at 3 mph = 160 min");
            Assert.AreEqual(expected, shipment.transitDurationGameSeconds,
                "P2 travel-time gating holds on the freight-company path — no 120-second hauls");
        }

        [Test]
        public void BuildFreightShipment_UnknownDistance_UsesConservativeDefault()
        {
            var lots = new List<ITransitLot> { new TestLot() };
            DeliveryJob job = DeliveryService.PlanDelivery(
                "agree-f2", "flour", lots, "farm-1", "farm-biz-1", "mill-1", "Mill",
                DeliveryHaulerOption.FreightCompany, -1f, 0, "mill-1", null, diag);
            Assert.IsNotNull(job);

            LogisticsShipmentState shipment = DeliveryService.BuildFreightShipment(job, diag);
            Assert.IsNotNull(shipment);
            Assert.AreEqual(30 * 60f, shipment.transitDurationGameSeconds,
                "unknown distance keeps the conservative half-hour estimate");
        }
    }
}
