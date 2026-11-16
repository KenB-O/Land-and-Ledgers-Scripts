using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

// A2 plan 28 (2026-11-14, note 113): the log-haul dispatch/advance driver.
// The daily method itself is harness-excluded (SimulationDrivers.cs), so
// these tests pin the runtime methods plus the new route helper
// LogHaulRoute, which is where the slice's logic lives. T10 mirrors the
// dispatch loop (snapshot, FIFO by FelledDayIndex, EnsureRoute per lot,
// DispatchLogHaul per lot) the way the plan-27 tests mirror DriveFelling.
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class HaulDispatchAdvanceDriverTests
    {
        private sealed class World
        {
            public LoggingStandRegistry Stands = new LoggingStandRegistry();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public LogLotCustodyLedger Ledger;
            public List<string> Diagnostics = new List<string>();
            public EntityId Worker = EntityId.For(EntityKind.Person, 5);
            public LoggingOperationRuntime Runtime;
            public SawmillShopRuntime Mill;

            public World(float kilogramsPerLog = 300f)
            {
                Assert.IsNull(Stands.RegisterStand(new TimberStand
                {
                    StandId = "stand-sawmill-district",
                    DisplayName = "Sawmill District Pines",
                    LocationId = "sawmill-district",
                    Species = "white pine",
                    KilogramsPerLog = kilogramsPerLog,
                    StandingTimberUnits = 10,
                    // C4: authored road miles from this stand's landing to
                    // the mill; the driver reads MilesToMill per lot.
                    MilesToMill = 2f,
                }));
                Assert.IsNull(Stands.GrantTimberRights("stand-sawmill-district", "logging-business-1", 200, "town-land-office"));
                Ledger = new LogLotCustodyLedger(Diagnostics);
                Runtime = new LoggingOperationRuntime("logging-1", Ids, Ledger);
                Mill = new SawmillShopRuntime("mill-1", Ids);
            }

            public LogLot FellViaRuntime(int units, int dayIndex)
            {
                LogLot lot = Runtime.RunFelling(Stands, "stand-sawmill-district", "logging-business-1", units, Worker, dayIndex, Diagnostics, FellingEquipmentGate());
                Assert.IsNotNull(lot, "RunFelling must produce a lot on a granted stand.");
                return lot;
            }

            // The driver-faithful journey: the origin is the stand's landing
            // (stand.LocationId), the miles are the authored miles passed
            // through by the caller, the road is the fixed "logging road".
            public JourneyModel JourneyToMill(float miles)
            {
                var model = new JourneyModel();
                TimberStand stand = Stands.GetStand("stand-sawmill-district");
                string problem = LogHaulRoute.EnsureRoute(model, stand.LocationId, stand.DisplayName,
                    "mill-1-yard", "mill-1", "Local Sawmill", miles, "logging road", Diagnostics);
                Assert.IsNull(problem, "EnsureRoute must register the landing-to-mill road.");
                return model;
            }

            public string LotIdOf(LogLot lot)
            {
                return lot.LotId.ToString();
            }
        }

        private static EquipmentTaskGate FellingEquipmentGate()
        {
            return new EquipmentTaskGate
            {
                AssetsSupplier = () => new List<EquipmentAsset>
                {
                    new EquipmentAsset
                    {
                        AssetId = "test-felling-axe",
                        Kind = "felling-axe",
                        OwnerKind = "business",
                        OwnerId = "logging-1",
                        Condition01 = 1f,
                    },
                },
            };
        }

        private static bool DiagnosticsMention(IReadOnlyList<string> diagnostics, string fragment)
        {
            foreach (string line in diagnostics)
            {
                if (line != null && line.Contains(fragment)) return true;
            }
            return false;
        }

        [Test]
        public void T1_DispatchHappyPath_TwoMileRoute()
        {
            var world = new World(200f);
            LogLot lot = world.FellViaRuntime(2, 200);
            JourneyModel journey = world.JourneyToMill(2f);

            LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                journey, "sawmill-district", 200, world.Diagnostics);

            Assert.IsNotNull(haul, "DispatchLogHaul must produce a haul on a routable journey.");
            Assert.AreEqual(LogHaulStatus.Loading, haul.Status);
            Assert.AreEqual(0, world.Runtime.LandingStock.Count, "The lot leaves the landing on dispatch.");
            Assert.AreEqual(2f, haul.RouteMiles, 0.01f);
            Assert.AreEqual(30, haul.TransitMinutesRemaining, "2 miles at 4 mph is 30 minutes.");
            Assert.AreEqual(4, haul.LoadingMinutesRemaining, "2 logs at 2 minutes each is 4 minutes.");

            // C4: the authored miles survive a save round trip —
            // CaptureSaveDto copies MilesToMill field-by-field and the
            // restore hands the stand back whole.
            var restored = new LoggingStandRegistry();
            restored.LoadFromSaveDto(world.Stands.CaptureSaveDto());
            Assert.AreEqual(2f, restored.GetStand("stand-sawmill-district").MilesToMill, 0.01f);
        }

        [Test]
        public void T2_DispatchRefusal_UnknownLot()
        {
            var world = new World(200f);
            world.FellViaRuntime(2, 200);
            EntityId unknown = EntityId.For(EntityKind.Lot, 9999);
            JourneyModel journey = world.JourneyToMill(2f);

            LogHaul haul = world.Runtime.DispatchLogHaul(unknown, "mill-1", "mill-1-yard",
                journey, "sawmill-district", 200, world.Diagnostics);

            Assert.IsNull(haul, "Dispatching an unknown lot must refuse.");
            Assert.IsTrue(DiagnosticsMention(world.Diagnostics, unknown.ToString()),
                "The refusal diagnostic must name the unknown lot.");
            Assert.AreEqual(1, world.Runtime.LandingStock.Count, "The landing stock is untouched by a refused dispatch.");
        }

        [Test]
        public void T3_DispatchRefusal_Unroutable()
        {
            var world = new World(200f);
            LogLot lot = world.FellViaRuntime(2, 200);
            var journey = new JourneyModel();
            Assert.IsNull(journey.RegisterLocation(new JourneyLocation(
                "sawmill-district", JourneyLocationKind.Waypoint, "Sawmill District Pines", 0f, 0f, false)));
            Assert.IsNull(journey.RegisterLocation(new JourneyLocation(
                "mill-1-yard", JourneyLocationKind.Mill, "Local Sawmill", 0f, 0f, false)
                { BusinessId = "mill-1" }));
            // No edge: the landing has no road to the mill.

            LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                journey, "sawmill-district", 200, world.Diagnostics);

            Assert.IsNull(haul, "An unroutable haul must refuse — travel is never assumed free.");
            Assert.IsTrue(DiagnosticsMention(world.Diagnostics, "REFUSED"),
                "The refusal must be loud.");
            Assert.AreEqual(1, world.Runtime.LandingStock.Count, "The lot stays at the landing.");
        }

        [Test]
        public void T4_DispatchSplit_OverCapacityLot()
        {
            // C-2 (OD-C4): the logging wagon carries 1200 kg — two 600 kg
            // authored sawlogs. Three logs (1800 kg) split: 2 fit, 1 stays.
            var world = new World(600f);
            LogLot lot = world.FellViaRuntime(3, 200); // 1800 kg, over the 1200 kg wagon
            JourneyModel journey = world.JourneyToMill(2f);

            LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                journey, "sawmill-district", 200, world.Diagnostics);

            Assert.IsNotNull(haul, "The fitting portion must still dispatch.");
            Assert.AreEqual(2, haul.Lot.LogUnits, "Two 600 kg logs fit the 1200 kg logging wagon.");
            Assert.AreEqual(1200f, haul.Lot.MassKg, 0.01f);
            Assert.AreEqual(1, world.Runtime.LandingStock.Count, "The balance stays at the landing.");
            LogLot balance = world.Runtime.LandingStock[0];
            Assert.AreEqual(lot.LotId, balance.LotId, "The balance keeps the original lot identity.");
            Assert.AreEqual(1, balance.LogUnits);
            Assert.AreEqual(600f, balance.MassKg, 0.01f);
            LogLotCustodyRecord cargo = world.Ledger.GetLot(haul.Lot.LotId.ToString());
            Assert.IsNotNull(cargo, "The dispatched cargo is registered under its fresh id.");
            Assert.AreEqual(LogLotCustodyState.Loaded, cargo.State);
        }

        [Test]
        public void T5_Advance_LoadingToInTransit_RecordsLedgerFlip()
        {
            var world = new World(200f);
            LogLot lot = world.FellViaRuntime(2, 200);
            JourneyModel journey = world.JourneyToMill(2f);
            LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                journey, "sawmill-district", 200, world.Diagnostics);
            Assert.AreEqual(LogHaulStatus.Loading, haul.Status);

            world.Runtime.AdvanceHauls(4, world.Diagnostics, 200); // the 4 loading minutes

            Assert.AreEqual(LogHaulStatus.InTransit, haul.Status);
            LogLotCustodyRecord record = world.Ledger.GetLot(world.LotIdOf(lot));
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.InTransit, record.State);
        }

        [Test]
        public void T6_Advance_InTransitToArrived()
        {
            var world = new World(200f);
            LogLot lot = world.FellViaRuntime(2, 200);
            JourneyModel journey = world.JourneyToMill(2f);
            LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                journey, "sawmill-district", 200, world.Diagnostics);
            world.Runtime.AdvanceHauls(4, world.Diagnostics, 200);
            Assert.AreEqual(LogHaulStatus.InTransit, haul.Status);

            world.Runtime.AdvanceHauls(30, world.Diagnostics, 200); // the 30 transit minutes

            Assert.AreEqual(LogHaulStatus.Arrived, haul.Status);
        }

        [Test]
        public void T7_MultiDayHaul_TwoDailyAdvances()
        {
            var world = new World(200f);
            LogLot lot = world.FellViaRuntime(2, 200);
            JourneyModel journey = world.JourneyToMill(50f); // 750 transit minutes
            LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                journey, "sawmill-district", 200, world.Diagnostics);
            Assert.AreEqual(750, haul.TransitMinutesRemaining);

            world.Runtime.AdvanceHauls(600, world.Diagnostics, 200); // day one: 4 loading + 596 transit

            Assert.AreEqual(LogHaulStatus.InTransit, haul.Status);
            Assert.AreEqual(154, haul.TransitMinutesRemaining);

            world.Runtime.AdvanceHauls(600, world.Diagnostics, 201); // day two

            Assert.AreEqual(LogHaulStatus.Arrived, haul.Status);
        }

        [Test]
        public void T8_DeliveryEndToEnd_ArrivedHaulReachesMillStock()
        {
            var world = new World(200f);
            LogLot lot = world.FellViaRuntime(2, 200);
            JourneyModel journey = world.JourneyToMill(2f);
            world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                journey, "sawmill-district", 200, world.Diagnostics);
            world.Runtime.AdvanceHauls(34, world.Diagnostics, 200); // 4 loading + 30 transit
            Assert.AreEqual(LogHaulStatus.Arrived, world.Runtime.Hauls[0].Status);

            world.Runtime.DeliverArrivedHauls(world.Mill.LogStock, world.Diagnostics, 200);

            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits, "The lot enters the mill LogStock.");
            LogLotCustodyRecord record = world.Ledger.GetLot(world.LotIdOf(lot));
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.Delivered, record.State);
        }

        [Test]
        public void T9_EnsureRoute_IsIdempotent()
        {
            var model = new JourneyModel();
            var diagnostics = new List<string>();
            string first = LogHaulRoute.EnsureRoute(model, "sawmill-district", "Sawmill District Pines",
                "mill-1-yard", "mill-1", "Local Sawmill", 2f, "logging road", diagnostics);
            Assert.IsNull(first);
            int diagnosticsAfterFirst = diagnostics.Count;

            string second = LogHaulRoute.EnsureRoute(model, "sawmill-district", "Sawmill District Pines",
                "mill-1-yard", "mill-1", "Local Sawmill", 2f, "logging road", diagnostics);

            Assert.IsNull(second, "The second EnsureRoute must also succeed.");
            Assert.AreEqual(diagnosticsAfterFirst, diagnostics.Count,
                "The second EnsureRoute adds no diagnostic — nothing is re-registered.");
            JourneyRoute route = model.FindRoute("sawmill-district", "mill-1-yard", TravelMode.Wagon);
            Assert.IsTrue(route.Found);
            Assert.AreEqual(2f, route.TotalMiles, 0.01f);
            Assert.AreEqual(30, route.TotalMinutes, "No duplicate edge grew the route.");
        }

        [Test]
        public void T10_DispatchAll_TwoLotsOnePass()
        {
            var world = new World(200f);
            LogLot first = world.FellViaRuntime(2, 200);
            LogLot second = world.FellViaRuntime(2, 200);
            var journey = new JourneyModel();

            // Mirror of the DriveLogHauls dispatch loop: snapshot the
            // landing stock, order FIFO by FelledDayIndex (landing-stock
            // order breaks ties), ensure the route per lot, dispatch per lot.
            var lots = new List<LogLot>(world.Runtime.LandingStock);
            var order = new List<int>();
            for (int i = 0; i < lots.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int byDay = lots[a].FelledDayIndex.CompareTo(lots[b].FelledDayIndex);
                return byDay != 0 ? byDay : a.CompareTo(b);
            });
            foreach (int index in order)
            {
                LogLot lot = lots[index];
                TimberStand stand = world.Stands.GetStand(lot.StandId);
                Assert.IsNotNull(stand, "Every landing lot resolves to a stand.");
                string routeProblem = LogHaulRoute.EnsureRoute(journey, stand.LocationId, stand.DisplayName,
                    "mill-1-yard", "mill-1", "Local Sawmill", stand.MilesToMill, "logging road", world.Diagnostics);
                Assert.IsNull(routeProblem);
                LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                    journey, stand.LocationId, 200, world.Diagnostics);
                Assert.IsNotNull(haul, "Both lots dispatch in one pass (note 113 Q6: dispatch-all).");
            }

            Assert.AreEqual(2, world.Runtime.Hauls.Count);
            Assert.AreEqual(0, world.Runtime.LandingStock.Count, "The landing is empty after one pass.");
            Assert.AreEqual(first.LotId, world.Runtime.Hauls[0].Lot.LotId, "FIFO by FelledDayIndex: the first-felled lot dispatches first.");
            Assert.AreEqual(second.LotId, world.Runtime.Hauls[1].Lot.LotId);
        }
    }
}
