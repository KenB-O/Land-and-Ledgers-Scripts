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
    /// F1 part 2 (expanded campaign 2026-11-16, O-F3): the producing
    /// carpentry business — the carpentry setup template, the opening
    /// endowment (tools, workstation, opening lumber), the lumberyard
    /// lumber procurement, and the furniture production binding. Pins
    /// that the workshop exists with real premises, owner, worker,
    /// tools, workstation, cash and lumber procurement; that the
    /// carpenter makes furniture from lumberyard lumber through the F1
    /// part 1 recipes; and that the opening endowment is explicit and
    /// auditable. Unity compilation, Test Runner, PlayMode, and scene
    /// wiring are NOT claimed; the bootstrapper and hub call sites are
    /// harness-excluded, so they are source-reviewed only and exercised
    /// here through the same public APIs they call.
    /// </summary>
    [TestFixture]
    public sealed class CarpentryWorkshopF1Part2Tests
    {
        private const string WorkshopKey = "carpentry-workshop";
        private const string WorkshopInstanceId = "f1p2-test-workshop";
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
            config.Settings.configurationId = "f1p2-test-town";
            config.Settings.seedOffset = 0;
            config.Settings.freightBusinessKey = "freight-company";
            SetPrivateField(config, "people", new[]
            {
                new DevelopmentTownConfiguration.PersonDefinition
                { key = "don-millward", firstName = "Don", lastName = "Millward", age = 58, homeBuildingOrdinal = 5, occupation = "Carpenter" },
                new DevelopmentTownConfiguration.PersonDefinition
                { key = "grace-millward", firstName = "Grace", lastName = "Millward", age = 56, isFemale = true, homeBuildingOrdinal = 5 },
            });
            SetPrivateField(config, "households", new[]
            {
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "millward", surname = "Millward", displayName = "Millward Household",
                    memberPersonKeys = new[] { "don-millward", "grace-millward" }, homeBuildingOrdinal = 5, openingCashCents = 0,
                },
            });
            SetPrivateField(config, "relationships", new[]
            {
                new DevelopmentTownConfiguration.RelationshipDefinition
                { firstPersonKey = "don-millward", secondPersonKey = "grace-millward", relationship = 0 },
            });
            // Schema 2: no matched entry means the workshop's opening
            // carries a null equipment record, so the derived view omits
            // it and the endowment reports "No opening equipment is
            // configured" and installs nothing (not even the lumber).
            // An empty (non-null) record would instead be a
            // present-but-empty entry that the workstation gate refuses.
            DevelopmentTownConfiguration.BusinessOpeningEquipment workshopEquipment = null;
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
                    businessId = "f1p2-test-freight", premisesRequired = false,
                    opening = new DevelopmentTownConfiguration.BusinessOpeningConfiguration
                    {
                        openingHorseId = "A0",
                        openingWagonId = "f1p2-test-wagon",
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
            var yard = new LumberYardShopRuntime("f1p2-test-yard", new EntityIdRegistry());
            Assert.IsNull(yard.ReceiveLumberPurchase(
                units, "eastern white pine", "merchantable", "f1p2-test-sawmill", "f1p2 fixture purchase", 0, unitCostCents, diag));
            return yard;
        }

        private static Workshop NewEndowedWorkshop()
        {
            var shop = new Workshop();
            shop.ApplyTemplate();
            shop.Endow(BuildConfig(new[] { CarpentryEquipmentEntry() }, 15));
            return shop;
        }

        // ---------- carpentry setup template ----------

        [Test]
        public void Template_CarpentrySetup_InstallsEightMethodsAndPoliciesAndNothingElse()
        {
            var shop = new Workshop();
            shop.ApplyTemplate();

            Assert.AreEqual(GenericBusinessSetupTemplateCatalog.CarpentryId, shop.Business.GenericConfiguration.SetupTemplateId);
            Assert.AreEqual(8, shop.Business.GenericConfiguration.ProductionMethods.Count);
            foreach (ProductionMethodDefinition method in GenericVerticalCatalog.Furniture())
            {
                Assert.IsTrue(shop.Business.GenericConfiguration.TryGetProductionPolicy(method.MethodId, out GenericProductionPolicy policy),
                    $"No policy for {method.MethodId}.");
                Assert.AreEqual(GenericProductionPolicyMode.MakeToStock, policy.Mode);
                Assert.AreEqual(1f, policy.TargetStock);
                Assert.AreEqual(2f, policy.MaximumStock);
            }
            Assert.AreEqual(0, shop.Business.GenericConfiguration.Retail.ProductLines.Count,
                "The template installs methods and policies only — furniture retail lines are F2.");
        }

        [Test]
        public void Template_CarpentrySetup_RefusesNonGenericBusiness()
        {
            BusinessProfileDefinition profile =
                BusinessProfileDefinition.CreateFallback(BusinessType.Bakery, "F1P2 Test Bakery");
            BusinessInstanceState bakery = BusinessInstanceState.Create(
                "f1p2-test-bakery", profile, -1, BusinessOwnerIdentity.Npc(912, "Test", "Baker"));
            Assert.IsFalse(
                GenericBusinessSetupTemplateCatalog.TryApply(bakery, GenericBusinessSetupTemplateCatalog.CarpentryId, out string _));
        }

        [Test]
        public void Template_Catalog_CarpentryEntry_NamesEightMethodsAndTools()
        {
            GenericBusinessSetupTemplate template =
                GenericBusinessSetupTemplateCatalog.Find(GenericBusinessSetupTemplateCatalog.CarpentryId);
            Assert.IsNotNull(template);
            Assert.AreEqual(8, template.SuggestedProductionMethodIds.Count);
            Assert.IsTrue(template.SuggestedProductionMethodIds.Contains("furniture.table"));
            Assert.IsTrue(template.SuggestedProductionMethodIds.Contains("furniture.chair-batch"));
            Assert.IsTrue(template.SuggestedEquipmentKinds.Contains("workbench"));
            Assert.IsTrue(template.SuggestedEquipmentKinds.Contains("carpenter-hand-tool-kit"));
            Assert.IsTrue(template.SuggestedProductIds.Contains("furniture.table"));
        }

        // ---------- opening endowment ----------

        [Test]
        public void Endowment_AppliesToolsWorkstationAndLumber()
        {
            var shop = new Workshop();
            DevelopmentTownConfiguration config = BuildConfig(new[] { CarpentryEquipmentEntry() }, 15);
            var diag = new List<string>();
            Assert.IsTrue(
                ScenarioCarpentryOpeningEndowment.TryApply(
                    config, WorkshopKey, shop.Business, shop.Register, shop.Workstations, DayIndex, diag,
                    out string report, out string error),
                error);
            Assert.IsTrue(report.Contains("carpenter-workbench-1"), report);
            Assert.IsTrue(report.Contains("carpenter-hand-tool-kit"), report);
            Assert.IsTrue(report.Contains("15 lumber"), report);

            Assert.AreEqual(1, shop.Register.Assets.Count);
            EquipmentAsset workbench = shop.Register.Assets[0];
            Assert.AreEqual("carpenter-workbench-1", workbench.AssetId);
            Assert.AreEqual("workbench", workbench.Kind);
            Assert.AreEqual(1f, workbench.Condition01);
            Assert.AreEqual("business", workbench.OwnerKind);
            Assert.AreEqual(WorkshopInstanceId, workbench.OwnerId);
            Assert.AreEqual(WorkshopInstanceId, workbench.LocationId);
            Assert.IsTrue(workbench.IsUsable);

            Assert.AreEqual(1, shop.Register.Kits.Count);
            ToolKitInstance kit = shop.Register.Kits[0];
            Assert.AreEqual("carpenter-hand-tool-kit", kit.KitId);
            Assert.AreEqual("business", kit.OwnerKind);
            Assert.AreEqual(WorkshopInstanceId, kit.OwnerId);
            Assert.IsTrue(kit.IsUsable);

            Assert.IsTrue(shop.Business.GenericConfiguration.IsEquipmentAvailable("workbench"));
            Assert.IsTrue(shop.Business.GenericConfiguration.IsEquipmentAvailable("carpenter-hand-tool-kit"));
            Assert.IsTrue(shop.Business.GenericConfiguration.IsWorkspaceAvailable("carpenter-workshop"));

            WorkstationInstance station = shop.Workstations.Get("carpenter-workshop");
            Assert.IsNotNull(station, "The workstation is established and registered.");
            Assert.AreEqual("carpenter-workshop", station.SpaceId);
            Assert.IsNull(
                shop.Workstations.CheckReady("carpenter-workshop", WorkstationCatalog.CarpenterWorkstation,
                    id => ScenarioCarpentryOpeningEndowment.FindRegisterComponent(shop.Register, id), diag),
                "The installed components satisfy the carpenter workstation.");

            Assert.AreEqual(15f, shop.OnHand("lumber"));
            Assert.AreEqual(0, shop.Business.GenericConfiguration.GetInventoryAverageUnitCostCents("lumber"),
                "Opening lumber is an endowment, not a purchase — it carries no purchase price.");
        }

        [Test]
        public void Endowment_IsIdempotent()
        {
            var shop = new Workshop();
            DevelopmentTownConfiguration config = BuildConfig(new[] { CarpentryEquipmentEntry() }, 15);
            shop.Endow(config);
            shop.Endow(config);

            Assert.AreEqual(1, shop.Register.Assets.Count, "A second application installs no second workbench.");
            Assert.AreEqual(1, shop.Register.Kits.Count, "A second application installs no second kit.");
            Assert.AreEqual(15f, shop.OnHand("lumber"), "A second application receives no second lumber.");
            Assert.IsNotNull(shop.Workstations.Get("carpenter-workshop"));
        }

        [Test]
        public void Endowment_NoEntry_InstallsNothing()
        {
            var shop = new Workshop();
            DevelopmentTownConfiguration config = BuildConfig(Array.Empty<DevelopmentTownConfiguration.BusinessOpeningEquipment>(), 15);
            var diag = new List<string>();
            Assert.IsTrue(
                ScenarioCarpentryOpeningEndowment.TryApply(
                    config, WorkshopKey, shop.Business, shop.Register, shop.Workstations, DayIndex, diag,
                    out string report, out string error),
                error);
            Assert.IsTrue(report.Contains("No opening equipment"), report);
            Assert.AreEqual(0, shop.Register.Assets.Count);
            Assert.AreEqual(0, shop.Register.Kits.Count);
            Assert.IsNull(shop.Workstations.Get("carpenter-workshop"));
            Assert.AreEqual(0f, shop.OnHand("lumber"),
                "No equipment entry means no endowment at all — not even the lumber.");
        }

        [Test]
        public void Endowment_UnknownKit_RefusesAndInstallsNothing()
        {
            var shop = new Workshop();
            var entry = new DevelopmentTownConfiguration.BusinessOpeningEquipment
            {
                businessKey = WorkshopKey,
                assetEntries = new[]
                {
                    new DevelopmentTownConfiguration.OpeningAssetEntry { assetId = "carpenter-workbench-1", kind = "workbench" },
                },
                kitIds = new[] { "not-a-real-kit" },
            };
            DevelopmentTownConfiguration config = BuildConfig(new[] { entry }, 15);
            var diag = new List<string>();
            Assert.IsFalse(
                ScenarioCarpentryOpeningEndowment.TryApply(
                    config, WorkshopKey, shop.Business, shop.Register, shop.Workstations, DayIndex, diag,
                    out string _, out string error));
            Assert.IsTrue(error.Contains("not-a-real-kit"), error);
            Assert.AreEqual(0, shop.Register.Assets.Count, "A refusal installs nothing.");
            Assert.AreEqual(0, shop.Register.Kits.Count);
            Assert.AreEqual(0f, shop.OnHand("lumber"));
        }

        [Test]
        public void Endowment_DuplicateAsset_RefusesAndInstallsNothing()
        {
            var shop = new Workshop();
            var entry = new DevelopmentTownConfiguration.BusinessOpeningEquipment
            {
                businessKey = WorkshopKey,
                assetEntries = new[]
                {
                    new DevelopmentTownConfiguration.OpeningAssetEntry { assetId = "carpenter-workbench-1", kind = "workbench" },
                    new DevelopmentTownConfiguration.OpeningAssetEntry { assetId = "carpenter-workbench-1", kind = "workbench" },
                },
                kitIds = new[] { "carpenter-hand-tool-kit" },
            };
            DevelopmentTownConfiguration config = BuildConfig(new[] { entry }, 15);
            var diag = new List<string>();
            Assert.IsFalse(
                ScenarioCarpentryOpeningEndowment.TryApply(
                    config, WorkshopKey, shop.Business, shop.Register, shop.Workstations, DayIndex, diag,
                    out string _, out string error));
            Assert.IsTrue(error.Contains("twice"), error);
            Assert.AreEqual(0, shop.Register.Assets.Count);
            Assert.AreEqual(0f, shop.OnHand("lumber"));
        }

        [Test]
        public void Endowment_WrongRegister_RefusesAndInstallsNothing()
        {
            var shop = new Workshop();
            DevelopmentTownConfiguration config = BuildConfig(new[] { CarpentryEquipmentEntry() }, 15);
            var otherRegister = new BusinessEquipmentRegister { BusinessInstanceId = "some-other-business" };
            var diag = new List<string>();
            Assert.IsFalse(
                ScenarioCarpentryOpeningEndowment.TryApply(
                    config, WorkshopKey, shop.Business, otherRegister, shop.Workstations, DayIndex, diag,
                    out string _, out string error));
            Assert.IsTrue(error.Contains("some-other-business"), error);
            Assert.AreEqual(0, otherRegister.Assets.Count);
            Assert.AreEqual(0f, shop.OnHand("lumber"));
        }

        // ---------- lumber procurement ----------

        [Test]
        public void Procurement_BuysLumberAtQuotedPriceAndSettlesBothSides()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(10000);
            var diag = new List<string>();
            LumberYardShopRuntime yard = NewYardWithStock(20, 32, diag);
            var yardCash = new FakeCashPort { BalanceCents = 20000 };
            var workshopCash = new RuntimeCashPort(shop.Business.RuntimeState);

            Assert.IsTrue(
                CarpentryLumberProcurement.TryBuyLumber(
                    yard, shop.Business, workshopCash, yardCash, 4, DayIndex, diag,
                    out CarpentryLumberReceipt receipt, out string error),
                error);
            Assert.AreEqual(4, receipt.Units);
            Assert.AreEqual(40, receipt.UnitPriceCents, "32c landed cost with the yard's 25 percent markup.");
            Assert.AreEqual(160, receipt.TotalCents);
            Assert.AreEqual(1, receipt.LotIds.Count, "All four units came from the one stocked yard lot.");
            Assert.AreEqual(9840, workshopCash.BalanceCents);
            Assert.AreEqual(20160, yardCash.BalanceCents);
            Assert.AreEqual(16, yard.LumberStock.AvailableUnitsForSale(null, null));
            Assert.AreEqual(19f, shop.OnHand("lumber"), "15 opening units plus 4 bought.");
        }

        [Test]
        public void Procurement_EmptyYard_RefusesWithoutMovingMoney()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(10000);
            var diag = new List<string>();
            var yard = new LumberYardShopRuntime("f1p2-test-yard", new EntityIdRegistry());
            var yardCash = new FakeCashPort { BalanceCents = 20000 };
            var workshopCash = new RuntimeCashPort(shop.Business.RuntimeState);

            Assert.IsFalse(
                CarpentryLumberProcurement.TryBuyLumber(
                    yard, shop.Business, workshopCash, yardCash, 4, DayIndex, diag,
                    out CarpentryLumberReceipt receipt, out string error));
            Assert.IsNull(receipt);
            Assert.IsTrue(error.Contains("no saleable lumber"), error);
            Assert.AreEqual(10000, workshopCash.BalanceCents);
            Assert.AreEqual(20000, yardCash.BalanceCents);
            Assert.AreEqual(15f, shop.OnHand("lumber"));
        }

        [Test]
        public void Procurement_InsufficientCash_RefusesWithoutMovingStock()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(100);
            var diag = new List<string>();
            LumberYardShopRuntime yard = NewYardWithStock(20, 32, diag);
            var yardCash = new FakeCashPort { BalanceCents = 20000 };
            var workshopCash = new RuntimeCashPort(shop.Business.RuntimeState);

            Assert.IsFalse(
                CarpentryLumberProcurement.TryBuyLumber(
                    yard, shop.Business, workshopCash, yardCash, 4, DayIndex, diag,
                    out CarpentryLumberReceipt _, out string error));
            Assert.IsTrue(error.Contains("100c"), error);
            Assert.AreEqual(100, workshopCash.BalanceCents);
            Assert.AreEqual(20, yard.LumberStock.AvailableUnitsForSale(null, null));
            Assert.AreEqual(15f, shop.OnHand("lumber"));
        }

        [Test]
        public void Procurement_ShortfallSale_RefundsBuyerInFull()
        {
            var shop = NewEndowedWorkshop();
            shop.Business.RuntimeState.AddCashCents(10000);
            var diag = new List<string>();
            LumberYardShopRuntime yard = NewYardWithStock(20, 32, diag);
            var yardCash = new FakeCashPort { BalanceCents = 20000 };
            var workshopCash = new RuntimeCashPort(shop.Business.RuntimeState);

            Assert.IsFalse(
                CarpentryLumberProcurement.TryBuyLumber(
                    yard, shop.Business, workshopCash, yardCash, 25, DayIndex, diag,
                    out CarpentryLumberReceipt receipt, out string error));
            Assert.IsNull(receipt);
            Assert.IsTrue(error.Contains("refunded"), error);
            Assert.AreEqual(10000, workshopCash.BalanceCents, "The debit is refunded in full when the sale refuses.");
            Assert.AreEqual(20000, yardCash.BalanceCents);
            Assert.AreEqual(20, yard.LumberStock.AvailableUnitsForSale(null, null));
            Assert.AreEqual(15f, shop.OnHand("lumber"));
        }

        // ---------- furniture production ----------

        [Test]
        public void Production_Table_ConsumesOnceAndCreatesOneAssetWithProvenance()
        {
            var shop = NewEndowedWorkshop();
            shop.Stock("nails_hardware_lot", 1f, 25, "f1p2-fixture-nails");
            GenericProductionRuntime runtime = shop.NewRuntime();
            string lumberLotId = shop.Business.GenericConfiguration.GetInventoryQuantity("lumber") > 0f
                ? FirstLotId(shop, "lumber") : string.Empty;
            string nailsLotId = FirstLotId(shop, "nails_hardware_lot");

            Assert.IsTrue(shop.Production.TryBeginFurniture(runtime, "furniture.table", Carpenter, DayIndex,
                out ProductionProcessState process, out string reason), reason);
            Assert.IsNotNull(process);
            Assert.AreEqual("furniture.table", process.MethodId);
            Assert.AreEqual(13f, shop.OnHand("lumber"), "Inputs are consumed exactly once at start.");
            Assert.AreEqual(0f, shop.OnHand("nails_hardware_lot"));
            Assert.AreEqual(0, shop.Register.Assets.Count(asset => asset.Kind == "furniture.table"), "No output before the work is done.");
            Assert.AreEqual(0, process.CurrentPhaseIndex);
            Assert.AreEqual(45, process.RemainingPhaseMinutes);

            Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 45, DayIndex, out reason), reason);
            Assert.AreEqual(1, process.CurrentPhaseIndex, "Cut done; assemble begins.");
            Assert.AreEqual(120, process.RemainingPhaseMinutes);
            Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 120, DayIndex, out reason), reason);
            Assert.AreEqual(2, process.CurrentPhaseIndex, "Assemble done; finish begins.");
            Assert.AreEqual(45, process.RemainingPhaseMinutes);
            Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 45, DayIndex, out reason), reason);
            Assert.AreEqual(3, process.CurrentPhaseIndex, "Finish done; cure begins.");
            Assert.AreEqual(120, process.RemainingPhaseMinutes);
            Assert.IsFalse(process.PersonRequiredNow(FurnitureRecipePresets.Table().ToMethodDefinition()),
                "Cure is passive — the carpenter is not held by it.");
            Assert.AreEqual(0, shop.Register.Assets.Count(asset => asset.Kind == "furniture.table"), "Still no output before the cure finishes.");

            Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 120, DayIndex, out reason), reason);
            Assert.IsTrue(process.Completed);
            Assert.IsTrue(process.OutputCreated);
            Assert.AreEqual(1, shop.Register.Assets.Count(asset => asset.Kind == "furniture.table"));

            EquipmentAsset table = shop.Register.Assets.Single(asset => asset.Kind == "furniture.table");
            Assert.AreEqual("business", table.OwnerKind);
            Assert.AreEqual(WorkshopInstanceId, table.OwnerId);
            Assert.AreEqual(WorkshopInstanceId, table.LocationId,
                "Manufacture is not furnishing: the table sits with the business, in no building.");
            Assert.AreEqual(WorkshopInstanceId, table.MadeByBusinessId);
            Assert.AreEqual(DayIndex, table.MadeDayIndex);
            Assert.AreEqual(2, table.MaterialLotIds.Count, "Provenance carries the lumber lot and the nails lot.");
            Assert.IsTrue(table.MaterialLotIds.Contains(lumberLotId), "The lumber lot the table was cut from is named.");
            Assert.IsTrue(table.MaterialLotIds.Contains(nailsLotId), "The nails lot the table was joined with is named.");
            Assert.IsTrue(table.IsUsable);

            Assert.IsFalse(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 60, DayIndex, out _),
                "A completed process advances no further.");
        }

        [Test]
        public void Production_ChairBatch_CreatesFourChairsAndFuelWoodOffcuts()
        {
            var shop = NewEndowedWorkshop();
            shop.Stock("nails_hardware_lot", 1f, 25, "f1p2-fixture-nails");
            GenericProductionRuntime runtime = shop.NewRuntime();

            Assert.IsTrue(shop.Production.TryBeginFurniture(runtime, "furniture.chair-batch", Carpenter, DayIndex,
                out ProductionProcessState process, out string reason), reason);
            Assert.AreEqual(11f, shop.OnHand("lumber"));
            Assert.AreEqual(0f, shop.OnHand("nails_hardware_lot"));

            Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 120, DayIndex, out reason), reason);
            Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 240, DayIndex, out reason), reason);
            Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 120, DayIndex, out reason), reason);
            Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 120, DayIndex, out reason), reason);
            Assert.IsTrue(process.Completed);
            Assert.AreEqual(4, shop.Register.Assets.Count(asset => asset.Kind == "furniture.chair"));
            Assert.AreEqual(4f, shop.OnHand("fuel-wood"), "The chair batch's offcuts are real inventory.");
        }

        [Test]
        public void Production_MakeToStockCap_RefusesPastPolicyMaximum()
        {
            var shop = NewEndowedWorkshop();
            shop.Stock("nails_hardware_lot", 3f, 25, "f1p2-fixture-nails");
            GenericProductionRuntime runtime = shop.NewRuntime();

            for (int table = 0; table < 2; table++)
            {
                Assert.IsTrue(shop.Production.TryBeginFurniture(runtime, "furniture.table", Carpenter, DayIndex,
                    out ProductionProcessState process, out string loopReason), loopReason);
                Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 45, DayIndex, out loopReason), loopReason);
                Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 120, DayIndex, out loopReason), loopReason);
                Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 45, DayIndex, out loopReason), loopReason);
                Assert.IsTrue(shop.Production.TryAdvanceFurniture(runtime, process, Carpenter, 120, DayIndex, out loopReason), loopReason);
                Assert.IsTrue(process.Completed);
            }
            Assert.AreEqual(2, shop.Register.Assets.Count(asset => asset.Kind == "furniture.table"));

            Assert.IsFalse(shop.Production.TryBeginFurniture(runtime, "furniture.table", Carpenter, DayIndex,
                out ProductionProcessState _, out string reason));
            Assert.IsTrue(reason.Contains("maximum"), reason);
        }

        [Test]
        public void Production_Bed_RefusesWithoutHay()
        {
            var shop = NewEndowedWorkshop();
            shop.Stock("nails_hardware_lot", 1f, 25, "f1p2-fixture-nails");
            shop.Stock("basic_cloth", 4f, 9, "f1p2-fixture-cloth");
            GenericProductionRuntime runtime = shop.NewRuntime();

            Assert.IsFalse(shop.Production.TryBeginFurniture(runtime, "furniture.bed", Carpenter, DayIndex,
                out ProductionProcessState _, out string reason));
            Assert.IsTrue(reason.Contains("hay"),
                "F7 finding F3: the bed recipe needs hay and hay has no priced supply, so the bed cannot start. Refusal: " + reason);
            Assert.AreEqual(15f, shop.OnHand("lumber"), "A refused start consumes nothing.");
        }

        [Test]
        public void Production_NonFurnitureMethod_Refuses()
        {
            var shop = NewEndowedWorkshop();
            GenericProductionRuntime runtime = shop.NewRuntime();
            Assert.IsFalse(shop.Production.TryBeginFurniture(runtime, "generic-bread", Carpenter, DayIndex,
                out ProductionProcessState _, out string reason));
            Assert.IsTrue(reason.Contains("not a furniture method"), reason);
        }

        [Test]
        public void Production_NoPolicy_Refuses()
        {
            var shop = new Workshop();
            GenericProductionRuntime runtime = shop.NewRuntime();
            Assert.IsFalse(shop.Production.TryBeginFurniture(runtime, "furniture.table", Carpenter, DayIndex,
                out ProductionProcessState _, out string reason));
            Assert.IsTrue(reason.Contains("no production policy"),
                "Without the carpentry setup template the workshop has no furniture policies and cannot produce. Refusal: " + reason);
        }

        private static string FirstLotId(Workshop shop, string productId)
        {
            GenericBusinessConfiguration configuration = shop.Business.GenericConfiguration;
            foreach (GenericInventoryPosition position in configuration.Inventory ?? new List<GenericInventoryPosition>())
            {
                if (position == null || !string.Equals(position.ProductId, productId, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (GenericInventoryLot lot in position.Lots ?? new List<GenericInventoryLot>())
                    return lot.LotId;
            }
            return string.Empty;
        }
    }
}
