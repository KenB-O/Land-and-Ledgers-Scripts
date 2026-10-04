using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Pathing;
using LandLedgers.Persistence;
using LandLedgers.Time;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class LogisticsFoundationTests
    {
        [Test]
        public void RoutePlannerUsesNearestRoadEdgeAndPreservesFullTravelTime()
        {
            List<Object> cleanup = new();
            try
            {
                TownGrid grid = CreateRoadGrid(6, 6, 2);
                TownWorldController townWorld = CreateTownWorld(grid, cleanup);

                GameObject pathingObject = new("Logistics Pathing");
                cleanup.Add(pathingObject);
                PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
                cleanup.Add(settings);
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(townWorld, settings);

                LogisticsRoutePlanner planner = new(townWorld, pathing);
                LogisticsRouteRequest request = new(
                    new GridCoord(4, 2),
                    new GridCoord(4, 2),
                    LogisticsPathEndpointKind.MapEdgeRoad,
                    LogisticsPathEndpointKind.GridCoord,
                    extraOffMapCells: 18,
                    label: "off-map inbound");

                Assert.IsTrue(planner.TryPlanRoute(request, out LogisticsRoutePlan route), route.FailureReason);
                Assert.AreEqual(new GridCoord(5, 2), route.VisibleStart);
                Assert.AreEqual(new GridCoord(4, 2), route.VisibleEnd);
                Assert.Greater(route.TotalTravelCells, route.VisibleTravelCells);
                Assert.Greater(route.TotalTravelGameSeconds, route.VisibleTravelGameSeconds);
                Assert.AreEqual(LogisticsRouteQuality.RoadLinked, route.Quality);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ShipmentStateAdvancesThroughMilestonesAcrossGameSeconds()
        {
            LogisticsShipmentState shipment = new()
            {
                shipmentId = "shipment:test",
                cargoClass = LogisticsCargoClass.PackagedGoods,
                supplierClass = LogisticsSupplierClass.OffMapSupplier,
                state = LogisticsShipmentStatus.Planned,
                plannedQuantityUnits = 8,
                remainingQuantityUnits = 8,
                loadingDurationGameSeconds = 10f,
                transitDurationGameSeconds = 20f,
                unloadingDurationGameSeconds = 10f
            };

            shipment.AdvanceGameSeconds(10f);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status);

            shipment.AdvanceGameSeconds(20f);
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status);

            shipment.AdvanceGameSeconds(10f);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
        }

        [Test]
        public void ShipmentStateRoundTripsThroughSaveDto()
        {
            LogisticsShipmentState shipment = new()
            {
                shipmentId = "shipment:save",
                shipmentKind = LogisticsShipmentKind.LocalTradeTransfer,
                cargoClass = LogisticsCargoClass.LiveAnimals,
                supplierClass = LogisticsSupplierClass.HiredHauling,
                haulingMode = ShipmentHaulingMode.HiredFreight,
                sourceEndpointKind = LogisticsShipmentEndpointKind.Business,
                destinationEndpointKind = LogisticsShipmentEndpointKind.Business,
                sourceBusinessInstanceId = "seller",
                destinationBusinessInstanceId = "buyer",
                carrierBusinessInstanceId = "carrier",
                freightPayerBusinessInstanceId = "buyer",
                sourceCategoryId = "livestock_inputs",
                destinationCategoryId = "livestock_inputs",
                state = LogisticsShipmentStatus.InTransit,
                plannedQuantityUnits = 4,
                remainingQuantityUnits = 3,
                buyerUnitCostCents = 250,
                sellerUnitRevenueCents = 225,
                freightChargeCents = 75,
                routePlan = new LogisticsRoutePlan
                {
                    label = "save route",
                    visibleStart = new GridCoord(1, 2),
                    visibleEnd = new GridCoord(5, 2),
                    totalTravelCells = 24,
                    visibleTravelCells = 4,
                    totalTravelGameSeconds = 240f,
                    visibleTravelGameSeconds = 40f,
                    quality = LogisticsRouteQuality.RoadLinked,
                    visiblePath = new List<GridCoord> { new(1, 2), new(2, 2), new(3, 2), new(4, 2), new(5, 2) }
                }
            };

            LogisticsShipmentSaveDto dto = shipment.CaptureSaveDto();
            LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(dto);

            Assert.AreEqual(shipment.ShipmentId, restored.ShipmentId);
            Assert.AreEqual(LogisticsCargoClass.LiveAnimals, restored.CargoClass);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, restored.Status);
            Assert.AreEqual(ShipmentHaulingMode.HiredFreight, restored.HaulingMode);
            Assert.AreEqual("carrier", restored.CarrierBusinessInstanceId);
            Assert.AreEqual("buyer", restored.FreightPayerBusinessInstanceId);
            Assert.AreEqual(75, restored.FreightChargeCents);
            Assert.AreEqual(24, restored.RoutePlan.TotalTravelCells);
            Assert.AreEqual(5, restored.RoutePlan.VisiblePath.Count);
        }

        [Test]
        public void HiredFreightDeliveryBooksCarrierRevenueAndPayerExpenseWithoutDoubleBooking()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject sharedObject = new("Shared Runtime");
                GameObject logisticsObject = new("Logistics Runtime");
                cleanup.Add(sharedObject);
                cleanup.Add(logisticsObject);

                SharedBusinessRuntimeManager sharedRuntime = sharedObject.AddComponent<SharedBusinessRuntimeManager>();
                LogisticsRuntimeManager logisticsRuntime = logisticsObject.AddComponent<LogisticsRuntimeManager>();

                BusinessInstanceState seller = CreateBusiness(BusinessType.Butcher, "seller", 1);
                BusinessInstanceState buyer = CreateBusiness(BusinessType.GeneralStore, "buyer", 2, BusinessOwnerIdentity.Player());
                BusinessInstanceState carrier = CreateBusiness(BusinessType.LiveryFreight, "carrier", 3);
                ActivateAllWorkers(seller);
                ActivateAllWorkers(buyer);
                ActivateAllWorkers(carrier);
                SetStock(seller, "meat", 8);
                SetStock(buyer, "meat", 0);
                SetBusinesses(sharedRuntime, new List<BusinessInstanceState> { seller, buyer, carrier });
                SetPrivateField(logisticsRuntime, "sharedBusinessRuntime", sharedRuntime);

                LogisticsShipmentState shipment = new()
                {
                    shipmentId = "shipment:hired",
                    shipmentKind = LogisticsShipmentKind.LocalTradeTransfer,
                    cargoClass = LogisticsCargoClass.Perishables,
                    supplierClass = LogisticsSupplierClass.HiredHauling,
                    haulingMode = ShipmentHaulingMode.HiredFreight,
                    deliveryMode = LogisticsShipmentDeliveryMode.AddCategoryStock,
                    sourceBusinessInstanceId = seller.InstanceId,
                    destinationBusinessInstanceId = buyer.InstanceId,
                    carrierBusinessInstanceId = carrier.InstanceId,
                    freightPayerBusinessInstanceId = buyer.InstanceId,
                    sourceCategoryId = "meat",
                    destinationCategoryId = "meat",
                    plannedQuantityUnits = 4,
                    remainingQuantityUnits = 4,
                    sellerUnitRevenueCents = 225,
                    buyerUnitCostCents = 250,
                    freightChargeCents = 75,
                    state = LogisticsShipmentStatus.Unloading
                };

                InvokeApplyDelivery(logisticsRuntime, shipment);

                Assert.AreEqual(4, buyer.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);
                Assert.AreEqual(900, seller.RuntimeState.LastWeeklyLocalTransferRevenueCents);
                Assert.AreEqual(1075, buyer.RuntimeState.LastWeeklyLocalTransferCostCents,
                    "Buyer cost is the agreed per-unit cost (4 × 250c) plus the 75c freight charge.");
                Assert.AreEqual(75, carrier.RuntimeState.LastWeeklyLocalTransferRevenueCents);
                StringAssert.Contains("freight", buyer.RuntimeState.LastWeeklyTransferSummary.ToLowerInvariant());
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void SourceDeliveryDoesNotBookLiveryRevenueOrFreightCharge()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject sharedObject = new("Shared Runtime");
                GameObject logisticsObject = new("Logistics Runtime");
                cleanup.Add(sharedObject);
                cleanup.Add(logisticsObject);

                SharedBusinessRuntimeManager sharedRuntime = sharedObject.AddComponent<SharedBusinessRuntimeManager>();
                LogisticsRuntimeManager logisticsRuntime = logisticsObject.AddComponent<LogisticsRuntimeManager>();

                BusinessInstanceState seller = CreateBusiness(BusinessType.Butcher, "seller", 1);
                BusinessInstanceState buyer = CreateBusiness(BusinessType.GeneralStore, "buyer", 2, BusinessOwnerIdentity.Player());
                BusinessInstanceState carrier = CreateBusiness(BusinessType.LiveryFreight, "carrier", 3);
                ActivateAllWorkers(seller);
                ActivateAllWorkers(buyer);
                ActivateAllWorkers(carrier);
                SetStock(seller, "meat", 8);
                SetStock(buyer, "meat", 0);
                SetBusinesses(sharedRuntime, new List<BusinessInstanceState> { seller, buyer, carrier });
                SetPrivateField(logisticsRuntime, "sharedBusinessRuntime", sharedRuntime);

                LogisticsShipmentState shipment = new()
                {
                    shipmentId = "shipment:source",
                    shipmentKind = LogisticsShipmentKind.LocalTradeTransfer,
                    cargoClass = LogisticsCargoClass.Perishables,
                    supplierClass = LogisticsSupplierClass.LocalBusiness,
                    haulingMode = ShipmentHaulingMode.SourceDelivers,
                    deliveryMode = LogisticsShipmentDeliveryMode.AddCategoryStock,
                    sourceBusinessInstanceId = seller.InstanceId,
                    destinationBusinessInstanceId = buyer.InstanceId,
                    sourceCategoryId = "meat",
                    destinationCategoryId = "meat",
                    plannedQuantityUnits = 4,
                    remainingQuantityUnits = 4,
                    sellerUnitRevenueCents = 225,
                    buyerUnitCostCents = 250,
                    freightChargeCents = 75,
                    state = LogisticsShipmentStatus.Unloading
                };

                InvokeApplyDelivery(logisticsRuntime, shipment);

                Assert.AreEqual(4, buyer.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);
                Assert.AreEqual(900, seller.RuntimeState.LastWeeklyLocalTransferRevenueCents);
                Assert.AreEqual(1000, buyer.RuntimeState.LastWeeklyLocalTransferCostCents,
                    "Source delivery still charges the agreed per-unit cost (4 × 250c), but no freight.");
                Assert.AreEqual(0, carrier.RuntimeState.LastWeeklyLocalTransferRevenueCents);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void HiredFreightFallsBackToSourceDeliveryWhenNoCarrierIsAvailable()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject sharedObject = new("Shared Runtime");
                GameObject logisticsObject = new("Logistics Runtime");
                cleanup.Add(sharedObject);
                cleanup.Add(logisticsObject);

                SharedBusinessRuntimeManager sharedRuntime = sharedObject.AddComponent<SharedBusinessRuntimeManager>();
                LogisticsRuntimeManager logisticsRuntime = logisticsObject.AddComponent<LogisticsRuntimeManager>();

                BusinessInstanceState seller = CreateBusiness(BusinessType.Butcher, "seller", 1);
                BusinessInstanceState buyer = CreateBusiness(BusinessType.GeneralStore, "buyer", 2, BusinessOwnerIdentity.Player());
                ActivateAllWorkers(seller);
                ActivateAllWorkers(buyer);
                SetBusinesses(sharedRuntime, new List<BusinessInstanceState> { seller, buyer });
                SetPrivateField(logisticsRuntime, "sharedBusinessRuntime", sharedRuntime);

                LogisticsRoutePlan plan = new()
                {
                    totalTravelCells = 24,
                    totalTravelGameSeconds = 240f
                };

                (ShipmentHaulingMode haulingMode, LogisticsSupplierClass supplierClass, string carrierId, string payerId, int freightChargeCents) =
                    InvokeResolveCarrierAssignment(
                        logisticsRuntime,
                        seller,
                        buyer,
                        LogisticsCargoClass.Perishables,
                        4,
                        plan,
                        ShipmentHaulingMode.HiredFreight,
                        buyer.InstanceId);

                Assert.AreEqual(ShipmentHaulingMode.HiredFreight, haulingMode,
                    "A hired-freight request cannot silently change its hauling authority when no carrier exists.");
                Assert.AreEqual(LogisticsSupplierClass.HiredHauling, supplierClass);
                Assert.IsEmpty(carrierId);
                Assert.AreEqual(buyer.InstanceId, payerId);
                Assert.AreEqual(0, freightChargeCents);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void TransportVisualTogglesHorseAndCargoByLoadState()
        {
            GameObject root = new("Wagon Cargo Horse");
            GameObject cargo = new("Cargo");
            GameObject horse = new("Horse");
            try
            {
                cargo.transform.SetParent(root.transform, false);
                horse.transform.SetParent(root.transform, false);

                LogisticsTransportVisualController controller = root.AddComponent<LogisticsTransportVisualController>();
                controller.ApplyVisualState(LogisticsTransportVisualState.LoadedWagon);
                Assert.IsTrue(cargo.activeSelf);
                Assert.IsTrue(horse.activeSelf);

                controller.ApplyVisualState(LogisticsTransportVisualState.LightReturn);
                Assert.IsFalse(cargo.activeSelf);
                Assert.IsTrue(horse.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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
                    roadAccessCell = new GridCoord(4, 2),
                    frontageCells = 2,
                    depthCells = 2,
                    roadFrontageDirection = GridDirection.South
                }
            };
            plotsField.SetValue(townWorld, plots);
            return townWorld;
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

        private static void ActivateAllWorkers(BusinessInstanceState business)
        {
            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = business.RuntimeState.WorkerSlots[i];
                slot.Assign((100 + i).ToString(), $"Worker {i}", slot.WeeklyWageCents);
                slot.MarkPaidActive();
            }

            business.ResolveWeeklyBaselineThroughput();
            business.ResolveDailyBaselineService();
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

        private static (ShipmentHaulingMode haulingMode, LogisticsSupplierClass supplierClass, string carrierId, string payerId, int freightChargeCents)
            InvokeResolveCarrierAssignment(
                LogisticsRuntimeManager manager,
                BusinessInstanceState source,
                BusinessInstanceState destination,
                LogisticsCargoClass cargoClass,
                int units,
                LogisticsRoutePlan plan,
                ShipmentHaulingMode requestedHaulingMode,
                string requestedFreightPayerBusinessInstanceId)
        {
            MethodInfo method = typeof(LogisticsRuntimeManager).GetMethod("ResolveCarrierAssignment", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            object[] parameters =
            {
                source,
                destination,
                cargoClass,
                units,
                plan,
                requestedHaulingMode,
                requestedFreightPayerBusinessInstanceId,
                ShipmentHaulingMode.SourceDelivers,
                LogisticsSupplierClass.LocalBusiness,
                string.Empty,
                string.Empty,
                0
            };

            method.Invoke(manager, parameters);

            return (
                (ShipmentHaulingMode)parameters[7],
                (LogisticsSupplierClass)parameters[8],
                (string)parameters[9],
                (string)parameters[10],
                (int)parameters[11]);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Missing field {fieldName} on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
