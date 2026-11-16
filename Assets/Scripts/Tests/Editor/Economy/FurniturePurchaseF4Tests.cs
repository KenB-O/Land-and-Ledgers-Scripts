// Land & Ledgers — F4 (2026-11-16, slice: household furniture purchase + delivery end to end).
// A staged purchase — quote -> order -> pay -> ship -> deliver -> receive -> install -> operational —
// moves cash exactly once through the HouseholdLedger, ships as ONE F3 consolidated shipment
// (bed + table + 4 chairs = 93 kg / 1.45 m3, one trip), and installs only after receipt.
// The vendor quote comes from the F2 cost-plus price (RetailPricingDefinition.GetCostPlusPriceCents
// with the furniture category markup 1.4): bed 294, table 221, chair 123. The freight fare is the
// caller-supplied DEF-016 one-trip quote (25c base + 1c/mile round trip; the test trip is 10 miles
// one way, 20 round-trip miles, so 25 + 20 = 45c) — F4 never invents a freight price.
// The install handler is a test double for B0's register: it records installs and sets the
// building location, the way the production binding (G) will through HouseholdFurnishingRegister.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Primitives;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.GenericBusiness;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public class FurniturePurchaseF4Tests
    {
        private const int BuyerHousehold = 7;
        private const int SellerHousehold = 9;
        private const int BuildingInstance = 12;
        private const int FreightFareCents = 45; // DEF-016 provisional card: 25 base + 20 round-trip miles at 1c/mile.
        private const float FurnitureCategoryMarkup = 1.4f;

        private FreightShipmentService freight;
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FreightShipmentHooks hooks;
        private EntityId loader;
        private EntityId driver;
        private EquipmentAsset wagon;
        private HouseholdLedger buyerLedger;
        private HouseholdLedger sellerLedger;
        private TestVendorStock vendorStock;
        private int storeCash;
        private int carrierCash;
        private bool refuseProceeds;
        private List<string> installs;
        private int refuseInstallAt = -1;
        private Dictionary<string, EquipmentAsset> assetLookup;
        private Dictionary<string, FurniturePurchaseOrder> ordersById;
        private FurniturePurchaseService service;

        private sealed class TestVendorStock : IFurnitureVendorStock
        {
            public Dictionary<string, int> Lots = new Dictionary<string, int>();

            public int UnitsOnHand(string lotId)
            {
                return Lots.TryGetValue(lotId ?? string.Empty, out int units) ? units : 0;
            }

            public string TryDebit(string lotId, int units)
            {
                if (UnitsOnHand(lotId) < units) return $"shortfall on lot '{lotId}'";
                Lots[lotId] -= units;
                return null;
            }

            public string TryCredit(string lotId, int units)
            {
                if (!Lots.ContainsKey(lotId)) Lots[lotId] = 0;
                Lots[lotId] += units;
                return null;
            }
        }

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
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
            Assert.IsNull(buyerLedger.RecordInflow(42, 40000, HouseholdIncomeSource.OwnerContribution,
                "test-endowment", "test opening cash", "test"));
            sellerLedger = new HouseholdLedger(SellerHousehold);
            Assert.IsNull(sellerLedger.RecordInflow(42, 1000, HouseholdIncomeSource.OwnerContribution,
                "test-endowment", "test opening cash", "test"));

            vendorStock = new TestVendorStock();
            storeCash = 0;
            carrierCash = 0;
            refuseProceeds = false;
            installs = new List<string>();
            refuseInstallAt = -1;
            assetLookup = new Dictionary<string, EquipmentAsset>();
            ordersById = new Dictionary<string, FurniturePurchaseOrder>();

            hooks = new FreightShipmentHooks
            {
                DayIndex = 42,
                Tasks = tasks,
                Budgets = budgets,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    return quantityUnits;
                },
            };

            service = new FurniturePurchaseService(
                buyerLedger,
                vendorStock,
                (int goodsCents, int freightCents, string purchaseId) =>
                {
                    if (refuseProceeds) return "proceeds refused by test";
                    if (goodsCents < 0 || freightCents < 0) return "proceeds cannot be negative";
                    FurniturePurchaseOrder order = null;
                    ordersById.TryGetValue(purchaseId ?? string.Empty, out order);
                    if (order != null && order.Kind == FurniturePurchaseKind.Secondhand)
                    {
                        string inflowRefusal = sellerLedger.RecordInflow(42, goodsCents,
                            HouseholdIncomeSource.SaleProceeds, purchaseId,
                            $"furniture sale {purchaseId}", "household:" + BuyerHousehold);
                        if (inflowRefusal != null) return inflowRefusal;
                    }
                    else
                    {
                        storeCash += goodsCents;
                    }

                    carrierCash += freightCents;
                    return null;
                },
                (EquipmentAsset asset, string purchaseId) =>
                {
                    if (refuseInstallAt >= 0 && installs.Count == refuseInstallAt) return "install refused by test";
                    asset.LocationId = "building:" + BuildingInstance;
                    installs.Add(asset.AssetId);
                    return null;
                },
                lotId => assetLookup.TryGetValue(lotId ?? string.Empty, out EquipmentAsset asset) ? asset : null);
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
                default:
                    return -1;
            }
        }

        private FurniturePurchaseOrder NewOrder(string purchaseId)
        {
            vendorStock.Lots["bed-lot-1"] = 1;
            vendorStock.Lots["table-lot-1"] = 1;
            vendorStock.Lots["chair-lot-1"] = 4;
            var order = new FurniturePurchaseOrder
            {
                PurchaseId = purchaseId,
                Kind = FurniturePurchaseKind.New,
                HouseholdId = BuyerHousehold,
                VendorId = "general-store",
                VendorName = "General Store",
                BuildingInstanceId = BuildingInstance,
                DayIndex = 42,
                FreightCents = FreightFareCents,
                Lines = new List<FurniturePurchaseLine>
                {
                    new FurniturePurchaseLine { ItemId = FurnitureItemSpecs.BedId, Units = 1, SourceLotId = "bed-lot-1", UnitPriceCents = QuotePrice(FurnitureItemSpecs.BedId) },
                    new FurniturePurchaseLine { ItemId = FurnitureItemSpecs.TableId, Units = 1, SourceLotId = "table-lot-1", UnitPriceCents = QuotePrice(FurnitureItemSpecs.TableId) },
                    new FurniturePurchaseLine { ItemId = FurnitureItemSpecs.ChairId, Units = 4, SourceLotId = "chair-lot-1", UnitPriceCents = QuotePrice(FurnitureItemSpecs.ChairId) },
                },
            };
            ordersById[purchaseId] = order;
            return order;
        }

        private EquipmentAsset UsedBed()
        {
            var asset = new EquipmentAsset
            {
                AssetId = "bed-used-1",
                Kind = FurnitureItemSpecs.BedId,
                DisplayName = "Bed",
                Condition01 = 0.8f,
                OwnerKind = "household",
                OwnerId = SellerHousehold.ToString(),
                LocationId = "household:" + SellerHousehold,
                MadeByBusinessId = "carpenter-1",
                MaterialLotIds = new List<string> { "bed-lot-old" },
                MadeDayIndex = 10,
            };
            assetLookup["bed-used-1"] = asset;
            return asset;
        }

        private FurniturePurchaseOrder SecondhandOrder(string purchaseId)
        {
            var order = new FurniturePurchaseOrder
            {
                PurchaseId = purchaseId,
                Kind = FurniturePurchaseKind.Secondhand,
                HouseholdId = BuyerHousehold,
                SellerHouseholdId = SellerHousehold,
                VendorId = "household:" + SellerHousehold,
                VendorName = "Seller household 9",
                BuildingInstanceId = BuildingInstance,
                DayIndex = 42,
                FreightCents = FreightFareCents,
                Lines = new List<FurniturePurchaseLine>
                {
                    new FurniturePurchaseLine { ItemId = FurnitureItemSpecs.BedId, Units = 1, SourceLotId = "bed-used-1", UnitPriceCents = 150 },
                },
            };
            ordersById[purchaseId] = order;
            return order;
        }

        private void DriveToPaid(FurniturePurchaseOrder order)
        {
            Assert.IsNull(service.Quote(order), "quote must pass");
            Assert.IsNull(service.TryOrder(order), "order must pass");
            Assert.IsNull(service.TryPay(order), "pay must pass");
        }

        private void DriveToReceived(FurniturePurchaseOrder order)
        {
            DriveToPaid(order);
            Assert.IsNull(service.TryShip(order, freight, "opening-freight-wagon", hooks), "ship must pass");
            Assert.IsNull(service.TryDeliver(order, freight, hooks, loader, driver, null), "deliver must pass");
        }

        [Test]
        public void Quote_VendorPrices_AreF2CostPlus()
        {
            Assert.AreEqual(294, QuotePrice(FurnitureItemSpecs.BedId), "bed: landed 210 x 1.4 = 294, inside 250-450");
            Assert.AreEqual(221, QuotePrice(FurnitureItemSpecs.TableId), "table: landed 158 x 1.4 = 221, inside 175-325");
            Assert.AreEqual(123, QuotePrice(FurnitureItemSpecs.ChairId), "chair: landed 88 x 1.4 = 123, inside 100-175");
        }

        [Test]
        public void NewPurchase_EndToEnd_BedTableFourChairs()
        {
            FurniturePurchaseOrder order = NewOrder("furn-buy-1");
            Assert.AreEqual(1007, order.GoodsTotalCents(), "294 + 221 + 4x123");
            Assert.AreEqual(1052, order.TotalCents(), "goods 1007 + freight 45");

            DriveToReceived(order);
            Assert.IsNull(service.TryInstall(order), "install must pass");
            Assert.IsTrue(order.IsOperational, "every line must reach Operational");
            Assert.IsTrue(order.AllLinesAt(EquipmentPurchaseStage.Operational));

            // Cash exactly once, conservation cent for cent.
            Assert.AreEqual(40000 - 1052, buyerLedger.GetBalanceCents(), "buyer pays goods + freight once");
            Assert.AreEqual(2, buyerLedger.Entries.Count, "endowment + one purchase outflow, nothing else");
            Assert.AreEqual(1007, storeCash, "seller ends with the goods money");
            Assert.AreEqual(45, carrierCash, "carrier ends with the one-trip fare");

            // Vendor stock debited exactly once per lot.
            Assert.AreEqual(0, vendorStock.UnitsOnHand("bed-lot-1"));
            Assert.AreEqual(0, vendorStock.UnitsOnHand("table-lot-1"));
            Assert.AreEqual(0, vendorStock.UnitsOnHand("chair-lot-1"));

            // One asset per UNIT (not one per line): 6 assets for bed + table + 4 chairs.
            Assert.AreEqual(6, order.ReceivedAssets.Count);
            Assert.AreEqual(6, order.Lines[0].AssetIds.Count + order.Lines[1].AssetIds.Count + order.Lines[2].AssetIds.Count);
            Assert.AreEqual("furn-furn-buy-1-0-0", order.Lines[0].AssetIds[0]);
            Assert.AreEqual("furn-furn-buy-1-1-0", order.Lines[1].AssetIds[0]);
            Assert.AreEqual("furn-furn-buy-1-2-0", order.Lines[2].AssetIds[0]);
            Assert.AreEqual("furn-furn-buy-1-2-3", order.Lines[2].AssetIds[3]);
            foreach (EquipmentAsset asset in order.ReceivedAssets)
            {
                Assert.AreEqual("household", asset.OwnerKind, "buyer-owned");
                Assert.AreEqual(BuyerHousehold.ToString(), asset.OwnerId);
                Assert.AreEqual("building:" + BuildingInstance, asset.LocationId, "installed in the buyer's building");
                Assert.AreEqual(string.Empty, asset.MadeByBusinessId, "maker unknown until O-F3 binds a producing business");
                Assert.AreEqual(1f, asset.Condition01);
                Assert.AreEqual(42, asset.MadeDayIndex);
                Assert.AreEqual(1, asset.MaterialLotIds.Count, "provenance is the source lot");
            }

            Assert.AreEqual("bed-lot-1", order.ReceivedAssets[0].MaterialLotIds[0]);
            Assert.AreEqual("chair-lot-1", order.ReceivedAssets[5].MaterialLotIds[0]);

            // Install exactly once per asset, in line order, only after receipt.
            Assert.AreEqual(6, installs.Count);
            Assert.AreEqual("furn-furn-buy-1-0-0", installs[0]);
            Assert.AreEqual("furn-furn-buy-1-2-3", installs[5]);

            // The shipment completed as one consolidated trip.
            Assert.AreEqual(1, freight.TrackedShipments.Count);
            LogisticsShipmentState shipment = freight.TrackedShipments[0];
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.IsTrue(shipment.IsConsolidated);
            Assert.IsTrue(shipment.deliveryApplied);
            Assert.IsTrue(shipment.sourceLotDebitApplied);
            Assert.AreEqual(6, shipment.DeliveredQuantityUnits);
        }

        [Test]
        public void Quote_UnknownItem_Refused()
        {
            FurniturePurchaseOrder order = NewOrder("furn-buy-unknown");
            order.Lines[0].ItemId = "furniture.throne";
            Assert.IsNotNull(service.Quote(order));
            StringAssert.Contains("unknown", service.Diagnostics[service.Diagnostics.Count - 1]);
        }

        [Test]
        public void Quote_ZeroPrice_Refused()
        {
            FurniturePurchaseOrder order = NewOrder("furn-buy-free");
            order.Lines[0].UnitPriceCents = 0;
            Assert.IsNotNull(service.Quote(order), "a zero price is free inventory, never a purchase");
        }

        [Test]
        public void Quote_DuplicatePurchaseId_Refused()
        {
            FurniturePurchaseOrder first = NewOrder("furn-buy-dup");
            Assert.IsNull(service.Quote(first));
            FurniturePurchaseOrder second = NewOrder("furn-buy-dup");
            Assert.IsNotNull(service.Quote(second), "purchase ids are unique per service");
        }

        [Test]
        public void Order_Unaffordable_Refused_NoCashMoves()
        {
            var poorLedger = new HouseholdLedger(BuyerHousehold);
            Assert.IsNull(poorLedger.RecordInflow(42, 100, HouseholdIncomeSource.OwnerContribution,
                "test-endowment", "test opening cash", "test"));
            var poorService = new FurniturePurchaseService(poorLedger, vendorStock,
                (int g, int f, string p) => null,
                (EquipmentAsset a, string p) => null,
                lotId => null);
            FurniturePurchaseOrder order = NewOrder("furn-buy-poor");
            Assert.IsNull(poorService.Quote(order));
            string refusal = poorService.TryOrder(order);
            Assert.IsNotNull(refusal, "1052c against 100c must refuse");
            StringAssert.Contains("insufficient funds", refusal);
            Assert.AreEqual(100, poorLedger.GetBalanceCents(), "nothing moved");
            Assert.AreEqual(1, poorLedger.Entries.Count, "endowment only");
            Assert.IsTrue(order.AllLinesAt(EquipmentPurchaseStage.Quoted), "lines stay Quoted");
            Assert.AreEqual(0, freight.TrackedShipments.Count, "no shipment was created");
        }

        [Test]
        public void Pay_Twice_Refused()
        {
            FurniturePurchaseOrder order = NewOrder("furn-buy-pay2");
            DriveToPaid(order);
            int balanceAfterPay = buyerLedger.GetBalanceCents();
            Assert.IsNotNull(service.TryPay(order), "a second pay is refused by the stage guard");
            Assert.AreEqual(balanceAfterPay, buyerLedger.GetBalanceCents(), "no second outflow");
            Assert.AreEqual(1007, storeCash);
            Assert.AreEqual(45, carrierCash);
        }

        [Test]
        public void Ship_BeforePay_Refused()
        {
            FurniturePurchaseOrder order = NewOrder("furn-buy-early-ship");
            Assert.IsNull(service.Quote(order));
            Assert.IsNull(service.TryOrder(order));
            Assert.IsNotNull(service.TryShip(order, freight, "opening-freight-wagon", hooks), "shipment follows payment");
            Assert.AreEqual(0, freight.TrackedShipments.Count, "no shipment was created");
        }

        [Test]
        public void Install_BeforeReceipt_Refused()
        {
            FurniturePurchaseOrder order = NewOrder("furn-buy-early-install");
            DriveToPaid(order);
            Assert.IsNotNull(service.TryInstall(order), "install follows receipt, never precedes it");
            Assert.AreEqual(0, installs.Count, "the install handler was never called");
        }

        [Test]
        public void Ship_OverCapacity_StaysPaid()
        {
            vendorStock.Lots["bed-lot-9"] = 9;
            var order = new FurniturePurchaseOrder
            {
                PurchaseId = "furn-buy-9beds",
                Kind = FurniturePurchaseKind.New,
                HouseholdId = BuyerHousehold,
                VendorId = "general-store",
                VendorName = "General Store",
                BuildingInstanceId = BuildingInstance,
                DayIndex = 42,
                FreightCents = FreightFareCents,
                Lines = new List<FurniturePurchaseLine>
                {
                    new FurniturePurchaseLine { ItemId = FurnitureItemSpecs.BedId, Units = 9, SourceLotId = "bed-lot-9", UnitPriceCents = QuotePrice(FurnitureItemSpecs.BedId) },
                },
            };
            ordersById["furn-buy-9beds"] = order;
            DriveToPaid(order);
            Assert.AreEqual(40000 - (9 * 294 + 45), buyerLedger.GetBalanceCents(), "the goods are paid for before shipping");
            string refusal = service.TryShip(order, freight, "opening-freight-wagon", hooks);
            Assert.IsNotNull(refusal, "9 beds = 4.5 m3 on a 4 m3 wagon must refuse pre-load");
            StringAssert.Contains("blocked pre-load", refusal);
            Assert.IsTrue(order.AllLinesAt(EquipmentPurchaseStage.PaidOrFinanced), "the purchase stays Paid — re-plan, never silently dropped");
            Assert.AreEqual(9, vendorStock.UnitsOnHand("bed-lot-9"), "no source stock may move on a refused manifest");
            Assert.AreEqual(0, order.ReceivedAssets.Count, "no assets were created");
            Assert.AreEqual(1, freight.TrackedShipments.Count, "the blocked record is tracked");
            Assert.AreEqual(LogisticsShipmentStatus.Failed, freight.TrackedShipments[0].Status);
        }

        [Test]
        public void Pay_SellerCreditRefused_RefundsBuyer()
        {
            refuseProceeds = true;
            FurniturePurchaseOrder order = NewOrder("furn-buy-nocredit");
            Assert.IsNull(service.Quote(order));
            Assert.IsNull(service.TryOrder(order));
            string refusal = service.TryPay(order);
            Assert.IsNotNull(refusal, "a refused credit must refuse the payment");
            Assert.AreEqual(40000, buyerLedger.GetBalanceCents(), "buyer refunded in full");
            Assert.AreEqual(3, buyerLedger.Entries.Count, "endowment + outflow + refund, nothing else");
            Assert.AreEqual(0, storeCash, "seller got nothing");
            Assert.AreEqual(0, carrierCash, "carrier got nothing");
            Assert.IsTrue(order.AllLinesAt(EquipmentPurchaseStage.Ordered), "purchase stays Ordered");
        }

        [Test]
        public void Deliver_ReceiveRefused_HoldsCargo()
        {
            EquipmentAsset usedBed = UsedBed();
            FurniturePurchaseOrder order = SecondhandOrder("furn-buy-reserved");
            DriveToPaid(order);
            usedBed.ReservedBy = "another-task";
            Assert.IsNull(service.TryShip(order, freight, "opening-freight-wagon", hooks), "ship must pass — the reservation is caught at load");
            string refusal = service.TryDeliver(order, freight, hooks, loader, driver, null);
            Assert.IsNotNull(refusal, "a reserved asset cannot be received");
            Assert.AreEqual(0, order.ReceivedAssets.Count, "no asset was transferred");
            Assert.AreEqual("household", usedBed.OwnerKind, "still the seller's asset");
            Assert.AreEqual(SellerHousehold.ToString(), usedBed.OwnerId);
            Assert.AreEqual(0, installs.Count, "nothing was installed");
            Assert.IsTrue(order.AllLinesAt(EquipmentPurchaseStage.Shipped), "purchase stays Shipped — cargo in wagon custody, never falsely delivered");
            LogisticsShipmentState shipment = freight.TrackedShipments[0];
            Assert.IsTrue(!shipment.deliveryApplied, "no delivery was applied");
        }

        [Test]
        public void Install_Refused_StaysReceived()
        {
            refuseInstallAt = 1;
            FurniturePurchaseOrder order = NewOrder("furn-buy-badinstall");
            DriveToReceived(order);
            string refusal = service.TryInstall(order);
            Assert.IsNotNull(refusal, "a refused install must stop the remaining installs");
            Assert.AreEqual(1, installs.Count, "only the first asset installed before the refusal");
            Assert.IsTrue(order.AllLinesAt(EquipmentPurchaseStage.Received), "purchase stays Received — never marked installed");
            Assert.IsTrue(!order.IsOperational);
            Assert.AreEqual(6, order.ReceivedAssets.Count, "the assets stay in buyer custody");
        }

        [Test]
        public void Secondhand_EndToEnd_TransfersAsset_NoCreation()
        {
            EquipmentAsset usedBed = UsedBed();
            FurniturePurchaseOrder order = SecondhandOrder("furn-buy-used");
            Assert.AreEqual(150, order.GoodsTotalCents());
            Assert.AreEqual(195, order.TotalCents(), "goods 150 + freight 45");

            DriveToReceived(order);
            Assert.IsNull(service.TryInstall(order), "install must pass");
            Assert.IsTrue(order.IsOperational);

            // Cash: buyer -> seller (goods) + carrier (fare), exactly once.
            Assert.AreEqual(40000 - 195, buyerLedger.GetBalanceCents());
            Assert.AreEqual(1000 + 150, sellerLedger.GetBalanceCents(), "seller ends with the goods money");
            Assert.AreEqual(45, carrierCash, "carrier ends with the one-trip fare");
            Assert.AreEqual(0, storeCash, "no new goods were sold");

            // No asset created: the SAME asset, new owner, provenance intact.
            Assert.AreEqual(1, order.ReceivedAssets.Count);
            Assert.AreSame(usedBed, order.ReceivedAssets[0]);
            Assert.AreEqual("bed-used-1", order.Lines[0].AssetIds[0]);
            Assert.AreEqual("household", usedBed.OwnerKind);
            Assert.AreEqual(BuyerHousehold.ToString(), usedBed.OwnerId);
            Assert.AreEqual("building:" + BuildingInstance, usedBed.LocationId, "installed in the buyer's building");
            Assert.AreEqual("carpenter-1", usedBed.MadeByBusinessId, "maker provenance survives the transfer");
            Assert.AreEqual(1, usedBed.OwnershipHistory.Count, "one transfer recorded");
            Assert.AreEqual(SellerHousehold.ToString(), usedBed.OwnershipHistory[0].FromOwnerId);
            Assert.AreEqual(BuyerHousehold.ToString(), usedBed.OwnershipHistory[0].ToOwnerId);
            Assert.AreEqual(1, installs.Count);
            Assert.AreEqual("bed-used-1", installs[0]);
        }

        [Test]
        public void Secondhand_AssetSoldElsewhere_DeliverRefuses()
        {
            EquipmentAsset usedBed = UsedBed();
            FurniturePurchaseOrder order = SecondhandOrder("furn-buy-gone");
            DriveToPaid(order);
            Assert.IsNull(usedBed.TransferOwnership("household", "5", "sold elsewhere first", 42), "the seller sold it to someone else first");
            Assert.IsNull(service.TryShip(order, freight, "opening-freight-wagon", hooks), "ship must pass — the moved sale is caught at load");
            string refusal = service.TryDeliver(order, freight, hooks, loader, driver, null);
            Assert.IsNotNull(refusal, "an asset no longer seller-owned cannot be received");
            StringAssert.Contains("no longer owned", refusal);
            Assert.AreEqual("5", usedBed.OwnerId, "the asset belongs to household 5 now");
            Assert.IsTrue(order.AllLinesAt(EquipmentPurchaseStage.Shipped), "purchase stays Shipped — never falsely delivered");
            Assert.AreEqual(0, installs.Count);
        }

        [Test]
        public void Quote_Secondhand_WrongOwner_Refused()
        {
            EquipmentAsset usedBed = UsedBed();
            usedBed.OwnerId = "5";
            FurniturePurchaseOrder order = SecondhandOrder("furn-buy-wrongowner");
            Assert.IsNotNull(service.Quote(order), "the asset is not the seller's");
        }
    }
}
