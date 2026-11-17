using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D4 (F-D2-3): the smith's iron import-order driver. The driver
    /// keeps the smith stocked with iron through paid import orders
    /// (Pittsburgh ironworks via railhead, 45c, 14 days) and never
    /// conjures stock. No free iron, no phantom merchant, no local iron
    /// industry. An empty smithy orders on day 1, the iron lands on day
    /// 14 as a provenance lot, and only then can the smith forge.
    /// </summary>
    [TestFixture]
    public sealed class BlacksmithIronImportDriverTests
    {
        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private static BlacksmithRuntime NewSmithy(int iron = 0, int coal = 50, bool forgeReady = true)
        {
            var smithy = new BlacksmithRuntime("smith-1", "Test Smithy", forgeReady);
            if (iron > 0)
            {
                smithy.ReceiveMaterial(new ImportLot
                {
                    LotId = "IMP-TEST-1", MaterialId = ImportCatalog.IronStockId,
                    MaterialName = "Iron stock", Units = iron,
                    OriginName = "Pittsburgh ironworks, via railhead", OrderId = "IMP-ORD-1",
                }, new List<string>());
            }
            if (coal > 0)
            {
                smithy.ReceiveMaterial(new ImportLot
                {
                    LotId = "IMP-TEST-2", MaterialId = ImportCatalog.ForgeCoalId,
                    MaterialName = "Forge coal", Units = coal,
                    OriginName = "Off-map coal dealer", OrderId = "IMP-ORD-2",
                }, new List<string>());
            }
            return smithy;
        }

        private static SkillService NoviceSmith(EntityId smith)
        {
            var skills = new SkillService();
            BlacksmithRuntime.RegisterSkills(skills, new List<string>());
            return skills;
        }

        private sealed class TestBusinessCashPort : IBusinessIdentityCashPort
        {
            public string BusinessId { get; }
            public int BalanceCents { get; private set; }
            public bool RefuseCredit { get; set; }
            public bool RefuseDebit { get; set; }

            public TestBusinessCashPort(string businessId, int openingBalanceCents)
            {
                BusinessId = businessId;
                BalanceCents = openingBalanceCents;
            }

            public string TryDebit(int amountCents, string reason)
            {
                if (RefuseDebit) return "TestBusinessCashPort: debit refused by the test double.";
                if (amountCents < 0) return "TestBusinessCashPort: negative debit.";
                if (BalanceCents < amountCents) return $"TestBusinessCashPort: insufficient funds ({BalanceCents}c for {amountCents}c).";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (RefuseCredit) return "TestBusinessCashPort: credit refused by the test double.";
                if (amountCents < 0) return "TestBusinessCashPort: negative credit.";
                BalanceCents += amountCents;
                return null;
            }
        }

        private static BlacksmithIronImportDriver NewIronDriver(
            BlacksmithRuntime smithy, TestBusinessCashPort smithCash,
            BusinessLiabilityLedger liabilities, EntityIdRegistry ids)
        {
            var diagnostics = new List<string>();
            var driver = new BlacksmithIronImportDriver(smithy, diagnostics);
            driver.SmithCash = smithCash;
            driver.LiabilityLedger = liabilities;
            driver.IdRegistry = ids;
            return driver;
        }

        // ---- F-D2-3: iron import order ----

        // The reorder policy: a covered stock at or below the threshold
        // fires, above it does not; outstanding units count toward cover.
        [Test]
        public void IronPolicy_SignalFiresAtOrBelowThreshold_NoneAbove()
        {
            var policy = new BlacksmithIronReorderPolicy();

            BlacksmithRuntime empty = NewSmithy();
            Assert.IsTrue(policy.Evaluate(empty, 0).ShouldOrder, "empty smithy orders");
            Assert.AreEqual(0, policy.Evaluate(empty, 0).CoveredUnits);

            BlacksmithRuntime atThreshold = NewSmithy(iron: 8);
            Assert.IsTrue(policy.Evaluate(atThreshold, 0).ShouldOrder, "8 on hand is at the threshold of 8");
            Assert.IsFalse(policy.Evaluate(atThreshold, 1).ShouldOrder, "8 on hand + 1 outstanding is above the threshold");

            BlacksmithRuntime above = NewSmithy(iron: 9);
            Assert.IsFalse(policy.Evaluate(above, 0).ShouldOrder, "9 on hand is above the threshold");
            Assert.AreEqual(9, policy.Evaluate(above, 0).CoveredUnits);
        }

        // An empty smithy orders 16 iron for cash at 45c, arriving day 14.
        [Test]
        public void Iron_DriveBelowThreshold_PlacesCashOrderAt45c()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewIronDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            Assert.IsNotNull(ImportCatalog.Get(ImportCatalog.IronStockId),
                "the driver wires the smith to the registered iron-stock material — no new supplier is invented");

            string summary = driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count, summary);
            ImportOrder order = driver.Orders[0];
            Assert.AreEqual(ImportCatalog.IronStockId, order.MaterialId);
            Assert.AreEqual(16, order.Units);
            Assert.AreEqual(45, order.PricePerUnitCents, "iron import at 45c per unit");
            Assert.AreEqual(14, order.ExpectedArrivalDayIndex, "14 days transit from Pittsburgh via the railhead");
            Assert.AreEqual("Pittsburgh ironworks, via railhead", order.OriginName);
            Assert.AreEqual("smith-1", order.BuyerBusinessId);
            Assert.AreEqual("Test Smithy", order.BuyerBusinessName);
            Assert.IsFalse(order.PaidOnCredit, "cash order");
            Assert.AreEqual(10000 - 16 * 45, smithCash.BalanceCents, "the smith pays 720c from its own business cash");
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.IronStockId), "nothing appears before arrival");
        }

        // The order is in transit for 14 days: nothing arrives early, and
        // no second order is placed while the first is outstanding.
        [Test]
        public void Iron_OrderInTransit_NoEarlyArrivalNoDoubleOrder()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewIronDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            Assert.AreEqual(16, driver.OutstandingIronUnits);

            driver.Drive(5);
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.IronStockId), "nothing arrives before day 14");
            Assert.AreEqual(1, driver.Orders.Count, "no second order while the first is outstanding");

            driver.Drive(13);
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.IronStockId), "day 13 is still too early");
            Assert.AreEqual(1, driver.Orders.Count);
        }

        // On day 14 the 16 iron land as one lot with the order id and
        // origin on it, and the covered stock (16) needs no new order.
        [Test]
        public void Iron_ArrivalDay_LandsAsProvenanceLot()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewIronDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            ImportOrder order = driver.Orders[0];

            driver.Drive(14);
            Assert.AreEqual(16, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            string expectedLotId = "IMP-" + order.OrderId + "-1";
            Assert.IsTrue(smithy.SmithLotIdsOnHand(ImportCatalog.IronStockId).Contains(expectedLotId),
                $"the delivered lot carries its import lot id {expectedLotId}");
            Assert.AreEqual(16, smithy.LotUnitsOnHand(ImportCatalog.IronStockId, expectedLotId));
            Assert.AreEqual(ImportOrderStatus.Delivered, order.Status);
            Assert.AreEqual(0, driver.OutstandingIronUnits);
            Assert.AreEqual(1, driver.Orders.Count, "16 on hand is above the threshold — no new order");
        }

        // A smithy already stocked above the threshold orders nothing.
        [Test]
        public void Iron_StockAboveThreshold_NoOrder()
        {
            var smithy = NewSmithy(iron: 9);
            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewIronDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(0, driver.Orders.Count);
            Assert.AreEqual(10000, smithCash.BalanceCents, "no cash moves when no order is placed");
        }

        // No cash and no credit: the order is refused and nothing is
        // created — no order, no lot, no payable.
        [Test]
        public void Iron_NoCashNoCredit_OrderRefusedNothingCreated()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 0);
            var driver = NewIronDriver(smithy, smithCash, null, new EntityIdRegistry());

            string summary = driver.Drive(0);
            Assert.AreEqual(0, driver.Orders.Count, summary);
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(0, smithCash.BalanceCents);
            Assert.IsTrue(summary.Contains("refused"), $"the refusal is the summary: '{summary}'");
        }

        // No cash but a liability ledger: the order is placed on honest
        // credit and the payable is recorded.
        [Test]
        public void Iron_NoCashWithCredit_OrderPlacedOnCreditPayableRecorded()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 0);
            var liabilities = new BusinessLiabilityLedger();
            var ids = new EntityIdRegistry();
            var driver = NewIronDriver(smithy, smithCash, liabilities, ids);

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            ImportOrder order = driver.Orders[0];
            Assert.IsTrue(order.PaidOnCredit);
            Assert.IsFalse(string.IsNullOrWhiteSpace(order.LiabilityId), "the credit order names its payable");
            Assert.AreEqual(1, liabilities.All.Count, "one payable on the liability ledger");
            Assert.AreEqual(16 * 45, liabilities.All[0].PrincipalCents, "the payable is the full order price");
            Assert.AreEqual(0, smithCash.BalanceCents, "no cash moves on a credit order");
        }

        // A zero-unit order is refused at the driver: nothing ordered.
        [Test]
        public void Iron_PlaceOrder_ZeroUnitsRefused()
        {
            var smithy = NewSmithy();
            var driver = NewIronDriver(smithy, new TestBusinessCashPort("smith-1", 10000), new BusinessLiabilityLedger(), new EntityIdRegistry());

            Assert.IsNull(driver.PlaceIronOrder(0, 0));
            Assert.AreEqual(0, driver.Orders.Count);
        }

        // A cash port belonging to another business never pays for the
        // smith's iron: the order is refused with no cash moved.
        [Test]
        public void Iron_PlaceOrder_MiswiredCashPortRefused()
        {
            var smithy = NewSmithy();
            var otherCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewIronDriver(smithy, otherCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            Assert.IsNull(driver.PlaceIronOrder(16, 0));
            Assert.AreEqual(0, driver.Orders.Count);
            Assert.AreEqual(10000, otherCash.BalanceCents, "no cash moves on a miswired port");
        }

        // Save round trip: the order book persists, the next order number
        // is not reissued, and a due order still arrives after restore.
        [Test]
        public void IronImport_SaveLoad_OrderBookPersistsNoReissueDueOrderArrives()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewIronDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            string firstOrderId = driver.Orders[0].OrderId;

            var restored = new BlacksmithIronImportDriver(smithy, new List<string>());
            restored.SmithCash = new TestBusinessCashPort("smith-1", 10000);
            restored.LiabilityLedger = new BusinessLiabilityLedger();
            restored.IdRegistry = new EntityIdRegistry();
            restored.LoadFromSaveDto(driver.ToSaveDto());
            Assert.AreEqual(1, restored.Orders.Count);
            Assert.AreEqual(firstOrderId, restored.Orders[0].OrderId);
            Assert.AreEqual(ImportOrderStatus.Ordered, restored.Orders[0].Status);

            ImportOrder second = restored.PlaceIronOrder(16, 1);
            Assert.IsNotNull(second);
            Assert.AreNotEqual(firstOrderId, second.OrderId, "the restored driver never reissues an order id");

            Assert.AreEqual(16, restored.ReceiveDueImports(14), "the restored order still arrives on day 14");
            Assert.AreEqual(16, smithy.MaterialOnHand(ImportCatalog.IronStockId));
            Assert.AreEqual(ImportOrderStatus.Delivered, restored.Orders[0].Status);
        }

        // End to end: an empty smithy cannot forge, orders iron on day 1,
        // receives it on day 14, and then forges 4 shoes from one unit.
        [Test]
        public void Iron_EndToEnd_ArrivedIronForgesShoes()
        {
            var smithy = NewSmithy();
            var skills = NoviceSmith(Person(1));
            var diagnostics = new List<string>();
            int minutes;
            Assert.IsNull(smithy.ForgeHorseshoe(Person(1), 0, skills, diagnostics, out minutes),
                "no iron, no shoes — the gap this driver closes");

            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewIronDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());
            driver.Drive(0);
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.IronStockId), "day 0: still no iron to forge with");

            driver.Drive(14);
            Assert.AreEqual(16, smithy.MaterialOnHand(ImportCatalog.IronStockId), "day 14: the iron has landed");

            for (int i = 0; i < 4; i++)
            {
                Assert.IsNotNull(smithy.ForgeHorseshoe(Person(1), 14, skills, diagnostics, out minutes),
                    string.Join("; ", diagnostics));
            }
            Assert.AreEqual(15, smithy.MaterialOnHand(ImportCatalog.IronStockId), "4 shoes consume one iron unit (quarter pool)");
            Assert.AreEqual(49, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "4 shoes consume one coal unit");
        }
    }
}
