using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Trade;
using LandLedgers.Economy.Wheelwright;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D3A: wheelwright depth — custom build orders, the spare-parts shelf,
    /// used-wagon commerce, repair quality tiers, customer book accounts, and
    /// the capability-code reconciliation. Canon §7.2 (repair/fabrication
    /// economy), §7.2E (book credit), §7.2F (parts, refurbishment, used
    /// equipment), §6.5C (repair quality), §9.3 (rush orders), §9.3B (wagon
    /// kinds), §6.5G (spare parts as real inventory).
    /// </summary>
    [TestFixture]
    public sealed class WheelwrightDepth3ATests
    {
        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private static EquipmentAsset BlacksmithPart(string makerId, string makerName)
        {
            return new EquipmentAsset
            {
                AssetId = "EQ-smith-1-001",
                Kind = "wagon-part",
                DisplayName = "Wagon ironwork (tire/shoe)",
                Condition01 = 1f,
                OwnerKind = "business",
                OwnerId = makerId,
                MadeByBusinessId = makerId,
                MadeByBusinessName = makerName,
                MadeDayIndex = 40,
                MaterialLotIds = new List<string> { "IMP-ORD-1-1" },
            };
        }

        private static WheelwrightRuntime NewShop(int lumberUnits = 200, int ironworkParts = 3)
        {
            var shop = new WheelwrightRuntime("wheel-1", "Test Wheelwright", true);
            shop.ReceiveLumberLot(new ImportLot
            {
                LotId = "IMP-TEST-L1", MaterialId = ImportCatalog.LumberId,
                MaterialName = "Lumber (boards)", Units = lumberUnits,
                OriginName = "Local sawmill, wagon delivery", OrderId = "IMP-ORD-9",
            }, new List<string>());
            for (int i = 0; i < ironworkParts; i++)
                Assert.IsNull(shop.ReceiveIronworkPart(
                    BlacksmithPart("smith-1", "Test Smithy"), new List<string>()));
            return shop;
        }

        private static SkillService SkilledWheelwright(EntityId person, int minutes)
        {
            var skills = new SkillService();
            WheelwrightRuntime.RegisterSkills(skills, new List<string>());
            skills.GrantPractice(person, WheelwrightRuntime.WheelwrightingSkillId, minutes);
            return skills;
        }

        private static HouseholdLedger FundedLedger(int householdId, int cents)
        {
            var ledger = new HouseholdLedger(householdId);
            ledger.RecordInflow(50, cents, HouseholdIncomeSource.OtherDocumented,
                "test", "test funds", "test");
            return ledger;
        }

        private static RepairWorkOrder AgreedRepairOrder(WheelwrightRuntime shop,
            string assetId, string diagnosedProblem, int materialUnits, int priceCents,
            List<string> diagnostics, string componentKind = null)
        {
            var queue = shop.Repairs;
            RepairWorkOrder order = queue.Intake("Freight Co", "freight-1", assetId,
                "Freight wagon", "wheel wobbles", WheelwrightRuntime.WheelwrightCapabilityCode,
                "freight-yard", RepairUrgency.Routine, 50, diagnostics);
            Assert.IsNull(queue.Diagnose(order.WorkOrderId, diagnosedProblem,
                materialUnits, ImportCatalog.LumberId, 240, componentKind));
            Assert.IsNull(queue.AgreeTerms(order.WorkOrderId, priceCents));
            return order;
        }

        // ---------------- capability-code reconciliation ----------------

        [Test]
        public void CapabilityCode_SingleCanonicalCode()
        {
            Assert.AreEqual("wheelwright-work", WheelwrightRuntime.WheelwrightCapabilityCode,
                "D3A: one canonical capability code for wagon/wheel work — 'wagon-work' retired.");
        }

        [Test]
        public void Repair_Refused_RetiredWagonWorkCode()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            var queue = shop.Repairs;
            RepairWorkOrder order = queue.Intake("Farmer Jo", "farm-1", "WG-farm-1-001",
                "Farm wagon", "axle groans", "wagon-work", "farm-1", RepairUrgency.Routine, 50, diagnostics);
            Assert.IsNull(queue.Diagnose(order.WorkOrderId, "axle cracked", 8, ImportCatalog.LumberId, 180));
            Assert.IsNull(queue.AgreeTerms(order.WorkOrderId, 400));

            EntityId wright = Person(101);
            var skills = SkilledWheelwright(wright, 200);
            string failure = shop.CompleteWagonRepair(order.WorkOrderId,
                new EquipmentAsset { AssetId = "WG-farm-1-001" }, wright, 51, skills, null, diagnostics);
            Assert.IsNotNull(failure, "the retired 'wagon-work' code must not satisfy the wheelwright gate");
        }

        // ---------------- custom build orders ----------------

        [Test]
        public void BuildOrder_Lifecycle_Request_Quote_Agree_Build_Sell()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(102);
            var skills = SkilledWheelwright(wright, 200);

            WheelwrightBuildOrder order = shop.RequestBuildOrder(
                "Freight Co", "freight-1", "freight-wagon", false, 50, diagnostics);
            Assert.IsNotNull(order, string.Join("; ", diagnostics));
            Assert.AreEqual(WheelwrightBuildOrderStatus.Intake, order.Status);

            // Quote: parts (60 lumber @10c + 3 ironwork @50c) * 1.2 = 900c;
            // labor ceil(720/60)=12h * 25c = 300c; total 1200c.
            Assert.IsNull(shop.QuoteBuildOrder(order.OrderId, 10, 50, 50, diagnostics));
            Assert.AreEqual(1200, order.QuotedPriceCents);
            Assert.AreEqual(WheelwrightBuildOrderStatus.Quoted, order.Status);

            Assert.IsNull(shop.AgreeBuildOrderTerms(order.OrderId, 1200, 50, diagnostics));
            Assert.IsTrue(order.TermsAgreed);
            Assert.AreEqual(1, order.QueuePosition);
            Assert.AreEqual(51, order.PromisedDayIndex, "720 min over a 480-min bench day = 2 days");

            EquipmentAsset wagon = shop.CompleteBuildOrder(
                order.OrderId, wright, 52, skills, diagnostics);
            Assert.IsNotNull(wagon, string.Join("; ", diagnostics));
            Assert.AreEqual("freight-wagon", wagon.Kind);
            Assert.AreEqual(WheelwrightBuildOrderStatus.Complete, order.Status);
            Assert.AreEqual(wagon.AssetId, order.BuiltAssetId);
            Assert.AreEqual(200 - 60, shop.TotalLumberOnHand());
            Assert.AreEqual(0, shop.IronworkPartsOnHand);

            var buyerLedger = FundedLedger(21, 5000);
            Assert.IsNull(shop.RecordBuildSale(wagon, order.AgreedPriceCents, 52, buyerLedger, diagnostics));
            Assert.AreEqual(1200, shop.BuildRevenueCents);
            Assert.AreEqual(5000 - 1200, buyerLedger.GetBalanceCents());
        }

        [Test]
        public void BuildOrder_Rush_QuoteIncludesPremium_AndJumpsQueue()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();

            WheelwrightBuildOrder normal = shop.RequestBuildOrder(
                "Farmer Jo", "farm-1", "farm-wagon", false, 50, diagnostics);
            Assert.IsNull(shop.QuoteBuildOrder(normal.OrderId, 10, 50, 50, diagnostics));
            Assert.IsNull(shop.AgreeBuildOrderTerms(normal.OrderId, normal.QuotedPriceCents, 50, diagnostics));

            WheelwrightBuildOrder rush = shop.RequestBuildOrder(
                "Freight Co", "freight-1", "spring-wagon", true, 50, diagnostics);
            // parts (32*10 + 2*50) * 1.2 = 504c; labor ceil(420/60)=7h*25c=175c * 1.25 = 219c; total 723c.
            Assert.IsNull(shop.QuoteBuildOrder(rush.OrderId, 10, 50, 50, diagnostics));
            Assert.AreEqual(723, rush.QuotedPriceCents, "rush premium applies to labor");
            Assert.IsNull(shop.AgreeBuildOrderTerms(rush.OrderId, rush.QuotedPriceCents, 50, diagnostics));

            Assert.AreEqual(1, rush.QueuePosition, "rush jumps the non-rush queue (Canon §9.3 lever)");
            Assert.AreEqual(2, normal.QueuePosition);
        }

        [Test]
        public void BuildOrder_Refused_UnknownRecipe()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            WheelwrightBuildOrder order = shop.RequestBuildOrder(
                "Farmer Jo", "farm-1", "steam-wagon", false, 50, diagnostics);
            Assert.IsNull(order, "the book only sells what the recipes can build");
        }

        [Test]
        public void BuildOrder_Cancel_BeforeConstruction()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            WheelwrightBuildOrder order = shop.RequestBuildOrder(
                "Farmer Jo", "farm-1", "dray", false, 50, diagnostics);
            Assert.IsNotNull(order);
            Assert.IsNull(shop.CancelBuildOrder(order.OrderId, diagnostics));
            Assert.AreEqual(WheelwrightBuildOrderStatus.Cancelled, order.Status);
        }

        [Test]
        public void BuildOrder_Requeued_WhenMaterialsShort()
        {
            var shop = NewShop(lumberUnits: 10, ironworkParts: 3);
            var diagnostics = new List<string>();
            EntityId wright = Person(103);
            var skills = SkilledWheelwright(wright, 200);

            WheelwrightBuildOrder order = shop.RequestBuildOrder(
                "Freight Co", "freight-1", "freight-wagon", false, 50, diagnostics);
            Assert.IsNull(shop.QuoteBuildOrder(order.OrderId, 10, 50, 50, diagnostics));
            Assert.IsNull(shop.AgreeBuildOrderTerms(order.OrderId, order.QuotedPriceCents, 50, diagnostics));

            EquipmentAsset wagon = shop.CompleteBuildOrder(
                order.OrderId, wright, 52, skills, diagnostics);
            Assert.IsNull(wagon, "60 lumber needed, 10 on hand — build refused");
            Assert.AreEqual(WheelwrightBuildOrderStatus.Agreed, order.Status,
                "refused build requeues; restock and retry");
            Assert.AreEqual(10, shop.TotalLumberOnHand(), "nothing consumed on refusal");
        }

        // ---------------- spare-parts shelf ----------------

        [Test]
        public void SpareParts_Fabricate_Sell_TracksCostBasis()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(104);
            var skills = SkilledWheelwright(wright, 200);

            Assert.IsNull(shop.FabricateSparePart(
                "wagon-wheel", 2, wright, 60, skills, 10, 50, diagnostics));
            Assert.AreEqual(2, shop.PartsShelf.UnitsOnHand("wagon-wheel"));
            // Unit cost: 6 lumber @10c + 1 ironwork @50c = 110c.
            Assert.AreEqual(220, shop.PartsShelf.CashTiedUpCents());
            Assert.AreEqual(200 - 12, shop.TotalLumberOnHand());
            Assert.AreEqual(1, shop.IronworkPartsOnHand, "2 ironwork parts consumed");
            Assert.IsTrue(shop.PartsShelf.Batches[0].ProvenanceNote.Contains("IMP-TEST-L1"),
                "provenance carries onto the shelf");

            var buyerLedger = FundedLedger(22, 1000);
            Assert.IsNull(shop.SellSparePart("wagon-wheel", 200, 61, buyerLedger, diagnostics));
            Assert.AreEqual(1, shop.PartsShelf.UnitsOnHand("wagon-wheel"));
            Assert.AreEqual(200, shop.SparePartRevenueCents);
            Assert.AreEqual(1000 - 200, buyerLedger.GetBalanceCents());
        }

        [Test]
        public void SpareParts_SlowMoving_And_DeadInventory()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(105);
            var skills = SkilledWheelwright(wright, 200);
            Assert.IsNull(shop.FabricateSparePart(
                "wagon-axle", 1, wright, 60, skills, 10, 50, diagnostics));

            Assert.IsEmpty(shop.PartsShelf.SlowMovingKinds(119), "not slow before 60 days");
            CollectionAssert.Contains(shop.PartsShelf.SlowMovingKinds(120), "wagon-axle");
            Assert.IsEmpty(shop.PartsShelf.DeadInventoryKinds(239), "not dead before 180 days");
            CollectionAssert.Contains(shop.PartsShelf.DeadInventoryKinds(240), "wagon-axle");
        }

        [Test]
        public void SpareParts_Fabricate_Refused_UnknownKind()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(106);
            var skills = SkilledWheelwright(wright, 200);
            Assert.IsNotNull(shop.FabricateSparePart(
                "harness-buckle", 1, wright, 60, skills, 10, 50, diagnostics),
                "the shelf stocks wheelwright-made wooden parts, not everything");
        }

        [Test]
        public void Repair_ConsumesSparePart_InsteadOfLumber()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(107);
            var skills = SkilledWheelwright(wright, 200);
            Assert.IsNull(shop.FabricateSparePart(
                "wagon-wheel", 1, wright, 60, skills, 10, 50, diagnostics));

            RepairWorkOrder order = AgreedRepairOrder(shop, "WG-freight-1-001",
                "rim split — needs a new wheel", 6, 620, diagnostics, componentKind: "wagon-wheel");
            var wagon = new EquipmentAsset { AssetId = "WG-freight-1-001", Kind = "freight-wagon", Condition01 = 0.4f };

            string failure = shop.CompleteWagonRepair(order.WorkOrderId, wagon, wright, 61,
                skills, null, diagnostics, consumeSparePartKind: "wagon-wheel");
            Assert.IsNull(failure, failure);
            Assert.AreEqual(0, shop.PartsShelf.UnitsOnHand("wagon-wheel"), "the shelf supplied the wheel");
            Assert.AreEqual(200 - 6, shop.TotalLumberOnHand(), "no raw lumber burned for the wheel");
            Assert.AreEqual(1f, wagon.Condition01);
            Assert.IsTrue(string.Join(" ", diagnostics).Contains("from the shelf"));
        }

        // ---------------- repair quality ----------------

        [Test]
        public void RepairQuality_Temporary_RestoresPartial_AndFlagsFollowUp()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(108);
            var skills = SkilledWheelwright(wright, 200);

            RepairWorkOrder order = AgreedRepairOrder(shop, "WG-farm-1-002",
                "spokes cracked", 6, 400, diagnostics);
            var wagon = new EquipmentAsset { AssetId = "WG-farm-1-002", Kind = "farm-wagon", Condition01 = 0.3f };

            string failure = shop.CompleteWagonRepair(order.WorkOrderId, wagon, wright, 61,
                skills, null, diagnostics,
                quality: WheelwrightRepairQuality.Temporary, temporaryRestore01: 0.6f);
            Assert.IsNull(failure, failure);
            Assert.AreEqual(0.6f, wagon.Condition01, 0.001f, "temporary repair restores partially");
            Assert.IsTrue(wagon.MaintenanceLog[wagon.MaintenanceLog.Count - 1]
                .Contains("NEEDS PROPER SHOP FOLLOW-UP"), "known defect recorded (Canon §6.5D)");
            // Materials pro-rata to the gap closed: ceil(6 * (0.6-0.3)/(1-0.3)) = 3.
            Assert.AreEqual(200 - 3, shop.TotalLumberOnHand());
            Assert.AreEqual(RepairOrderStatus.Complete, order.Status);
        }

        [Test]
        public void RepairQuality_Improvised_RestoresLessThanTemporary()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(109);
            var skills = SkilledWheelwright(wright, 200);

            RepairWorkOrder order = AgreedRepairOrder(shop, "WG-farm-1-003",
                "axle groaning", 6, 300, diagnostics);
            var wagon = new EquipmentAsset { AssetId = "WG-farm-1-003", Kind = "farm-wagon", Condition01 = 0.2f };

            string failure = shop.CompleteWagonRepair(order.WorkOrderId, wagon, wright, 61,
                skills, null, diagnostics,
                quality: WheelwrightRepairQuality.Improvised, temporaryRestore01: 0.6f);
            Assert.IsNull(failure, failure);
            Assert.AreEqual(0.3f, wagon.Condition01, 0.001f, "improvised restores half the temporary target");
        }

        [Test]
        public void RepairQuality_Temporary_Refused_WhenAlreadyAboveTarget()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(110);
            var skills = SkilledWheelwright(wright, 200);

            RepairWorkOrder order = AgreedRepairOrder(shop, "WG-farm-1-004",
                "minor rattle", 6, 200, diagnostics);
            var wagon = new EquipmentAsset { AssetId = "WG-farm-1-004", Kind = "farm-wagon", Condition01 = 0.8f };

            string failure = shop.CompleteWagonRepair(order.WorkOrderId, wagon, wright, 61,
                skills, null, diagnostics,
                quality: WheelwrightRepairQuality.Temporary, temporaryRestore01: 0.6f);
            Assert.IsNotNull(failure, "a temporary fix above its own target does nothing — re-quote as proper");
            Assert.AreEqual(200, shop.TotalLumberOnHand(), "nothing consumed on refusal");
        }

        // ---------------- used-wagon commerce ----------------

        [Test]
        public void UsedWagons_Buy_Refurbish_Sell_BooksMargin()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(111);
            var skills = SkilledWheelwright(wright, 200);

            var wreck = new EquipmentAsset
            {
                AssetId = "WG-used-001", Kind = "freight-wagon",
                DisplayName = "Freight wagon", Condition01 = 0.4f,
                OwnerKind = "business", OwnerId = "freight-9",
            };
            Assert.IsNull(shop.AcquireWornWagon(wreck, 800, "Bankrupt Freighter", 60, diagnostics));
            Assert.AreEqual(1, shop.UsedWagons.Count);
            Assert.AreEqual("wheel-1", wreck.OwnerId, "ownership transfers to the shop");

            // Refurb: ceil(60 * 0.5) = 30 lumber @10c + 1 ironwork @50c = 350c.
            Assert.IsNull(shop.RefurbishWagonForResale(
                "WG-used-001", wright, 61, skills, 10, 50, diagnostics));
            UsedWagonRecord record = shop.UsedWagons.Find("WG-used-001");
            Assert.IsTrue(record.RefurbishedForResale);
            Assert.AreEqual(350, record.RefurbCostCents);
            Assert.AreEqual(1f, wreck.Condition01);
            Assert.IsTrue(wreck.MaintenanceLog[wreck.MaintenanceLog.Count - 1]
                .Contains("refurbished for resale"));

            var buyerLedger = FundedLedger(23, 5000);
            Assert.IsNull(shop.SellUsedWagon(
                "WG-used-001", "ranch-2", 2000, 62, buyerLedger, diagnostics));
            Assert.AreEqual(2000, shop.UsedWagonRevenueCents);
            Assert.AreEqual(0, shop.UsedWagons.Count);
            Assert.AreEqual("ranch-2", wreck.OwnerId);
            Assert.AreEqual(5000 - 2000, buyerLedger.GetBalanceCents());
            Assert.IsTrue(string.Join(" ", diagnostics).Contains("margin 850c"),
                "margin = 2000 - 800 acquisition - 350 refurb");
        }

        [Test]
        public void UsedWagons_Acquire_Refuses_Pristine_Anonymous_NonWagon()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();

            var sound = new EquipmentAsset { AssetId = "WG-sound", Kind = "farm-wagon", Condition01 = 0.9f };
            Assert.IsNotNull(shop.AcquireWornWagon(sound, 500, "Seller", 60, diagnostics),
                "the yard flips wrecks, not sound wagons");

            var wreck = new EquipmentAsset { AssetId = "WG-anon", Kind = "farm-wagon", Condition01 = 0.3f };
            Assert.IsNotNull(shop.AcquireWornWagon(wreck, 500, "", 60, diagnostics),
                "no anonymous sellers");

            var plow = new EquipmentAsset { AssetId = "EQ-plow", Kind = "plow", Condition01 = 0.2f };
            Assert.IsNotNull(shop.AcquireWornWagon(plow, 100, "Seller", 60, diagnostics),
                "the yard flips wagons, not plows");

            Assert.AreEqual(0, shop.UsedWagons.Count);
        }

        [Test]
        public void UsedWagons_Sell_Refused_BeforeRefurbish()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            var wreck = new EquipmentAsset
            {
                AssetId = "WG-used-002", Kind = "spring-wagon",
                DisplayName = "Spring wagon", Condition01 = 0.3f,
                OwnerKind = "business", OwnerId = "farm-3",
            };
            Assert.IsNull(shop.AcquireWornWagon(wreck, 300, "Seller", 60, diagnostics));
            Assert.IsNotNull(shop.SellUsedWagon(
                "WG-used-002", "ranch-2", 900, 61, null, diagnostics),
                "Canon §7.2F: buy, REPAIR, resell — no selling wrecks");
        }

        [Test]
        public void UsedWagons_Dismantle_YieldsParts_AndScrap()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(112);
            var skills = SkilledWheelwright(wright, 200);

            var wreck = new EquipmentAsset
            {
                AssetId = "WG-used-003", Kind = "farm-wagon",
                DisplayName = "Farm wagon", Condition01 = 0.15f,
                OwnerKind = "business", OwnerId = "farm-4",
            };
            Assert.IsNull(shop.AcquireWornWagon(wreck, 600, "Seller", 60, diagnostics));
            Assert.IsNull(shop.DismantleWagonForParts(
                "WG-used-003", wright, 61, skills, diagnostics));

            Assert.AreEqual(2, shop.PartsShelf.UnitsOnHand("wagon-wheel"));
            Assert.AreEqual(1, shop.PartsShelf.UnitsOnHand("wagon-axle"));
            Assert.AreEqual(60, shop.SalvageRevenueCents, "10% of 600c acquisition as scrap");
            Assert.AreEqual(0, shop.UsedWagons.Count, "dismantling is one-way");
            Assert.IsTrue(shop.PartsShelf.Batches[0].ProvenanceNote.Contains("WG-used-003"),
                "salvaged parts carry the donor's identity");
        }

        // ---------------- customer book accounts ----------------

        [Test]
        public void Accounts_Charge_Pay_Overdue()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            var book = shop.Accounts;

            WheelwrightCustomerAccount account = book.OpenAccount("freight-1", "Test Freight Co",
                WheelwrightCreditPosture.AccountOnRequest,
                new WheelwrightSettlementTerms(30, 5000), 60, diagnostics);
            Assert.IsNotNull(account);

            Assert.IsNull(book.ChargeToAccount("freight-1", 1200, "WO-0001 repair", 60, diagnostics));
            Assert.AreEqual(1200, account.BalanceCents);
            Assert.IsNotNull(book.ChargeToAccount("freight-1", 4000, "WO-0002", 61, diagnostics),
                "1200 + 4000 breaches the 5000c limit — refused loudly");

            Assert.IsNull(book.RecordAccountPayment("freight-1", 200, 65, diagnostics));
            Assert.AreEqual(1000, account.BalanceCents);
            Assert.IsNotNull(book.RecordAccountPayment("freight-1", 2000, 66, diagnostics),
                "overpayment refused — the shop is not a bank");

            Assert.IsEmpty(book.OverdueAccounts(90), "30-day terms from day 60: not yet overdue");
            CollectionAssert.Contains(
                book.OverdueAccounts(91).ConvertAll(a => a.CustomerBusinessId), "freight-1");
            Assert.AreEqual(1000, book.TotalReceivablesCents());
        }

        [Test]
        public void Accounts_Strict_Posture_Refuses_Charge()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            var book = shop.Accounts;
            book.OpenAccount("farm-1", "Farmer Jo", WheelwrightCreditPosture.Strict,
                new WheelwrightSettlementTerms(30, 5000), 60, diagnostics);
            Assert.IsNotNull(book.ChargeToAccount("farm-1", 300, "WO-0003", 60, diagnostics),
                "Strict = cash on completion, no tab");
        }

        [Test]
        public void Accounts_UnknownCustomer_Refused()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            Assert.IsNotNull(shop.Accounts.ChargeToAccount("ghost-9", 300, "WO-0004", 60, diagnostics));
        }

        // ---------------- save round-trip ----------------

        [Test]
        public void Save_RoundTrip_Preserves_D3A_State()
        {
            var shop = NewShop();
            var diagnostics = new List<string>();
            EntityId wright = Person(113);
            var skills = SkilledWheelwright(wright, 200);

            WheelwrightBuildOrder order = shop.RequestBuildOrder(
                "Freight Co", "freight-1", "farm-wagon", false, 50, diagnostics);
            Assert.IsNull(shop.QuoteBuildOrder(order.OrderId, 10, 50, 50, diagnostics));
            Assert.IsNull(shop.AgreeBuildOrderTerms(order.OrderId, order.QuotedPriceCents, 50, diagnostics));

            Assert.IsNull(shop.FabricateSparePart(
                "wagon-wheel", 2, wright, 60, skills, 10, 50, diagnostics));
            var buyerLedger = FundedLedger(24, 1000);
            Assert.IsNull(shop.SellSparePart("wagon-wheel", 250, 61, buyerLedger, diagnostics));

            var wreck = new EquipmentAsset
            {
                AssetId = "WG-used-004", Kind = "dray", DisplayName = "Dray",
                Condition01 = 0.3f, OwnerKind = "business", OwnerId = "farm-5",
            };
            Assert.IsNull(shop.AcquireWornWagon(wreck, 400, "Seller", 60, diagnostics));

            shop.Accounts.OpenAccount("freight-1", "Test Freight Co",
                WheelwrightCreditPosture.AccountOnRequest,
                new WheelwrightSettlementTerms(30, 5000), 60, diagnostics);
            Assert.IsNull(shop.Accounts.ChargeToAccount("freight-1", 300, "WO-0005", 60, diagnostics));

            WheelwrightRuntime.WheelwrightSaveDto dto = shop.ToSaveDto();
            var restored = new WheelwrightRuntime();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.BuildOrders.Orders.Count);
            Assert.AreEqual(order.OrderId, restored.BuildOrders.Orders[0].OrderId);
            Assert.AreEqual(WheelwrightBuildOrderStatus.Agreed,
                restored.BuildOrders.Orders[0].Status);
            Assert.AreEqual(1, restored.PartsShelf.UnitsOnHand("wagon-wheel"),
                "2 fabricated, 1 sold");
            Assert.AreEqual(250, restored.SparePartRevenueCents);
            Assert.AreEqual(1, restored.UsedWagons.Count);
            Assert.AreEqual(400, restored.UsedWagons.Find("WG-used-004").AcquisitionCents);
            Assert.AreEqual(300,
                restored.Accounts.FindByCustomer("freight-1").BalanceCents);
            Assert.AreEqual(shop.TotalLumberOnHand(), restored.TotalLumberOnHand());
        }
    }
}
