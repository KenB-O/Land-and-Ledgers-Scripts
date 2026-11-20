using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Carpentry;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.GenericBusiness;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// G-1 (expanded campaign 2026-11-16, W-G4 Workstream 1): the
    /// carpentry production driver — the equipment-output route for
    /// furniture methods, the lumber reorder policy, the nails/cloth
    /// store purchase leg, and the workshop-to-household sale. Fixture
    /// mirrors CarpentryWorkshopF1Part2Tests (the same endowed workshop,
    /// yard, and cash ports) and SawmillLubricationPurchaseC5Tests (the
    /// store seeded with identified retail lots). Unity compilation,
    /// Test Runner, PlayMode, and the driver call site are NOT claimed;
    /// SimulationDrivers and FirstLedgerSliceBootstrapper are
    /// harness-excluded, so the driver and the premises binding are
    /// source-reviewed only and exercised here through the same public
    /// APIs they call.
    /// </summary>
    [TestFixture]
    public sealed class CarpentryWorkshopDriverG1Tests
    {
        private const string WorkshopKey = "carpentry-workshop";
        private const string WorkshopInstanceId = "g1-test-workshop";
        private const string StoreId = "g1-test-store";
        private const int DayIndex = 1;

        private static readonly EntityId Carpenter = EntityId.For(EntityKind.Person, 911);
        private static readonly EntityId BusinessId = EntityId.For(EntityKind.Business, 911);

        private static void SetPrivateField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static DevelopmentTownConfiguration BuildConfig(
            DevelopmentTownConfiguration.BusinessOpeningEquipment[] openingEquipment,
            int openingLumberUnits)
        {
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            // Schema 2 (foundation pass): configurationId, seedOffset and
            // freightBusinessKey live on the nested GeneralSettings; the
            // equipment and lumber live on the workshop's opening record
            // and the horse/wagon on the freight business's opening
            // record. The flat top-level fields no longer exist.
            config.Settings.configurationId = "g1-test-town";
            config.Settings.seedOffset = 0;
            config.Settings.freightBusinessKey = "freight-company";
            SetPrivateField(config, "people", new[]
            {
                new DevelopmentTownConfiguration.PersonDefinition
                { key = "don-millward", firstName = "Don", lastName = "Millward", age = 58, homeBuildingOrdinal = 7, occupation = "Carpenter" },
                new DevelopmentTownConfiguration.PersonDefinition
                { key = "grace-millward", firstName = "Grace", lastName = "Millward", age = 56, isFemale = true, homeBuildingOrdinal = 7 },
            });
            SetPrivateField(config, "households", new[]
            {
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "millward", surname = "Millward", displayName = "Millward Household",
                    memberPersonKeys = new[] { "don-millward", "grace-millward" }, homeBuildingOrdinal = 7, openingCashCents = 0,
                },
            });
            SetPrivateField(config, "relationships", new[]
            {
                new DevelopmentTownConfiguration.RelationshipDefinition
                { firstPersonKey = "don-millward", secondPersonKey = "grace-millward", relationship = 0 },
            });
            DevelopmentTownConfiguration.BusinessOpeningEquipment workshopEquipment =
                new DevelopmentTownConfiguration.BusinessOpeningEquipment();
            if (openingEquipment != null)
                foreach (DevelopmentTownConfiguration.BusinessOpeningEquipment entry in openingEquipment)
                    if (entry != null && string.Equals(entry.businessKey, WorkshopKey, StringComparison.OrdinalIgnoreCase))
                    { workshopEquipment = entry; break; }
            SetPrivateField(config, "businesses", new[]
            {
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = WorkshopKey, operationId = "carpentry-workshop", ownerPersonKey = "don-millward",
                    businessId = WorkshopInstanceId, premisesRequired = true, openingCashCents = 10000,
                    opening = new DevelopmentTownConfiguration.BusinessOpeningConfiguration
                    {
                        equipment = workshopEquipment,
                        carpentryLumberUnits = openingLumberUnits,
                    },
                },
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = "freight-company", operationId = "livery-freight", ownerPersonKey = "don-millward",
                    businessId = "g1-test-freight", premisesRequired = false,
                    opening = new DevelopmentTownConfiguration.BusinessOpeningConfiguration
                    {
                        openingHorseId = "A0",
                        openingWagonId = "g1-test-wagon",
                    },
                },
            });
            return config;
        }

        private static DevelopmentTownConfiguration.BusinessOpeningEquipment CarpentryEquipmentEntry() =>
            new DevelopmentTownConfiguration.BusinessOpeningEquipment
            {
                businessKey = WorkshopKey,
                assetEntries = new[]
                {
                    new DevelopmentTownConfiguration.OpeningAssetEntry { assetId = "carpenter-workbench-1", kind = "workbench" },
                },
                kitIds = new[] { "carpenter-hand-tool-kit" },
            };

        /// <summary>The test workshop: Don Millward's Generic business, with its register and workstation registry.</summary>
        private sealed class Workshop
        {
            public readonly BusinessInstanceState Business;
            public readonly BusinessEquipmentRegister Register = new BusinessEquipmentRegister();
            public readonly BusinessWorkstations Workstations;
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly CarpentryProduction Production;

            public Workshop()
            {
                BusinessProfileDefinition profile =
                    BusinessProfileDefinition.CreateFallback(BusinessType.Generic, "Don Millward Carpentry Workshop");
                Business = BusinessInstanceState.Create(
                    WorkshopInstanceId, profile, -1, BusinessOwnerIdentity.Npc(911, "Don Millward", "Millward"));
                Register.BusinessInstanceId = WorkshopInstanceId;
                Workstations = new BusinessWorkstations(WorkshopInstanceId);
                Production = new CarpentryProduction(Business, Register);
            }

            public GenericProductionRuntime NewRuntime() =>
                new GenericProductionRuntime(Tasks, Budgets, BusinessId, DayIndex);

            public void ApplyTemplate()
            {
                Assert.IsTrue(
                    GenericBusinessSetupTemplateCatalog.TryApply(Business, GenericBusinessSetupTemplateCatalog.CarpentryId, out string message),
                    message);
            }

            public void Endow(DevelopmentTownConfiguration config)
            {
                var diag = new List<string>();
                Assert.IsTrue(
                    ScenarioCarpentryOpeningEndowment.TryApply(
                        config, WorkshopKey, Business, Register, Workstations, DayIndex, diag, out string report, out string error),
                    error);
            }

            public void Stock(string productId, float units, int unitCostCents, string provenance)
            {
                GenericProductDefinition definition = string.Equals(productId, "lumber", StringComparison.Ordinal)
                    ? GenericVerticalCatalog.LumberDefinition()
                    : new GenericProductDefinition(productId, productId, ProductQuantityUnit.Each, requiresLotIdentity: true);
                Business.GenericConfiguration.AddInventory(productId, units, unitCostCents, definition, provenance, DayIndex);
            }

            public float OnHand(string productId) =>
                Business.GenericConfiguration.GetInventoryQuantity(productId);
        }

        private sealed class RuntimeCashPort : IBusinessCashPort
        {
            private readonly BusinessRuntimeState runtime;
            public RuntimeCashPort(BusinessRuntimeState runtime) { this.runtime = runtime; }
            public int BalanceCents => runtime != null ? runtime.CurrentCashCents : 0;
            public string TryDebit(int amountCents, string reason) =>
                runtime == null ? "no runtime" : runtime.TrySpendCents(amountCents) ? null : "insufficient business cash";
            public string Credit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                runtime.AddCashCents(amountCents);
                return null;
            }
        }

        private sealed class FakeCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; set; }
            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents > BalanceCents) return "insufficient cash";
                BalanceCents -= amountCents;
                return null;
            }
            public string Credit(int amountCents, string reason)
            {
                BalanceCents += amountCents;
                return null;
            }
        }

        private static LumberYardShopRuntime NewYardWithStock(int units, int unitCostCents, List<string> diag)
        {
            var yard = new LumberYardShopRuntime("g1-test-yard", new EntityIdRegistry());
            Assert.IsNull(yard.ReceiveLumberPurchase(
                units, "eastern white pine", "merchantable", "g1-test-sawmill", "g1 fixture purchase", 0, unitCostCents, diag));
            return yard;
        }

        private static Workshop NewEndowedWorkshop()
        {
            var shop = new Workshop();
            shop.ApplyTemplate();
            shop.Endow(BuildConfig(new[] { CarpentryEquipmentEntry() }, 15));
            return shop;
        }

        private sealed class StoreSlice
        {
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
        }

        private static StoreSlice NewStoreWithSupplies(int nailsUnits, int clothUnits)
        {
            var s = new StoreSlice();
            s.StoreRuntime.BindBusinessIdentity(StoreId);
            s.StoreRuntime.AddCashCents(5000);
            s.StoreRuntime.EnsureCategoryStock("tools_hardware", 0, 1000);
            s.StoreRuntime.EnsureCategoryStock("clothing", 0, 1000);
            if (nailsUnits > 0)
            {
                Assert.IsNull(s.StoreRuntime.TryAddRetailItemLot(
                    "g1-test-store:nails", "nails_hardware_lot", "tools_hardware", "lot",
                    nailsUnits, StoreId, StoreId,
                    "DevelopmentTownConfiguration opening inventory, general-store nails", 0),
                    "store nails lot seeding should succeed");
            }
            if (clothUnits > 0)
            {
                Assert.IsNull(s.StoreRuntime.TryAddRetailItemLot(
                    "g1-test-store:cloth", "basic_cloth", "clothing", "yard",
                    clothUnits, StoreId, StoreId,
                    "DevelopmentTownConfiguration opening inventory, general-store cloth", 0),
                    "store cloth lot seeding should succeed");
            }
            return s;
        }

        private static EquipmentAsset MakeWorkshopAsset(string assetId, string kind) =>
            new EquipmentAsset
            {
                AssetId = assetId,
                Kind = kind,
                Condition01 = 1f,
                OwnerKind = "business",
                OwnerId = WorkshopInstanceId,
                LocationId = WorkshopInstanceId,
                MadeByBusinessId = WorkshopInstanceId,
                MadeDayIndex = DayIndex,
            };

        private static HouseholdLedger FundedLedger(int householdId, int cashCents)
        {
            var ledger = new HouseholdLedger(householdId);
            Assert.IsNull(ledger.RecordInflow(DayIndex, cashCents, HouseholdIncomeSource.OtherDocumented,
                "g1-test-funding", "test funding", "test"));
            return ledger;
        }

        private static FurnishingWant WantFor(string kind, int priority, bool affordable) =>
            new FurnishingWant
            {
                ItemKind = kind,
                Reason = FurnishingWantReason.BedForFloorSleeper,
                PersonsAffected = 2,
                Priority = priority,
                EstimatedCostCents = FurnitureValuation.ReplacementCostCentsForKind(kind),
                Affordable = affordable,
            };

        private static CarpentryFurnitureSaleCandidate CandidateFor(int householdId, HouseholdLedger ledger,
            HouseholdFurnishingRegister furnishings, params FurnishingWant[] wants) =>
            new CarpentryFurnitureSaleCandidate
            {
                HouseholdId = householdId,
                Ledger = ledger,
                Furnishings = furnishings,
                Wants = new List<FurnishingWant>(wants),
            };

        // ---------- workshop detection ----------

        [Test]
        public void IsCarpentryWorkshop_TrueOnlyForGenericWithFurnitureMethods()
        {
            var shop = NewEndowedWorkshop();
            Assert.IsTrue(CarpentryProduction.IsCarpentryWorkshop(shop.Business));

            BusinessProfileDefinition bakeryProfile =
                BusinessProfileDefinition.CreateFallback(BusinessType.Bakery, "G1 Test Bakery");
            BusinessInstanceState bakery = BusinessInstanceState.Create(
                "g1-test-bakery", bakeryProfile, -1, BusinessOwnerIdentity.Npc(912, "Test", "Baker"));
            Assert.IsFalse(CarpentryProduction.IsCarpentryWorkshop(bakery), "A bakery is not a carpentry workshop.");

            BusinessProfileDefinition plainProfile =
                BusinessProfileDefinition.CreateFallback(BusinessType.Generic, "G1 Plain Generic");
            BusinessInstanceState plain = BusinessInstanceState.Create(
                "g1-test-plain", plainProfile, -1, BusinessOwnerIdentity.Npc(913, "Test", "Plain"));
            Assert.IsFalse(CarpentryProduction.IsCarpentryWorkshop(plain), "A Generic business without the carpentry template has no furniture methods.");
            Assert.IsFalse(CarpentryProduction.IsCarpentryWorkshop(null));
        }

        // ---------- driver start leg ----------

        [Test]
        public void BeginEligible_PicksTableWhenBedBlockedOnHay()
        {
            var shop = NewEndowedWorkshop();
            shop.Stock("nails_hardware_lot", 2f, 25, "g1-fixture-nails");
            shop.Stock("basic_cloth", 8f, 12, "g1-fixture-cloth");
            GenericProductionRuntime runtime = shop.NewRuntime();

            Assert.IsTrue(shop.Production.TryBeginEligibleFurniture(runtime, new[] { Carpenter }, DayIndex,
                out ProductionProcessState process, out string reason), reason);
            Assert.AreEqual("furniture.table", process.MethodId,
                "Bed is first in catalog order but blocked on hay, so the table — the first runnable method — starts.");
            Assert.AreEqual(13f, shop.OnHand("lumber"), "Table consumes 2 lumber.");
            Assert.AreEqual(1f, shop.OnHand("nails_hardware_lot"), "Table consumes 1 nails lot.");
        }

        [Test]
        public void BeginDemandedFurniture_IdlesWhenNoHouseholdCanBuy()
        {
            var shop = NewEndowedWorkshop();
            shop.Stock("nails_hardware_lot", 2f, 25, "g1-demand-nails");
            shop.Stock("basic_cloth", 8f, 12, "g1-demand-cloth");

            Assert.IsFalse(shop.Production.TryBeginDemandedFurniture(shop.NewRuntime(), new[] { Carpenter },
                new List<CarpentryFurnitureSaleCandidate>(), DayIndex, out ProductionProcessState process, out string reason));
            Assert.IsNull(process);
            Assert.IsEmpty(reason, "No available household buyer is a normal no-production decision.");
            Assert.AreEqual(0, shop.Business.GenericConfiguration.ActiveProcesses.Count,
                "Demand gating must not create a work task or reserve inputs without a buyer.");
            Assert.AreEqual(15f, shop.OnHand("lumber"), "No-demand planning consumes no lumber.");
        }

        [Test]
        public void BeginDemandedFurniture_DoesNotTreatUnaffordableWantAsSaleDemand()
        {
            var shop = NewEndowedWorkshop();
            shop.Stock("nails_hardware_lot", 2f, 25, "g1-unaffordable-nails");
            var buyer = CandidateFor(1, FundedLedger(1, 1), new HouseholdFurnishingRegister { HouseholdId = 1 },
                WantFor("furniture.table", 1, true));

            Assert.IsFalse(shop.Production.TryBeginDemandedFurniture(shop.NewRuntime(), new[] { Carpenter },
                new[] { buyer }, DayIndex, out ProductionProcessState process, out string reason));
            Assert.IsNull(process);
            Assert.IsEmpty(reason);
            Assert.AreEqual(15f, shop.OnHand("lumber"), "An unaffordable want cannot consume material or create WIP.");
            Assert.AreEqual(2f, shop.OnHand("nails_hardware_lot"));
        }

        [Test]
        public void BeginDemandedFurniture_UsesAffordableHouseholdWantAndConfiguredRecipe()
        {
            var shop = NewEndowedWorkshop();
            shop.Stock("nails_hardware_lot", 2f, 25, "g1-demand-nails");
            shop.Stock("basic_cloth", 8f, 12, "g1-demand-cloth");
            var buyer = CandidateFor(1, FundedLedger(1, 100000), new HouseholdFurnishingRegister { HouseholdId = 1 },
                WantFor("furniture.table", 1, true));

            Assert.IsTrue(shop.Production.TryBeginDemandedFurniture(shop.NewRuntime(), new[] { Carpenter },
                new[] { buyer }, DayIndex, out ProductionProcessState process, out string reason), reason);
            Assert.AreEqual("furniture.table", process.MethodId,
                "The planner starts the policy-backed recipe matching a real buyer's affordable want.");
            Assert.AreEqual(13f, shop.OnHand("lumber"), "The shared production authority consumes real lumber once.");
            Assert.AreEqual(1f, shop.OnHand("nails_hardware_lot"), "The recipe consumes its actual nail input once.");
        }

        [Test]
        public void BeginEligible_FallsToNextMethodWhenAtTarget()
        {
            var shop = NewEndowedWorkshop();
            shop.Stock("nails_hardware_lot", 2f, 25, "g1-fixture-nails");
            shop.Register.RegisterAsset(MakeWorkshopAsset("furniture.table-carpentry-g1-test-workshop-prior-1", "furniture.table"));
            GenericProductionRuntime runtime = shop.NewRuntime();

            Assert.IsTrue(shop.Production.TryBeginEligibleFurniture(runtime, new[] { Carpenter }, DayIndex,
                out ProductionProcessState process, out string reason), reason);
            Assert.AreEqual("furniture.chair-batch", process.MethodId,
                "One table on hand is the target stock (1), so the table plan is ineligible and the chair batch starts.");
        }

        [Test]
        public void BeginEligible_CountsActiveFurnitureAsCommittedStock()
        {
            var shop = NewEndowedWorkshop();
            foreach (GenericProductionPolicy policy in shop.Business.GenericConfiguration.ProductionPolicies)
                if (policy != null) policy.Enabled = false;
            GenericProductionPolicy shelfPolicy = shop.Business.GenericConfiguration.ProductionPolicies
                .First(policy => policy != null && policy.MethodId == "furniture.shelf");
            shelfPolicy.Enabled = true;
            shop.Business.RegisterGenericProductionProcess(new ProductionProcessState
            {
                ProcessId = "g1-test-shelf-active-1",
                MethodId = "furniture.shelf",
                AssignedPersonId = Carpenter.ToString(),
                Completed = false,
            });

            Assert.IsFalse(shop.Production.TryBeginEligibleFurniture(shop.NewRuntime(),
                new[] { EntityId.For(EntityKind.Person, 912) }, DayIndex,
                out ProductionProcessState process, out string reason));
            Assert.IsNull(process, "The active one-shelf run already covers the one-piece target.");
            Assert.IsEmpty(reason, "Covered make-to-stock policy is normal idle time, not a production refusal.");
            Assert.AreEqual(1, shop.Business.GenericConfiguration.ActiveProcesses.Count(p => p != null && !p.Completed));
            Assert.AreEqual(15f, shop.OnHand("lumber"), "Planning must not consume another run's inputs.");
        }

        [Test]
        public void BeginEligible_RefusesWhenNothingEligible()
        {
            var shop = NewEndowedWorkshop();
            Assert.IsTrue(shop.Business.GenericConfiguration.TryConsumeInventory("lumber", 15f, out _),
                "Drain the opening lumber so no method has its inputs.");
            GenericProductionRuntime runtime = shop.NewRuntime();

            Assert.IsFalse(shop.Production.TryBeginEligibleFurniture(runtime, new[] { Carpenter }, DayIndex,
                out ProductionProcessState process, out string reason));
            Assert.IsNull(process);
            StringAssert.Contains("lumber", reason);
            StringAssert.Contains("deferred", reason);
        }

        [Test]
        public void BeginEligible_SilentlyIdlesWhenAllFurniturePoliciesAreDisabled()
        {
            var shop = NewEndowedWorkshop();
            foreach (GenericProductionPolicy policy in shop.Business.GenericConfiguration.ProductionPolicies)
            {
                if (policy != null) policy.Enabled = false;
            }
            GenericProductionRuntime runtime = shop.NewRuntime();

            Assert.IsFalse(shop.Production.TryBeginEligibleFurniture(runtime, new[] { Carpenter }, DayIndex,
                out ProductionProcessState process, out string reason));
            Assert.IsNull(process);
            Assert.IsEmpty(reason, "An intentionally disabled policy is an ordinary no-work day, not a refusal.");
        }

        // ---------- driver advance leg ----------

        [Test]
        public void Advance_CreatesEquipmentAssetNotInventory()
        {
            var shop = NewEndowedWorkshop();
            GenericProductionRuntime runtime = shop.NewRuntime();

            // Only lumber is stocked, so the shelf (lumber only) is the
            // first eligible method; one advance call works one phase,
            // so the two-phase shelf takes two daily advances.
            Assert.IsTrue(shop.Production.TryBeginEligibleFurniture(runtime, new[] { Carpenter }, DayIndex,
                out ProductionProcessState process, out string reason), reason);
            Assert.AreEqual("furniture.shelf", process.MethodId);
            for (int day = 0; day < 4 && !process.Completed; day++)
            {
                Assert.IsTrue(shop.Production.TryAdvanceFurnitureProcesses(runtime, 24 * 60, DayIndex, out reason), reason);
            }
            Assert.IsTrue(process.Completed, "The shelf completes within its two phases.");

            Assert.AreEqual(1, shop.Register.Assets.Count(asset => asset.Kind == "furniture.shelf"),
                "The shelf is a business-owned EquipmentAsset in the register.");
            EquipmentAsset shelf = shop.Register.Assets.First(asset => asset.Kind == "furniture.shelf");
            Assert.AreEqual("business", shelf.OwnerKind);
            Assert.AreEqual(WorkshopInstanceId, shelf.OwnerId);
            Assert.AreEqual(WorkshopInstanceId, shelf.MadeByBusinessId);
            Assert.AreEqual(DayIndex, shelf.MadeDayIndex);
            Assert.IsTrue(shelf.MaterialLotIds != null && shelf.MaterialLotIds.Count > 0, "The lumber lot provenance is named.");
            Assert.AreEqual(0f, shop.OnHand("furniture.shelf"),
                "No furniture lands in generic inventory — the driver never creates inventory furniture.");
            Assert.AreEqual(1f, shop.OnHand("fuel-wood"), "The shelf's offcut is real inventory.");
        }

        [Test]
        public void Advance_RefusesNonFurnitureProcess()
        {
            var shop = NewEndowedWorkshop();
            GenericProductionRuntime runtime = shop.NewRuntime();
            var foreign = new ProductionProcessState
            {
                ProcessId = "g1-test-foreign-1",
                MethodId = "bake-flour-bread",
                AssignedPersonId = Carpenter.ToString(),
            };
            shop.Business.RegisterGenericProductionProcess(foreign);

            Assert.IsFalse(shop.Production.TryAdvanceFurnitureProcesses(runtime, 24 * 60, DayIndex, out string reason));
            Assert.IsTrue(reason.Contains("bake-flour-bread"), reason);
            Assert.IsFalse(foreign.Completed, "The foreign process is not advanced by the furniture route.");
        }

        // ---------- lumber reorder policy ----------

        [Test]
        public void MaintainLumber_BuysWhenBelowTrigger()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(10000);
            Assert.IsTrue(shop.Business.GenericConfiguration.TryConsumeInventory("lumber", 10f, out _),
                "Drain the opening lumber from 15 to 5, below the trigger of 6.");
            var diag = new List<string>();
            LumberYardShopRuntime yard = NewYardWithStock(20, 32, diag);
            var yardCash = new FakeCashPort { BalanceCents = 20000 };
            var workshopCash = new RuntimeCashPort(shop.Business.RuntimeState);

            Assert.IsTrue(CarpentryLumberProcurement.TryMaintainLumberStock(
                yard, shop.Business, workshopCash, yardCash, DayIndex, diag,
                out CarpentryLumberReceipt receipt, out bool purchased, out string error), error);
            Assert.IsTrue(purchased);
            Assert.AreEqual(10, receipt.Units, "The reorder quantity is the proposed default (10).");
            Assert.AreEqual(40, receipt.UnitPriceCents, "32c landed cost with the yard's 25 percent markup.");
            Assert.AreEqual(400, receipt.TotalCents);
            Assert.AreEqual(9600, workshopCash.BalanceCents);
            Assert.AreEqual(20400, yardCash.BalanceCents);
            Assert.AreEqual(15f, shop.OnHand("lumber"), "5 on hand plus 10 bought.");
        }

        [Test]
        public void MaintainLumber_SkipsWhenAboveTrigger()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(10000);
            var diag = new List<string>();
            LumberYardShopRuntime yard = NewYardWithStock(20, 32, diag);
            var yardCash = new FakeCashPort { BalanceCents = 20000 };
            var workshopCash = new RuntimeCashPort(shop.Business.RuntimeState);

            Assert.IsTrue(CarpentryLumberProcurement.TryMaintainLumberStock(
                yard, shop.Business, workshopCash, yardCash, DayIndex, diag,
                out CarpentryLumberReceipt receipt, out bool purchased, out string error), error);
            Assert.IsFalse(purchased, "15 lumber is above the trigger of 6 — nothing is bought.");
            Assert.IsNull(receipt);
            Assert.AreEqual(10000, workshopCash.BalanceCents);
            Assert.AreEqual(20000, yardCash.BalanceCents);
            Assert.AreEqual(15f, shop.OnHand("lumber"));
        }

        // ---------- nails/cloth store purchase leg ----------

        [Test]
        public void SuppliesPurchase_MovesCashStockExactlyOnce()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(10000);
            shop.Business.RuntimeState.BindBusinessIdentity(WorkshopInstanceId);
            StoreSlice store = NewStoreWithSupplies(30, 40);
            var diag = new List<string>();
            var workshopCash = new Series2BusinessRuntimeCashPort(shop.Business.RuntimeState);
            var storeCash = new Series2BusinessRuntimeCashPort(store.StoreRuntime);

            var nailsInputs = new CarpentrySuppliesPurchaseInputs
            {
                StoreRuntime = store.StoreRuntime,
                Workshop = shop.Business,
                WorkshopCashPort = workshopCash,
                StoreCashPort = storeCash,
                WorkshopBusinessId = WorkshopInstanceId,
                StoreBusinessId = StoreId,
                ItemId = CarpentrySuppliesPurchase.NailsItemId,
                UnitPriceCents = 25,
                UnitsRequested = 1,
                DayIndex = DayIndex,
                TransactionId = CarpentrySuppliesPurchase.BuildTransactionId(
                    CarpentrySuppliesPurchase.NailsItemId, WorkshopInstanceId, DayIndex),
            };
            CarpentrySuppliesPurchaseResult nails =
                CarpentrySuppliesPurchase.PurchaseUnits(nailsInputs, diag);
            Assert.IsTrue(nails.Succeeded, "nails purchase should succeed; refusal: " + nails.Refusal);
            Assert.IsFalse(nails.Duplicate);
            Assert.AreEqual(1, nails.UnitsPurchased);
            Assert.AreEqual(25, nails.TotalCostCents);
            Assert.AreEqual(1, nails.SourceLotIds.Count);
            Assert.AreEqual("g1-test-store:nails", nails.SourceLotIds[0], "The source lot is named.");

            var clothInputs = new CarpentrySuppliesPurchaseInputs
            {
                StoreRuntime = store.StoreRuntime,
                Workshop = shop.Business,
                WorkshopCashPort = workshopCash,
                StoreCashPort = storeCash,
                WorkshopBusinessId = WorkshopInstanceId,
                StoreBusinessId = StoreId,
                ItemId = CarpentrySuppliesPurchase.ClothItemId,
                UnitPriceCents = 12,
                UnitsRequested = 4,
                DayIndex = DayIndex,
                TransactionId = CarpentrySuppliesPurchase.BuildTransactionId(
                    CarpentrySuppliesPurchase.ClothItemId, WorkshopInstanceId, DayIndex),
            };
            CarpentrySuppliesPurchaseResult cloth =
                CarpentrySuppliesPurchase.PurchaseUnits(clothInputs, diag);
            Assert.IsTrue(cloth.Succeeded, "cloth purchase should succeed; refusal: " + cloth.Refusal);
            Assert.AreEqual(4, cloth.UnitsPurchased);
            Assert.AreEqual(48, cloth.TotalCostCents);

            Assert.AreEqual(1f, shop.OnHand("nails_hardware_lot"));
            Assert.AreEqual(4f, shop.OnHand("basic_cloth"));
            Assert.AreEqual(10000 - 25 - 48, workshopCash.BalanceCents, "Workshop cash 10,000 -> 9,927c.");
            Assert.AreEqual(5000 + 25 + 48, storeCash.BalanceCents, "Store cash +73c, settlement recorded once per item.");
            Assert.AreEqual(29, store.StoreRuntime.GetRetailItemStockUnits("nails_hardware_lot", "tools_hardware"));
            Assert.AreEqual(36, store.StoreRuntime.GetRetailItemStockUnits("basic_cloth", "clothing"));

            CarpentrySuppliesPurchaseResult duplicate =
                CarpentrySuppliesPurchase.PurchaseUnits(nailsInputs, diag);
            Assert.IsTrue(duplicate.Succeeded);
            Assert.IsTrue(duplicate.Duplicate, "The same transaction id buys nothing twice.");
            Assert.AreEqual(0, duplicate.UnitsPurchased);
            Assert.AreEqual(1f, shop.OnHand("nails_hardware_lot"), "No double receipt.");
            Assert.AreEqual(10000 - 25 - 48, workshopCash.BalanceCents, "No double payment.");
        }

        [Test]
        public void SuppliesPurchase_ShortfallRefuses_NothingMoves()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(10000);
            shop.Business.RuntimeState.BindBusinessIdentity(WorkshopInstanceId);
            StoreSlice store = NewStoreWithSupplies(30, 40);
            var diag = new List<string>();
            var inputs = new CarpentrySuppliesPurchaseInputs
            {
                StoreRuntime = store.StoreRuntime,
                Workshop = shop.Business,
                WorkshopCashPort = new Series2BusinessRuntimeCashPort(shop.Business.RuntimeState),
                StoreCashPort = new Series2BusinessRuntimeCashPort(store.StoreRuntime),
                WorkshopBusinessId = WorkshopInstanceId,
                StoreBusinessId = StoreId,
                ItemId = CarpentrySuppliesPurchase.NailsItemId,
                UnitPriceCents = 25,
                UnitsRequested = 31,
                DayIndex = DayIndex,
                TransactionId = CarpentrySuppliesPurchase.BuildTransactionId(
                    CarpentrySuppliesPurchase.NailsItemId, WorkshopInstanceId, DayIndex),
            };

            CarpentrySuppliesPurchaseResult result =
                CarpentrySuppliesPurchase.PurchaseUnits(inputs, diag);
            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Refusal.Contains("shortfall") || result.Refusal.Contains("prove"), result.Refusal);
            Assert.AreEqual(10000, shop.Business.RuntimeState.CurrentCashCents, "Workshop cash unchanged.");
            Assert.AreEqual(5000, store.StoreRuntime.CurrentCashCents, "Store cash unchanged.");
            Assert.AreEqual(0f, shop.OnHand("nails_hardware_lot"), "Nothing received.");
            Assert.AreEqual(30, store.StoreRuntime.GetRetailItemStockUnits("nails_hardware_lot", "tools_hardware"));
        }

        [Test]
        public void SuppliesMaintain_BuysShortfallToTarget()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(10000);
            shop.Business.RuntimeState.BindBusinessIdentity(WorkshopInstanceId);
            shop.Stock("basic_cloth", 2f, 12, "g1-fixture-cloth");
            StoreSlice store = NewStoreWithSupplies(30, 40);
            var diag = new List<string>();
            var workshopCash = new Series2BusinessRuntimeCashPort(shop.Business.RuntimeState);
            var storeCash = new Series2BusinessRuntimeCashPort(store.StoreRuntime);

            Assert.IsTrue(CarpentrySuppliesPurchase.TryMaintainSupplies(
                store.StoreRuntime, shop.Business, workshopCash, storeCash, StoreId,
                itemId => string.Equals(itemId, CarpentrySuppliesPurchase.NailsItemId, StringComparison.Ordinal) ? 25 : 12,
                DayIndex, diag, out string error), error);
            Assert.AreEqual(2f, shop.OnHand("nails_hardware_lot"), "Nails bought to the target of 2 lots.");
            Assert.AreEqual(8f, shop.OnHand("basic_cloth"), "Cloth bought to the target of 8 yards (2 on hand, 6 bought).");
            Assert.AreEqual(10000 - (2 * 25 + 6 * 12), workshopCash.BalanceCents, "Workshop cash 10,000 -> 9,878c.");
            Assert.AreEqual(5000 + (2 * 25 + 6 * 12), storeCash.BalanceCents);
        }

        [Test]
        public void SuppliesMaintain_BuysAvailablePartialRetailStockWithoutInventingRemainder()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(10000);
            shop.Business.RuntimeState.BindBusinessIdentity(WorkshopInstanceId);
            StoreSlice store = NewStoreWithSupplies(2, 3);
            var diag = new List<string>();
            var workshopCash = new Series2BusinessRuntimeCashPort(shop.Business.RuntimeState);
            var storeCash = new Series2BusinessRuntimeCashPort(store.StoreRuntime);

            Assert.IsTrue(CarpentrySuppliesPurchase.TryMaintainSupplies(
                store.StoreRuntime, shop.Business, workshopCash, storeCash, StoreId,
                itemId => string.Equals(itemId, CarpentrySuppliesPurchase.NailsItemId, StringComparison.Ordinal) ? 25 : 12,
                DayIndex, diag, out string error), error);

            Assert.AreEqual(2f, shop.OnHand("nails_hardware_lot"));
            Assert.AreEqual(3f, shop.OnHand("basic_cloth"), "Only the store's three identified cloth yards are received.");
            Assert.AreEqual(0, store.StoreRuntime.GetRetailItemStockUnits("nails_hardware_lot", "tools_hardware"));
            Assert.AreEqual(0, store.StoreRuntime.GetRetailItemStockUnits("basic_cloth", "clothing"));
            Assert.AreEqual(10000 - (2 * 25 + 3 * 12), workshopCash.BalanceCents);
            Assert.AreEqual(5000 + (2 * 25 + 3 * 12), storeCash.BalanceCents);
            Assert.IsTrue(diag.Any(line => line.Contains("remaining need is 5 unit(s)")
                && line.Contains("no incoming stock assumed")), "The unmet target remains observable and no stock is implied.");
        }

        [Test]
        public void SuppliesMaintain_BuysOnlyCashAffordableQuantity()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(25);
            shop.Business.RuntimeState.BindBusinessIdentity(WorkshopInstanceId);
            shop.Stock("basic_cloth", CarpentrySuppliesPurchase.ClothTargetYards, 12, "g1-fixture-cloth-at-target");
            StoreSlice store = NewStoreWithSupplies(2, 0);
            var diag = new List<string>();
            var workshopCash = new Series2BusinessRuntimeCashPort(shop.Business.RuntimeState);
            var storeCash = new Series2BusinessRuntimeCashPort(store.StoreRuntime);

            Assert.IsTrue(CarpentrySuppliesPurchase.TryMaintainSupplies(
                store.StoreRuntime, shop.Business, workshopCash, storeCash, StoreId,
                itemId => string.Equals(itemId, CarpentrySuppliesPurchase.NailsItemId, StringComparison.Ordinal) ? 25 : 12,
                DayIndex, diag, out string error), error);

            Assert.AreEqual(1f, shop.OnHand("nails_hardware_lot"), "The workshop can afford one of the two requested lots.");
            Assert.AreEqual(1, store.StoreRuntime.GetRetailItemStockUnits("nails_hardware_lot", "tools_hardware"));
            Assert.AreEqual(0, workshopCash.BalanceCents);
            Assert.AreEqual(5000 + 25, storeCash.BalanceCents);
            Assert.IsTrue(diag.Any(line => line.Contains("affordable at 25c/unit=1")
                && line.Contains("remaining need is 1 unit(s)")), "The unpaid remainder remains explicit.");
        }

        // ---------- workshop-to-household sale ----------

        [Test]
        public void FurnitureSale_SellsToWantingHousehold()
        {
            var shop = NewEndowedWorkshop();
            EquipmentAsset bed = MakeWorkshopAsset("furn-bed-1", "furniture.bed");
            shop.Register.RegisterAsset(bed);
            var workshopCash = new FakeCashPort { BalanceCents = 10000 };
            HouseholdLedger ledger = FundedLedger(1, 500);
            var furnishings = new HouseholdFurnishingRegister { HouseholdId = 1 };
            var candidates = new List<CarpentryFurnitureSaleCandidate>
            {
                CandidateFor(1, ledger, furnishings, WantFor("furniture.bed", 1, true)),
            };
            var diag = new List<string>();

            Assert.IsTrue(CarpentryFurnitureSale.TrySellFinishedPieces(
                shop.Business, shop.Register, workshopCash, candidates, DayIndex, diag, out string error), error);
            Assert.AreEqual(200, ledger.GetBalanceCents(), "Buyer paid the 300c retail baseline.");
            Assert.AreEqual(10300, workshopCash.BalanceCents, "Workshop credited 300c.");
            Assert.AreEqual("household", bed.OwnerKind);
            Assert.AreEqual("1", bed.OwnerId);
            Assert.AreEqual("household:1", bed.LocationId);
            Assert.IsTrue(bed.OwnershipHistory.Count > 0, "The transfer is recorded on the asset.");
            Assert.IsNull(shop.Register.Assets.FirstOrDefault(a => a.AssetId == "furn-bed-1"),
                "The sold piece leaves the workshop register, so the cap stops counting it.");
            Assert.IsNotNull(furnishings.FindAsset("furn-bed-1"),
                "The sold piece is registered to the buying household, where it can be installed.");
        }

        [Test]
        public void FurnitureSale_RefusesWhenBuyerCannotPay()
        {
            var shop = NewEndowedWorkshop();
            EquipmentAsset bed = MakeWorkshopAsset("furn-bed-1", "furniture.bed");
            shop.Register.RegisterAsset(bed);
            var workshopCash = new FakeCashPort { BalanceCents = 10000 };
            HouseholdLedger ledger = FundedLedger(1, 100);
            var furnishings = new HouseholdFurnishingRegister { HouseholdId = 1 };
            var candidates = new List<CarpentryFurnitureSaleCandidate>
            {
                CandidateFor(1, ledger, furnishings, WantFor("furniture.bed", 1, true)),
            };
            var diag = new List<string>();

            Assert.IsTrue(CarpentryFurnitureSale.TrySellFinishedPieces(
                shop.Business, shop.Register, workshopCash, candidates, DayIndex, diag, out string error), error);
            Assert.AreEqual(100, ledger.GetBalanceCents(), "Nothing moved.");
            Assert.AreEqual(10000, workshopCash.BalanceCents);
            Assert.AreEqual("business", bed.OwnerKind, "The piece stays workshop-owned.");
            Assert.IsNotNull(shop.Register.Assets.FirstOrDefault(a => a.AssetId == "furn-bed-1"));
            Assert.IsNull(furnishings.FindAsset("furn-bed-1"));
        }

        [Test]
        public void FurnitureSale_OnePiecePerHouseholdPerDay()
        {
            var shop = NewEndowedWorkshop();
            shop.Register.RegisterAsset(MakeWorkshopAsset("furn-bed-1", "furniture.bed"));
            shop.Register.RegisterAsset(MakeWorkshopAsset("furn-bed-2", "furniture.bed"));
            var workshopCash = new FakeCashPort { BalanceCents = 10000 };
            HouseholdLedger ledger = FundedLedger(1, 1000);
            var furnishings = new HouseholdFurnishingRegister { HouseholdId = 1 };
            var candidates = new List<CarpentryFurnitureSaleCandidate>
            {
                CandidateFor(1, ledger, furnishings, WantFor("furniture.bed", 1, true)),
            };
            var diag = new List<string>();

            Assert.IsTrue(CarpentryFurnitureSale.TrySellFinishedPieces(
                shop.Business, shop.Register, workshopCash, candidates, DayIndex, diag, out string error), error);
            Assert.AreEqual(700, ledger.GetBalanceCents(), "One bed sold for 300c; the second stays.");
            Assert.AreEqual(10300, workshopCash.BalanceCents);
            Assert.AreEqual(1, furnishings.Assets.Count, "Exactly one piece changed hands.");
            Assert.AreEqual(1, shop.Register.Assets.Count(a => a.Kind == "furniture.bed"),
                "Exactly one bed remains in workshop stock.");
        }

        [Test]
        public void FurnitureSale_SkipsHouseholdWithNoWantForTheKind()
        {
            var shop = NewEndowedWorkshop();
            shop.Register.RegisterAsset(MakeWorkshopAsset("furn-table-1", "furniture.table"));
            var workshopCash = new FakeCashPort { BalanceCents = 10000 };
            HouseholdLedger ledger = FundedLedger(1, 1000);
            var furnishings = new HouseholdFurnishingRegister { HouseholdId = 1 };
            var candidates = new List<CarpentryFurnitureSaleCandidate>
            {
                CandidateFor(1, ledger, furnishings, WantFor("furniture.bed", 1, true)),
            };
            var diag = new List<string>();

            Assert.IsTrue(CarpentryFurnitureSale.TrySellFinishedPieces(
                shop.Business, shop.Register, workshopCash, candidates, DayIndex, diag, out string error), error);
            Assert.AreEqual(1000, ledger.GetBalanceCents(), "No want for a table, no sale — no want is invented.");
            Assert.AreEqual(10000, workshopCash.BalanceCents);
            Assert.IsNotNull(shop.Register.Assets.FirstOrDefault(a => a.AssetId == "furn-table-1"));
        }

        [Test]
        public void FurnitureSale_DoesNotRepurchaseKindWhileUsablePieceRemainsInHouseholdCustody()
        {
            var shop = NewEndowedWorkshop();
            shop.Register.RegisterAsset(MakeWorkshopAsset("furn-table-new", "furniture.table"));
            var workshopCash = new FakeCashPort { BalanceCents = 10000 };
            HouseholdLedger ledger = FundedLedger(1, 1000);
            var furnishings = new HouseholdFurnishingRegister { HouseholdId = 1 };
            var pendingTable = new EquipmentAsset
            {
                AssetId = "furn-table-already-owned",
                Kind = "furniture.table",
                Condition01 = 1f,
                OwnerKind = HouseholdFurnishingRegister.OwnerKindHousehold,
                OwnerId = HouseholdFurnishingRegister.OwnerIdForHousehold(1),
                LocationId = HouseholdFurnishingRegister.HouseholdCustodyLocationId(1),
            };
            Assert.IsNull(furnishings.TryRegisterAsset(pendingTable));
            var candidates = new List<CarpentryFurnitureSaleCandidate>
            {
                CandidateFor(1, ledger, furnishings, WantFor("furniture.table", 3, true)),
            };
            var diag = new List<string>();

            Assert.IsTrue(CarpentryFurnitureSale.TrySellFinishedPieces(
                shop.Business, shop.Register, workshopCash, candidates, DayIndex, diag, out string error), error);

            Assert.AreEqual(1000, ledger.GetBalanceCents(), "The household must not pay again while its first table is awaiting installation.");
            Assert.AreEqual(10000, workshopCash.BalanceCents, "No duplicate sale proceeds are posted.");
            Assert.AreEqual("household:1", pendingTable.LocationId, "The existing piece remains in household custody.");
            Assert.IsNotNull(shop.Register.Assets.FirstOrDefault(a => a.AssetId == "furn-table-new"), "The unsold table remains workshop-owned.");
            Assert.IsNull(furnishings.FindAsset("furn-table-new"), "No duplicate household asset is created.");
        }

        // ---------- business register deregistration ----------

        [Test]
        public void BusinessRegister_TryDeregisterAsset()
        {
            var register = new BusinessEquipmentRegister { BusinessInstanceId = WorkshopInstanceId };
            EquipmentAsset asset = MakeWorkshopAsset("furn-bed-1", "furniture.bed");
            register.RegisterAsset(asset);

            Assert.IsNull(register.TryDeregisterAsset("furn-bed-1"));
            Assert.AreEqual(0, register.Assets.Count);
            Assert.IsNotNull(register.TryDeregisterAsset("furn-bed-1"),
                "Deregistering an asset that is not registered refuses loudly.");
            Assert.IsNotNull(register.TryDeregisterAsset(null));
        }
    }
}
