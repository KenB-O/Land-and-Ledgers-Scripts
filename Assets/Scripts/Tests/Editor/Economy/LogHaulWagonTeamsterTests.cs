using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// C-2 (expanded campaign, Phase 1 forestry, OD-C3/OD-C4): the log-haul
    /// calibration, wagon, and teamster. The 1200 kg two-horse logging
    /// wagon (two 600 kg authored sawlogs per trip) replaces the 450 kg
    /// farm wagon that refused every lot (F4: floor(450/600) = 0 logs
    /// fit). The Canon 4.1 log-wagon gate is enforced through a real
    /// equipment gate on the mill register, and the teamster's day budget
    /// is charged for haul minutes the way DriveFelling charges the
    /// fellers. The daily driver itself (SimulationDrivers.cs) is
    /// harness-excluded, so the teamster sequence is pinned here as a
    /// driver-faithful test using the same calls the driver makes, the
    /// way HaulDispatchAdvanceDriverTests mirrors the dispatch loop.
    /// Unity compilation, Test Runner, PlayMode, and the asset edit that
    /// puts log-wagon-1 in the sawmill opening-equipment entry are NOT
    /// claimed here — the asset edit is pending until E clears C-1.
    /// </summary>
    [TestFixture]
    public sealed class LogHaulWagonTeamsterTests
    {
        private const string MillKey = "sawmill";
        private const string MillInstanceId = "test-sawmill-1";
        private const string MillLocationId = "mill-1-yard";

        // The dev scenario's mill hand (C-1): Silas Ward, runtime person
        // id 10, the mill's first active non-owner employee — the teamster
        // OD-C3 names.
        private static readonly EntityId Teamster = EntityId.For(EntityKind.Person, 10);

        private sealed class World
        {
            public LoggingStandRegistry Stands = new LoggingStandRegistry();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public LogLotCustodyLedger Ledger;
            public List<string> Diagnostics = new List<string>();
            public EntityId Worker = EntityId.For(EntityKind.Person, 5);
            public LoggingOperationRuntime Runtime;
            public SawmillShopRuntime Mill;

            public World(float kilogramsPerLog = 600f)
            {
                Assert.IsNull(Stands.RegisterStand(new TimberStand
                {
                    StandId = "stand-sawmill-district",
                    DisplayName = "Sawmill District Pines",
                    LocationId = "sawmill-district",
                    Species = "white pine",
                    KilogramsPerLog = kilogramsPerLog,
                    StandingTimberUnits = 10,
                    MilesToMill = 2f,
                }));
                Assert.IsNull(Stands.GrantTimberRights("stand-sawmill-district", MillInstanceId, 200, "town-land-office"));
                Ledger = new LogLotCustodyLedger(Diagnostics);
                Runtime = new LoggingOperationRuntime(MillInstanceId, Ids, Ledger);
                Mill = new SawmillShopRuntime("mill-1", Ids);
            }

            public LogLot FellViaRuntime(int units, int dayIndex)
            {
                LogLot lot = Runtime.RunFelling(Stands, "stand-sawmill-district", MillInstanceId, units, Worker, dayIndex, Diagnostics, FellingEquipmentGate());
                Assert.IsNotNull(lot, "RunFelling must produce a lot on a granted stand.");
                return lot;
            }

            public JourneyModel JourneyToMill(float miles)
            {
                var model = new JourneyModel();
                TimberStand stand = Stands.GetStand("stand-sawmill-district");
                string problem = LogHaulRoute.EnsureRoute(model, stand.LocationId, stand.DisplayName,
                    MillLocationId, "mill-1", "Local Sawmill", miles, "logging road", Diagnostics);
                Assert.IsNull(problem, "EnsureRoute must register the landing-to-mill road.");
                return model;
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
                        OwnerId = MillInstanceId,
                        Condition01 = 1f,
                    },
                },
            };
        }

        private static EquipmentTaskGate LogWagonGate()
        {
            return new EquipmentTaskGate
            {
                AssetsSupplier = () => new List<EquipmentAsset>
                {
                    new EquipmentAsset
                    {
                        AssetId = "test-log-wagon",
                        Kind = "log-wagon",
                        OwnerKind = "business",
                        OwnerId = MillInstanceId,
                        Condition01 = 1f,
                    },
                },
            };
        }

        private static void SetPrivateField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static DevelopmentTownConfiguration BuildConfig(
            DevelopmentTownConfiguration.BusinessOpeningEquipment[] openingEquipment)
        {
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            // Schema 2 (foundation pass): configurationId, seedOffset and
            // freightBusinessKey live on the nested GeneralSettings; the
            // horse, wagon and equipment live on the mill's opening
            // record. The flat top-level fields no longer exist.
            config.Settings.configurationId = "test-town";
            config.Settings.seedOffset = 0;
            config.Settings.freightBusinessKey = MillKey;
            SetPrivateField(config, "people", new[]
            {
                new DevelopmentTownConfiguration.PersonDefinition
                { key = "person-0", firstName = "Test", lastName = "Miller", age = 30, homeBuildingOrdinal = 0 },
            });
            SetPrivateField(config, "households", new[]
            {
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "household-0", surname = "Miller", displayName = "Miller Household",
                    memberPersonKeys = new[] { "person-0" }, homeBuildingOrdinal = 0, openingCashCents = 0,
                },
            });
            SetPrivateField(config, "relationships", Array.Empty<DevelopmentTownConfiguration.RelationshipDefinition>());
            DevelopmentTownConfiguration.BusinessOpeningEquipment millEquipment =
                new DevelopmentTownConfiguration.BusinessOpeningEquipment();
            if (openingEquipment != null)
                foreach (DevelopmentTownConfiguration.BusinessOpeningEquipment entry in openingEquipment)
                    if (entry != null && string.Equals(entry.businessKey, MillKey, StringComparison.OrdinalIgnoreCase))
                    { millEquipment = entry; break; }
            SetPrivateField(config, "businesses", new[]
            {
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = MillKey, operationId = "sawmill", ownerPersonKey = "person-0",
                    businessId = MillInstanceId, premisesRequired = false,
                    opening = new DevelopmentTownConfiguration.BusinessOpeningConfiguration
                    {
                        openingHorseId = "A0",
                        openingWagonId = "test-wagon",
                        equipment = millEquipment,
                    },
                },
            });
            return config;
        }

        private static DevelopmentTownConfiguration.BusinessOpeningEquipment MillEquipmentEntry(
            params (string assetId, string kind)[] assets)
        {
            var entries = new DevelopmentTownConfiguration.OpeningAssetEntry[assets.Length];
            for (int i = 0; i < assets.Length; i++)
                entries[i] = new DevelopmentTownConfiguration.OpeningAssetEntry { assetId = assets[i].assetId, kind = assets[i].kind };
            return new DevelopmentTownConfiguration.BusinessOpeningEquipment
            {
                businessKey = MillKey,
                assetEntries = entries,
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
        public void T1_SingleAuthoredLog_DispatchesAtNewCapacity_WithWagonGate()
        {
            // F4 regression pin: at the old 450 kg wagon a single 600 kg
            // log refused (floor(450/600) = 0 fit). At the C-2 1200 kg
            // logging wagon it dispatches, travels, and reaches the mill.
            var world = new World(600f);
            LogLot lot = world.FellViaRuntime(1, 200);
            Assert.AreEqual(600f, lot.MassKg, 0.01f);
            JourneyModel journey = world.JourneyToMill(2f);

            LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", MillLocationId,
                journey, "sawmill-district", 200, world.Diagnostics, LogWagonGate());

            Assert.IsNotNull(haul, "A 600 kg log must dispatch against the 1200 kg logging wagon; diag: " + string.Join(" | ", world.Diagnostics));
            Assert.AreEqual(1, haul.Lot.LogUnits);
            Assert.AreEqual(600f, haul.Lot.MassKg, 0.01f);
            Assert.AreEqual(2, haul.LoadingMinutesRemaining, "1 log at 2 minutes each.");
            Assert.AreEqual(30, haul.TransitMinutesRemaining, "2 miles at 4 mph is 30 minutes.");
            Assert.AreEqual(0, world.Runtime.LandingStock.Count, "The lot leaves the landing on dispatch.");

            world.Runtime.AdvanceHauls(34, world.Diagnostics, 200); // 2 loading + 30 transit + slack
            Assert.AreEqual(LogHaulStatus.Arrived, haul.Status);
            world.Runtime.DeliverArrivedHauls(world.Mill.LogStock, world.Diagnostics, 200);
            Assert.AreEqual(1, world.Mill.LogStock.TotalLogUnits, "The log reaches the mill yard.");
        }

        [Test]
        public void T2_OverCapacityLot_SplitsToFittingUnits()
        {
            var world = new World(600f);
            LogLot lot = world.FellViaRuntime(3, 200); // 1800 kg, over the 1200 kg wagon
            JourneyModel journey = world.JourneyToMill(2f);

            LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", MillLocationId,
                journey, "sawmill-district", 200, world.Diagnostics, LogWagonGate());

            Assert.IsNotNull(haul, "The fitting portion must still dispatch.");
            Assert.AreEqual(2, haul.Lot.LogUnits, "Two 600 kg logs fit the 1200 kg wagon.");
            Assert.AreEqual(1200f, haul.Lot.MassKg, 0.01f);
            Assert.AreEqual(1, world.Runtime.LandingStock.Count, "The balance stays at the landing.");
            LogLot balance = world.Runtime.LandingStock[0];
            Assert.AreEqual(lot.LotId, balance.LotId, "The balance keeps the original lot identity.");
            Assert.AreEqual(1, balance.LogUnits);
            Assert.AreEqual(600f, balance.MassKg, 0.01f);
        }

        [Test]
        public void T3_GateWithoutWagon_RefusesDispatch()
        {
            var world = new World(600f);
            LogLot lot = world.FellViaRuntime(1, 200);
            JourneyModel journey = world.JourneyToMill(2f);

            var bareGate = new EquipmentTaskGate(); // no wagon anywhere
            LogHaul refused = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", MillLocationId,
                journey, "sawmill-district", 200, world.Diagnostics, bareGate);

            Assert.IsNull(refused, "No log wagon, no haul (Canon 4.1).");
            Assert.AreEqual(1, world.Runtime.LandingStock.Count, "The refused lot stays at the landing.");
            Assert.IsTrue(DiagnosticsMention(world.Diagnostics, "REFUSED"),
                "The refusal is loud; diag: " + string.Join(" | ", world.Diagnostics));
        }

        [Test]
        public void T4_EndowmentInstallsLogWagon_FromConfig_AndGatePasses()
        {
            // Pins the exact config shape the pending asset edit must
            // produce: log-wagon-1 (kind log-wagon) in the sawmill
            // opening-equipment entry, beside the C-1 axes. The installer
            // is the same one the bootstrapper calls; the asset edit
            // itself (DevelopmentTownConfiguration.asset) is pending
            // until E clears C-1's files.
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                MillEquipmentEntry(
                    ("felling-axe-1", "felling-axe"),
                    ("felling-axe-2", "felling-axe"),
                    ("log-wagon-1", "log-wagon")),
            });
            Assert.IsTrue(config.Validate(out string configError), configError);
            var state = new BusinessRuntimeState();
            var register = new BusinessEquipmentRegister { BusinessInstanceId = MillInstanceId };

            Assert.IsTrue(ScenarioMillOpeningEquipmentEndowment.TryApply(
                config, MillKey, MillInstanceId, state, register, 0, diag,
                out string report, out string error), error);
            StringAssert.Contains("log-wagon-1", report, "report names the installed wagon; report: " + report);

            Assert.AreEqual(3, register.Assets.Count, "one asset per authored entry");
            EquipmentAsset wagon = register.Assets[2];
            Assert.AreEqual("log-wagon-1", wagon.AssetId);
            Assert.AreEqual("log-wagon", wagon.Kind);
            Assert.AreEqual(1f, wagon.Condition01, "opening assets install at full condition");
            Assert.AreEqual("business", wagon.OwnerKind, "the wagon is business-owned");
            Assert.AreEqual(MillInstanceId, wagon.OwnerId);
            Assert.AreEqual(MillInstanceId, wagon.LocationId, "the wagon is located at the mill");

            // The installed wagon satisfies the haul gate through the
            // register-backed gate, the same shape the hub gate uses.
            var world = new World(600f);
            LogLot lot = world.FellViaRuntime(1, 200);
            JourneyModel journey = world.JourneyToMill(2f);
            var gate = new EquipmentTaskGate { AssetsSupplier = () => register.Assets };
            LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", MillLocationId,
                journey, "sawmill-district", 200, world.Diagnostics, gate);
            Assert.IsNotNull(haul, "The endowment-installed wagon must satisfy the log-wagon gate; diag: " + string.Join(" | ", world.Diagnostics));
            Assert.AreEqual(1, haul.Lot.LogUnits);
        }

        [Test]
        public void T5_UnitsThatFitWagon_CalibrationTable()
        {
            Assert.AreEqual(1, LoggingOperationRuntime.UnitsThatFitWagon(new LogLot { LogUnits = 1, MassKg = 600f }),
                "One 600 kg log fits.");
            Assert.AreEqual(2, LoggingOperationRuntime.UnitsThatFitWagon(new LogLot { LogUnits = 2, MassKg = 1200f }),
                "Two 600 kg logs fill the wagon exactly.");
            Assert.AreEqual(2, LoggingOperationRuntime.UnitsThatFitWagon(new LogLot { LogUnits = 3, MassKg = 1800f }),
                "Three 600 kg logs: only two fit.");
            Assert.AreEqual(4, LoggingOperationRuntime.UnitsThatFitWagon(new LogLot { LogUnits = 4, MassKg = 1200f }),
                "Four 300 kg logs fill the wagon exactly.");
            Assert.AreEqual(4, LoggingOperationRuntime.UnitsThatFitWagon(new LogLot { LogUnits = 5, MassKg = 1500f }),
                "Five 300 kg logs: only four fit.");
            Assert.AreEqual(0, LoggingOperationRuntime.UnitsThatFitWagon(new LogLot { LogUnits = 1, MassKg = 1500f }),
                "A single 1500 kg log fits no wagon — it refuses, it is never shrunk.");
            Assert.AreEqual(0, LoggingOperationRuntime.UnitsThatFitWagon(null), "No lot, no fit.");
            Assert.AreEqual(0, LoggingOperationRuntime.UnitsThatFitWagon(new LogLot { LogUnits = 0, MassKg = 0f }),
                "An empty lot fits nothing.");
        }

        [Test]
        public void T6_PlanHaul_RefusesOversizedWholeLot()
        {
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            var journey = new JourneyModel();
            Assert.IsNull(journey.RegisterLocation(new JourneyLocation(
                "sawmill-district", JourneyLocationKind.Waypoint, "Sawmill District Pines", 0f, 0f, false)));
            Assert.IsNull(journey.RegisterLocation(new JourneyLocation(
                MillLocationId, JourneyLocationKind.Mill, "Local Sawmill", 0f, 0f, false)
                { BusinessId = "mill-1" }));
            Assert.IsNull(journey.AddEdge("sawmill-district", MillLocationId, 2f, "logging road"));

            var lot = new LogLot
            {
                LotId = EntityId.For(EntityKind.Lot, 7001),
                LogUnits = 3,
                MassKg = 1800f,
                OwnerId = MillInstanceId,
                StandId = "stand-sawmill-district",
                Species = "white pine",
                FelledBy = EntityId.For(EntityKind.Person, 5),
                FelledDayIndex = 200,
            };

            LogHaul refused = LogHaul.PlanHaul(ids, lot, "mill-1", MillLocationId,
                journey, "sawmill-district", diag);

            Assert.IsNull(refused, "A whole lot over wagon capacity never plans — the runtime splits first.");
            Assert.IsTrue(DiagnosticsMention(diag, "1200"),
                "The refusal names the 1200 kg capacity; diag: " + string.Join(" | ", diag));
        }

        [Test]
        public void T7_TeamsterBudget_DriverFaithfulSequence_CommitsWorksAndBinds()
        {
            // Mirrors DriveLogHauls step for step: resolve the teamster,
            // compute the trip (2 logs x 2 min loading + 30 min transit
            // over 2 miles = 34 min), pre-commit it, dispatch through the
            // real gate, advance by the capped day minutes, record the
            // minutes actually worked. A second identical trip then
            // refuses the pre-commit on the teamster's remaining budget
            // and its lot stays at the landing — exactly the driver's
            // stop-dispatch behaviour. The teamster's day is deliberately
            // small (40 min) so the bind is observable in one test.
            var world = new World(600f);
            LogLot firstLot = world.FellViaRuntime(2, 200);
            LogLot secondLot = world.FellViaRuntime(2, 200);
            JourneyModel journey = world.JourneyToMill(2f);

            var budgets = new WorkTimeBudgetStore();
            budgets.GetOrCreate(Teamster, 40);
            Assert.AreEqual(40, budgets.GetOrCreate(Teamster).MinutesRemaining);

            int unitsThatFit = LoggingOperationRuntime.UnitsThatFitWagon(firstLot);
            Assert.AreEqual(2, unitsThatFit);
            JourneyRoute plannedRoute = journey.FindRoute("sawmill-district", MillLocationId, TravelMode.Wagon);
            Assert.IsTrue(plannedRoute.Found);
            Assert.AreEqual(30, plannedRoute.TotalMinutes);
            int tripMinutes = unitsThatFit * LogHaul.LoadingMinutesPerLog + plannedRoute.TotalMinutes;
            Assert.AreEqual(34, tripMinutes, "2 logs x 2 min loading + 30 min transit.");

            Assert.IsTrue(budgets.TryCommitTask(Teamster, tripMinutes, out string commitError), commitError);
            LogHaul haul = world.Runtime.DispatchLogHaul(firstLot.LotId, "mill-1", MillLocationId,
                journey, "sawmill-district", 200, world.Diagnostics, LogWagonGate());
            Assert.IsNotNull(haul, "The committed trip dispatches; diag: " + string.Join(" | ", world.Diagnostics));

            WorkTimeBudget teamsterBudget = budgets.GetOrCreate(Teamster);
            int advanceMinutes = Math.Min(WorkTimeBudget.DefaultDailyWorkMinutes,
                tripMinutes + teamsterBudget.MinutesRemaining);
            Assert.AreEqual(40, advanceMinutes, "34 committed + 6 remaining = the whole short day.");
            world.Runtime.AdvanceHauls(advanceMinutes, world.Diagnostics, 200);
            Assert.AreEqual(LogHaulStatus.Arrived, haul.Status);

            int advancedMinutes = 34; // 4 loading + 30 transit, the whole trip
            teamsterBudget.RecordWorked(advancedMinutes);
            int unspentMinutes = tripMinutes - advancedMinutes;
            if (unspentMinutes > 0) teamsterBudget.ReleaseCommitment(unspentMinutes);

            Assert.AreEqual(34, teamsterBudget.MinutesWorked, "The teamster worked the trip.");
            Assert.AreEqual(6, teamsterBudget.MinutesRemaining, "40 - 34 leaves 6 minutes.");

            // Second identical trip: the pre-commit refuses on the
            // remaining budget, so the driver never dispatches it.
            int secondTripMinutes = LoggingOperationRuntime.UnitsThatFitWagon(secondLot) * LogHaul.LoadingMinutesPerLog + 30;
            Assert.AreEqual(34, secondTripMinutes);
            Assert.IsFalse(budgets.TryCommitTask(Teamster, secondTripMinutes, out string refusal),
                "A 34-minute trip cannot commit against 6 remaining minutes.");
            Assert.IsTrue(refusal.Contains("6"), "The refusal names the remaining minutes; refusal: " + refusal);
            Assert.AreEqual(1, world.Runtime.LandingStock.Count, "The uncommitted lot stays at the landing.");
            Assert.AreEqual(secondLot.LotId, world.Runtime.LandingStock[0].LotId);
            Assert.AreEqual(2, world.Runtime.LandingLogUnits);
        }

        [Test]
        public void T8_PartialAdvance_SettlesWorkedAndReleasesRemainder()
        {
            // The settle arithmetic for a trip that does not finish in
            // the advanced minutes: worked minutes stay worked, the
            // unspent pre-commit is released back to the teamster.
            var world = new World(600f);
            LogLot lot = world.FellViaRuntime(2, 200);
            JourneyModel journey = world.JourneyToMill(2f);

            var budgets = new WorkTimeBudgetStore();
            budgets.GetOrCreate(Teamster, 40);
            int tripMinutes = 2 * LogHaul.LoadingMinutesPerLog + 30;
            Assert.AreEqual(34, tripMinutes);
            Assert.IsTrue(budgets.TryCommitTask(Teamster, tripMinutes, out string commitError), commitError);
            LogHaul haul = world.Runtime.DispatchLogHaul(lot.LotId, "mill-1", MillLocationId,
                journey, "sawmill-district", 200, world.Diagnostics, LogWagonGate());
            Assert.IsNotNull(haul);

            world.Runtime.AdvanceHauls(10, world.Diagnostics, 200); // only 10 of the 34 trip minutes
            Assert.AreEqual(LogHaulStatus.InTransit, haul.Status);
            Assert.AreEqual(24, haul.TransitMinutesRemaining, "4 loading + 6 of 30 transit advanced.");

            WorkTimeBudget teamsterBudget = budgets.GetOrCreate(Teamster);
            int advancedMinutes = 10;
            teamsterBudget.RecordWorked(advancedMinutes);
            int unspentMinutes = tripMinutes - advancedMinutes;
            if (unspentMinutes > 0) teamsterBudget.ReleaseCommitment(unspentMinutes);

            Assert.AreEqual(10, teamsterBudget.MinutesWorked);
            Assert.AreEqual(30, teamsterBudget.MinutesRemaining, "40 - 10 worked leaves 30 minutes free.");
        }
    }
}
