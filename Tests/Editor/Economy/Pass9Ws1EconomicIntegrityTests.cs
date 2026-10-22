using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Postal;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass IX WS1 — Economic Integrity conservation suite (A3-01..A3-06,
    /// A3-10, A3-12). Every test asserts the conservation identity for its
    /// path: debits == credits, or the shortfall exists as a named,
    /// settleable Payable in FinancialObligationAuthority. Never both,
    /// never neither, never from nothing.
    /// </summary>
    [TestFixture]
    public sealed class Pass9Ws1EconomicIntegrityTests
    {
        private static BusinessRuntimeState NewBusiness(string id, int cashCents)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cashCents);
            return runtime;
        }

        private static HouseholdLedger FundedLedger(HouseholdLedgerRegistry ledgers, int householdId, int cents)
        {
            HouseholdLedger ledger = ledgers.GetOrCreate(householdId);
            string rejection = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OtherDocumented,
                "ws1-test-seed", "test seed funds", "test");
            Assert.IsNull(rejection);
            return ledger;
        }

        private static EmploymentRelationship NewEmployment(string businessId, int personId, int wageCents, string id)
        {
            return new EmploymentRelationship
            {
                Id = id,
                EmployeePersonId = personId,
                EmployerBusinessId = businessId,
                RoleDisplayName = "Hand",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(wageCents, "test"),
                StartDayIndex = 0,
                Source = EmploymentSource.Authored,
            };
        }

        // ---------------- A3-01: service revenue == household debit ----------------

        [Test]
        public void ServiceRevenue_EqualsHouseholdDebit_NoMultiplier()
        {
            // Former multiplier types (Barber 1.2, Saloon 1.15, Builder 1.1,
            // LiveryFreight 1.05) all settle at exactly visits x price now.
            var cases = new[]
            {
                (visits: 7, price: 22),   // Barber
                (visits: 5, price: 45),   // Saloon
                (visits: 3, price: 95),   // Builder
                (visits: 9, price: 65),   // LiveryFreight
                (visits: 4, price: 35),   // default
            };
            foreach ((int visits, int price) in cases)
            {
                Assert.AreEqual(visits * price, BusinessRuntimeState.ComputeServiceRevenueCents(visits, price),
                    $"Service revenue must equal the household debit for {visits} visits at {price}c.");
            }
        }

        [Test]
        public void ServiceWeek_HouseholdOutflowEqualsBusinessInflow()
        {
            var ledgers = new HouseholdLedgerRegistry();
            HouseholdLedger household = FundedLedger(ledgers, 11, 10000);
            BusinessRuntimeState barber = NewBusiness("biz-barber", 0);
            barber.EnsureCategoryStock("barber_service", 0, 0);

            int visits = 6;
            int price = 22;
            int debit = visits * price;
            string rejection = household.RecordOutflow(1, debit, "Barber service visits", "service");
            Assert.IsNull(rejection);

            int revenue = BusinessRuntimeState.ComputeServiceRevenueCents(visits, price);
            barber.RecordDailyServiceRevenue("barber_service", visits, revenue);

            Assert.AreEqual(10000 - debit, household.GetBalanceCents());
            Assert.AreEqual(debit, barber.CurrentCashCents,
                "Conservation per service week: household outflow == business inflow.");
        }

        // ---------------- A3-02: freight charge fail-closed & symmetric ----------------

        [Test]
        public void FreightCharge_SolventPayer_BothLegsPostInFull()
        {
            BusinessRuntimeState payer = NewBusiness("biz-payer", 1000);
            BusinessRuntimeState carrier = NewBusiness("biz-carrier", 0);

            bool settled = BusinessRuntimeState.TrySettleFreightCharge(
                payer, carrier, 300, "shipment s1", out string diagnostic);

            Assert.IsTrue(settled, diagnostic);
            Assert.AreEqual(700, payer.CurrentCashCents);
            Assert.AreEqual(300, carrier.CurrentCashCents, "Carrier credit == payer debit.");
            Assert.AreEqual(0, payer.AccruedLiabilityCents);
        }

        [Test]
        public void FreightCharge_BrokePayer_CarrierNotCredited_NamedPayableCreated()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            BusinessRuntimeState payer = NewBusiness("biz-payer", 100);
            payer.ObligationAuthority = authority;
            payer.ObligationIdRegistry = ids;
            BusinessRuntimeState carrier = NewBusiness("biz-carrier", 0);

            bool settled = BusinessRuntimeState.TrySettleFreightCharge(
                payer, carrier, 300, "shipment s2", out string diagnostic);

            Assert.IsFalse(settled);
            Assert.AreEqual(100, payer.CurrentCashCents, "No partial payer spend.");
            Assert.AreEqual(0, carrier.CurrentCashCents, "Carrier is NOT paid from nothing.");
            Assert.AreEqual(300, payer.AccruedLiabilityCents);
            Assert.AreEqual(1, payer.NamedPayables.Count);
            Assert.AreEqual("biz-carrier", payer.NamedPayables[0].Creditor, "The payable names the carrier as creditor.");
            Assert.AreEqual(300, authority.TotalOutstandingFor("biz-payer"),
                "The shortfall is a real, settleable Payable in the single debt authority.");
        }

        [Test]
        public void FreightCharge_SelfOrMissingLeg_PostsNothing()
        {
            BusinessRuntimeState solo = NewBusiness("biz-solo", 1000);

            Assert.IsFalse(BusinessRuntimeState.TrySettleFreightCharge(solo, solo, 300, "self", out _));
            Assert.IsFalse(BusinessRuntimeState.TrySettleFreightCharge(null, solo, 300, "nopayer", out _));
            Assert.AreEqual(1000, solo.CurrentCashCents);
            Assert.AreEqual(0, solo.AccruedLiabilityCents);
        }

        // ---------------- A3-03: postage has a real payer ----------------

        private static PostalService TwoOffices()
        {
            var postal = new PostalService();
            var diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-po-a", new List<int> { 0 }, 7, diag));
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-po-b", new List<int> { 0 }, 9, diag));
            return postal;
        }

        [Test]
        public void Postage_HouseholdPays_PostOfficeCollects_TotalCashConserved()
        {
            PostalService postal = TwoOffices();
            var diag = new List<string>();
            var ledgers = new HouseholdLedgerRegistry();
            HouseholdLedger household = FundedLedger(ledgers, 21, 10);
            var payer = new HouseholdLedgerPostagePayer(household);
            BusinessRuntimeState office = NewBusiness("biz-po-a", 0);

            MailItem item = postal.PostItem(MailKind.Letter, "Sender", 1, "Emma",
                "po-a", "po-b", 0, payer, diag);

            Assert.IsNotNull(item);
            Assert.AreEqual(7, household.GetBalanceCents(), "Sender debited the 3c letter rate.");
            Assert.AreEqual(3, postal.UncollectedPostageCents);

            int posted = postal.CollectAndPostPostageRevenue(office, diag);
            Assert.AreEqual(3, posted);
            Assert.AreEqual(3, office.CurrentCashCents);
            Assert.AreEqual(10, household.GetBalanceCents() + office.CurrentCashCents,
                "Conservation: sender debit == post office credit; nothing minted.");
        }

        [Test]
        public void Postage_SenderCannotPay_PostingRefused_NothingAccrues()
        {
            PostalService postal = TwoOffices();
            var diag = new List<string>();
            var ledgers = new HouseholdLedgerRegistry();
            HouseholdLedger household = FundedLedger(ledgers, 22, 2); // letter costs 3c
            var payer = new HouseholdLedgerPostagePayer(household);

            MailItem item = postal.PostItem(MailKind.Letter, "Sender", 1, "Emma",
                "po-a", "po-b", 0, payer, diag);

            Assert.IsNull(item, "No free carriage: an unpaid letter is refused.");
            Assert.AreEqual(2, household.GetBalanceCents(), "Refused posting moves nothing.");
            Assert.AreEqual(0, postal.UncollectedPostageCents, "No uncollected pot from an unpaid letter.");
        }

        [Test]
        public void Postage_NoPayerAuthority_PayerOverloadRefuses()
        {
            PostalService postal = TwoOffices();
            var diag = new List<string>();

            MailItem item = postal.PostItem(MailKind.Letter, "Sender", 1, "Emma",
                "po-a", "po-b", 0, (IPostalSenderCashAuthority)null, diag);

            Assert.IsNull(item);
            Assert.AreEqual(0, postal.UncollectedPostageCents);
        }

        [Test]
        public void Postage_BusinessPayer_DebitsTill_ParcelRefusedWhenShort()
        {
            PostalService postal = TwoOffices();
            var diag = new List<string>();
            BusinessRuntimeState business = NewBusiness("biz-sender", 10);
            var payer = new BusinessCashPostagePayer(business);

            Assert.IsNull(postal.PostItem(MailKind.Parcel, "Shop", 1, "Emma",
                "po-a", "po-b", 0, payer, diag), "A 25c parcel exceeds the 10c till — refused.");
            Assert.AreEqual(10, business.CurrentCashCents);

            Assert.IsNotNull(postal.PostItem(MailKind.Letter, "Shop", 1, "Emma",
                "po-a", "po-b", 0, payer, diag));
            Assert.AreEqual(7, business.CurrentCashCents);
            Assert.AreEqual(3, postal.UncollectedPostageCents);
        }

        // ---------------- A3-04: freight revenue verifies the payer debit ----------------

        private static FreightCompanyRuntime FreightCompanyWithCompletedShipment(string payerId, int chargeCents)
        {
            var company = new FreightCompanyRuntime("biz_freight_001");
            var diag = new List<string>();
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "ws1-ship",
                plannedQuantityUnits = 10,
                remainingQuantityUnits = 0,
                freightChargeCents = chargeCents,
                freightPayerBusinessInstanceId = payerId,
                cargoClass = LogisticsCargoClass.PackagedGoods,
                state = LogisticsShipmentStatus.Completed,
                deliveryApplied = true,
            };
            Assert.IsTrue(company.TryAcceptShipment(shipment, diag));
            return company;
        }

        [Test]
        public void FreightCollect_PayerCannotCover_NothingCollected_RevenueNeverPosts()
        {
            FreightCompanyRuntime company = FreightCompanyWithCompletedShipment("biz_store_001", 500);
            var diag = new List<string>();
            BusinessRuntimeState payer = NewBusiness("biz_store_001", 400); // short by 100c
            BusinessRuntimeState carrier = NewBusiness("biz_freight_001", 0);

            int collected = company.CollectFreightCharge("ws1-ship", payer, diag);
            Assert.AreEqual(0, collected, "Undebitable charge is not collected.");
            Assert.AreEqual(400, payer.CurrentCashCents);

            int posted = company.CollectAndPostUncollectedRevenue(carrier,
                id => id == "biz_store_001" ? payer : null, diag);
            Assert.AreEqual(0, posted);
            Assert.AreEqual(0, carrier.CurrentCashCents, "No revenue without a verified debit.");
            Assert.AreEqual(1, company.FindUncollectedFreightChargeShipmentIds().Count,
                "The shipment stays uncollected for a later retry.");
        }

        [Test]
        public void FreightCollect_VerifiedDebit_CarrierCreditEqualsPayerDebit_ExactlyOnce()
        {
            FreightCompanyRuntime company = FreightCompanyWithCompletedShipment("biz_store_001", 500);
            var diag = new List<string>();
            BusinessRuntimeState payer = NewBusiness("biz_store_001", 600);
            BusinessRuntimeState carrier = NewBusiness("biz_freight_001", 0);

            int posted = company.CollectAndPostUncollectedRevenue(carrier,
                id => id == "biz_store_001" ? payer : null, diag);

            Assert.AreEqual(500, posted);
            Assert.AreEqual(100, payer.CurrentCashCents, "Payer debited the charge.");
            Assert.AreEqual(500, carrier.CurrentCashCents, "Carrier credited exactly the debit.");
            Assert.AreEqual(0, company.CollectFreightCharge("ws1-ship", payer, diag),
                "Already-collected charges never collect twice.");
            Assert.AreEqual(100, payer.CurrentCashCents);
        }

        [Test]
        public void FreightCollect_WrongTill_Refused()
        {
            FreightCompanyRuntime company = FreightCompanyWithCompletedShipment("biz_store_001", 500);
            var diag = new List<string>();
            BusinessRuntimeState stranger = NewBusiness("biz_other", 10000);

            Assert.AreEqual(0, company.CollectFreightCharge("ws1-ship", stranger, diag));
            Assert.AreEqual(10000, stranger.CurrentCashCents, "A till that is not the named payer is never debited.");
        }

        // ---------------- A3-06: valuation feeds ----------------

        [Test]
        public void WeeklyOperatingProfit_ExcludesLoanProceedsAndOwnerTransfers()
        {
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment("biz-val", 5, 300, "val-emp-5"));
            var ledgers = new HouseholdLedgerRegistry();
            BusinessRuntimeState runtime = NewBusiness("biz-val", 10000);
            runtime.EmploymentRegistry = employments;
            runtime.HouseholdLedgers = ledgers;
            runtime.PersonHouseholdIdLookup = pid => pid == 5 ? 11 : -1;
            runtime.PayrollDayIndex = 7;

            runtime.RecordDailyServiceRevenue("svc", 1, 1000);       // revenue 1000
            runtime.AddWeeklyLocalTransferRevenue(500, "local work"); // revenue +500, cash +500
            runtime.AddWeeklyLocalTransferCost(200, "freight in");    // operating cost 200
            runtime.ResolveWeeklyPayroll();                            // payroll 300

            // Financing events that pollute WeekToDateNetCents:
            runtime.AdjustCashCents(5000, recordNetDelta: true);       // loan proceeds credit
            runtime.RecordWeeklyCashTransferIn(5000);
            runtime.RecordWeeklyOwnerDistribution(700);

            Assert.AreEqual(1000 + 500 + 5000 - 200 - 300,
                runtime.WeekToDateNetCents, "The cash-net really does include the loan (the old feed's defect).");
            Assert.AreEqual((1000 + 500) - (300 + 200), runtime.WeeklyOperatingProfitCents,
                "Operating profit = revenue - operating expense only; financing excluded (knownProfitProxy derivation).");

            var valuation = new EnterpriseValuationReadModel();
            valuation.RegisterBusiness("biz-val", "owner", true);
            valuation.RecordWeeklyProfit("biz-val", runtime.WeeklyOperatingProfitCents);
            Assert.AreEqual(1000, valuation.GetEvidence("biz-val").WeeklyOperatingProfitCents[0]);
        }

        [Test]
        public void ValuationLiabilities_BankLoanFromAuthority_ReducesOwnerEquity()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            // A BusinessLenderService-style loan: created DIRECTLY in the
            // authority; the liability ledger never sees an entry for it.
            FinancialObligation loan = authority.Create(ids, FinancialObligationKind.Loan,
                "biz-borrower", "town-bank", 50000, 0, "8% annual", "working capital loan");
            Assert.IsNotNull(loan);

            var valuation = new EnterpriseValuationReadModel();
            valuation.RegisterBusiness("biz-borrower", "owner", true);
            var ledger = new BusinessLiabilityLedger();
            ledger.AttachValuation(valuation);
            ledger.AttachFinancialAuthority(authority);

            ledger.SyncAllToValuation(new List<string>());

            Assert.AreEqual(50000, ledger.TotalLiabilitiesIncludingAuthorityFor("biz-borrower"));
            EnterpriseValuationResult result = valuation.GetValuation("biz-borrower");
            Assert.AreEqual(0, result.GrossGoingConcernValueCents);
            Assert.AreEqual(-50000, result.OwnerEquityValueCents,
                "Canon 11.3: bank-loan debt reduces owner equity even with no ledger entry.");
        }

        [Test]
        public void ValuationLiabilities_MirroredLedgerEntry_NotDoubleCounted()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var ledger = new BusinessLiabilityLedger();
            ledger.AttachFinancialAuthority(authority);

            BusinessLiability liability = ledger.Borrow(ids, "biz-y", "lender", 10000,
                "terms", "reason", 0, null, new List<string>());
            Assert.IsNotNull(liability);
            Assert.AreEqual(10000, authority.TotalOutstandingFor("biz-y"), "Borrow mirrors into the authority.");
            Assert.AreEqual(10000, ledger.TotalLiabilitiesIncludingAuthorityFor("biz-y"),
                "Mirrored debt counts once, not once per record.");
        }

        // ---------------- A3-10 / A3-12: payroll fallback & named payables ----------------

        [Test]
        public void Payroll_UnwiredHouseholdLedgers_WageBecomesEscrowedNamedPayable()
        {
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment("biz-esc", 5, 800, "esc-emp-5"));
            BusinessRuntimeState runtime = NewBusiness("biz-esc", 10000);
            runtime.EmploymentRegistry = employments;
            // HouseholdLedgers / PersonHouseholdIdLookup deliberately unwired.
            runtime.PayrollDayIndex = 42;

            runtime.ResolveWeeklyPayroll();

            Assert.AreEqual(9200, runtime.CurrentCashCents, "The business deduction stands (existing behavior).");
            Assert.AreEqual(800, runtime.AccruedLiabilityCents,
                "The deducted wage no longer vanishes: it is owed to the worker.");
            Assert.AreEqual(1, runtime.NamedPayables.Count);
            BusinessNamedPayableRecord payable = runtime.NamedPayables[0];
            Assert.AreEqual("person:5", payable.Creditor);
            Assert.AreEqual(800, payable.AmountCents);
            Assert.IsTrue(payable.CashAlreadyDeducted,
                "Escrowed: settlement must not deduct business cash a second time.");
            Assert.AreEqual(800, 10000 - runtime.CurrentCashCents,
                "Conservation: business out == escrowed payable (household in happens at settlement).");
        }

        [Test]
        public void Payroll_WorkerWithoutHousehold_WageBecomesEscrowedNamedPayable()
        {
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment("biz-nh", 7, 800, "nh-emp-7"));
            var ledgers = new HouseholdLedgerRegistry();
            BusinessRuntimeState runtime = NewBusiness("biz-nh", 10000);
            runtime.EmploymentRegistry = employments;
            runtime.HouseholdLedgers = ledgers;
            runtime.PersonHouseholdIdLookup = _ => -1; // worker has no household
            runtime.PayrollDayIndex = 42;

            runtime.ResolveWeeklyPayroll();

            Assert.AreEqual(9200, runtime.CurrentCashCents);
            Assert.AreEqual(1, runtime.NamedPayables.Count);
            Assert.AreEqual("person:7", runtime.NamedPayables[0].Creditor);
            Assert.IsNull(ledgers.Get(99), "Still no phantom household invented.");
        }

        [Test]
        public void MissedPayroll_InsufficientCash_BecomesNamedPayableInAuthority()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment("biz-broke", 1, 3000, "broke-emp-1"));
            var ledgers = new HouseholdLedgerRegistry();
            BusinessRuntimeState runtime = NewBusiness("biz-broke", 100);
            runtime.EmploymentRegistry = employments;
            runtime.HouseholdLedgers = ledgers;
            runtime.PersonHouseholdIdLookup = pid => 101;
            runtime.ObligationAuthority = authority;
            runtime.ObligationIdRegistry = ids;
            runtime.PayrollDayIndex = 42;

            runtime.ResolveWeeklyPayroll();

            Assert.AreEqual(100, runtime.CurrentCashCents, "Unpaid wages are not deducted.");
            Assert.AreEqual(3000, runtime.AccruedLiabilityCents);
            Assert.AreEqual(3000, authority.TotalOutstandingFor("biz-broke"),
                "The missed wage is a named, settleable Payable in the debt authority.");
            Assert.AreEqual(1, runtime.NamedPayables.Count);
            Assert.AreEqual("person:1", runtime.NamedPayables[0].Creditor);
            Assert.IsFalse(string.IsNullOrWhiteSpace(runtime.NamedPayables[0].ObligationId));
            Assert.IsFalse(runtime.NamedPayables[0].CashAlreadyDeducted,
                "Cash never moved for a missed payroll — settlement pays from cash then.");
        }

        [Test]
        public void NamedPayables_FlushToAuthorityWhenWiringArrivesLate()
        {
            BusinessRuntimeState runtime = NewBusiness("biz-flush", 5000);
            runtime.AccrueOperatingLiabilityCents(700, "supplier:acme", "unpaid supplier invoice");
            Assert.AreEqual(1, runtime.NamedPayables.Count);
            Assert.IsTrue(string.IsNullOrWhiteSpace(runtime.NamedPayables[0].ObligationId),
                "Unwired at accrual time: named locally, not yet in the authority.");

            runtime.ObligationAuthority = new FinancialObligationAuthority();
            runtime.ObligationIdRegistry = new EntityIdRegistry();

            Assert.AreEqual(1, runtime.FlushNamedPayablesToAuthority());
            Assert.AreEqual(700, runtime.ObligationAuthority.TotalOutstandingFor("biz-flush"));
            Assert.AreEqual(0, runtime.FlushNamedPayablesToAuthority(), "Flush is idempotent.");
        }

        [Test]
        public void SettleNamedPayable_EscrowedWage_DoesNotDeductCashTwice()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment("biz-settle", 5, 800, "settle-emp-5"));
            BusinessRuntimeState runtime = NewBusiness("biz-settle", 10000);
            runtime.EmploymentRegistry = employments;
            runtime.ObligationAuthority = authority;
            runtime.ObligationIdRegistry = ids;
            runtime.PayrollDayIndex = 42;

            runtime.ResolveWeeklyPayroll(); // unwired household ledgers -> escrow payable
            Assert.AreEqual(9200, runtime.CurrentCashCents);
            string obligationId = runtime.NamedPayables[0].ObligationId;
            Assert.IsFalse(string.IsNullOrWhiteSpace(obligationId));

            bool settled = runtime.TrySettleNamedPayable(obligationId, 800, 43, out string diagnostic);

            Assert.IsTrue(settled, diagnostic);
            Assert.AreEqual(9200, runtime.CurrentCashCents,
                "Escrow settlement records the payment without a second deduction.");
            Assert.AreEqual(0, authority.TotalOutstandingFor("biz-settle"));
            Assert.AreEqual(0, runtime.AccruedLiabilityCents);
            Assert.AreEqual(0, runtime.NamedPayables.Count);
        }

        [Test]
        public void TransferCostShortfall_WithNamedCounterparty_BecomesNamedPayable()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            BusinessRuntimeState buyer = NewBusiness("biz-buyer", 100);
            buyer.ObligationAuthority = authority;
            buyer.ObligationIdRegistry = ids;

            buyer.AddWeeklyLocalTransferCost(450, "recurring order bought grain", "biz-seller");

            Assert.AreEqual(0, buyer.CurrentCashCents, "Only the 100c on hand was spent.");
            Assert.AreEqual(350, buyer.AccruedLiabilityCents);
            Assert.AreEqual(1, buyer.NamedPayables.Count);
            Assert.AreEqual("biz-seller", buyer.NamedPayables[0].Creditor,
                "A3-12: transfer shortfalls name the real creditor (the seller), never an anonymous scalar alone.");
            Assert.AreEqual(350, authority.TotalOutstandingFor("biz-buyer"));
        }

        [Test]
        public void SettleNamedPayable_OrdinaryPayable_SpendsRealCash_AndRefusesWhenShort()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            BusinessRuntimeState runtime = NewBusiness("biz-ord", 1000);
            runtime.ObligationAuthority = authority;
            runtime.ObligationIdRegistry = ids;
            runtime.AccrueOperatingLiabilityCents(700, "supplier:acme", "invoice");
            string obligationId = runtime.NamedPayables[0].ObligationId;

            Assert.IsFalse(runtime.TrySettleNamedPayable(obligationId, 701, 1, out _),
                "Overpayment is refused.");
            Assert.IsTrue(runtime.TrySettleNamedPayable(obligationId, 700, 1, out string diagnostic), diagnostic);
            Assert.AreEqual(300, runtime.CurrentCashCents, "Ordinary settlement pays from real business cash.");
            Assert.AreEqual(0, authority.TotalOutstandingFor("biz-ord"));
        }
    }
}
