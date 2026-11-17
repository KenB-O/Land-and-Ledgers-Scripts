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
    /// D5 (D4 follow-up): the smith's forge-coal import-order driver.
    /// The driver keeps the smith stocked with coal through paid import
    /// orders (off-map coal dealer via railhead, 18c, 12 days) and
    /// never conjures stock. No free coal, no phantom merchant, no
    /// local coal industry, no yard-wood substitution. An empty smithy
    /// orders on day 1, the coal lands on day 12 as a provenance lot,
    /// and only then can the smith forge.
    /// </summary>
    [TestFixture]
    public sealed class BlacksmithCoalImportDriverTests
    {
        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private static BlacksmithRuntime NewSmithy(int iron = 50, int coal = 0, bool forgeReady = true)
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
                    OriginName = "Off-map coal dealer, via railhead", OrderId = "IMP-ORD-2",
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

        private static BlacksmithCoalImportDriver NewCoalDriver(
            BlacksmithRuntime smithy, TestBusinessCashPort smithCash,
            BusinessLiabilityLedger liabilities, EntityIdRegistry ids)
        {
            var diagnostics = new List<string>();
            var driver = new BlacksmithCoalImportDriver(smithy, diagnostics);
            driver.SmithCash = smithCash;
            driver.LiabilityLedger = liabilities;
            driver.IdRegistry = ids;
            return driver;
        }

        // ---- D5: coal import order ----

        // The reorder policy: a covered stock at or below the threshold
        // fires, above it does not; outstanding units count toward cover.
        [Test]
        public void CoalPolicy_SignalFiresAtOrBelowThreshold_NoneAbove()
        {
            var policy = new BlacksmithCoalReorderPolicy();

            BlacksmithRuntime empty = NewSmithy();
            Assert.IsTrue(policy.Evaluate(empty, 0).ShouldOrder, "empty smithy orders");
            Assert.AreEqual(0, policy.Evaluate(empty, 0).CoveredUnits);

            BlacksmithRuntime atThreshold = NewSmithy(coal: 6);
            Assert.IsTrue(policy.Evaluate(atThreshold, 0).ShouldOrder, "6 on hand is at the threshold of 6");
            Assert.IsFalse(policy.Evaluate(atThreshold, 1).ShouldOrder, "6 on hand + 1 outstanding is above the threshold");

            BlacksmithRuntime above = NewSmithy(coal: 7);
            Assert.IsFalse(policy.Evaluate(above, 0).ShouldOrder, "7 on hand is above the threshold");
            Assert.AreEqual(7, policy.Evaluate(above, 0).CoveredUnits);
        }

        // An empty smithy orders 12 coal for cash at 18c, arriving day 12.
        [Test]
        public void Coal_DriveBelowThreshold_PlacesCashOrderAt18c()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewCoalDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            Assert.IsNotNull(ImportCatalog.Get(ImportCatalog.ForgeCoalId),
                "the driver wires the smith to the registered forge-coal material — no new supplier is invented");

            string summary = driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count, summary);
            ImportOrder order = driver.Orders[0];
            Assert.AreEqual(ImportCatalog.ForgeCoalId, order.MaterialId);
            Assert.AreEqual(12, order.Units);
            Assert.AreEqual(18, order.PricePerUnitCents, "coal import at 18c per unit");
            Assert.AreEqual(12, order.ExpectedArrivalDayIndex, "12 days transit from the off-map coal dealer via the railhead");
            Assert.AreEqual("Off-map coal dealer, via railhead", order.OriginName);
            Assert.AreEqual("smith-1", order.BuyerBusinessId);
            Assert.AreEqual("Test Smithy", order.BuyerBusinessName);
            Assert.IsFalse(order.PaidOnCredit, "cash order");
            Assert.AreEqual(10000 - 12 * 18, smithCash.BalanceCents, "the smith pays 216c from its own business cash");
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "nothing appears before arrival");
        }

        // The order is in transit for 12 days: nothing arrives early, and
        // no second order is placed while the first is outstanding.
        [Test]
        public void Coal_OrderInTransit_NoEarlyArrivalNoDoubleOrder()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewCoalDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            Assert.AreEqual(12, driver.OutstandingCoalUnits);

            driver.Drive(5);
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "nothing arrives before day 12");
            Assert.AreEqual(1, driver.Orders.Count, "no second order while the first is outstanding");

            driver.Drive(11);
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "day 11 is still too early");
            Assert.AreEqual(1, driver.Orders.Count);
        }

        // On day 12 the 12 coal land as one lot with the order id and
        // origin on it, and the covered stock (12) needs no new order.
        [Test]
        public void Coal_ArrivalDay_LandsAsProvenanceLot()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewCoalDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            ImportOrder order = driver.Orders[0];

            driver.Drive(12);
            Assert.AreEqual(12, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            string expectedLotId = "IMP-" + order.OrderId + "-1";
            Assert.IsTrue(smithy.SmithLotIdsOnHand(ImportCatalog.ForgeCoalId).Contains(expectedLotId),
                $"the delivered lot carries its import lot id {expectedLotId}");
            Assert.AreEqual(12, smithy.LotUnitsOnHand(ImportCatalog.ForgeCoalId, expectedLotId));
            Assert.AreEqual(ImportOrderStatus.Delivered, order.Status);
            Assert.AreEqual(0, driver.OutstandingCoalUnits);
            Assert.AreEqual(1, driver.Orders.Count, "12 on hand is above the threshold — no new order");
        }

        // A smithy already stocked above the threshold orders nothing.
        [Test]
        public void Coal_StockAboveThreshold_NoOrder()
        {
            var smithy = NewSmithy(coal: 7);
            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewCoalDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(0, driver.Orders.Count);
            Assert.AreEqual(10000, smithCash.BalanceCents, "no cash moves when no order is placed");
        }

        // No cash and no credit: the order is refused and nothing is
        // created — no order, no lot, no payable.
        [Test]
        public void Coal_NoCashNoCredit_OrderRefusedNothingCreated()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 0);
            var driver = NewCoalDriver(smithy, smithCash, null, new EntityIdRegistry());

            string summary = driver.Drive(0);
            Assert.AreEqual(0, driver.Orders.Count, summary);
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.AreEqual(0, smithCash.BalanceCents);
            Assert.IsTrue(summary.Contains("refused"), $"the refusal is the summary: '{summary}'");
        }

        // No cash but a liability ledger: the order is placed on honest
        // credit and the payable is recorded.
        [Test]
        public void Coal_NoCashWithCredit_OrderPlacedOnCreditPayableRecorded()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 0);
            var liabilities = new BusinessLiabilityLedger();
            var ids = new EntityIdRegistry();
            var driver = NewCoalDriver(smithy, smithCash, liabilities, ids);

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            ImportOrder order = driver.Orders[0];
            Assert.IsTrue(order.PaidOnCredit);
            Assert.IsFalse(string.IsNullOrWhiteSpace(order.LiabilityId), "the credit order names its payable");
            Assert.AreEqual(1, liabilities.All.Count, "one payable on the liability ledger");
            Assert.AreEqual(12 * 18, liabilities.All[0].PrincipalCents, "the payable is the full order price");
            Assert.AreEqual(0, smithCash.BalanceCents, "no cash moves on a credit order");
        }

        // A zero-unit order is refused at the driver: nothing ordered.
        [Test]
        public void Coal_PlaceOrder_ZeroUnitsRefused()
        {
            var smithy = NewSmithy();
            var driver = NewCoalDriver(smithy, new TestBusinessCashPort("smith-1", 10000), new BusinessLiabilityLedger(), new EntityIdRegistry());

            Assert.IsNull(driver.PlaceCoalOrder(0, 0));
            Assert.AreEqual(0, driver.Orders.Count);
        }

        // A cash port belonging to another business never pays for the
        // smith's coal: the order is refused with no cash moved.
        [Test]
        public void Coal_PlaceOrder_MiswiredCashPortRefused()
        {
            var smithy = NewSmithy();
            var otherCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewCoalDriver(smithy, otherCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            Assert.IsNull(driver.PlaceCoalOrder(12, 0));
            Assert.AreEqual(0, driver.Orders.Count);
            Assert.AreEqual(10000, otherCash.BalanceCents, "no cash moves on a miswired port");
        }

        // Save round trip: the order book persists, the next order number
        // is not reissued, and a due order still arrives after restore.
        [Test]
        public void CoalImport_SaveLoad_OrderBookPersistsNoReissueDueOrderArrives()
        {
            var smithy = NewSmithy();
            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewCoalDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            string firstOrderId = driver.Orders[0].OrderId;

            var restored = new BlacksmithCoalImportDriver(smithy, new List<string>());
            restored.SmithCash = new TestBusinessCashPort("smith-1", 10000);
            restored.LiabilityLedger = new BusinessLiabilityLedger();
            restored.IdRegistry = new EntityIdRegistry();
            restored.LoadFromSaveDto(driver.ToSaveDto());
            Assert.AreEqual(1, restored.Orders.Count);
            Assert.AreEqual(firstOrderId, restored.Orders[0].OrderId);
            Assert.AreEqual(ImportOrderStatus.Ordered, restored.Orders[0].Status);

            ImportOrder second = restored.PlaceCoalOrder(12, 1);
            Assert.IsNotNull(second);
            Assert.AreNotEqual(firstOrderId, second.OrderId, "the restored driver never reissues an order id");

            Assert.AreEqual(12, restored.ReceiveDueImports(12), "the restored order still arrives on day 12");
            Assert.AreEqual(12, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId));
            Assert.AreEqual(ImportOrderStatus.Delivered, restored.Orders[0].Status);
        }

        // End to end: a smithy with iron but no coal cannot forge,
        // orders coal on day 1, receives it on day 12, and then forges
        // 4 shoes from one iron unit and one coal unit.
        [Test]
        public void Coal_EndToEnd_ArrivedCoalForgesShoes()
        {
            var smithy = NewSmithy();
            var skills = NoviceSmith(Person(1));
            var diagnostics = new List<string>();
            int minutes;
            Assert.IsNull(smithy.ForgeHorseshoe(Person(1), 0, skills, diagnostics, out minutes),
                "no coal, no shoes — the gap this driver closes");

            var smithCash = new TestBusinessCashPort("smith-1", 10000);
            var driver = NewCoalDriver(smithy, smithCash, new BusinessLiabilityLedger(), new EntityIdRegistry());
            driver.Drive(0);
            Assert.AreEqual(0, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "day 0: still no coal to forge with");

            driver.Drive(12);
            Assert.AreEqual(12, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "day 12: the coal has landed");

            for (int i = 0; i < 4; i++)
            {
                Assert.IsNotNull(smithy.ForgeHorseshoe(Person(1), 12, skills, diagnostics, out minutes),
                    string.Join("; ", diagnostics));
            }
            Assert.AreEqual(49, smithy.MaterialOnHand(ImportCatalog.IronStockId), "4 shoes consume one iron unit (quarter pool)");
            Assert.AreEqual(11, smithy.MaterialOnHand(ImportCatalog.ForgeCoalId), "4 shoes consume one coal unit");
        }
    }
}
