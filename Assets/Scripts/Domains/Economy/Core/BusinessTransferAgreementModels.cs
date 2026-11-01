using System;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum BusinessTransferAllocationMode
    {
        FixedUnits = 0,
        PercentOfAvailable = 1
    }

    // First-pass pricing uses current category market/cost helpers because lot-level carrying basis does not exist yet.
    public enum BusinessTransferPricingMode
    {
        Free = 0,
        AtCostOrNominalCost = 1,
        BelowMarket = 2,
        Market = 3,
        AboveMarket = 4,
        CustomModifier = 5
    }

    [Serializable]
    public sealed class BusinessTransferAgreementState
    {
        [SerializeField]
        private string agreementId = string.Empty;

        [SerializeField]
        private string sourceBusinessInstanceId = string.Empty;

        [SerializeField]
        private string destinationBusinessInstanceId = string.Empty;

        [SerializeField]
        private string sourceCategoryId = string.Empty;

        [SerializeField]
        private string destinationCategoryId = string.Empty;

        [SerializeField]
        private BusinessTransferAllocationMode allocationMode;

        [SerializeField]
        private int allocationValue;

        [SerializeField]
        private BusinessCadence cadence = BusinessCadence.Weekly;

        [SerializeField, Min(1)]
        private int cadenceInterval = 1;

        [SerializeField]
        private BusinessTransferPricingMode pricingMode = BusinessTransferPricingMode.Market;

        [SerializeField]
        private float priceModifier = 1f;

        [SerializeField, Min(0)]
        private int sourceReserveUnits;

        [SerializeField]
        private LocalRecurringOrderHaulingResponsibility haulingResponsibility;

        [SerializeField]
        private bool active = true;

        [SerializeField]
        private int nextDueWeekKey = -1;

        [SerializeField]
        private int lastResolvedWeekKey = -1;

        [SerializeField]
        private int lastScheduledUnits;

        [SerializeField]
        private int lastDeliveredUnits;

        [SerializeField]
        private int lastScheduledTotalPriceCents;

        [SerializeField]
        private int lastDeliveredTotalPriceCents;

        [SerializeField]
        private string lastShipmentId = string.Empty;

        [SerializeField, TextArea(1, 3)]
        private string lastFulfillmentSummary = "No owned transfers resolved yet.";

        public string AgreementId => agreementId ?? string.Empty;
        public string SourceBusinessInstanceId => sourceBusinessInstanceId ?? string.Empty;
        public string DestinationBusinessInstanceId => destinationBusinessInstanceId ?? string.Empty;
        public string SourceCategoryId => sourceCategoryId ?? string.Empty;
        public string DestinationCategoryId => destinationCategoryId ?? string.Empty;
        public BusinessTransferAllocationMode AllocationMode => allocationMode;
        public int AllocationValue => Mathf.Max(0, allocationValue);
        public BusinessCadence Cadence => cadence;
        public int CadenceInterval => Mathf.Max(1, cadenceInterval);
        public BusinessTransferPricingMode PricingMode => pricingMode;
        public float PriceModifier => float.IsNaN(priceModifier) ? 1f : priceModifier;
        public int SourceReserveUnits => Mathf.Max(0, sourceReserveUnits);
        public LocalRecurringOrderHaulingResponsibility HaulingResponsibility => haulingResponsibility;
        public bool Active => active;
        public int NextDueWeekKey => nextDueWeekKey;
        public int LastResolvedWeekKey => lastResolvedWeekKey;
        public int LastScheduledUnits => Mathf.Max(0, lastScheduledUnits);
        public int LastDeliveredUnits => Mathf.Max(0, lastDeliveredUnits);
        public int LastScheduledTotalPriceCents => Mathf.Max(0, lastScheduledTotalPriceCents);
        public int LastDeliveredTotalPriceCents => Mathf.Max(0, lastDeliveredTotalPriceCents);
        public string LastShipmentId => lastShipmentId ?? string.Empty;
        public string LastFulfillmentSummary => string.IsNullOrWhiteSpace(lastFulfillmentSummary)
            ? "No owned transfers resolved yet."
            : lastFulfillmentSummary;
        public bool HasLastShipment => !string.IsNullOrWhiteSpace(LastShipmentId);
        public int LastPendingUnits => Mathf.Max(0, LastScheduledUnits - LastDeliveredUnits);
        public bool LastAttemptStillInTransit => HasLastShipment && LastScheduledUnits > 0 && LastDeliveredUnits < LastScheduledUnits;

        public void Configure(
            string agreementId,
            string sourceBusinessInstanceId,
            string destinationBusinessInstanceId,
            string sourceCategoryId,
            string destinationCategoryId,
            BusinessTransferAllocationMode allocationMode,
            int allocationValue,
            BusinessCadence cadence,
            int cadenceInterval,
            BusinessTransferPricingMode pricingMode,
            float priceModifier,
            int sourceReserveUnits,
            LocalRecurringOrderHaulingResponsibility haulingResponsibility,
            bool active,
            int nextDueWeekKey)
        {
            this.agreementId = agreementId ?? string.Empty;
            this.sourceBusinessInstanceId = sourceBusinessInstanceId ?? string.Empty;
            this.destinationBusinessInstanceId = destinationBusinessInstanceId ?? string.Empty;
            this.sourceCategoryId = sourceCategoryId ?? string.Empty;
            this.destinationCategoryId = destinationCategoryId ?? string.Empty;
            this.allocationMode = allocationMode;
            this.allocationValue = Mathf.Max(0, allocationValue);
            this.cadence = cadence;
            this.cadenceInterval = Mathf.Max(1, cadenceInterval);
            this.pricingMode = pricingMode;
            this.priceModifier = SanitizePriceModifier(priceModifier);
            this.sourceReserveUnits = Mathf.Max(0, sourceReserveUnits);
            this.haulingResponsibility = haulingResponsibility;
            this.active = active;
            this.nextDueWeekKey = nextDueWeekKey;
        }

        public bool IsDue(int weekKey)
        {
            return Active && (nextDueWeekKey < 0 || weekKey >= nextDueWeekKey);
        }

        public void RecordAttemptScheduled(int weekKey, int units, int totalPriceCents, string summary, string shipmentId = null)
        {
            lastResolvedWeekKey = weekKey;
            lastScheduledUnits = Mathf.Max(0, units);
            lastScheduledTotalPriceCents = Mathf.Max(0, totalPriceCents);
            lastDeliveredUnits = 0;
            lastDeliveredTotalPriceCents = 0;
            lastShipmentId = shipmentId ?? string.Empty;
            lastFulfillmentSummary = string.IsNullOrWhiteSpace(summary) ? "Transfer scheduled." : summary.Trim();
            ScheduleNextDue(weekKey);
        }

        public void RecordDelivery(int weekKey, int units, int totalPriceCents, string summary, string shipmentId = null)
        {
            lastResolvedWeekKey = weekKey;
            lastDeliveredUnits = Mathf.Max(0, units);
            lastDeliveredTotalPriceCents = Mathf.Max(0, totalPriceCents);
            if (!string.IsNullOrWhiteSpace(shipmentId))
            {
                lastShipmentId = shipmentId.Trim();
            }

            lastFulfillmentSummary = string.IsNullOrWhiteSpace(summary) ? "Transfer delivered." : summary.Trim();
        }

        public void RecordBlocked(int weekKey, string summary)
        {
            lastResolvedWeekKey = weekKey;
            lastScheduledUnits = 0;
            lastDeliveredUnits = 0;
            lastScheduledTotalPriceCents = 0;
            lastDeliveredTotalPriceCents = 0;
            lastShipmentId = string.Empty;
            lastFulfillmentSummary = string.IsNullOrWhiteSpace(summary) ? "Transfer blocked." : summary.Trim();
            ScheduleNextDue(weekKey);
        }

        public void RecordCancelled(string summary)
        {
            active = false;
            lastFulfillmentSummary = string.IsNullOrWhiteSpace(summary) ? "Transfer cancelled." : summary.Trim();
            nextDueWeekKey = -1;
        }

        public int CalculateSchedulableUnits(int availableSourceUnits)
        {
            int availableAfterReserve = Mathf.Max(0, availableSourceUnits - SourceReserveUnits);
            if (availableAfterReserve <= 0 || !Active)
            {
                return 0;
            }

            return AllocationMode switch
            {
                BusinessTransferAllocationMode.PercentOfAvailable => Mathf.Clamp(Mathf.RoundToInt(availableAfterReserve * (AllocationValue / 100f)), 0, availableAfterReserve),
                _ => Mathf.Min(AllocationValue, availableAfterReserve)
            };
        }

        public bool TryCalculateSchedulableUnits(int availableSourceUnits, out int units, out string reason)
        {
            units = CalculateSchedulableUnits(availableSourceUnits);
            if (!Active)
            {
                reason = "agreement inactive";
                return false;
            }

            if (availableSourceUnits <= SourceReserveUnits)
            {
                reason = $"source reserve protected {SourceReserveUnits} units";
                return false;
            }

            if (units <= 0)
            {
                reason = "allocation resolves to zero units";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public string BuildStandingTermsSummary()
        {
            string allocation = AllocationMode == BusinessTransferAllocationMode.PercentOfAvailable
                ? $"{AllocationValue}% of available after reserve"
                : $"{AllocationValue} units";
            return $"{SourceCategoryId}->{DestinationCategoryId}: {allocation}, reserve {SourceReserveUnits}, {Cadence} every {CadenceInterval}, pricing {PricingMode}, haul {HaulingResponsibility}.";
        }

        public string BuildLastFulfillmentReadout(string sourceDisplayName = null, string destinationDisplayName = null)
        {
            string source = string.IsNullOrWhiteSpace(sourceDisplayName) ? "source" : sourceDisplayName.Trim();
            string destination = string.IsNullOrWhiteSpace(destinationDisplayName) ? "destination" : destinationDisplayName.Trim();
            string shipment = HasLastShipment ? $" shipment {LastShipmentId}" : string.Empty;
            string pending = LastPendingUnits > 0 ? $" pending {LastPendingUnits}" : string.Empty;
            return $"{source}->{destination}: scheduled {LastScheduledUnits}, delivered {LastDeliveredUnits},{pending}{shipment}. {LastFulfillmentSummary}";
        }

        public string BuildInspectionSummary(string sourceDisplayName = null, string destinationDisplayName = null)
        {
            return $"{BuildStandingTermsSummary()} Last: {BuildLastFulfillmentReadout(sourceDisplayName, destinationDisplayName)}";
        }

        public BusinessTransferAgreementSaveDto CaptureSaveDto()
        {
            return new BusinessTransferAgreementSaveDto
            {
                agreementId = AgreementId,
                sourceBusinessInstanceId = SourceBusinessInstanceId,
                destinationBusinessInstanceId = DestinationBusinessInstanceId,
                sourceCategoryId = SourceCategoryId,
                destinationCategoryId = DestinationCategoryId,
                allocationMode = AllocationMode,
                allocationValue = AllocationValue,
                cadence = Cadence,
                cadenceInterval = CadenceInterval,
                pricingMode = PricingMode,
                priceModifier = PriceModifier,
                sourceReserveUnits = SourceReserveUnits,
                haulingResponsibility = HaulingResponsibility,
                active = Active,
                nextDueWeekKey = NextDueWeekKey,
                lastResolvedWeekKey = LastResolvedWeekKey,
                lastScheduledUnits = LastScheduledUnits,
                lastDeliveredUnits = LastDeliveredUnits,
                lastScheduledTotalPriceCents = LastScheduledTotalPriceCents,
                lastDeliveredTotalPriceCents = LastDeliveredTotalPriceCents,
                lastShipmentId = LastShipmentId,
                lastFulfillmentSummary = LastFulfillmentSummary
            };
        }

        public static BusinessTransferAgreementState FromSaveDto(BusinessTransferAgreementSaveDto dto)
        {
            if (dto == null)
            {
                return new BusinessTransferAgreementState();
            }

            return new BusinessTransferAgreementState
            {
                agreementId = dto.agreementId ?? string.Empty,
                sourceBusinessInstanceId = dto.sourceBusinessInstanceId ?? string.Empty,
                destinationBusinessInstanceId = dto.destinationBusinessInstanceId ?? string.Empty,
                sourceCategoryId = dto.sourceCategoryId ?? string.Empty,
                destinationCategoryId = dto.destinationCategoryId ?? string.Empty,
                allocationMode = dto.allocationMode,
                allocationValue = Mathf.Max(0, dto.allocationValue),
                cadence = dto.cadence,
                cadenceInterval = Mathf.Max(1, dto.cadenceInterval),
                pricingMode = dto.pricingMode,
                priceModifier = SanitizePriceModifier(dto.priceModifier),
                sourceReserveUnits = Mathf.Max(0, dto.sourceReserveUnits),
                haulingResponsibility = dto.haulingResponsibility,
                active = dto.active,
                nextDueWeekKey = dto.nextDueWeekKey,
                lastResolvedWeekKey = dto.lastResolvedWeekKey,
                lastScheduledUnits = Mathf.Max(0, dto.lastScheduledUnits),
                lastDeliveredUnits = Mathf.Max(0, dto.lastDeliveredUnits),
                lastScheduledTotalPriceCents = Mathf.Max(0, dto.lastScheduledTotalPriceCents),
                lastDeliveredTotalPriceCents = Mathf.Max(0, dto.lastDeliveredTotalPriceCents),
                lastShipmentId = dto.lastShipmentId ?? string.Empty,
                lastFulfillmentSummary = string.IsNullOrWhiteSpace(dto.lastFulfillmentSummary)
                    ? "No owned transfers resolved yet."
                    : dto.lastFulfillmentSummary
            };
        }

        private void ScheduleNextDue(int weekKey)
        {
            nextDueWeekKey = weekKey >= 0 ? weekKey + CadenceInterval : nextDueWeekKey;
        }

        private static float SanitizePriceModifier(float modifier)
        {
            if (float.IsNaN(modifier) || float.IsInfinity(modifier))
            {
                return 1f;
            }

            return Mathf.Clamp(modifier, 0f, 4f);
        }
    }
}
