// Pass VII slices G and H: manager decision execution against production
// authorities, driven by the real ManagerBrain.
//
// Slice G: declining inventory -> ManagerBrain.DailyCycle emits a Procure
// decision -> ManagerDecisionExecutionService executes it -> a supplier with
// actual goods is found -> cash moves buyer to supplier (conserved) ->
// shipment books -> AdvanceShipments delivers -> stock is received and the
// accounts reconcile.
//
// Slice H: a blocked action (escalated borrowing) becomes a legitimate
// pending request; repeated attempts neither duplicate it nor loop forever.
using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class ManagerExecutionSliceTests
    {
        private const int Day = 410;

        private sealed class Fixture
        {
            public ManagerDecisionExecutionService Service = new ManagerDecisionExecutionService();
            public ManagerBrain Brain = new ManagerBrain();
            public Dictionary<string, BusinessInstanceState> Businesses = new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkScheduler Scheduling = new WorkScheduler();
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public List<string> Diagnostics = new List<string>();
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "field " + name);
            field.SetValue(target, value);
        }

        private static BusinessInstanceState MakeBusiness(string id, int cashCents)
        {
            var business = new BusinessInstanceState();
            SetField(business, "instanceId", id);
            Assert.IsTrue(business.TryAssignPremises(42), "premises assigned");
            var runtime = new BusinessRuntimeState();
            runtime.AddCashCents(cashCents);
            SetField(business, "runtimeState", runtime);
            return business;
        }

        private Fixture MakeFixture()
        {
            var fixture = new Fixture();
            fixture.Service.Configure(new ManagerExecutionServices
            {
                FindBusiness = id =>
                {
                    BusinessInstanceState found;
                    return fixture.Businesses.TryGetValue(id, out found) ? found : null;
                },
                AllBusinesses = () => fixture.Businesses.Values,
                DemandFor = id => new LaborDemandReport { BusinessInstanceId = id, DayIndex = Day },
                BusinessEntityFor = id => EntityId.For(EntityKind.Business, Math.Abs(id.GetHashCode())),
                Tasks = fixture.Tasks,
                WorkScheduling = fixture.Scheduling,
                PersonSchedules = fixture.Schedules,
                WorkTimeBudgets = fixture.Budgets,
                Employments = fixture.Employments,
                FinancialObligations = fixture.Obligations,
                Ids = fixture.Ids,
                SupplierKnowledge = new ShopperSellerKnowledge(),
                BusinessLender = null,
                OpenRecruitment = (business, demand, decision, day, diag) => "effort:" + business.InstanceId,
            });
            return fixture;
        }

        private static DelegatedAuthority MakeAuthority(string businessId, int managerPersonId, DelegationScope scopes, int purchaseLimitCents)
        {
            return new DelegatedAuthority
            {
                AuthorityId = "auth-" + businessId,
                BusinessInstanceId = businessId,
                DelegatePersonId = managerPersonId,
                Scopes = scopes,
                PurchaseLimitCents = purchaseLimitCents,
                GrantedDayIndex = Day - 10,
            };
        }

        // ---------- Slice G ----------

        [Test]
        public void SliceG_ManagerProcurement_EndToEnd()
        {
            Fixture fixture = MakeFixture();

            // The buyer's flour is declining: 2 units against a reorder point of 5.
            BusinessInstanceState buyer = MakeBusiness("general-store", 100000);
            buyer.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("flour", "shelf-a", 20f, 40f, 5f, 120));
            buyer.GenericConfiguration.AddInventory("flour", 2f, 80);
            fixture.Businesses["general-store"] = buyer;

            // The supplier holds actual flour.
            BusinessInstanceState supplier = MakeBusiness("mill", 50000);
            supplier.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("flour", "shelf-a", 50f, 100f, 10f, 100));
            supplier.GenericConfiguration.AddInventory("flour", 100f, 80);
            fixture.Businesses["mill"] = supplier;

            var authority = MakeAuthority("general-store", 7, DelegationScope.Purchasing, 50000);

            // The real brain observes the shortage and decides to procure.
            ManagerDecisionResult decision = fixture.Brain.DailyCycle(new ManagerCycleInputs
            {
                BusinessInstanceId = "general-store",
                ManagerPersonId = 7,
                CashCents = buyer.RuntimeState.CurrentCashCents,
                DemandReport = new LaborDemandReport { BusinessInstanceId = "general-store", DayIndex = Day },
                DelegatedScopes = new List<string> { ManagerScopeNames.Purchasing },
                DayIndex = Day,
                JudgmentSeed = 42,
                JudgmentQuality01 = 1f,
                PurchaseLimitCents = 50000,
                ShortageObserved = true,
                ProposedProcurementCents = 2000,
            }, fixture.Diagnostics);

            Assert.IsTrue(decision.Acted, "the brain acted on the shortage");
            Assert.AreEqual(ManagerActKinds.Procure, decision.ActionKind, "it chose procurement");

            int buyerCashBefore = buyer.RuntimeState.CurrentCashCents;
            int supplierCashBefore = supplier.RuntimeState.CurrentCashCents;
            float supplierStockBefore = supplier.GenericConfiguration.GetAvailableInventoryQuantity("flour");

            // The decision executes through the production executor.
            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                buyer, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "general-store", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(outcome.Executed, "executed: " + outcome.Reason);

            // Accounts reconcile: cash conserved, supplier stock consumed.
            int paid = buyerCashBefore - buyer.RuntimeState.CurrentCashCents;
            Assert.Greater(paid, 0, "the buyer paid real cash");
            Assert.AreEqual(paid, supplier.RuntimeState.CurrentCashCents - supplierCashBefore,
                "cash conserved across the transaction");
            Assert.Less(supplier.GenericConfiguration.GetAvailableInventoryQuantity("flour"), supplierStockBefore,
                "the supplier's real goods left its shelves");
            Assert.AreEqual(1, fixture.Service.Ledger.Shipments.Count, "one shipment booked");
            Assert.AreEqual("InTransit", fixture.Service.Ledger.Shipments[0].Status);

            // The shipment arrives the next day and stock is received.
            float ordered = fixture.Service.Ledger.Shipments[0].Quantity;
            fixture.Service.AdvanceShipments(Day + 1, fixture.Diagnostics);
            Assert.AreEqual("Delivered", fixture.Service.Ledger.Shipments[0].Status);
            Assert.AreEqual(2f + ordered, buyer.GenericConfiguration.GetAvailableInventoryQuantity("flour"), 0.001f,
                "the ordered stock arrived — no teleport, no invention");

            // The brain's activity log recorded the decision.
            Assert.Greater(fixture.Brain.ActivityLog.Records.Count, 0, "the brain logged its activity");
        }

        // ---------- Slice H ----------

        [Test]
        public void SliceH_BlockedBorrowing_BecomesPendingRequest_WithoutInfiniteRetries()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("general-store", 10000);
            fixture.Businesses["general-store"] = business;

            // The brain escalates borrowing: it needs owner approval.
            var authority = MakeAuthority("general-store", 7, DelegationScope.CashHandling, 20000);
            ManagerDecisionResult decision = fixture.Brain.DailyCycle(new ManagerCycleInputs
            {
                BusinessInstanceId = "general-store",
                ManagerPersonId = 7,
                CashCents = business.RuntimeState.CurrentCashCents,
                DemandReport = new LaborDemandReport { BusinessInstanceId = "general-store", DayIndex = Day },
                DelegatedScopes = new List<string> { ManagerScopeNames.CashHandling },
                DayIndex = Day,
                JudgmentSeed = 7,
                JudgmentQuality01 = 1f,
                PurchaseLimitCents = 20000,
                MaxBorrowingCents = 0,
                ProposedBorrowingCents = 8000,
            }, fixture.Diagnostics);

            Assert.IsTrue(decision.Escalated, "the decision is escalated");
            Assert.AreEqual(ManagerActKinds.Borrow, decision.ActionKind, "the brain names the blocked act");


            // The blocked action becomes a legitimate pending request.
            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                business, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "general-store", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(outcome.Pending, "blocked borrowing becomes a pending request");
            Assert.AreEqual(1, fixture.Service.Ledger.PendingRequests.Count);
            Assert.AreEqual("Pending", fixture.Service.Ledger.PendingRequests[0].Status);

            // Retrying across days never duplicates the request and never loops.
            for (int day = Day + 1; day <= Day + 5; day++)
            {
                ManagerExecutionOutcome retry = fixture.Service.ExecuteDecision(
                    business, authority, 7,
                    new LaborDemandReport { BusinessInstanceId = "general-store", DayIndex = day },
                    decision, day, fixture.Diagnostics);
                Assert.IsTrue(retry.Pending, "still pending on day " + day);
            }

            Assert.AreEqual(1, fixture.Service.Ledger.PendingRequests.Count,
                "exactly one pending request after six attempts — no duplicates, no loop");
            Assert.AreEqual(0, fixture.Obligations.Obligations.Count,
                "no obligation was created without owner approval");
            Assert.AreEqual(10000, business.RuntimeState.CurrentCashCents,
                "no cash moved without approval");
        }
    }
}
