// Generic Business Reconstruction, Pass 1 (new series), section 13.
// Roslyn-verifiable tests for the plain-C# generic-business core.
// These tests compile ONLY the new GenericBusiness folder (dependency-
// free, System only) plus this file, under the small Roslyn harness.
// They do NOT test Unity systems (ScriptableObjects, MonoBehaviours,
// scenes, drivers); those bindings are covered by the Unity-only
// integration tests in GenericBusinessUnityIntegrationTests.cs, which
// must be run in Unity Test Runner by Codex/Unity AI, not claimed
// from this harness.
// Every price, quantity, and rate below is a test parameter, NOT canon.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.GenericBusiness;
using NUnit.Framework;

namespace LandLedgers.Tests.Economy
{
    [TestFixture]
    public sealed class GenericBusinessReconstructionTests
    {
        private static bool ContainsId(System.Collections.Generic.IEnumerable<string> ids, string wanted)
        {
            foreach (string id in ids)
            {
                if (string.Equals(id, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        [Test]
        public void NewBusinessBeginsWithoutInventedStock()
        {
            bool ok = GenericBusinessFormation.TryFormNewBusiness("biz-new-1", "Ken's Emporium", null, out NewBusinessRecord record, out string reason);
            Assert.IsTrue(ok, reason);
            Assert.AreEqual(0, record.CashCents);
            Assert.AreEqual(0f, record.InventoryQuantity("flour"));
            Assert.IsFalse(record.IsScenarioEndowment);
        }

        [Test]
        public void NewBusinessContributionWithoutProvenanceIsRefused()
        {
            var contributions = new[]
            {
                new OwnerContribution { Source = string.Empty, AssetOrProductId = "flour", Quantity = 100f, Provenance = string.Empty },
            };
            bool ok = GenericBusinessFormation.TryFormNewBusiness("biz-new-2", "No Provenance Co", contributions, out NewBusinessRecord record, out string reason);
            Assert.IsFalse(ok);
            Assert.IsNull(record);
            StringAssert.Contains("provenance", reason);
        }

        [Test]
        public void NewBusinessLegitimateContributionIsRecorded()
        {
            var contributions = new[]
            {
                new OwnerContribution { Source = "owner savings", AssetOrProductId = "flour", Quantity = 50f, CashCents = 5000, Provenance = "owner contribution, day 1" },
            };
            bool ok = GenericBusinessFormation.TryFormNewBusiness("biz-new-3", "Contributed Co", contributions, out NewBusinessRecord record, out string reason);
            Assert.IsTrue(ok, reason);
            Assert.AreEqual(5000, record.CashCents);
            Assert.AreEqual(50f, record.InventoryQuantity("flour"));
        }

        [Test]
        public void ExistingBusinessRetainsDocumentedStartingInventory()
        {
            var endowment = new ScenarioEndowment
            {
                EndowmentId = "endow-slice-1",
                ScenarioId = "six-person-slice",
                Provenance = "scenario endowment, documented in the scenario definition",
                CashCents = 5000,
                Items = new List<ScenarioEndowmentItem>
                {
                    new ScenarioEndowmentItem { ItemId = "flour", LotOrAssetId = "lot-flour-001", Quantity = 100f, Provenance = "scenario endowment" },
                },
            };
            bool ok = GenericBusinessFormation.TryBeginScenarioBusiness("biz-slice-1", "Baldwin-Ooms General Store", endowment, out NewBusinessRecord record, out string reason);
            Assert.IsTrue(ok, reason);
            Assert.IsTrue(record.IsScenarioEndowment);
            Assert.AreEqual("endow-slice-1", record.EndowmentId);
            Assert.AreEqual(5000, record.CashCents);
            Assert.AreEqual(100f, record.InventoryQuantity("flour"));
        }

        [Test]
        public void ScenarioEndowmentWithoutIdentityIsRefused()
        {
            var endowment = new ScenarioEndowment
            {
                EndowmentId = "endow-bad",
                ScenarioId = "six-person-slice",
                Provenance = "scenario endowment",
                Items = new List<ScenarioEndowmentItem>
                {
                    new ScenarioEndowmentItem { ItemId = "flour", LotOrAssetId = string.Empty, Quantity = 100f, Provenance = "scenario endowment" },
                },
            };
            bool ok = GenericBusinessFormation.TryBeginScenarioBusiness("biz-slice-2", "Bad Endowment Co", endowment, out NewBusinessRecord record, out string reason);
            Assert.IsFalse(ok);
            Assert.IsNull(record);
        }

        [Test]
        public void RetailWithoutTemplateAndCatalogExpansionDoNotDuplicateRetail()
        {
            var set = new GenericBusinessCapabilitySet();
            set.TryExpandRetailCatalog(new[] { "meat", "clothing" }, out _);
            Assert.IsTrue(set.HasKind(BusinessCapabilityKind.SellGoods));
            Assert.IsTrue(set.HasRetailProduct("meat"));
            Assert.IsTrue(set.HasRetailProduct("clothing"));
            set.TryExpandRetailCatalog(new[] { "meat", "shoes" }, out _);
            Assert.AreEqual(3, set.RetailProductIds.Count);
            Assert.IsTrue(set.HasRetailProduct("shoes"));
            int retailKindCount = 0;
            foreach (BusinessCapabilityKind kind in set.EnabledKinds)
            {
                if (kind == BusinessCapabilityKind.SellGoods) retailKindCount++;
            }
            Assert.AreEqual(1, retailKindCount);
        }

        [Test]
        public void AddingBarberCapabilityToExistingRetailerKeepsOneBusiness()
        {
            var set = new GenericBusinessCapabilitySet();
            set.TryExpandRetailCatalog(new[] { "meat", "clothing" }, out _);
            set.TryEnableService("barber-haircut", out string reason);
            Assert.IsTrue(set.HasKind(BusinessCapabilityKind.SellGoods));
            Assert.IsTrue(set.HasKind(BusinessCapabilityKind.OfferServices));
            Assert.IsTrue(set.HasRetailProduct("meat"));
            Assert.AreEqual(1, set.EnabledServiceIds.Count);
            set.TryEnableService("barber-haircut", out _);
            Assert.AreEqual(1, set.EnabledServiceIds.Count);
        }

        [Test]
        public void BarberFeasibilityRefusesWithoutChairToolsSpaceOrQualifiedPerson()
        {
            var recipe = new ProductionRecipeDetail
            {
                MethodId = "barber.haircut",
                RequiredEquipmentIds = new List<string> { "barber-chair", "barber-cutting-tools" },
                RequiredWorkspaceIds = new List<string> { "barber-shop" },
                PerformanceSkillId = "barbering",
                Outputs = new List<ProductionRecipeOutput> { new ProductionRecipeOutput { ProductId = "haircut", Quantity = 1f } },
            };
            var input = new CapabilityFeasibilityInput { HasPremisesRight = true };
            bool ok = GenericBusinessCapabilitySet.CheckProductionFeasibility(recipe, input, out List<string> reasons);
            Assert.IsFalse(ok);
            Assert.IsTrue(reasons.Count >= 3);
            input.OwnedEquipmentIds = new List<string> { "barber-chair", "barber-cutting-tools" };
            input.AvailableWorkspaceIds = new List<string> { "barber-shop" };
            input.QualifiedPersonSkillIds = new List<string> { "barbering" };
            ok = GenericBusinessCapabilitySet.CheckProductionFeasibility(recipe, input, out reasons);
            Assert.IsTrue(ok, string.Join(" ", reasons));
        }

        [Test]
        public void MultipleProductionRecipesAreDistinctAndValid()
        {
            IReadOnlyList<ProductionRecipeDetail> recipes = BakeryRecipePresets.All();
            Assert.AreEqual(3, recipes.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProductionRecipeDetail recipe in recipes)
            {
                Assert.AreEqual(0, recipe.Validate().Count, string.Join(" ", recipe.Validate()));
                Assert.IsTrue(seen.Add(recipe.MethodId));
                Assert.IsTrue(recipe.TotalOutputQuantity() > 0f);
            }
            Assert.AreEqual(24f, BakeryRecipePresets.BreadBatch().TotalOutputQuantity());
            Assert.AreEqual(48f, BakeryRecipePresets.RollsBatch().TotalOutputQuantity());
            Assert.AreEqual(12f, BakeryRecipePresets.PieBatch().TotalOutputQuantity());
        }

        [Test]
        public void InsufficientEquipmentRefusesProduction()
        {
            var recipe = BakeryRecipePresets.BreadBatch();
            var input = new CapabilityFeasibilityInput
            {
                HasPremisesRight = true,
                OnHandQuantities = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase) { { "flour", 12f }, { "notions", 1f } },
                OwnedEquipmentIds = new List<string> { "baker-hand-kit" },
                AvailableWorkspaceIds = new List<string> { "preparation", "oven-space" },
                QualifiedPersonSkillIds = new List<string> { "baking" },
            };
            bool ok = GenericBusinessCapabilitySet.CheckProductionFeasibility(recipe, input, out List<string> reasons);
            Assert.IsFalse(ok);
            StringAssert.Contains("oven", string.Join(" ", reasons));
        }

        [Test]
        public void InsufficientFloorSpaceRefusesReservation()
        {
            var premises = new PremisesCapacity();
            premises.AddFloor(new BuildingFloor { FloorIndex = 0, GrossAreaSqM = 200f, UsableFraction01 = 0.9f, Use = "shop" });
            bool ok = premises.TryReserve(new SpaceReservation { ReservationId = "chair-1", Activity = "barbering", FloorIndex = 0, AreaSqM = 5f }, out string reason);
            Assert.IsTrue(ok, reason);
            Assert.AreEqual(175f, premises.FreeAreaSqM(0), 0.001f);
            ok = premises.TryReserve(new SpaceReservation { ReservationId = "hundreds-of-chairs", Activity = "seating", FloorIndex = 0, AreaSqM = 500f }, out reason);
            Assert.IsFalse(ok);
            StringAssert.Contains("Insufficient usable space", reason);
        }

        [Test]
        public void MixedUsePremisesAndMultipleFloors()
        {
            var premises = new PremisesCapacity();
            premises.AddFloor(new BuildingFloor { FloorIndex = 0, GrossAreaSqM = 200f, UsableFraction01 = 0.9f, Use = "shop" });
            premises.AddFloor(new BuildingFloor { FloorIndex = 1, GrossAreaSqM = 200f, UsableFraction01 = 0.9f, Use = "residential" });
            Assert.IsTrue(premises.TryReserve(new SpaceReservation { ReservationId = "shop-counter", Activity = "retail", FloorIndex = 0, AreaSqM = 20f }, out _));
            Assert.IsTrue(premises.TryReserve(new SpaceReservation { ReservationId = "bedroom", Activity = "residential", FloorIndex = 1, AreaSqM = 40f }, out _));
            Assert.AreEqual(300f, premises.TotalFreeAreaSqM(), 0.001f);
            premises.DeclareIncompatible("slaughter", "retail");
            Assert.IsFalse(premises.TryReserve(new SpaceReservation { ReservationId = "slaughter-bench", Activity = "slaughter", FloorIndex = 0, AreaSqM = 10f }, out string reason));
            StringAssert.Contains("incompatible", reason);
        }

        [Test]
        public void LegitimateLeasingAllowsOperationAndMissingRightRefuses()
        {
            var leased = new SiteEvaluationInput
            {
                RightKind = PremisesRightKind.Leased,
                RightDescription = "leased commercial space, terms satisfied",
                LeaseTermsSatisfied = true,
                PlotZoneLabel = "Residential",
                RecommendedPlotZones = new List<string> { "Business", "MixedUse" },
            };
            SiteEvaluationResult leasedResult = SiteEligibility.Evaluate(leased);
            Assert.IsTrue(leasedResult.CanOperate, string.Join(" ", leasedResult.RefusalReasons));
            Assert.AreEqual(1, leasedResult.Warnings.Count);
            var noRight = new SiteEvaluationInput { RightKind = PremisesRightKind.None };
            SiteEvaluationResult noRightResult = SiteEligibility.Evaluate(noRight);
            Assert.IsFalse(noRightResult.CanOperate);
        }

        [Test]
        public void NoArtificialZoningLock()
        {
            Assert.IsFalse(SiteEligibility.ZoneAloneBlocksActivity());
            var input = new SiteEvaluationInput
            {
                RightKind = PremisesRightKind.Owned,
                PlotZoneLabel = "Agricultural",
                RecommendedPlotZones = new List<string> { "Business" },
            };
            SiteEvaluationResult result = SiteEligibility.Evaluate(input);
            Assert.IsTrue(result.CanOperate);
            Assert.AreEqual(1, result.Warnings.Count);
        }

        [Test]
        public void ValidSupplierPurchaseSortsByDeliveredTotalAndKeepsHorseWagonDistinct()
        {
            var horse = new EquipmentPurchaseLine { AssetKind = "horse", AssetId = "horse-001", VendorId = "vendor-a", VendorName = "Test Vendor A", PriceCents = 10000, FreightCents = 500, FreightOption = "seller-freight" };
            var wagon = new EquipmentPurchaseLine { AssetKind = "wagon", AssetId = "wagon-001", VendorId = "vendor-a", VendorName = "Test Vendor A", PriceCents = 8000, FreightCents = 200, FreightOption = "seller-freight" };
            var barberTools = new EquipmentPurchaseLine { AssetKind = "barber tools", AssetId = "tools-001", VendorId = "vendor-b", VendorName = "Test Vendor B", PriceCents = 3000, FreightCents = 0, FreightOption = "self-collect" };
            Assert.AreEqual(0, EquipmentPurchase.ValidateLine(horse).Count);
            Assert.AreEqual(0, EquipmentPurchase.ValidateLine(wagon).Count);
            Assert.IsFalse(EquipmentPurchase.SameAsset(horse, wagon));
            Assert.IsTrue(EquipmentPurchase.SameAsset(horse, horse));
            List<EquipmentPurchaseLine> sorted = EquipmentPurchase.SortByDeliveredTotal(new[] { horse, wagon, barberTools });
            Assert.AreEqual("tools-001", sorted[0].AssetId);
            Assert.AreEqual("wagon-001", sorted[1].AssetId);
            Assert.AreEqual("horse-001", sorted[2].AssetId);
            Assert.IsTrue(EquipmentPurchase.TryAdvance(horse, out _));
            Assert.AreEqual(EquipmentPurchaseStage.Ordered, horse.Stage);
        }

        [Test]
        public void PurchaseWithoutVendorIsRefused()
        {
            var line = new EquipmentPurchaseLine { AssetKind = "horse", AssetId = "horse-002", VendorId = string.Empty, PriceCents = 10000 };
            Assert.IsTrue(EquipmentPurchase.ValidateLine(line).Count > 0);
        }

        [Test]
        public void NoDuplicatedBusinessOrFinancialAuthorityInCapabilitySet()
        {
            var set = new GenericBusinessCapabilitySet();
            set.TryAddCapability(BusinessCapabilityKind.SellGoods, out _);
            set.TryAddCapability(BusinessCapabilityKind.SellGoods, out _);
            set.TryAddCapability(BusinessCapabilityKind.MakeProducts, out _);
            Assert.AreEqual(2, set.EnabledKinds.Count);
        }

        [Test]
        public void ThroughputCapacityIsBoundByRealConstraintsNotAQuota()
        {
            var input = new ThroughputCapacityInput
            {
                MaterialUnitsOnHand = 100f,
                MaterialUnitsPerOutput = 2f,
                WorkerHoursAvailable = 10f,
                OutputsPerWorkerHour = 5f,
                EquipmentHoursAvailable = 10f,
                OutputsPerEquipmentHour = 10f,
                FuelOrEnergyOnHand = 100f,
                FuelOrEnergyPerOutput = 1f,
                TransportCapacityUnits = 1000f,
                StorageCapacityUnits = 1000f,
            };
            ThroughputCapacity.CapacityResult result = ThroughputCapacity.Compute(input);
            Assert.AreEqual(50f, result.MaxOutputs);
            Assert.AreEqual("materials", result.BindingConstraint);
            input.WorkerHoursAvailable = 2f;
            result = ThroughputCapacity.Compute(input);
            Assert.AreEqual(10f, result.MaxOutputs);
            Assert.AreEqual("worker time", result.BindingConstraint);
        }

        [Test]
        public void TemplatesNeverGrantStockCashEquipmentOrWorkers()
        {
            foreach (GenericBusinessTemplate template in GenericBusinessTemplates.All())
            {
                AppliedTemplate applied = GenericBusinessTemplates.Apply(template);
                Assert.AreEqual(template.TemplateId, applied.TemplateId);
                Assert.IsTrue(applied.EquipmentToAcquire.Count > 0);
            }
            AppliedTemplate barber = GenericBusinessTemplates.Apply(GenericBusinessTemplates.Barber());
            Assert.IsFalse(barber.CapabilitySet.HasKind(BusinessCapabilityKind.SellGoods));
            Assert.IsTrue(ContainsId(barber.CapabilitySet.EnabledServiceIds, "barber-haircut"));
            Assert.IsTrue(ContainsId(barber.EquipmentToAcquire, "barber-chair"));
            Assert.IsTrue(ContainsId(barber.EquipmentToAcquire, "barber-cutting-tools"));
        }

        [Test]
        public void AcquisitionInterfaceDefaultsToStartABusinessWithoutLandTab()
        {
            AcquisitionInterfaceModel model = AcquisitionInterfaceModel.Default();
            Assert.AreEqual(AcquisitionTab.StartABusiness, model.SelectedTab);
            Assert.AreEqual("Start a Business", model.SelectedTabName());
            Assert.IsFalse(model.LandTabPresentInThisInterface);
            Assert.IsTrue(model.LandAvailableViaPropertyParcelWorkflow);
            model.SelectTab(AcquisitionTab.PreExistingBusinesses);
            Assert.AreEqual("Pre-existing Businesses", model.SelectedTabName());
        }

        [Test]
        public void StartBusinessFormRefusesMissingPremisesRight()
        {
            var form = new StartBusinessForm
            {
                BusinessDisplayName = "Ken's Emporium",
                OwnerId = "player-1",
                PremisesRight = PremisesRightKind.None,
                RequestedCapabilities = new List<BusinessCapabilityKind> { BusinessCapabilityKind.SellGoods },
            };
            Assert.IsFalse(form.TryCreate("biz-form-1", out NewBusinessRecord record, out string reason));
            Assert.IsNull(record);
            StringAssert.Contains("premises right", reason);
            form.PremisesRight = PremisesRightKind.Leased;
            Assert.IsTrue(form.TryCreate("biz-form-1", out record, out reason), reason);
            Assert.AreEqual(0, record.CashCents);
            Assert.IsTrue(record.Capabilities.HasKind(BusinessCapabilityKind.SellGoods));
        }
    }
}
