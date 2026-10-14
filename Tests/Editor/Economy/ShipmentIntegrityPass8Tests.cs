// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Muse Pass VIII, Worker 1 (Inventory and Shipment Integrity):
// production-authority tests for PASS7-006, PASS7-007, PASS7-008.
//
// These tests exercise the production authorities directly (no fixture
// workarounds): GenericBusinessConfiguration / GenericRetailConfiguration /
// RetailCapacityPool, BusinessRuntimeState, GenericRetailSaleAuthority,
// LogisticsShipmentState, FreightShipmentService, and ExternalMarketAuthority.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Logistics;
using LandLedgers.Persistence;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class ShipmentIntegrityPass8Tests
    {
        private static GenericBusinessConfiguration NewStoreConfig(string productId, string poolId)
        {
            var config = new GenericBusinessConfiguration();
            config.Retail.AddOrReplaceLine(new GenericRetailProductLine(
                productId, poolId, target: 100f, maximum: 200f, reorder: 20f, priceCents: 500));
            return config;
        }

        // ------------------------------------------------------------------
        // PASS7-006: retail capacity pool corruption.
        // ------------------------------------------------------------------

        [Test]
        public void ReleaseProductSpace_NeverMintsPool_UnconfiguredStoreKeepsReceiving()
        {
            GenericBusinessConfiguration config = NewStoreConfig("nails", "shelf-a");

            // First shipment received into a store that never configured capacity.
            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-1", "nails", 50f, 100, null, "test", 1));
            Assert.AreEqual(50f, config.GetAvailableInventoryQuantity("nails"), 0.001f);

            // A sale releases shelf space. The release must NOT mint a
            // zero-allocation pool (the PASS7-006 corruption).
            config.Retail.ReleaseProductSpace("nails", 50f);
            Assert.AreEqual(0, config.Retail.CapacityPools.Count,
                "ReleaseProductSpace minted a pool: future receives would false-block.");

            // Every later shipment must still be receivable.
            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-2", "nails", 50f, 100, null, "test", 1),
                "PASS7-006: shipment refused after an unrelated release minted a zero pool.");
            Assert.AreEqual(100f, config.GetAvailableInventoryQuantity("nails"), 0.001f);
            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-3", "nails", 25f, 100, null, "test", 1));
            Assert.AreEqual(125f, config.GetAvailableInventoryQuantity("nails"), 0.001f);
        }

        [Test]
        public void TryOccupyProductSpace_UnconfiguredPool_ImposesNoConstraint()
        {
            GenericBusinessConfiguration config = NewStoreConfig("nails", "shelf-a");
            Assert.IsTrue(config.Retail.TryOccupyProductSpace("nails", 10000f),
                "An unconfigured pool must not constrain legitimate stock movement.");
            Assert.AreEqual(0, config.Retail.CapacityPools.Count, "Occupancy must not mint a pool either.");
        }

        [Test]
        public void ExplicitZeroAllocationPool_BlocksReceivesHonestly()
        {
            GenericBusinessConfiguration config = NewStoreConfig("nails", "shelf-a");
            // Explicitly configured zero capacity is a legitimate constraint:
            // receives are refused loudly (false), never silently lost.
            config.Retail.GetOrCreatePool("shelf-a", 0f);
            Assert.IsFalse(config.TryReceiveInventoryShipment("ship-1", "nails", 10f, 100, null, "test", 1));
            Assert.AreEqual(0f, config.GetAvailableInventoryQuantity("nails"), 0.001f,
                "Refused goods must not appear in inventory.");
        }

        [Test]
        public void PartiallyOccupiedPool_EnforcesExactRoom()
        {
            GenericBusinessConfiguration config = NewStoreConfig("nails", "shelf-a");
            config.Retail.GetOrCreatePool("shelf-a", 100f);

            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-1", "nails", 60f, 100, null, "test", 1));
            Assert.IsFalse(config.TryReceiveInventoryShipment("ship-2", "nails", 50f, 100, null, "test", 1),
                "50u does not fit in 40u of room — must be refused, not partially silently kept.");
            Assert.AreEqual(60f, config.GetAvailableInventoryQuantity("nails"), 0.001f);
            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-3", "nails", 40f, 100, null, "test", 1));
            Assert.AreEqual(100f, config.GetAvailableInventoryQuantity("nails"), 0.001f);
        }

        [Test]
        public void CapacityIncreaseAfterRefusal_ReceiveSucceedsOnRetry()
        {
            GenericBusinessConfiguration config = NewStoreConfig("nails", "shelf-a");
            RetailCapacityPool pool = config.Retail.GetOrCreatePool("shelf-a", 100f);

            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-1", "nails", 60f, 100, null, "test", 1));
            Assert.IsFalse(config.TryReceiveInventoryShipment("ship-2", "nails", 50f, 100, null, "test", 1));

            Assert.IsTrue(pool.SetAllocation(200f), "capacity increase accepted");
            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-2", "nails", 50f, 100, null, "test", 1),
                "Retry after a legitimate capacity change must succeed.");
            Assert.AreEqual(110f, config.GetAvailableInventoryQuantity("nails"), 0.001f);
        }

        [Test]
        public void ConfiguredPool_SaleReleasesRoomForNextReceive()
        {
            GenericBusinessConfiguration config = NewStoreConfig("nails", "shelf-a");
            config.Retail.GetOrCreatePool("shelf-a", 100f);

            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-1", "nails", 60f, 100, null, "test", 1));
            config.Retail.ReleaseProductSpace("nails", 60f);
            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-2", "nails", 60f, 100, null, "test", 1),
                "A real release against a configured pool must free real room.");
            Assert.AreEqual(120f, config.GetAvailableInventoryQuantity("nails"), 0.001f);
        }

        [Test]
        public void ReleaseProductSpace_UnknownProduct_DoesNothing()
        {
            GenericBusinessConfiguration config = NewStoreConfig("nails", "shelf-a");
            config.Retail.ReleaseProductSpace("ghost-product", 10f);
            Assert.AreEqual(0, config.Retail.CapacityPools.Count, "No pool minted for an unknown product.");
        }

        // ------------------------------------------------------------------
        // PASS7-007: AddCategoryStockUnits silent cap.
        // ------------------------------------------------------------------

        [Test]
        public void AddCategoryStockUnits_CapsAtTarget_ReturnsAcceptedOnly()
        {
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("grain", 0, 100);

            Assert.AreEqual(60, state.AddCategoryStockUnits("grain", 60));
            Assert.AreEqual(40, state.AddCategoryStockUnits("grain", 60),
                "Only 40u fit: the return must say 40, not 60.");
            Assert.AreEqual(100, state.GetCategoryStock("grain").CurrentStockUnits);
        }

        [Test]
        public void AddCategoryStockUnits_ShipmentLargerThanAvailableCapacity_PartialAccepted()
        {
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("grain", 0, 100);
            state.GetCategoryStock("grain").SetCurrentStockForTests(90);

            Assert.AreEqual(10, state.AddCategoryStockUnits("grain", 50),
                "A shipment larger than available capacity is partially accepted, never over-counted.");
            Assert.AreEqual(100, state.GetCategoryStock("grain").CurrentStockUnits);
        }

        [Test]
        public void AddCategoryStockUnits_FullDestination_AcceptsZero()
        {
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("grain", 0, 100);
            state.GetCategoryStock("grain").SetCurrentStockForTests(100);

            Assert.AreEqual(0, state.AddCategoryStockUnits("grain", 25));
            Assert.IsFalse(state.CanReceiveShipment("grain", 25, out string reason));
            Assert.IsFalse(string.IsNullOrWhiteSpace(reason), "Refusal must carry a reason.");
        }

        [Test]
        public void AddCategoryStockUnits_MultipleCompetingAdds_ConserveTotal()
        {
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("grain", 0, 100);

            int first = state.AddCategoryStockUnits("grain", 70);
            int second = state.AddCategoryStockUnits("grain", 70);
            Assert.AreEqual(70, first);
            Assert.AreEqual(30, second);
            Assert.AreEqual(100, state.GetCategoryStock("grain").CurrentStockUnits,
                "Competing adds must conserve: 70 + 30 = 100, never 140.");
        }

        [Test]
        public void RetailSaleRollback_RestoresExactConsumedUnits()
        {
            BusinessInstanceState business = NewTestBusiness("store-rb");
            business.RuntimeState.EnsureCategoryStock("nails", 0, 100);
            business.RuntimeState.AddCategoryStockUnits("nails", 50);
            business.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("nails", "shelf", 100f, 200f, 20f, 500));

            var context = new GenericRetailSaleContext
            {
                BuyerPrincipal = "buyer-1",
                ActingPersonId = 7,
                SellerBusinessId = "store-rb",
                ProductId = "nails",
                Quantity = 10,
                UnitPriceCents = 500,
            };

            bool ok = GenericRetailSaleAuthority.TryExecuteSale(business, context, ctx => false, out string reason);
            Assert.IsFalse(ok, "Settlement refused — sale must fail.");
            StringAssert.Contains("rolled back", reason);
            Assert.IsFalse(reason.Contains("WARNING"), "Full rollback must not warn: " + reason);
            Assert.AreEqual(50, business.RuntimeState.GetCategoryStock("nails").CurrentStockUnits,
                "Rollback must restore exactly the consumed units.");
        }

        [Test]
        public void RetailSaleRollback_CappedRestore_WarnsLoudly()
        {
            BusinessInstanceState business = NewTestBusiness("store-rb2");
            business.RuntimeState.EnsureCategoryStock("nails", 0, 100);
            business.RuntimeState.AddCategoryStockUnits("nails", 50);
            business.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("nails", "shelf", 100f, 200f, 20f, 500));

            var context = new GenericRetailSaleContext
            {
                BuyerPrincipal = "buyer-1",
                ActingPersonId = 7,
                SellerBusinessId = "store-rb2",
                ProductId = "nails",
                Quantity = 10,
                UnitPriceCents = 500,
            };

            // The settlement step itself fills the freed room, so the rollback caps.
            bool ok = GenericRetailSaleAuthority.TryExecuteSale(business, context,
                ctx =>
                {
                    business.RuntimeState.AddCategoryStockUnits("nails", 60);
                    return false;
                }, out string reason);
            Assert.IsFalse(ok);
            StringAssert.Contains("WARNING", reason, "A capped rollback must warn loudly, never vanish silently: " + reason);
        }

        // ------------------------------------------------------------------
        // Reservations and idempotent receipt.
        // ------------------------------------------------------------------

        [Test]
        public void ReserveInventory_ReducesAvailable_ConsumeReservedExact()
        {
            GenericBusinessConfiguration config = NewStoreConfig("nails", "shelf-a");
            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-a", "nails", 10f, 100, null, "test", 1));

            Assert.IsTrue(config.TryReserveInventory("nails", 6f));
            Assert.AreEqual(4f, config.GetAvailableInventoryQuantity("nails"), 0.001f);
            Assert.IsFalse(config.TryReserveInventory("nails", 5f), "Only 4u available.");

            Assert.IsTrue(config.TryConsumeReservedInventory("nails", 6f, out float consumed));
            Assert.AreEqual(6f, consumed, 0.001f);
            Assert.AreEqual(4f, config.GetAvailableInventoryQuantity("nails"), 0.001f);
        }

        [Test]
        public void TryReceiveInventoryShipment_RepeatedCallback_NoDuplication()
        {
            GenericBusinessConfiguration config = NewStoreConfig("nails", "shelf-a");
            Assert.IsTrue(config.TryReceiveInventoryShipment("ship-x", "nails", 25f, 100, null, "test", 1));
            Assert.IsFalse(config.TryReceiveInventoryShipment("ship-x", "nails", 25f, 100, null, "test", 1),
                "Repeated receipt callback must be refused.");
            Assert.AreEqual(25f, config.GetAvailableInventoryQuantity("nails"), 0.001f,
                "No duplicated inventory on repeated callback.");
        }

        // ------------------------------------------------------------------
        // PASS7-008: shipment completion must imply actual acceptance.
        // ------------------------------------------------------------------

        [Test]
        public void AdvanceGameSeconds_UnloadingPins_NoFiatCompletion()
        {
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "sm-1",
                plannedQuantityUnits = 8,
                remainingQuantityUnits = 8,
                loadApplied = true,
                loadingDurationGameSeconds = 10f,
                transitDurationGameSeconds = 20f,
                unloadingDurationGameSeconds = 10f,
            };

            shipment.AdvanceGameSeconds(10f);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status);
            shipment.AdvanceGameSeconds(20f);
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status,
                "Goods are awaiting unloading: in legitimate custody, not delivered.");

            // Even unbounded time must not mark the shipment delivered.
            shipment.AdvanceGameSeconds(100000f);
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status,
                "PASS7-008: the clock must never complete a shipment.");
            Assert.IsFalse(shipment.deliveryApplied, "No delivery authority applied the receipt.");
            Assert.AreEqual(8, shipment.RemainingQuantityUnits, "Cargo intact while awaiting delivery.");
            Assert.AreEqual(0, shipment.DeliveredQuantityUnits, "Nothing was accepted.");
        }

        [Test]
        public void SaveLoad_UnresolvedDelivery_PreservesCargoAndFlags()
        {
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "sl-1",
                transferAgreementId = "ta-1",
                state = LogisticsShipmentStatus.Partial,
                plannedQuantityUnits = 100,
                remainingQuantityUnits = 70,
                deliveredQuantityUnits = 30,
                loadApplied = true,
                deliveryApplied = false,
                blockedReason = "recipient accepted 30/100u (partial)",
                loadingDurationGameSeconds = 10f,
                transitDurationGameSeconds = 20f,
                unloadingDurationGameSeconds = 10f,
            };

            LogisticsShipmentSaveDto dto = shipment.CaptureSaveDto();
            LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(dto);

            Assert.AreEqual(100, restored.PlannedQuantityUnits);
            Assert.AreEqual(70, restored.RemainingQuantityUnits, "Unresolved cargo survives reload.");
            Assert.AreEqual(30, restored.DeliveredQuantityUnits, "Accepted count survives reload.");
            Assert.AreEqual(LogisticsShipmentStatus.Partial, restored.Status);
            Assert.IsFalse(restored.deliveryApplied);
            Assert.IsTrue(restored.loadApplied);
            Assert.AreEqual("ta-1", restored.TransferAgreementId);

            // Reloading the restore is idempotent: no cargo invented or destroyed.
            LogisticsShipmentState reread = LogisticsShipmentState.FromSaveDto(restored.CaptureSaveDto());
            Assert.AreEqual(70, reread.RemainingQuantityUnits);
            Assert.AreEqual(30, reread.DeliveredQuantityUnits);
            Assert.AreEqual(LogisticsShipmentStatus.Partial, reread.Status);
        }

        [Test]
        public void SaveLoad_DelayedShipmentWithRealCargo_PreservesCargo()
        {
            // A Delayed shipment whose goods are genuinely in wagon custody
            // (loadApplied) must survive reload with cargo intact — the old
            // "ambiguous quarantine" repair used to vaporize exactly this.
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "sl-2",
                state = LogisticsShipmentStatus.Delayed,
                resumeStateAfterDelay = LogisticsShipmentStatus.Arrived,
                plannedQuantityUnits = 100,
                remainingQuantityUnits = 100,
                loadApplied = true,
                deliveryApplied = false,
                blockedReason = "recipient refused shipment",
            };

            LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(shipment.CaptureSaveDto());
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, restored.Status);
            Assert.AreEqual(100, restored.RemainingQuantityUnits, "Real cargo must survive reload.");
            Assert.IsTrue(restored.loadApplied);
        }

        [Test]
        public void SaveLoad_FailedShipmentWithQuarantinedCargo_PreservesCargo()
        {
            // FailShipmentAfterLoad quarantines stranded cargo ON the failed
            // shipment with an explicit note: visible and auditable, never dropped.
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "sl-3",
                state = LogisticsShipmentStatus.Failed,
                plannedQuantityUnits = 40,
                remainingQuantityUnits = 40,
                loadApplied = true,
                deliveryApplied = false,
                blockedReason = "destination unavailable; STRANDED CARGO: 40u grain quarantined on failed shipment (source unavailable)",
            };

            LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(shipment.CaptureSaveDto());
            Assert.AreEqual(LogisticsShipmentStatus.Failed, restored.Status);
            Assert.AreEqual(40, restored.RemainingQuantityUnits, "Quarantined cargo must survive reload.");
            StringAssert.Contains("STRANDED CARGO", restored.BlockedReason);
        }

        // ------------------------------------------------------------------
        // Freight service: real receipt through the receiving authority.
        // ------------------------------------------------------------------

        private sealed class FreightFixture : IDisposable
        {
            public readonly FreightShipmentService Service = new FreightShipmentService();
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly FreightShipmentHooks Hooks;
            public readonly Dictionary<string, int> RecipientStock = new(StringComparer.Ordinal);
            public int ReceiveCalls;
            public readonly EntityId Loader = EntityId.For(EntityKind.Person, 701);
            public readonly EntityId Driver = EntityId.For(EntityKind.Person, 702);

            public FreightFixture(Func<LogisticsShipmentState, int, int> receiver)
            {
                Budgets.EnsureDay(7);
                Hooks = new FreightShipmentHooks
                {
                    Tasks = Tasks,
                    Budgets = Budgets,
                    DayIndex = 7,
                    Diagnostics = new List<string>(),
                    ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                    {
                        reason = string.Empty;
                        ReceiveCalls++;
                        int accepted = Math.Max(0, receiver(shipment, quantityUnits));
                        if (accepted > 0)
                        {
                            string key = string.IsNullOrWhiteSpace(shipment.destinationBusinessInstanceId)
                                ? "nowhere" : shipment.destinationBusinessInstanceId;
                            RecipientStock[key] = RecipientStock.TryGetValue(key, out int have) ? have + accepted : accepted;
                        }
                        return accepted;
                    },
                };
                Service.RegisterWagon(new EquipmentAsset
                {
                    AssetId = "wagon-1",
                    Kind = "farm-wagon",
                    DisplayName = "Farm wagon",
                    Condition01 = 0.9f,
                    PayloadMassCapacityKg = 1000f,
                    CargoVolumeCapacityM3 = 4f,
                });
            }

            public LogisticsShipmentState CreateAndDeliverToUnload(string shipmentId, int units)
            {
                LogisticsShipmentState shipment = Service.CreateShipment(
                    shipmentId, "mill-1", "store-1", units,
                    new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f },
                    "wagon-1", Hooks, out string creason);
                Assert.NotNull(shipment, "create: " + creason);
                Assert.IsTrue(Service.BeginLoading(shipment, Loader, Hooks, out string lreason), lreason);
                int guard = 0;
                while (!shipment.loadApplied && guard++ < 20)
                    Assert.IsTrue(Service.WorkLoading(shipment, 60, Hooks, out string wreason), wreason);
                Assert.IsTrue(Service.Dispatch(shipment, Driver, null, Hooks, out string dreason), dreason);
                shipment.transitDurationGameSeconds = 120f;
                Service.AdvanceTransit(shipment, 120f, Hooks);
                Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status);
                Assert.IsTrue(Service.BeginUnloading(shipment, Loader, Hooks, out string ureason), ureason);
                return shipment;
            }

            public void WorkUnloadingToSettled(LogisticsShipmentState shipment)
            {
                int guard = 0;
                while (guard++ < 20
                    && shipment.Status != LogisticsShipmentStatus.Completed
                    && shipment.Status != LogisticsShipmentStatus.Partial
                    && shipment.Status != LogisticsShipmentStatus.Delayed)
                {
                    Assert.IsTrue(Service.WorkUnloading(shipment, 60, Hooks, out string wreason), wreason);
                }
            }

            public void Dispose() { }
        }

        [Test]
        public void FreightUnload_FullAcceptance_CompletesWithRealCustody()
        {
            using var fx = new FreightFixture((shipment, qty) => qty);
            LogisticsShipmentState shipment = fx.CreateAndDeliverToUnload("fr-1", 100);
            fx.WorkUnloadingToSettled(shipment);

            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.IsTrue(shipment.deliveryApplied);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits);
            Assert.AreEqual(100, shipment.DeliveredQuantityUnits, "Delivered equals actual accepted.");
            Assert.AreEqual(100, fx.RecipientStock["store-1"], "Goods landed in real recipient stock.");
            Assert.AreEqual(1, fx.ReceiveCalls, "Receipt invoked exactly once.");
            Assert.IsTrue(fx.Service.VerifyConservation(shipment, 100, out string cons), cons);
        }

        [Test]
        public void FreightUnload_RefusedReceipt_HoldsAsDelayed_GoodsRetained_RetrySucceeds()
        {
            bool refuse = true;
            using var fx = new FreightFixture((shipment, qty) => refuse ? 0 : qty);
            LogisticsShipmentState shipment = fx.CreateAndDeliverToUnload("fr-2", 100);
            fx.WorkUnloadingToSettled(shipment);

            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status,
                "PASS7-008: a refused receipt must not be marked delivered.");
            Assert.IsFalse(shipment.deliveryApplied);
            Assert.AreEqual(100, shipment.RemainingQuantityUnits, "Goods remain in legitimate wagon custody.");
            Assert.AreEqual(0, shipment.DeliveredQuantityUnits);
            Assert.IsFalse(fx.RecipientStock.ContainsKey("store-1"), "Nothing reached the recipient.");

            // Retry after the recipient can accept: resume, unload again, deliver.
            refuse = false;
            fx.Service.ResumeShipment(shipment, fx.Hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status);
            Assert.IsTrue(fx.Service.BeginUnloading(shipment, fx.Loader, fx.Hooks, out string ureason), ureason);
            fx.WorkUnloadingToSettled(shipment);

            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits);
            Assert.AreEqual(100, shipment.DeliveredQuantityUnits);
            Assert.AreEqual(100, fx.RecipientStock["store-1"]);
            Assert.IsTrue(fx.Service.VerifyConservation(shipment, 100, out string cons), cons);
        }

        [Test]
        public void FreightUnload_PartialAcceptance_RecordsExactCounts_RetryCompletes()
        {
            int acceptCap = 30;
            using var fx = new FreightFixture((shipment, qty) => Math.Min(qty, acceptCap));
            LogisticsShipmentState shipment = fx.CreateAndDeliverToUnload("fr-3", 100);
            fx.WorkUnloadingToSettled(shipment);

            Assert.AreEqual(LogisticsShipmentStatus.Partial, shipment.Status);
            Assert.IsFalse(shipment.deliveryApplied);
            Assert.AreEqual(30, shipment.DeliveredQuantityUnits, "Exact accepted quantity recorded.");
            Assert.AreEqual(70, shipment.RemainingQuantityUnits, "Exact remaining quantity recorded.");
            Assert.AreEqual(30, fx.RecipientStock["store-1"]);
            Assert.IsTrue(fx.Service.VerifyConservation(shipment, 30, out string cons), cons);

            // Retry the remainder: the recipient now takes everything.
            acceptCap = int.MaxValue;
            Assert.IsTrue(fx.Service.BeginUnloading(shipment, fx.Loader, fx.Hooks, out string ureason), ureason);
            fx.WorkUnloadingToSettled(shipment);

            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.AreEqual(100, shipment.DeliveredQuantityUnits);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits);
            Assert.AreEqual(100, fx.RecipientStock["store-1"], "No duplication across partial retries.");
            Assert.IsTrue(fx.Service.VerifyConservation(shipment, 100, out string cons2), cons2);
        }

        [Test]
        public void FreightUnload_NoReceiver_HoldsAsDelayed_NotFalselyDelivered()
        {
            using var fx = new FreightFixture((shipment, qty) => qty);
            fx.Hooks.ReceiveShipment = null;
            LogisticsShipmentState shipment = fx.CreateAndDeliverToUnload("fr-4", 100);
            fx.WorkUnloadingToSettled(shipment);

            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status,
                "With no receiving authority, the shipment must hold — never false-deliver.");
            Assert.IsFalse(shipment.deliveryApplied);
            Assert.AreEqual(100, shipment.RemainingQuantityUnits, "Cargo held in wagon custody.");
            StringAssert.Contains("no receiving authority", shipment.BlockedReason);
        }

        [Test]
        public void FreightUnload_ConservationGate_RejectsMismatchedClaim()
        {
            using var fx = new FreightFixture((shipment, qty) => qty);
            LogisticsShipmentState shipment = fx.CreateAndDeliverToUnload("fr-5", 100);
            fx.WorkUnloadingToSettled(shipment);

            Assert.IsTrue(fx.Service.VerifyConservation(shipment, 100, out _));
            Assert.IsFalse(fx.Service.VerifyConservation(shipment, 60, out string reason),
                "A delivered claim that disagrees with actual acceptance must fail: " + reason);
        }

        [Test]
        public void FreightBlockedShipment_DuplicateCreation_NoCargoDuplication()
        {
            using var fx = new FreightFixture((shipment, qty) => qty);
            var bulky = new FreightCargoSpec { MassKgPerUnit = 5f, VolumeM3PerUnit = 0.01f };

            // The same commercial intent retried twice: two blocked records, zero
            // cargo each — goods cannot duplicate on failed shipment creation.
            LogisticsShipmentState first = fx.Service.CreateShipment(
                "blocked-attempt-1", "mill-1", "store-1", 1000, bulky, "wagon-1", fx.Hooks, out _);
            LogisticsShipmentState second = fx.Service.CreateShipment(
                "blocked-attempt-2", "mill-1", "store-1", 1000, bulky, "wagon-1", fx.Hooks, out _);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.IsTrue(first.IsPreLoadBlocked);
            Assert.IsTrue(second.IsPreLoadBlocked);
            Assert.AreEqual(0, first.RemainingQuantityUnits);
            Assert.AreEqual(0, second.RemainingQuantityUnits);
            Assert.IsFalse(first.loadApplied);
            Assert.IsFalse(second.loadApplied);
            Assert.AreEqual(0, fx.RecipientStock.Count, "Blocked shipments deliver nothing.");
            Assert.IsTrue(fx.Service.VerifyConservation(first, 0, out string c1), c1);
            Assert.IsTrue(fx.Service.VerifyConservation(second, 0, out string c2), c2);
        }

        // ------------------------------------------------------------------
        // External trade boundary receipt.
        // ------------------------------------------------------------------

        [Test]
        public void ExternalPurchaseArrival_CreditedOnce_WithProvenance()
        {
            var authority = new ExternalMarketAuthority();
            var counterparty = new ExternalCounterparty
            {
                CounterpartyId = "ext-1",
                DisplayName = "Off-map trader",
                TransitDays = 2,
            };
            counterparty.SupplyUnits["grain"] = 50;
            Assert.IsNull(authority.RegisterCounterparty(counterparty));

            string receipt = authority.RecordExternalPurchase("ext-1", "grain", 20, 5000, null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(receipt), "Purchase recorded.");

            Assert.AreEqual(0, authority.ResolveArrivals(0).Count, "In transit: nothing arrives early.");
            var arrived = authority.ResolveArrivals(2);
            Assert.AreEqual(1, arrived.Count, "Arrives on the modeled day.");
            Assert.AreEqual(0, authority.ResolveArrivals(3).Count, "Arrival resolves exactly once.");

            // The caller credits local stock through the idempotent receive path,
            // keyed by the receipt id as provenance.
            GenericBusinessConfiguration config = NewStoreConfig("grain", "bulk");
            Assert.IsTrue(config.TryReceiveInventoryShipment(receipt, "grain", 20f, 250, null, "external:ext-1", 2));
            Assert.AreEqual(20f, config.GetAvailableInventoryQuantity("grain"), 0.001f);

            Assert.IsFalse(config.TryReceiveInventoryShipment(receipt, "grain", 20f, 250, null, "external:ext-1", 2),
                "A repeated arrival credit must be refused — no duplication at the boundary.");
            Assert.AreEqual(20f, config.GetAvailableInventoryQuantity("grain"), 0.001f);
        }

        // ------------------------------------------------------------------
        // Purchase cancellation: in-flight goods are not vaporized.
        // ------------------------------------------------------------------

        [Test]
        public void PurchaseCancellation_InFlightShipmentStillDeliversExactlyOnce()
        {
            // Commercial cancellation must not vaporize goods already in transit,
            // and the late delivery must settle exactly once against the record.
            var agreement = new BusinessTransferAgreementState();
            agreement.Configure(
                "ta-cancel-1", "mill-1", "store-1", "grain", "grain",
                BusinessTransferAllocationMode.FixedUnits, 100,
                BusinessCadence.Weekly, 1,
                BusinessTransferPricingMode.Market, 1.0f,
                0, LocalRecurringOrderHaulingResponsibility.Seller, true, 0);
            Assert.IsTrue(agreement.Active);

            using var fx = new FreightFixture((shipment, qty) => qty);
            LogisticsShipmentState shipment = fx.CreateAndDeliverToUnload("fr-cancel-1", 100);
            shipment.transferAgreementId = "ta-cancel-1";

            // The buyer cancels while the goods are on the wagon.
            agreement.RecordCancelled("buyer cancelled the purchase");
            Assert.IsFalse(agreement.Active);

            // The physical delivery still completes: custody is real, not contractual.
            fx.WorkUnloadingToSettled(shipment);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.AreEqual(100, fx.RecipientStock["store-1"],
                "In-flight goods are not vaporized by a purchase cancellation.");

            // Settlement records the actual delivery once; the agreement stays cancelled.
            agreement.RecordDelivery(1, shipment.DeliveredQuantityUnits, 100 * 250,
                "late delivery after cancellation", shipment.ShipmentId);
            Assert.AreEqual(100, agreement.LastDeliveredUnits);
            Assert.AreEqual("fr-cancel-1", agreement.LastShipmentId);
            Assert.IsFalse(agreement.Active, "Recording a delivery must not resurrect a cancelled agreement.");
            agreement.RecordDelivery(1, shipment.DeliveredQuantityUnits, 100 * 250,
                "duplicate notification", shipment.ShipmentId);
            Assert.AreEqual(100, agreement.LastDeliveredUnits,
                "A duplicate delivery notification must not double-record.");
        }

        private static BusinessInstanceState NewTestBusiness(string instanceId)
        {
            BusinessProfileDefinition profile =
                BusinessProfileDefinition.CreateFallback(BusinessType.GeneralStore, "Test Store");
            return BusinessInstanceState.Create(
                instanceId, profile, 1, BusinessOwnerIdentity.Npc(1, "Test Owner", "Test"));
        }
    }
}

