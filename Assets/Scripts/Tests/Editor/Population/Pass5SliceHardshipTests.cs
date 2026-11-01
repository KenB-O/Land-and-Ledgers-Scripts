using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Pass V slice I: the hardship chain on production authorities.
    /// Income loss -> savings drawdown -> reduced purchases (economize) ->
    /// credit use through a REAL lender's judgment and finite purse (and
    /// justified refusal creating nothing) -> arrears as real obligations ->
    /// asset sale to real buyers (goods and cash conserved) -> cheaper
    /// accommodation search. No hidden rescue, no guaranteed loan, no
    /// automatic forgiveness anywhere in the chain.
    /// </summary>
    [TestFixture]
    public sealed class Pass5SliceHardshipTests
    {
        private sealed class HardshipFixture
        {
            public const int HouseholdId = 7;
            public const int BuyerHouseholdId = 8;
            public const int Day = 100;

            public readonly HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public readonly HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public readonly HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public readonly HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public readonly EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public readonly ContactGraphService Contacts = new ContactGraphService();
            public readonly FinancialObligationAuthority Finance = new FinancialObligationAuthority();
            public readonly EntityIdRegistry Ids = new EntityIdRegistry();
            public readonly CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public readonly CreditCashBridge Cash = new CreditCashBridge();
            public readonly CreditEventLog Events = new CreditEventLog();
            public readonly HouseholdLenderDirectory Lenders = new HouseholdLenderDirectory();
            public readonly HouseholdDecisionService Decisions = new HouseholdDecisionService();
            public readonly HouseholdDecisionDriver Driver;
            public readonly HouseholdAutonomyDriver Autonomy = new HouseholdAutonomyDriver();
            public readonly HousingSearchNeedRegistry HousingNeeds = new HousingSearchNeedRegistry();
            public readonly List<string> Diag = new List<string>();

            public HardshipFixture()
            {
                Lenders.BindHubServices(Workflow, Finance, Cash, Ids, Events);
                Driver = new HouseholdDecisionDriver(Contacts, Employments, HousingNeeds, Finance, Ids, Cash);
            }

            public void SeedHousehold(int householdId, int openingCents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(householdId);
                if (openingCents > 0)
                {
                    ledger.RecordInflow(0, openingCents, HouseholdIncomeSource.WageEmployment,
                        "emp-seed", "seed wages", "test");
                }

                Cash.Register("household:" + householdId, new HouseholdCashStore(householdId, ledger));
                Memberships.Register(new HouseholdMembership
                {
                    MembershipId = householdId * 1000 + 1,
                    PersonId = householdId * 10 + 1,
                    HouseholdId = householdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = 0,
                });
            }

            public void Employ(int personId, int weeklyWageCents)
            {
                Employments.Register(new EmploymentRelationship
                {
                    Id = "emp-" + personId,
                    EmployeePersonId = personId,
                    EmployerBusinessId = "mill-1",
                    RoleDisplayName = "hand",
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, ""),
                });
            }

            public void EndEmployment(int personId)
            {
                EmploymentRelationship rel;
                if (Employments.TryGetById("emp-" + personId, out rel) && rel != null)
                {
                    rel.LifecycleState = EmploymentLifecycleState.Ended;
                }
            }

            public HouseholdAutonomyDriver.AutonomyContext AutonomyContext(
                IHardshipAssetSalePort assetSale, System.Func<int, int> surplusValueFor)
            {
                return new HouseholdAutonomyDriver.AutonomyContext
                {
                    Ledgers = Ledgers,
                    Memberships = Memberships,
                    Employments = Employments,
                    ContactGraph = Contacts,
                    Decisions = Decisions,
                    DecisionDriver = Driver,
                    HousingSearchNeeds = HousingNeeds,
                    FinancialObligations = Finance,
                    Ids = Ids,
                    Lenders = Lenders,
                    PersonNameOf = pid => pid == 91 ? "Abel Hart" : "Person " + pid,
                    AssetSale = assetSale,
                    SaleableSurplusValueFor = surplusValueFor,
                    RentObligationsFor = hid => new List<RentObligationState>
                    {
                        new RentObligationState
                        {
                            AgreementId = "agree-7",
                            AmountDueCents = 6000,
                            PaidCents = 0,
                            Status = RentObligationStatus.Current,
                        },
                    },
                };
            }
        }

        private static HouseholdAssetSaleService AssetSaleService(HardshipFixture f, int soapPriceCents)
        {
            return new HouseholdAssetSaleService(
                f.Ledgers, f.Inventories, f.Needs,
                householdId => householdId == HardshipFixture.HouseholdId || householdId == HardshipFixture.BuyerHouseholdId,
                () => new[] { HardshipFixture.HouseholdId, HardshipFixture.BuyerHouseholdId },
                itemId => itemId == HouseholdItemCatalog.SoapId ? soapPriceCents : 0);
        }

        [Test]
        public void I_IncomeLoss_LeadsToEconomize_NoHiddenRescue()
        {
            var f = new HardshipFixture();
            f.SeedHousehold(HardshipFixture.HouseholdId, 2000);
            int memberId = HardshipFixture.HouseholdId * 10 + 1;
            f.Employ(memberId, 2100); // 300c/day expected income

            var noSale = new HouseholdToHouseholdAssetSalePort(null);
            var ctx = f.AutonomyContext(noSale, hid => 0);

            // Day 1: employed — reserves + expected income cover rent.
            f.Autonomy.EvaluateHouseholdDay(HardshipFixture.HouseholdId, HardshipFixture.Day, ctx, f.Diag);
            HouseholdDecision day1 = f.Decisions.LastDecisionFor(HardshipFixture.HouseholdId.ToString());
            Assert.AreEqual(HouseholdDecisionKind.NoAction, day1.Kind,
                "employed household with adequate coverage does nothing, got: " + day1.Reason);

            // Day 2: the job ends. No contacts, no lenders, no surplus.
            f.EndEmployment(memberId);
            f.Autonomy.EvaluateHouseholdDay(HardshipFixture.HouseholdId, HardshipFixture.Day + 1, ctx, f.Diag);
            HouseholdDecision day2 = f.Decisions.LastDecisionFor(HardshipFixture.HouseholdId.ToString());
            Assert.AreEqual(HouseholdDecisionKind.Economize, day2.Kind,
                "job loss with no opportunities, contacts, credit, or surplus must economize, got: " + day2.Reason);
            Assert.IsTrue(day2.Reason.Contains("job loss") || f.Diag.Exists(d => d.Contains("job loss")),
                "the job loss must be visible in the record, not silent");

            // Reduced purchases: a real standing spending policy exists.
            HouseholdSpendingPolicy policy = f.Driver.GetSpendingPolicy(HardshipFixture.HouseholdId);
            Assert.IsNotNull(policy);
            Assert.AreEqual(50, policy.DiscretionaryCapPercent);

            // No rescue: no obligations were invented, no cash appeared.
            Assert.AreEqual(0, f.Finance.Obligations.Count);
            Assert.AreEqual(2000, f.Ledgers.Get(HardshipFixture.HouseholdId).GetBalanceCents());
        }

        [Test]
        public void I_BorrowWithRealLender_AdvancesCashAndRecordsDebt()
        {
            var f = new HardshipFixture();
            f.SeedHousehold(HardshipFixture.HouseholdId, 400);
            int memberId = HardshipFixture.HouseholdId * 10 + 1;

            string refusal = f.Lenders.RegisterPrivateLender("Abel Hart", 91, 20000,
                "inheritance documented in the county will register");
            Assert.IsNull(refusal, refusal);
            // The household knows Abel Hart through a documented contact link.
            f.Contacts.AddLink(memberId, 91, ContactLinkKind.Correspondent, "letter of introduction, day 40");

            IHouseholdCreditFacility facility = f.Lenders.FindFacilityFor(
                new[] { memberId },
                pid => f.Contacts.ReachableCandidates(string.Empty, new[] { pid }),
                pid => pid == 91 ? "Abel Hart" : "Person " + pid);
            Assert.IsNotNull(facility, "a known lender with real funds must be found");
            Assert.AreEqual("Abel Hart", facility.CreditorName);
            Assert.AreEqual(20000, facility.LendableCents());

            var decision = new HouseholdDecision
            {
                HouseholdId = HardshipFixture.HouseholdId.ToString(),
                Day = HardshipFixture.Day,
                Kind = HouseholdDecisionKind.Borrow,
                Reason = "test",
            };
            var pressure = new HouseholdPressureInput
            {
                HouseholdId = HardshipFixture.HouseholdId.ToString(),
                Day = HardshipFixture.Day,
                ReservesCents = 400,
                ObligationsDueCents = 6000,
                ExpectedIncomeCentsPerDay = 100,
                KnownCreditorId = "Abel Hart",
            };
            var execCtx = new HouseholdDecisionDriver.ExecutionContext { CreditFacility = facility };

            int lenderBefore = facility.LendableCents();
            HouseholdDecisionDriver.DecisionExecutionResult result =
                f.Driver.Execute(decision, pressure, execCtx, HardshipFixture.Day, f.Diag);

            Assert.IsTrue(result.Executed, result.Detail);
            Assert.AreEqual(1, f.Finance.Obligations.Count);
            FinancialObligation obligation = null;
            foreach (FinancialObligation o in f.Finance.Obligations) obligation = o;
            Assert.IsNotNull(obligation);
            Assert.AreEqual(FinancialObligationKind.Loan, obligation.Kind);
            Assert.AreEqual("household:7", obligation.Debtor);
            Assert.AreEqual("Abel Hart", obligation.Creditor);
            Assert.AreEqual(2600, obligation.OutstandingPrincipalCents); // 6000 - (400 + 3000)

            // Conservation: the lender's purse fell by exactly what the
            // household's ledger gained, and the debt matches.
            int lenderAfter = facility.LendableCents();
            int householdAfter = f.Ledgers.Get(HardshipFixture.HouseholdId).GetBalanceCents();
            Assert.AreEqual(lenderBefore - 2600, lenderAfter);
            Assert.AreEqual(400 + 2600, householdAfter);
            Assert.AreEqual(20400, lenderAfter + householdAfter, "cash is conserved across the advance");
        }

        [Test]
        public void I_BorrowRefusedByLenderPolicy_CreatesNothing()
        {
            var f = new HardshipFixture();
            f.SeedHousehold(HardshipFixture.HouseholdId, 400);
            int memberId = HardshipFixture.HouseholdId * 10 + 1;

            // A strict lender: requires 5 disclosed evidence records, which a
            // distressed household cannot supply.
            var strictPolicy = CreditLenderPolicy.ForPrivateIndividual();
            strictPolicy.MinimumEvidenceRecords = 5;
            string refusal = f.Lenders.RegisterPrivateLender("Abel Hart", 91, 20000,
                "inheritance documented in the county will register", strictPolicy);
            Assert.IsNull(refusal, refusal);
            f.Contacts.AddLink(memberId, 91, ContactLinkKind.Correspondent, "letter of introduction, day 40");

            IHouseholdCreditFacility facility = f.Lenders.FindFacilityFor(
                new[] { memberId },
                pid => f.Contacts.ReachableCandidates(string.Empty, new[] { pid }),
                pid => pid == 91 ? "Abel Hart" : "Person " + pid);
            Assert.IsNotNull(facility);

            var decision = new HouseholdDecision
            {
                HouseholdId = HardshipFixture.HouseholdId.ToString(),
                Day = HardshipFixture.Day,
                Kind = HouseholdDecisionKind.Borrow,
                Reason = "test",
            };
            var pressure = new HouseholdPressureInput
            {
                HouseholdId = HardshipFixture.HouseholdId.ToString(),
                Day = HardshipFixture.Day,
                ReservesCents = 400,
                ObligationsDueCents = 6000,
                ExpectedIncomeCentsPerDay = 100,
                KnownCreditorId = "Abel Hart",
            };
            var execCtx = new HouseholdDecisionDriver.ExecutionContext { CreditFacility = facility };

            HouseholdDecisionDriver.DecisionExecutionResult result =
                f.Driver.Execute(decision, pressure, execCtx, HardshipFixture.Day, f.Diag);

            Assert.IsFalse(result.Executed, "a refusing lender must stop the loan, got: " + result.Detail);
            Assert.AreEqual(0, f.Finance.Obligations.Count, "refusal must not record debt");
            Assert.AreEqual(400, f.Ledgers.Get(HardshipFixture.HouseholdId).GetBalanceCents(),
                "refusal must not create money");
            Assert.AreEqual(20000, facility.LendableCents(), "the lender's purse is untouched");
        }

        [Test]
        public void I_UnknownLender_MeansNoCredit()
        {
            var f = new HardshipFixture();
            f.SeedHousehold(HardshipFixture.HouseholdId, 400);
            int memberId = HardshipFixture.HouseholdId * 10 + 1;

            // A lender exists in the world but the household does NOT know them.
            string refusal = f.Lenders.RegisterPrivateLender("Abel Hart", 91, 20000,
                "inheritance documented in the county will register");
            Assert.IsNull(refusal, refusal);

            IHouseholdCreditFacility facility = f.Lenders.FindFacilityFor(
                new[] { memberId },
                pid => f.Contacts.ReachableCandidates(string.Empty, new[] { pid }),
                pid => pid == 91 ? "Abel Hart" : "Person " + pid);
            Assert.IsNull(facility, "an unknown lender is not a credit facility — no guaranteed loan");
        }

        [Test]
        public void I_AssetSale_MovesGoodsAndCashConserved()
        {
            var f = new HardshipFixture();
            f.SeedHousehold(HardshipFixture.HouseholdId, 100);
            f.SeedHousehold(HardshipFixture.BuyerHouseholdId, 5000);

            // Seller holds 4 units of soap (non-essential surplus).
            HouseholdInventory sellerInv = f.Inventories.GetOrCreate(HardshipFixture.HouseholdId);
            Assert.IsNull(sellerInv.AddLot(HouseholdItemCatalog.SoapId, 4, 90, "bought at the general store, day 90"));

            // Buyer genuinely needs soap and can pay.
            HouseholdPurchasingNeed need = f.Needs.CreateNeed(HardshipFixture.BuyerHouseholdId,
                HouseholdItemCatalog.SoapId, HardshipFixture.Day);
            need.UnitsNeeded = 4;
            need.Urgency01 = 0.8f;

            var service = AssetSaleService(f, 100); // 100c reference -> 60c distress
            int proceeds = service.SellSurplus(HardshipFixture.HouseholdId, HardshipFixture.Day, f.Diag);

            Assert.AreEqual(240, proceeds);
            // Goods conserved: 4 left the seller, 4 arrived at the buyer.
            Assert.AreEqual(0, sellerInv.GetAvailableUnits(HouseholdItemCatalog.SoapId));
            Assert.AreEqual(4, f.Inventories.Get(HardshipFixture.BuyerHouseholdId)
                .GetAvailableUnits(HouseholdItemCatalog.SoapId));
            // Cash conserved: buyer outflow == seller inflow.
            int sellerAfter = f.Ledgers.Get(HardshipFixture.HouseholdId).GetBalanceCents();
            int buyerAfter = f.Ledgers.Get(HardshipFixture.BuyerHouseholdId).GetBalanceCents();
            Assert.AreEqual(340, sellerAfter);
            Assert.AreEqual(4760, buyerAfter);
            Assert.AreEqual(5100, sellerAfter + buyerAfter);
        }

        [Test]
        public void I_AssetSale_EssentialsAreNeverSold()
        {
            var f = new HardshipFixture();
            f.SeedHousehold(HardshipFixture.HouseholdId, 100);
            f.SeedHousehold(HardshipFixture.BuyerHouseholdId, 5000);

            // Only flour (essential food) — the household will not sell it.
            HouseholdInventory sellerInv = f.Inventories.GetOrCreate(HardshipFixture.HouseholdId);
            Assert.IsNull(sellerInv.AddLot(HouseholdItemCatalog.FlourId, 10, 90, "harvest share, day 90"));
            HouseholdPurchasingNeed need = f.Needs.CreateNeed(HardshipFixture.BuyerHouseholdId,
                HouseholdItemCatalog.FlourId, HardshipFixture.Day);
            need.UnitsNeeded = 10;
            need.Urgency01 = 0.9f;

            var service = AssetSaleService(f, 120);
            int proceeds = service.SellSurplus(HardshipFixture.HouseholdId, HardshipFixture.Day, f.Diag);

            Assert.AreEqual(0, proceeds);
            Assert.AreEqual(10, sellerInv.GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(100, f.Ledgers.Get(HardshipFixture.HouseholdId).GetBalanceCents());
        }

        [Test]
        public void I_AssetSale_NoBuyer_FallsBackToEconomize()
        {
            var f = new HardshipFixture();
            f.SeedHousehold(HardshipFixture.HouseholdId, 100);

            HouseholdInventory sellerInv = f.Inventories.GetOrCreate(HardshipFixture.HouseholdId);
            Assert.IsNull(sellerInv.AddLot(HouseholdItemCatalog.SoapId, 4, 90, "bought at the general store, day 90"));
            // No buyer household has an open need for soap: no sale possible.

            var service = AssetSaleService(f, 100);
            var port = new HouseholdToHouseholdAssetSalePort(service);
            var decision = new HouseholdDecision
            {
                HouseholdId = HardshipFixture.HouseholdId.ToString(),
                Day = HardshipFixture.Day,
                Kind = HouseholdDecisionKind.SellAssets,
                Reason = "test",
            };
            var pressure = new HouseholdPressureInput
            {
                HouseholdId = HardshipFixture.HouseholdId.ToString(),
                Day = HardshipFixture.Day,
                ReservesCents = 100,
                ObligationsDueCents = 6000,
                ExpectedIncomeCentsPerDay = 0,
            };
            var execCtx = new HouseholdDecisionDriver.ExecutionContext { AssetSale = port };

            HouseholdDecisionDriver.DecisionExecutionResult result =
                f.Driver.Execute(decision, pressure, execCtx, HardshipFixture.Day, f.Diag);

            Assert.IsTrue(result.Executed, result.Detail);
            Assert.IsTrue(result.Detail.Contains("no sale possible"),
                "the fallback must be visible, got: " + result.Detail);
            // The household still acts: economizing policy for the shortfall.
            HouseholdSpendingPolicy policy = f.Driver.GetSpendingPolicy(HardshipFixture.HouseholdId);
            Assert.IsNotNull(policy);
            Assert.AreEqual(4, sellerInv.GetAvailableUnits(HouseholdItemCatalog.SoapId),
                "unsold goods stay with the household");
            Assert.AreEqual(100, f.Ledgers.Get(HardshipFixture.HouseholdId).GetBalanceCents(),
                "no phantom proceeds");
        }

        [Test]
        public void I_SellAssetsDecision_FiresWhenSurplusCoversShortfall()
        {
            var f = new HardshipFixture();
            f.SeedHousehold(HardshipFixture.HouseholdId, 100);

            HouseholdInventory sellerInv = f.Inventories.GetOrCreate(HardshipFixture.HouseholdId);
            Assert.IsNull(sellerInv.AddLot(HouseholdItemCatalog.SoapId, 40, 90, "bought at the general store, day 90"));

            var service = AssetSaleService(f, 100);
            int surplus = service.EvaluateSaleableValue(HardshipFixture.HouseholdId, f.Diag);
            Assert.AreEqual(40 * 60, surplus);

            var noSale = new HouseholdToHouseholdAssetSalePort(null);
            var ctx = f.AutonomyContext(noSale, hid => hid == HardshipFixture.HouseholdId ? surplus : 0);
            // No employment, no contacts, no lenders: shortfall 5900c, surplus 2400c < shortfall -> Economize.
            // Raise the surplus view: with 100 units the surplus would cover it.
            f.Autonomy.EvaluateHouseholdDay(HardshipFixture.HouseholdId, HardshipFixture.Day, ctx, f.Diag);
            HouseholdDecision d1 = f.Decisions.LastDecisionFor(HardshipFixture.HouseholdId.ToString());
            Assert.AreEqual(HouseholdDecisionKind.Economize, d1.Kind, "2400c surplus < 5900c shortfall -> economize, got " + d1.Kind);

            // Now give the household enough surplus to cover the shortfall.
            Assert.IsNull(sellerInv.AddLot(HouseholdItemCatalog.SoapId, 100, 90, "bought at the general store, day 90"));
            int bigSurplus = service.EvaluateSaleableValue(HardshipFixture.HouseholdId, f.Diag);
            var ctx2 = f.AutonomyContext(noSale, hid => hid == HardshipFixture.HouseholdId ? bigSurplus : 0);
            f.Autonomy.EvaluateHouseholdDay(HardshipFixture.HouseholdId, HardshipFixture.Day + 1, ctx2, f.Diag);
            HouseholdDecision d2 = f.Decisions.LastDecisionFor(HardshipFixture.HouseholdId.ToString());
            Assert.AreEqual(HouseholdDecisionKind.SellAssets, d2.Kind,
                "surplus covering the shortfall must sell assets before relocating, got: " + d2.Reason);
        }

        [Test]
        public void I_ArrearsFeedPressure_NegotiateTermsWhenShelterAtRisk()
        {
            var f = new HardshipFixture();
            // Tight but temporary: 500c reserves, 1400c/week wages, 6000c rent
            // due, shelter at risk. Coverage 6500c < 1.25x obligations, but
            // 30d income (6000c) meets the rent: negotiate, don't relocate.
            f.SeedHousehold(HardshipFixture.HouseholdId, 500);
            int memberId = HardshipFixture.HouseholdId * 10 + 1;
            f.Employ(memberId, 1400);

            // Unpaid rent (arrears) with shelter at risk: the household
            // negotiates terms rather than being silently forgiven.
            var noSale = new HouseholdToHouseholdAssetSalePort(null);
            var ctx = f.AutonomyContext(noSale, hid => 0);
            ctx.ShelterRiskFor = hid => true;
            ctx.ExecutionParams.AgreementId = "agree-7";
            ctx.ExecutionParams.LandlordHolderId = "landlord-1";
            ctx.ExecutionParams.RequestedChange = "extend due date by 14 days";

            f.Autonomy.EvaluateHouseholdDay(HardshipFixture.HouseholdId, HardshipFixture.Day, ctx, f.Diag);
            HouseholdDecision decision = f.Decisions.LastDecisionFor(HardshipFixture.HouseholdId.ToString());
            Assert.AreEqual(HouseholdDecisionKind.NegotiateTerms, decision.Kind,
                "shelter risk with temporary shortfall negotiates, got: " + decision.Kind);
            Assert.AreEqual(1, f.Driver.NegotiationRequests.Count);
            Assert.AreEqual(TenancyNegotiationStatus.Pending, f.Driver.NegotiationRequests[0].Status);
        }

        [Test]
        public void I_LenderDirectory_SaveLoad_RestoresLenders()
        {
            var f = new HardshipFixture();
            string refusal = f.Lenders.RegisterPrivateLender("Abel Hart", 91, 20000,
                "inheritance documented in the county will register");
            Assert.IsNull(refusal, refusal);

            HouseholdLenderDirectory.HouseholdLenderDirectorySaveDto dto = f.Lenders.CaptureSaveDto();
            var restored = new HouseholdLenderDirectory();
            restored.BindHubServices(f.Workflow, f.Finance, f.Cash, f.Ids, f.Events);
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.RegisteredLenderCount);
            Assert.IsTrue(restored.HasLender("Abel Hart"));

            // Clear-then-load: loading an empty DTO removes lenders.
            restored.LoadFromSaveDto(new HouseholdLenderDirectory.HouseholdLenderDirectorySaveDto());
            Assert.AreEqual(0, restored.RegisteredLenderCount);
        }
    }
}
