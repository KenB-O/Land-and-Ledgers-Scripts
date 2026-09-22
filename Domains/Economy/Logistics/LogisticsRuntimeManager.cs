using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.MVP;
using LandLedgers.Pathing;
using LandLedgers.Persistence;
using LandLedgers.Time;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(264)]
    public sealed class LogisticsRuntimeManager : MonoBehaviour
    {
        private const int DefaultOffMapApproachCells = 24;
        private const float DefaultLoadingGameSeconds = 900f;
        private const float DefaultUnloadingGameSeconds = 900f;
        private const int BaseFreightJobChargeCents = 30;
        private const float ReadinessPriceFloorMultiplier = 0.9f;
        private const float ReadinessPriceCeilingMultiplier = 1.35f;

        [SerializeField] private TownWorldController townWorld;
        [SerializeField] private PathingManager pathingManager;
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private GeneralStoreRuntimeManager generalStoreRuntime;
        [SerializeField] private SharedBusinessRuntimeManager sharedBusinessRuntime;
        [SerializeField] private string lastTownLedgerSummary = "Logistics ledger: no shipments yet.";
        [SerializeField] private List<LogisticsShipmentState> shipments = new();

        private bool subscribedToTime;
        private LogisticsRoutePlanner routePlanner;

        public static LogisticsRuntimeManager Instance { get; private set; }
        public IReadOnlyList<LogisticsShipmentState> Shipments => shipments;
        public string LastTownLedgerSummary => string.IsNullOrWhiteSpace(lastTownLedgerSummary) ? "Logistics ledger: no shipments yet." : lastTownLedgerSummary;

        public static LogisticsRuntimeManager FindOrCreate()
        {
            LogisticsRuntimeManager existing = FindAnyObjectByType<LogisticsRuntimeManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject runtimeObject = new("Logistics Runtime");
            return runtimeObject.AddComponent<LogisticsRuntimeManager>();
        }

        public void Configure(
            TownWorldController newTownWorld,
            PathingManager newPathingManager,
            TimeManager newTimeManager,
            GeneralStoreRuntimeManager newGeneralStoreRuntime,
            SharedBusinessRuntimeManager newSharedBusinessRuntime)
        {
            townWorld = newTownWorld != null ? newTownWorld : townWorld;
            pathingManager = newPathingManager != null ? newPathingManager : pathingManager;
            timeManager = newTimeManager != null ? newTimeManager : timeManager;
            generalStoreRuntime = newGeneralStoreRuntime != null ? newGeneralStoreRuntime : generalStoreRuntime;
            sharedBusinessRuntime = newSharedBusinessRuntime != null ? newSharedBusinessRuntime : sharedBusinessRuntime;
            routePlanner = townWorld != null && pathingManager != null ? new LogisticsRoutePlanner(townWorld, pathingManager) : routePlanner;
            SubscribeToTime();
        }

        public LogisticsRuntimeSaveDto CaptureSaveDto()
        {
            LogisticsRuntimeSaveDto dto = new()
            {
                lastTownLedgerSummary = LastTownLedgerSummary
            };

            for (int i = 0; i < shipments.Count; i++)
            {
                LogisticsShipmentState shipment = shipments[i];
                if (shipment == null || string.IsNullOrWhiteSpace(shipment.ShipmentId))
                {
                    continue;
                }

                dto.shipments.Add(shipment.CaptureSaveDto());
            }

            return dto;
        }

        public void LoadFromSaveDto(LogisticsRuntimeSaveDto dto)
        {
            shipments.Clear();
            AutoWire();
            if (dto != null && dto.shipments != null)
            {
                for (int i = 0; i < dto.shipments.Count; i++)
                {
                    LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(dto.shipments[i]);
                    if (restored == null || string.IsNullOrWhiteSpace(restored.ShipmentId))
                    {
                        continue;
                    }

                    shipments.Add(restored);
                }
            }

            lastTownLedgerSummary = dto != null && !string.IsNullOrWhiteSpace(dto.lastTownLedgerSummary)
                ? dto.lastTownLedgerSummary
                : "Logistics ledger: no shipments yet.";
            RefreshTownLedgerSummary();
            SubscribeToTime();
        }

        public LogisticsShipmentState CreateLocalBusinessShipment(
            BusinessInstanceState source,
            string sourceCategoryId,
            BusinessInstanceState destination,
            string destinationCategoryId,
            int units,
            int sellerUnitRevenueCents,
            int buyerUnitCostCents,
            bool sourceCommittedAtSchedule = false,
            LogisticsShipmentDeliveryMode deliveryMode = LogisticsShipmentDeliveryMode.AddCategoryStock,
            string summaryLabel = null,
            ShipmentHaulingMode haulingMode = ShipmentHaulingMode.SourceDelivers,
            string freightPayerBusinessInstanceId = null,
            string transferAgreementId = null,
            LogisticsSupplierClass? supplierClassOverride = null)
        {
            if (source == null || destination == null || units <= 0)
            {
                return null;
            }

            if (!TryResolveBusinessRoadAccess(source, out GridCoord start)
                || !TryResolveBusinessRoadAccess(destination, out GridCoord end))
            {
                return CreateBlockedShipment(
                    LogisticsShipmentKind.LocalTradeTransfer,
                    ResolveCargoClass(sourceCategoryId, destinationCategoryId),
                    supplierClassOverride ?? LogisticsSupplierClass.LocalBusiness,
                    source != null ? source.InstanceId : string.Empty,
                    destination != null ? destination.InstanceId : string.Empty,
                    sourceCategoryId,
                    destinationCategoryId,
                    units,
                    "shipment endpoints are missing road access",
                    transferAgreementId);
            }

            LogisticsRoutePlan plan = PlanRoute(new LogisticsRouteRequest(start, end, label: summaryLabel ?? "local trade"), out string failureReason);
            if (plan == null)
            {
                return CreateBlockedShipment(
                    LogisticsShipmentKind.LocalTradeTransfer,
                    ResolveCargoClass(sourceCategoryId, destinationCategoryId),
                    supplierClassOverride ?? LogisticsSupplierClass.LocalBusiness,
                    source.InstanceId,
                    destination.InstanceId,
                    sourceCategoryId,
                    destinationCategoryId,
                    units,
                    failureReason,
                    transferAgreementId);
            }

            LogisticsCargoClass cargoClass = ResolveCargoClass(sourceCategoryId, destinationCategoryId);
            ResolveCarrierAssignment(
                source,
                destination,
                cargoClass,
                Mathf.Max(0, units),
                plan,
                haulingMode,
                freightPayerBusinessInstanceId,
                out ShipmentHaulingMode resolvedHaulingMode,
                out LogisticsSupplierClass supplierClass,
                out string carrierBusinessInstanceId,
                out string resolvedFreightPayerBusinessInstanceId,
                out int freightChargeCents);

            if (haulingMode == ShipmentHaulingMode.HiredFreight && string.IsNullOrWhiteSpace(carrierBusinessInstanceId))
            {
                return CreateBlockedShipment(
                    LogisticsShipmentKind.LocalTradeTransfer,
                    cargoClass,
                    LogisticsSupplierClass.HiredHauling,
                    source.InstanceId,
                    destination.InstanceId,
                    sourceCategoryId,
                    destinationCategoryId,
                    units,
                    "no livery/freight carrier available for hired hauling",
                    transferAgreementId);
            }

            LogisticsSupplierClass resolvedSupplierClass = supplierClassOverride ?? supplierClass;

            LogisticsShipmentState shipment = new()
            {
                shipmentId = BuildShipmentId("local", source.InstanceId, destination.InstanceId, destinationCategoryId),
                transferAgreementId = transferAgreementId ?? string.Empty,
                shipmentKind = LogisticsShipmentKind.LocalTradeTransfer,
                cargoClass = cargoClass,
                supplierClass = resolvedSupplierClass,
                sourceEndpointKind = source.BusinessType == BusinessType.GeneralStore ? LogisticsShipmentEndpointKind.GeneralStore : LogisticsShipmentEndpointKind.Business,
                destinationEndpointKind = destination.BusinessType == BusinessType.GeneralStore ? LogisticsShipmentEndpointKind.GeneralStore : LogisticsShipmentEndpointKind.Business,
                deliveryMode = deliveryMode,
                haulingMode = resolvedHaulingMode,
                sourceBusinessInstanceId = source.InstanceId,
                destinationBusinessInstanceId = destination.InstanceId,
                carrierBusinessInstanceId = carrierBusinessInstanceId,
                freightPayerBusinessInstanceId = resolvedFreightPayerBusinessInstanceId,
                sourceCategoryId = sourceCategoryId ?? string.Empty,
                destinationCategoryId = destinationCategoryId ?? string.Empty,
                plannedQuantityUnits = Mathf.Max(0, units),
                remainingQuantityUnits = Mathf.Max(0, units),
                sellerUnitRevenueCents = Mathf.Max(0, sellerUnitRevenueCents),
                buyerUnitCostCents = Mathf.Max(0, buyerUnitCostCents),
                freightChargeCents = freightChargeCents,
                sourceCommittedAtSchedule = sourceCommittedAtSchedule,
                loadApplied = sourceCommittedAtSchedule,
                loadingDurationGameSeconds = DefaultLoadingGameSeconds,
                transitDurationGameSeconds = Mathf.Max(60f, plan.TotalTravelGameSeconds),
                unloadingDurationGameSeconds = DefaultUnloadingGameSeconds,
                routePlan = plan,
                summaryLabel = string.IsNullOrWhiteSpace(summaryLabel)
                    ? $"{source.RuntimeDisplayName}->{destination.RuntimeDisplayName}"
                    : summaryLabel.Trim()
            };

            shipments.Add(shipment);
            RefreshTownLedgerSummary();
            return shipment;
        }

        public LogisticsShipmentState CreateOffMapInboundShipment(
            BusinessInstanceState destination,
            string destinationCategoryId,
            int units,
            int buyerUnitCostCents,
            LogisticsShipmentDeliveryMode deliveryMode,
            string summaryLabel,
            int extraOffMapCells = DefaultOffMapApproachCells)
        {
            if (destination == null || units <= 0)
            {
                return null;
            }

            if (!TryResolveBusinessRoadAccess(destination, out GridCoord destinationRoad))
            {
                return CreateBlockedShipment(
                    LogisticsShipmentKind.OffMapInboundPurchase,
                    ResolveCargoClass(destinationCategoryId, destinationCategoryId),
                    LogisticsSupplierClass.OffMapSupplier,
                    string.Empty,
                    destination.InstanceId,
                    string.Empty,
                    destinationCategoryId,
                    units,
                    "destination has no valid road access");
            }

            LogisticsRoutePlan plan = PlanRoute(
                new LogisticsRouteRequest(
                    destinationRoad,
                    destinationRoad,
                    LogisticsPathEndpointKind.MapEdgeRoad,
                    LogisticsPathEndpointKind.GridCoord,
                    extraOffMapCells,
                    summaryLabel),
                out string failureReason);
            if (plan == null)
            {
                return CreateBlockedShipment(
                    LogisticsShipmentKind.OffMapInboundPurchase,
                    ResolveCargoClass(destinationCategoryId, destinationCategoryId),
                    LogisticsSupplierClass.OffMapSupplier,
                    string.Empty,
                    destination.InstanceId,
                    string.Empty,
                    destinationCategoryId,
                    units,
                    failureReason);
            }

            LogisticsShipmentState shipment = new()
            {
                shipmentId = BuildShipmentId("offmap_in", string.Empty, destination.InstanceId, destinationCategoryId),
                shipmentKind = LogisticsShipmentKind.OffMapInboundPurchase,
                cargoClass = ResolveCargoClass(destinationCategoryId, destinationCategoryId),
                supplierClass = LogisticsSupplierClass.OffMapSupplier,
                sourceEndpointKind = LogisticsShipmentEndpointKind.OffMap,
                destinationEndpointKind = destination.BusinessType == BusinessType.GeneralStore ? LogisticsShipmentEndpointKind.GeneralStore : LogisticsShipmentEndpointKind.Business,
                deliveryMode = deliveryMode,
                destinationBusinessInstanceId = destination.InstanceId,
                destinationCategoryId = destinationCategoryId ?? string.Empty,
                plannedQuantityUnits = Mathf.Max(0, units),
                remainingQuantityUnits = Mathf.Max(0, units),
                buyerUnitCostCents = Mathf.Max(0, buyerUnitCostCents),
                loadingDurationGameSeconds = DefaultLoadingGameSeconds,
                transitDurationGameSeconds = Mathf.Max(60f, plan.TotalTravelGameSeconds),
                unloadingDurationGameSeconds = DefaultUnloadingGameSeconds,
                routePlan = plan,
                summaryLabel = string.IsNullOrWhiteSpace(summaryLabel)
                    ? $"Off-map->{destination.RuntimeDisplayName}"
                    : summaryLabel.Trim()
            };

            shipments.Add(shipment);
            RefreshTownLedgerSummary();
            return shipment;
        }

        public LogisticsShipmentState CreateOffMapOutboundShipment(
            BusinessInstanceState source,
            string sourceCategoryId,
            int units,
            int sellerUnitRevenueCents,
            string summaryLabel,
            bool sourceCommittedAtSchedule = false,
            int extraOffMapCells = DefaultOffMapApproachCells)
        {
            if (source == null || units <= 0)
            {
                return null;
            }

            if (!TryResolveBusinessRoadAccess(source, out GridCoord sourceRoad))
            {
                return CreateBlockedShipment(
                    LogisticsShipmentKind.OffMapOutboundSale,
                    ResolveCargoClass(sourceCategoryId, sourceCategoryId),
                    LogisticsSupplierClass.OffMapSupplier,
                    source.InstanceId,
                    string.Empty,
                    sourceCategoryId,
                    string.Empty,
                    units,
                    "source has no valid road access");
            }

            LogisticsRoutePlan plan = PlanRoute(
                new LogisticsRouteRequest(
                    sourceRoad,
                    sourceRoad,
                    LogisticsPathEndpointKind.GridCoord,
                    LogisticsPathEndpointKind.MapEdgeRoad,
                    extraOffMapCells,
                    summaryLabel),
                out string failureReason);
            if (plan == null)
            {
                return CreateBlockedShipment(
                    LogisticsShipmentKind.OffMapOutboundSale,
                    ResolveCargoClass(sourceCategoryId, sourceCategoryId),
                    LogisticsSupplierClass.OffMapSupplier,
                    source.InstanceId,
                    string.Empty,
                    sourceCategoryId,
                    string.Empty,
                    units,
                    failureReason);
            }

            LogisticsShipmentState shipment = new()
            {
                shipmentId = BuildShipmentId("offmap_out", source.InstanceId, string.Empty, sourceCategoryId),
                shipmentKind = LogisticsShipmentKind.OffMapOutboundSale,
                cargoClass = ResolveCargoClass(sourceCategoryId, sourceCategoryId),
                supplierClass = LogisticsSupplierClass.OffMapSupplier,
                sourceEndpointKind = source.BusinessType == BusinessType.GeneralStore ? LogisticsShipmentEndpointKind.GeneralStore : LogisticsShipmentEndpointKind.Business,
                destinationEndpointKind = LogisticsShipmentEndpointKind.OffMap,
                sourceBusinessInstanceId = source.InstanceId,
                sourceCategoryId = sourceCategoryId ?? string.Empty,
                plannedQuantityUnits = Mathf.Max(0, units),
                remainingQuantityUnits = Mathf.Max(0, units),
                sellerUnitRevenueCents = Mathf.Max(0, sellerUnitRevenueCents),
                sourceCommittedAtSchedule = sourceCommittedAtSchedule,
                loadApplied = sourceCommittedAtSchedule,
                loadingDurationGameSeconds = DefaultLoadingGameSeconds,
                transitDurationGameSeconds = Mathf.Max(60f, plan.TotalTravelGameSeconds),
                unloadingDurationGameSeconds = DefaultUnloadingGameSeconds,
                routePlan = plan,
                summaryLabel = string.IsNullOrWhiteSpace(summaryLabel)
                    ? $"{source.RuntimeDisplayName}->Off-map"
                    : summaryLabel.Trim()
            };

            shipments.Add(shipment);
            RefreshTownLedgerSummary();
            return shipment;
        }

        public int ScheduleRecurringLocalOrderShipment(
            LocalRecurringOrderFulfillmentRequest request,
            int units,
            int sellerUnitRevenueCents,
            int buyerUnitCostCents)
        {
            if (request.Template == null || request.Seller == null || request.Buyer == null || units <= 0)
            {
                return 0;
            }

            LogisticsShipmentDeliveryMode deliveryMode = request.Buyer.BusinessType == BusinessType.GeneralStore
                ? LogisticsShipmentDeliveryMode.GeneralStoreLocalSupply
                : LogisticsShipmentDeliveryMode.AddCategoryStock;
            ShipmentHaulingMode haulingMode = ResolveRecurringOrderHaulingMode(request.Template.HaulingResponsibility);
            string freightPayer = ResolveRecurringOrderFreightPayer(request);
            LogisticsShipmentState shipment = CreateLocalBusinessShipment(
                request.Seller,
                request.Template.SellerCategoryId,
                request.Buyer,
                request.Template.BuyerCategoryId,
                units,
                sellerUnitRevenueCents,
                buyerUnitCostCents,
                true,
                deliveryMode,
                BuildRecurringOrderShipmentLabel(request),
                haulingMode,
                freightPayer);

            return IsAcceptedScheduledShipment(shipment) ? Mathf.Max(0, units) : 0;
        }

        public int ScheduleBusinessTransferAgreementShipment(
            BusinessTransferAgreementState agreement,
            BusinessInstanceState source,
            BusinessInstanceState destination,
            int units,
            int sellerUnitRevenueCents,
            int buyerUnitCostCents,
            out string shipmentId,
            bool sourceCommittedAtSchedule = false)
        {
            shipmentId = string.Empty;
            if (agreement == null || source == null || destination == null || units <= 0)
            {
                return 0;
            }

            LogisticsShipmentState shipment = CreateLocalBusinessShipment(
                source,
                agreement.SourceCategoryId,
                destination,
                agreement.DestinationCategoryId,
                units,
                sellerUnitRevenueCents,
                buyerUnitCostCents,
                sourceCommittedAtSchedule,
                LogisticsShipmentDeliveryMode.AddCategoryStock,
                BuildBusinessTransferShipmentLabel(agreement, source, destination),
                ResolveBusinessTransferHaulingMode(agreement.HaulingResponsibility),
                ResolveBusinessTransferFreightPayer(agreement, source, destination),
                agreement.AgreementId);

            shipmentId = shipment != null ? shipment.ShipmentId : string.Empty;
            return IsAcceptedScheduledShipment(shipment) ? Mathf.Max(0, units) : 0;
        }

        public LogisticsTownPressureSnapshot CaptureTownPressureSnapshot()
        {
            AutoWire();
            int active = 0;
            int local = 0;
            int offMap = 0;
            int delayed = 0;
            int failed = 0;
            int blocked = 0;
            int hiredFreight = 0;
            int criticalBlocked = 0;

            for (int i = 0; i < shipments.Count; i++)
            {
                LogisticsShipmentState shipment = shipments[i];
                if (shipment == null)
                {
                    continue;
                }

                if (shipment.shipmentKind == LogisticsShipmentKind.LocalTradeTransfer)
                {
                    local++;
                }
                else
                {
                    offMap++;
                }

                if (shipment.Status != LogisticsShipmentStatus.Completed)
                {
                    active++;
                }

                if (shipment.Status == LogisticsShipmentStatus.Delayed)
                {
                    delayed++;
                }

                if (shipment.Status == LogisticsShipmentStatus.Failed)
                {
                    failed++;
                }

                if (!string.IsNullOrWhiteSpace(shipment.BlockedReason))
                {
                    blocked++;
                    if (IsCriticalSupplyShipment(shipment))
                    {
                        criticalBlocked++;
                    }
                }

                if (shipment.HaulingMode == ShipmentHaulingMode.HiredFreight && IsActiveShipmentForCapacity(shipment))
                {
                    hiredFreight++;
                }
            }

            LiveryFreightReadinessSnapshot livery = CaptureLiveryFreightReadinessSnapshot();
            float pressure = Mathf.Max(
                NormalizeForPressure(delayed, 4),
                NormalizeForPressure(failed, 2),
                NormalizeForPressure(blocked, 3),
                NormalizeForPressure(criticalBlocked, 1),
                hiredFreight > 0 && livery.ActiveCarrierCount <= 0 ? 0.75f : 0f,
                livery.UnassignedHiredFreightShipmentCount > 0 ? 0.8f : 0f,
                livery.OverloadedCarrierCount > 0 ? 0.65f : 0f,
                livery.LoadRatio01 > 1f ? Mathf.Clamp01(0.5f + (livery.LoadRatio01 - 1f) * 0.35f) : 0f);

            return new LogisticsTownPressureSnapshot(
                active,
                local,
                offMap,
                delayed,
                failed,
                blocked,
                hiredFreight,
                criticalBlocked,
                livery.TotalCarrierCount,
                livery.ActiveCarrierCount,
                livery.EffectiveDailyCapacity,
                livery.UnassignedHiredFreightShipmentCount,
                livery.OverloadedCarrierCount,
                livery.LoadRatio01,
                livery.AverageReadiness01,
                pressure);
        }

        public LiveryFreightReadinessSnapshot CaptureLiveryFreightReadinessSnapshot()
        {
            AutoWire();
            int total = 0;
            int active = 0;
            int unavailable = 0;
            int capacity = 0;
            int overloaded = 0;
            float readinessSum = 0f;

            if (sharedBusinessRuntime != null)
            {
                for (int i = 0; i < sharedBusinessRuntime.Businesses.Count; i++)
                {
                    BusinessInstanceState business = sharedBusinessRuntime.Businesses[i];
                    if (business == null || business.BusinessType != BusinessType.LiveryFreight)
                    {
                        continue;
                    }

                    total++;
                    float readiness = CalculateHiredFreightReadiness01(business);
                    int businessCapacity = CalculateEffectiveHiredFreightCapacity(business, readiness);
                    bool usable = CanServeAsHiredCarrier(business);
                    if (usable)
                    {
                        active++;
                        capacity += businessCapacity;
                        readinessSum += readiness;
                        if (CountActiveHiredFreightTripsForCarrier(business.InstanceId) > businessCapacity)
                        {
                            overloaded++;
                        }
                    }
                    else
                    {
                        unavailable++;
                    }
                }
            }

            int hired = CountActiveHiredFreightShipments();
            int unassigned = CountUnassignedActiveHiredFreightShipments();
            float avgReadiness = active > 0 ? Mathf.Clamp01(readinessSum / active) : 0f;
            float loadRatio = capacity > 0 ? Mathf.Max(0f, hired / (float)capacity) : hired > 0 ? 2f : 0f;
            return new LiveryFreightReadinessSnapshot(total, active, unavailable, capacity, hired, unassigned, overloaded, avgReadiness, loadRatio);
        }

        public string BuildBusinessLogisticsSummary(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId))
            {
                return string.Empty;
            }

            int active = 0;
            int delayed = 0;
            int blocked = 0;
            LogisticsShipmentState next = null;
            for (int i = 0; i < shipments.Count; i++)
            {
                LogisticsShipmentState shipment = shipments[i];
                if (shipment == null || !TouchesBusiness(shipment, businessInstanceId))
                {
                    continue;
                }

                if (shipment.Status == LogisticsShipmentStatus.Completed)
                {
                    continue;
                }

                active++;
                if (shipment.Status == LogisticsShipmentStatus.Delayed)
                {
                    delayed++;
                }

                if (!string.IsNullOrWhiteSpace(shipment.BlockedReason))
                {
                    blocked++;
                }

                if (next == null || shipment.EstimatedRemainingGameSeconds < next.EstimatedRemainingGameSeconds)
                {
                    next = shipment;
                }
            }

            if (active <= 0)
            {
                return "Logistics: no active shipments.";
            }

            string eta = next != null ? FormatGameTime(next.EstimatedRemainingGameSeconds) : "n/a";
            string blockedReason = next != null && !string.IsNullOrWhiteSpace(next.BlockedReason)
                ? $" | next issue {next.BlockedReason}"
                : string.Empty;
            return $"Logistics: {active} active | delayed {delayed} | blocked {blocked} | next ETA {eta}{blockedReason}";
        }

        public string BuildTownLogisticsLedgerSummary()
        {
            return LastTownLedgerSummary;
        }

        public bool TrySampleVisibleShipment(LogisticsShipmentState shipment, out Vector3 worldPosition, out LogisticsTransportVisualState visualState)
        {
            worldPosition = Vector3.zero;
            visualState = LogisticsTransportVisualState.LoadedWagon;
            if (shipment == null || townWorld == null || townWorld.Grid == null || shipment.RoutePlan == null || shipment.RoutePlan.VisiblePath == null || shipment.RoutePlan.VisiblePath.Count <= 0)
            {
                return false;
            }

            IReadOnlyList<GridCoord> path = shipment.RoutePlan.VisiblePath;
            float progress = shipment.ResolveVisiblePathProgress01();
            int maxIndex = Mathf.Max(0, path.Count - 1);
            float scaled = progress * maxIndex;
            int leftIndex = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, maxIndex);
            int rightIndex = Mathf.Clamp(leftIndex + 1, 0, maxIndex);
            float lerpT = Mathf.Clamp01(scaled - leftIndex);
            Vector3 left = townWorld.Grid.CoordToWorldCenter(path[leftIndex], 0.08f);
            Vector3 right = townWorld.Grid.CoordToWorldCenter(path[rightIndex], 0.08f);
            worldPosition = Vector3.Lerp(left, right, lerpT);
            visualState = shipment.Status == LogisticsShipmentStatus.Unloading || shipment.Status == LogisticsShipmentStatus.Completed
                ? LogisticsTransportVisualState.LightReturn
                : LogisticsTransportVisualState.LoadedWagon;
            return true;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            AutoWire();
            SubscribeToTime();
        }

        private void OnEnable()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            AutoWire();
            SubscribeToTime();
        }

        private void OnDisable()
        {
            UnsubscribeFromTime();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void AutoWire()
        {
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            pathingManager ??= FindAnyObjectByType<PathingManager>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            generalStoreRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            routePlanner = townWorld != null && pathingManager != null ? new LogisticsRoutePlanner(townWorld, pathingManager) : routePlanner;
        }

        private void SubscribeToTime()
        {
            if (subscribedToTime || timeManager == null)
            {
                return;
            }

            timeManager.ShortTick += OnShortTick;
            subscribedToTime = true;
        }

        private void UnsubscribeFromTime()
        {
            if (!subscribedToTime || timeManager == null)
            {
                subscribedToTime = false;
                return;
            }

            timeManager.ShortTick -= OnShortTick;
            subscribedToTime = false;
        }

        private void OnShortTick(SimulationTickContext context)
        {
            AdvanceShipments(context.TickIntervalGameSeconds);
        }

        private void AdvanceShipments(float gameSeconds)
        {
            if (shipments.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < shipments.Count; i++)
            {
                LogisticsShipmentState shipment = shipments[i];
                if (shipment == null)
                {
                    continue;
                }

                LogisticsShipmentStatus previousStatus = shipment.Status;
                shipment.AdvanceGameSeconds(gameSeconds);

                if (!shipment.loadApplied && shipment.Status != LogisticsShipmentStatus.Planned)
                {
                    if (!TryApplyLoad(shipment))
                    {
                        continue;
                    }
                }

                if (!shipment.deliveryApplied
                    && (shipment.Status == LogisticsShipmentStatus.Unloading
                        || shipment.Status == LogisticsShipmentStatus.Completed
                        || shipment.Status == LogisticsShipmentStatus.Partial))
                {
                    TryApplyDelivery(shipment);
                }

                if (previousStatus != shipment.Status)
                {
                    RefreshTownLedgerSummary();
                }
            }
        }

        private bool TryApplyLoad(LogisticsShipmentState shipment)
        {
            if (shipment == null || shipment.loadApplied || shipment.state == LogisticsShipmentStatus.Failed || shipment.remainingQuantityUnits <= 0)
            {
                return false;
            }

            if (shipment.sourceCommittedAtSchedule || shipment.shipmentKind == LogisticsShipmentKind.OffMapInboundPurchase)
            {
                shipment.loadApplied = true;
                return true;
            }

            BusinessInstanceState source = FindBusinessByInstanceId(shipment.sourceBusinessInstanceId);
            if (source == null || source.RuntimeState == null)
            {
                shipment.state = LogisticsShipmentStatus.Failed;
                shipment.blockedReason = "source business unavailable";
                return false;
            }

            if (!source.RuntimeState.TryConsumeCategoryStockUnits(shipment.sourceCategoryId, shipment.remainingQuantityUnits, out int consumed)
                || consumed <= 0)
            {
                shipment.state = LogisticsShipmentStatus.Failed;
                shipment.blockedReason = $"source stock unavailable: {shipment.sourceCategoryId}";
                return false;
            }

            shipment.remainingQuantityUnits = consumed;
            shipment.loadApplied = true;
            return true;
        }

        private void TryApplyDelivery(LogisticsShipmentState shipment)
        {
            if (shipment == null || shipment.deliveryApplied || shipment.state == LogisticsShipmentStatus.Failed)
            {
                return;
            }

            int deliveredUnits = shipment.remainingQuantityUnits;
            if (deliveredUnits <= 0)
            {
                if (shipment.state == LogisticsShipmentStatus.Unloading)
                {
                    shipment.deliveryApplied = true;
                    shipment.state = LogisticsShipmentStatus.Partial;
                }
                return;
            }

            switch (shipment.deliveryMode)
            {
                case LogisticsShipmentDeliveryMode.GeneralStoreLocalSupply:
                {
                    if (generalStoreRuntime == null || !generalStoreRuntime.InitializeIfNeeded())
                    {
                        shipment.state = LogisticsShipmentStatus.Failed;
                        shipment.blockedReason = "general store runtime unavailable";
                        return;
                    }

                    int accepted = generalStoreRuntime.ReceiveLocalSupply(
                        shipment.destinationCategoryId,
                        deliveredUnits,
                        shipment.buyerUnitCostCents,
                        ResolveSummaryLabel(shipment));
                    if (accepted <= 0)
                    {
                        shipment.state = LogisticsShipmentStatus.Delayed;
                        shipment.blockedReason = "general store could not receive shipment";
                        return;
                    }

                    CreditSellerRevenue(shipment, accepted);
                    shipment.remainingQuantityUnits = Mathf.Max(0, deliveredUnits - accepted);
                    shipment.deliveryApplied = accepted >= deliveredUnits;
                    shipment.state = shipment.remainingQuantityUnits > 0 ? LogisticsShipmentStatus.Partial : LogisticsShipmentStatus.Completed;
                    return;
                }

                case LogisticsShipmentDeliveryMode.ReceivePendingReorder:
                {
                    if (generalStoreRuntime != null
                        && string.Equals(generalStoreRuntime.CurrentBusiness != null ? generalStoreRuntime.CurrentBusiness.InstanceId : string.Empty, shipment.destinationBusinessInstanceId, StringComparison.OrdinalIgnoreCase))
                    {
                        int accepted = generalStoreRuntime.ReceiveOffMapReorderShipment(
                            shipment.destinationCategoryId,
                            deliveredUnits,
                            shipment.buyerUnitCostCents,
                            ResolveSummaryLabel(shipment));
                        shipment.remainingQuantityUnits = Mathf.Max(0, deliveredUnits - accepted);
                        shipment.deliveryApplied = accepted >= deliveredUnits;
                        shipment.state = shipment.remainingQuantityUnits > 0 ? LogisticsShipmentStatus.Partial : LogisticsShipmentStatus.Completed;
                        if (accepted <= 0)
                        {
                            shipment.blockedReason = "general store reorder intake failed";
                            shipment.state = LogisticsShipmentStatus.Delayed;
                            NotifyTransferAgreementDeliveryBlocked(shipment, shipment.blockedReason);
                        }

                        return;
                    }

                    goto case LogisticsShipmentDeliveryMode.AddCategoryStock;
                }

                case LogisticsShipmentDeliveryMode.AddCategoryStock:
                default:
                {
                    if (shipment.shipmentKind == LogisticsShipmentKind.OffMapOutboundSale)
                    {
                        CreditSellerRevenue(shipment, deliveredUnits);
                        shipment.deliveryApplied = true;
                        shipment.state = LogisticsShipmentStatus.Completed;
                        return;
                    }

                    BusinessInstanceState destination = FindBusinessByInstanceId(shipment.destinationBusinessInstanceId);
                    if (destination == null || destination.RuntimeState == null)
                    {
                        shipment.state = LogisticsShipmentStatus.Failed;
                        shipment.blockedReason = "destination business unavailable";
                        NotifyTransferAgreementDeliveryBlocked(shipment, shipment.blockedReason);
                        return;
                    }

                    if (!destination.RuntimeState.CanReceiveShipment(shipment.destinationCategoryId, deliveredUnits, out string blockedReason))
                    {
                        shipment.state = LogisticsShipmentStatus.Delayed;
                        shipment.blockedReason = blockedReason;
                        NotifyTransferAgreementDeliveryBlocked(shipment, blockedReason);
                        return;
                    }

                    int accepted = destination.RuntimeState.ApplyDeliveredShipment(shipment.destinationCategoryId, deliveredUnits);
                    if (accepted <= 0)
                    {
                        shipment.state = LogisticsShipmentStatus.Delayed;
                        shipment.blockedReason = "destination refused shipment";
                        NotifyTransferAgreementDeliveryBlocked(shipment, shipment.blockedReason);
                        return;
                    }

                    if (shipment.buyerUnitCostCents > 0)
                    {
                        int buyerCost = accepted * shipment.buyerUnitCostCents;
                        destination.RuntimeState.AddWeeklyLocalTransferCost(
                            buyerCost,
                            $"{ResolveSummaryLabel(shipment)} delivered {accepted} {shipment.destinationCategoryId} for {FormatMoney(buyerCost)}");
                    }

                    CreditSellerRevenue(shipment, accepted);
                    BookFreightCharge(shipment, accepted, destination);
                    shipment.remainingQuantityUnits = Mathf.Max(0, deliveredUnits - accepted);
                    shipment.deliveryApplied = accepted >= deliveredUnits;
                    shipment.state = shipment.remainingQuantityUnits > 0 ? LogisticsShipmentStatus.Partial : LogisticsShipmentStatus.Completed;
                    destination.RefreshCapacityState();
                    NotifyTransferAgreementDelivered(shipment, accepted);
                    return;
                }
            }
        }

        private void CreditSellerRevenue(LogisticsShipmentState shipment, int acceptedUnits)
        {
            if (shipment == null || acceptedUnits <= 0 || shipment.sellerUnitRevenueCents <= 0)
            {
                return;
            }

            BusinessInstanceState source = FindBusinessByInstanceId(shipment.sourceBusinessInstanceId);
            if (source == null || source.RuntimeState == null)
            {
                return;
            }

            int revenue = acceptedUnits * shipment.sellerUnitRevenueCents;
            source.RuntimeState.AddWeeklyLocalTransferRevenue(
                revenue,
                $"{ResolveSummaryLabel(shipment)} delivered {acceptedUnits} {shipment.sourceCategoryId} for {FormatMoney(revenue)}");
            source.RefreshCapacityState();
        }

        private void BookFreightCharge(LogisticsShipmentState shipment, int acceptedUnits, BusinessInstanceState fallbackDestination)
        {
            if (shipment == null
                || acceptedUnits <= 0
                || shipment.HaulingMode != ShipmentHaulingMode.HiredFreight)
            {
                return;
            }

            int freightCharge = Mathf.Max(0, shipment.FreightChargeCents);
            if (freightCharge <= 0)
            {
                return;
            }

            if (shipment.PlannedQuantityUnits > 0 && acceptedUnits < shipment.PlannedQuantityUnits)
            {
                freightCharge = Mathf.RoundToInt(freightCharge * (acceptedUnits / (float)shipment.PlannedQuantityUnits));
            }

            BusinessInstanceState carrier = FindBusinessByInstanceId(shipment.CarrierBusinessInstanceId);
            if (carrier != null
                && carrier.BusinessType == BusinessType.LiveryFreight
                && carrier.RuntimeState != null)
            {
                carrier.RuntimeState.AddWeeklyLocalTransferRevenue(
                    freightCharge,
                    $"{ResolveSummaryLabel(shipment)} freight for {FormatMoney(freightCharge)}");
                carrier.RefreshCapacityState();
            }

            string payerBusinessId = !string.IsNullOrWhiteSpace(shipment.FreightPayerBusinessInstanceId)
                ? shipment.FreightPayerBusinessInstanceId
                : fallbackDestination != null
                    ? fallbackDestination.InstanceId
                    : shipment.destinationBusinessInstanceId;
            BusinessInstanceState payer = FindBusinessByInstanceId(payerBusinessId);
            if (payer == null || payer.RuntimeState == null)
            {
                return;
            }

            payer.RuntimeState.AddWeeklyLocalTransferCost(
                freightCharge,
                $"{ResolveSummaryLabel(shipment)} freight charged {FormatMoney(freightCharge)}");
            payer.RefreshCapacityState();
        }

        private LogisticsShipmentState CreateBlockedShipment(
            LogisticsShipmentKind kind,
            LogisticsCargoClass cargoClass,
            LogisticsSupplierClass supplierClass,
            string sourceBusinessId,
            string destinationBusinessId,
            string sourceCategoryId,
            string destinationCategoryId,
            int units,
            string blockedReason,
            string transferAgreementId = null)
        {
            // MR-P001: Enforce physical conservation invariant.
            // A shipment blocked before loading occurred owns ZERO physical cargo.
            // plannedQuantityUnits is preserved for diagnostic/retry context only.
            // remainingQuantityUnits = 0: no cargo exists to deliver or settle.
            // sourceCommittedAtSchedule = false: no source stock was removed.
            // loadApplied = false: loading never occurred.
            // state = Failed: AdvanceGameSeconds exits immediately for Failed
            // and TryApplyDelivery only fires for Unloading/Completed/Partial,
            // so this shipment can never advance into delivery or settlement.
            LogisticsShipmentState shipment = new()
            {
                shipmentId = BuildShipmentId("blocked", sourceBusinessId, destinationBusinessId, destinationCategoryId),
                transferAgreementId = transferAgreementId ?? string.Empty,
                shipmentKind = kind,
                cargoClass = cargoClass,
                supplierClass = supplierClass,
                haulingMode = supplierClass == LogisticsSupplierClass.HiredHauling ? ShipmentHaulingMode.HiredFreight : ShipmentHaulingMode.SourceDelivers,
                sourceEndpointKind = string.IsNullOrWhiteSpace(sourceBusinessId) ? LogisticsShipmentEndpointKind.OffMap : LogisticsShipmentEndpointKind.Business,
                destinationEndpointKind = string.IsNullOrWhiteSpace(destinationBusinessId) ? LogisticsShipmentEndpointKind.OffMap : LogisticsShipmentEndpointKind.Business,
                sourceBusinessInstanceId = sourceBusinessId ?? string.Empty,
                destinationBusinessInstanceId = destinationBusinessId ?? string.Empty,
                sourceCategoryId = sourceCategoryId ?? string.Empty,
                destinationCategoryId = destinationCategoryId ?? string.Empty,
                plannedQuantityUnits = Mathf.Max(0, units),
                remainingQuantityUnits = 0,
                sourceCommittedAtSchedule = false,
                loadApplied = false,
                deliveryApplied = false,
                state = LogisticsShipmentStatus.Failed,
                blockedReason = string.IsNullOrWhiteSpace(blockedReason) ? "shipment blocked" : blockedReason.Trim()
            };
            shipments.Add(shipment);
            RefreshTownLedgerSummary();
            NotifyTransferAgreementDeliveryBlocked(shipment, shipment.blockedReason);
            return shipment;
        }

        private LogisticsRoutePlan PlanRoute(LogisticsRouteRequest request, out string failureReason)
        {
            failureReason = string.Empty;
            AutoWire();
            if (routePlanner == null)
            {
                failureReason = "route planner unavailable";
                return null;
            }

            if (!routePlanner.TryPlanRoute(request, out LogisticsRoutePlan plan))
            {
                failureReason = plan != null ? plan.FailureReason : "route planning failed";
                return null;
            }

            return plan;
        }

        private void ResolveCarrierAssignment(
            BusinessInstanceState source,
            BusinessInstanceState destination,
            LogisticsCargoClass cargoClass,
            int units,
            LogisticsRoutePlan plan,
            ShipmentHaulingMode requestedHaulingMode,
            string requestedFreightPayerBusinessInstanceId,
            out ShipmentHaulingMode resolvedHaulingMode,
            out LogisticsSupplierClass supplierClass,
            out string carrierBusinessInstanceId,
            out string freightPayerBusinessInstanceId,
            out int freightChargeCents)
        {
            resolvedHaulingMode = requestedHaulingMode;
            supplierClass = LogisticsSupplierClass.LocalBusiness;
            carrierBusinessInstanceId = string.Empty;
            freightPayerBusinessInstanceId = string.Empty;
            freightChargeCents = 0;

            if (requestedHaulingMode != ShipmentHaulingMode.HiredFreight)
            {
                return;
            }

            BusinessInstanceState carrier = TryFindBestHiredFreightCarrier(source, destination, plan, out float readiness01);
            if (carrier == null)
            {
                resolvedHaulingMode = ShipmentHaulingMode.HiredFreight;
                supplierClass = LogisticsSupplierClass.HiredHauling;
                freightPayerBusinessInstanceId = string.IsNullOrWhiteSpace(requestedFreightPayerBusinessInstanceId)
                    ? destination != null ? destination.InstanceId : string.Empty
                    : requestedFreightPayerBusinessInstanceId.Trim();
                return;
            }

            supplierClass = LogisticsSupplierClass.HiredHauling;
            carrierBusinessInstanceId = carrier.InstanceId;
            freightPayerBusinessInstanceId = string.IsNullOrWhiteSpace(requestedFreightPayerBusinessInstanceId)
                ? destination != null ? destination.InstanceId : string.Empty
                : requestedFreightPayerBusinessInstanceId.Trim();
            freightChargeCents = CalculateFreightChargeCents(plan, cargoClass, units, readiness01);
        }

        private BusinessInstanceState TryFindBestHiredFreightCarrier(
            BusinessInstanceState source,
            BusinessInstanceState destination,
            LogisticsRoutePlan plan,
            out float readiness01)
        {
            readiness01 = 0f;
            if (sharedBusinessRuntime == null)
            {
                return null;
            }

            BusinessInstanceState best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < sharedBusinessRuntime.Businesses.Count; i++)
            {
                BusinessInstanceState business = sharedBusinessRuntime.Businesses[i];
                if (!CanServeAsHiredCarrier(business))
                {
                    continue;
                }

                float businessReadiness = CalculateHiredFreightReadiness01(business);
                int serviceCapacity = Mathf.Max(1, CalculateEffectiveHiredFreightCapacity(business, businessReadiness));
                int activeTrips = CountActiveHiredFreightTripsForCarrier(business.InstanceId);
                int spareCapacity = Mathf.Max(0, serviceCapacity - activeTrips);
                float overloadPenalty = activeTrips > serviceCapacity ? (activeTrips - serviceCapacity) * 18f : 0f;
                float routePressure = plan != null ? Mathf.Clamp01(plan.TotalTravelCells / 64f) : 0.5f;
                float score = businessReadiness * 100f + spareCapacity * 8f - routePressure * 10f - overloadPenalty;
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                best = business;
                readiness01 = businessReadiness;
            }

            return best;
        }

        private bool CanServeAsHiredCarrier(BusinessInstanceState business)
        {
            return business != null
                && business.BusinessType == BusinessType.LiveryFreight
                && business.RuntimeState != null
                && business.BaselineDailyServiceCapacity > 0
                && business.OperatingEfficiency01 > 0f
                && business.RuntimeState.ActiveRequiredWorkerCount >= business.RuntimeState.RequiredWorkerCount
                && (sharedBusinessRuntime == null || sharedBusinessRuntime.IsBusinessAssignmentValid(business));
        }

        private static int CalculateFreightChargeCents(
            LogisticsRoutePlan plan,
            LogisticsCargoClass cargoClass,
            int units,
            float readiness01)
        {
            int quantityUnits = Mathf.Max(1, units);
            int distanceCharge = plan != null ? Mathf.Max(0, Mathf.RoundToInt(plan.TotalTravelCells * 1.25f)) : 0;
            int timeCharge = plan != null ? Mathf.Max(0, Mathf.RoundToInt(plan.TotalTravelGameSeconds / 1800f) * 3) : 0;
            float cargoMultiplier = cargoClass switch
            {
                LogisticsCargoClass.Perishables => 1.18f,
                LogisticsCargoClass.LiveAnimals => 1.25f,
                LogisticsCargoClass.BulkMaterials => 1.12f,
                _ => 1f
            };
            float readinessMultiplier = Mathf.Lerp(ReadinessPriceCeilingMultiplier, ReadinessPriceFloorMultiplier, Mathf.Clamp01(readiness01));
            int baseCharge = BaseFreightJobChargeCents + distanceCharge + timeCharge + quantityUnits * 6;
            return Mathf.Max(0, Mathf.RoundToInt(baseCharge * cargoMultiplier * readinessMultiplier));
        }

        private bool TryResolveBusinessRoadAccess(BusinessInstanceState business, out GridCoord coord)
        {
            coord = default;
            if (business == null || townWorld == null || townWorld.Grid == null)
            {
                return false;
            }

            if (generalStoreRuntime != null
                && generalStoreRuntime.CurrentBusiness != null
                && string.Equals(generalStoreRuntime.CurrentBusiness.InstanceId, business.InstanceId, StringComparison.OrdinalIgnoreCase)
                && townWorld.TryGetBuildingById(generalStoreRuntime.StoreBuildingId, out PlacedBuilding storeBuilding))
            {
                TownPlot storePlot = FindPlot(storeBuilding.plotId);
                if (storePlot != null)
                {
                    coord = storePlot.roadAccessCell;
                    return townWorld.Grid.IsInBounds(coord);
                }
            }

            if (business.AssignedBuildingId < 0 || !townWorld.TryGetBuildingById(business.AssignedBuildingId, out PlacedBuilding building))
            {
                return false;
            }

            TownPlot plot = FindPlot(building.plotId);
            if (plot == null)
            {
                return false;
            }

            coord = plot.roadAccessCell;
            return townWorld.Grid.IsInBounds(coord);
        }

        private TownPlot FindPlot(int plotId)
        {
            if (townWorld == null)
            {
                return null;
            }

            for (int i = 0; i < townWorld.Plots.Count; i++)
            {
                if (townWorld.Plots[i] != null && townWorld.Plots[i].id == plotId)
                {
                    return townWorld.Plots[i];
                }
            }

            return null;
        }

        private BusinessInstanceState FindBusinessByInstanceId(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return null;
            }

            if (generalStoreRuntime != null
                && generalStoreRuntime.CurrentBusiness != null
                && string.Equals(generalStoreRuntime.CurrentBusiness.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            {
                return generalStoreRuntime.CurrentBusiness;
            }

            if (sharedBusinessRuntime != null)
            {
                for (int i = 0; i < sharedBusinessRuntime.Businesses.Count; i++)
                {
                    BusinessInstanceState business = sharedBusinessRuntime.Businesses[i];
                    if (business != null && string.Equals(business.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                    {
                        return business;
                    }
                }
            }

            return null;
        }

        private static bool IsAcceptedScheduledShipment(LogisticsShipmentState shipment)
        {
            return shipment != null
                && string.IsNullOrWhiteSpace(shipment.BlockedReason)
                && shipment.Status != LogisticsShipmentStatus.Failed;
        }

        private static ShipmentHaulingMode ResolveRecurringOrderHaulingMode(LocalRecurringOrderHaulingResponsibility responsibility)
        {
            return ResolveHaulingMode(responsibility);
        }

        private static ShipmentHaulingMode ResolveBusinessTransferHaulingMode(LocalRecurringOrderHaulingResponsibility responsibility)
        {
            return ResolveHaulingMode(responsibility);
        }

        private static ShipmentHaulingMode ResolveHaulingMode(LocalRecurringOrderHaulingResponsibility responsibility)
        {
            if (responsibility == LocalRecurringOrderHaulingResponsibility.Seller)
            {
                return ShipmentHaulingMode.SourceDelivers;
            }

            if (Enum.TryParse("DestinationPickup", true, out ShipmentHaulingMode destinationPickup))
            {
                return destinationPickup;
            }

            if (Enum.TryParse("DestinationPicksUp", true, out ShipmentHaulingMode destinationPicksUp))
            {
                return destinationPicksUp;
            }

            return ShipmentHaulingMode.HiredFreight;
        }

        private static string ResolveRecurringOrderFreightPayer(LocalRecurringOrderFulfillmentRequest request)
        {
            if (request.Template == null)
            {
                return request.Buyer != null ? request.Buyer.InstanceId : string.Empty;
            }

            return request.Template.HaulingResponsibility == LocalRecurringOrderHaulingResponsibility.Seller
                ? request.Seller != null ? request.Seller.InstanceId : string.Empty
                : request.Buyer != null ? request.Buyer.InstanceId : string.Empty;
        }

        private static string ResolveBusinessTransferFreightPayer(BusinessTransferAgreementState agreement, BusinessInstanceState source, BusinessInstanceState destination)
        {
            return agreement != null && agreement.HaulingResponsibility == LocalRecurringOrderHaulingResponsibility.Seller
                ? source != null ? source.InstanceId : string.Empty
                : destination != null ? destination.InstanceId : string.Empty;
        }

        private static string BuildRecurringOrderShipmentLabel(LocalRecurringOrderFulfillmentRequest request)
        {
            string seller = request.Seller != null ? request.Seller.RuntimeDisplayName : "seller";
            string buyer = request.Buyer != null ? request.Buyer.RuntimeDisplayName : "buyer";
            string category = request.Template != null ? request.Template.BuyerCategoryId : "goods";
            return $"recurring order {seller}->{buyer} {category}";
        }

        private static string BuildBusinessTransferShipmentLabel(BusinessTransferAgreementState agreement, BusinessInstanceState source, BusinessInstanceState destination)
        {
            string seller = source != null ? source.RuntimeDisplayName : "source";
            string buyer = destination != null ? destination.RuntimeDisplayName : "destination";
            string category = agreement != null ? agreement.DestinationCategoryId : "goods";
            return $"business transfer {seller}->{buyer} {category}";
        }

        private static bool IsCriticalSupplyShipment(LogisticsShipmentState shipment)
        {
            if (shipment == null)
            {
                return false;
            }

            string category = $"{shipment.sourceCategoryId} {shipment.destinationCategoryId}";
            return category.Contains("food", StringComparison.OrdinalIgnoreCase)
                || category.Contains("meat", StringComparison.OrdinalIgnoreCase)
                || category.Contains("bread", StringComparison.OrdinalIgnoreCase)
                || category.Contains("livestock", StringComparison.OrdinalIgnoreCase)
                || category.Contains("lumber", StringComparison.OrdinalIgnoreCase)
                || category.Contains("log", StringComparison.OrdinalIgnoreCase)
                || category.Contains("fuel", StringComparison.OrdinalIgnoreCase)
                || category.Contains("hardware", StringComparison.OrdinalIgnoreCase)
                || category.Contains("medicine", StringComparison.OrdinalIgnoreCase)
                || category.Contains("remed", StringComparison.OrdinalIgnoreCase);
        }

        private static float NormalizeForPressure(int value, int atValue)
        {
            return Mathf.Clamp01(atValue <= 0 ? value : value / (float)atValue);
        }

        private float CalculateHiredFreightReadiness01(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return 0f;
            }

            float workerCoverage = business.RuntimeState.RequiredWorkerCount <= 0
                ? 1f
                : Mathf.Clamp01(business.RuntimeState.ActiveRequiredWorkerCount / (float)Mathf.Max(1, business.RuntimeState.RequiredWorkerCount));
            return Mathf.Clamp01(business.OperatingEfficiency01 * business.RuntimeState.Reliability01 * workerCoverage);
        }

        private int CalculateEffectiveHiredFreightCapacity(BusinessInstanceState business, float readiness01)
        {
            if (business == null)
            {
                return 0;
            }

            return Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, business.BaselineDailyServiceCapacity) * Mathf.Clamp01(readiness01)));
        }

        private int CountActiveHiredFreightTripsForCarrier(string carrierBusinessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(carrierBusinessInstanceId))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < shipments.Count; i++)
            {
                LogisticsShipmentState shipment = shipments[i];
                if (shipment != null
                    && IsActiveShipmentForCapacity(shipment)
                    && shipment.HaulingMode == ShipmentHaulingMode.HiredFreight
                    && string.Equals(shipment.CarrierBusinessInstanceId, carrierBusinessInstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountActiveHiredFreightShipments()
        {
            int count = 0;
            for (int i = 0; i < shipments.Count; i++)
            {
                LogisticsShipmentState shipment = shipments[i];
                if (shipment != null && IsActiveShipmentForCapacity(shipment) && shipment.HaulingMode == ShipmentHaulingMode.HiredFreight)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountUnassignedActiveHiredFreightShipments()
        {
            int count = 0;
            for (int i = 0; i < shipments.Count; i++)
            {
                LogisticsShipmentState shipment = shipments[i];
                if (shipment != null
                    && IsActiveShipmentForCapacity(shipment)
                    && shipment.HaulingMode == ShipmentHaulingMode.HiredFreight
                    && string.IsNullOrWhiteSpace(shipment.CarrierBusinessInstanceId))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsActiveShipmentForCapacity(LogisticsShipmentState shipment)
        {
            return shipment != null
                && shipment.Status != LogisticsShipmentStatus.Completed
                && shipment.Status != LogisticsShipmentStatus.Failed;
        }

        private static LogisticsCargoClass ResolveCargoClass(string sourceCategoryId, string destinationCategoryId)
        {
            string categoryId = !string.IsNullOrWhiteSpace(destinationCategoryId) ? destinationCategoryId : sourceCategoryId;
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return LogisticsCargoClass.PackagedGoods;
            }

            if (categoryId.Contains("meat", StringComparison.OrdinalIgnoreCase)
                || categoryId.Contains("bread", StringComparison.OrdinalIgnoreCase)
                || categoryId.Contains("food", StringComparison.OrdinalIgnoreCase))
            {
                return LogisticsCargoClass.Perishables;
            }

            if (categoryId.Contains("livestock", StringComparison.OrdinalIgnoreCase))
            {
                return LogisticsCargoClass.LiveAnimals;
            }

            if (categoryId.Contains("lumber", StringComparison.OrdinalIgnoreCase)
                || categoryId.Contains("grain", StringComparison.OrdinalIgnoreCase)
                || categoryId.Contains("log", StringComparison.OrdinalIgnoreCase)
                || categoryId.Contains("fuel", StringComparison.OrdinalIgnoreCase))
            {
                return LogisticsCargoClass.BulkMaterials;
            }

            return LogisticsCargoClass.PackagedGoods;
        }

        private static string BuildShipmentId(string prefix, string sourceId, string destinationId, string categoryId)
        {
            return $"{prefix}:{sourceId}->{destinationId}:{categoryId}:{Guid.NewGuid():N}";
        }

        private void RefreshTownLedgerSummary()
        {
            lastTownLedgerSummary = CaptureTownPressureSnapshot().BuildLedgerSummary();
        }

        private static bool TouchesBusiness(LogisticsShipmentState shipment, string businessInstanceId)
        {
            return shipment != null
                && (string.Equals(shipment.sourceBusinessInstanceId, businessInstanceId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(shipment.destinationBusinessInstanceId, businessInstanceId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(shipment.CarrierBusinessInstanceId, businessInstanceId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(shipment.FreightPayerBusinessInstanceId, businessInstanceId, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveSummaryLabel(LogisticsShipmentState shipment)
        {
            return shipment != null && !string.IsNullOrWhiteSpace(shipment.summaryLabel)
                ? shipment.summaryLabel
                : "shipment";
        }

        private void NotifyTransferAgreementDelivered(LogisticsShipmentState shipment, int acceptedUnits)
        {
            if (shipment == null
                || acceptedUnits <= 0
                || sharedBusinessRuntime == null
                || string.IsNullOrWhiteSpace(shipment.TransferAgreementId))
            {
                return;
            }

            sharedBusinessRuntime.NotifyTransferAgreementShipmentDelivered(
                shipment.TransferAgreementId,
                shipment.ShipmentId,
                acceptedUnits,
                acceptedUnits * Mathf.Max(0, shipment.sellerUnitRevenueCents),
                acceptedUnits * Mathf.Max(0, shipment.buyerUnitCostCents),
                ResolveSummaryLabel(shipment));
        }

        private void NotifyTransferAgreementDeliveryBlocked(LogisticsShipmentState shipment, string reason)
        {
            if (shipment == null
                || sharedBusinessRuntime == null
                || string.IsNullOrWhiteSpace(shipment.TransferAgreementId))
            {
                return;
            }

            sharedBusinessRuntime.NotifyTransferAgreementShipmentBlocked(
                shipment.TransferAgreementId,
                shipment.ShipmentId,
                string.IsNullOrWhiteSpace(reason) ? "shipment blocked" : reason.Trim());
        }

        private static string FormatMoney(int cents)
        {
            return $"${Mathf.Max(0, cents) / 100f:0.00}";
        }

        private static string FormatGameTime(float gameSeconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(Mathf.Max(0f, gameSeconds));
            if (span.TotalDays >= 1d)
            {
                return $"{Mathf.Max(1, Mathf.RoundToInt((float)span.TotalDays))}d";
            }

            if (span.TotalHours >= 1d)
            {
                return $"{Mathf.Max(1, Mathf.RoundToInt((float)span.TotalHours))}h";
            }

            return $"{Mathf.Max(1, Mathf.RoundToInt((float)span.TotalMinutes))}m";
        }
    }

    public sealed class LogisticsTownPressureSnapshot
    {
        public LogisticsTownPressureSnapshot(
            int activeShipmentCount,
            int localShipmentCount,
            int offMapShipmentCount,
            int delayedShipmentCount,
            int failedShipmentCount,
            int blockedShipmentCount,
            int hiredFreightShipmentCount,
            int criticalSupplyBlockedShipmentCount,
            int liveryFreightCarrierCount,
            int activeLiveryFreightCarrierCount,
            int effectiveLiveryFreightDailyCapacity,
            int unassignedHiredFreightShipmentCount,
            int overloadedCarrierCount,
            float liveryFreightLoadRatio01,
            float averageLiveryFreightReadiness01,
            float pressure01)
        {
            ActiveShipmentCount = Mathf.Max(0, activeShipmentCount);
            LocalShipmentCount = Mathf.Max(0, localShipmentCount);
            OffMapShipmentCount = Mathf.Max(0, offMapShipmentCount);
            DelayedShipmentCount = Mathf.Max(0, delayedShipmentCount);
            FailedShipmentCount = Mathf.Max(0, failedShipmentCount);
            BlockedShipmentCount = Mathf.Max(0, blockedShipmentCount);
            HiredFreightShipmentCount = Mathf.Max(0, hiredFreightShipmentCount);
            CriticalSupplyBlockedShipmentCount = Mathf.Max(0, criticalSupplyBlockedShipmentCount);
            LiveryFreightCarrierCount = Mathf.Max(0, liveryFreightCarrierCount);
            ActiveLiveryFreightCarrierCount = Mathf.Max(0, activeLiveryFreightCarrierCount);
            EffectiveLiveryFreightDailyCapacity = Mathf.Max(0, effectiveLiveryFreightDailyCapacity);
            UnassignedHiredFreightShipmentCount = Mathf.Max(0, unassignedHiredFreightShipmentCount);
            OverloadedCarrierCount = Mathf.Max(0, overloadedCarrierCount);
            LiveryFreightLoadRatio01 = Mathf.Max(0f, liveryFreightLoadRatio01);
            AverageLiveryFreightReadiness01 = Mathf.Clamp01(averageLiveryFreightReadiness01);
            Pressure01 = Mathf.Clamp01(pressure01);
        }

        public int ActiveShipmentCount { get; }
        public int LocalShipmentCount { get; }
        public int OffMapShipmentCount { get; }
        public int DelayedShipmentCount { get; }
        public int FailedShipmentCount { get; }
        public int BlockedShipmentCount { get; }
        public int HiredFreightShipmentCount { get; }
        public int CriticalSupplyBlockedShipmentCount { get; }
        public int LiveryFreightCarrierCount { get; }
        public int ActiveLiveryFreightCarrierCount { get; }
        public int EffectiveLiveryFreightDailyCapacity { get; }
        public int UnassignedHiredFreightShipmentCount { get; }
        public int OverloadedCarrierCount { get; }
        public float LiveryFreightLoadRatio01 { get; }
        public float AverageLiveryFreightReadiness01 { get; }
        public float Pressure01 { get; }
        public bool HasPressure => Pressure01 > 0.01f;

        public string BuildLedgerSummary()
        {
            return $"Logistics ledger: {ActiveShipmentCount} active | local {LocalShipmentCount} | off-map {OffMapShipmentCount} | delayed {DelayedShipmentCount} | blocked {BlockedShipmentCount} | failed {FailedShipmentCount} | hired freight {HiredFreightShipmentCount}.";
        }

        public string BuildPressureDetail()
        {
            return $"Shipments active {ActiveShipmentCount}; blocked {BlockedShipmentCount}, delayed {DelayedShipmentCount}, failed {FailedShipmentCount}, critical blocked {CriticalSupplyBlockedShipmentCount}. Livery/Freight active carriers {ActiveLiveryFreightCarrierCount}/{LiveryFreightCarrierCount}, capacity {EffectiveLiveryFreightDailyCapacity}, load {Mathf.RoundToInt(LiveryFreightLoadRatio01 * 100f)}%, readiness {Mathf.RoundToInt(AverageLiveryFreightReadiness01 * 100f)}%.";
        }

        public string BuildActionText()
        {
            if (ActiveLiveryFreightCarrierCount <= 0 && HiredFreightShipmentCount > 0)
            {
                return "Open, staff, or hire Livery/Freight capacity before relying on hired hauling.";
            }

            if (CriticalSupplyBlockedShipmentCount > 0)
            {
                return "Clear blocked critical deliveries before stockouts become wider town pressure.";
            }

            if (OverloadedCarrierCount > 0 || LiveryFreightLoadRatio01 > 1f)
            {
                return "Add freight capacity or move some routes back to self-haul/pickup.";
            }

            return "Review Supply & Trade routes, hauling responsibility, and blocked deliveries.";
        }
    }

    public sealed class LiveryFreightReadinessSnapshot
    {
        public LiveryFreightReadinessSnapshot(
            int totalCarrierCount,
            int activeCarrierCount,
            int unavailableCarrierCount,
            int effectiveDailyCapacity,
            int activeHiredFreightShipmentCount,
            int unassignedHiredFreightShipmentCount,
            int overloadedCarrierCount,
            float averageReadiness01,
            float loadRatio01)
        {
            TotalCarrierCount = Mathf.Max(0, totalCarrierCount);
            ActiveCarrierCount = Mathf.Max(0, activeCarrierCount);
            UnavailableCarrierCount = Mathf.Max(0, unavailableCarrierCount);
            EffectiveDailyCapacity = Mathf.Max(0, effectiveDailyCapacity);
            ActiveHiredFreightShipmentCount = Mathf.Max(0, activeHiredFreightShipmentCount);
            UnassignedHiredFreightShipmentCount = Mathf.Max(0, unassignedHiredFreightShipmentCount);
            OverloadedCarrierCount = Mathf.Max(0, overloadedCarrierCount);
            AverageReadiness01 = Mathf.Clamp01(averageReadiness01);
            LoadRatio01 = Mathf.Max(0f, loadRatio01);
        }

        public int TotalCarrierCount { get; }
        public int ActiveCarrierCount { get; }
        public int UnavailableCarrierCount { get; }
        public int EffectiveDailyCapacity { get; }
        public int ActiveHiredFreightShipmentCount { get; }
        public int UnassignedHiredFreightShipmentCount { get; }
        public int OverloadedCarrierCount { get; }
        public float AverageReadiness01 { get; }
        public float LoadRatio01 { get; }

        public string BuildSummary()
        {
            return $"Livery/Freight: carriers {ActiveCarrierCount}/{TotalCarrierCount}, capacity {EffectiveDailyCapacity}, hired trips {ActiveHiredFreightShipmentCount}, unassigned {UnassignedHiredFreightShipmentCount}, load {Mathf.RoundToInt(LoadRatio01 * 100f)}%.";
        }
    }
}
