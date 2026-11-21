using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.World;
using LandLedgers.World.Property;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Orchestration
{
    public sealed class SimulationDriversSceneBindingTests
    {
        [Test]
        public void DueEventFailureClassifier_DoesNotPromoteSuccessfulCapacityEvidenceToWarning()
        {
            const string capacityEvidence = "[V1] capacity consulted at settle: P9 carrying 1u potatoes "
                + "(live cap 50u) — Fits: On-foot carry is within the live cap. "
                + "Mass and volume remain Unknown — no authored person mass or volume limit exists.";

            Assert.IsFalse(ClassifyDueEventDiagnostic(capacityEvidence),
                "A successful Fits result remains informational even if its evidence mentions Unknown limits.");
            Assert.IsTrue(ClassifyDueEventDiagnostic("Trip aborted: outbound route failed."),
                "Actual failed transitions must remain warning-worthy.");
        }

        [Test]
        public void WorkAssignmentDiagnosticClassifier_KeepsSuccessfulSchedulingInformationalAndRefusalsLoud()
        {
            var successfulScheduling = new List<string>
            {
                "WorkScheduler: 1 open item(s), 1 worker(s), day 1",
                "assigned T3 (Travel, 2m) to P3",
                "Day 1: P3 work released rest Resting [480-510] for the window from minute 480."
            };
            Assert.IsFalse(ClassifyWorkAssignmentDiagnostic(successfulScheduling),
                "open counts, a real assignment, and explicit rest preemption are successful scheduling outcomes.");

            Assert.IsTrue(ClassifyWorkAssignmentDiagnostic(new List<string>
            {
                "T3 unassigned: unassignable: P3 has no available time window"
            }), "an unassigned task remains warning-worthy.");
            Assert.IsTrue(ClassifyWorkAssignmentDiagnostic(new List<string>
            {
                "task T3: start refused (person already occupied)"
            }), "a task-start refusal remains warning-worthy.");
            Assert.IsTrue(ClassifyWorkAssignmentDiagnostic(new List<string>
            {
                "new unclassified scheduler diagnostic"
            }), "unknown diagnostic forms default to warning rather than being hidden.");
        }

        [Test]
        public void DailyNeedsDiagnosticClassifier_DropsRoutineNoObligationSummariesButKeepsFailuresLoud()
        {
            Assert.IsTrue(SimulationDrivers.IsRoutineDailyNeedsDiagnostic(
                "[FoodEconomy] shopping plan day 4: 0 physical trip(s) pending; no purchase is recorded until confirmed Store arrival; "
                + "needs without a settled receipt remain open: none", 4));
            Assert.IsTrue(SimulationDrivers.IsRoutineDailyNeedsDiagnostic(
                "HouseholdAutonomy: household 3 -> NoAction (NoAction: no obligations due (reserves 40000c))", 4));

            Assert.IsFalse(SimulationDrivers.IsRoutineDailyNeedsDiagnostic(
                "DailyNeedsService: P7 is off-map, needs stay open for return.", 4));
            Assert.IsFalse(SimulationDrivers.IsRoutineDailyNeedsDiagnostic(
                "Day 4: rejected 'Glen Pierce Bakery' for 'bread': no available stock (real lots exhausted).", 4));
            Assert.IsFalse(SimulationDrivers.IsRoutineDailyNeedsDiagnostic(
                "Day 4: shopping trip trip-4-0 planned — P1 → 'General Store' (1 min out, 15 min in store, 1 min back).", 4));
        }

        [Test]
        public void StagedBakeryBatch_UsesUncoveredHouseholdDemandWithoutDuplicatingActiveWork()
        {
            Assert.IsFalse(
                SimulationDrivers.ShouldBeginStagedBakeryBatch(0, 0, false, false),
                "No open household bread need means no retail batch.");
            Assert.IsFalse(
                SimulationDrivers.ShouldBeginStagedBakeryBatch(8, 8, false, false),
                "The bakery's saleable retail stock already covers the open need.");
            Assert.IsFalse(
                SimulationDrivers.ShouldBeginStagedBakeryBatch(24, 0, true, false),
                "An active conventional wholesale batch already occupies the production pipeline.");
            Assert.IsTrue(
                SimulationDrivers.ShouldBeginStagedBakeryBatch(24, 0, false, false),
                "An uncovered real household need authorizes one staged retail batch.");
            Assert.IsFalse(
                SimulationDrivers.ShouldBeginStagedBakeryBatch(48, 0, false, true),
                "An active staged batch or output awaiting shelf transfer prevents duplicate production.");
        }

        [Test]
        public void StagedBakeryPlanning_DoesNotCompeteWithAnActiveWholesaleAccount()
        {
            Assert.IsFalse(SimulationDrivers.ShouldBeginStagedBakeryBatch(
                    24, 0, false, false, hasActiveWholesaleAccount: true),
                "an active wholesale commitment remains with the conventional account planner; the staged retail path cannot consume the same bakery inputs concurrently");
            Assert.IsTrue(SimulationDrivers.ShouldBeginStagedBakeryBatch(
                    24, 0, false, false, hasActiveWholesaleAccount: false),
                "with no wholesale commitments, uncovered actual household need can still use staged physical production");
        }

        [Test]
        public void StagedBakeryRetailDemand_UsesOnlyOpenHouseholdBreadNeeds()
        {
            var needs = new HouseholdNeedRegistry();
            HouseholdPurchasingNeed openBread = needs.CreateNeed(1, HouseholdItemCatalog.BreadId, 2);
            openBread.UnitsNeeded = 9;
            HouseholdPurchasingNeed otherNeed = needs.CreateNeed(1, "beef", 2);
            otherNeed.UnitsNeeded = 40;
            HouseholdPurchasingNeed fulfilledBread = needs.CreateNeed(2, HouseholdItemCatalog.BreadId, 1);
            fulfilledBread.UnitsNeeded = 50;
            fulfilledBread.Status = PurchasingNeedStatus.Fulfilled;

            Assert.AreEqual(9, SimulationDrivers.CountOpenHouseholdBreadNeedUnits(needs),
                "Retail production demand comes from actual open bread needs, not other goods or historical fulfilled needs.");
        }

        [Test]
        public void StagedBakeryWorkerSelection_SkipsBusyOwnerAndUsesFirstEligibleActiveEmployee()
        {
            int selected = SimulationDrivers.FirstEligibleStagedBakeryWorkerId(
                new[] { 2, 7, 7, -1, 9 },
                personId => personId == 7 || personId == 9);

            Assert.AreEqual(7, selected,
                "An unavailable owner must not hide the first eligible employee, and duplicate/invalid identities are ignored.");
            Assert.AreEqual(-1, SimulationDrivers.FirstEligibleStagedBakeryWorkerId(
                new[] { 2, 7 }, personId => false),
                "No worker candidate may be fabricated when every legitimate candidate is occupied or unavailable.");
        }

        [Test]
        public void WorkshopMadeCustodyFurniture_IsReportedWithoutTreatingOtherGoodsAsInstallable()
        {
            MethodInfo method = typeof(SimulationDrivers).GetMethod(
                "HasInstallableCustodyFurniture", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "The custody warning predicate must remain available.");

            var register = new HouseholdFurnishingRegister();
            register.Assets.Add(new EquipmentAsset
            {
                AssetId = "purchased-bed-1",
                Kind = "bed",
                Condition01 = 1f,
                MadeByBusinessId = string.Empty,
            });
            Assert.IsFalse((bool)method.Invoke(null, new object[] { register }),
                "An uninstalled item without workshop provenance must not be mislabeled as workshop-made.");

            register.Assets.Add(new EquipmentAsset
            {
                AssetId = "workshop-table-1",
                Kind = "table",
                Condition01 = 1f,
                MadeByBusinessId = "carpentry-shop-1",
            });
            Assert.IsTrue((bool)method.Invoke(null, new object[] { register }),
                "A usable, unreserved workshop-made asset lacking a live placement must remain in custody and be reported.");
        }

        private static bool ClassifyDueEventDiagnostic(string message)
        {
            MethodInfo classifier = typeof(SimulationDrivers).GetMethod(
                "IsDueEventFailureDiagnostic", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(classifier, "The due-event diagnostic classifier must remain present.");
            return (bool)classifier.Invoke(null, new object[] { message });
        }

        private static bool ClassifyWorkAssignmentDiagnostic(List<string> diagnostics)
        {
            MethodInfo classifier = typeof(SimulationDrivers).GetMethod(
                "HasActionableWorkAssignmentDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(classifier, "The work-assignment diagnostic classifier must remain present.");
            return (bool)classifier.Invoke(null, new object[] { diagnostics });
        }

        private sealed class StorePort : IGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; }
            public string StoreName => "Local General Store";
            public string LocationId => "town-store";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability TradingCapability => new StoreTradingCapability();
            public int CurrentCashCents => 0;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;
            public int CategoryStockUnits(string categoryId) => 0;
            public int CategoryPricePerUnitCents(string categoryId) => 0;
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            { consumedUnits = 0; return false; }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose) => "not used";
            public string ReceiveStock(string categoryId, int units, string provenanceLabel) => "not used";
            public int CategoryReceivableUnits(string categoryId) => 0;
            public int CategoryTargetStockUnits(string categoryId) => 0;
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class OtherSupplier : IGoodsSupplier
        {
            public string SupplierBusinessId => "mill-1";
            public string SupplierName => "Mill";
            public string LocationId => "mill-road";
            public bool HasCategory(string categoryId) => false;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;
        }

        private sealed class BakeryBusinessCash : IBusinessIdentityCashPort
        {
            public BakeryBusinessCash(string businessId) => BusinessId = businessId;
            public string BusinessId { get; }
            public int BalanceCents { get; private set; } = 5000;
            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents < 0 || amountCents > BalanceCents) return "insufficient test cash";
                BalanceCents -= amountCents;
                return null;
            }
            public string Credit(int amountCents, string reason)
            {
                if (amountCents < 0) return "negative test credit";
                BalanceCents += amountCents;
                return null;
            }
        }

        [Test]
        public void Rebind_AttachesDiscoveredTownWorldToHubParcelAuthorities()
        {
            GameObject hubObject = new("Scene Binding Hub");
            GameObject worldObject = new("Scene Binding Town World");
            GameObject driversObject = new("Scene Binding Drivers");
            try
            {
                SimulationSystemsHub hub = hubObject.AddComponent<SimulationSystemsHub>();
                worldObject.AddComponent<TownWorldController>();
                SimulationDrivers drivers = driversObject.AddComponent<SimulationDrivers>();

                drivers.Rebind();

                Assert.IsInstanceOf<TownWorldParcelBuildingIndex>(
                    hub.ParcelSubdivisions.BuildingIndex,
                    "A freshly rebound driver must attach the real scene world to parcel subdivision checks.");
            }
            finally
            {
                Object.DestroyImmediate(driversObject);
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(hubObject);
            }
        }

        [Test]
        public void LocalGeneralStoreKnowledge_IsIdempotentAndExcludesOtherOrPlaceholderSuppliers()
        {
            var people = new List<PersonState>
            {
                new PersonState { id = 1, deathDayIndex = -1 },
                new PersonState { id = 2, deathDayIndex = 9 },
                new PersonState { id = 3, isOffMap = true },
            };
            var actualStore = new GeneralStoreSupplierAdapter(new StorePort { StoreBusinessId = "general_store_060" });
            var placeholderStore = new GeneralStoreSupplierAdapter(new StorePort { StoreBusinessId = "general-store" });
            var suppliers = new List<IGoodsSupplier> { actualStore, placeholderStore, new OtherSupplier() };
            var knowledge = new ShopperSellerKnowledge();

            SimulationDrivers.SeedLocalGeneralStoreKnowledge(people, suppliers, knowledge, 12);
            SimulationDrivers.SeedLocalGeneralStoreKnowledge(people, suppliers, knowledge, 13);

            Assert.IsTrue(knowledge.Knows(1, "general_store_060"));
            Assert.IsFalse(knowledge.Knows(2, "general_store_060"), "Dead residents are not seeded as current local shoppers.");
            Assert.IsFalse(knowledge.Knows(3, "general_store_060"), "Off-map Persons are not residents of this local store's town.");
            Assert.IsFalse(knowledge.Knows(1, "general-store"), "The generic placeholder identity must never be learned as B60.");
            Assert.IsFalse(knowledge.Knows(1, "mill-1"), "Registering B60 must not reveal unrelated suppliers.");
            Assert.That(knowledge.Records.Count, Is.EqualTo(1), "Repeated bootstrap calls must not duplicate knowledge records.");
            Assert.That(knowledge.Records[0].LearnedDayIndex, Is.EqualTo(12), "Idempotence preserves the original discovery event.");
        }

        [Test]
        public void BakerySupplierRegistration_UsesMatchingBusinessCashAndSeedsOnlyLocalResidents()
        {
            var directory = new SupplierDirectory();
            var firstRuntime = new BakeryShopRuntime("bakery-1");
            BakeryBusinessCash cash = new BakeryBusinessCash("bakery-1");
            Series2BakerySupplierAdapter first = SimulationDrivers.RegisterBakerySupplierInDirectory(
                directory, firstRuntime, cash, "bakery-1", "Glenn's Bakery", "building:42");

            Assert.IsNotNull(first);
            Assert.AreEqual(1, directory.All.Count);
            Assert.IsTrue(first.OffersItem(HouseholdItemCatalog.BreadId));
            Assert.IsFalse(first.OffersItem("cooked_meal"), "A bread seller must not silently advertise an unrelated item.");
            Assert.AreEqual(0, first.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "Registration must not create opening bread stock.");

            var restoredRuntime = new BakeryShopRuntime("bakery-1");
            Series2BakerySupplierAdapter restored = SimulationDrivers.RegisterBakerySupplierInDirectory(
                directory, restoredRuntime, new BakeryBusinessCash("bakery-1"),
                "bakery-1", "Glenn's Bakery", "building:42");
            Assert.AreEqual(1, directory.All.Count, "Restore must replace the old runtime adapter, not leave a second seller.");
            Assert.AreSame(restored, directory.All[0]);

            Assert.IsNull(SimulationDrivers.RegisterBakerySupplierInDirectory(
                directory, new BakeryShopRuntime("bakery-2"), cash,
                "bakery-2", "Another Bakery", "building:43"),
                "A bakery must not be wired to a different business's cash authority.");
            Assert.IsNull(SimulationDrivers.RegisterBakerySupplierInDirectory(
                directory, new BakeryShopRuntime("bakery-3"), new BakeryBusinessCash("bakery-3"),
                "bakery-3", "Unplaced Bakery", string.Empty),
                "A seller without an authored building location cannot create a walkable shopping destination.");

            var people = new List<PersonState>
            {
                new PersonState { id = 1, deathDayIndex = -1 },
                new PersonState { id = 2, deathDayIndex = 10 },
                new PersonState { id = 3, isOffMap = true },
            };
            var knowledge = new ShopperSellerKnowledge();
            SimulationDrivers.SeedLocalBakeryKnowledge(people, directory.All, knowledge, 12);
            SimulationDrivers.SeedLocalBakeryKnowledge(people, directory.All, knowledge, 13);

            Assert.IsTrue(knowledge.Knows(1, "bakery-1"));
            Assert.IsFalse(knowledge.Knows(2, "bakery-1"));
            Assert.IsFalse(knowledge.Knows(3, "bakery-1"));
            Assert.AreEqual(1, knowledge.Records.Count, "Repeated bootstrap must not duplicate seller knowledge.");
            Assert.AreEqual(12, knowledge.Records[0].LearnedDayIndex);
        }
    }
}
