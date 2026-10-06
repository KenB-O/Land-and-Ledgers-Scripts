using System;
using System.Collections.Generic;
using LandLedgers.Pathing;
using LandLedgers.Persistence;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum LogisticsCargoClass
    {
        PackagedGoods = 0,
        Perishables = 1,
        LiveAnimals = 2,
        BulkMaterials = 3
    }

    public enum LogisticsSupplierClass
    {
        InternalSupplier = 0,
        LocalBusiness = 1,
        HiredHauling = 2,
        OffMapSupplier = 3
    }

    public enum LogisticsShipmentKind
    {
        LocalTradeTransfer = 0,
        OffMapInboundPurchase = 1,
        OffMapOutboundSale = 2,
        GeneralStoreReorder = 3
    }

    public enum LogisticsShipmentStatus
    {
        Planned = 0,
        Loading = 1,
        InTransit = 2,
        Arrived = 3,
        Unloading = 4,
        Completed = 5,
        Partial = 6,
        Failed = 7,
        Delayed = 8
    }

    public enum LogisticsShipmentEndpointKind
    {
        None = 0,
        Business = 1,
        GeneralStore = 2,
        OffMap = 3
    }

    public enum LogisticsShipmentDeliveryMode
    {
        AddCategoryStock = 0,
        ReceivePendingReorder = 1,
        GeneralStoreLocalSupply = 2
    }

    public enum ShipmentHaulingMode
    {
        SourceDelivers = 0,
        DestinationPicksUp = 1,
        HiredFreight = 2
    }

    public enum LogisticsTransportVisualState
    {
        LoadedWagon = 0,
        LightReturn = 1,
        HorseOnly = 2
    }

    [Serializable]
    public sealed class LogisticsShipmentState
    {
        [SerializeField] public string shipmentId = string.Empty;
        [SerializeField] public string transferAgreementId = string.Empty;
        [SerializeField] public LogisticsShipmentKind shipmentKind;
        [SerializeField] public LogisticsCargoClass cargoClass;
        [SerializeField] public LogisticsSupplierClass supplierClass;
        [SerializeField] public LogisticsShipmentEndpointKind sourceEndpointKind;
        [SerializeField] public LogisticsShipmentEndpointKind destinationEndpointKind;
        [SerializeField] public LogisticsShipmentDeliveryMode deliveryMode;
        [SerializeField] public ShipmentHaulingMode haulingMode;
        [SerializeField] public LogisticsShipmentStatus state = LogisticsShipmentStatus.Planned;
        [SerializeField] public LogisticsShipmentStatus resumeStateAfterDelay = LogisticsShipmentStatus.Unloading;
        [SerializeField] public string sourceBusinessInstanceId = string.Empty;
        [SerializeField] public string destinationBusinessInstanceId = string.Empty;
        [SerializeField] public string carrierBusinessInstanceId = string.Empty;
        [SerializeField] public string freightPayerBusinessInstanceId = string.Empty;
        [SerializeField] public string sourceCategoryId = string.Empty;
        [SerializeField] public string destinationCategoryId = string.Empty;
        [SerializeField] public string summaryLabel = string.Empty;
        [SerializeField] public string blockedReason = string.Empty;
        [SerializeField] public int plannedQuantityUnits;
        [SerializeField] public int remainingQuantityUnits;
        [SerializeField] public int sellerUnitRevenueCents;
        [SerializeField] public int buyerUnitCostCents;
        [SerializeField] public int freightChargeCents;
        [SerializeField] public bool sourceCommittedAtSchedule;
        [SerializeField] public bool loadApplied;
        [SerializeField] public bool deliveryApplied;
        [SerializeField] public float loadingDurationGameSeconds = 30f;
        [SerializeField] public float transitDurationGameSeconds = 120f;
        [SerializeField] public float unloadingDurationGameSeconds = 30f;
        [SerializeField] public float stageElapsedGameSeconds;
        [SerializeField] public float totalElapsedGameSeconds;
        [SerializeField] public string physicalWagonAssetId = string.Empty;
        [SerializeField] public string physicalDriverPersonId = string.Empty;
        [SerializeField] public List<string> physicalDraftAnimalIds = new();
        [SerializeField] public bool physicalTransportBound;
        [SerializeField] public string physicalLocationId = string.Empty;
        [SerializeField] public float physicalProgress01;
        [SerializeField] public LogisticsRoutePlan routePlan = new();

        public string ShipmentId => shipmentId ?? string.Empty;
        public string TransferAgreementId => transferAgreementId ?? string.Empty;
        public LogisticsCargoClass CargoClass => cargoClass;
        public ShipmentHaulingMode HaulingMode => haulingMode;
        public LogisticsShipmentStatus Status => state;
        public string BlockedReason => blockedReason ?? string.Empty;
        public LogisticsRoutePlan RoutePlan => routePlan ??= new LogisticsRoutePlan();
        public string CarrierBusinessInstanceId => carrierBusinessInstanceId ?? string.Empty;
        public string FreightPayerBusinessInstanceId => freightPayerBusinessInstanceId ?? string.Empty;
        public int PlannedQuantityUnits => Mathf.Max(0, plannedQuantityUnits);
        public int RemainingQuantityUnits => Mathf.Max(0, remainingQuantityUnits);
        public int FreightChargeCents => Mathf.Max(0, freightChargeCents);
        public float EstimatedRemainingGameSeconds => Mathf.Max(0f, ResolveCurrentStageDuration() - stageElapsedGameSeconds);

        /// <summary>
        /// MR-P001: True when this shipment is a pre-load blocked record that owns zero
        /// physical cargo. Loading never occurred; source inventory was not committed.
        /// </summary>
        public bool IsPreLoadBlocked =>
            !string.IsNullOrWhiteSpace(blockedReason)
            && state == LogisticsShipmentStatus.Failed
            && !loadApplied
            && !sourceCommittedAtSchedule
            && remainingQuantityUnits <= 0;


        public void AdvanceGameSeconds(float gameSeconds)
        {
            float remaining = Mathf.Max(0f, gameSeconds);
            while (remaining > 0f)
            {
                if (state == LogisticsShipmentStatus.Completed
                    || state == LogisticsShipmentStatus.Failed
                    || state == LogisticsShipmentStatus.Partial)
                {
                    return;
                }

                if (state == LogisticsShipmentStatus.Planned)
                {
                    state = LogisticsShipmentStatus.Loading;
                    stageElapsedGameSeconds = 0f;
                }

                float duration = Mathf.Max(0.01f, ResolveCurrentStageDuration());
                float step = Mathf.Min(remaining, duration - stageElapsedGameSeconds);
                stageElapsedGameSeconds += step;
                totalElapsedGameSeconds += step;
                remaining -= step;

                if (stageElapsedGameSeconds + 0.0001f < duration)
                {
                    break;
                }

                AdvanceToNextStage();
            }
        }

        public void MarkDelayed(string reason, LogisticsShipmentStatus resumeState = LogisticsShipmentStatus.Unloading)
        {
            blockedReason = reason ?? string.Empty;
            resumeStateAfterDelay = resumeState;
            state = LogisticsShipmentStatus.Delayed;
            stageElapsedGameSeconds = 0f;
        }

        public void ResumeFromDelay()
        {
            if (state != LogisticsShipmentStatus.Delayed)
            {
                return;
            }

            state = resumeStateAfterDelay;
            stageElapsedGameSeconds = 0f;
        }

        public LogisticsShipmentSaveDto CaptureSaveDto()
        {
            return new LogisticsShipmentSaveDto
            {
                shipmentId = ShipmentId,
                transferAgreementId = TransferAgreementId,
                shipmentKind = shipmentKind,
                cargoClass = cargoClass,
                supplierClass = supplierClass,
                sourceEndpointKind = sourceEndpointKind,
                destinationEndpointKind = destinationEndpointKind,
                deliveryMode = deliveryMode,
                haulingMode = haulingMode,
                state = state,
                resumeStateAfterDelay = resumeStateAfterDelay,
                sourceBusinessInstanceId = sourceBusinessInstanceId ?? string.Empty,
                destinationBusinessInstanceId = destinationBusinessInstanceId ?? string.Empty,
                carrierBusinessInstanceId = carrierBusinessInstanceId ?? string.Empty,
                freightPayerBusinessInstanceId = freightPayerBusinessInstanceId ?? string.Empty,
                sourceCategoryId = sourceCategoryId ?? string.Empty,
                destinationCategoryId = destinationCategoryId ?? string.Empty,
                summaryLabel = summaryLabel ?? string.Empty,
                blockedReason = blockedReason ?? string.Empty,
                plannedQuantityUnits = PlannedQuantityUnits,
                remainingQuantityUnits = RemainingQuantityUnits,
                sellerUnitRevenueCents = Mathf.Max(0, sellerUnitRevenueCents),
                buyerUnitCostCents = Mathf.Max(0, buyerUnitCostCents),
                freightChargeCents = FreightChargeCents,
                sourceCommittedAtSchedule = sourceCommittedAtSchedule,
                loadApplied = loadApplied,
                deliveryApplied = deliveryApplied,
                loadingDurationGameSeconds = Mathf.Max(0f, loadingDurationGameSeconds),
                transitDurationGameSeconds = Mathf.Max(0f, transitDurationGameSeconds),
                unloadingDurationGameSeconds = Mathf.Max(0f, unloadingDurationGameSeconds),
                stageElapsedGameSeconds = Mathf.Max(0f, stageElapsedGameSeconds),
                totalElapsedGameSeconds = Mathf.Max(0f, totalElapsedGameSeconds),
                physicalWagonAssetId = physicalWagonAssetId ?? string.Empty,
                physicalDriverPersonId = physicalDriverPersonId ?? string.Empty,
                physicalDraftAnimalIds = new List<string>(physicalDraftAnimalIds ?? new List<string>()),
                physicalTransportBound = physicalTransportBound,
                physicalLocationId = physicalLocationId ?? string.Empty,
                physicalProgress01 = Mathf.Clamp01(physicalProgress01),
                routePlan = CaptureRoutePlanSaveDto(RoutePlan)
            };
        }

        public static LogisticsShipmentState FromSaveDto(LogisticsShipmentSaveDto dto)
        {
            LogisticsShipmentState state = new();
            if (dto == null)
            {
                return state;
            }

            state.shipmentId = dto.shipmentId ?? string.Empty;
            state.transferAgreementId = dto.transferAgreementId ?? string.Empty;
            state.shipmentKind = dto.shipmentKind;
            state.cargoClass = dto.cargoClass;
            state.supplierClass = dto.supplierClass;
            state.sourceEndpointKind = dto.sourceEndpointKind;
            state.destinationEndpointKind = dto.destinationEndpointKind;
            state.deliveryMode = dto.deliveryMode;
            state.haulingMode = dto.haulingMode;
            state.state = dto.state;
            state.resumeStateAfterDelay = dto.resumeStateAfterDelay;
            state.sourceBusinessInstanceId = dto.sourceBusinessInstanceId ?? string.Empty;
            state.destinationBusinessInstanceId = dto.destinationBusinessInstanceId ?? string.Empty;
            state.carrierBusinessInstanceId = dto.carrierBusinessInstanceId ?? string.Empty;
            state.freightPayerBusinessInstanceId = dto.freightPayerBusinessInstanceId ?? string.Empty;
            state.sourceCategoryId = dto.sourceCategoryId ?? string.Empty;
            state.destinationCategoryId = dto.destinationCategoryId ?? string.Empty;
            state.summaryLabel = dto.summaryLabel ?? string.Empty;
            state.blockedReason = dto.blockedReason ?? string.Empty;
            state.plannedQuantityUnits = Mathf.Max(0, dto.plannedQuantityUnits);
            state.remainingQuantityUnits = Mathf.Max(0, dto.remainingQuantityUnits);
            state.sellerUnitRevenueCents = Mathf.Max(0, dto.sellerUnitRevenueCents);
            state.buyerUnitCostCents = Mathf.Max(0, dto.buyerUnitCostCents);
            state.freightChargeCents = Mathf.Max(0, dto.freightChargeCents);
            state.sourceCommittedAtSchedule = dto.sourceCommittedAtSchedule;
            state.loadApplied = dto.loadApplied;
            state.deliveryApplied = dto.deliveryApplied;
            state.loadingDurationGameSeconds = Mathf.Max(0f, dto.loadingDurationGameSeconds);
            state.transitDurationGameSeconds = Mathf.Max(0f, dto.transitDurationGameSeconds);
            state.unloadingDurationGameSeconds = Mathf.Max(0f, dto.unloadingDurationGameSeconds);
            state.stageElapsedGameSeconds = Mathf.Max(0f, dto.stageElapsedGameSeconds);
            state.totalElapsedGameSeconds = Mathf.Max(0f, dto.totalElapsedGameSeconds);
            state.physicalWagonAssetId = dto.physicalWagonAssetId ?? string.Empty;
            state.physicalDriverPersonId = dto.physicalDriverPersonId ?? string.Empty;
            state.physicalDraftAnimalIds = new List<string>(dto.physicalDraftAnimalIds ?? new List<string>());
            state.physicalTransportBound = dto.physicalTransportBound;
            state.physicalLocationId = dto.physicalLocationId ?? string.Empty;
            state.physicalProgress01 = Mathf.Clamp01(dto.physicalProgress01);
            state.routePlan = RestoreRoutePlanSaveDto(dto.routePlan);

            // MR-P001: Normalize legacy blocked shipment data.
            // Before MR-P001, CreateBlockedShipment set remainingQuantityUnits = planned,
            // sourceCommittedAtSchedule = true, loadApplied = true, state = Delayed.
            // This created phantom cargo that could survive reload and trigger delivery.

            // Deterministic repair (J): Where evidence proves loading never occurred:
            // - shipmentId starts with "blocked:" (direct CreateBlockedShipment record), OR
            // - loadApplied is false with a blocked reason, OR
            // - route planning / road access / carrier failure with empty route plan.
            bool provablyNeverLoaded =
                (!string.IsNullOrWhiteSpace(state.shipmentId) && state.shipmentId.StartsWith("blocked:", StringComparison.OrdinalIgnoreCase))
                || (!state.loadApplied && !string.IsNullOrWhiteSpace(state.blockedReason))
                || (!string.IsNullOrWhiteSpace(state.blockedReason)
                    && (state.routePlan == null || state.routePlan.TotalTravelCells <= 0)
                    && (state.blockedReason.Contains("road access", StringComparison.OrdinalIgnoreCase)
                        || state.blockedReason.Contains("route planning", StringComparison.OrdinalIgnoreCase)
                        || state.blockedReason.Contains("carrier available", StringComparison.OrdinalIgnoreCase)));

            if (provablyNeverLoaded && state.remainingQuantityUnits > 0)
            {
                state.remainingQuantityUnits = 0;
                state.loadApplied = false;
                state.sourceCommittedAtSchedule = false;
                state.state = LogisticsShipmentStatus.Failed;
            }

            // Ambiguous quarantine (K): If legacy state is ambiguous (e.g. delayed with blocked reason,
            // or has blocked reason but delivery never occurred and remaining cargo > 0),
            // quarantine/block execution rather than inventing physical or economic history.
            // Do not fabricate source deductions, payments, or destination receipts.
            bool isAmbiguousMalformed =
                !provablyNeverLoaded
                && !string.IsNullOrWhiteSpace(state.blockedReason)
                && !state.deliveryApplied
                && state.remainingQuantityUnits > 0
                && (state.state == LogisticsShipmentStatus.Delayed || state.state == LogisticsShipmentStatus.Failed);

            if (isAmbiguousMalformed)
            {
                state.state = LogisticsShipmentStatus.Failed;
                state.remainingQuantityUnits = 0;
                state.loadApplied = false;
                state.sourceCommittedAtSchedule = false;
            }

            return state;
        }

        internal float ResolveVisiblePathProgress01()
        {
            if (state == LogisticsShipmentStatus.Loading)
            {
                return 0f;
            }

            if (state == LogisticsShipmentStatus.Unloading
                || state == LogisticsShipmentStatus.Arrived
                || state == LogisticsShipmentStatus.Completed
                || state == LogisticsShipmentStatus.Partial)
            {
                return 1f;
            }

            if (transitDurationGameSeconds <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01(stageElapsedGameSeconds / transitDurationGameSeconds);
        }

        private float ResolveCurrentStageDuration()
        {
            return state switch
            {
                LogisticsShipmentStatus.Loading => Mathf.Max(0.01f, loadingDurationGameSeconds),
                LogisticsShipmentStatus.InTransit => Mathf.Max(0.01f, transitDurationGameSeconds),
                LogisticsShipmentStatus.Arrived => 0.01f,
                LogisticsShipmentStatus.Unloading => Mathf.Max(0.01f, unloadingDurationGameSeconds),
                LogisticsShipmentStatus.Delayed => 0.01f,
                _ => 0.01f
            };
        }

        private void AdvanceToNextStage()
        {
            stageElapsedGameSeconds = 0f;
            state = state switch
            {
                LogisticsShipmentStatus.Loading => LogisticsShipmentStatus.InTransit,
                LogisticsShipmentStatus.InTransit => LogisticsShipmentStatus.Unloading,
                LogisticsShipmentStatus.Arrived => LogisticsShipmentStatus.Unloading,
                LogisticsShipmentStatus.Unloading => LogisticsShipmentStatus.Completed,
                LogisticsShipmentStatus.Delayed => resumeStateAfterDelay,
                _ => state
            };
        }

        private static LogisticsRoutePlanSaveDto CaptureRoutePlanSaveDto(LogisticsRoutePlan plan)
        {
            LogisticsRoutePlanSaveDto dto = new();
            if (plan == null)
            {
                return dto;
            }

            dto.label = plan.label ?? string.Empty;
            dto.visibleStart = new GridCoordSaveDto { x = plan.visibleStart.x, z = plan.visibleStart.z };
            dto.visibleEnd = new GridCoordSaveDto { x = plan.visibleEnd.x, z = plan.visibleEnd.z };
            dto.totalTravelCells = plan.TotalTravelCells;
            dto.visibleTravelCells = plan.VisibleTravelCells;
            dto.totalTravelGameSeconds = plan.TotalTravelGameSeconds;
            dto.visibleTravelGameSeconds = plan.VisibleTravelGameSeconds;
            dto.quality = plan.Quality;
            dto.failureReason = plan.FailureReason;
            if (plan.VisiblePath != null)
            {
                for (int i = 0; i < plan.VisiblePath.Count; i++)
                {
                    GridCoord coord = plan.VisiblePath[i];
                    dto.visiblePath.Add(new GridCoordSaveDto { x = coord.x, z = coord.z });
                }
            }

            return dto;
        }

        private static LogisticsRoutePlan RestoreRoutePlanSaveDto(LogisticsRoutePlanSaveDto dto)
        {
            LogisticsRoutePlan plan = new();
            if (dto == null)
            {
                return plan;
            }

            plan.label = dto.label ?? string.Empty;
            plan.visibleStart = new GridCoord(dto.visibleStart != null ? dto.visibleStart.x : 0, dto.visibleStart != null ? dto.visibleStart.z : 0);
            plan.visibleEnd = new GridCoord(dto.visibleEnd != null ? dto.visibleEnd.x : 0, dto.visibleEnd != null ? dto.visibleEnd.z : 0);
            plan.totalTravelCells = Mathf.Max(0, dto.totalTravelCells);
            plan.visibleTravelCells = Mathf.Max(0, dto.visibleTravelCells);
            plan.totalTravelGameSeconds = Mathf.Max(0f, dto.totalTravelGameSeconds);
            plan.visibleTravelGameSeconds = Mathf.Max(0f, dto.visibleTravelGameSeconds);
            plan.quality = dto.quality;
            plan.failureReason = dto.failureReason ?? string.Empty;
            if (dto.visiblePath != null)
            {
                for (int i = 0; i < dto.visiblePath.Count; i++)
                {
                    GridCoordSaveDto coord = dto.visiblePath[i];
                    if (coord == null)
                    {
                        continue;
                    }

                    plan.visiblePath.Add(new GridCoord(coord.x, coord.z));
                }
            }

            return plan;
        }
    }

    public static class LogisticsHaulingModeBridge
    {
        public static ShipmentHaulingMode ToShipmentHaulingMode(this LocalRecurringOrderHaulingResponsibility responsibility)
        {
            return responsibility switch
            {
                LocalRecurringOrderHaulingResponsibility.Buyer => ShipmentHaulingMode.DestinationPicksUp,
                LocalRecurringOrderHaulingResponsibility.Shared => ShipmentHaulingMode.HiredFreight,
                _ => ShipmentHaulingMode.SourceDelivers
            };
        }
    }
}
