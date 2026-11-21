// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Lane 9d tests: freight recovery and settlement.
//
// Pins the pure seams: shipment/lot ETA and overdue judgment (E-18),
// terminal-request reopen + lot release/refund conservation (F6-2 / F-02),
// the staging deadline (C-4), the Queued sweep bound (F6-2 Queued gap),
// the intake-gated import receive (E R-08), and the receiving-point
// resolver (M-R5). The SimulationDrivers wiring that calls these seams is
// harness-excluded and is verified by inspection against this table, not
// by a harness test. Uses StringAssert.Contains, never Does.Contain: the
// harness NUnit shim does not support Does.Contain.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Trade;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;
using LogisticsShipmentState = global::LandLedgers.Economy.LogisticsShipmentState;
using LogisticsShipmentStatus = global::LandLedgers.Economy.LogisticsShipmentStatus;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class Lane9dFreightRecoveryTests
    {
        // BuildingActivityLocationType.Receiving == 5 in production
        // (BuildingFootprintAuthority.cs); the production enum is
        // Unity-bound and shimmed out of the harness, so the value is
        // carried as a literal here with its source named.
        private const int ReceivingType = 5;

        private sealed class FakeBusinessCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }
            public bool RefuseCredit;

            public FakeBusinessCashPort(int balanceCents) => BalanceCents = balanceCents;

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents <= 0 || amountCents > BalanceCents)
                    return "test cash port refused debit";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (RefuseCredit) return "test cash port refused credit";
                if (amountCents <= 0) return "test cash port refused credit";
                BalanceCents += amountCents;
                return null;
            }
        }

        private static OffMapAidBusinessDefinition MillbrookDefinition()
        {
            OffMapAidBusinessDefinition definition = ScriptableObject.CreateInstance<OffMapAidBusinessDefinition>();
            definition.BusinessId = "millbrook_grain_co";
            definition.DisplayName = "Millbrook Grain Co.";
            definition.PickupRouteMilesFromTown = 50f;
            definition.Inventory = new List<OffMapAidInventoryRow>
            {
                new OffMapAidInventoryRow
                {
                    ItemId = "bakery_flour_sack", DisplayName = "Flour sack", InventoryUnit = "lb",
                    StartingUnits = 230, OrderUnit = "sack", InventoryUnitsPerOrderUnit = 10,
                    BakeryUnitsPerOrderUnit = 18, OrderPriceCents = 48,
                },
            };
            return definition;
        }

        private static (OffMapAidBusinessRuntime Supplier, FakeBusinessCashPort BakeryCash, OffMapAidCargoLot Lot)
            PlaceMillbrookOrder(string requestId = "opening-flour-b1", int buyerBalance = 20000)
        {
            var supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            var bakeryCash = new FakeBusinessCashPort(buyerBalance);
            string refusal = supplier.TryPlaceOrder(requestId, "bakery_001", "Glen Pierce", 1, bakeryCash, 0,
                out OffMapAidCargoLot lot);
            Assert.IsNull(refusal, refusal);
            Assert.IsNotNull(lot);
            return (supplier, bakeryCash, lot);
        }

        private static FreightServiceRequest NewRequest(string requestId, string lotId) => new FreightServiceRequest
        {
            RequestId = requestId, RequestingBusinessId = "bakery-1",
            CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
            DestinationBusinessId = "bakery-1", CargoLotId = lotId, Quantity = 1,
            RequestingPersonId = 4, CarrierOperatorPersonId = 7,
            PickupLocationId = "millbrook-pickup", DestinationLocationId = "bakery-door",
        };

        private static FreightServiceQuote NewQuote() => new FreightServiceQuote
        {
            QuoteId = "quote-1", OneWayDistanceMiles = 50, ChargeableMiles = 100,
            BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
            TotalPriceCents = 125, PayerBusinessId = "bakery-1",
        };

        // ---- E-18: ETA and overdue ----

        [Test]
        public void ShipmentEta_EstimateRoundsUpToWholeDays()
        {
            // Zero stages: expected the same day it starts.
            Assert.AreEqual(10, LogisticsShipmentState.EstimateDeliveryDay(10, 0f, 0f, 0f));
            // Any part of a day is expected by the end of that day: a
            // three-minute haul starting day 10 is expected by day 11 in
            // the worst case (it may start at 23:59), never "today" for a
            // multi-day route.
            Assert.AreEqual(11, LogisticsShipmentState.EstimateDeliveryDay(10, 30f, 120f, 30f));
            // Exactly one day of stages: one day later.
            Assert.AreEqual(11, LogisticsShipmentState.EstimateDeliveryDay(10, 0f, 86400f, 0f));
            // 25 hours of stages spans two day boundaries in the worst case.
            Assert.AreEqual(12, LogisticsShipmentState.EstimateDeliveryDay(10, 0f, 90000f, 0f));
            // Negative durations and a negative start day never produce a
            // negative ETA.
            Assert.AreEqual(10, LogisticsShipmentState.EstimateDeliveryDay(10, -30f, -120f, -30f));
            Assert.AreEqual(1, LogisticsShipmentState.EstimateDeliveryDay(-5, 30f, 120f, 30f));
        }

        [Test]
        public void ShipmentEta_OverdueJudgment()
        {
            var shipment = new LogisticsShipmentState();
            Assert.IsFalse(shipment.HasExpectedDelivery);
            Assert.IsFalse(shipment.IsOverdue(999), "A shipment with no ETA is never overdue.");
            Assert.AreEqual(0, shipment.OverdueDays(999));

            Assert.IsNotNull(shipment.SetExpectedDelivery(-1), "A negative ETA is refused.");
            Assert.IsFalse(shipment.HasExpectedDelivery);
            Assert.IsNull(shipment.SetExpectedDelivery(12));
            Assert.IsTrue(shipment.HasExpectedDelivery);
            Assert.IsFalse(shipment.IsOverdue(11));
            Assert.IsFalse(shipment.IsOverdue(12), "On the expected day it is not yet overdue.");
            Assert.IsTrue(shipment.IsOverdue(13));
            Assert.AreEqual(0, shipment.OverdueDays(12));
            Assert.AreEqual(1, shipment.OverdueDays(13));
            Assert.AreEqual(3, shipment.OverdueDays(15));
        }

        [Test]
        public void ShipmentEta_RoundTripsThroughSaveDto()
        {
            var shipment = new LogisticsShipmentState();
            shipment.SetExpectedDelivery(12);
            var restored = LogisticsShipmentState.FromSaveDto(shipment.CaptureSaveDto());
            Assert.IsNotNull(restored);
            Assert.IsTrue(restored.HasExpectedDelivery);
            Assert.AreEqual(12, restored.expectedDeliveryDayIndex);
            Assert.IsTrue(restored.IsOverdue(13));

            // A legacy save carries no ETA: it restores as "no ETA", never
            // as an overdue shipment.
            var legacy = LogisticsShipmentState.FromSaveDto(new LogisticsShipmentState().CaptureSaveDto());
            Assert.IsFalse(legacy.HasExpectedDelivery);
            Assert.IsFalse(legacy.IsOverdue(999));
        }

        [Test]
        public void LotEta_OverdueJudgmentAndSaveRoundTrip()
        {
            (OffMapAidBusinessRuntime supplier, _, OffMapAidCargoLot lot) = PlaceMillbrookOrder();
            Assert.IsFalse(lot.HasExpectedDelivery);
            Assert.IsFalse(lot.IsOverdue(999));
            Assert.IsNotNull(lot.SetExpectedDelivery(-1));
            Assert.IsNull(lot.SetExpectedDelivery(12));
            Assert.IsFalse(lot.IsOverdue(12));
            Assert.IsTrue(lot.IsOverdue(13));
            Assert.AreEqual(2, lot.OverdueDays(14));

            // The ETA survives the supplier's export/import round trip.
            string importRefusal = supplier.ImportState(supplier.ExportState());
            Assert.IsNull(importRefusal, importRefusal);
            Assert.AreEqual(1, supplier.Lots.Count);
            Assert.AreEqual(12, supplier.Lots[0].ExpectedDeliveryDayIndex);
            Assert.IsTrue(supplier.Lots[0].IsOverdue(13));
        }

        // ---- F6-2: terminal-request reopen ----

        [Test]
        public void TerminalRequest_ReopensAfterAgreementCancelled_AndReacceptsWithRetryId()
        {
            var book = new FreightServiceAgreementBook();
            Assert.IsNull(book.CreateRequest(NewRequest("request-1", "lot-1"), out _));
            Assert.IsNull(book.SetQuote("request-1", NewQuote()));
            Assert.IsNull(book.Accept("request-1", 7, 2, 3600f, out FreightServiceAgreement first));
            Assert.AreEqual("freight-agreement:request-1", first.AgreementId);

            Assert.IsNull(book.CancelAgreement(first.AgreementId, "dispatch recovery limit reached after load"));
            Assert.AreEqual(FreightServiceAgreementStatus.Cancelled,
                book.FindAgreement(first.AgreementId).Status);

            Assert.IsNull(book.ReopenTerminalRequest("request-1",
                "Lot 'lot-1' is still shippable after its shipment was abandoned."));
            FreightServiceRequest request = book.FindRequest("request-1");
            Assert.AreEqual(FreightServiceRequestStatus.Open, request.Status);
            Assert.IsTrue(string.IsNullOrWhiteSpace(request.AgreementId), "The dead agreement link is cleared.");
            Assert.IsTrue(string.IsNullOrWhiteSpace(request.ShipmentId), "The dead shipment link is cleared.");

            // The retry re-accepts under a distinct agreement id; the
            // cancelled record stays in the book as history.
            Assert.IsNull(book.SetQuote("request-1", NewQuote()));
            Assert.IsNull(book.Accept("request-1", 7, 4, 3600f, out FreightServiceAgreement retry));
            Assert.AreEqual("freight-agreement:request-1:retry-2", retry.AgreementId);
            Assert.AreSame(retry, book.FindAgreementForRequest("request-1"),
                "The live lookup returns the retry, not the cancelled record.");
            Assert.AreSame(first, book.FindCancelledAgreementForRequest("request-1"));
        }

        [Test]
        public void TerminalRequest_RefusesLiveAgreementAndNonAcceptedRequests()
        {
            var book = new FreightServiceAgreementBook();
            Assert.IsNull(book.CreateRequest(NewRequest("request-live", "lot-live"), out _));
            Assert.IsNull(book.SetQuote("request-live", NewQuote()));
            Assert.IsNull(book.Accept("request-live", 7, 2, 3600f, out _));
            StringAssert.Contains("cancelled agreement",
                book.ReopenTerminalRequest("request-live", "not cancelled yet"));
            Assert.AreEqual(FreightServiceRequestStatus.Accepted, book.FindRequest("request-live").Status);

            Assert.IsNull(book.CreateRequest(NewRequest("request-refused", "lot-refused"), out _));
            Assert.IsNull(book.SetStatus("request-refused", FreightServiceRequestStatus.Refused, "no reply"));
            StringAssert.Contains("terminally accepted",
                book.ReopenTerminalRequest("request-refused", "wrong status"));
            StringAssert.Contains("Unknown freight service request",
                book.ReopenTerminalRequest("request-missing", "no such request"));

            // The reason guard is only reachable once the agreement IS
            // cancelled: the cancelled-agreement check comes first.
            Assert.IsNull(book.CancelAgreement(
                book.FindAgreementForRequest("request-live").AgreementId, "cancelled for the reason test"));
            Assert.IsNotNull(book.FindCancelledAgreementForRequest("request-live"));
            StringAssert.Contains("requires its cause",
                book.ReopenTerminalRequest("request-live", "  "));
        }

        // ---- F6-2 / F-02: lot release and refund ----

        [Test]
        public void LotRelease_RecommitAfterAbandonment()
        {
            (OffMapAidBusinessRuntime supplier, _, OffMapAidCargoLot lot) = PlaceMillbrookOrder();
            Assert.IsNull(supplier.CommitFreightPickup(lot.LotId, "ship-1", 1));
            Assert.IsTrue(lot.SourceCommitted);

            Assert.IsNull(supplier.ReleaseFreightCommitment(lot.LotId, "ship-1",
                "shipment abandoned after dispatch recovery limit"));
            Assert.IsFalse(lot.SourceCommitted);
            Assert.IsTrue(string.IsNullOrWhiteSpace(lot.ShipmentId), "The dead shipment link is cleared.");

            // The same paid lot recommits to the retried shipment: the
            // goods, the payment, and the lot identity are preserved.
            Assert.IsNull(supplier.CommitFreightPickup(lot.LotId, "ship-2", 1));
            Assert.IsTrue(lot.SourceCommitted);
            Assert.AreEqual("ship-2", lot.ShipmentId);
            Assert.AreEqual(1, supplier.Lots.Count, "No lot was duplicated or destroyed.");
        }

        [Test]
        public void LotRelease_RefusesReceivedLotAndWrongShipment()
        {
            (OffMapAidBusinessRuntime supplier, _, OffMapAidCargoLot lot) = PlaceMillbrookOrder();
            Assert.IsNull(supplier.CommitFreightPickup(lot.LotId, "ship-1", 1));
            StringAssert.Contains("different shipment",
                supplier.ReleaseFreightCommitment(lot.LotId, "ship-2", "wrong shipment"));
            Assert.IsTrue(lot.SourceCommitted, "A refused release moves nothing.");

            Assert.IsNull(supplier.ReceiveAtDestination(lot.LotId, "ship-1", "bakery-1", out int bakeryUnits));
            Assert.AreEqual(18, bakeryUnits);
            StringAssert.Contains("already received",
                supplier.ReleaseFreightCommitment(lot.LotId, "ship-1", "too late"));
            StringAssert.Contains("unknown aid lot",
                supplier.ReleaseFreightCommitment("aidlot-nope", "ship-1", "no such lot"));
        }

        [Test]
        public void LotRefund_ConservesMoneyAndRestocks()
        {
            (OffMapAidBusinessRuntime supplier, FakeBusinessCashPort bakeryCash, OffMapAidCargoLot lot) =
                PlaceMillbrookOrder();
            Assert.AreEqual(19952, bakeryCash.BalanceCents);
            Assert.AreEqual(48, supplier.CashCents);
            Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"));

            string refusal = supplier.RefundUnreceivedLot(lot.LotId, bakeryCash, 5,
                "shipment abandoned and the lot cannot be re-shipped", out int refundedCents);
            Assert.IsNull(refusal, refusal);
            Assert.AreEqual(48, refundedCents);
            Assert.AreEqual(20000, bakeryCash.BalanceCents, "The buyer is made whole.");
            Assert.AreEqual(0, supplier.CashCents, "The supplier keeps none of the payment.");
            Assert.AreEqual(230, supplier.GetInventoryUnits("bakery_flour_sack"), "The units are restocked.");
            Assert.AreEqual(0, supplier.Lots.Count, "The refunded lot no longer exists as a purchasable lot.");
            Assert.AreEqual(2, supplier.CashTransactions.Count, "Purchase + compensating refund.");
            Assert.AreEqual(-48, supplier.CashTransactions[1].AmountCents);
            Assert.AreEqual("aidrefund-millbrook_grain_co:opening-flour-b1",
                supplier.CashTransactions[1].TransactionId);
        }

        [Test]
        public void LotRefund_RefusesReceivedLotAndRollsBackFailedBuyerCredit()
        {
            (OffMapAidBusinessRuntime supplier, FakeBusinessCashPort bakeryCash, OffMapAidCargoLot lot) =
                PlaceMillbrookOrder();
            Assert.IsNull(supplier.CommitFreightPickup(lot.LotId, "ship-1", 1));
            Assert.IsNull(supplier.ReceiveAtDestination(lot.LotId, "ship-1", "bakery-1", out _));
            StringAssert.Contains("already received",
                supplier.RefundUnreceivedLot(lot.LotId, bakeryCash, 5, "too late", out int refusedRefund));
            Assert.AreEqual(0, refusedRefund);
            Assert.AreEqual(19952, bakeryCash.BalanceCents, "A refused refund moves no money.");
            Assert.AreEqual(48, supplier.CashCents);

            (OffMapAidBusinessRuntime supplier2, FakeBusinessCashPort bakeryCash2, OffMapAidCargoLot lot2) =
                PlaceMillbrookOrder("opening-flour-b2");
            bakeryCash2.RefuseCredit = true;
            StringAssert.Contains("buyer credit failed",
                supplier2.RefundUnreceivedLot(lot2.LotId, bakeryCash2, 5, "credit fails", out int rolledBack));
            Assert.AreEqual(0, rolledBack);
            Assert.AreEqual(48, supplier2.CashCents, "The supplier debit is rolled back.");
            Assert.AreEqual(220, supplier2.GetInventoryUnits("bakery_flour_sack"), "The restock is rolled back.");
            Assert.AreEqual(1, supplier2.Lots.Count, "The lot is still there.");
            Assert.AreEqual(19952, bakeryCash2.BalanceCents);
            StringAssert.Contains("no buyer cash port",
                supplier2.RefundUnreceivedLot(lot2.LotId, null, 5, "no port", out _));
        }

        // ---- C-4: staging deadline ----

        [Test]
        public void StagingWait_DecisionTable()
        {
            Assert.AreEqual(FreightDispatchStaging.StagingWaitVerdict.Waiting,
                FreightDispatchStaging.DecideStagingWait(-1, 10, false, false, out _),
                "Not yet prepared: the wait has not started.");
            Assert.AreEqual(FreightDispatchStaging.StagingWaitVerdict.Waiting,
                FreightDispatchStaging.DecideStagingWait(10, 12, false, false, out _),
                "Two days in, nothing moving yet: still waiting.");
            Assert.AreEqual(FreightDispatchStaging.StagingWaitVerdict.Overdue,
                FreightDispatchStaging.DecideStagingWait(10, 13, false, false, out string overdueReason),
                "Three days with no travel and no operator at staging: overdue.");
            StringAssert.Contains("3 day(s)", overdueReason);
            Assert.AreEqual(FreightDispatchStaging.StagingWaitVerdict.Waiting,
                FreightDispatchStaging.DecideStagingWait(10, 20, true, false, out _),
                "A travel task in flight keeps the wait alive however long it runs.");
            Assert.AreEqual(FreightDispatchStaging.StagingWaitVerdict.Waiting,
                FreightDispatchStaging.DecideStagingWait(10, 20, false, true, out _),
                "An operator already at staging keeps the wait alive.");
            Assert.AreEqual(3, FreightDispatchStaging.StagingWaitLimitDays);
        }

        // ---- F6-2 Queued gap ----

        private static (TaskAuthority Authority, WorkTimeBudgetStore Budgets, WorkTask Task) CreateQueuedTask(
            string definitionId, int dayIndex)
        {
            var authority = new TaskAuthority();
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(definitionId, definitionId, 30),
                out string reason), reason);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(dayIndex);
            WorkTask task = authority.CreateTaskWithPlannedMinutes(
                definitionId, EntityId.For(EntityKind.Business, 4), dayIndex, 30);
            Assert.AreEqual(TaskStatus.Queued, task.Status);
            return (authority, budgets, task);
        }

        [Test]
        public void QueuedSweep_BoundIsThreeDaysAndOnlyEscalatesTheStuck()
        {
            (_, _, WorkTask task) = CreateQueuedTask("lane9d-queued", 1);
            Assert.AreEqual(StaleQueuedDecision.UntouchedHealthy,
                StaleQueuedSweep.DecideQueued(task, 3, task.CreatedDayIndex).Decision,
                "Two days queued: within the bound.");
            Assert.AreEqual(StaleQueuedDecision.EscalateUnassigned,
                StaleQueuedSweep.DecideQueued(task, 4, task.CreatedDayIndex).Decision,
                "Three days queued with no assignee: escalate.");
            Assert.AreEqual(3, StaleQueuedSweep.QueuedStallLimitDays);

            Assert.AreEqual(StaleQueuedDecision.Skip,
                StaleQueuedSweep.DecideQueued(null, 10, 1).Decision);
            Assert.AreEqual(StaleQueuedDecision.Skip,
                StaleQueuedSweep.DecideQueued(task, 0, 1).Decision,
                "An anchor day ahead of the current day is not a stall.");
        }

        [Test]
        public void QueuedSweep_NonQueuedTaskIsSkipped()
        {
            (TaskAuthority authority, WorkTimeBudgetStore budgets, WorkTask task) =
                CreateQueuedTask("lane9d-started", 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, EntityId.For(EntityKind.Person, 7), budgets, out _));
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out _));
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.AreEqual(StaleQueuedDecision.Skip,
                StaleQueuedSweep.DecideQueued(task, 10, task.CreatedDayIndex).Decision);
        }

        // ---- E R-08: intake-gated import receive ----

        private static HouseholdLedger FundedLedger(int householdId, int balanceCents)
        {
            var ledger = new HouseholdLedger(householdId);
            ledger.RecordInflow(0, balanceCents, HouseholdIncomeSource.OtherDocumented,
                "test-endowment", "test endowment", "test");
            return ledger;
        }

        [Test]
        public void ImportIntakeGate_RefusalKeepsOrderOrderedAndRetryable()
        {
            var service = new ImportService();
            var ledger = FundedLedger(1, 100000);
            var diagnostics = new List<string>();
            ImportOrder order = service.PlaceOrder(
                "smith-1", "Test Smithy", ImportCatalog.IronStockId, 100, 10,
                ledger, null, null, diagnostics);
            Assert.IsNotNull(order, string.Join("; ", diagnostics));
            Assert.AreEqual(ImportOrderStatus.Ordered, order.Status);

            bool intakeOpen = false;
            int intakeCalls = 0;
            List<ImportLot> refused = service.ReceiveDueImportsWithIntake(
                order.ExpectedArrivalDayIndex,
                lot =>
                {
                    intakeCalls++;
                    return intakeOpen ? null : "yard intake is full today";
                },
                diagnostics);
            Assert.AreEqual(0, refused.Count, "A refused lot is not handed over.");
            Assert.AreEqual(1, intakeCalls, "The intake runs before any delivery is marked.");
            Assert.AreEqual(ImportOrderStatus.Ordered, order.Status,
                "A refused intake leaves the order Ordered and retryable.");
            StringAssert.Contains("intake refused", string.Join(" ", diagnostics));

            intakeOpen = true;
            List<ImportLot> delivered = service.ReceiveDueImportsWithIntake(
                order.ExpectedArrivalDayIndex + 1,
                lot =>
                {
                    intakeCalls++;
                    return intakeOpen ? null : "yard intake is full today";
                },
                diagnostics);
            Assert.AreEqual(1, delivered.Count);
            Assert.AreEqual(100, delivered[0].Units);
            Assert.AreEqual(order.OrderId, delivered[0].OrderId);
            Assert.AreEqual(ImportOrderStatus.Delivered, order.Status);
            Assert.AreEqual(2, intakeCalls);
        }

        [Test]
        public void ImportIntakeGate_NullIntakeRefusesDelivery()
        {
            var service = new ImportService();
            var ledger = FundedLedger(2, 100000);
            var diagnostics = new List<string>();
            ImportOrder order = service.PlaceOrder(
                "smith-1", "Test Smithy", ImportCatalog.IronStockId, 100, 10,
                ledger, null, null, diagnostics);
            Assert.IsNotNull(order, string.Join("; ", diagnostics));

            List<ImportLot> delivered = service.ReceiveDueImportsWithIntake(
                order.ExpectedArrivalDayIndex, null, diagnostics);
            Assert.AreEqual(0, delivered.Count, "No intake authority means no delivery, not a silent ungated one.");
            Assert.AreEqual(ImportOrderStatus.Ordered, order.Status);
        }

        // ---- M-R5: receiving-point resolver ----

        [Test]
        public void ReceivingPoint_ExactIdWinsAndTypeFallbackResolvesAuthoredStation()
        {
            var authored = new List<FreightReceivingPointResolver.AuthoredReceivingPoint>
            {
                new FreightReceivingPointResolver.AuthoredReceivingPoint("lumberyard-receiving-approach", ReceivingType),
                new FreightReceivingPointResolver.AuthoredReceivingPoint("lumberyard-counter", 3),
            };
            FreightReceivingPointResolver.ReceivingPointResolution exact =
                FreightReceivingPointResolver.Resolve("lumberyard-receiving-approach", authored, ReceivingType);
            Assert.AreEqual(FreightReceivingPointResolver.ReceivingPointSource.ExactId, exact.Source);
            Assert.AreEqual("lumberyard-receiving-approach", exact.ActivityLocationId);

            FreightReceivingPointResolver.ReceivingPointResolution byType =
                FreightReceivingPointResolver.Resolve("receiving", authored, ReceivingType);
            Assert.AreEqual(FreightReceivingPointResolver.ReceivingPointSource.ReceivingType, byType.Source);
            Assert.AreEqual("lumberyard-receiving-approach", byType.ActivityLocationId);
            StringAssert.Contains("lumberyard-receiving-approach", byType.Reason);
        }

        [Test]
        public void ReceivingPoint_NoStationOrAmbiguityReturnsNone()
        {
            var noStation = new List<FreightReceivingPointResolver.AuthoredReceivingPoint>
            {
                new FreightReceivingPointResolver.AuthoredReceivingPoint("bakery-counter", 3),
            };
            FreightReceivingPointResolver.ReceivingPointResolution none =
                FreightReceivingPointResolver.Resolve("receiving", noStation, ReceivingType);
            Assert.AreEqual(FreightReceivingPointResolver.ReceivingPointSource.None, none.Source);
            StringAssert.Contains("front door", none.Reason);

            // A non-"receiving" id never resolves by type: it names a
            // specific station, and a type match would deliver to the
            // wrong point.
            FreightReceivingPointResolver.ReceivingPointResolution other =
                FreightReceivingPointResolver.Resolve("counter", noStation, ReceivingType);
            Assert.AreEqual(FreightReceivingPointResolver.ReceivingPointSource.None, other.Source);

            var ambiguous = new List<FreightReceivingPointResolver.AuthoredReceivingPoint>
            {
                new FreightReceivingPointResolver.AuthoredReceivingPoint("receiving-a", ReceivingType),
                new FreightReceivingPointResolver.AuthoredReceivingPoint("receiving-b", ReceivingType),
            };
            FreightReceivingPointResolver.ReceivingPointResolution refused =
                FreightReceivingPointResolver.Resolve("receiving", ambiguous, ReceivingType);
            Assert.AreEqual(FreightReceivingPointResolver.ReceivingPointSource.None, refused.Source);
            StringAssert.Contains("more than once", refused.Reason);
            Assert.AreEqual(FreightReceivingPointResolver.ReceivingPointSource.None,
                FreightReceivingPointResolver.Resolve("  ", noStation, ReceivingType).Source);
        }
    }
}
