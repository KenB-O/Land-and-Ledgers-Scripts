// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Pass IV (Worker 3) — Slice B tests: retail service desk.
//
// Real reconciliation figures: inventory units, shelf units, cash moved
// through HouseholdLedger (the ONE cash writer), missed sales.

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using LandLedgers.Primitives;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Economy;
using LandLedgers.Economy.Generic;
using LandLedgers.Population;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class RetailServiceDeskTests
    {
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private BusinessInstanceState business;
        private RetailServiceDesk desk;
        private RetailServiceHooks hooks;
        private Dictionary<EntityId, HouseholdLedger> personLedgers;
        private HouseholdLedger businessLedger;
        private EntityId clerk;
        private float clock;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
            personLedgers = new Dictionary<EntityId, HouseholdLedger>();
            businessLedger = new HouseholdLedger(9001);

            business = new BusinessInstanceState();
            SetInstanceId(business, "test-general-store");
            Assert.IsTrue(business.TryAssignPremises(42), "premises assigned");
            // Stock: 100 nails @ 50c cost; retail line @ 150c.
            business.GenericConfiguration.AddInventory("nails", 100f, 50);
            business.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("nails", "shelf-a", 20f, 100f, 10f, 150));
            desk = new RetailServiceDesk(business);
            desk.NoteShelfStock("nails", 20f);

            clerk = EntityId.For(EntityKind.Person, 101);
            string creason;
            Assert.IsTrue(desk.AddClerk(clerk, out creason), "clerk added: " + creason);

            clock = 0f;
            hooks = new RetailServiceHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                NowGameSeconds = () => clock,
                DayIndex = 42,
                LedgerForPerson = p => personLedgers.TryGetValue(p, out HouseholdLedger l) ? l : null,
                BusinessLedger = businessLedger,
                BusinessDisplayName = "Test General Store",
                Diagnostics = new List<string>(),
            };
        }

        private static void SetInstanceId(BusinessInstanceState b, string id)
        {
            FieldInfo f = typeof(BusinessInstanceState).GetField("instanceId",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(b, id);
            // Pass V P1-4: production routes sales by business type. This desk
            // is the generic-path desk, so the fixture business must be
            // Generic-typed (a GeneralStore-typed business sells from category
            // stock, which this fixture intentionally does not stock).
            FieldInfo t = typeof(BusinessInstanceState).GetField("businessType",
                BindingFlags.NonPublic | BindingFlags.Instance);
            t.SetValue(b, BusinessType.Generic);
            // The shared retail authority requires a non-null RuntimeState; real
            // businesses get one from BusinessInstanceState.Create. In tests we
            // attach a blank one (no category stock — the generic inventory path).
            FieldInfo r = typeof(BusinessInstanceState).GetField("runtimeState",
                BindingFlags.NonPublic | BindingFlags.Instance);
            r.SetValue(b, new BusinessRuntimeState());
        }

        private EntityId NewCustomer(int personId, int startingCashCents)
        {
            var person = EntityId.For(EntityKind.Person, personId);
            var ledger = new HouseholdLedger(8000 + personId);
            if (startingCashCents > 0)
            {
                string refusal = ledger.RecordInflow(42, startingCashCents,
                    HouseholdIncomeSource.OtherDocumented, "test-setup", "test funding for " + personId, "test");
                Assert.IsNull(refusal, "funding refused: " + refusal);
            }
            personLedgers[person] = ledger;
            return person;
        }

        private void WorkTaskToCompletion(EntityId taskId)
        {
            int guard = 0;
            while (guard++ < 20)
            {
                WorkTask task = tasks.FindTask(taskId);
                Assert.IsNotNull(task, "task vanished: " + taskId);
                if (task.Status == TaskStatus.Complete) return;
                Assert.IsTrue(desk.WorkOnTask(taskId, 60, hooks, out string reason),
                    "WorkOnTask failed: " + reason);
                task = tasks.FindTask(taskId);
                if (task.Status == TaskStatus.Complete) return;
            }
            Assert.Fail("task did not complete: " + taskId);
        }

        // ---------- eligibility: any inventory + premises business may retail ----------

        [Test]
        public void CanRetail_TrueForStockedShopWithPremises()
        {
            Assert.IsTrue(RetailServiceDesk.CanRetail(business, out string reason), "can retail: " + reason);
        }

        [Test]
        public void CanRetail_RefusedWithoutPremises()
        {
            var noShop = new BusinessInstanceState();
            SetInstanceId(noShop, "no-shop");
            noShop.GenericConfiguration.AddInventory("nails", 10f, 50);
            Assert.IsFalse(RetailServiceDesk.CanRetail(noShop, out string reason));
            StringAssert.Contains("premises", reason.ToLowerInvariant(), "reason names premises: " + reason);
        }

        [Test]
        public void CanRetail_RefusedWithoutInventory()
        {
            var empty = new BusinessInstanceState();
            SetInstanceId(empty, "empty-shop");
            empty.TryAssignPremises(7);
            Assert.IsFalse(RetailServiceDesk.CanRetail(empty, out string reason));
            StringAssert.Contains("inventory", reason.ToLowerInvariant(), "reason names inventory: " + reason);
        }

        [Test]
        public void CustomerArrives_FabricatedCustomer_RefusedLoudly()
        {
            Assert.IsFalse(desk.CustomerArrives(EntityId.Invalid, "nails", 2, hooks, out string reason));
            StringAssert.Contains("fabricat", reason.ToLowerInvariant(), "reason names fabrication: " + reason);
            Assert.IsFalse(desk.CustomerArrives(EntityId.For(EntityKind.Business, 5), "nails", 2, hooks, out reason),
                "a business is not a person — refused");
            Assert.AreEqual(0, desk.Visits.Count, "no phantom visit recorded");
        }

        // ---------- full service episode with real cash ----------

        [Test]
        public void ServeCustomer_FullService_CashMovesThroughHouseholdLedger()
        {
            EntityId customer = NewCustomer(201, 10000);
            Assert.IsTrue(desk.CustomerArrives(customer, "nails", 2, hooks, out string areason), areason);
            desk.Tick(hooks);

            CustomerVisit visit = desk.Visits[0];
            Assert.AreEqual(CustomerVisitStatus.BeingServed, visit.Status, "clerk picked up the customer");
            Assert.AreEqual(clerk, visit.ServedBy, "real clerk assigned");
            Assert.IsTrue(visit.ServiceTaskId.IsValid, "real WorkTask created");

            // Service phases advance as the clerk works: retrieve -> measure/wrap -> payment.
            desk.WorkOnTask(visit.ServiceTaskId, 2, hooks, out _);
            Assert.AreEqual(RetailServicePhase.RetrievingStock, visit.Phase, "2m in: retrieving");
            desk.WorkOnTask(visit.ServiceTaskId, 2, hooks, out _);
            Assert.AreEqual(RetailServicePhase.MeasuringWrapping, visit.Phase, "4m in: measuring/wrapping");
            desk.WorkOnTask(visit.ServiceTaskId, 3, hooks, out _);
            Assert.AreEqual(RetailServicePhase.ReceivingPayment, visit.Phase, "7m in: receiving payment");
            WorkTaskToCompletion(visit.ServiceTaskId);

            Assert.AreEqual(CustomerVisitStatus.Served, visit.Status, "served");
            // Reconciliation: inventory 100 -> 98, shelf 20 -> 18.
            Assert.AreEqual(98f, business.GenericConfiguration.GetAvailableInventoryQuantity("nails"), 0.001f, "inventory -2");
            Assert.AreEqual(18f, desk.GetShelfUnits("nails"), 0.001f, "shelf -2");
            // Cash: buyer 10000 -> 9700, business 0 -> +300 (2 x 150c), source + reason named.
            Assert.AreEqual(9700, personLedgers[customer].GetBalanceCents(), "buyer paid 300c");
            Assert.AreEqual(300, businessLedger.GetBalanceCents(), "business received 300c");
            HouseholdLedgerEntry inflow = businessLedger.Entries[businessLedger.Entries.Count - 1];
            Assert.AreEqual(HouseholdIncomeSource.SaleProceeds, inflow.Source, "inflow names SaleProceeds");
            Assert.IsFalse(string.IsNullOrWhiteSpace(inflow.Reason), "inflow names a reason");
            StringAssert.Contains("nails", inflow.Reason, "reason names the product: " + inflow.Reason);
            Assert.AreEqual(300, desk.TotalSalesCents, "desk totals: 300c");
            Assert.AreEqual(2, desk.TotalUnitsSold, "desk totals: 2 units");
        }

        // ---------- understaffing: waiting, then missed sales ----------

        [Test]
        public void Understaffed_WaitingCustomers_MissedSalesRecorded()
        {
            EntityId c1 = NewCustomer(301, 10000);
            EntityId c2 = NewCustomer(302, 10000);
            EntityId c3 = NewCustomer(303, 10000);
            // Patience 5 minutes; service takes 10 minutes per customer; one clerk.
            Assert.IsTrue(desk.CustomerArrives(c1, "nails", 1, hooks, out _, 300f));
            Assert.IsTrue(desk.CustomerArrives(c2, "nails", 1, hooks, out _, 300f));
            Assert.IsTrue(desk.CustomerArrives(c3, "nails", 1, hooks, out _, 300f));
            desk.Tick(hooks);

            Assert.AreEqual(CustomerVisitStatus.BeingServed, desk.Visits[0].Status, "c1 served");
            Assert.AreEqual(CustomerVisitStatus.Waiting, desk.Visits[1].Status, "c2 waits — no second clerk");
            Assert.AreEqual(CustomerVisitStatus.Waiting, desk.Visits[2].Status, "c3 waits");

            // Serve c1 fully (10 min of work).
            WorkTaskToCompletion(desk.Visits[0].ServiceTaskId);
            Assert.AreEqual(CustomerVisitStatus.Served, desk.Visits[0].Status);

            // 6 minutes pass: c2 and c3's patience (5 min) expires while waiting.
            clock += 360f;
            desk.Tick(hooks);
            Assert.AreEqual(CustomerVisitStatus.LeftImpatient, desk.Visits[1].Status, "c2 left");
            Assert.AreEqual(CustomerVisitStatus.LeftImpatient, desk.Visits[2].Status, "c3 left");
            Assert.AreEqual(2, desk.MissedSales.Count, "two missed sales recorded");
            StringAssert.Contains("waiting", desk.MissedSales[0].Reason.ToLowerInvariant(),
                "missed reason names waiting: " + desk.MissedSales[0].Reason);

            // Reconciliation: only c1's sale moved anything.
            Assert.AreEqual(99f, business.GenericConfiguration.GetAvailableInventoryQuantity("nails"), 0.001f, "inventory -1");
            Assert.AreEqual(150, businessLedger.GetBalanceCents(), "business +150c (one sale)");
            Assert.AreEqual(10000, personLedgers[c2].GetBalanceCents(), "c2 paid nothing");
            Assert.AreEqual(10000, personLedgers[c3].GetBalanceCents(), "c3 paid nothing");
        }

        // ---------- restock is real labor ----------

        [Test]
        public void RestockShelves_RealLaborThenServe()
        {
            desk.NoteShelfStock("nails", 1f); // only 1 on the shelf; bulk holds 100
            EntityId customer = NewCustomer(401, 10000);
            Assert.IsTrue(desk.CustomerArrives(customer, "nails", 2, hooks, out _, 3600f));
            desk.Tick(hooks);

            Assert.AreEqual(CustomerVisitStatus.Waiting, desk.Visits[0].Status, "waits — shelf short");
            Assert.AreEqual(1, desk.RestockJobs.Count, "restock job queued");
            RetailRestockJob job = desk.RestockJobs[0];
            Assert.IsTrue(job.TaskId.IsValid, "restock is a real WorkTask");
            WorkTaskToCompletion(job.TaskId);
            Assert.IsTrue(job.Done, "restock done");
            Assert.AreEqual(2f, desk.GetShelfUnits("nails"), 0.001f, "shelf now holds 2");

            desk.Tick(hooks);
            Assert.AreEqual(CustomerVisitStatus.BeingServed, desk.Visits[0].Status, "served after restock");
            WorkTaskToCompletion(desk.Visits[0].ServiceTaskId);
            Assert.AreEqual(CustomerVisitStatus.Served, desk.Visits[0].Status);
            Assert.AreEqual(98f, business.GenericConfiguration.GetAvailableInventoryQuantity("nails"), 0.001f, "inventory -2 total");
        }

        // ---------- deliveries: no teleported goods ----------

        [Test]
        public void AcceptDelivery_BooksInventoryWithProvenance_NoDuplicates()
        {
            Assert.IsTrue(desk.NoteIncomingDelivery("del-1", "ship-77", "nails", 10, 45, "Acme Freight", hooks, out string nreason), nreason);
            // Noting books nothing — goods enter only when the acceptance task completes.
            Assert.AreEqual(100f, business.GenericConfiguration.GetAvailableInventoryQuantity("nails"), 0.001f, "nothing booked on note");
            desk.Tick(hooks);
            RetailDeliveryAcceptance delivery = desk.Deliveries[0];
            Assert.IsTrue(delivery.TaskId.IsValid, "acceptance is a real WorkTask");
            WorkTaskToCompletion(delivery.TaskId);
            Assert.IsTrue(delivery.Done);
            Assert.AreEqual(110f, business.GenericConfiguration.GetAvailableInventoryQuantity("nails"), 0.001f, "inventory +10");
            Assert.AreEqual(30f, desk.GetShelfUnits("nails"), 0.001f, "shelf +10");

            // Duplicate delivery id refused — no duplicated inventory.
            Assert.IsFalse(desk.NoteIncomingDelivery("del-1", "ship-77", "nails", 10, 45, "Acme Freight", hooks, out string dreason));
            StringAssert.Contains("duplicate", dreason.ToLowerInvariant(), "duplicate refused: " + dreason);
            Assert.AreEqual(110f, business.GenericConfiguration.GetAvailableInventoryQuantity("nails"), 0.001f, "still 110");
        }

        // ---------- cannot pay: no sale, inventory rolls back ----------

        [Test]
        public void CannotPay_SaleRefused_InventoryUnmoved()
        {
            EntityId broke = NewCustomer(501, 0); // no cash
            Assert.IsTrue(desk.CustomerArrives(broke, "nails", 2, hooks, out _, 3600f));
            desk.Tick(hooks);
            EntityId serviceTask = desk.Visits[0].ServiceTaskId;
            // Work the service to completion; the sale refuses at settlement
            // (WorkOnTask returns false with the loud reason — the task itself completed).
            int guard = 0;
            while (guard++ < 10)
            {
                WorkTask task = tasks.FindTask(serviceTask);
                if (task.Status == TaskStatus.Complete) break;
                desk.WorkOnTask(serviceTask, 60, hooks, out string wreason);
            }
            Assert.AreEqual(TaskStatus.Complete, tasks.FindTask(serviceTask).Status, "service labor completed");

            Assert.AreEqual(CustomerVisitStatus.SaleRefused, desk.Visits[0].Status, "sale refused — cannot pay");
            Assert.AreEqual(100f, business.GenericConfiguration.GetAvailableInventoryQuantity("nails"), 0.001f, "inventory rolled back");
            Assert.AreEqual(20f, desk.GetShelfUnits("nails"), 0.001f, "shelf untouched");
            Assert.AreEqual(0, businessLedger.GetBalanceCents(), "business received nothing");
            Assert.AreEqual(1, desk.MissedSales.Count, "missed sale recorded");
        }

        // ---------- save / load mid-service ----------

        [Test]
        public void SaveLoad_MidService_ResumesWithoutDoubleSale()
        {
            EntityId customer = NewCustomer(601, 10000);
            Assert.IsTrue(desk.CustomerArrives(customer, "nails", 2, hooks, out _, 3600f));
            desk.Tick(hooks);
            EntityId serviceTask = desk.Visits[0].ServiceTaskId;
            desk.WorkOnTask(serviceTask, 5, hooks, out _); // halfway through service

            RetailServiceDeskSaveDto dto = desk.CaptureSaveDto();
            var desk2 = new RetailServiceDesk(business);
            desk2.RestoreSaveDto(dto);
            desk2.RebindTasks(hooks);

            Assert.AreEqual(CustomerVisitStatus.BeingServed, desk2.Visits[0].Status, "still being served after load");
            Assert.AreEqual(serviceTask, desk2.Visits[0].ServiceTaskId, "task rebound, not duplicated");
            WorkTaskToCompletion(desk2.Visits[0].ServiceTaskId);
            Assert.AreEqual(CustomerVisitStatus.Served, desk2.Visits[0].Status);
            Assert.AreEqual(300, businessLedger.GetBalanceCents(), "exactly one sale settled");
            Assert.AreEqual(98f, business.GenericConfiguration.GetAvailableInventoryQuantity("nails"), 0.001f, "inventory -2 once");
        }

        // ---------- clerk availability is real ----------

        [Test]
        public void ClerkWithNoBudget_ServesNobody_CustomersWait()
        {
            budgets.GetOrCreate(clerk).TryCommit(600, out _); // burn the whole day budget
            EntityId customer = NewCustomer(701, 10000);
            Assert.IsTrue(desk.CustomerArrives(customer, "nails", 1, hooks, out _, 3600f));
            desk.Tick(hooks);
            Assert.AreEqual(CustomerVisitStatus.Waiting, desk.Visits[0].Status, "waits — clerk has no budget left");
            Assert.IsFalse(desk.Visits[0].ServiceTaskId.IsValid, "no task conjured without a worker");
        }
    }
}
