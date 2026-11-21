// Land & Ledgers — Business Operations Monitor tests (Lane 5, Tool 04).
//
// Focused tests per classifier. Known cases are reproduced from the current
// tree's source-reviewed evidence values (no run exists on this HEAD):
// the 7 dev-town businesses and their config opening cash (D-36), the
// D-04 bake-block, the E-14 fuel-config value, the D-02 wholesale
// starvation policy, the D-18 dealer knowledge gap, the D-09/D-10 + Lane 9b
// settlement cadences.

using System.Collections.Generic;
using System.Linq;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class BusinessOperationsMonitorTests
    {
        private static BusinessSnapshot HealthySnapshot(string key)
        {
            return new BusinessSnapshot
            {
                Key = key,
                DisplayName = key,
                BusinessTypeName = "GeneralStore",
                OwnerKind = "Npc",
                OwnerDisplayName = "Owner",
                HasRuntimeState = true,
                CashCents = 1000,
                OpeningCashCents = 1000,
                FilledWorkerCount = 1,
                WorkerSlots = new List<BusinessWorkerSnapshot>
                {
                    new BusinessWorkerSnapshot
                    {
                        SlotId = "clerk", DisplayName = "Clerk", Filled = true,
                        WorkerDisplayName = "Worker", WeeklyWageCents = 100, PaidActive = true,
                    },
                },
                CategoryStock = new List<BusinessStockSnapshot>(),
            };
        }

        private static BakeryOperationsDetail BoundBakery()
        {
            return new BakeryOperationsDetail
            {
                OwnerPersonId = 7,
                SupplyProtectionPolicy = "ProtectRetailCounter",
                WholesaleAccountCount = 0,
                WholesaleUnitsDueToday = 0,
                RetailUnitsAvailableForSale = 24,
                RetailUnitsNetOfDueWholesale = 24,
                FuelUnits = 40,
                FlourUnits = 120,
                NotionsUnits = 30,
                RetailUnitsSoldToday = 10,
                RetailRevenueTodayCents = 80,
                WasteUnitsToday = 0,
                FuelDealerIntakeUnitsPerBakeryUnit = 4,
            };
        }

        // --- Expected businesses (config-grounded) ------------------------------

        [Test]
        public void ExpectedBusinesses_ListsAllSevenDevTownBusinessesWithConfigCash()
        {
            IReadOnlyList<ExpectedBusiness> expected = BusinessOperationsMonitor.ExpectedBusinesses();
            Assert.AreEqual(7, expected.Count);
            Assert.AreEqual(162000, expected.Sum(e => e.OpeningCashCents));
            Assert.AreEqual("kennedy-baldwin-ooms", expected.First(e => e.Key == "general-store").OwnerPersonKey);
            Assert.AreEqual("glen-pierce", expected.First(e => e.Key == "bakery").OwnerPersonKey);
            Assert.AreEqual("jacob-boniecki", expected.First(e => e.Key == "fuel-dealer").OwnerPersonKey);
            Assert.AreEqual("ethan-boniecki", expected.First(e => e.Key == "freight-company").OwnerPersonKey);
            Assert.AreEqual("enzo-kwindt", expected.First(e => e.Key == "sawmill").OwnerPersonKey);
            Assert.AreEqual("grace-boniecki", expected.First(e => e.Key == "lumber-yard").OwnerPersonKey);
            Assert.AreEqual("don-millward", expected.First(e => e.Key == "carpentry-workshop").OwnerPersonKey);
        }

        [Test]
        public void VerifyExpectedBusinesses_ReportsMissingKeysOnly()
        {
            List<string> missing = BusinessOperationsMonitor.VerifyExpectedBusinesses(
                new[] { "general-store", "bakery", "fuel-dealer", "freight-company", "sawmill", "lumber-yard" });
            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual("carpentry-workshop", missing[0]);

            List<string> noneMissing = BusinessOperationsMonitor.VerifyExpectedBusinesses(
                new[] { "general-store", "bakery", "fuel-dealer", "freight-company", "sawmill", "lumber-yard", "carpentry-workshop" });
            Assert.AreEqual(0, noneMissing.Count);
        }

        // --- Health -------------------------------------------------------------

        [Test]
        public void ClassifyHealth_NoRuntimeStateIsUnknownNeverHealthy()
        {
            var snapshot = HealthySnapshot("bakery");
            snapshot.HasRuntimeState = false;
            Assert.AreEqual(BusinessHealthKind.Unknown,
                BusinessOperationsMonitor.ClassifyHealth(snapshot, new List<BusinessBlocker>()));
        }

        [Test]
        public void ClassifyHealth_NoBlockersIsHealthy()
        {
            Assert.AreEqual(BusinessHealthKind.Healthy,
                BusinessOperationsMonitor.ClassifyHealth(HealthySnapshot("sawmill"), new List<BusinessBlocker>()));
        }

        [Test]
        public void ClassifyHealth_WarningBlockerIsBlocked()
        {
            var blockers = new List<BusinessBlocker>
            {
                new BusinessBlocker("X", "sawmill", "T", "C", "A", "E", IncidentSeverity.Warning, false),
            };
            Assert.AreEqual(BusinessHealthKind.Blocked,
                BusinessOperationsMonitor.ClassifyHealth(HealthySnapshot("sawmill"), blockers));
        }

        [Test]
        public void ClassifyHealth_OwnerDecisionOutranksBlocked()
        {
            var blockers = new List<BusinessBlocker>
            {
                new BusinessBlocker("X", "bakery", "T", "C", "A", "E", IncidentSeverity.Warning, false),
                new BusinessBlocker("D-04", "bakery", "T", "C", "A", "E", IncidentSeverity.Warning, true),
            };
            Assert.AreEqual(BusinessHealthKind.OwnerDecision,
                BusinessOperationsMonitor.ClassifyHealth(HealthySnapshot("bakery"), blockers));
        }

        // --- D-04 bakery bake-block (owner decision, surfaced not resolved) -----

        [Test]
        public void ClassifyBakery_UnboundOwnerIsOwnerDecisionBlockerWithCause()
        {
            BakeryOperationsDetail detail = BoundBakery();
            detail.OwnerPersonId = -1;
            var blockers = new List<BusinessBlocker>();
            var notes = new List<BusinessNote>();
            BusinessOperationsMonitor.ClassifyBakery(detail, "bakery", blockers, notes);

            BusinessBlocker blocker = blockers.FirstOrDefault(b => b.Id == "D-04");
            Assert.IsNotNull(blocker);
            Assert.IsTrue(blocker.IsOwnerDecision);
            Assert.AreEqual(IncidentSeverity.Warning, blocker.Severity);
            Assert.IsTrue(blocker.Cause.Contains("can never bake"),
                "The cause must name the consequence: a player-owned bakery can never bake.");
            Assert.IsTrue(blocker.Cause.Contains("OWNER DECISION"),
                "D-04 must be labelled an owner decision, not silently resolved.");
            Assert.IsTrue(blocker.Cause.Contains("BindOwnerPerson"),
                "The cause must name the refusing authority.");
        }

        [Test]
        public void ClassifyBakery_BoundOwnerWithStockHasNoBlockers()
        {
            var blockers = new List<BusinessBlocker>();
            var notes = new List<BusinessNote>();
            BusinessOperationsMonitor.ClassifyBakery(BoundBakery(), "bakery", blockers, notes);
            Assert.AreEqual(0, blockers.Count);
        }

        // --- E-14 bakery fuel / flour / notions ---------------------------------

        [Test]
        public void ClassifyBakery_NoFuelWithZeroConfigValueNamesTheConfigValue()
        {
            BakeryOperationsDetail detail = BoundBakery();
            detail.FuelUnits = 0;
            detail.FuelDealerIntakeUnitsPerBakeryUnit = 0;
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyBakery(detail, "bakery", blockers, new List<BusinessNote>());

            BusinessBlocker blocker = blockers.FirstOrDefault(b => b.Title.Contains("no fuel"));
            Assert.IsNotNull(blocker);
            Assert.IsTrue(blocker.Cause.Contains("fuelDealerIntakeUnitsPerBakeryUnit"),
                "With the config value at 0 the cause must name the config value, not missing code.");
            Assert.IsTrue(blocker.Cause.Contains("day-41"),
                "The cause must name the day-41 fuel death that follows from the config value.");
        }

        [Test]
        public void ClassifyBakery_NoFuelWithConfiguredDealerSourceNamesTheEmptyStore()
        {
            BakeryOperationsDetail detail = BoundBakery();
            detail.FuelUnits = 0;
            detail.FuelDealerIntakeUnitsPerBakeryUnit = 4;
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyBakery(detail, "bakery", blockers, new List<BusinessNote>());

            BusinessBlocker blocker = blockers.FirstOrDefault(b => b.Title.Contains("no fuel"));
            Assert.IsNotNull(blocker);
            Assert.IsFalse(blocker.Cause.Contains("config value"),
                "With a configured dealer source the cause is the empty store, not the config.");
        }

        [Test]
        public void ClassifyBakery_NoFuelWithUnobservedConfigValueSaysSo()
        {
            BakeryOperationsDetail detail = BoundBakery();
            detail.FuelUnits = 0;
            detail.FuelDealerIntakeUnitsPerBakeryUnit = -1;
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyBakery(detail, "bakery", blockers, new List<BusinessNote>());

            BusinessBlocker blocker = blockers.FirstOrDefault(b => b.Title.Contains("no fuel"));
            Assert.IsNotNull(blocker);
            Assert.IsTrue(blocker.Cause.Contains("not observed"),
                "An unobserved config value must be reported as unknown, never guessed as 0.");
        }

        [Test]
        public void ClassifyBakery_NoFlourAndNoNotionsAreSeparateBlockersWithCauses()
        {
            BakeryOperationsDetail detail = BoundBakery();
            detail.FlourUnits = 0;
            detail.NotionsUnits = 0;
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyBakery(detail, "bakery", blockers, new List<BusinessNote>());

            Assert.IsTrue(blockers.Any(b => b.Title.Contains("no flour") && b.Cause.Length > 0));
            Assert.IsTrue(blockers.Any(b => b.Title.Contains("no notions") && b.Cause.Length > 0));
            Assert.IsTrue(blockers.All(b => !string.IsNullOrWhiteSpace(b.Cause)),
                "No business shows a blocker without a cause.");
        }

        // --- D-02 wholesale starvation ------------------------------------------

        [Test]
        public void ClassifyBakery_WholesaleFirstWithDueUnitsAndNoRetailIsStarved()
        {
            BakeryOperationsDetail detail = BoundBakery();
            detail.SupplyProtectionPolicy = "ProtectWholesaleContracts";
            detail.WholesaleAccountCount = 1;
            detail.WholesaleUnitsDueToday = 24;
            detail.RetailUnitsAvailableForSale = 0;
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyBakery(detail, "bakery", blockers, new List<BusinessNote>());

            Assert.IsTrue(blockers.Any(b => b.Id == "D-02" && b.Cause.Contains("RetailAllowanceFor")),
                "Under ProtectWholesaleContracts with due units and zero retail, the counter is starved.");
        }

        [Test]
        public void ClassifyBakery_RetailFirstPolicyDoesNotStarve()
        {
            BakeryOperationsDetail detail = BoundBakery();
            detail.SupplyProtectionPolicy = "ProtectRetailCounter";
            detail.WholesaleAccountCount = 1;
            detail.WholesaleUnitsDueToday = 24;
            detail.RetailUnitsAvailableForSale = 0;
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyBakery(detail, "bakery", blockers, new List<BusinessNote>());

            Assert.IsFalse(blockers.Any(b => b.Id == "D-02"),
                "Lane 9a changed the default to ProtectRetailCounter; under it the counter is not starved.");
        }

        [Test]
        public void ClassifyBakery_StandingWholesaleAccountNotesTheNettingRule()
        {
            BakeryOperationsDetail detail = BoundBakery();
            detail.WholesaleAccountCount = 1;
            detail.WholesaleUnitsDueToday = 24;
            detail.RetailUnitsNetOfDueWholesale = 5;
            var notes = new List<BusinessNote>();
            BusinessOperationsMonitor.ClassifyBakery(detail, "bakery", new List<BusinessBlocker>(), notes);

            Assert.IsTrue(notes.Any(n => n.Id == "D-03" && n.Text.Contains("nets due-today wholesale")),
                "A standing account must surface the Lane 9a netting rule, not the old blanket veto.");
        }

        // --- D-18 fuel dealer -----------------------------------------------------

        [Test]
        public void ClassifyFuelDealer_UnseededDealerHasNoReachableCustomers()
        {
            var detail = new FuelDealerOperationsDetail
            {
                StockIntakeUnits = 100,
                TargetStockIntakeUnits = 200,
                ReorderPointIntakeUnits = 60,
                RetailPriceCentsPerBundle = 10,
                SellerKnowledgeSeeded = false,
                RetailSalesRecorded = 0,
            };
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyFuelDealer(detail, "fuel-dealer", blockers);

            BusinessBlocker blocker = blockers.FirstOrDefault(b => b.Id == "D-18");
            Assert.IsNotNull(blocker);
            Assert.IsTrue(blocker.Cause.Contains("SeedLocalFuelDealerKnowledge"),
                "The cause must name the missing seeding by its exact name.");
            Assert.IsTrue(blocker.Cause.Contains("Lane 9e"),
                "Seeding is Lane 9e's repair; this lane surfaces it and does not implement it.");
            // Fix (F2): the cause must describe the actual failure mode (no
            // household knows the dealer), not the now-false claim that the
            // seeding method does not exist anywhere.
            Assert.IsTrue(blocker.Cause.Contains("no household knows"),
                "The cause must say what actually failed: no household knows the dealer.");
            Assert.IsFalse(blocker.Cause.Contains("exists anywhere"),
                "The cause must not claim the seeding method does not exist; Lane 9e landed it.");
        }

        [Test]
        public void ClassifyFuelDealer_SeededDealerWithStockHasNoBlockers()
        {
            var detail = new FuelDealerOperationsDetail
            {
                StockIntakeUnits = 100,
                TargetStockIntakeUnits = 200,
                ReorderPointIntakeUnits = 60,
                RetailPriceCentsPerBundle = 10,
                SellerKnowledgeSeeded = true,
                RetailSalesRecorded = 3,
            };
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyFuelDealer(detail, "fuel-dealer", blockers);
            Assert.AreEqual(0, blockers.Count);
        }

        [Test]
        public void ClassifyFuelDealer_NoStockNoOrderNoIncomingIsBlocked()
        {
            var detail = new FuelDealerOperationsDetail
            {
                StockIntakeUnits = 0,
                TargetStockIntakeUnits = 200,
                ReorderPointIntakeUnits = 60,
                RetailPriceCentsPerBundle = 10,
                SellerKnowledgeSeeded = true,
            };
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyFuelDealer(detail, "fuel-dealer", blockers);
            Assert.IsTrue(blockers.Any(b => b.Id == "D-19" && b.Title.Contains("no fuel stock")));
        }

        [Test]
        public void ClassifyFuelDealer_BelowReorderPointWithNoOrderIsBlocked()
        {
            var detail = new FuelDealerOperationsDetail
            {
                StockIntakeUnits = 30,
                TargetStockIntakeUnits = 200,
                ReorderPointIntakeUnits = 60,
                RetailPriceCentsPerBundle = 10,
                SellerKnowledgeSeeded = true,
            };
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyFuelDealer(detail, "fuel-dealer", blockers);
            Assert.IsTrue(blockers.Any(b => b.Id == "D-19" && b.Title.Contains("reorder point")));
        }

        [Test]
        public void ClassifyFuelDealer_ZeroPriceIsBlocked()
        {
            var detail = new FuelDealerOperationsDetail
            {
                StockIntakeUnits = 100,
                TargetStockIntakeUnits = 200,
                ReorderPointIntakeUnits = 60,
                RetailPriceCentsPerBundle = 0,
                SellerKnowledgeSeeded = true,
            };
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyFuelDealer(detail, "fuel-dealer", blockers);
            Assert.IsTrue(blockers.Any(b => b.Id == "D-17"));
        }

        // --- Store: clerk, queue, cadence ---------------------------------------

        [Test]
        public void ClassifyStore_NoClerkAndNoCandidatesIsBlocked()
        {
            var detail = new StoreOperationsDetail
            {
                ClerkPersonId = -1,
                ClerkCandidateCount = 0,
                QueueWaitingCount = 0,
                QueueMaxTolerated = 5,
                PhysicalCounterServiceRequired = true,
            };
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyStore(detail, "general-store", blockers, new List<BusinessNote>());

            BusinessBlocker blocker = blockers.FirstOrDefault(b => b.Id == "D-15");
            Assert.IsNotNull(blocker);
            Assert.IsTrue(blocker.Cause.Contains("STORE_UNSERVED"),
                "With no clerk the counter fails closed; the cause must say so.");
        }

        [Test]
        public void ClassifyStore_OwnerOperatorFallbackMeansNoClerkBlocker()
        {
            var detail = new StoreOperationsDetail
            {
                ClerkPersonId = 3,
                ClerkCandidateCount = 1,
                QueueWaitingCount = 0,
                QueueMaxTolerated = 5,
                PhysicalCounterServiceRequired = true,
            };
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyStore(detail, "general-store", blockers, new List<BusinessNote>());
            Assert.IsFalse(blockers.Any(b => b.Id == "D-15"));
        }

        [Test]
        public void ClassifyStore_FullQueueIsBlocked()
        {
            var detail = new StoreOperationsDetail
            {
                ClerkPersonId = 3,
                ClerkCandidateCount = 1,
                QueueWaitingCount = 5,
                QueueMaxTolerated = 5,
            };
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyStore(detail, "general-store", blockers, new List<BusinessNote>());
            Assert.IsTrue(blockers.Any(b => b.Id == "D-16"));
        }

        [Test]
        public void ClassifyStore_LegacyDeadDailySalesIsANoteNotABlocker()
        {
            var detail = new StoreOperationsDetail
            {
                ClerkPersonId = 3,
                ClerkCandidateCount = 1,
                DailySalesState = DailySalesCadenceState.LegacyDeadNoPlayerStore,
            };
            var blockers = new List<BusinessBlocker>();
            var notes = new List<BusinessNote>();
            BusinessOperationsMonitor.ClassifyStore(detail, "general-store", blockers, notes);
            Assert.IsFalse(blockers.Any(b => b.Id == "D-09"));
            Assert.IsTrue(notes.Any(n => n.Id == "D-09" && n.Text.Contains("Lane 9b")),
                "D-09 is a known state with a live replacement cadence; it is noted, not blocked.");
        }

        // --- General: runtime, weekly reason, payroll, staffing -----------------

        [Test]
        public void ClassifyGeneral_WeeklyBlockedReasonIsSurfacedVerbatim()
        {
            BusinessSnapshot snapshot = HealthySnapshot("sawmill");
            snapshot.LastWeeklyBlockedReason = "yard cannot pay the full lot price";
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyGeneral(snapshot, blockers, new List<BusinessNote>());

            BusinessBlocker blocker = blockers.FirstOrDefault(b => b.Id == "BIZ-WEEKLY");
            Assert.IsNotNull(blocker);
            Assert.AreEqual("yard cannot pay the full lot price", blocker.Cause,
                "The weekly blocked reason is the cause, verbatim — never a bare 'blocked'.");
        }

        [Test]
        public void ClassifyGeneral_NoneBlockedReasonIsNotABlocker()
        {
            BusinessSnapshot snapshot = HealthySnapshot("sawmill");
            snapshot.LastWeeklyBlockedReason = "None";
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyGeneral(snapshot, blockers, new List<BusinessNote>());
            Assert.AreEqual(0, blockers.Count);
        }

        [Test]
        public void ClassifyGeneral_SuspendedWorkerIsBlocked()
        {
            BusinessSnapshot snapshot = HealthySnapshot("sawmill");
            snapshot.WorkerSlots = new List<BusinessWorkerSnapshot>
            {
                new BusinessWorkerSnapshot
                {
                    SlotId = "sawyer", DisplayName = "Sawyer", Filled = true,
                    WorkerDisplayName = "Richard", WeeklyWageCents = 750,
                    PaidActive = false, SuspendedForMissedPayroll = true,
                },
            };
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyGeneral(snapshot, blockers, new List<BusinessNote>());
            BusinessBlocker blocker = blockers.FirstOrDefault(b => b.Id == "BIZ-PAYROLL");
            Assert.IsNotNull(blocker);
            Assert.IsTrue(blocker.Title.Contains("Sawyer"), "The title names the suspended slot.");
            Assert.IsTrue(blocker.Cause.Contains("Richard"), "The cause names the suspended worker.");
        }

        [Test]
        public void ClassifyGeneral_UnfilledRequiredSlotIsANoteNotABlocker()
        {
            BusinessSnapshot snapshot = HealthySnapshot("sawmill");
            snapshot.WorkerSlots = new List<BusinessWorkerSnapshot>
            {
                new BusinessWorkerSnapshot
                {
                    SlotId = "sawyer", DisplayName = "Sawyer", Filled = false,
                    RequiredForOpening = true,
                },
            };
            var blockers = new List<BusinessBlocker>();
            var notes = new List<BusinessNote>();
            BusinessOperationsMonitor.ClassifyGeneral(snapshot, blockers, notes);
            Assert.AreEqual(0, blockers.Count);
            Assert.IsTrue(notes.Any(n => n.Id == "BIZ-STAFF" && n.Text.Contains("not a causal gate")),
                "Required-for-opening is a staffing-need flag, not a causal gate.");
        }

        [Test]
        public void ClassifyGeneral_CashAtZeroAloneIsNotABlocker()
        {
            BusinessSnapshot snapshot = HealthySnapshot("lumber-yard");
            snapshot.CashCents = 0;
            var blockers = new List<BusinessBlocker>();
            BusinessOperationsMonitor.ClassifyGeneral(snapshot, blockers, new List<BusinessNote>());
            Assert.AreEqual(0, blockers.Count,
                "Cash at zero is a legitimate economic stoppage (yard at 29c, D-25), not a defect by itself.");
        }

        // --- Cadence labels ------------------------------------------------------

        [Test]
        public void CadenceLabels_NameBothCadencesAndTheirStates()
        {
            Assert.AreEqual("NPC daily cadence (Lane 9b reset)",
                BusinessOperationsMonitor.DailyCadenceLabel(DailySalesCadenceState.NpcDailyReset));
            Assert.AreEqual("Legacy daily sales dead (no player store)",
                BusinessOperationsMonitor.DailyCadenceLabel(DailySalesCadenceState.LegacyDeadNoPlayerStore));
            Assert.AreEqual("Weekly payroll settled (shared manager)",
                BusinessOperationsMonitor.WeeklyCadenceLabel(WeeklyPayrollCadenceState.SettledViaSharedManager));
            Assert.AreEqual("Weekly payroll settled (dedicated manager)",
                BusinessOperationsMonitor.WeeklyCadenceLabel(WeeklyPayrollCadenceState.SettledViaDedicatedManager));
        }

        // --- BuildReport ----------------------------------------------------------

        [Test]
        public void BuildReport_AggregatesBlockersNotesCadencesAndMissingBusinesses()
        {
            BusinessSnapshot bakery = HealthySnapshot("bakery");
            bakery.Bakery = BoundBakery();
            bakery.Bakery.OwnerPersonId = -1;
            BusinessSnapshot store = HealthySnapshot("general-store");
            store.Store = new StoreOperationsDetail
            {
                ClerkPersonId = 3,
                ClerkCandidateCount = 1,
                DailySalesState = DailySalesCadenceState.NpcDailyReset,
            };

            var cadences = new List<BusinessSettlementCadence>
            {
                new BusinessSettlementCadence
                {
                    BusinessKey = "general-store",
                    DailyState = DailySalesCadenceState.NpcDailyReset,
                    DailyEvidence = "DriveNpcDailySalesCadence reset the day.",
                    WeeklyState = WeeklyPayrollCadenceState.SettledViaSharedManager,
                    WeeklyEvidence = "ResolveNpcGeneralStoreWeeklyPayroll settled week 0.",
                    WeeklySettledWeekIndex = 0,
                },
            };

            BusinessOperationsReport report = BusinessOperationsMonitor.BuildReport(
                new[] { bakery, store }, cadences);

            Assert.AreEqual(1, report.OwnerDecisionCount);
            Assert.AreEqual(1, report.HealthyCount);
            Assert.AreEqual(0, report.BlockedCount);
            Assert.AreEqual(0, report.UnknownCount);
            Assert.IsTrue(report.Blockers.Any(b => b.Id == "D-04" && b.IsOwnerDecision));
            Assert.AreEqual(1, report.Cadences.Count);
            Assert.AreEqual(DailySalesCadenceState.NpcDailyReset, report.Cadences[0].DailyState);
            Assert.AreEqual(WeeklyPayrollCadenceState.SettledViaSharedManager, report.Cadences[0].WeeklyState);
            Assert.AreEqual(5, report.MissingBusinessKeys.Count,
                "Only 2 of the 7 expected businesses were observed; 5 are missing.");
            Assert.IsTrue(report.MissingBusinessKeys.Contains("fuel-dealer"));
            Assert.IsTrue(report.Blockers.All(b => !string.IsNullOrWhiteSpace(b.Cause)),
                "No business shows a blocker without a cause.");
        }
    }
}
