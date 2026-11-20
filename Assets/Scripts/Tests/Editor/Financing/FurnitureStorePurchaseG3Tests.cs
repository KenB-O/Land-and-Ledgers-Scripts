// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent G, slice G-3 (expanded campaign 2026-11-16, W-G4 Workstream 3):
// the F4 purchase/install binding — the store vendor adapter, the order
// builder, the delivery quote, the install handler, the free-placement
// selection, the custody-piece leg, and the end-to-end store purchase
// through the real F4 service. The live prices are the F4 pins (bed 294,
// table 221, chair 123 — F2 cost-plus at baseline pressure, markup 1.4);
// the freight fare is the DEF-016 provisional card (25 base + 1c/mile,
// round trip), so a 10-mile one-way bed delivery quotes 45c, the same
// fare the F4 test uses.

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using LandLedgers.Primitives;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.GenericBusiness;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.Persistence;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public class FurnitureStorePurchaseG3Tests
    {
        private const int BuyerHousehold = 7;
        private const int SellerHousehold = 9;
        private const int BuildingInstance = 12;
        private const int DayIndex = 42;
        private const string StoreId = "general-store";
        private const string CarrierId = "freight-co";
        private const float FurnitureCategoryMarkup = 1.4f;

        private FreightShipmentService freight;
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private EntityId loader;
        private EntityId driver;
        private EquipmentAsset wagon;
        private HouseholdLedger buyerLedger;
        private HouseholdLedger sellerLedger;
        private BusinessRuntimeState storeRuntime;
        private BusinessRuntimeState carrierRuntime;
        private Series2BusinessRuntimeCashPort carrierCash;
        private FreightShipmentHooks hooksTemplate;
        private BuildingFootprintAuthority building;
        private BuildingFootprintAuthority otherBuilding;
        private List<BuildingEquipmentPlacement> placements;
        private HouseholdFurnishingRegister buyerRegister;
        private HouseholdFurnishingRegister sellerRegister;
        private List<HouseholdFurnishingRegister> allRegisters;
        private FurnitureStorePurchaseDriver purchaseDriver;
        private List<GameObject> ownedObjects;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(DayIndex);
            freight = new FreightShipmentService();
            loader = EntityId.For(EntityKind.Person, 701);
            driver = EntityId.For(EntityKind.Person, 702);
            wagon = new EquipmentAsset
            {
                AssetId = "opening-freight-wagon", Kind = "wagon", DisplayName = "Opening wagon",
                Condition01 = 1f, PayloadMassCapacityKg = 450f, CargoVolumeCapacityM3 = 4f,
            };
            freight.RegisterWagon(wagon);

            buyerLedger = new HouseholdLedger(BuyerHousehold);
            Assert.IsNull(buyerLedger.RecordInflow(DayIndex, 40000, HouseholdIncomeSource.OwnerContribution,
                "test-endowment", "test opening cash", "test"));
            sellerLedger = new HouseholdLedger(SellerHousehold);
            Assert.IsNull(sellerLedger.RecordInflow(DayIndex, 1000, HouseholdIncomeSource.OwnerContribution,
                "test-endowment", "test opening cash", "test"));

            storeRuntime = new BusinessRuntimeState();
            storeRuntime.BindBusinessIdentity(StoreId);
            storeRuntime.AddCashCents(5000);
            storeRuntime.EnsureCategoryStock(FurnitureStorePurchase.FurnitureCategoryId, 0, 1000);
            carrierRuntime = new BusinessRuntimeState();
            carrierRuntime.BindBusinessIdentity(CarrierId);
            carrierRuntime.AddCashCents(0);
            carrierCash = new Series2BusinessRuntimeCashPort(carrierRuntime);

            hooksTemplate = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = DayIndex,
                Diagnostics = new List<string>(),
                Reservations = null,
                NowGameSeconds = 0f,
                Schedules = null,
                MinuteOfDay = 480,
            };

            ownedObjects = new List<GameObject>();
            building = MakeBuilding("buyer-house");
            otherBuilding = MakeBuilding("other-house");
            placements = new List<BuildingEquipmentPlacement>();
            buyerRegister = new HouseholdFurnishingRegister { HouseholdId = BuyerHousehold };
            sellerRegister = new HouseholdFurnishingRegister { HouseholdId = SellerHousehold };
            allRegisters = new List<HouseholdFurnishingRegister> { buyerRegister, sellerRegister };

            purchaseDriver = new FurnitureStorePurchaseDriver(storeRuntime, QuotePrice, carrierCash,
                freight, "opening-freight-wagon", loader, driver, null,
                () => placements, PlacementIsBound, hooksTemplate, DayIndex);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject owned in ownedObjects)
            {
                if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
            }
            ownedObjects.Clear();
        }

        private bool PlacementIsBound(string placementId)
        {
            if (string.IsNullOrWhiteSpace(placementId)) return false;
            foreach (HouseholdFurnishingRegister register in allRegisters)
            {
                if (register?.Bindings == null) continue;
                foreach (FurnishingPlacementBinding binding in register.Bindings)
                {
                    if (binding != null && string.Equals(binding.BindingId, placementId, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        private static RetailPricingDefinition Pricing(int baseline, int bandMin, int bandMax) =>
            new RetailPricingDefinition
            {
                baselineCents = baseline,
                baseWholesaleCostCents = 0,
                importCostCents = 0,
                localMarketBand = new PriceBand { minCents = bandMin, maxCents = bandMax },
                markupMultiplier = 1f,
                supplyDemandSensitivity = 0.25f,
            };

        private static int QuotePrice(string itemId)
        {
            switch (itemId)
            {
                case FurnitureItemSpecs.BedId:
                    return FurniturePurchaseService.QuoteUnitPriceCents(Pricing(300, 250, 450), FurnitureCategoryMarkup, 0f);
                case FurnitureItemSpecs.TableId:
                    return FurniturePurchaseService.QuoteUnitPriceCents(Pricing(225, 175, 325), FurnitureCategoryMarkup, 0f);
                case FurnitureItemSpecs.ChairId:
                    return FurniturePurchaseService.QuoteUnitPriceCents(Pricing(125, 100, 175), FurnitureCategoryMarkup, 0f);
                case FurnitureItemSpecs.CabinetSmallId:
                    return FurniturePurchaseService.QuoteUnitPriceCents(Pricing(325, 250, 450), FurnitureCategoryMarkup, 0f);
                case FurnitureItemSpecs.CabinetLargeId:
                    return FurniturePurchaseService.QuoteUnitPriceCents(Pricing(425, 325, 600), FurnitureCategoryMarkup, 0f);
                case FurnitureItemSpecs.ShelfId:
                    return FurniturePurchaseService.QuoteUnitPriceCents(Pricing(80, 60, 125), FurnitureCategoryMarkup, 0f);
                case FurnitureItemSpecs.CrateId:
                    return FurniturePurchaseService.QuoteUnitPriceCents(Pricing(75, 50, 125), FurnitureCategoryMarkup, 0f);
                case FurnitureItemSpecs.BedrollId:
                    return FurniturePurchaseService.QuoteUnitPriceCents(Pricing(60, 40, 100), FurnitureCategoryMarkup, 0f);
                default:
                    return -1;
            }
        }

        private static List<InteriorFloorAuthoring> GetInteriorFloors(BuildingFootprintAuthority authority)
        {
            FieldInfo field = typeof(BuildingFootprintAuthority).GetField("interiorFloors", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (List<InteriorFloorAuthoring>)field.GetValue(authority);
        }

        private BuildingFootprintAuthority MakeBuilding(string name)
        {
            GameObject buildingObject = new(name);
            ownedObjects.Add(buildingObject);
            BuildingFootprintAuthority authority = buildingObject.AddComponent<BuildingFootprintAuthority>();
            InteriorFloorAuthoring floor = new() { floorIndex = 0, floorHeightLocal = 0f };
            floor.walkableAreas.Add(new InteriorPolygonAuthoring
            {
                label = "Test Floor",
                vertices = new List<Vector3>
                {
                    new(-3f, 0f, -3f), new(3f, 0f, -3f), new(3f, 0f, 3f), new(-3f, 0f, 3f),
                },
            });
            GetInteriorFloors(authority).Add(floor);
            return authority;
        }

        private BuildingEquipmentPlacement MakePlacement(string name, BuildingFootprintAuthority targetBuilding,
            string definitionId, string placementId)
        {
            GameObject item = new(name);
            ownedObjects.Add(item);
            BuildingEquipmentPlacement placement = item.AddComponent<BuildingEquipmentPlacement>();
            if (targetBuilding != null) placement.AssociateWithBuilding(targetBuilding);
            if (placementId != null) placement.Configure(placementId, definitionId ?? "furniture.unspecified", name, 0);
            return placement;
        }

        private void AddLot(string lotId, string itemId, int units)
        {
            Assert.IsNull(storeRuntime.TryAddRetailItemLot(lotId, itemId,
                FurnitureStorePurchase.FurnitureCategoryId, "each", units, StoreId, StoreId,
                "G3 test lot", DayIndex), "store lot seeding should succeed");
        }

        private static EquipmentAsset MakeAsset(string assetId, string kind, int ownerId,
            string madeByBusinessId = "", float condition = 1f, string reservedBy = "")
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = kind,
                DisplayName = kind,
                Condition01 = condition,
                OwnerKind = "household",
                OwnerId = ownerId.ToString(),
                LocationId = "household:" + ownerId,
                MadeByBusinessId = madeByBusinessId ?? string.Empty,
                ReservedBy = reservedBy ?? string.Empty,
            };
        }

        private FurniturePurchaseInstallHandler NewHandler(HouseholdFurnishingRegister register,
            FurnishingAcquisitionSource source, HouseholdFurnishingRegister seller, List<string> diag)
        {
            return new FurniturePurchaseInstallHandler(register, building, BuildingInstance,
                placements, PlacementIsBound, source, seller, diag);
        }

        private static DevelopmentTownConfiguration.FreightServiceRateCard Def016Card() =>
            new DevelopmentTownConfiguration.FreightServiceRateCard
            {
                baseServiceFeeCents = 25,
                ratePerMileCents = 1,
                chargeRoundTrip = true,
            };

        // --- Capability map ---

        [Test]
        public void CapabilityMap_AllEightKinds()
        {
            Assert.IsTrue(FurnitureInstallBinding.TryCapabilityForKind(FurnitureItemSpecs.BedId, out FurnishingCapabilityKind bed));
            Assert.AreEqual(FurnishingCapabilityKind.SleepingPlace, bed);
            Assert.IsTrue(FurnitureInstallBinding.TryCapabilityForKind(FurnitureItemSpecs.BedrollId, out FurnishingCapabilityKind bedroll));
            Assert.AreEqual(FurnishingCapabilityKind.SleepingPlace, bedroll);
            Assert.IsTrue(FurnitureInstallBinding.TryCapabilityForKind(FurnitureItemSpecs.TableId, out FurnishingCapabilityKind table));
            Assert.AreEqual(FurnishingCapabilityKind.DiningTable, table);
            Assert.IsTrue(FurnitureInstallBinding.TryCapabilityForKind(FurnitureItemSpecs.ChairId, out FurnishingCapabilityKind chair));
            Assert.AreEqual(FurnishingCapabilityKind.DiningSeat, chair);
            Assert.IsTrue(FurnitureInstallBinding.TryCapabilityForKind(FurnitureItemSpecs.CabinetSmallId, out FurnishingCapabilityKind cabinetSmall));
            Assert.AreEqual(FurnishingCapabilityKind.StorageZone, cabinetSmall);
            Assert.IsTrue(FurnitureInstallBinding.TryCapabilityForKind(FurnitureItemSpecs.CabinetLargeId, out FurnishingCapabilityKind cabinetLarge));
            Assert.AreEqual(FurnishingCapabilityKind.StorageZone, cabinetLarge);
            Assert.IsTrue(FurnitureInstallBinding.TryCapabilityForKind(FurnitureItemSpecs.ShelfId, out FurnishingCapabilityKind shelf));
            Assert.AreEqual(FurnishingCapabilityKind.StorageZone, shelf);
            Assert.IsTrue(FurnitureInstallBinding.TryCapabilityForKind(FurnitureItemSpecs.CrateId, out FurnishingCapabilityKind crate));
            Assert.AreEqual(FurnishingCapabilityKind.None, crate);
            Assert.IsFalse(FurnitureInstallBinding.TryCapabilityForKind("furniture.throne", out _), "unknown kinds are never installed on a guessed capability");
            Assert.IsFalse(FurnitureInstallBinding.TryCapabilityForKind("furniture.cooking-stove", out _), "stoves are out of the first wave — no stove can reach this binding");
            Assert.IsFalse(FurnitureInstallBinding.TryCapabilityForKind("", out _));
            Assert.IsFalse(FurnitureInstallBinding.TryCapabilityForKind(null, out _));
        }

        // --- Free-placement selection ---

        [Test]
        public void FreePlacement_ExactKindMatch()
        {
            BuildingEquipmentPlacement placement = MakePlacement("Bed", building, "furniture.bed", "plc-bed");
            placements.Add(placement);
            Assert.IsTrue(FurnitureInstallBinding.TryFindFreePlacement("furniture.bed", building,
                placements, PlacementIsBound, out BuildingEquipmentPlacement found, out string refusal), refusal);
            Assert.AreSame(placement, found);
        }

        [Test]
        public void FreePlacement_UnspecifiedDefinition_Accepted()
        {
            BuildingEquipmentPlacement placement = MakePlacement("Empty slot", building, null, null);
            placements.Add(placement);
            Assert.AreEqual("furniture.unspecified", placement.DefinitionId, "a never-configured placement keeps the default definition");
            Assert.IsTrue(FurnitureInstallBinding.TryFindFreePlacement("furniture.bed", building,
                placements, PlacementIsBound, out BuildingEquipmentPlacement found, out string refusal), refusal);
            Assert.AreSame(placement, found);
        }

        [Test]
        public void FreePlacement_DifferentKind_Refused()
        {
            BuildingEquipmentPlacement placement = MakePlacement("Table", building, "furniture.table", "plc-table");
            placements.Add(placement);
            Assert.IsFalse(FurnitureInstallBinding.TryFindFreePlacement("furniture.bed", building,
                placements, PlacementIsBound, out BuildingEquipmentPlacement found, out string refusal));
            Assert.IsNull(found);
            Assert.IsNotNull(refusal);
        }

        [Test]
        public void FreePlacement_WrongBuilding_Refused()
        {
            BuildingEquipmentPlacement placement = MakePlacement("Bed", otherBuilding, "furniture.bed", "plc-bed");
            placements.Add(placement);
            Assert.IsFalse(FurnitureInstallBinding.TryFindFreePlacement("furniture.bed", building,
                placements, PlacementIsBound, out _, out _), "a placement from another building is never adopted");
        }

        [Test]
        public void FreePlacement_BoundPlacement_Refused()
        {
            BuildingEquipmentPlacement placement = MakePlacement("Bed", building, "furniture.bed", "plc-bed");
            placements.Add(placement);
            EquipmentAsset first = MakeAsset("bed-1", FurnitureItemSpecs.BedId, BuyerHousehold);
            var handler = NewHandler(buyerRegister, FurnishingAcquisitionSource.Purchase, null, new List<string>());
            Assert.IsNull(handler.Install(first, "purchase-1"), "the first bed installs");
            Assert.IsFalse(FurnitureInstallBinding.TryFindFreePlacement("furniture.bed", building,
                placements, PlacementIsBound, out _, out _), "the bound placement is no longer free — the register does not enforce this, the selection does");
        }

        [Test]
        public void FreePlacement_NullBoundCheck_Refuses()
        {
            BuildingEquipmentPlacement placement = MakePlacement("Bed", building, "furniture.bed", "plc-bed");
            placements.Add(placement);
            Assert.IsFalse(FurnitureInstallBinding.TryFindFreePlacement("furniture.bed", building,
                placements, null, out _, out string refusal), "without the bound check a placement cannot be proven free");
            Assert.IsNotNull(refusal);
        }

        // --- Install handler ---

        [Test]
        public void Handler_InstallsBed_SleepingPlace()
        {
            BuildingEquipmentPlacement placement = MakePlacement("Bed", building, "furniture.bed", "plc-bed");
            placements.Add(placement);
            EquipmentAsset asset = MakeAsset("bed-1", FurnitureItemSpecs.BedId, BuyerHousehold);
            var diag = new List<string>();
            var handler = NewHandler(buyerRegister, FurnishingAcquisitionSource.Purchase, null, diag);
            Assert.IsNull(handler.Install(asset, "purchase-1"));
            Assert.AreSame(asset, buyerRegister.FindAsset("bed-1"), "the received asset is registered");
            FurnishingPlacementBinding binding = buyerRegister.LiveBindingFor("bed-1");
            Assert.NotNull(binding);
            Assert.AreEqual(FurnishingCapabilityKind.SleepingPlace, binding.CapabilityKind);
            Assert.AreEqual(FurnishingAcquisitionSource.Purchase, binding.AcquisitionSource);
            Assert.AreEqual("purchase-1", binding.AcquisitionSourceId);
            Assert.AreEqual(BuildingInstance, binding.BuildingInstanceId);
            Assert.AreEqual("building:" + BuildingInstance, asset.LocationId);
            Assert.AreEqual(BuyerHousehold.ToString(), placement.EconomicOwnerId, "the placement's economic record names the buyer");
            Assert.AreEqual("purchase-1", placement.AcquisitionSourceId);
        }

        [Test]
        public void Handler_NoFreePlacement_Refuses()
        {
            EquipmentAsset asset = MakeAsset("bed-1", FurnitureItemSpecs.BedId, BuyerHousehold);
            var handler = NewHandler(buyerRegister, FurnishingAcquisitionSource.Purchase, null, new List<string>());
            Assert.IsNotNull(handler.Install(asset, "purchase-1"), "no placement, no install — never a guessed spot");
            Assert.IsNull(buyerRegister.LiveBindingFor("bed-1"));
            Assert.AreSame(asset, buyerRegister.FindAsset("bed-1"), "the asset stays registered in custody — the stuck-purchase retry is the plan §9.9 follow-up");
        }

        [Test]
        public void Handler_UnknownKind_Refuses()
        {
            BuildingEquipmentPlacement placement = MakePlacement("Throne", building, "furniture.throne", "plc-throne");
            placements.Add(placement);
            EquipmentAsset asset = MakeAsset("throne-1", "furniture.throne", BuyerHousehold);
            var handler = NewHandler(buyerRegister, FurnishingAcquisitionSource.Purchase, null, new List<string>());
            Assert.IsNotNull(handler.Install(asset, "purchase-1"));
            Assert.IsNull(buyerRegister.LiveBindingFor("throne-1"));
        }

        [Test]
        public void Handler_Secondhand_DeregistersSeller()
        {
            // The seller's house is a DIFFERENT building from the buyer's
            // (two households cannot furnish the same building — the G-2
            // invariant): the seller's placement belongs to the seller's
            // building, so the buyer's free-placement selection can never
            // adopt it, and its economic record stays cleared after the
            // binding ends.
            BuildingFootprintAuthority sellerBuilding = MakeBuilding("seller-house");
            BuildingEquipmentPlacement sellerPlacement = MakePlacement("Seller bed", sellerBuilding, "furniture.bed", "plc-seller-bed");
            placements.Add(sellerPlacement);
            BuildingEquipmentPlacement buyerPlacement = MakePlacement("Buyer bed", building, "furniture.bed", "plc-buyer-bed");
            placements.Add(buyerPlacement);
            EquipmentAsset asset = MakeAsset("bed-used-1", FurnitureItemSpecs.BedId, SellerHousehold, "carpenter-1", 0.8f);
            Assert.IsNull(sellerRegister.TryRegisterAsset(asset));
            Assert.IsNull(sellerRegister.TryInstall(asset.AssetId, sellerPlacement, sellerBuilding, 11, 0,
                FurnishingCapabilityKind.SleepingPlace, string.Empty, FurnishingAcquisitionSource.Manufacture,
                "carpenter-1", out FurnishingPlacementBinding _), "the seller's bed starts installed in the seller's house");
            Assert.IsNull(asset.TransferOwnership("household", BuyerHousehold.ToString(), "furniture purchase purchase-used", DayIndex),
                "the F4 service transfers ownership at receipt, before the handler runs");
            asset.LocationId = "household:" + BuyerHousehold;
            var diag = new List<string>();
            var handler = NewHandler(buyerRegister, FurnishingAcquisitionSource.Secondhand, sellerRegister, diag);
            Assert.IsNull(handler.Install(asset, "purchase-used"));
            Assert.IsNull(sellerRegister.FindAsset("bed-used-1"), "the seller no longer holds the asset");
            Assert.IsNull(sellerRegister.LiveBindingFor("bed-used-1"), "the seller's binding ended");
            Assert.AreEqual(string.Empty, sellerPlacement.EconomicOwnerId, "the seller placement's economic record was cleared");
            Assert.AreSame(asset, buyerRegister.FindAsset("bed-used-1"));
            FurnishingPlacementBinding binding = buyerRegister.LiveBindingFor("bed-used-1");
            Assert.NotNull(binding);
            Assert.AreEqual(FurnishingAcquisitionSource.Secondhand, binding.AcquisitionSource);
            Assert.AreEqual(FurnishingCapabilityKind.SleepingPlace, binding.CapabilityKind);
            Assert.AreEqual("carpenter-1", asset.MadeByBusinessId, "maker provenance survives the transfer");
        }

        // --- Vendor adapter ---

        [Test]
        public void Adapter_UnitsOnHand_NamedLot()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            AddLot("bed-lot-2", FurnitureItemSpecs.BedId, 2);
            var stock = new StoreFurnitureVendorStock(storeRuntime, new List<string>());
            Assert.AreEqual(1, stock.UnitsOnHand("bed-lot-1"));
            Assert.AreEqual(2, stock.UnitsOnHand("bed-lot-2"));
            Assert.AreEqual(0, stock.UnitsOnHand("bed-lot-9"), "an unknown lot holds nothing");
            Assert.AreEqual(0, stock.UnitsOnHand(null));
        }

        [Test]
        public void Adapter_TryDebit_LotPinned()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            AddLot("bed-lot-2", FurnitureItemSpecs.BedId, 2);
            var stock = new StoreFurnitureVendorStock(storeRuntime, new List<string>());
            Assert.IsNull(stock.TryDebit("bed-lot-2", 1), "the named lot debits, not the FIFO-first lot");
            Assert.AreEqual(1, stock.UnitsOnHand("bed-lot-1"), "the first lot is untouched — provenance names the lot that was debited");
            Assert.AreEqual(1, stock.UnitsOnHand("bed-lot-2"));
            Assert.AreEqual(2, storeRuntime.GetCategoryStock(FurnitureStorePurchase.FurnitureCategoryId).CurrentStockUnits, "the category drops by the same unit");
        }

        [Test]
        public void Adapter_TryDebit_Shortfall_Refuses_NothingMoved()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            var stock = new StoreFurnitureVendorStock(storeRuntime, new List<string>());
            Assert.IsNotNull(stock.TryDebit("bed-lot-1", 2));
            Assert.AreEqual(1, stock.UnitsOnHand("bed-lot-1"), "nothing moved on a shortfall");
            Assert.AreEqual(1, storeRuntime.GetCategoryStock(FurnitureStorePurchase.FurnitureCategoryId).CurrentStockUnits);
            Assert.IsNotNull(stock.TryDebit("bed-lot-9", 1), "a phantom lot refuses");
        }

        [Test]
        public void Adapter_TryCredit_LogsAndRefuses()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            var diag = new List<string>();
            var stock = new StoreFurnitureVendorStock(storeRuntime, diag);
            Assert.IsNull(stock.TryDebit("bed-lot-1", 1));
            Assert.IsNotNull(stock.TryCredit("bed-lot-1", 1), "no lot-level restore API exists — the rollback refuses loudly");
            Assert.AreEqual(0, stock.UnitsOnHand("bed-lot-1"), "the stock stays debited — never silently re-added");
            Assert.AreEqual(1, diag.Count, "the refusal is recorded, not silent");
            StringAssert.Contains("no lot-level restore API exists", diag[0]);
        }

        // --- Order builder ---

        [Test]
        public void BuildOrder_TwoUnits_TwoLines_FifoOrder()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            AddLot("bed-lot-2", FurnitureItemSpecs.BedId, 1);
            string refusal = FurnitureStorePurchase.TryBuildOrder(storeRuntime, QuotePrice,
                FurnitureItemSpecs.BedId, 2, "purchase-1", BuyerHousehold, BuildingInstance, DayIndex, 45,
                out FurniturePurchaseOrder order);
            Assert.IsNull(refusal, refusal);
            Assert.NotNull(order);
            Assert.AreEqual(FurniturePurchaseKind.New, order.Kind);
            Assert.AreEqual(StoreId, order.VendorId, "the vendor is the store runtime's own business id");
            Assert.AreEqual(2, order.Lines.Count, "one line per lot, per-lot caps");
            Assert.AreEqual("bed-lot-1", order.Lines[0].SourceLotId, "oldest lot first");
            Assert.AreEqual(1, order.Lines[0].Units);
            Assert.AreEqual("bed-lot-2", order.Lines[1].SourceLotId);
            Assert.AreEqual(1, order.Lines[1].Units);
            Assert.AreEqual(294, order.Lines[0].UnitPriceCents, "the live F2 cost-plus price");
            Assert.AreEqual(588, order.GoodsTotalCents());
            Assert.AreEqual(633, order.TotalCents(), "goods 588 + freight 45");
        }

        [Test]
        public void BuildOrder_Shortfall_Refuses()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 2);
            string refusal = FurnitureStorePurchase.TryBuildOrder(storeRuntime, QuotePrice,
                FurnitureItemSpecs.BedId, 3, "purchase-1", BuyerHousehold, BuildingInstance, DayIndex, 45,
                out FurniturePurchaseOrder order);
            Assert.IsNotNull(refusal);
            Assert.IsNull(order);
            StringAssert.Contains("shortfall", refusal);
        }

        [Test]
        public void BuildOrder_UnknownKind_Refuses()
        {
            string refusal = FurnitureStorePurchase.TryBuildOrder(storeRuntime, QuotePrice,
                "furniture.throne", 1, "purchase-1", BuyerHousehold, BuildingInstance, DayIndex, 45,
                out FurniturePurchaseOrder order);
            Assert.IsNotNull(refusal);
            Assert.IsNull(order);
        }

        [Test]
        public void BuildOrder_ZeroPrice_Refuses()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            string refusal = FurnitureStorePurchase.TryBuildOrder(storeRuntime, _ => 0,
                FurnitureItemSpecs.BedId, 1, "purchase-1", BuyerHousehold, BuildingInstance, DayIndex, 45,
                out FurniturePurchaseOrder order);
            Assert.IsNotNull(refusal, "a zero live price is free inventory, never a purchase");
            Assert.IsNull(order);
        }

        // --- Delivery quote ---

        [Test]
        public void QuoteDeliveryCharge_Bed_TenMiles_45c()
        {
            bool quoted = FurnitureStorePurchase.TryQuoteDeliveryCharge(1, FurnitureItemSpecs.BedId, 10f,
                Def016Card(), wagon, 1f, out FreightServiceQuote quote, out string reason);
            Assert.IsTrue(quoted, reason);
            Assert.NotNull(quote);
            Assert.AreEqual(45, quote.TotalPriceCents, "25 base + 20 round-trip miles at 1c/mile — the F4 fare");
            Assert.AreEqual(1, quote.TripCount, "one bed fits one wagon, one trip");
        }

        [Test]
        public void QuoteDeliveryCharge_NineBeds_TwoTrips_90c()
        {
            bool quoted = FurnitureStorePurchase.TryQuoteDeliveryCharge(9, FurnitureItemSpecs.BedId, 10f,
                Def016Card(), wagon, 1f, out FreightServiceQuote quote, out string reason);
            Assert.IsTrue(quoted, reason);
            Assert.AreEqual(2, quote.TripCount, "9 beds need two wagon loads (8 per trip)");
            Assert.AreEqual(90, quote.TotalPriceCents, "two trips at 45c");
        }

        [Test]
        public void QuoteDeliveryCharge_TooBigForWagon_Refuses()
        {
            var tinyWagon = new EquipmentAsset
            {
                AssetId = "tiny-wagon", Kind = "wagon", DisplayName = "Tiny wagon",
                Condition01 = 1f, PayloadMassCapacityKg = 10f, CargoVolumeCapacityM3 = 0.1f,
            };
            bool quoted = FurnitureStorePurchase.TryQuoteDeliveryCharge(1, FurnitureItemSpecs.BedId, 10f,
                Def016Card(), tinyWagon, 1f, out FreightServiceQuote quote, out string reason);
            Assert.IsFalse(quoted, "a wagon that cannot carry even one bed has no trip to quote");
            Assert.IsNull(quote);
            Assert.IsNotNull(reason);
        }

        [Test]
        public void QuoteDeliveryCharge_UnknownKind_Refuses()
        {
            bool quoted = FurnitureStorePurchase.TryQuoteDeliveryCharge(1, "furniture.throne", 10f,
                Def016Card(), wagon, 1f, out FreightServiceQuote quote, out string reason);
            Assert.IsFalse(quoted);
            Assert.IsNull(quote);
        }

        [Test]
        public void QuoteDeliveryCharge_ZeroMiles_Refuses()
        {
            bool quoted = FurnitureStorePurchase.TryQuoteDeliveryCharge(1, FurnitureItemSpecs.BedId, 0f,
                Def016Card(), wagon, 1f, out FreightServiceQuote quote, out string reason);
            Assert.IsFalse(quoted, "a zero-mile route is a data defect, not a free delivery");
            Assert.IsNull(quote);
        }

        // --- Custody-piece leg ---

        [Test]
        public void InstallLeg_WorkshopPiece_InstallsManufacture()
        {
            BuildingEquipmentPlacement placement = MakePlacement("Bed", building, "furniture.bed", "plc-bed");
            placements.Add(placement);
            EquipmentAsset piece = MakeAsset("bed-made-1", FurnitureItemSpecs.BedId, BuyerHousehold, "carpenter-1");
            Assert.IsNull(buyerRegister.TryRegisterAsset(piece));
            var diag = new List<string>();
            int installed = FurnitureInstallBinding.TryInstallCustodyPieces(buyerRegister, building,
                BuildingInstance, placements, PlacementIsBound, diag);
            Assert.AreEqual(1, installed);
            FurnishingPlacementBinding binding = buyerRegister.LiveBindingFor("bed-made-1");
            Assert.NotNull(binding);
            Assert.AreEqual(FurnishingAcquisitionSource.Manufacture, binding.AcquisitionSource);
            Assert.AreEqual("carpenter-1", binding.AcquisitionSourceId, "the source id is the maker business");
            Assert.AreEqual(FurnishingCapabilityKind.SleepingPlace, binding.CapabilityKind);
            Assert.AreEqual("building:" + BuildingInstance, piece.LocationId);
        }

        [Test]
        public void InstallLeg_SkipsNonWorkshop_Reserved_Unusable_AlreadyInstalled()
        {
            BuildingEquipmentPlacement bedPlacement = MakePlacement("Bed", building, "furniture.bed", "plc-bed");
            placements.Add(bedPlacement);
            BuildingEquipmentPlacement tablePlacement = MakePlacement("Table", building, "furniture.table", "plc-table");
            placements.Add(tablePlacement);
            EquipmentAsset workshopBed = MakeAsset("bed-made-1", FurnitureItemSpecs.BedId, BuyerHousehold, "carpenter-1");
            EquipmentAsset storeBed = MakeAsset("bed-bought-1", FurnitureItemSpecs.BedId, BuyerHousehold);
            EquipmentAsset reservedChair = MakeAsset("chair-made-1", FurnitureItemSpecs.ChairId, BuyerHousehold, "carpenter-1", 1f, "another-task");
            EquipmentAsset wreckedTable = MakeAsset("table-made-1", FurnitureItemSpecs.TableId, BuyerHousehold, "carpenter-1", 0.01f);
            EquipmentAsset installedShelf = MakeAsset("shelf-made-1", FurnitureItemSpecs.ShelfId, BuyerHousehold, "carpenter-1");
            foreach (EquipmentAsset asset in new[] { workshopBed, storeBed, reservedChair, wreckedTable, installedShelf })
            {
                Assert.IsNull(buyerRegister.TryRegisterAsset(asset));
            }
            BuildingEquipmentPlacement shelfPlacement = MakePlacement("Shelf", building, "furniture.shelf", "plc-shelf");
            placements.Add(shelfPlacement);
            Assert.IsNull(buyerRegister.TryInstall(installedShelf.AssetId, shelfPlacement, building, BuildingInstance, 0,
                FurnishingCapabilityKind.StorageZone, string.Empty, FurnishingAcquisitionSource.Manufacture,
                "carpenter-1", out FurnishingPlacementBinding _), "the shelf starts installed");
            var diag = new List<string>();
            int installed = FurnitureInstallBinding.TryInstallCustodyPieces(buyerRegister, building,
                BuildingInstance, placements, PlacementIsBound, diag);
            Assert.AreEqual(1, installed, "only the workshop bed installs");
            Assert.NotNull(buyerRegister.LiveBindingFor("bed-made-1"));
            Assert.IsNull(buyerRegister.LiveBindingFor("bed-bought-1"), "a store-bought custody piece is not the workshop leg's — its install is the F4 handler's job");
            Assert.IsNull(buyerRegister.LiveBindingFor("chair-made-1"), "a reserved piece is never installed out from under its reservation");
            Assert.IsNull(buyerRegister.LiveBindingFor("table-made-1"), "a wrecked piece is never installed");
            Assert.AreEqual("household:" + BuyerHousehold, storeBed.LocationId, "the skipped pieces stay in custody");
        }

        // --- End to end ---

        [Test]
        public void EndToEnd_StorePurchase_Bed()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            BuildingEquipmentPlacement placement = MakePlacement("Bed", building, "furniture.bed", "plc-bed");
            placements.Add(placement);
            var diag = new List<string>();
            FurniturePurchaseOrder order = purchaseDriver.TryPurchaseForHousehold(buyerLedger, buyerRegister,
                BuyerHousehold, BuildingInstance, building, FurnitureItemSpecs.BedId, 45, diag);
            Assert.NotNull(order, "the purchase must complete: " + string.Join(" | ", diag));
            Assert.IsTrue(order.IsOperational, "every line reaches Operational");
            Assert.AreEqual("furn-store-7-furniture.bed-42", order.PurchaseId, "the deterministic purchase id");
            Assert.AreEqual(294, order.GoodsTotalCents());
            Assert.AreEqual(339, order.TotalCents(), "goods 294 + freight 45");

            Assert.AreEqual(40000 - 339, buyerLedger.GetBalanceCents(), "buyer pays goods + freight once");
            Assert.AreEqual(2, buyerLedger.Entries.Count, "endowment + one purchase outflow, nothing else");
            Assert.AreEqual(5000 + 294, storeRuntime.CurrentCashCents, "the store ends with the goods money");
            Assert.AreEqual(45, carrierRuntime.CurrentCashCents, "the carrier ends with the one-trip fare");
            Assert.AreEqual(0, storeRuntime.FindRetailItemLot("bed-lot-1").AvailableUnits, "the lot debited exactly once");

            Assert.AreEqual(1, order.ReceivedAssets.Count);
            EquipmentAsset asset = order.ReceivedAssets[0];
            Assert.AreEqual("furn-furn-store-7-furniture.bed-42-0-0", asset.AssetId);
            Assert.AreEqual("household", asset.OwnerKind, "buyer-owned");
            Assert.AreEqual(BuyerHousehold.ToString(), asset.OwnerId);
            Assert.AreEqual("building:" + BuildingInstance, asset.LocationId, "installed in the buyer's building");
            Assert.AreEqual(string.Empty, asset.MadeByBusinessId, "a store-bought piece has no maker");
            Assert.AreEqual(1, asset.MaterialLotIds.Count, "provenance is the source lot");
            Assert.AreEqual("bed-lot-1", asset.MaterialLotIds[0]);
            Assert.AreSame(asset, buyerRegister.FindAsset(asset.AssetId), "the received asset is registered");
            FurnishingPlacementBinding binding = buyerRegister.LiveBindingFor(asset.AssetId);
            Assert.NotNull(binding);
            Assert.AreEqual(FurnishingCapabilityKind.SleepingPlace, binding.CapabilityKind);
            Assert.AreEqual(FurnishingAcquisitionSource.Purchase, binding.AcquisitionSource);
            Assert.AreEqual(order.PurchaseId, binding.AcquisitionSourceId);
            Assert.AreEqual(placement.PlacementId, binding.PlacementId, "the binding names the free placement");

            Assert.AreEqual(1, freight.TrackedShipments.Count);
            LogisticsShipmentState shipment = freight.TrackedShipments[0];
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.IsTrue(shipment.IsConsolidated);
            Assert.IsTrue(shipment.deliveryApplied);
            Assert.IsTrue(shipment.sourceLotDebitApplied);
            Assert.AreEqual(1, shipment.DeliveredQuantityUnits);
        }

        [Test]
        public void EndToEnd_NoPlacement_InstallRefuses_PurchaseStaysReceived()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            var diag = new List<string>();
            FurniturePurchaseOrder order = purchaseDriver.TryPurchaseForHousehold(buyerLedger, buyerRegister,
                BuyerHousehold, BuildingInstance, building, FurnitureItemSpecs.BedId, 45, diag);
            Assert.IsNull(order, "no placement, no install — the purchase cannot complete: " + string.Join(" | ", diag));
            Assert.AreEqual(40000 - 339, buyerLedger.GetBalanceCents(), "the goods are paid for before the install refuses — the purchase stays Received, never falsely marked installed");
            Assert.AreEqual(1, buyerRegister.Assets.Count, "the received asset stays registered in custody");
            Assert.IsNull(buyerRegister.LiveBindingFor(buyerRegister.Assets[0].AssetId), "nothing installed");
            Assert.AreEqual(0, storeRuntime.FindRetailItemLot("bed-lot-1").AvailableUnits, "the lot was debited at load, as F4 specifies");
            Assert.AreEqual(1, freight.TrackedShipments.Count, "the shipment completed — the install is the only refused stage");
        }

        private FurnitureStorePurchaseDriver NewStagedDriver(List<FurniturePurchaseOrder> orders,
            out FreightProductionDriver freightOwner)
        {
            freightOwner = new FreightProductionDriver(freight);
            freightOwner.EnsureCompany(CarrierId, carrierRuntime);
            return new FurnitureStorePurchaseDriver(storeRuntime, QuotePrice, carrierCash, freight,
                "opening-freight-wagon", loader, driver, null, () => placements, PlacementIsBound,
                hooksTemplate, DayIndex, true, orders, freightOwner, CarrierId, carrierRuntime);
        }

        private FurniturePurchaseOrder StartStagedBedOrder(List<FurniturePurchaseOrder> orders,
            out FurnitureStorePurchaseDriver stagedDriver, out FreightProductionDriver freightOwner)
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            stagedDriver = NewStagedDriver(orders, out freightOwner);
            return stagedDriver.TryPurchaseForHousehold(buyerLedger, buyerRegister,
                BuyerHousehold, BuildingInstance, building, FurnitureItemSpecs.BedId, 45, new List<string>());
        }

        private void AdvanceFreightUntilTerminal(FurniturePurchaseOrder order, FreightProductionDriver owner)
        {
            LogisticsShipmentState shipment = owner.AllShipments().Find(item => item != null && item.ShipmentId == order.ShipmentId);
            Assert.NotNull(shipment, "purchase shipment must be enrolled in the carrier production authority");
            var diagnostics = new List<string>();
            FreightShipmentHooks hooks = order.FreightHooks;
            Assert.NotNull(hooks);
            for (int day = DayIndex + 1; day <= DayIndex + 15 && shipment.Status != LogisticsShipmentStatus.Completed
                && shipment.Status != LogisticsShipmentStatus.Failed; day++)
            {
                budgets.EnsureDay(day);
                hooks.DayIndex = day;
                owner.AdvanceTick(86400f, hooks, loader, diagnostics);
            }
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status,
                "freight must physically progress over repeated game-time ticks: " + string.Join(" | ", diagnostics));
        }

        [Test]
        public void StagedOrder_IsPaidOnce_AndWaitsForCarrierOverGameTime()
        {
            var orders = new List<FurniturePurchaseOrder>();
            FurniturePurchaseOrder order = StartStagedBedOrder(orders, out _, out FreightProductionDriver owner);

            Assert.NotNull(order);
            Assert.AreEqual(EquipmentPurchaseStage.Shipped, order.Lines[0].Stage);
            Assert.AreEqual(1, orders.Count, "durable order identity is recorded before physical transport");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, owner.AllShipments()[0].Status,
                "purchase does not synchronously load, travel, unload, or install");
            Assert.AreEqual(40000 - 339, buyerLedger.GetBalanceCents(), "goods and freight are charged once");
            Assert.AreEqual(1, storeRuntime.FindRetailItemLot("bed-lot-1").AvailableUnits,
                "seller stock remains until the freight loading task commits it");
            Assert.AreEqual(0, buyerRegister.Assets.Count, "no household receipt before delivery");
        }

        [Test]
        public void StagedOrder_RepeatedStableIdentityCannotDoubleChargeOrCreateShipment()
        {
            var orders = new List<FurniturePurchaseOrder>();
            FurniturePurchaseOrder order = StartStagedBedOrder(orders,
                out FurnitureStorePurchaseDriver staged, out FreightProductionDriver owner);
            int paidBalance = buyerLedger.GetBalanceCents();
            FurniturePurchaseOrder repeated = staged.TryPurchaseForHousehold(buyerLedger, buyerRegister,
                BuyerHousehold, BuildingInstance, building, FurnitureItemSpecs.BedId, 45,
                new List<string>());

            Assert.AreSame(order, repeated, "the stable purchase identity resolves to the saved order");
            Assert.AreEqual(1, orders.Count);
            Assert.AreEqual(1, owner.AllShipments().Count);
            Assert.AreEqual(paidBalance, buyerLedger.GetBalanceCents(), "a repeated call does not charge again");
            Assert.AreEqual(1, storeRuntime.FindRetailItemLot("bed-lot-1").AvailableUnits,
                "seller stock remains until the one shipment's physical loading task commits it");
        }

        [Test]
        public void StagedOrder_CompletesFreightThenWaitsForInstallationWorkAtAuthoredPlacement()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            BuildingEquipmentPlacement placement = MakePlacement("Bed", building, "furniture.bed", "plc-staged-bed");
            placements.Add(placement);
            var orders = new List<FurniturePurchaseOrder>();
            FurnitureStorePurchaseDriver staged = NewStagedDriver(orders, out FreightProductionDriver owner);
            FurniturePurchaseOrder order = staged.TryPurchaseForHousehold(buyerLedger, buyerRegister,
                BuyerHousehold, BuildingInstance, building, FurnitureItemSpecs.BedId, 45, new List<string>());

            Assert.NotNull(order);
            AdvanceFreightUntilTerminal(order, owner);
            Assert.IsNull(staged.ReconcilePendingOrder(order, buyerLedger, buyerRegister, building,
                BuildingInstance, new List<string>()));
            Assert.IsFalse(order.IsOperational, "freight receipt is not a substitute for household installation work");
            Assert.AreEqual(1, buyerRegister.Assets.Count);
            Assert.IsNull(buyerRegister.LiveBindingFor(buyerRegister.Assets[0].AssetId));
            Assert.AreEqual("household:" + BuyerHousehold, buyerRegister.Assets[0].LocationId,
                "the delivered item remains physically represented in household custody");
            StringAssert.Contains("No production furnishing.install task outcome is wired", order.InstallationBlockedReason);
            Assert.AreEqual(40000 - 339, buyerLedger.GetBalanceCents(), "reconciliation does not duplicate payment");
            Assert.AreEqual(1, owner.AllShipments().Count, "shipment identity survives the complete lifecycle");
        }

        [Test]
        public void DelayedOrder_RemainsDurable_AndDoesNotCreateSecondShipmentOrPayment()
        {
            var orders = new List<FurniturePurchaseOrder>();
            FurniturePurchaseOrder order = StartStagedBedOrder(orders, out FurnitureStorePurchaseDriver staged, out FreightProductionDriver owner);
            LogisticsShipmentState shipment = owner.AllShipments()[0];
            freight.DelayShipment(shipment, "weather delay", order.FreightHooks, LogisticsShipmentStatus.Planned);
            int balanceAfterPayment = buyerLedger.GetBalanceCents();
            string refusal = staged.ReconcilePendingOrder(order, buyerLedger, buyerRegister, building,
                BuildingInstance, new List<string>());

            Assert.IsNull(refusal);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, "furniture service does not bypass freight's delay authority");
            Assert.AreEqual(1, owner.AllShipments().Count, "a delay never causes duplicate shipment creation");
            Assert.AreEqual(balanceAfterPayment, buyerLedger.GetBalanceCents(), "reconciliation never charges again");
            Assert.IsFalse(order.IsOperational);
        }

        [Test]
        public void NoAuthoredPlacement_LeavesReceivedFurnitureInCustodyWithBlocker()
        {
            var orders = new List<FurniturePurchaseOrder>();
            FurniturePurchaseOrder order = StartStagedBedOrder(orders, out FurnitureStorePurchaseDriver staged, out FreightProductionDriver owner);
            AdvanceFreightUntilTerminal(order, owner);
            string refusal = staged.ReconcilePendingOrder(order, buyerLedger, buyerRegister, building,
                BuildingInstance, new List<string>());

            Assert.IsNull(refusal, "lack of a placement is a delivery success with a durable installation blocker");
            Assert.AreEqual(1, buyerRegister.Assets.Count);
            Assert.IsNull(buyerRegister.LiveBindingFor(buyerRegister.Assets[0].AssetId));
            Assert.AreEqual("household:" + BuyerHousehold, buyerRegister.Assets[0].LocationId);
            StringAssert.Contains("no free placement", order.InstallationBlockedReason);
            string blocker = order.InstallationBlockedReason;
            staged.ReconcilePendingOrder(order, buyerLedger, buyerRegister, building, BuildingInstance, new List<string>());
            Assert.AreEqual(blocker, order.InstallationBlockedReason, "the unavailable placement is observed without repeating or mutating the attempt");
            Assert.AreEqual(1, buyerRegister.Assets.Count, "reconciliation cannot duplicate the delivered item");
        }

        [Test]
        public void UnpaidOrder_CanBeCancelledWithoutGoodsOrMoneyMovement()
        {
            AddLot("bed-lot-1", FurnitureItemSpecs.BedId, 1);
            FurnitureStorePurchase.TryBuildOrder(storeRuntime, QuotePrice, FurnitureItemSpecs.BedId, 1,
                "furn-store-cancel-before-pay", BuyerHousehold, BuildingInstance, DayIndex, 45,
                out FurniturePurchaseOrder order);
            var service = new FurniturePurchaseService(buyerLedger,
                new StoreFurnitureVendorStock(storeRuntime, new List<string>()),
                (int goods, int fare, string id) => null, (asset, id) => null, null);
            Assert.IsNull(service.Quote(order));
            Assert.IsNull(service.TryOrder(order));
            int balance = buyerLedger.GetBalanceCents();
            Assert.IsNull(service.CancelUnpaidOrder(order));
            Assert.IsTrue(order.CancellationRequested);
            Assert.AreEqual(balance, buyerLedger.GetBalanceCents());
            Assert.AreEqual(1, storeRuntime.FindRetailItemLot("bed-lot-1").AvailableUnits);
        }

        [Test]
        public void FailedDelivery_IsTerminalInFreightAndDoesNotCreditHouseholdOrRefundSilently()
        {
            var orders = new List<FurniturePurchaseOrder>();
            FurniturePurchaseOrder order = StartStagedBedOrder(orders, out FurnitureStorePurchaseDriver staged, out FreightProductionDriver owner);
            LogisticsShipmentState shipment = owner.AllShipments()[0];
            freight.FailAbandonedShipment(shipment, "test terminal failure", order.FreightHooks);
            int paidBalance = buyerLedger.GetBalanceCents();
            Assert.IsNull(staged.ReconcilePendingOrder(order, buyerLedger, buyerRegister, building,
                BuildingInstance, new List<string>()));

            Assert.IsFalse(order.IsOperational);
            StringAssert.Contains("test terminal failure", order.TerminalReason);
            Assert.AreEqual(0, buyerRegister.Assets.Count, "failed, undelivered cargo is not credited as household furniture");
            Assert.AreEqual(paidBalance, buyerLedger.GetBalanceCents(), "failure does not invent a refund or charge");
            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
        }

        [Test]
        public void Pass9SaveContinue_RestoresOrderShipmentAndExactlyOncePayment()
        {
            var orders = new List<FurniturePurchaseOrder>();
            FurniturePurchaseOrder order = StartStagedBedOrder(orders, out _, out FreightProductionDriver owner);
            var source = new Series2Pass9SaveState
            {
                FreightDriver = owner,
                FurniturePurchaseOrders = orders,
                HouseholdFurnishingRegisters = new List<HouseholdFurnishingRegister> { buyerRegister },
            };
            Series2Pass9SaveDto dto = source.Capture(new List<string>());
            var restoredOwner = new FreightProductionDriver(new FreightShipmentService());
            restoredOwner.EnsureCompany(CarrierId, carrierRuntime);
            var restored = new Series2Pass9SaveState
            {
                FreightDriver = restoredOwner,
                HouseholdFurnishingRegisters = new List<HouseholdFurnishingRegister>(),
            };
            restored.Restore(dto, null, new List<string>());

            Assert.AreEqual(1, restored.FurniturePurchaseOrders.Count);
            Assert.AreEqual(order.PurchaseId, restored.FurniturePurchaseOrders[0].PurchaseId);
            Assert.AreEqual(order.ShipmentId, restored.FurniturePurchaseOrders[0].ShipmentId);
            Assert.AreEqual(EquipmentPurchaseStage.Shipped, restored.FurniturePurchaseOrders[0].Lines[0].Stage);
            Assert.AreEqual(1, restoredOwner.AllShipments().Count);
            Assert.AreEqual(40000 - 339, buyerLedger.GetBalanceCents(), "saving or restoring cannot charge again");
        }

        [Test]
        public void Pass9SaveContinue_AfterReceiptRetainsFurnitureAndInstallationBlocker()
        {
            var orders = new List<FurniturePurchaseOrder>();
            FurniturePurchaseOrder order = StartStagedBedOrder(orders, out FurnitureStorePurchaseDriver staged,
                out FreightProductionDriver owner);
            AdvanceFreightUntilTerminal(order, owner);
            Assert.IsNull(staged.ReconcilePendingOrder(order, buyerLedger, buyerRegister, building,
                BuildingInstance, new List<string>()));
            Assert.AreEqual(1, buyerRegister.Assets.Count);
            Assert.IsFalse(order.IsOperational);
            int paidBalance = buyerLedger.GetBalanceCents();

            var source = new Series2Pass9SaveState
            {
                FreightDriver = owner,
                FurniturePurchaseOrders = orders,
                HouseholdFurnishingRegisters = new List<HouseholdFurnishingRegister> { buyerRegister },
            };
            Series2Pass9SaveDto dto = source.Capture(new List<string>());
            var restoredOwner = new FreightProductionDriver(new FreightShipmentService());
            restoredOwner.EnsureCompany(CarrierId, carrierRuntime);
            var restored = new Series2Pass9SaveState
            {
                FreightDriver = restoredOwner,
                HouseholdFurnishingRegisters = new List<HouseholdFurnishingRegister>(),
            };
            restored.Restore(dto, null, new List<string>());

            FurniturePurchaseOrder restoredOrder = restored.FurniturePurchaseOrders[0];
            Assert.AreEqual(EquipmentPurchaseStage.Received, restoredOrder.Lines[0].Stage);
            Assert.AreEqual(1, restoredOrder.ReceivedAssets.Count, "receipt stays exactly once in household custody");
            Assert.AreEqual("household:" + BuyerHousehold, restoredOrder.ReceivedAssets[0].LocationId);
            Assert.AreEqual(order.InstallationBlockedReason, restoredOrder.InstallationBlockedReason);
            Assert.IsFalse(restoredOrder.IsOperational, "loading never fabricates the installation stage");
            Assert.AreEqual(paidBalance, buyerLedger.GetBalanceCents(), "restoration does not charge again");
            Assert.AreEqual(1, restoredOwner.AllShipments().Count, "shipment identity remains singular after receipt");
        }
    }
}
