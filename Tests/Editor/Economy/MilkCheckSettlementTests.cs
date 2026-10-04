using System.Collections.Generic;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W10B: creamery monthly milk-check model (deferred from EQU/T1). Butterfat tests
    /// per delivery, patron statements aggregated by month, and milk checks as real
    /// payables on the creamery with real receipts on the patron side.
    /// </summary>
    [TestFixture]
    public sealed class MilkCheckSettlementTests
    {
        private static Creamery NewCreamery()
        {
            return new Creamery("biz-creamery-1", "Maple Grove Creamery")
            {
                PricePerMilkUnitCents = 3,
                MaxIntakeUnitsPerDay = 500,
            };
        }

        private static MilkLot NewMilkLot(EntityIdRegistry registry, string farmId, int units, int dayIndex)
        {
            return new MilkLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                CowId = EntityId.For(EntityKind.Animal, 1),
                FarmId = farmId,
                ProducedDayIndex = dayIndex,
                QuantityUnits = units,
            };
        }

        private static CreameryDelivery Deliver(Creamery creamery, EntityIdRegistry registry, string farmId, int units, int dayIndex)
        {
            var diag = new List<string>();
            return creamery.AcceptDelivery(
                registry, farmId,
                new List<MilkLot> { NewMilkLot(registry, farmId, units, dayIndex) },
                dayIndex, diag);
        }

        [Test]
        public void ButterfatPrice_AnchorsToFlatPatronPrice()
        {
            var creamery = NewCreamery();
            int poundPrice = MilkCheckService.PricePerButterfatPoundCents(creamery);

            // At exactly standard butterfat, 100 units settle to the flat price (300c).
            float pounds = 100 * MilkCheckService.StandardButterfat01;
            int settled = (int)System.Math.Round(pounds * poundPrice);
            Assert.AreEqual(100 * creamery.PricePerMilkUnitCents, settled);
        }

        [Test]
        public void RecordButterfatTest_ValidTestRecorded()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();
            CreameryDelivery delivery = Deliver(creamery, registry, "farm-a", 100, 40);

            ButterfatTest test = service.RecordButterfatTest(
                creamery, delivery, 0.042f, EntityId.For(EntityKind.Person, 5), 40, registry, diag);

            Assert.IsNotNull(test);
            Assert.AreEqual(delivery.DeliveryId, test.DeliveryId);
            Assert.AreEqual(100, test.UnitsTested);
            Assert.AreEqual(4.2f, test.ButterfatPounds, 0.001f);
            Assert.AreEqual(1, service.Tests.Count);
        }

        [Test]
        public void RecordButterfatTest_RefusesUnknownDelivery()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();
            var stranger = new CreameryDelivery
            {
                DeliveryId = registry.Allocate(EntityKind.Lot),
                FarmId = "farm-a",
                DayIndex = 40,
                AcceptedUnits = 50,
            };

            ButterfatTest test = service.RecordButterfatTest(
                creamery, stranger, 0.04f, EntityId.For(EntityKind.Person, 5), 40, registry, diag);

            Assert.IsNull(test);
            Assert.AreEqual(0, service.Tests.Count);
        }

        [Test]
        public void RecordButterfatTest_RefusesAnonymousTester()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();
            CreameryDelivery delivery = Deliver(creamery, registry, "farm-a", 100, 40);

            ButterfatTest test = service.RecordButterfatTest(
                creamery, delivery, 0.04f, EntityId.Invalid, 40, registry, diag);

            Assert.IsNull(test);
        }

        [Test]
        public void RecordButterfatTest_RefusesNonsenseReading()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();
            CreameryDelivery delivery = Deliver(creamery, registry, "farm-a", 100, 40);

            Assert.IsNull(service.RecordButterfatTest(
                creamery, delivery, 0f, EntityId.For(EntityKind.Person, 5), 40, registry, diag));
            Assert.IsNull(service.RecordButterfatTest(
                creamery, delivery, 1.5f, EntityId.For(EntityKind.Person, 5), 40, registry, diag));
            Assert.AreEqual(0, service.Tests.Count);
        }

        [Test]
        public void BuildPatronStatement_TestedMilkSettlesOnButterfat()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();
            var tester = EntityId.For(EntityKind.Person, 5);

            CreameryDelivery d1 = Deliver(creamery, registry, "farm-a", 100, 40);
            CreameryDelivery d2 = Deliver(creamery, registry, "farm-a", 200, 41);
            Deliver(creamery, registry, "farm-b", 50, 40); // other patron — must not leak in

            service.RecordButterfatTest(creamery, d1, 0.04f, tester, 40, registry, diag);
            service.RecordButterfatTest(creamery, d2, 0.05f, tester, 41, registry, diag);

            PatronMilkStatement statement = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, registry, diag);

            Assert.IsNotNull(statement);
            Assert.AreEqual(300, statement.TotalAcceptedUnits);
            Assert.AreEqual(0, statement.UntestedUnits);
            Assert.AreEqual(300, statement.TestedUnits);
            // 100*0.04 + 200*0.05 = 14 lb butterfat @ 79c/lb = 1106c
            Assert.AreEqual(14f, statement.TotalButterfatPounds, 0.001f);
            Assert.AreEqual(1106, statement.GrossPayableCents);
            Assert.AreEqual(1106, statement.NetPayableCents);
            Assert.AreEqual(2, statement.DeliveryIds.Count);
        }

        [Test]
        public void BuildPatronStatement_UntestedDeliverySettlesFlatAndFlagged()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();

            Deliver(creamery, registry, "farm-a", 100, 40); // no test — flat settlement

            PatronMilkStatement statement = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, registry, diag);

            Assert.IsNotNull(statement);
            Assert.AreEqual(100, statement.UntestedUnits);
            Assert.AreEqual(300, statement.GrossPayableCents); // 100 x 3c flat
            Assert.AreEqual(1, statement.UntestedDeliveryIds.Count);
            CollectionAssert.IsNotEmpty(diag); // the flag is loud
        }

        [Test]
        public void BuildPatronStatement_DeductionsReduceNet()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();

            Deliver(creamery, registry, "farm-a", 100, 40);
            var deductions = new List<PatronStatementDeduction>
            {
                new PatronStatementDeduction { Reason = "can hauling, March", AmountCents = 50 },
            };

            PatronMilkStatement statement = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, deductions, registry, diag);

            Assert.AreEqual(300, statement.GrossPayableCents);
            Assert.AreEqual(50, statement.TotalDeductionsCents);
            Assert.AreEqual(250, statement.NetPayableCents);
        }

        [Test]
        public void BuildPatronStatement_NegativeNetFloorsAtZeroWithCarriedShortfall()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();

            Deliver(creamery, registry, "farm-a", 100, 40);
            var deductions = new List<PatronStatementDeduction>
            {
                new PatronStatementDeduction { Reason = "prior balance", AmountCents = 9999 },
            };

            PatronMilkStatement statement = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, deductions, registry, diag);

            Assert.AreEqual(0, statement.NetPayableCents);
            StringAssert.Contains("carried shortfall", statement.Notes);
        }

        [Test]
        public void IssueMilkCheck_BooksRealPayableOnCreamery()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var liabilities = new BusinessLiabilityLedger();
            var diag = new List<string>();

            Deliver(creamery, registry, "farm-a", 100, 40);
            PatronMilkStatement statement = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, registry, diag);

            MilkCheck check = service.IssueMilkCheck(
                statement, "Morrow Farm", liabilities, registry, 61, diag);

            Assert.IsNotNull(check);
            Assert.AreEqual(MilkCheckStatus.Issued, check.Status);
            Assert.AreEqual(300, check.AmountCents);
            Assert.AreEqual(statement.StatementId, check.StatementId);
            Assert.AreEqual(1, liabilities.All.Count);
            Assert.AreEqual("biz-creamery-1", liabilities.All[0].BusinessInstanceId);
            Assert.AreEqual("Morrow Farm", liabilities.All[0].Counterparty);
            Assert.AreEqual(300, liabilities.All[0].BalanceCents);
        }

        [Test]
        public void IssueMilkCheck_ZeroNetRefused()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var liabilities = new BusinessLiabilityLedger();
            var diag = new List<string>();

            // No deliveries at all — nothing to settle.
            PatronMilkStatement statement = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, registry, diag);

            MilkCheck check = service.IssueMilkCheck(
                statement, "Morrow Farm", liabilities, registry, 61, diag);

            Assert.IsNull(check);
            Assert.AreEqual(0, liabilities.All.Count);
        }

        [Test]
        public void PayMilkCheck_SettlesBothSidesWithProvenance()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var liabilities = new BusinessLiabilityLedger();
            var diag = new List<string>();
            var creameryCash = new HouseholdLedger(7);
            var patronLedger = new HouseholdLedger(9);

            Deliver(creamery, registry, "farm-a", 100, 40);
            PatronMilkStatement statement = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, registry, diag);
            MilkCheck check = service.IssueMilkCheck(
                statement, "Morrow Farm", liabilities, registry, 61, diag);

            string problem = service.PayMilkCheck(
                check, liabilities, creameryCash, patronLedger, "Maple Grove Creamery", 62, diag);

            Assert.IsNull(problem);
            Assert.AreEqual(MilkCheckStatus.Paid, check.Status);
            Assert.AreEqual(62, check.PaidDayIndex);
            Assert.AreEqual(0, liabilities.All[0].BalanceCents);
            Assert.IsTrue(liabilities.All[0].Settled);

            // Patron side: a real inflow with provenance.
            Assert.AreEqual(1, patronLedger.Entries.Count);
            Assert.AreEqual(300, patronLedger.Entries[0].AmountCents);
            Assert.AreEqual(HouseholdIncomeSource.SaleProceeds, patronLedger.Entries[0].Source);
            Assert.AreEqual(check.CheckId.ToString(), patronLedger.Entries[0].SourceReference);

            // Double-pay is refused.
            string second = service.PayMilkCheck(
                check, liabilities, creameryCash, patronLedger, "Maple Grove Creamery", 63, diag);
            Assert.IsNotNull(second);
        }

        [Test]
        public void VoidMilkCheck_VoidsUnpaidButNeverPaid()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var liabilities = new BusinessLiabilityLedger();
            var diag = new List<string>();
            var creameryCash = new HouseholdLedger(7);
            var patronLedger = new HouseholdLedger(9);

            Deliver(creamery, registry, "farm-a", 100, 40);
            PatronMilkStatement statement = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, registry, diag);
            MilkCheck check = service.IssueMilkCheck(
                statement, "Morrow Farm", liabilities, registry, 61, diag);

            Assert.IsNull(service.VoidMilkCheck(check, diag));
            Assert.AreEqual(MilkCheckStatus.Voided, check.Status);

            // A paid check cannot be voided.
            MilkCheck check2 = service.IssueMilkCheck(
                statement, "Morrow Farm", liabilities, registry, 61, diag);
            Assert.IsNull(service.PayMilkCheck(
                check2, liabilities, creameryCash, patronLedger, "Maple Grove Creamery", 62, diag));
            Assert.IsNotNull(service.VoidMilkCheck(check2, diag));
        }

        [Test]
        public void Renderer_ProducesReadableStatement()
        {
            var creamery = NewCreamery();
            var registry = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();

            Deliver(creamery, registry, "farm-a", 100, 40);
            PatronMilkStatement statement = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, registry, diag);

            string text = PatronStatementRenderer.ToText(statement, "Maple Grove Creamery", "Morrow Farm");

            StringAssert.Contains("Maple Grove Creamery", text);
            StringAssert.Contains("Morrow Farm", text);
            StringAssert.Contains("NET PAYABLE (milk check): 300c", text);
        }
    }
}
