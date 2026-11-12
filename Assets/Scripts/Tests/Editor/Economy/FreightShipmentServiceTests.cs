// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Pass IV (Worker 3) — Slice E tests: freight shipment service.
//
// Real reconciliation figures: units loaded/unloaded, wagon capacity
// enforcement, conservation (planned == delivered + remaining).

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LandLedgers.Primitives;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Logistics;
using LandLedgers.Persistence;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class FreightShipmentServiceTests
    {
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FreightShipmentService service;
        private FreightShipmentHooks hooks;
        private EntityId loader;
        private EntityId driver;
        private Dictionary<string, int> recipientStock;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
            service = new FreightShipmentService();
            recipientStock = new Dictionary<string, int>(StringComparer.Ordinal);
            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = 42,
                Diagnostics = new List<string>(),
                // PASS7-008: the receiving authority is wired — unloaded goods land
                // in real recipient stock, and delivery completes only on acceptance.
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (quantityUnits <= 0) return 0;
                    string key = string.IsNullOrWhiteSpace(shipment.destinationBusinessInstanceId)
                        ? "nowhere" : shipment.destinationBusinessInstanceId;
                    recipientStock[key] = recipientStock.TryGetValue(key, out int have) ? have + quantityUnits : quantityUnits;
                    return quantityUnits;
                },
            };
            loader = EntityId.For(EntityKind.Person, 701);
            driver = EntityId.For(EntityKind.Person, 702);

            var wagon = new EquipmentAsset
            {
                AssetId = "wagon-1",
                Kind = "farm-wagon",
                DisplayName = "Farm wagon",
                Condition01 = 0.9f,
                PayloadMassCapacityKg = 1000f,
                CargoVolumeCapacityM3 = 4f,
            };
            service.RegisterWagon(wagon);
        }

        private FreightCargoSpec GrainSpec() => new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f };

        [Test]
        public void SourceLotReceiptWaitsForPhysicalReceivingArrival()
        {
            LogisticsShipmentState shipment = service.CreateShipment("physical-receipt", "mill-1", "bakery-1",
                10, GrainSpec(), "wagon-1", hooks, out string createReason);
            Assert.IsNotNull(shipment, createReason);
            Assert.IsTrue(service.TryBindSourceLot(shipment, "flour-lot-1", "flour", out string bindReason), bindReason);
            shipment.sourceLotDebitApplied = true;
            shipment.loadApplied = true;
            shipment.remainingQuantityUnits = shipment.PlannedQuantityUnits;
            shipment.state = LogisticsShipmentStatus.Arrived;
            hooks.ReceiveCargoLot = (LogisticsShipmentState received, int quantity, out string reason) =>
            {
                reason = string.Empty;
                recipientStock[received.destinationBusinessInstanceId] = quantity;
                return true;
            };
            bool physicallyAtReceivingPoint = false;
            hooks.ConfirmPhysicalReceiving = (LogisticsShipmentState received, EntityId handler,
                out bool arrived, out string reason) =>
            {
                arrived = physicallyAtReceivingPoint;
                reason = arrived ? string.Empty : "Carrying: route in progress.";
                return true;
            };

            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string beginReason), beginReason);
            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string firstReason), firstReason);
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status);
            Assert.AreEqual("person:701", shipment.physicalLocationId);
            Assert.IsFalse(shipment.deliveryApplied);
            Assert.IsFalse(recipientStock.ContainsKey("bakery-1"), "No stock receipt before physical arrival.");

            physicallyAtReceivingPoint = true;
            Assert.IsTrue(service.WorkUnloading(shipment, 1, hooks, out string secondReason), secondReason);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.AreEqual(10, recipientStock["bakery-1"]);
            Assert.IsTrue(shipment.deliveryApplied);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RefusedHandlingStartReleasesItsAssignmentAndBudget(bool unloading)
        {
            tasks.RegisterDefinition(new TaskDefinition("already-working", "Existing legitimate job", 30), out _);
            WorkTask busy = tasks.CreateTask("already-working", EntityId.For(EntityKind.Business, 999), 42);
            Assert.IsTrue(tasks.AssignTask(busy.TaskId, loader, budgets, out _));
            Assert.IsTrue(tasks.StartTask(busy.TaskId, 42, out _));
            int availableBefore = budgets.GetOrCreate(loader).MinutesRemaining;
            LogisticsShipmentState shipment = service.CreateShipment("refused-handling", "mill", "store",
                10, GrainSpec(), "wagon-1", hooks, out _);
            if (unloading) shipment.state = LogisticsShipmentStatus.Arrived;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool started = unloading
                    ? service.BeginUnloading(shipment, loader, hooks, out _)
                    : service.BeginLoading(shipment, loader, hooks, out _);
                Assert.IsFalse(started);
                Assert.AreEqual(0, tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3)).Count,
                    "A refused retry must not leave a second assigned freight job.");
                Assert.AreEqual(availableBefore, budgets.GetOrCreate(loader).MinutesRemaining);
                Assert.IsFalse(shipment.loadApplied);
                Assert.IsFalse(shipment.deliveryApplied);
                Assert.AreEqual(TaskStatus.InProgress, busy.Status);
            }
        }

        [Test]
        public void FullShipment_LoadTravelUnload_RecipientCustody_Conserved()
        {
            LogisticsShipmentState shipment = service.CreateShipment("ship-1", "mill-1", "store-1",
                100, GrainSpec(), "wagon-1", hooks, out string creason);
            Assert.IsNotNull(shipment, "create: " + creason);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.AreEqual("mill-1", shipment.physicalLocationId, "custody: source");

            // Load: real handling time (100 units -> 10 min task).
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string lreason), lreason);
            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status);
            int lguard = 0;
            while (!shipment.loadApplied && lguard++ < 10)
                Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string wreason), wreason);
            Assert.IsTrue(shipment.loadApplied, "loaded");
            Assert.AreEqual(100, shipment.RemainingQuantityUnits, "wagon holds 100");
            Assert.AreEqual("wagon:wagon-1", shipment.physicalLocationId, "custody: wagon");
            Assert.IsTrue(service.VerifyConservation(shipment, 0, out string cons1), cons1);

            // Dispatch with a real driver and team.
            var team = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
            Assert.IsTrue(service.Dispatch(shipment, driver, team, hooks, out string dreason), dreason);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status);
            Assert.IsTrue(shipment.physicalTransportBound, "transport bound");

            // Travel: advance past the transit duration.
            shipment.transitDurationGameSeconds = 120f;
            service.AdvanceTransit(shipment, 60f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status, "still travelling");
            Assert.Greater(shipment.physicalProgress01, 0f, "progress advancing");
            service.AdvanceTransit(shipment, 60f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status, "arrived");
            Assert.AreEqual(1f, shipment.physicalProgress01, 0.001f, "progress complete");

            // Unload: real handling time; recipient takes custody.
            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string ureason), ureason);
            int uguard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && uguard++ < 10)
                Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string wureason), wureason);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status, "completed");
            Assert.IsTrue(shipment.deliveryApplied, "delivery applied");
            Assert.AreEqual(0, shipment.RemainingQuantityUnits, "wagon empty");
            Assert.AreEqual(100, shipment.DeliveredQuantityUnits, "accepted quantity recorded");
            Assert.AreEqual(100, recipientStock["store-1"], "goods landed in real recipient stock");
            Assert.AreEqual("store-1", shipment.physicalLocationId, "custody: recipient");
            Assert.IsFalse(shipment.physicalTransportBound, "transport released");
            Assert.IsTrue(service.VerifyConservation(shipment, 100, out string cons2), cons2);
        }

        [Test]
        public void LotCustody_DebitsSourceAndCreditsSameLotExactlyOnce()
        {
            var sourceLots = new Dictionary<string, int> { ["mill-lot-001"] = 10 };
            var destinationLots = new Dictionary<string, int>();
            var debitedShipments = new HashSet<string>(StringComparer.Ordinal);
            var receivedShipments = new HashSet<string>(StringComparer.Ordinal);

            hooks.CommitSourceLot = (LogisticsShipmentState s, int quantity, out string reason) =>
            {
                reason = string.Empty;
                if (debitedShipments.Contains(s.ShipmentId)) return true;
                if (!sourceLots.TryGetValue(s.CargoLotId, out int onHand) || onHand < quantity)
                {
                    reason = "source lot shortfall";
                    return false;
                }
                sourceLots[s.CargoLotId] = onHand - quantity;
                debitedShipments.Add(s.ShipmentId);
                return true;
            };
            hooks.ReceiveCargoLot = (LogisticsShipmentState s, int quantity, out string reason) =>
            {
                reason = string.Empty;
                if (receivedShipments.Contains(s.ShipmentId))
                {
                    reason = "duplicate shipment/lot receipt refused";
                    return false;
                }
                receivedShipments.Add(s.ShipmentId);
                destinationLots[s.CargoLotId] = quantity;
                return true;
            };

            LogisticsShipmentState shipment = service.CreateShipment("millbrook-flour-001", "millbrook_grain_co",
                "bakery_001", 2, GrainSpec(), "wagon-1", hooks, out string createReason);
            Assert.IsNotNull(shipment, createReason);
            Assert.IsTrue(service.TryBindSourceLot(shipment, "mill-lot-001", "bakery_flour_sack", out string bindReason), bindReason);

            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string loadReason), loadReason);
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 10)
                Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string workReason), workReason);
            Assert.IsTrue(shipment.sourceLotDebitApplied);
            Assert.AreEqual(8, sourceLots["mill-lot-001"], "source lot debited once");
            Assert.AreEqual("mill-lot-001", shipment.CargoLotId);

            LogisticsShipmentSaveDto saved = shipment.CaptureSaveDto();
            LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(saved);
            Assert.AreEqual("mill-lot-001", restored.CargoLotId, "lot identity survives save/load");
            Assert.IsTrue(restored.sourceLotDebitApplied, "debit marker survives save/load");

            Assert.IsTrue(service.Dispatch(shipment, driver, null, hooks, out string dispatchReason), dispatchReason);
            service.AdvanceTransit(shipment, 120f, hooks);
            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string unloadReason), unloadReason);
            guard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && guard++ < 10)
                Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string workReason), workReason);

            Assert.AreEqual(2, destinationLots["mill-lot-001"], "receiver credits the same lot identity");
            Assert.AreEqual(2, shipment.DeliveredQuantityUnits);
            Assert.IsFalse(service.BeginUnloading(shipment, loader, hooks, out string duplicateReason));
            StringAssert.Contains("duplicate receipt", duplicateReason.ToLowerInvariant());
            Assert.AreEqual(2, destinationLots["mill-lot-001"], "repeat receipt does not duplicate stock");
            Assert.IsTrue(service.VerifyConservation(shipment, 2, out string conservationReason), conservationReason);
        }

        [Test]
        public void LotCustody_MissingSourceDebitAuthority_RefusesLoadWithoutAffectingLegacyShipments()
        {
            LogisticsShipmentState strict = service.CreateShipment("ship-lot-no-debit", "mill-1", "store-1",
                5, GrainSpec(), "wagon-1", hooks, out _);
            Assert.IsTrue(service.TryBindSourceLot(strict, "named-source-lot", "flour-sack", out string bindReason), bindReason);
            hooks.CommitSourceLot = null;
            Assert.IsFalse(service.BeginLoading(strict, loader, hooks, out string beginReason));
            StringAssert.Contains("debit authority", beginReason);
            Assert.IsFalse(strict.loadApplied);
            Assert.IsFalse(strict.sourceLotDebitApplied);

            // Existing construction/legacy freight remains on its original path.
            // It runs on a second wagon: the strict shipment above already
            // holds wagon-1 live (Planned), and the A4-13 rule binds a wagon
            // from creation until a terminal state, so a legacy shipment on
            // the same wagon is correctly refused. Same shape as
            // LotCustody_OneLotCannotBeBoundToTwoShipmentRecords.
            var secondWagon = new EquipmentAsset
            {
                AssetId = "wagon-2", Kind = "farm-wagon", DisplayName = "Second wagon",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            };
            service.RegisterWagon(secondWagon);
            LogisticsShipmentState legacy = service.CreateShipment("construction-legacy", "supplier", "site",
                5, GrainSpec(), "wagon-2", hooks, out string legacyReason);
            Assert.IsNotNull(legacy, legacyReason);
            Assert.IsTrue(service.BeginLoading(legacy, loader, hooks, out string legacyBegin), legacyBegin);
            int guard = 0;
            while (!legacy.loadApplied && guard++ < 10)
                Assert.IsTrue(service.WorkLoading(legacy, 60, hooks, out string legacyWork), legacyWork);
            Assert.IsTrue(legacy.loadApplied, "legacy construction path unaffected");
        }

        [Test]
        public void LotCustody_OneLotCannotBeBoundToTwoShipmentRecords()
        {
            LogisticsShipmentState first = service.CreateShipment("lot-owner-1", "mill-1", "bakery-1",
                2, GrainSpec(), "wagon-1", hooks, out string firstReason);
            Assert.IsNotNull(first, firstReason);
            Assert.IsTrue(service.TryBindSourceLot(first, "shared-lot", "bakery_flour_sack", out string firstBind), firstBind);

            var secondWagon = new EquipmentAsset
            {
                AssetId = "wagon-2", Kind = "farm-wagon", DisplayName = "Second wagon",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            };
            service.RegisterWagon(secondWagon);
            LogisticsShipmentState second = service.CreateShipment("lot-owner-2", "mill-1", "bakery-1",
                2, GrainSpec(), "wagon-2", hooks, out string secondReason);
            Assert.IsNotNull(second, secondReason);
            Assert.IsFalse(service.TryBindSourceLot(second, "shared-lot", "bakery_flour_sack", out string duplicateReason));
            StringAssert.Contains("already assigned", duplicateReason);
        }

        [Test]
        public void LotCustody_MissingReceiverRefusesBeforeUnloadingTaskStarts()
        {
            var sourceLots = new Dictionary<string, int> { ["source-lot"] = 5 };
            hooks.CommitSourceLot = (LogisticsShipmentState s, int quantity, out string reason) =>
            {
                reason = string.Empty;
                if (!sourceLots.TryGetValue(s.CargoLotId, out int units) || units < quantity)
                {
                    reason = "source stock shortfall";
                    return false;
                }
                sourceLots[s.CargoLotId] = units - quantity;
                return true;
            };
            LogisticsShipmentState shipment = service.CreateShipment("no-receiver", "mill-1", "bakery-1",
                5, GrainSpec(), "wagon-1", hooks, out string createReason);
            Assert.IsNotNull(shipment, createReason);
            Assert.IsTrue(service.TryBindSourceLot(shipment, "source-lot", "bakery_flour_sack", out string bindReason), bindReason);
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string loadingReason), loadingReason);
            Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string workReason), workReason);
            Assert.AreEqual(0, sourceLots["source-lot"]);
            Assert.IsTrue(shipment.sourceLotDebitApplied);
            Assert.IsTrue(service.Dispatch(shipment, driver, null, hooks, out string dispatchReason), dispatchReason);
            service.AdvanceTransit(shipment, shipment.transitDurationGameSeconds, hooks);

            hooks.ReceiveCargoLot = null;
            Assert.IsFalse(service.BeginUnloading(shipment, loader, hooks, out string unloadReason));
            StringAssert.Contains("lot receiver", unloadReason);
            Assert.AreEqual(5, shipment.RemainingQuantityUnits, "cargo remains aboard the wagon until an authority accepts it");
            Assert.IsFalse(shipment.deliveryApplied);
        }

        [Test]
        public void OverweightCargo_BlockedPreLoad_ZeroCargo_MR_P001()
        {
            // 1000 units x 2kg = 2000kg > 1000kg capacity.
            LogisticsShipmentState shipment = service.CreateShipment("ship-heavy", "mill-1", "store-1",
                1000, GrainSpec(), "wagon-1", hooks, out string reason);
            Assert.IsNotNull(shipment, "blocked record returned");
            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status, "failed");
            Assert.IsTrue(shipment.IsPreLoadBlocked, "MR-P001: pre-load blocked owns zero cargo");
            Assert.AreEqual(0, shipment.RemainingQuantityUnits, "zero cargo");
            Assert.IsFalse(shipment.loadApplied, "never loaded");
            StringAssert.Contains("mass", shipment.BlockedReason.ToLowerInvariant(), "reason: " + shipment.BlockedReason);

            // A blocked shipment cannot be loaded.
            Assert.IsFalse(service.BeginLoading(shipment, loader, hooks, out string lreason));
            StringAssert.Contains("blocked", lreason.ToLowerInvariant(), "load refused: " + lreason);
            Assert.IsTrue(service.VerifyConservation(shipment, 0, out string cons), cons);
        }

        [Test]
        public void OverVolumeCargo_BlockedPreLoad()
        {
            // 1000 units x 0.01m3 = 10m3 > 4m3 capacity (mass 2000kg also over, but volume checked too).
            var bulky = new FreightCargoSpec { MassKgPerUnit = 0.5f, VolumeM3PerUnit = 0.01f };
            LogisticsShipmentState shipment = service.CreateShipment("ship-bulky", "mill-1", "store-1",
                1000, bulky, "wagon-1", hooks, out _);
            Assert.IsTrue(shipment.IsPreLoadBlocked, "blocked on volume");
            StringAssert.Contains("volume", shipment.BlockedReason.ToLowerInvariant(), "reason: " + shipment.BlockedReason);
        }

        [Test]
        public void PhantomWagon_ShipmentRefused()
        {
            LogisticsShipmentState shipment = service.CreateShipment("ship-ghost", "mill-1", "store-1",
                10, GrainSpec(), "wagon-999", hooks, out string reason);
            Assert.IsNull(shipment, "refused");
            StringAssert.Contains("wagon", reason.ToLowerInvariant(), "reason: " + reason);
        }

        [Test]
        public void DelayedShipment_Holds_Resumes_Completes()
        {
            LogisticsShipmentState shipment = service.CreateShipment("ship-2", "mill-1", "store-1",
                50, GrainSpec(), "wagon-1", hooks, out _);
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out _));
            int lguard = 0;
            while (!shipment.loadApplied && lguard++ < 10)
                service.WorkLoading(shipment, 60, hooks, out _);
            Assert.IsTrue(service.Dispatch(shipment, driver, null, hooks, out _));

            shipment.transitDurationGameSeconds = 120f;
            service.AdvanceTransit(shipment, 60f, hooks);
            service.DelayShipment(shipment, "bridge washed out", hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, "delayed");
            float progressAtDelay = shipment.physicalProgress01;
            service.AdvanceTransit(shipment, 600f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, "still delayed — no phantom progress");
            Assert.AreEqual(50, shipment.RemainingQuantityUnits, "cargo intact during delay");

            service.ResumeShipment(shipment, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status, "resumed");
            service.AdvanceTransit(shipment, 120f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status, "arrived after resume");
        }

        [Test]
        public void SaveLoad_MidTransit_ResumesWithoutDuplicationOrLoss()
        {
            LogisticsShipmentState shipment = service.CreateShipment("ship-3", "mill-1", "store-1",
                80, GrainSpec(), "wagon-1", hooks, out _);
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out _));
            int lguard = 0;
            while (!shipment.loadApplied && lguard++ < 10)
                service.WorkLoading(shipment, 60, hooks, out _);
            Assert.IsTrue(service.Dispatch(shipment, driver, null, hooks, out _));
            shipment.transitDurationGameSeconds = 200f;
            service.AdvanceTransit(shipment, 80f, hooks); // partway

            // Save mid-transit, restore into a fresh service.
            FreightShipmentService.FreightServiceSaveDto dto =
                service.CaptureSaveDto(new List<LogisticsShipmentState> { shipment });
            var service2 = new FreightShipmentService();
            var wagon = new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "farm-wagon", PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            };
            service2.RegisterWagon(wagon);
            List<LogisticsShipmentState> restored = service2.RestoreSaveDto(dto, hooks);
            Assert.AreEqual(1, restored.Count, "one shipment restored");
            LogisticsShipmentState r = restored[0];
            Assert.AreEqual("ship-3", r.ShipmentId, "identity preserved");
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, r.Status, "status preserved");
            Assert.AreEqual(80, r.RemainingQuantityUnits, "cargo preserved — no loss, no duplication");
            Assert.IsTrue(r.loadApplied, "load flag preserved");
            Assert.AreEqual("wagon:wagon-1", r.physicalLocationId, "custody preserved");

            // Finish the restored shipment.
            service2.AdvanceTransit(r, 200f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, r.Status, "arrived");
            Assert.IsTrue(service2.BeginUnloading(r, loader, hooks, out _));
            int uguard = 0;
            while (r.Status != LogisticsShipmentStatus.Completed && uguard++ < 10)
                service2.WorkUnloading(r, 60, hooks, out _);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, r.Status, "completed after load");
            Assert.AreEqual(80, r.DeliveredQuantityUnits, "accepted quantity recorded");
            Assert.AreEqual(80, recipientStock["store-1"], "goods landed in real recipient stock");
            Assert.IsTrue(service2.VerifyConservation(r, 80, out string cons), cons);
        }

        // Q04 Slice 5: FreightShipmentService.RestoreSaveDto must clear the
        // tracked state on restore (clear-then-load, like the ledger
        // registry). The private load/unload-task maps have no public
        // reader; CaptureSaveDto is the read surface — it emits
        // ShipmentIdsWithLoadTasks / ShipmentIdsWithUnloadTasks from the
        // live maps. These three tests are reproducing-first: the first
        // two FAIL before the fix (stale S2 survives the smaller restore)
        // and PASS after; the third is a pin that passes before and after.
        [Test]
        public void FreightServiceRestore_SmallerSecondSave_DropsStaleShipment()
        {
            RegisterSecondWagon();
            LogisticsShipmentState s1 = service.CreateShipment("ship-s1", "mill-1", "store-1",
                10, GrainSpec(), "wagon-1", hooks, out string r1);
            LogisticsShipmentState s2 = service.CreateShipment("ship-s2", "mill-1", "store-1",
                10, GrainSpec(), "wagon-2", hooks, out string r2);
            Assert.IsNotNull(s1, r1);
            Assert.IsNotNull(s2, r2);
            FreightShipmentService.FreightServiceSaveDto fullDto =
                service.CaptureSaveDto(new List<LogisticsShipmentState> { s1, s2 });
            FreightShipmentService.FreightServiceSaveDto smallerDto =
                service.CaptureSaveDto(new List<LogisticsShipmentState> { s1 });

            var service2 = new FreightShipmentService();
            List<LogisticsShipmentState> restored = service2.RestoreSaveDto(fullDto, null);
            Assert.AreEqual(2, restored.Count, "full restore returns both shipments");
            Assert.AreEqual(2, service2.TrackedShipments.Count, "full restore tracks both shipments");

            restored = service2.RestoreSaveDto(smallerDto, null);
            Assert.AreEqual(1, restored.Count, "smaller restore returns exactly S1");
            Assert.AreEqual(1, service2.TrackedShipments.Count, "smaller restore must drop stale S2");
            Assert.AreEqual("ship-s1", service2.TrackedShipments[0].ShipmentId, "only S1 survives");
        }

        [Test]
        public void FreightServiceRestore_SmallerSecondSave_DropsStaleLoadTaskBinding()
        {
            RegisterSecondWagon();
            LogisticsShipmentState s1 = service.CreateShipment("ship-s1", "mill-1", "store-1",
                10, GrainSpec(), "wagon-1", hooks, out string r1);
            LogisticsShipmentState s2 = service.CreateShipment("ship-s2", "mill-1", "store-1",
                10, GrainSpec(), "wagon-2", hooks, out string r2);
            Assert.IsNotNull(s1, r1);
            Assert.IsNotNull(s2, r2);
            // Real load tasks: BeginLoading creates and starts the
            // freight:load:<shipmentId> task and records the binding.
            // Each shipment loads under a different loader: one person
            // cannot hold two in-progress tasks at once.
            Assert.IsTrue(service.BeginLoading(s1, loader, hooks, out string l1), l1);
            Assert.IsTrue(service.BeginLoading(s2, EntityId.For(EntityKind.Person, 703), hooks, out string l2), l2);
            FreightShipmentService.FreightServiceSaveDto fullDto =
                service.CaptureSaveDto(new List<LogisticsShipmentState> { s1, s2 });
            Assert.IsTrue(fullDto.ShipmentIdsWithLoadTasks.Contains("ship-s1"), "fixture: S1 load binding captured");
            Assert.IsTrue(fullDto.ShipmentIdsWithLoadTasks.Contains("ship-s2"), "fixture: S2 load binding captured");

            var service2 = new FreightShipmentService();
            service2.RestoreSaveDto(fullDto, hooks);
            FreightShipmentService.FreightServiceSaveDto captured =
                service2.CaptureSaveDto(new List<LogisticsShipmentState>(service2.TrackedShipments));
            Assert.IsTrue(captured.ShipmentIdsWithLoadTasks.Contains("ship-s1"), "S1 load binding rebinds");
            Assert.IsTrue(captured.ShipmentIdsWithLoadTasks.Contains("ship-s2"), "S2 load binding rebinds");

            // A smaller second save carrying only S1 (and only S1's load
            // task) must drop S2's binding, not leave it pointing at a
            // task for a shipment the save no longer carries.
            var smallerDto = new FreightShipmentService.FreightServiceSaveDto();
            smallerDto.Shipments.Add(s1.CaptureSaveDto());
            smallerDto.ShipmentIdsWithLoadTasks.Add("ship-s1");
            service2.RestoreSaveDto(smallerDto, hooks);
            captured = service2.CaptureSaveDto(new List<LogisticsShipmentState>(service2.TrackedShipments));
            Assert.AreEqual(1, service2.TrackedShipments.Count, "smaller restore tracks only S1");
            Assert.AreEqual(1, captured.ShipmentIdsWithLoadTasks.Count, "exactly one load-task binding survives");
            Assert.AreEqual("ship-s1", captured.ShipmentIdsWithLoadTasks[0], "only S1's load binding survives");
            Assert.IsTrue(!captured.ShipmentIdsWithLoadTasks.Contains("ship-s2"), "no load-task binding remains for S2");
        }

        [Test]
        public void FreightServiceRestore_SameDtoTwice_IsIdempotent()
        {
            RegisterSecondWagon();
            LogisticsShipmentState s1 = service.CreateShipment("ship-s1", "mill-1", "store-1",
                10, GrainSpec(), "wagon-1", hooks, out string r1);
            LogisticsShipmentState s2 = service.CreateShipment("ship-s2", "mill-1", "store-1",
                10, GrainSpec(), "wagon-2", hooks, out string r2);
            Assert.IsNotNull(s1, r1);
            Assert.IsNotNull(s2, r2);
            FreightShipmentService.FreightServiceSaveDto dto =
                service.CaptureSaveDto(new List<LogisticsShipmentState> { s1, s2 });

            var service2 = new FreightShipmentService();
            List<LogisticsShipmentState> first = service2.RestoreSaveDto(dto, null);
            List<LogisticsShipmentState> second = service2.RestoreSaveDto(dto, null);
            Assert.AreEqual(2, first.Count, "first restore returns both shipments");
            Assert.AreEqual(2, second.Count, "second restore of the same DTO returns both shipments");
            Assert.AreEqual(2, service2.TrackedShipments.Count, "same DTO twice tracks exactly the same set");
            Assert.AreEqual("ship-s1", service2.TrackedShipments[0].ShipmentId, "S1 still tracked");
            Assert.AreEqual("ship-s2", service2.TrackedShipments[1].ShipmentId, "S2 still tracked");
        }

        // Q04 Slice 6: FreightShipmentService.RestoreSaveDto rebinds the
        // load/unload-task bindings and the dispatch authorities when it
        // is given real FreightShipmentHooks — and skips both, safely,
        // when hooks are null. The service half is harness-provable;
        // the production half (SaveLoadManager passing real hooks
        // instead of the literal null at the Pass XL restore call) is
        // harness-excluded and source-reviewed only.
        //
        // FIXTURE DEVIATION FROM THE PLAN, verified in source before
        // writing the test (recorded in the slice log, not silently
        // weakened): the plan names one dispatched mid-journey shipment
        // holding an OPEN freight:load:<id> task. That combined state
        // cannot occur through the real service flow — WorkLoading
        // completes the load task (TaskAuthority.CompleteTask) and
        // removes the binding the moment loading finishes
        // (FreightShipmentService.cs, WorkLoading completion branch),
        // and Dispatch refuses any shipment whose loadApplied is false,
        // so no dispatched shipment ever has an open load task or a
        // live load-task binding for a save to carry. The only creator
        // of freight:load: tasks is BeginLoading, and FindOpenTask
        // skips Complete tasks, so the rebind could never resolve one
        // for a dispatched shipment. The fixture therefore uses TWO
        // shipments in reachable states, and pins the SAME two
        // assertions the plan names: S1 mid-loading (open load task,
        // binding captured) for the load-task rebind, and S2 dispatched
        // mid-journey (transport bound, wagon hold taken through the
        // reservation authority) for the dispatch-authority rebind.
        // Both rebinds are the same code paths the single-shipment
        // fixture would have exercised.
        private FreightShipmentService.FreightServiceSaveDto BuildRebindFixture(
            out EquipmentReservationService reservations, out EntityId openLoadTaskId)
        {
            RegisterSecondWagon();
            reservations = new EquipmentReservationService();
            // Dispatch takes its wagon/driver hold through this hooks'
            // Reservations, so the captured DTO carries S2 in
            // ShipmentIdsWithDispatchAuthorities.
            var dispatchHooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = 42,
                Diagnostics = new List<string>(),
                Reservations = reservations,
                ReceiveShipment = hooks.ReceiveShipment,
                NowGameSeconds = 0f,
            };

            // S2: dispatched mid-journey — loaded, dispatched, partway
            // through transit, transport bound, wagon hold live.
            LogisticsShipmentState s2 = service.CreateShipment("ship-s2", "mill-1", "store-1",
                10, GrainSpec(), "wagon-2", dispatchHooks, out string r2);
            Assert.IsNotNull(s2, r2);
            Assert.IsTrue(service.BeginLoading(s2, EntityId.For(EntityKind.Person, 703), dispatchHooks, out string l2), l2);
            int guard = 0;
            while (!s2.loadApplied && guard++ < 10)
                Assert.IsTrue(service.WorkLoading(s2, 60, dispatchHooks, out string w2), w2);
            Assert.IsTrue(s2.loadApplied, "fixture: S2 loaded");
            Assert.IsTrue(service.Dispatch(s2, driver, null, dispatchHooks, out string d2), d2);
            Assert.IsTrue(s2.physicalTransportBound, "fixture: S2 transport bound");
            s2.transitDurationGameSeconds = 200f;
            service.AdvanceTransit(s2, 80f, dispatchHooks);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, s2.Status, "fixture: S2 mid-journey");

            // S1: mid-loading — its freight:load:<id> task is open and bound.
            LogisticsShipmentState s1 = service.CreateShipment("ship-s1", "mill-1", "store-1",
                10, GrainSpec(), "wagon-1", hooks, out string r1);
            Assert.IsNotNull(s1, r1);
            Assert.IsTrue(service.BeginLoading(s1, loader, hooks, out string l1), l1);
            WorkTask openLoadTask = tasks.FindOpenTask("freight:load:ship-s1",
                EntityId.For(EntityKind.Business, 3), EntityId.Invalid);
            Assert.IsNotNull(openLoadTask, "fixture: S1 load task is open");
            openLoadTaskId = openLoadTask.TaskId;

            FreightShipmentService.FreightServiceSaveDto dto =
                service.CaptureSaveDto(new List<LogisticsShipmentState> { s1, s2 });
            Assert.IsTrue(dto.ShipmentIdsWithLoadTasks.Contains("ship-s1"), "fixture: S1 load binding captured");
            Assert.IsTrue(dto.ShipmentIdsWithDispatchAuthorities.Contains("ship-s2"), "fixture: S2 dispatch authority captured");
            Assert.IsTrue(reservations.IsReserved(ReservationKeyConventions.Asset("wagon-2"), 10f),
                "fixture: dispatch holds wagon-2");
            return dto;
        }

        private static LogisticsShipmentState FindRestored(
            List<LogisticsShipmentState> restored, string shipmentId)
        {
            foreach (LogisticsShipmentState s in restored)
            {
                if (s != null && string.Equals(s.ShipmentId, shipmentId, StringComparison.Ordinal))
                    return s;
            }
            return null;
        }

        private FreightShipmentHooks RestoreHooks(EquipmentReservationService reservations)
        {
            return new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = 42,
                Diagnostics = new List<string>(),
                Reservations = reservations,
                ReceiveShipment = hooks.ReceiveShipment,
                NowGameSeconds = 0f,
            };
        }

        private static FreightShipmentService FreshServiceWithWagons()
        {
            var fresh = new FreightShipmentService();
            fresh.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "farm-wagon", DisplayName = "Farm wagon",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            });
            fresh.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-2", Kind = "farm-wagon", DisplayName = "Second wagon",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            });
            return fresh;
        }

        [Test]
        public void FreightServiceRestore_WithHooks_RebindsLoadTaskAndDispatchAuthority()
        {
            // Reproducing-first per the plan: this test pins the service
            // behaviour the production path could not reach before the
            // Slice 6 wiring fix — it PASSES against the service
            // directly with real hooks, and the production defect was
            // the literal null SaveLoadManager passed as freightHooks
            // (see the null-hooks test, which shows the
            // production-equivalent restore losing both rebinds and
            // leaking the wagon hold).
            FreightShipmentService.FreightServiceSaveDto dto = BuildRebindFixture(
                out EquipmentReservationService reservations, out EntityId openLoadTaskId);

            FreightShipmentService service2 = FreshServiceWithWagons();
            FreightShipmentHooks restoreHooks = RestoreHooks(reservations);
            List<LogisticsShipmentState> restored = service2.RestoreSaveDto(dto, restoreHooks);
            Assert.AreEqual(2, restored.Count, "both shipments restore");
            LogisticsShipmentState rs1 = FindRestored(restored, "ship-s1");
            LogisticsShipmentState rs2 = FindRestored(restored, "ship-s2");
            Assert.IsNotNull(rs1, "S1 restored");
            Assert.IsNotNull(rs2, "S2 restored");
            Assert.AreEqual(LogisticsShipmentStatus.Loading, rs1.Status, "S1 still mid-loading");
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, rs2.Status, "S2 still mid-journey");

            // Both rebinds are visible through CaptureSaveDto, the read
            // surface for the private load/unload-task and
            // dispatch-authority maps (Slice 5 precedent).
            FreightShipmentService.FreightServiceSaveDto captured =
                service2.CaptureSaveDto(new List<LogisticsShipmentState>(service2.TrackedShipments));
            Assert.IsTrue(captured.ShipmentIdsWithLoadTasks.Contains("ship-s1"), "load-task binding rebinds");
            Assert.IsTrue(captured.ShipmentIdsWithDispatchAuthorities.Contains("ship-s2"), "dispatch authority rebinds");

            // The rebound load-task binding resolves to the open task
            // itself: working the restored shipment completes that
            // same task id.
            int guard = 0;
            while (!rs1.loadApplied && guard++ < 10)
                Assert.IsTrue(service2.WorkLoading(rs1, 60, restoreHooks, out string wreason), wreason);
            Assert.IsTrue(rs1.loadApplied, "restored S1 finishes loading through the rebound task");
            WorkTask loadTask = tasks.FindTask(openLoadTaskId);
            Assert.IsNotNull(loadTask, "load task still known after completion");
            Assert.AreEqual(TaskStatus.Complete, loadTask.Status, "the rebound binding was the open task");

            // The rebound dispatch authority releases the wagon hold
            // exactly once when the restored shipment completes —
            // the plan §2 item 8 pin: no leaked reservation after a
            // restore with real hooks.
            service2.AdvanceTransit(rs2, 200f, restoreHooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, rs2.Status, "restored S2 arrives");
            Assert.IsTrue(service2.BeginUnloading(rs2, EntityId.For(EntityKind.Person, 703), restoreHooks, out string ureason), ureason);
            int uguard = 0;
            while (rs2.Status != LogisticsShipmentStatus.Completed && uguard++ < 10)
                Assert.IsTrue(service2.WorkUnloading(rs2, 60, restoreHooks, out string wureason), wureason);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, rs2.Status, "restored S2 completes after load");
            Assert.AreEqual(10, recipientStock["store-1"], "goods landed exactly once");
            Assert.IsTrue(!reservations.IsReserved(ReservationKeyConventions.Asset("wagon-2"), 10f),
                "wagon-2 hold released on completion — no leak");
        }

        [Test]
        public void FreightServiceRestore_NullHooks_SkipsRebindWithoutCrashing()
        {
            FreightShipmentService.FreightServiceSaveDto dto = BuildRebindFixture(
                out EquipmentReservationService reservations, out EntityId openLoadTaskId);

            // Restore with null hooks — exactly what production did
            // before the Slice 6 wiring fix (SaveLoadManager passed a
            // literal null as freightHooks): no exception, both
            // shipments restore, but NEITHER rebind happens.
            FreightShipmentService service2 = FreshServiceWithWagons();
            List<LogisticsShipmentState> restored = service2.RestoreSaveDto(dto, null);
            Assert.AreEqual(2, restored.Count, "shipments still restore with null hooks");
            LogisticsShipmentState rs1 = FindRestored(restored, "ship-s1");
            LogisticsShipmentState rs2 = FindRestored(restored, "ship-s2");
            Assert.IsNotNull(rs1, "S1 restored with null hooks");
            Assert.IsNotNull(rs2, "S2 restored with null hooks");
            Assert.AreEqual(LogisticsShipmentStatus.Loading, rs1.Status, "S1 still mid-loading");
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, rs2.Status, "S2 still mid-journey");
            FreightShipmentService.FreightServiceSaveDto captured =
                service2.CaptureSaveDto(new List<LogisticsShipmentState>(service2.TrackedShipments));
            Assert.AreEqual(0, captured.ShipmentIdsWithLoadTasks.Count, "null hooks: no load-task binding rebinds");
            Assert.AreEqual(0, captured.ShipmentIdsWithDispatchAuthorities.Count, "null hooks: no dispatch authority rebinds");

            // The consequence the production fix closes (plan §2 item 8):
            // the restored mid-journey shipment completes, but its
            // wagon hold is never released — the service never rebound
            // the authority, so completion has nothing to release
            // through, and the wagon stays held forever.
            FreightShipmentHooks finishHooks = RestoreHooks(reservations);
            service2.AdvanceTransit(rs2, 200f, finishHooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, rs2.Status, "restored S2 arrives");
            Assert.IsTrue(service2.BeginUnloading(rs2, EntityId.For(EntityKind.Person, 703), finishHooks, out string ureason), ureason);
            int uguard = 0;
            while (rs2.Status != LogisticsShipmentStatus.Completed && uguard++ < 10)
                Assert.IsTrue(service2.WorkUnloading(rs2, 60, finishHooks, out string wureason), wureason);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, rs2.Status, "shipment completes even without the rebind");
            Assert.AreEqual(10, recipientStock["store-1"], "goods landed exactly once");
            Assert.IsTrue(reservations.IsReserved(ReservationKeyConventions.Asset("wagon-2"), 10f),
                "null hooks: wagon-2 hold LEAKS after completion — the rebind production skips is the only release path");
        }

        private void RegisterSecondWagon()
        {
            service.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-2", Kind = "farm-wagon", DisplayName = "Second wagon",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            });
        }

        [Test]
        public void NoDuplicateGoods_DoubleUnloadImpossible()
        {
            LogisticsShipmentState shipment = service.CreateShipment("ship-4", "mill-1", "store-1",
                40, GrainSpec(), "wagon-1", hooks, out _);
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out _));
            int lguard = 0;
            while (!shipment.loadApplied && lguard++ < 10)
                service.WorkLoading(shipment, 60, hooks, out _);
            Assert.IsTrue(service.Dispatch(shipment, driver, null, hooks, out _));
            shipment.transitDurationGameSeconds = 10f;
            service.AdvanceTransit(shipment, 10f, hooks);
            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out _));
            int uguard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && uguard++ < 10)
                service.WorkUnloading(shipment, 60, hooks, out _);

            // Unloading again is impossible: no task, no status, no cargo.
            Assert.IsFalse(service.BeginUnloading(shipment, loader, hooks, out string reason),
                "second unload refused");
            Assert.IsFalse(service.WorkUnloading(shipment, 60, hooks, out _), "work unload refused");
            Assert.AreEqual(0, shipment.RemainingQuantityUnits, "still zero — no duplicated goods");
            Assert.AreEqual(40, recipientStock["store-1"], "recipient stock credited exactly once");
            Assert.IsTrue(service.VerifyConservation(shipment, 40, out string cons), cons);
        }
    }
}
