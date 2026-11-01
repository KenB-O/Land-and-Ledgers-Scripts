using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum LocalRecurringOrderFulfillmentStatus
    {
        None = 0,
        SkippedNoNeed = 1,
        Clean = 2,
        Short = 3,
        Failed = 4,
        BuyerCashBreach = 5,
        Fulfilled = Clean,
        PartiallyFulfilled = Short,
        Delayed = 6,
        Breached = Failed,
        Renewed = 7,
        Cancelled = 8
    }

    public enum LocalRecurringOrderPriceMode
    {
        LocalWholesale = 0,
        PreferredRelationship = 1,
        EmergencySpot = 2
    }

    public enum LocalRecurringOrderHaulingResponsibility
    {
        Seller = 0,
        Buyer = 1,
        Shared = 2
    }

    public sealed class LocalRecurringOrderTemplate
    {
        public LocalRecurringOrderTemplate(
            string orderId,
            BusinessType sellerType,
            string sellerCategoryId,
            BusinessType buyerType,
            string buyerCategoryId,
            int weeklyTargetUnits,
            int cadenceWeeks = 1,
            LocalRecurringOrderPriceMode priceMode = LocalRecurringOrderPriceMode.LocalWholesale,
            LocalRecurringOrderHaulingResponsibility haulingResponsibility = LocalRecurringOrderHaulingResponsibility.Seller)
        {
            OrderId = string.IsNullOrWhiteSpace(orderId)
                ? $"{sellerType}_{sellerCategoryId}_to_{buyerType}_{buyerCategoryId}"
                : orderId;
            SellerType = sellerType;
            SellerCategoryId = sellerCategoryId ?? string.Empty;
            BuyerType = buyerType;
            BuyerCategoryId = buyerCategoryId ?? string.Empty;
            WeeklyTargetUnits = Mathf.Max(0, weeklyTargetUnits);
            CadenceWeeks = Mathf.Max(1, cadenceWeeks);
            PriceMode = priceMode;
            HaulingResponsibility = haulingResponsibility;
        }

        public string OrderId { get; }
        public BusinessType SellerType { get; }
        public string SellerCategoryId { get; }
        public BusinessType BuyerType { get; }
        public string BuyerCategoryId { get; }
        public int WeeklyTargetUnits { get; }
        public int CadenceWeeks { get; }
        public LocalRecurringOrderPriceMode PriceMode { get; }
        public LocalRecurringOrderHaulingResponsibility HaulingResponsibility { get; }

        public static IReadOnlyList<LocalRecurringOrderTemplate> CreateDefaultTemplates()
        {
            return new[]
            {
                new LocalRecurringOrderTemplate("crop_food_to_ranch_feed", BusinessType.CropFarm, "crop_food", BusinessType.Ranch, "feed_inputs", 6),
                new LocalRecurringOrderTemplate("ranch_livestock_to_butcher", BusinessType.Ranch, "livestock_inputs", BusinessType.Butcher, "livestock_inputs", 4),
                new LocalRecurringOrderTemplate("crop_food_to_general_store", BusinessType.CropFarm, "crop_food", BusinessType.GeneralStore, "staple_food", 10),
                new LocalRecurringOrderTemplate("crop_food_to_livery_upkeep", BusinessType.CropFarm, "crop_food", BusinessType.LiveryFreight, "livery_feed_upkeep", 6),
                new LocalRecurringOrderTemplate("butcher_meat_to_general_store", BusinessType.Butcher, "meat", BusinessType.GeneralStore, "meat", 8),
                new LocalRecurringOrderTemplate("blacksmith_hardware_to_general_store", BusinessType.Blacksmith, "tools_hardware", BusinessType.GeneralStore, "tools_hardware", 6),
                new LocalRecurringOrderTemplate("sawmill_lumber_to_lumber_yard", BusinessType.Sawmill, "lumber", BusinessType.LumberYard, "lumber", 80),
                new LocalRecurringOrderTemplate("crop_food_to_grain_mill", BusinessType.CropFarm, "crop_food", BusinessType.GrainMill, "grain", 12),
                new LocalRecurringOrderTemplate("grain_mill_flour_to_bakery", BusinessType.GrainMill, "flour", BusinessType.Bakery, "flour", 10),
                new LocalRecurringOrderTemplate("grain_mill_flour_to_general_store", BusinessType.GrainMill, "flour", BusinessType.GeneralStore, "staple_food", 8),
                new LocalRecurringOrderTemplate("bakery_bread_to_general_store", BusinessType.Bakery, "bread", BusinessType.GeneralStore, "staple_food", 8),
                new LocalRecurringOrderTemplate("crop_food_to_boarding_house", BusinessType.CropFarm, "crop_food", BusinessType.BoardingHouse, "staple_food", 6),
                new LocalRecurringOrderTemplate("bakery_bread_to_boarding_house", BusinessType.Bakery, "bread", BusinessType.BoardingHouse, "staple_food", 6),
                new LocalRecurringOrderTemplate("butcher_meat_to_boarding_house", BusinessType.Butcher, "meat", BusinessType.BoardingHouse, "meat", 5),
                new LocalRecurringOrderTemplate("butcher_meat_to_saloon", BusinessType.Butcher, "meat", BusinessType.Saloon, "meal_inputs", 4),
                new LocalRecurringOrderTemplate("bakery_bread_to_saloon", BusinessType.Bakery, "bread", BusinessType.Saloon, "meal_inputs", 4),
                new LocalRecurringOrderTemplate("sawmill_offcuts_to_fuel_dealer", BusinessType.Sawmill, "slabs_offcuts", BusinessType.FuelDealer, "slabs_offcuts", 10),
                new LocalRecurringOrderTemplate("fuel_dealer_to_boarding_house", BusinessType.FuelDealer, "fuel_wood", BusinessType.BoardingHouse, "fuel_wood", 6),
                new LocalRecurringOrderTemplate("fuel_dealer_to_bakery", BusinessType.FuelDealer, "fuel_wood", BusinessType.Bakery, "fuel_wood", 4),
                new LocalRecurringOrderTemplate("fuel_dealer_to_saloon", BusinessType.FuelDealer, "fuel_wood", BusinessType.Saloon, "fuel_wood", 4),
                new LocalRecurringOrderTemplate("lumber_yard_lumber_to_builder", BusinessType.LumberYard, "lumber", BusinessType.Builder, "lumber", 10),
                new LocalRecurringOrderTemplate("blacksmith_hardware_to_builder", BusinessType.Blacksmith, "tools_hardware", BusinessType.Builder, "tools_hardware", 4),
                new LocalRecurringOrderTemplate("lumber_yard_lumber_to_wheelwright", BusinessType.LumberYard, "lumber", BusinessType.Wheelwright, "repair_inputs", 6),
                new LocalRecurringOrderTemplate("blacksmith_hardware_to_wheelwright", BusinessType.Blacksmith, "tools_hardware", BusinessType.Wheelwright, "repair_inputs", 4),
                new LocalRecurringOrderTemplate("general_store_goods_to_tailor", BusinessType.GeneralStore, "household_goods", BusinessType.Tailor, "cloth_notions", 4),
                new LocalRecurringOrderTemplate("general_store_staples_to_boarding_house", BusinessType.GeneralStore, "staple_food", BusinessType.BoardingHouse, "staple_food", 6),
                new LocalRecurringOrderTemplate("general_store_staples_to_saloon", BusinessType.GeneralStore, "staple_food", BusinessType.Saloon, "meal_inputs", 4),
                new LocalRecurringOrderTemplate("general_store_hardware_to_builder", BusinessType.GeneralStore, "tools_hardware", BusinessType.Builder, "tools_hardware", 3),
                new LocalRecurringOrderTemplate("general_store_remedies_to_doctor", BusinessType.GeneralStore, "medicine_remedies", BusinessType.Doctor, "medicine_remedies", 3),
                new LocalRecurringOrderTemplate("tailor_clothing_to_general_store", BusinessType.Tailor, "clothing", BusinessType.GeneralStore, "clothing", 4),
                new LocalRecurringOrderTemplate("general_store_goods_to_barber", BusinessType.GeneralStore, "household_goods", BusinessType.Barber, "barber_consumables", 3),
                new LocalRecurringOrderTemplate("wheelwright_repairs_to_livery", BusinessType.Wheelwright, "wheelwright_repairs", BusinessType.LiveryFreight, "wheelwright_repairs", 3)
            };
        }
    }

    [Serializable]
    public sealed class LocalRecurringOrderRelationshipState
    {
        [SerializeField]
        private string relationshipId = string.Empty;

        [SerializeField]
        private string orderId = string.Empty;

        [SerializeField]
        private string sellerInstanceId = string.Empty;

        [SerializeField]
        private string buyerInstanceId = string.Empty;

        [SerializeField]
        private string sellerDisplayName = string.Empty;

        [SerializeField]
        private string buyerDisplayName = string.Empty;

        [SerializeField]
        private string sellerCategoryId = string.Empty;

        [SerializeField]
        private string buyerCategoryId = string.Empty;

        [SerializeField]
        private int targetQuantity;

        [SerializeField]
        private int cadenceWeeks = 1;

        [SerializeField]
        private int nextDueWeekKey = -1;

        [SerializeField]
        private LocalRecurringOrderPriceMode priceMode;

        [SerializeField]
        private LocalRecurringOrderHaulingResponsibility haulingResponsibility;

        [SerializeField]
        private float relationshipHealth01 = 0.5f;

        [SerializeField]
        private int renewalCount;

        [SerializeField]
        private bool active = true;

        [SerializeField]
        private string cancellationReason = string.Empty;

        [SerializeField]
        private int lastRequestedUnits;

        [SerializeField]
        private int lastFulfilledUnits;

        [SerializeField]
        private int lastPaidCents;

        [SerializeField]
        private int lastResolvedWeekKey = -1;

        [SerializeField]
        private int cleanFulfillmentCount;

        [SerializeField]
        private int shortFulfillmentCount;

        [SerializeField]
        private int failedFulfillmentCount;

        [SerializeField]
        private int buyerCashBreachCount;

        [SerializeField]
        private int consecutiveCleanFulfillmentWeeks;

        [SerializeField]
        private int consecutiveBreachWeeks;

        [SerializeField]
        private string lastBreachReason = string.Empty;

        [SerializeField]
        private LocalRecurringOrderFulfillmentStatus lastStatus;

        [SerializeField]
        private SupplierRelationshipState supplierRelationship = new();

        public string RelationshipId => relationshipId ?? string.Empty;
        public string OrderId => orderId ?? string.Empty;
        public string SellerInstanceId => sellerInstanceId ?? string.Empty;
        public string BuyerInstanceId => buyerInstanceId ?? string.Empty;
        public string SellerDisplayName => sellerDisplayName ?? string.Empty;
        public string BuyerDisplayName => buyerDisplayName ?? string.Empty;
        public string SellerCategoryId => sellerCategoryId ?? string.Empty;
        public string BuyerCategoryId => buyerCategoryId ?? string.Empty;
        public int TargetQuantity => Mathf.Max(0, targetQuantity);
        public int CadenceWeeks => Mathf.Max(1, cadenceWeeks);
        public int NextDueWeekKey => nextDueWeekKey;
        public LocalRecurringOrderPriceMode PriceMode => priceMode;
        public LocalRecurringOrderHaulingResponsibility HaulingResponsibility => haulingResponsibility;
        public float RelationshipHealth01 => Mathf.Clamp01(relationshipHealth01);
        public int RenewalCount => Mathf.Max(0, renewalCount);
        public bool Active => active;
        public string CancellationReason => cancellationReason ?? string.Empty;
        public int LastRequestedUnits => Mathf.Max(0, lastRequestedUnits);
        public int LastFulfilledUnits => Mathf.Max(0, lastFulfilledUnits);
        public int LastPaidCents => Mathf.Max(0, lastPaidCents);
        public int LastResolvedWeekKey => lastResolvedWeekKey;
        public int CleanFulfillmentCount => Mathf.Max(0, cleanFulfillmentCount);
        public int ShortFulfillmentCount => Mathf.Max(0, shortFulfillmentCount);
        public int FailedFulfillmentCount => Mathf.Max(0, failedFulfillmentCount);
        public int BuyerCashBreachCount => Mathf.Max(0, buyerCashBreachCount);
        public int ConsecutiveCleanFulfillmentWeeks => Mathf.Max(0, consecutiveCleanFulfillmentWeeks);
        public int ConsecutiveBreachWeeks => Mathf.Max(0, consecutiveBreachWeeks);
        public string LastBreachReason => lastBreachReason ?? string.Empty;
        public LocalRecurringOrderFulfillmentStatus LastStatus => lastStatus;
        public SupplierRelationshipState SupplierRelationship => supplierRelationship ??= new SupplierRelationshipState();

        internal static string BuildRelationshipId(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState seller,
            BusinessInstanceState buyer)
        {
            string order = template != null ? template.OrderId : "local_order";
            string sellerId = seller != null ? seller.InstanceId : "seller";
            string buyerId = buyer != null ? buyer.InstanceId : "buyer";
            return $"{order}:{sellerId}->{buyerId}";
        }

        internal void Configure(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState seller,
            BusinessInstanceState buyer)
        {
            relationshipId = BuildRelationshipId(template, seller, buyer);
            orderId = template != null ? template.OrderId : string.Empty;
            sellerInstanceId = seller != null ? seller.InstanceId : string.Empty;
            buyerInstanceId = buyer != null ? buyer.InstanceId : string.Empty;
            sellerDisplayName = seller != null ? seller.RuntimeDisplayName : string.Empty;
            buyerDisplayName = buyer != null ? buyer.RuntimeDisplayName : string.Empty;
            sellerCategoryId = template != null ? template.SellerCategoryId : string.Empty;
            buyerCategoryId = template != null ? template.BuyerCategoryId : string.Empty;
            targetQuantity = template != null ? template.WeeklyTargetUnits : targetQuantity;
            cadenceWeeks = template != null ? template.CadenceWeeks : Mathf.Max(1, cadenceWeeks);
            priceMode = template != null ? template.PriceMode : priceMode;
            haulingResponsibility = template != null ? template.HaulingResponsibility : haulingResponsibility;
            if (relationshipHealth01 <= 0f)
            {
                relationshipHealth01 = 0.5f;
            }

            active = string.IsNullOrWhiteSpace(cancellationReason);
            EnsureSupplierRelationship();
        }

        internal bool IsDue(int weekKey)
        {
            return active && (nextDueWeekKey < 0 || weekKey >= nextDueWeekKey);
        }

        internal void RecordSkippedNoNeed(int weekKey)
        {
            lastRequestedUnits = 0;
            lastFulfilledUnits = 0;
            lastPaidCents = 0;
            lastResolvedWeekKey = weekKey;
            lastBreachReason = string.Empty;
            lastStatus = LocalRecurringOrderFulfillmentStatus.SkippedNoNeed;
            ScheduleNextDue(weekKey);
        }

        internal void RecordClean(int weekKey, int requestedUnits, int fulfilledUnits, int paidCents)
        {
            RecordCommon(weekKey, requestedUnits, fulfilledUnits, paidCents, LocalRecurringOrderFulfillmentStatus.Clean, string.Empty);
            cleanFulfillmentCount++;
            consecutiveCleanFulfillmentWeeks++;
            consecutiveBreachWeeks = 0;
            relationshipHealth01 = Mathf.Clamp01(relationshipHealth01 + 0.05f);
            ScheduleNextDue(weekKey);
        }

        internal void RecordShort(int weekKey, int requestedUnits, int fulfilledUnits, int paidCents, string reason)
        {
            RecordCommon(weekKey, requestedUnits, fulfilledUnits, paidCents, LocalRecurringOrderFulfillmentStatus.Short, reason);
            shortFulfillmentCount++;
            consecutiveCleanFulfillmentWeeks = 0;
            consecutiveBreachWeeks++;
            relationshipHealth01 = Mathf.Clamp01(relationshipHealth01 - 0.09f);
            ScheduleNextDue(weekKey);
        }

        internal void RecordFailed(int weekKey, int requestedUnits, string reason)
        {
            RecordCommon(weekKey, requestedUnits, 0, 0, LocalRecurringOrderFulfillmentStatus.Failed, reason);
            failedFulfillmentCount++;
            consecutiveCleanFulfillmentWeeks = 0;
            consecutiveBreachWeeks++;
            relationshipHealth01 = Mathf.Clamp01(relationshipHealth01 - 0.14f);
            ScheduleNextDue(weekKey);
        }

        internal void RecordBuyerCashBreach(int weekKey, int requestedUnits, int fulfilledUnits, int paidCents, string reason)
        {
            RecordCommon(weekKey, requestedUnits, fulfilledUnits, paidCents, LocalRecurringOrderFulfillmentStatus.BuyerCashBreach, reason);
            buyerCashBreachCount++;
            consecutiveCleanFulfillmentWeeks = 0;
            consecutiveBreachWeeks++;
            relationshipHealth01 = Mathf.Clamp01(relationshipHealth01 - 0.11f);
            ScheduleNextDue(weekKey);
        }

        internal void RecordRenewed(int weekKey)
        {
            renewalCount++;
            lastResolvedWeekKey = weekKey;
            lastStatus = LocalRecurringOrderFulfillmentStatus.Renewed;
            relationshipHealth01 = Mathf.Clamp01(relationshipHealth01 + 0.04f);
            ScheduleNextDue(weekKey);
        }

        internal void RecordCancelled(int weekKey, string reason)
        {
            active = false;
            cancellationReason = string.IsNullOrWhiteSpace(reason) ? "relationship cancelled" : reason.Trim();
            lastResolvedWeekKey = weekKey;
            lastStatus = LocalRecurringOrderFulfillmentStatus.Cancelled;
            lastBreachReason = cancellationReason;
            nextDueWeekKey = -1;
            relationshipHealth01 = Mathf.Clamp01(relationshipHealth01 - 0.2f);
        }

        internal void ApplySupplierEvent(SupplierTrustEvaluator evaluator, SupplierRelationshipEventType eventType, int weekKey, float severity01, string note)
        {
            EnsureSupplierRelationship();
            if (evaluator == null || eventType == SupplierRelationshipEventType.None)
            {
                return;
            }

            supplierRelationship = evaluator.BuildUpdatedState(
                supplierRelationship,
                new SupplierRelationshipEvent
                {
                    supplierId = SellerInstanceId,
                    eventType = eventType,
                    absoluteDayIndex = weekKey >= 0 ? weekKey * 7 : -1,
                    severity01 = Mathf.Clamp01(severity01 <= 0f ? 1f : severity01),
                    note = note ?? string.Empty
                });
            supplierRelationship.displayName = SellerDisplayName;
            supplierRelationship.originKind = SupplierOriginKind.LocalTown;
        }

        internal LocalRecurringOrderRelationshipSaveDto CaptureSaveDto()
        {
            EnsureSupplierRelationship();
            return new LocalRecurringOrderRelationshipSaveDto
            {
                relationshipId = RelationshipId,
                orderId = OrderId,
                sellerInstanceId = SellerInstanceId,
                buyerInstanceId = BuyerInstanceId,
                sellerDisplayName = SellerDisplayName,
                buyerDisplayName = BuyerDisplayName,
                sellerCategoryId = SellerCategoryId,
                buyerCategoryId = BuyerCategoryId,
                targetQuantity = TargetQuantity,
                cadenceWeeks = CadenceWeeks,
                nextDueWeekKey = NextDueWeekKey,
                priceMode = PriceMode,
                haulingResponsibility = HaulingResponsibility,
                relationshipHealth01 = RelationshipHealth01,
                renewalCount = RenewalCount,
                active = Active,
                cancellationReason = CancellationReason,
                lastRequestedUnits = LastRequestedUnits,
                lastFulfilledUnits = LastFulfilledUnits,
                lastPaidCents = LastPaidCents,
                lastResolvedWeekKey = LastResolvedWeekKey,
                cleanFulfillmentCount = CleanFulfillmentCount,
                shortFulfillmentCount = ShortFulfillmentCount,
                failedFulfillmentCount = FailedFulfillmentCount,
                buyerCashBreachCount = BuyerCashBreachCount,
                consecutiveCleanFulfillmentWeeks = ConsecutiveCleanFulfillmentWeeks,
                consecutiveBreachWeeks = ConsecutiveBreachWeeks,
                lastBreachReason = LastBreachReason,
                lastStatus = LastStatus,
                supplierRelationship = SupplierRelationship.SanitizedCopy()
            };
        }

        internal static LocalRecurringOrderRelationshipState FromSaveDto(LocalRecurringOrderRelationshipSaveDto dto)
        {
            LocalRecurringOrderRelationshipState state = new();
            if (dto == null)
            {
                state.EnsureSupplierRelationship();
                return state;
            }

            state.relationshipId = dto.relationshipId ?? string.Empty;
            state.orderId = dto.orderId ?? string.Empty;
            state.sellerInstanceId = dto.sellerInstanceId ?? string.Empty;
            state.buyerInstanceId = dto.buyerInstanceId ?? string.Empty;
            state.sellerDisplayName = dto.sellerDisplayName ?? string.Empty;
            state.buyerDisplayName = dto.buyerDisplayName ?? string.Empty;
            state.sellerCategoryId = dto.sellerCategoryId ?? string.Empty;
            state.buyerCategoryId = dto.buyerCategoryId ?? string.Empty;
            state.targetQuantity = Mathf.Max(0, dto.targetQuantity);
            state.cadenceWeeks = Mathf.Max(1, dto.cadenceWeeks);
            state.nextDueWeekKey = dto.nextDueWeekKey;
            state.priceMode = dto.priceMode;
            state.haulingResponsibility = dto.haulingResponsibility;
            state.relationshipHealth01 = Mathf.Clamp01(dto.relationshipHealth01 <= 0f ? 0.5f : dto.relationshipHealth01);
            state.renewalCount = Mathf.Max(0, dto.renewalCount);
            state.active = dto.active || string.IsNullOrWhiteSpace(dto.cancellationReason);
            state.cancellationReason = dto.cancellationReason ?? string.Empty;
            state.lastRequestedUnits = Mathf.Max(0, dto.lastRequestedUnits);
            state.lastFulfilledUnits = Mathf.Max(0, dto.lastFulfilledUnits);
            state.lastPaidCents = Mathf.Max(0, dto.lastPaidCents);
            state.lastResolvedWeekKey = dto.lastResolvedWeekKey;
            state.cleanFulfillmentCount = Mathf.Max(0, dto.cleanFulfillmentCount);
            state.shortFulfillmentCount = Mathf.Max(0, dto.shortFulfillmentCount);
            state.failedFulfillmentCount = Mathf.Max(0, dto.failedFulfillmentCount);
            state.buyerCashBreachCount = Mathf.Max(0, dto.buyerCashBreachCount);
            state.consecutiveCleanFulfillmentWeeks = Mathf.Max(0, dto.consecutiveCleanFulfillmentWeeks);
            state.consecutiveBreachWeeks = Mathf.Max(0, dto.consecutiveBreachWeeks);
            state.lastBreachReason = dto.lastBreachReason ?? string.Empty;
            state.lastStatus = dto.lastStatus;
            state.supplierRelationship = dto.supplierRelationship != null
                ? dto.supplierRelationship.SanitizedCopy()
                : new SupplierRelationshipState();
            state.EnsureSupplierRelationship();
            return state;
        }

        private void RecordCommon(
            int weekKey,
            int requestedUnits,
            int fulfilledUnits,
            int paidCents,
            LocalRecurringOrderFulfillmentStatus status,
            string breachReason)
        {
            lastRequestedUnits = Mathf.Max(0, requestedUnits);
            lastFulfilledUnits = Mathf.Max(0, fulfilledUnits);
            lastPaidCents = Mathf.Max(0, paidCents);
            lastResolvedWeekKey = weekKey;
            lastStatus = status;
            lastBreachReason = breachReason ?? string.Empty;
        }

        private void ScheduleNextDue(int weekKey)
        {
            nextDueWeekKey = weekKey >= 0 ? weekKey + CadenceWeeks : nextDueWeekKey;
        }

        private void EnsureSupplierRelationship()
        {
            supplierRelationship ??= new SupplierRelationshipState();
            if (string.IsNullOrWhiteSpace(supplierRelationship.supplierId))
            {
                supplierRelationship.supplierId = SellerInstanceId;
            }

            supplierRelationship.displayName = SellerDisplayName;
            supplierRelationship.originKind = SupplierOriginKind.LocalTown;
            supplierRelationship.trust01 = Mathf.Clamp01(supplierRelationship.trust01);
            supplierRelationship.reliability01 = Mathf.Clamp01(supplierRelationship.reliability01);
            supplierRelationship.paymentHistory01 = Mathf.Clamp01(supplierRelationship.paymentHistory01);
        }
    }

    public struct LocalRecurringOrderFulfillmentRequest
    {
        public LocalRecurringOrderTemplate Template;
        public BusinessInstanceState Seller;
        public BusinessInstanceState Buyer;
        public int WeekKey;
        public int UnitPriceCents;
        public int ExtraBuyerUnitCostCents;
        public int BuyerCashReserveCents;
        public bool SellerCanFulfill;
        public bool BuyerCanReceive;
        public Func<string, int, int, string, int, int> ReceiveBuyerSupply;
        public Func<LocalRecurringOrderFulfillmentRequest, int, int, int, int> ScheduleShipment;
        public Action<LocalRecurringOrderFulfillmentResult> AfterTransferApplied;
    }

    public readonly struct LocalRecurringOrderFulfillmentResult
    {
        public LocalRecurringOrderFulfillmentResult(
            LocalRecurringOrderTemplate template,
            LocalRecurringOrderRelationshipState relationship,
            LocalRecurringOrderFulfillmentStatus status,
            int requestedUnits,
            int fulfilledUnits,
            int paidCents,
            string reason,
            bool deliveryDeferred = false)
        {
            Template = template;
            Relationship = relationship;
            Status = status;
            RequestedUnits = Mathf.Max(0, requestedUnits);
            FulfilledUnits = Mathf.Max(0, fulfilledUnits);
            PaidCents = Mathf.Max(0, paidCents);
            Reason = reason ?? string.Empty;
            DeliveryDeferred = deliveryDeferred && FulfilledUnits > 0;
        }

        public LocalRecurringOrderTemplate Template { get; }
        public LocalRecurringOrderRelationshipState Relationship { get; }
        public LocalRecurringOrderFulfillmentStatus Status { get; }
        public int RequestedUnits { get; }
        public int FulfilledUnits { get; }
        public int PaidCents { get; }
        public string Reason { get; }
        public bool DeliveryDeferred { get; }
        public bool HasScheduledShipment => DeliveryDeferred && FulfilledUnits > 0;
        public bool HasDeliveredTransfer => !DeliveryDeferred && FulfilledUnits > 0 && PaidCents > 0;
        public bool HasTransfer => HasDeliveredTransfer || HasScheduledShipment;
        public bool IsBreach => Status == LocalRecurringOrderFulfillmentStatus.Short
            || Status == LocalRecurringOrderFulfillmentStatus.Failed
            || Status == LocalRecurringOrderFulfillmentStatus.BuyerCashBreach;
    }
}
