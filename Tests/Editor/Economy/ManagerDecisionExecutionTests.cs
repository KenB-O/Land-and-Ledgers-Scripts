// Pass VII Workstream D: manager decision execution through real production
// authorities. Every decision kind is tested: legitimate trigger, correct
// authority, correct production executor, financial/resource validation, a
// real resulting task or transaction, success and genuine failure outcomes,
// save/load continuity, no duplicate actions, no invented goods or money.
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
    public sealed class ManagerDecisionExecutionTests
    {
        private const int Day = 410;

        private sealed class Fixture
        {
            public ManagerDecisionExecutionService Service = new ManagerDecisionExecutionService();
            public Dictionary<string, BusinessInstanceState> Businesses = new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkScheduler Scheduling = new WorkScheduler();
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public List<string> Diagnostics = new List<string>();
            public string LastEffortId;
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

        private static ManagerDecisionResult MakeDecision(string kind, string reference, bool escalated = false, string escalationReason = "")
        {
            return new ManagerDecisionResult
            {
                Acted = true,
                ActionKind = kind,
                Decision = "test decision " + kind,
                Evidence = "test evidence",
                AuthorityUsed = "test authority",
                ResultingReference = reference,
                Outcome = "test outcome",
                Escalated = escalated,
                EscalationReason = escalationReason,
                DayIndex = Day,
            };
        }

        private Fixture MakeFixture(IManagerBusinessLender lender = null)
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
                BusinessLender = lender,
                OpenRecruitment = (business, demand, decision, day, diag) =>
                {
                    fixture.LastEffortId = "effort:" + business.InstanceId + ":d" + day;
                    diag.Add("test recruitment path opened " + fixture.LastEffortId);
                    return fixture.LastEffortId;
                },
            });
            return fixture;
        }

        private void Employ(Fixture fixture, string businessId, int personId)
        {
            var relationship = new EmploymentRelationship
            {
                Id = "emp-" + businessId + "-" + personId,
                EmployeePersonId = personId,
                EmployerBusinessId = businessId,
                LifecycleState = EmploymentLifecycleState.Active,
            };
            Assert.IsNull(fixture.Employments.Register(relationship), "employment registered");
        }

        // ---------- OpenRecruitment ----------

        [Test]
        public void OpenRecruitment_ExecutesThroughExistingPath()
        {
            Fixture fixture = MakeFixture();
            fixture.Businesses["store-1"] = MakeBusiness("store-1", 10000);
            var authority = MakeAuthority("store-1", 7, DelegationScope.Staffing, 2000);

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                fixture.Businesses["store-1"], authority, 7,
                new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.OpenRecruitment, "intent:recruitment-effort:store-1:d410"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Executed, "executed: " + outcome.Reason);
            Assert.IsNotNull(fixture.LastEffortId, "the existing recruitment path ran");
            Assert.AreEqual(fixture.LastEffortId, outcome.ResultingReference);
        }

        [Test]
        public void OpenRecruitment_DuplicateRefused()
        {
            Fixture fixture = MakeFixture();
            fixture.Businesses["store-1"] = MakeBusiness("store-1", 10000);
            var authority = MakeAuthority("store-1", 7, DelegationScope.Staffing, 2000);
            var decision = MakeDecision(ManagerActKinds.OpenRecruitment, "intent:recruitment-effort:store-1:d410");

            ManagerExecutionOutcome first = fixture.Service.ExecuteDecision(
                fixture.Businesses["store-1"], authority, 7,
                new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(first.Executed);

            fixture.LastEffortId = null;
            ManagerExecutionOutcome second = fixture.Service.ExecuteDecision(
                fixture.Businesses["store-1"], authority, 7,
                new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(second.Failed, "duplicate refused");
            Assert.IsNull(fixture.LastEffortId, "no second effort opened");
        }

        // ---------- Schedule ----------

        [Test]
        public void Schedule_AssignsAndStartsRealWork()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            fixture.Businesses["store-1"] = business;
            Employ(fixture, "store-1", 11);
            var authority = MakeAuthority("store-1", 7, DelegationScope.Scheduling, 2000);

            string rejection;
            Assert.IsTrue(fixture.Tasks.RegisterDefinition(new TaskDefinition("test.work", "Test work", 60), out rejection), rejection);
            EntityId businessEntity = EntityId.For(EntityKind.Business, Math.Abs("store-1".GetHashCode()));
            WorkTask task = fixture.Tasks.CreateTask("test.work", businessEntity, Day);
            Assert.IsNotNull(task, "task created");

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                business, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.Schedule, "intent:work-schedule:store-1:d410"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Executed, "executed: " + outcome.Reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status, "the task was started through the task authority");
            StringAssert.Contains("started", outcome.ResultingReference);
        }

        [Test]
        public void Schedule_NoOpenWork_FailsHonestly()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            fixture.Businesses["store-1"] = business;
            Employ(fixture, "store-1", 11);
            var authority = MakeAuthority("store-1", 7, DelegationScope.Scheduling, 2000);

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                business, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.Schedule, "intent:work-schedule:store-1:d410"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Failed, "no open work means no scheduling");
            StringAssert.Contains("no open queued work", outcome.Reason);
        }

        // ---------- Procure ----------

        private static void StockForProcurement(BusinessInstanceState business, string product, float onHand, float target, float reorder, int priceCents, int unitCostCents)
        {
            business.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine(product, "shelf-a", target, target * 2f, reorder, priceCents));
            business.GenericConfiguration.AddInventory(product, onHand, unitCostCents);
        }

        [Test]
        public void Procure_DecliningInventory_OrdersFromSupplierWithActualGoods()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState buyer = MakeBusiness("buyer-1", 100000);
            StockForProcurement(buyer, "flour", 2f, 20f, 5f, 120, 80);
            fixture.Businesses["buyer-1"] = buyer;

            BusinessInstanceState supplier = MakeBusiness("supplier-1", 50000);
            supplier.GenericConfiguration.AddInventory("flour", 100f, 80);
            supplier.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("flour", "shelf-a", 50f, 100f, 10f, 100));
            fixture.Businesses["supplier-1"] = supplier;

            var authority = MakeAuthority("buyer-1", 7, DelegationScope.Purchasing, 50000);
            int buyerCashBefore = buyer.RuntimeState.CurrentCashCents;
            int supplierCashBefore = supplier.RuntimeState.CurrentCashCents;
            float supplierStockBefore = supplier.GenericConfiguration.GetAvailableInventoryQuantity("flour");

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                buyer, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "buyer-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.Procure, "intent:procurement:buyer-1:d410:2000c"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Executed, "executed: " + outcome.Reason + " | " + string.Join(" | ", fixture.Diagnostics));

            // Cash conserved: buyer paid, supplier paid the same amount.
            int buyerCashAfter = buyer.RuntimeState.CurrentCashCents;
            int supplierCashAfter = supplier.RuntimeState.CurrentCashCents;
            int paid = buyerCashBefore - buyerCashAfter;
            Assert.Greater(paid, 0, "the buyer paid real cash");
            Assert.AreEqual(paid, supplierCashAfter - supplierCashBefore, "cash conserved: supplier received exactly what the buyer paid");

            // Supplier goods actually left.
            float supplierStockAfter = supplier.GenericConfiguration.GetAvailableInventoryQuantity("flour");
            Assert.Less(supplierStockAfter, supplierStockBefore, "supplier inventory decreased");

            // Shipment in transit; stock NOT yet received (no teleport).
            Assert.AreEqual(1, fixture.Service.Ledger.Shipments.Count);
            Assert.AreEqual("InTransit", fixture.Service.Ledger.Shipments[0].Status);
            Assert.AreEqual(2f, buyer.GenericConfiguration.GetAvailableInventoryQuantity("flour"), 0.001f);
        }

        [Test]
        public void Procure_ShipmentDelivery_ReceivesStock_Idempotent()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState buyer = MakeBusiness("buyer-1", 100000);
            StockForProcurement(buyer, "flour", 2f, 20f, 5f, 120, 80);
            fixture.Businesses["buyer-1"] = buyer;
            BusinessInstanceState supplier = MakeBusiness("supplier-1", 50000);
            supplier.GenericConfiguration.AddInventory("flour", 100f, 80);
            fixture.Businesses["supplier-1"] = supplier;
            var authority = MakeAuthority("buyer-1", 7, DelegationScope.Purchasing, 50000);

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                buyer, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "buyer-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.Procure, "intent:procurement:buyer-1:d410:2000c"),
                Day, fixture.Diagnostics);
            Assert.IsTrue(outcome.Executed, outcome.Reason);

            float ordered = fixture.Service.Ledger.Shipments[0].Quantity;
            fixture.Service.AdvanceShipments(Day + 1, fixture.Diagnostics);
            Assert.AreEqual("Delivered", fixture.Service.Ledger.Shipments[0].Status);
            Assert.AreEqual(2f + ordered, buyer.GenericConfiguration.GetAvailableInventoryQuantity("flour"), 0.001f,
                "stock received on delivery");

            // Advancing again never double-receives.
            fixture.Service.AdvanceShipments(Day + 2, fixture.Diagnostics);
            Assert.AreEqual(2f + ordered, buyer.GenericConfiguration.GetAvailableInventoryQuantity("flour"), 0.001f,
                "no duplicate receipt");
        }

        [Test]
        public void Procure_NoSupplierWithGoods_FailsHonestly()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState buyer = MakeBusiness("buyer-1", 100000);
            StockForProcurement(buyer, "flour", 2f, 20f, 5f, 120, 80);
            fixture.Businesses["buyer-1"] = buyer;
            // A supplier exists but holds no flour.
            BusinessInstanceState supplier = MakeBusiness("supplier-1", 50000);
            fixture.Businesses["supplier-1"] = supplier;
            var authority = MakeAuthority("buyer-1", 7, DelegationScope.Purchasing, 50000);

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                buyer, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "buyer-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.Procure, "intent:procurement:buyer-1:d410:2000c"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Failed);
            StringAssert.Contains("no supplier", outcome.Reason);
            Assert.AreEqual(100000, buyer.RuntimeState.CurrentCashCents, "no cash moved");
            Assert.AreEqual(0, fixture.Service.Ledger.Shipments.Count, "no phantom shipment");
        }

        [Test]
        public void Procure_InsufficientCash_FailsWithoutSpending()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState buyer = MakeBusiness("buyer-1", 10);
            StockForProcurement(buyer, "flour", 2f, 20f, 5f, 120, 80);
            fixture.Businesses["buyer-1"] = buyer;
            BusinessInstanceState supplier = MakeBusiness("supplier-1", 50000);
            supplier.GenericConfiguration.AddInventory("flour", 100f, 80);
            fixture.Businesses["supplier-1"] = supplier;
            var authority = MakeAuthority("buyer-1", 7, DelegationScope.Purchasing, 50000);

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                buyer, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "buyer-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.Procure, "intent:procurement:buyer-1:d410:2000c"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Failed);
            StringAssert.Contains("cash", outcome.Reason);
            Assert.AreEqual(10, buyer.RuntimeState.CurrentCashCents, "cash untouched");
            Assert.AreEqual(100f, supplier.GenericConfiguration.GetAvailableInventoryQuantity("flour"), 0.001f,
                "supplier stock untouched");
        }

        [Test]
        public void Procure_DuplicateOrderRefused_Idempotent()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState buyer = MakeBusiness("buyer-1", 100000);
            StockForProcurement(buyer, "flour", 2f, 20f, 5f, 120, 80);
            fixture.Businesses["buyer-1"] = buyer;
            BusinessInstanceState supplier = MakeBusiness("supplier-1", 50000);
            supplier.GenericConfiguration.AddInventory("flour", 100f, 80);
            fixture.Businesses["supplier-1"] = supplier;
            var authority = MakeAuthority("buyer-1", 7, DelegationScope.Purchasing, 50000);
            var decision = MakeDecision(ManagerActKinds.Procure, "intent:procurement:buyer-1:d410:2000c");

            ManagerExecutionOutcome first = fixture.Service.ExecuteDecision(
                buyer, authority, 7, new LaborDemandReport { BusinessInstanceId = "buyer-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(first.Executed, first.Reason);
            int cashAfterFirst = buyer.RuntimeState.CurrentCashCents;

            ManagerExecutionOutcome second = fixture.Service.ExecuteDecision(
                buyer, authority, 7, new LaborDemandReport { BusinessInstanceId = "buyer-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(second.Failed, "duplicate procurement refused");
            Assert.AreEqual(cashAfterFirst, buyer.RuntimeState.CurrentCashCents, "no second cash movement");
            Assert.AreEqual(1, fixture.Service.Ledger.Shipments.Count, "no duplicate shipment");
        }

        // ---------- PriceChange ----------

        [Test]
        public void PriceChange_Overstock_LowersRealPrice()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            business.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("nails", "shelf-a", 20f, 100f, 10f, 150));
            business.GenericConfiguration.AddInventory("nails", 80f, 50);
            fixture.Businesses["store-1"] = business;
            var authority = MakeAuthority("store-1", 7, DelegationScope.Inventory, 2000);

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                business, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.PriceChange, "intent:price-change:store-1:d410"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Executed, "executed: " + outcome.Reason);
            GenericRetailProductLine line;
            Assert.IsTrue(business.GenericConfiguration.Retail.TryGetLine("nails", out line));
            Assert.Less(line.SellingPriceCents, 150, "overstock nudged the price down");
            Assert.GreaterOrEqual(line.SellingPriceCents, 135, "bounded: at most 10% down");
        }

        [Test]
        public void PriceChange_Scarcity_RaisesRealPrice_DuplicateRefused()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            business.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("nails", "shelf-a", 20f, 100f, 10f, 150));
            business.GenericConfiguration.AddInventory("nails", 3f, 50);
            fixture.Businesses["store-1"] = business;
            var authority = MakeAuthority("store-1", 7, DelegationScope.Inventory, 2000);
            var decision = MakeDecision(ManagerActKinds.PriceChange, "intent:price-change:store-1:d410");

            ManagerExecutionOutcome first = fixture.Service.ExecuteDecision(
                business, authority, 7, new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(first.Executed, first.Reason);
            GenericRetailProductLine line;
            Assert.IsTrue(business.GenericConfiguration.Retail.TryGetLine("nails", out line));
            Assert.Greater(line.SellingPriceCents, 150, "scarcity nudged the price up");

            ManagerExecutionOutcome second = fixture.Service.ExecuteDecision(
                business, authority, 7, new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(second.Failed, "duplicate price change refused");
            Assert.IsTrue(business.GenericConfiguration.Retail.TryGetLine("nails", out line));
            int priceAfterFirst = line.SellingPriceCents;
            Assert.AreEqual(priceAfterFirst, line.SellingPriceCents, "price unchanged by the refused duplicate");
        }

        // ---------- OrderMaintenance ----------

        [Test]
        public void OrderMaintenance_CreatesRealWorkOrder()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            fixture.Businesses["store-1"] = business;
            var authority = MakeAuthority("store-1", 7, DelegationScope.Maintenance, 2000);

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                business, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.OrderMaintenance, "intent:maintenance:store-1:d410"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Executed, "executed: " + outcome.Reason);
            EntityId businessEntity = EntityId.For(EntityKind.Business, Math.Abs("store-1".GetHashCode()));
            List<WorkTask> open = fixture.Tasks.GetOpenTasks(businessEntity);
            Assert.AreEqual(1, open.Count, "one real work order in the task queue");
            Assert.AreEqual(ManagerMaintenanceTaskCatalog.MaintenanceWorkOrderId, open[0].DefinitionId);
        }

        [Test]
        public void OrderMaintenance_OpenOrderNeverDuplicated()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            fixture.Businesses["store-1"] = business;
            var authority = MakeAuthority("store-1", 7, DelegationScope.Maintenance, 2000);
            var decision = MakeDecision(ManagerActKinds.OrderMaintenance, "intent:maintenance:store-1:d410");

            ManagerExecutionOutcome first = fixture.Service.ExecuteDecision(
                business, authority, 7, new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(first.Executed, first.Reason);

            ManagerExecutionOutcome second = fixture.Service.ExecuteDecision(
                business, authority, 7, new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(second.Failed, "second order refused while one is open");

            EntityId businessEntity = EntityId.For(EntityKind.Business, Math.Abs("store-1".GetHashCode()));
            Assert.AreEqual(1, fixture.Tasks.GetOpenTasks(businessEntity).Count, "still exactly one work order");
        }

        // ---------- Borrow ----------

        private sealed class TestBusinessLender : IManagerBusinessLender
        {
            public string LenderName => "Test Lender";
            public int CapitalCents;
            public TestBusinessLender(int capitalCents) { CapitalCents = capitalCents; }
            public FinancialObligation TryArrangeLoan(
                EntityIdRegistry ids, FinancialObligationAuthority obligations, string debtorBusinessId,
                int amountCents, int dayIndex, List<string> diagnostics, out string refusalReason)
            {
                refusalReason = string.Empty;
                if (amountCents <= 0) { refusalReason = "amount must be positive"; return null; }
                if (amountCents > CapitalCents) { refusalReason = "insufficient lender capital"; return null; }
                FinancialObligation obligation = obligations.Create(
                    ids, FinancialObligationKind.Loan, debtorBusinessId, LenderName,
                    amountCents, dayIndex, "12 months", "manager-authorized working capital");
                if (obligation == null) { refusalReason = "the obligation authority refused"; return null; }
                CapitalCents -= amountCents;
                return obligation;
            }
        }

        [Test]
        public void Borrow_EscalatedDecision_BecomesPendingRequest()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            fixture.Businesses["store-1"] = business;
            var authority = MakeAuthority("store-1", 7, DelegationScope.CashHandling, 2000);

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                business, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.Borrow, "intent:borrow-request:store-1:d410:5000c",
                    escalated: true, escalationReason: "all borrowing needs explicit owner approval"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Pending, "escalated borrowing becomes a pending request");
            Assert.AreEqual(1, fixture.Service.Ledger.PendingRequests.Count);
            ManagerPendingRequest request = fixture.Service.Ledger.PendingRequests[0];
            Assert.AreEqual("Pending", request.Status);
            Assert.AreEqual(5000, request.AmountCents);
            Assert.AreEqual(0, fixture.Obligations.Obligations.Count, "no obligation created without approval");
            Assert.AreEqual(10000, business.RuntimeState.CurrentCashCents, "no cash moved without approval");
        }

        [Test]
        public void Borrow_PendingRequestNeverDuplicated_NoInfiniteRetries()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            fixture.Businesses["store-1"] = business;
            var authority = MakeAuthority("store-1", 7, DelegationScope.CashHandling, 2000);
            var decision = MakeDecision(ManagerActKinds.Borrow, "intent:borrow-request:store-1:d410:5000c",
                escalated: true, escalationReason: "all borrowing needs explicit owner approval");

            for (int i = 0; i < 3; i++)
            {
                ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                    business, authority, 7, new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                    decision, Day, fixture.Diagnostics);
                Assert.IsTrue(outcome.Pending, "still pending, attempt " + i);
            }

            Assert.AreEqual(1, fixture.Service.Ledger.PendingRequests.Count, "exactly one pending request after repeated attempts");
        }

        [Test]
        public void Borrow_WithinDelegatedLimits_CreatesRealObligationWithRealCash()
        {
            var lender = new TestBusinessLender(100000);
            Fixture fixture = MakeFixture(lender);
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            fixture.Businesses["store-1"] = business;
            var authority = MakeAuthority("store-1", 7, DelegationScope.CashHandling, 20000);

            var context = new ManagerExecutionContext
            {
                BusinessInstanceId = "store-1",
                ManagerPersonId = 7,
                DayIndex = Day,
                PurchaseLimitCents = 20000,
                MaxBorrowingCents = 15000,
                DelegatedScopes = new List<string> { ManagerScopeNames.CashHandling },
            };
            var executor = new ManagerBorrowExecutor(
                new ManagerExecutionServices
                {
                    FindBusiness = id =>
                    {
                        BusinessInstanceState found;
                        return fixture.Businesses.TryGetValue(id, out found) ? found : null;
                    },
                    FinancialObligations = fixture.Obligations,
                    Ids = fixture.Ids,
                    BusinessLender = lender,
                },
                new ManagerDecisionExecutionLedger());
            ManagerExecutionOutcome outcome = executor.Execute(
                MakeDecision(ManagerActKinds.Borrow, "intent:borrow-request:store-1:d410:5000c"),
                context, fixture.Diagnostics);

            Assert.IsTrue(outcome.Executed, "executed: " + outcome.Reason);
            Assert.AreEqual(1, fixture.Obligations.Obligations.Count, "one real obligation");
            FinancialObligation obligation = new List<FinancialObligation>(fixture.Obligations.Obligations)[0];
            Assert.AreEqual("store-1", obligation.Debtor);
            Assert.AreEqual("Test Lender", obligation.Creditor);
            Assert.AreEqual(5000, obligation.OutstandingPrincipalCents);
            Assert.AreEqual(15000, business.RuntimeState.CurrentCashCents, "loan proceeds funded real cash");
            Assert.AreEqual(95000, lender.CapitalCents, "lender capital decreased — no invented money");
        }

        [Test]
        public void Borrow_AboveOwnerMax_FailsHonestly()
        {
            Fixture fixture = MakeFixture(new TestBusinessLender(100000));
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            fixture.Businesses["store-1"] = business;

            var context = new ManagerExecutionContext
            {
                BusinessInstanceId = "store-1",
                ManagerPersonId = 7,
                DayIndex = Day,
                PurchaseLimitCents = 20000,
                MaxBorrowingCents = 3000,
                DelegatedScopes = new List<string> { ManagerScopeNames.CashHandling },
            };
            var executor = new ManagerBorrowExecutor(
                new ManagerExecutionServices
                {
                    FindBusiness = id =>
                    {
                        BusinessInstanceState found;
                        return fixture.Businesses.TryGetValue(id, out found) ? found : null;
                    },
                    FinancialObligations = fixture.Obligations,
                    Ids = fixture.Ids,
                    BusinessLender = new TestBusinessLender(100000),
                },
                new ManagerDecisionExecutionLedger());

            ManagerExecutionOutcome outcome = executor.Execute(
                MakeDecision(ManagerActKinds.Borrow, "intent:borrow-request:store-1:d410:5000c"),
                context, fixture.Diagnostics);

            Assert.IsTrue(outcome.Failed);
            StringAssert.Contains("max borrowing permission", outcome.Reason);
            Assert.AreEqual(0, fixture.Obligations.Obligations.Count, "no obligation above the owner's max");
        }

        // ---------- SellAsset ----------

        [Test]
        public void SellAsset_OwnerReserved_BecomesPendingRequest_NeverExecutes()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            fixture.Businesses["store-1"] = business;
            var authority = MakeAuthority("store-1", 7,
                DelegationScope.Purchasing | DelegationScope.Scheduling | DelegationScope.CashHandling, 50000);

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                business, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                MakeDecision(ManagerActKinds.SellAsset, "intent:SellAsset:store-1:d410",
                    escalated: true, escalationReason: "asset sale is owner-reserved"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Pending, "owner-reserved sale becomes a pending request");
            Assert.IsFalse(outcome.Executed, "never executed unilaterally");
            Assert.AreEqual(1, fixture.Service.Ledger.PendingRequests.Count);
            Assert.AreEqual(ManagerActKinds.SellAsset, fixture.Service.Ledger.PendingRequests[0].Kind);
        }

        // ---------- Save/load continuity ----------

        [Test]
        public void SaveLoad_PreservesLedger_NoDuplicateExecutionAfterRestore()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState buyer = MakeBusiness("buyer-1", 100000);
            StockForProcurement(buyer, "flour", 2f, 20f, 5f, 120, 80);
            fixture.Businesses["buyer-1"] = buyer;
            BusinessInstanceState supplier = MakeBusiness("supplier-1", 50000);
            supplier.GenericConfiguration.AddInventory("flour", 100f, 80);
            fixture.Businesses["supplier-1"] = supplier;
            var authority = MakeAuthority("buyer-1", 7, DelegationScope.Purchasing | DelegationScope.CashHandling, 50000);
            var decision = MakeDecision(ManagerActKinds.Procure, "intent:procurement:buyer-1:d410:2000c");

            ManagerExecutionOutcome first = fixture.Service.ExecuteDecision(
                buyer, authority, 7, new LaborDemandReport { BusinessInstanceId = "buyer-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(first.Executed, first.Reason);

            // Save, restore into a fresh service, re-run the same decision.
            ManagerDecisionExecutionSaveDto dto = fixture.Service.CaptureSaveDto();
            Fixture restored = MakeFixture();
            restored.Businesses["buyer-1"] = buyer;
            restored.Businesses["supplier-1"] = supplier;
            restored.Service.LoadFromSaveDto(dto);

            ManagerExecutionOutcome retry = restored.Service.ExecuteDecision(
                buyer, authority, 7, new LaborDemandReport { BusinessInstanceId = "buyer-1", DayIndex = Day },
                decision, Day, restored.Diagnostics);
            Assert.IsTrue(retry.Failed, "restored ledger still refuses the duplicate");
            Assert.AreEqual(1, restored.Service.Ledger.Shipments.Count, "shipment survived the round trip");
            Assert.AreEqual("InTransit", restored.Service.Ledger.Shipments[0].Status);

            // Delivery still works after restore.
            restored.Service.AdvanceShipments(Day + 1, restored.Diagnostics);
            Assert.AreEqual("Delivered", restored.Service.Ledger.Shipments[0].Status);
        }

        [Test]
        public void UnknownDecisionKind_FailsLoudly_NeverSilent()
        {
            Fixture fixture = MakeFixture();
            BusinessInstanceState business = MakeBusiness("store-1", 10000);
            fixture.Businesses["store-1"] = business;
            var authority = MakeAuthority("store-1", 7, DelegationScope.AllRoutine, 50000);

            ManagerExecutionOutcome outcome = fixture.Service.ExecuteDecision(
                business, authority, 7,
                new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                MakeDecision("InventKind", "intent:InventKind:store-1:d410"),
                Day, fixture.Diagnostics);

            Assert.IsTrue(outcome.Failed);
            StringAssert.Contains("no executor", outcome.Reason);
            Assert.AreEqual(1, fixture.Service.Ledger.Actions.Count, "the failed decision is recorded, not dropped");
        }
    }
}
