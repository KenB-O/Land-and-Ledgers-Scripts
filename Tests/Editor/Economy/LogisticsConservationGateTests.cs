using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Pathing;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class LogisticsConservationGateTests
    {
        // -----------------------------------------------------------------------------------------
        // 1. Invariant A & 1: Blocked pre-load shipment has zero physical cargo and non-deliverable state
        // -----------------------------------------------------------------------------------------
        [Test]
        public void BlockedPreLoadShipment_HasZeroAuthoritativeLoadedCargo()
        {
            List<Object> cleanup = new();
            try
            {
                LogisticsRuntimeManager runtime = CreateLogisticsRuntime(cleanup);
                BusinessInstanceState source = CreateBusiness(BusinessType.Butcher, "source_1", 1);
                BusinessInstanceState destination = CreateBusiness(BusinessType.GeneralStore, "dest_1", 2);

                // Both endpoints lack road access, triggering CreateBlockedShipment
                LogisticsShipmentState shipment = runtime.CreateLocalBusinessShipment(
                    source,
                    "meat",
                    destination,
                    "meat",
                    units: 12,
                    sellerUnitRevenueCents: 200,
                    buyerUnitCostCents: 250);

                Assert.NotNull(shipment, "Blocked shipment record should be created for tracking.");
                Assert.AreEqual(12, shipment.PlannedQuantityUnits, "Planned quantity reflects commercial intent.");
                Assert.AreEqual(0, shipment.RemainingQuantityUnits, "Authoritative loaded physical cargo must be zero.");
                Assert.IsFalse(shipment.loadApplied, "loadApplied must be false when loading never occurred.");
                Assert.IsFalse(shipment.sourceCommittedAtSchedule, "sourceCommittedAtSchedule must be false.");
                Assert.IsFalse(shipment.deliveryApplied, "deliveryApplied must be false.");
                Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status, "State must be Failed to block advancement.");
                Assert.IsTrue(shipment.IsPreLoadBlocked, "IsPreLoadBlocked should return true.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(shipment.BlockedReason), "Blocked reason should be recorded.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 2. Invariant B & 2: Local route failure preserves source inventory
        // -----------------------------------------------------------------------------------------
        [Test]
        public void LocalRouteFailure_PreservesSourceInventory()
        {
            List<Object> cleanup = new();
            try
            {
                LogisticsRuntimeManager runtime = CreateLogisticsRuntime(cleanup);
                BusinessInstanceState source = CreateBusiness(BusinessType.Butcher, "source_2", 1);
                BusinessInstanceState destination = CreateBusiness(BusinessType.GeneralStore, "dest_2", 2);
                SetStock(source, "meat", 20);
                SetStock(destination, "meat", 0);

                int sourceBefore = source.RuntimeState.GetCategoryStock("meat").CurrentStockUnits;
                int destBefore = destination.RuntimeState.GetCategoryStock("meat").CurrentStockUnits;

                LogisticsShipmentState shipment = runtime.CreateLocalBusinessShipment(
                    source,
                    "meat",
                    destination,
                    "meat",
                    units: 8,
                    sellerUnitRevenueCents: 200,
                    buyerUnitCostCents: 250);

                int sourceAfter = source.RuntimeState.GetCategoryStock("meat").CurrentStockUnits;
                int destAfter = destination.RuntimeState.GetCategoryStock("meat").CurrentStockUnits;
                int cargo = shipment != null ? shipment.RemainingQuantityUnits : 0;

                Assert.AreEqual(20, sourceAfter, "Source inventory must be preserved when route fails.");
                Assert.AreEqual(0, destAfter, "Destination inventory must remain unchanged.");
                Assert.AreEqual(0, cargo, "Shipment must not own physical cargo.");
                Assert.AreEqual(sourceBefore + destBefore, sourceAfter + destAfter + cargo, "Total physical units strictly conserved.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 3. Invariant B & 3: Off-map inbound failure preserves physical truth
        // -----------------------------------------------------------------------------------------
        [Test]
        public void OffMapInboundFailure_PreservesPhysicalTruth()
        {
            List<Object> cleanup = new();
            try
            {
                LogisticsRuntimeManager runtime = CreateLogisticsRuntime(cleanup);
                BusinessInstanceState destination = CreateBusiness(BusinessType.GeneralStore, "dest_3", 2);
                SetStock(destination, "tools_hardware", 5);

                int destBefore = destination.RuntimeState.GetCategoryStock("tools_hardware").CurrentStockUnits;

                LogisticsShipmentState shipment = runtime.CreateOffMapInboundShipment(
                    destination,
                    "tools_hardware",
                    units: 10,
                    buyerUnitCostCents: 150,
                    LogisticsShipmentDeliveryMode.AddCategoryStock,
                    "offmap inbound test");

                int destAfter = destination.RuntimeState.GetCategoryStock("tools_hardware").CurrentStockUnits;
                int cargo = shipment != null ? shipment.RemainingQuantityUnits : 0;

                Assert.NotNull(shipment);
                Assert.AreEqual(0, cargo, "Off-map inbound failure must have zero physical cargo.");
                Assert.AreEqual(destBefore, destAfter, "Destination inventory untouched by failed creation.");
                Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 4. Invariant 4: Missing carrier cannot fabricate loaded cargo
        // -----------------------------------------------------------------------------------------
        [Test]
        public void MissingCarrierFailure_CannotFabricateLoadedCargo()
        {
            List<Object> cleanup = new();
            try
            {
                TownGrid grid = CreateRoadGrid(8, 8, 3);
                TownWorldController townWorld = CreateTownWorld(grid, cleanup);
                PathingManager pathing = CreatePathingManager(townWorld, cleanup);
                GameObject sharedObject = new("Shared Runtime");
                GameObject logisticsObject = new("Logistics Runtime");
                cleanup.Add(sharedObject);
                cleanup.Add(logisticsObject);

                SharedBusinessRuntimeManager sharedRuntime = sharedObject.AddComponent<SharedBusinessRuntimeManager>();
                LogisticsRuntimeManager runtime = logisticsObject.AddComponent<LogisticsRuntimeManager>();
                runtime.Configure(townWorld, pathing, null, null, sharedRuntime);

                BusinessInstanceState seller = CreateBusiness(BusinessType.Butcher, "seller_4", 1);
                BusinessInstanceState buyer = CreateBusiness(BusinessType.GeneralStore, "buyer_4", 2);
                SetStock(seller, "meat", 15);
                SetBusinesses(sharedRuntime, new List<BusinessInstanceState> { seller, buyer }); // No LiveryFreight carrier

                LogisticsShipmentState shipment = runtime.CreateLocalBusinessShipment(
                    seller,
                    "meat",
                    buyer,
                    "meat",
                    units: 6,
                    sellerUnitRevenueCents: 200,
                    buyerUnitCostCents: 250,
                    haulingMode: ShipmentHaulingMode.HiredFreight);

                Assert.NotNull(shipment);
                Assert.AreEqual(0, shipment.RemainingQuantityUnits, "Missing carrier cannot fabricate cargo.");
                Assert.AreEqual(6, shipment.PlannedQuantityUnits, "Planned units preserved.");
                Assert.IsFalse(shipment.loadApplied);
                Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
                StringAssert.Contains("carrier", shipment.BlockedReason.ToLowerInvariant());
                Assert.AreEqual(15, seller.RuntimeState.GetCategoryStock("meat").CurrentStockUnits, "Source stock conserved.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 5. Invariant C & 5: Blocked pre-load shipment cannot advance into unloading or delivery
        // -----------------------------------------------------------------------------------------
        [Test]
        public void BlockedPreLoadShipment_CannotAdvanceIntoUnloadingOrDelivery()
        {
            List<Object> cleanup = new();
            try
            {
                LogisticsRuntimeManager runtime = CreateLogisticsRuntime(cleanup);
                BusinessInstanceState source = CreateBusiness(BusinessType.Butcher, "src_5", 1);
                BusinessInstanceState destination = CreateBusiness(BusinessType.GeneralStore, "dst_5", 2);

                LogisticsShipmentState shipment = runtime.CreateLocalBusinessShipment(
                    source, "meat", destination, "meat", 10, 200, 250);

                Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
                Assert.AreEqual(0, shipment.RemainingQuantityUnits);

                // Advance time significantly
                shipment.AdvanceGameSeconds(10000f);

                Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status, "Status must remain Failed across time.");
                Assert.AreEqual(0, shipment.RemainingQuantityUnits, "Cargo remains zero.");
                Assert.IsFalse(shipment.loadApplied, "Load must never apply.");
                Assert.IsFalse(shipment.deliveryApplied, "Delivery must never apply.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 6. Invariant 6: TryApplyDelivery refuses/non-applies never-loaded blocked shipment
        // -----------------------------------------------------------------------------------------
        [Test]
        public void TryApplyDelivery_RefusesNeverLoadedBlockedShipment()
        {
            List<Object> cleanup = new();
            try
            {
                LogisticsRuntimeManager runtime = CreateLogisticsRuntime(cleanup);
                BusinessInstanceState source = CreateBusiness(BusinessType.Butcher, "src_6", 1);
                BusinessInstanceState destination = CreateBusiness(BusinessType.GeneralStore, "dst_6", 2);
                SetStock(destination, "meat", 0);

                LogisticsShipmentState shipment = runtime.CreateLocalBusinessShipment(
                    source, "meat", destination, "meat", 5, 200, 250);

                // Directly invoke TryApplyDelivery
                InvokeApplyDelivery(runtime, shipment);

                Assert.IsFalse(shipment.deliveryApplied, "deliveryApplied must remain false.");
                Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status, "Status must remain Failed.");
                Assert.AreEqual(0, destination.RuntimeState.GetCategoryStock("meat").CurrentStockUnits, "Destination receives no goods.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 7, 8, 9, 10. Invariants D, E, F & 7-10: Financial conservation (no buyer pay, seller credit, freight charge/revenue)
        // -----------------------------------------------------------------------------------------
        [Test]
        public void BlockedPreLoadShipment_FinancialConservationStrictlyEnforced()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject sharedObject = new("Shared Runtime");
                GameObject logisticsObject = new("Logistics Runtime");
                cleanup.Add(sharedObject);
                cleanup.Add(logisticsObject);

                SharedBusinessRuntimeManager sharedRuntime = sharedObject.AddComponent<SharedBusinessRuntimeManager>();
                LogisticsRuntimeManager runtime = logisticsObject.AddComponent<LogisticsRuntimeManager>();
                SetPrivateField(runtime, "sharedBusinessRuntime", sharedRuntime);

                BusinessInstanceState seller = CreateBusiness(BusinessType.Butcher, "seller_fin", 1);
                BusinessInstanceState buyer = CreateBusiness(BusinessType.GeneralStore, "buyer_fin", 2);
                BusinessInstanceState carrier = CreateBusiness(BusinessType.LiveryFreight, "carrier_fin", 3);
                SetBusinesses(sharedRuntime, new List<BusinessInstanceState> { seller, buyer, carrier });

                LogisticsShipmentState shipment = runtime.CreateLocalBusinessShipment(
                    seller, "meat", buyer, "meat", 10, 200, 250, haulingMode: ShipmentHaulingMode.HiredFreight);

                // Attempt delivery on blocked shipment
                InvokeApplyDelivery(runtime, shipment);

                // Buyer payment check (Invariant D & 7)
                Assert.AreEqual(0, buyer.RuntimeState.LastWeeklyLocalTransferCostCents, "No buyer payment on blocked shipment.");

                // Seller revenue check (Invariant E & 8)
                Assert.AreEqual(0, seller.RuntimeState.LastWeeklyLocalTransferRevenueCents, "No seller credit on blocked shipment.");

                // Freight carrier revenue check (Invariant F & 10)
                Assert.AreEqual(0, carrier.RuntimeState.LastWeeklyLocalTransferRevenueCents, "No carrier freight revenue on blocked shipment.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 11 & 12. Invariant G & 11, 12: Recurring order failure and retry conserves inventory and money
        // -----------------------------------------------------------------------------------------
        [Test]
        public void RecurringOrder_FailureAndRetry_ConservesTotalInventoryAndPayments()
        {
            List<Object> cleanup = new();
            try
            {
                LocalRecurringOrderManager manager = new();
                BusinessInstanceState seller = CreateBusiness(BusinessType.Ranch, "seller_rec", 1);
                BusinessInstanceState buyer = CreateBusiness(BusinessType.Butcher, "buyer_rec", 2);
                SetStock(seller, "livestock_inputs", 10);
                SetStock(buyer, "livestock_inputs", 0);

                LocalRecurringOrderTemplate template = new(
                    "rec_test_order",
                    BusinessType.Ranch,
                    "livestock_inputs",
                    BusinessType.Butcher,
                    "livestock_inputs",
                    5);

                int initialTotalStock = seller.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits +
                                        buyer.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits;

                // Simulate delivery scheduler that fails (e.g. logistics route blocked)
                int scheduledShipmentCalls = 0;
                LocalRecurringOrderFulfillmentRequest request = new()
                {
                    Template = template,
                    Seller = seller,
                    Buyer = buyer,
                    WeekKey = 1,
                    UnitPriceCents = 100,
                    ExtraBuyerUnitCostCents = 0,
                    BuyerCashReserveCents = 0,
                    SellerCanFulfill = true,
                    BuyerCanReceive = true,
                    ScheduleShipment = (req, units, sellPrice, buyCost) =>
                    {
                        scheduledShipmentCalls++;
                        // Route blocked: returns 0 accepted units, representing a blocked shipment creation
                        return 0;
                    }
                };

                // Week 1 attempt
                LocalRecurringOrderFulfillmentResult resultWeek1 = manager.ResolveOrder(request);

                Assert.AreEqual(1, scheduledShipmentCalls);
                int stockAfterWeek1 = seller.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits +
                                      buyer.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits;
                Assert.AreEqual(initialTotalStock, stockAfterWeek1, "Inventory conserved after failed scheduling in Week 1.");
                Assert.AreEqual(0, seller.RuntimeState.LastWeeklyLocalTransferRevenueCents, "No seller revenue booked on failed attempt.");
                Assert.AreEqual(0, buyer.RuntimeState.LastWeeklyLocalTransferCostCents, "No buyer cost booked on failed attempt.");

                // Week 2 retry
                request.WeekKey = 2;
                LocalRecurringOrderFulfillmentResult resultWeek2 = manager.ResolveOrder(request);

                Assert.AreEqual(2, scheduledShipmentCalls);
                int stockAfterWeek2 = seller.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits +
                                      buyer.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits;
                Assert.AreEqual(initialTotalStock, stockAfterWeek2, "Inventory conserved after retry in Week 2.");
                Assert.AreEqual(0, seller.RuntimeState.LastWeeklyLocalTransferRevenueCents, "No payment duplication on retry.");
                Assert.AreEqual(0, buyer.RuntimeState.LastWeeklyLocalTransferCostCents, "No cost duplication on retry.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 13. Invariant H & 13: General Store failed reorder restores pending reorder without deliverable cargo
        // -----------------------------------------------------------------------------------------
        [Test]
        public void GeneralStore_FailedReorder_RestoresPendingReorderWithoutDeliverableCargo()
        {
            List<Object> cleanup = new();
            try
            {
                LogisticsRuntimeManager runtime = CreateLogisticsRuntime(cleanup);
                BusinessInstanceState store = CreateBusiness(BusinessType.GeneralStore, "gs_reorder", 1);
                SetStock(store, "staple_food", 5);
                CategoryStockState stapleStock = store.RuntimeState.GetCategoryStock("staple_food");
                int expectedPending = stapleStock.TargetStockUnits - stapleStock.CurrentStockUnits;
                stapleStock.QueueReorderToTarget();

                int pendingBefore = stapleStock.PendingReorderUnits;
                Assert.AreEqual(expectedPending, pendingBefore);

                // Allocate reorder units
                int allocateUnits = Mathf.Min(10, pendingBefore);
                int allocated = store.RuntimeState.AllocatePendingReorderUnits("staple_food", allocateUnits);
                Assert.AreEqual(allocateUnits, allocated);
                Assert.AreEqual(pendingBefore - allocateUnits, stapleStock.PendingReorderUnits);

                // Create inbound shipment with road failure -> blocked
                LogisticsShipmentState blockedShipment = runtime.CreateOffMapInboundShipment(
                    store, "staple_food", allocated, 100, LogisticsShipmentDeliveryMode.ReceivePendingReorder, "GS Reorder");

                Assert.NotNull(blockedShipment);
                Assert.AreEqual(0, blockedShipment.RemainingQuantityUnits, "Blocked reorder shipment has zero deliverable physical cargo.");

                // Failure rollback: re-queue reorder to target
                stapleStock.QueueReorderToTarget();

                Assert.AreEqual(expectedPending, stapleStock.PendingReorderUnits, "Pending reorder restored to target.");
                Assert.AreEqual(5, stapleStock.CurrentStockUnits, "Current stock unchanged.");

                // Attempt delivery on the blocked shipment
                InvokeApplyDelivery(runtime, blockedShipment);

                Assert.AreEqual(5, stapleStock.CurrentStockUnits, "No phantom cargo was delivered to store stock.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 14. Invariant I & 14: Valid blocked shipment save/load round-trips with zero cargo intact
        // -----------------------------------------------------------------------------------------
        [Test]
        public void BlockedShipment_SaveLoadRoundTrip_PreservesZeroCargoAndNonDeliverability()
        {
            List<Object> cleanup = new();
            try
            {
                LogisticsRuntimeManager runtime = CreateLogisticsRuntime(cleanup);
                BusinessInstanceState source = CreateBusiness(BusinessType.Butcher, "src_save", 1);
                BusinessInstanceState destination = CreateBusiness(BusinessType.GeneralStore, "dst_save", 2);

                LogisticsShipmentState shipment = runtime.CreateLocalBusinessShipment(
                    source, "meat", destination, "meat", 8, 200, 250);

                Assert.AreEqual(0, shipment.RemainingQuantityUnits);
                Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);

                LogisticsShipmentSaveDto dto = shipment.CaptureSaveDto();
                LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(dto);

                Assert.AreEqual(8, restored.PlannedQuantityUnits, "Planned quantity preserved.");
                Assert.AreEqual(0, restored.RemainingQuantityUnits, "Remaining cargo preserved as zero.");
                Assert.IsFalse(restored.loadApplied, "loadApplied preserved as false.");
                Assert.IsFalse(restored.sourceCommittedAtSchedule, "sourceCommittedAtSchedule preserved as false.");
                Assert.AreEqual(LogisticsShipmentStatus.Failed, restored.Status, "Status preserved as Failed.");
                Assert.IsTrue(restored.IsPreLoadBlocked, "IsPreLoadBlocked remains true after reload.");

                // Attempt to advance after reload
                restored.AdvanceGameSeconds(5000f);
                Assert.AreEqual(LogisticsShipmentStatus.Failed, restored.Status, "Cannot advance after reload.");
                Assert.AreEqual(0, restored.RemainingQuantityUnits);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 15. Invariant J & 15: Deterministic repair of legacy malformed blocked save state
        // -----------------------------------------------------------------------------------------
        [Test]
        public void LegacyMalformedBlockedShipment_DeterministicRepair_ZeroesCargoAndSetsFailed()
        {
            // Simulate old defective save file where CreateBlockedShipment wrote remainingQuantity = planned,
            // loadApplied = true, sourceCommittedAtSchedule = true, state = Delayed
            LogisticsShipmentSaveDto legacyDto = new()
            {
                shipmentId = "blocked:source->dest:meat:legacy123",
                shipmentKind = LogisticsShipmentKind.LocalTradeTransfer,
                cargoClass = LogisticsCargoClass.Perishables,
                supplierClass = LogisticsSupplierClass.LocalBusiness,
                sourceBusinessInstanceId = "source",
                destinationBusinessInstanceId = "dest",
                sourceCategoryId = "meat",
                destinationCategoryId = "meat",
                plannedQuantityUnits = 10,
                remainingQuantityUnits = 10, // Defect: phantom cargo!
                sourceCommittedAtSchedule = true, // Defect: false commitment!
                loadApplied = true, // Defect: false load flag!
                deliveryApplied = false,
                state = LogisticsShipmentStatus.Delayed, // Defect: Delayed instead of Failed!
                blockedReason = "route planning failed"
            };

            LogisticsShipmentState repaired = LogisticsShipmentState.FromSaveDto(legacyDto);

            Assert.AreEqual(10, repaired.PlannedQuantityUnits, "Planned quantity preserved for context.");
            Assert.AreEqual(0, repaired.RemainingQuantityUnits, "Deterministic repair zeroes phantom cargo.");
            Assert.IsFalse(repaired.loadApplied, "loadApplied normalized to false.");
            Assert.IsFalse(repaired.sourceCommittedAtSchedule, "sourceCommittedAtSchedule normalized to false.");
            Assert.AreEqual(LogisticsShipmentStatus.Failed, repaired.Status, "Status repaired to Failed.");
            Assert.IsTrue(repaired.IsPreLoadBlocked, "IsPreLoadBlocked is true on repaired record.");
        }

        // -----------------------------------------------------------------------------------------
        // 16. Invariant K & 16: Ambiguous malformed shipment is quarantined rather than inventing history
        // -----------------------------------------------------------------------------------------
        [Test]
        public void AmbiguousMalformedShipment_QuarantinedAndCannotProduceDelivery()
        {
            List<Object> cleanup = new();
            try
            {
                LogisticsRuntimeManager runtime = CreateLogisticsRuntime(cleanup);
                BusinessInstanceState buyer = CreateBusiness(BusinessType.GeneralStore, "buyer_amb", 1);
                SetStock(buyer, "clothing", 0);

                // Ambiguous record: Delayed with blocked reason, not delivered, remaining > 0, but not starting with "blocked:"
                LogisticsShipmentSaveDto ambiguousDto = new()
                {
                    shipmentId = "local:source->dest:clothing:ambiguous456",
                    shipmentKind = LogisticsShipmentKind.LocalTradeTransfer,
                    cargoClass = LogisticsCargoClass.PackagedGoods,
                    supplierClass = LogisticsSupplierClass.LocalBusiness,
                    sourceBusinessInstanceId = "source_amb",
                    destinationBusinessInstanceId = buyer.InstanceId,
                    sourceCategoryId = "clothing",
                    destinationCategoryId = "clothing",
                    plannedQuantityUnits = 5,
                    remainingQuantityUnits = 5,
                    buyerUnitCostCents = 300,
                    deliveryApplied = false,
                    state = LogisticsShipmentStatus.Delayed,
                    blockedReason = "destination temporarily unreachable"
                };

                LogisticsShipmentState quarantined = LogisticsShipmentState.FromSaveDto(ambiguousDto);

                Assert.AreEqual(LogisticsShipmentStatus.Failed, quarantined.Status, "Ambiguous shipment quarantined to Failed.");
                Assert.AreEqual(0, quarantined.RemainingQuantityUnits, "Quarantined shipment cargo zeroed to prevent phantom delivery.");

                // Attempt delivery
                InvokeApplyDelivery(runtime, quarantined);

                Assert.IsFalse(quarantined.deliveryApplied, "Quarantined shipment refused by delivery.");
                Assert.AreEqual(0, buyer.RuntimeState.GetCategoryStock("clothing").CurrentStockUnits, "No goods delivered.");
                Assert.AreEqual(0, buyer.RuntimeState.LastWeeklyLocalTransferCostCents, "No payment fabricated.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // -----------------------------------------------------------------------------------------
        // 17. Invariant L & 17: All current CreateBlockedShipment call-path families covered
        // -----------------------------------------------------------------------------------------
        [Test]
        public void AllCreateBlockedShipmentCallPaths_Covered()
        {
            List<Object> cleanup = new();
            try
            {
                LogisticsRuntimeManager runtime = CreateLogisticsRuntime(cleanup);
                BusinessInstanceState b1 = CreateBusiness(BusinessType.Butcher, "b1", 1);
                BusinessInstanceState b2 = CreateBusiness(BusinessType.GeneralStore, "b2", 2);

                // Path 1: Local business shipment — Missing road access
                LogisticsShipmentState s1 = runtime.CreateLocalBusinessShipment(b1, "meat", b2, "meat", 4, 100, 120);
                Assert.NotNull(s1);
                Assert.IsTrue(s1.IsPreLoadBlocked, "Path 1 (Local road access) must produce pre-load blocked shipment.");
                Assert.AreEqual(0, s1.RemainingQuantityUnits);

                // Path 2: Off-map inbound shipment — Missing road access
                LogisticsShipmentState s2 = runtime.CreateOffMapInboundShipment(b2, "meat", 4, 120, LogisticsShipmentDeliveryMode.AddCategoryStock, "offmap in");
                Assert.NotNull(s2);
                Assert.IsTrue(s2.IsPreLoadBlocked, "Path 2 (Off-map inbound road access) must produce pre-load blocked shipment.");
                Assert.AreEqual(0, s2.RemainingQuantityUnits);

                // Path 3: Off-map outbound shipment — Missing road access
                LogisticsShipmentState s3 = runtime.CreateOffMapOutboundShipment(b1, "meat", 4, 100, "offmap out");
                Assert.NotNull(s3);
                Assert.IsTrue(s3.IsPreLoadBlocked, "Path 3 (Off-map outbound road access) must produce pre-load blocked shipment.");
                Assert.AreEqual(0, s3.RemainingQuantityUnits);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        // =========================================================================================
        // Helpers
        // =========================================================================================
        private static LogisticsRuntimeManager CreateLogisticsRuntime(List<Object> cleanup)
        {
            GameObject go = new("Logistics Runtime Test");
            cleanup.Add(go);
            return go.AddComponent<LogisticsRuntimeManager>();
        }

        private static TownGrid CreateRoadGrid(int width, int depth, int roadZ)
        {
            TownGrid grid = new(width, depth, 2f, Vector3.zero);
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    GridCoord coord = new(x, z);
                    TownCell cell = TownCell.CreateDefault();
                    cell.terrainZone = TerrainZone.Buildable;
                    if (z == roadZ)
                    {
                        cell.occupancy = CellOccupancy.Road;
                        cell.roadType = RoadType.MainStreet;
                    }

                    grid.SetCell(coord, cell);
                }
            }

            return grid;
        }

        private static TownWorldController CreateTownWorld(TownGrid grid, List<Object> cleanup)
        {
            GameObject townObject = new("Logistics Test Town");
            cleanup.Add(townObject);
            TownWorldController townWorld = townObject.AddComponent<TownWorldController>();

            FieldInfo gridField = typeof(TownWorldController).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo plotsField = typeof(TownWorldController).GetField("plots", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(gridField);
            Assert.NotNull(plotsField);

            gridField.SetValue(townWorld, grid);
            List<TownPlot> plots = new()
            {
                new TownPlot
                {
                    id = 1,
                    zone = PlotZone.Business,
                    bounds = new GridRect(3, 1, 2, 2),
                    roadAccessCell = new GridCoord(4, 3),
                    frontageCells = 2,
                    depthCells = 2,
                    roadFrontageDirection = GridDirection.South
                },
                new TownPlot
                {
                    id = 2,
                    zone = PlotZone.Business,
                    bounds = new GridRect(1, 1, 2, 2),
                    roadAccessCell = new GridCoord(2, 3),
                    frontageCells = 2,
                    depthCells = 2,
                    roadFrontageDirection = GridDirection.South
                }
            };
            plotsField.SetValue(townWorld, plots);

            FieldInfo buildingsField = typeof(TownWorldController).GetField("buildings", BindingFlags.Instance | BindingFlags.NonPublic);
            if (buildingsField != null)
            {
                List<PlacedBuilding> buildings = new()
                {
                    new PlacedBuilding { id = 1, plotId = 1 },
                    new PlacedBuilding { id = 2, plotId = 2 }
                };
                buildingsField.SetValue(townWorld, buildings);
            }

            return townWorld;
        }

        private static PathingManager CreatePathingManager(TownWorldController townWorld, List<Object> cleanup)
        {
            GameObject pathingObject = new("Logistics Pathing");
            cleanup.Add(pathingObject);
            PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
            cleanup.Add(settings);
            PathingManager pathing = pathingObject.AddComponent<PathingManager>();
            pathing.Configure(townWorld, settings);
            return pathing;
        }

        private static BusinessInstanceState CreateBusiness(BusinessType businessType, string instanceId, int buildingId, BusinessOwnerIdentity owner = null)
        {
            return BusinessInstanceState.Create(instanceId, LoadProfile(businessType), buildingId, owner ?? BusinessOwnerIdentity.Npc(1, "Test Owner", "Test"));
        }

        private static BusinessProfileDefinition LoadProfile(BusinessType businessType)
        {
            string[] guids = AssetDatabase.FindAssets("t:BusinessProfileDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BusinessProfileDefinition profile = AssetDatabase.LoadAssetAtPath<BusinessProfileDefinition>(path);
                if (profile != null && profile.Business.BusinessType == businessType)
                {
                    return profile;
                }
            }

            Assert.Fail($"Missing business profile for {businessType}.");
            return null;
        }

        private static void SetStock(BusinessInstanceState business, string categoryId, int units)
        {
            CategoryStockState stock = business.RuntimeState.GetCategoryStock(categoryId);
            Assert.NotNull(stock, $"{business.BusinessType} should have {categoryId} stock.");
            stock.SetCurrentStockForTests(units);
        }

        private static void SetBusinesses(SharedBusinessRuntimeManager manager, List<BusinessInstanceState> businesses)
        {
            SetPrivateField(manager, "businesses", businesses);
        }

        private static void InvokeApplyDelivery(LogisticsRuntimeManager manager, LogisticsShipmentState shipment)
        {
            MethodInfo method = typeof(LogisticsRuntimeManager).GetMethod("TryApplyDelivery", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(manager, new object[] { shipment });
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Missing field {fieldName} on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static void DestroyAll(List<Object> cleanup)
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
            {
                if (cleanup[i] != null)
                {
                    Object.DestroyImmediate(cleanup[i]);
                }
            }
        }
    }
}
