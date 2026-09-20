using System.Collections.Generic;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Economy
{
    public sealed class LocalRecurringOrderManager
    {
        private const float PreferredSupplierUnitPriceMultiplier = 0.92f;
        private const float StrongTermsUnitPriceMultiplier = 0.96f;
        private const float WeakTermsUnitPriceMultiplier = 1.10f;
        private const float StrongTermsReadinessThreshold01 = 0.68f;
        private const float PreferredTermsReadinessThreshold01 = 0.72f;
        private const float WeakTermsReadinessThreshold01 = 0.38f;
        private const float StrainedEmergencyOrderThreshold01 = 0.45f;
        private const float EmergencyStrainSurcharge = 0.05f;
        private const float LowSourcingReliabilityThreshold01 = 0.40f;
        private const float MinimumLowTrustFillRate01 = 0.35f;
        private const float MaximumLowTrustFillRate01 = 0.85f;
        private const int CancelAfterConsecutiveBreachWeeks = 3;
        private const int RenewAfterConsecutiveCleanWeeks = 4;

        private readonly Dictionary<string, LocalRecurringOrderRelationshipState> relationshipsById = new();
        private readonly List<LocalRecurringOrderRelationshipState> relationships = new();
        private readonly List<string> weeklySummarySegments = new();
        private readonly SupplierTrustEvaluator supplierTrustEvaluator = new();

        public IReadOnlyList<LocalRecurringOrderRelationshipState> Relationships => relationships;
        public string LastWeeklySummary { get; private set; } = "No recurring local orders resolved yet.";

        public void Clear()
        {
            relationshipsById.Clear();
            relationships.Clear();
            weeklySummarySegments.Clear();
            LastWeeklySummary = "No recurring local orders resolved yet.";
        }

        public void BeginWeeklyResolution()
        {
            weeklySummarySegments.Clear();
            LastWeeklySummary = "No recurring local orders resolved yet.";
        }

        public void CompleteWeeklyResolution()
        {
            LastWeeklySummary = weeklySummarySegments.Count > 0
                ? "Recurring local orders: " + string.Join("; ", weeklySummarySegments)
                : "No recurring local orders resolved this week.";
        }

        public List<LocalRecurringOrderRelationshipSaveDto> CaptureSaveDtos()
        {
            List<LocalRecurringOrderRelationshipSaveDto> result = new();
            for (int i = 0; i < relationships.Count; i++)
            {
                LocalRecurringOrderRelationshipState relationship = relationships[i];
                if (relationship == null || string.IsNullOrWhiteSpace(relationship.RelationshipId))
                {
                    continue;
                }

                result.Add(relationship.CaptureSaveDto());
            }

            return result;
        }

        public void LoadFromSaveDtos(IReadOnlyList<LocalRecurringOrderRelationshipSaveDto> dtos, string lastWeeklySummary)
        {
            Clear();
            if (dtos != null)
            {
                for (int i = 0; i < dtos.Count; i++)
                {
                    LocalRecurringOrderRelationshipState relationship = LocalRecurringOrderRelationshipState.FromSaveDto(dtos[i]);
                    if (relationship == null || string.IsNullOrWhiteSpace(relationship.RelationshipId))
                    {
                        continue;
                    }

                    if (relationshipsById.ContainsKey(relationship.RelationshipId))
                    {
                        continue;
                    }

                    relationshipsById[relationship.RelationshipId] = relationship;
                    relationships.Add(relationship);
                }
            }

            LastWeeklySummary = string.IsNullOrWhiteSpace(lastWeeklySummary)
                ? "No recurring local orders resolved yet."
                : lastWeeklySummary;
        }

        public LocalRecurringOrderFulfillmentResult ResolveOrder(LocalRecurringOrderFulfillmentRequest request)
        {
            LocalRecurringOrderTemplate template = request.Template;
            BusinessInstanceState seller = request.Seller;
            BusinessInstanceState buyer = request.Buyer;
            if (template == null || seller == null || buyer == null)
            {
                return new LocalRecurringOrderFulfillmentResult(template, null, LocalRecurringOrderFulfillmentStatus.None, 0, 0, 0, "missing order parties");
            }

            LocalRecurringOrderRelationshipState relationship = GetOrCreateRelationship(template, seller, buyer);
            if (!relationship.Active)
            {
                LocalRecurringOrderFulfillmentResult cancelled = new(
                    template,
                    relationship,
                    LocalRecurringOrderFulfillmentStatus.Cancelled,
                    0,
                    0,
                    0,
                    relationship.CancellationReason);
                AppendWeeklySummary(cancelled);
                return cancelled;
            }

            BusinessRuntimeState sellerRuntime = seller.RuntimeState;
            BusinessRuntimeState buyerRuntime = buyer.RuntimeState;
            CategoryStockState buyerStock = buyerRuntime != null ? buyerRuntime.GetCategoryStock(template.BuyerCategoryId) : null;
            int weekKey = request.WeekKey;
            if (!relationship.IsDue(weekKey))
            {
                return new LocalRecurringOrderFulfillmentResult(template, relationship, LocalRecurringOrderFulfillmentStatus.Delayed, 0, 0, 0, "not due");
            }

            if (buyerRuntime == null || buyerStock == null)
            {
                return RecordFailed(relationship, template, weekKey, template.WeeklyTargetUnits, "buyer cannot receive order");
            }

            int buyerNeed = buyerStock.TargetStockUnits > 0
                ? Mathf.Max(0, buyerStock.TargetStockUnits - buyerStock.CurrentStockUnits)
                : Mathf.Max(0, template.WeeklyTargetUnits);
            int requestedUnits = Mathf.Min(Mathf.Max(0, template.WeeklyTargetUnits), buyerNeed);
            if (requestedUnits <= 0)
            {
                relationship.RecordSkippedNoNeed(weekKey);
                LocalRecurringOrderFulfillmentResult skipped = new(
                    template,
                    relationship,
                    LocalRecurringOrderFulfillmentStatus.SkippedNoNeed,
                    0,
                    0,
                    0,
                    "buyer stock full");
                AppendWeeklySummary(skipped);
                return skipped;
            }

            if (!request.BuyerCanReceive)
            {
                return RecordFailed(relationship, template, weekKey, requestedUnits, "buyer cannot receive order");
            }

            if (sellerRuntime == null || !request.SellerCanFulfill)
            {
                return RecordFailed(relationship, template, weekKey, requestedUnits, "seller not operating");
            }

            CategoryStockState sellerStock = sellerRuntime.GetCategoryStock(template.SellerCategoryId);
            if (sellerStock == null || sellerStock.CurrentStockUnits <= 0)
            {
                return RecordFailed(relationship, template, weekKey, requestedUnits, "seller stock unavailable");
            }

            int baseUnitPriceCents = Mathf.Max(0, request.UnitPriceCents);
            if (baseUnitPriceCents <= 0)
            {
                return RecordFailed(relationship, template, weekKey, requestedUnits, "unit price unavailable");
            }

            SupplierTrustStatusSummary supplierStatus = supplierTrustEvaluator.Summarize(relationship.SupplierRelationship);
            int unitPriceCents = CalculateSupplierTrustAdjustedUnitPriceCents(baseUnitPriceCents, supplierStatus);
            int totalUnitCostCents = Mathf.Max(1, unitPriceCents + Mathf.Max(0, request.ExtraBuyerUnitCostCents));
            int spendableCash = Mathf.Max(0, buyerRuntime.CurrentCashCents - Mathf.Max(0, request.BuyerCashReserveCents));
            int affordableUnits = Mathf.Min(requestedUnits, spendableCash / totalUnitCostCents);
            if (affordableUnits < requestedUnits)
            {
                int cashLimitedUnits = Mathf.Min(
                    affordableUnits,
                    CalculateSupplierTrustReliableUnits(requestedUnits, sellerStock.CurrentStockUnits, supplierStatus));
                int acceptedByCash = ApplyTransfer(request, cashLimitedUnits, unitPriceCents, totalUnitCostCents);
                LocalRecurringOrderFulfillmentResult result = RecordBuyerCashBreach(
                    relationship,
                    template,
                    weekKey,
                    requestedUnits,
                    acceptedByCash,
                    acceptedByCash * totalUnitCostCents,
                    BuildFulfillmentReason($"buyer cash/reserve limited {template.BuyerCategoryId}", request.ScheduleShipment != null && acceptedByCash > 0),
                    request.ScheduleShipment != null && acceptedByCash > 0);
                if (acceptedByCash > 0)
                {
                    request.AfterTransferApplied?.Invoke(result);
                }

                return result;
            }

            int transferUnits = CalculateSupplierTrustReliableUnits(requestedUnits, sellerStock.CurrentStockUnits, supplierStatus);
            int accepted = ApplyTransfer(request, transferUnits, unitPriceCents, totalUnitCostCents);
            int paid = accepted * totalUnitCostCents;
            if (accepted >= requestedUnits)
            {
                relationship.RecordClean(weekKey, requestedUnits, accepted, paid);
                relationship.ApplySupplierEvent(supplierTrustEvaluator, SupplierRelationshipEventType.OrderFulfilledClean, weekKey, 1f, template.OrderId);
                relationship.ApplySupplierEvent(supplierTrustEvaluator, SupplierRelationshipEventType.InvoicePaidOnTime, weekKey, 1f, template.OrderId);
                MaybeRenewRelationship(relationship, weekKey);
                bool cleanDeliveryDeferred = request.ScheduleShipment != null && accepted > 0;
                LocalRecurringOrderFulfillmentResult clean = new(
                    template,
                    relationship,
                    LocalRecurringOrderFulfillmentStatus.Clean,
                    requestedUnits,
                    accepted,
                    paid,
                    BuildFulfillmentReason(string.Empty, cleanDeliveryDeferred),
                    cleanDeliveryDeferred);
                request.AfterTransferApplied?.Invoke(clean);
                AppendWeeklySummary(clean);
                return clean;
            }

            if (accepted > 0)
            {
                float severity = requestedUnits <= 0 ? 1f : Mathf.Clamp01((requestedUnits - accepted) / (float)requestedUnits);
                string shortReason = BuildShortFulfillmentReason(template, requestedUnits, sellerStock.CurrentStockUnits, supplierStatus);
                relationship.RecordShort(weekKey, requestedUnits, accepted, paid, shortReason);
                relationship.ApplySupplierEvent(supplierTrustEvaluator, SupplierRelationshipEventType.OrderFulfilledShort, weekKey, severity, template.OrderId);
                MaybeCancelRelationship(relationship, weekKey, shortReason);
                bool shortDeliveryDeferred = request.ScheduleShipment != null && accepted > 0;
                LocalRecurringOrderFulfillmentResult shortResult = new(
                    template,
                    relationship,
                    LocalRecurringOrderFulfillmentStatus.Short,
                    requestedUnits,
                    accepted,
                    paid,
                    BuildFulfillmentReason(relationship.LastBreachReason, shortDeliveryDeferred),
                    shortDeliveryDeferred);
                request.AfterTransferApplied?.Invoke(shortResult);
                AppendWeeklySummary(shortResult);
                return shortResult;
            }

            string finalFailure = request.ScheduleShipment != null && transferUnits > 0
                ? "delivery could not be scheduled"
                : "seller stock unavailable";
            return RecordFailed(relationship, template, weekKey, requestedUnits, finalFailure);
        }

        private LocalRecurringOrderRelationshipState GetOrCreateRelationship(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState seller,
            BusinessInstanceState buyer)
        {
            string relationshipId = LocalRecurringOrderRelationshipState.BuildRelationshipId(template, seller, buyer);
            if (!relationshipsById.TryGetValue(relationshipId, out LocalRecurringOrderRelationshipState relationship) || relationship == null)
            {
                relationship = new LocalRecurringOrderRelationshipState();
                relationshipsById[relationshipId] = relationship;
                relationships.Add(relationship);
            }

            relationship.Configure(template, seller, buyer);
            return relationship;
        }

        private int ApplyTransfer(
            LocalRecurringOrderFulfillmentRequest request,
            int transferUnits,
            int sellerUnitPriceCents,
            int buyerUnitCostCents)
        {
            int requestedUnits = Mathf.Max(0, transferUnits);
            if (requestedUnits <= 0
                || request.Seller == null
                || request.Seller.RuntimeState == null
                || request.Buyer == null
                || request.Buyer.RuntimeState == null
                || request.Template == null)
            {
                return 0;
            }

            LocalRecurringOrderTemplate template = request.Template;
            if (!request.Seller.RuntimeState.TryConsumeCategoryStockUnits(template.SellerCategoryId, requestedUnits, out int consumed) || consumed <= 0)
            {
                return 0;
            }

            int accepted = request.ScheduleShipment != null
                ? request.ScheduleShipment(request, consumed, sellerUnitPriceCents, buyerUnitCostCents)
                : request.ReceiveBuyerSupply != null
                ? request.ReceiveBuyerSupply(template.BuyerCategoryId, consumed, sellerUnitPriceCents, request.Seller.RuntimeDisplayName, request.BuyerCashReserveCents)
                : request.Buyer.RuntimeState.AddCategoryStockUnits(template.BuyerCategoryId, consumed);
            accepted = Mathf.Clamp(accepted, 0, consumed);

            if (accepted < consumed)
            {
                request.Seller.RuntimeState.AddCategoryStockUnits(template.SellerCategoryId, consumed - accepted);
            }

            int sellerRevenue = accepted * sellerUnitPriceCents;
            int buyerCost = accepted * buyerUnitCostCents;
            bool deliveryDeferred = request.ScheduleShipment != null;
            if (accepted > 0 && sellerRevenue > 0 && !deliveryDeferred)
            {
                request.Seller.RuntimeState.AddWeeklyLocalTransferRevenue(
                    sellerRevenue,
                    $"recurring order sold {accepted} {template.SellerCategoryId} to {request.Buyer.RuntimeDisplayName} for {FormatMoney(sellerRevenue)}");
            }

            if (accepted > 0 && buyerCost > 0 && request.ReceiveBuyerSupply == null && !deliveryDeferred)
            {
                request.Buyer.RuntimeState.AddWeeklyLocalTransferCost(
                    buyerCost,
                    $"recurring order bought {accepted} {template.BuyerCategoryId} from {request.Seller.RuntimeDisplayName} for {FormatMoney(buyerCost)}");
            }

            request.Seller.RefreshCapacityState();
            request.Buyer.RefreshCapacityState();
            return accepted;
        }

        private static string BuildFulfillmentReason(string baseReason, bool deliveryDeferred)
        {
            if (!deliveryDeferred)
            {
                return baseReason ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(baseReason)
                ? "delivery scheduled"
                : $"{baseReason}; delivery scheduled";
        }

        private LocalRecurringOrderFulfillmentResult RecordFailed(
            LocalRecurringOrderRelationshipState relationship,
            LocalRecurringOrderTemplate template,
            int weekKey,
            int requestedUnits,
            string reason)
        {
            relationship.RecordFailed(weekKey, requestedUnits, reason);
            relationship.ApplySupplierEvent(supplierTrustEvaluator, SupplierRelationshipEventType.OrderFailed, weekKey, 1f, template != null ? template.OrderId : string.Empty);
            MaybeCancelRelationship(relationship, weekKey, reason);
            LocalRecurringOrderFulfillmentResult result = new(template, relationship, LocalRecurringOrderFulfillmentStatus.Failed, requestedUnits, 0, 0, reason);
            AppendWeeklySummary(result);
            return result;
        }

        private LocalRecurringOrderFulfillmentResult RecordBuyerCashBreach(
            LocalRecurringOrderRelationshipState relationship,
            LocalRecurringOrderTemplate template,
            int weekKey,
            int requestedUnits,
            int fulfilledUnits,
            int paidCents,
            string reason,
            bool deliveryDeferred = false)
        {
            relationship.RecordBuyerCashBreach(weekKey, requestedUnits, fulfilledUnits, paidCents, reason);
            relationship.ApplySupplierEvent(supplierTrustEvaluator, SupplierRelationshipEventType.InvoiceMissed, weekKey, 1f, template != null ? template.OrderId : string.Empty);
            MaybeCancelRelationship(relationship, weekKey, reason);
            LocalRecurringOrderFulfillmentResult result = new(template, relationship, LocalRecurringOrderFulfillmentStatus.BuyerCashBreach, requestedUnits, fulfilledUnits, paidCents, reason, deliveryDeferred);
            AppendWeeklySummary(result);
            return result;
        }

        private static void MaybeRenewRelationship(LocalRecurringOrderRelationshipState relationship, int weekKey)
        {
            if (relationship == null || !relationship.Active)
            {
                return;
            }

            if (relationship.ConsecutiveCleanFulfillmentWeeks > 0
                && relationship.ConsecutiveCleanFulfillmentWeeks % RenewAfterConsecutiveCleanWeeks == 0)
            {
                relationship.RecordRenewed(weekKey);
            }
        }

        private static void MaybeCancelRelationship(LocalRecurringOrderRelationshipState relationship, int weekKey, string reason)
        {
            if (relationship == null || !relationship.Active)
            {
                return;
            }

            if (relationship.ConsecutiveBreachWeeks >= CancelAfterConsecutiveBreachWeeks
                || relationship.RelationshipHealth01 <= 0.12f)
            {
                relationship.RecordCancelled(weekKey, reason);
            }
        }

        private void AppendWeeklySummary(LocalRecurringOrderFulfillmentResult result)
        {
            if (result.Template == null || result.Relationship == null)
            {
                return;
            }

            if (result.Status == LocalRecurringOrderFulfillmentStatus.SkippedNoNeed)
            {
                return;
            }

            string status = result.Status switch
            {
                LocalRecurringOrderFulfillmentStatus.Clean => "clean",
                LocalRecurringOrderFulfillmentStatus.Short => "short",
                LocalRecurringOrderFulfillmentStatus.Failed => "failed",
                LocalRecurringOrderFulfillmentStatus.BuyerCashBreach => "buyer cash breach",
                LocalRecurringOrderFulfillmentStatus.Renewed => "renewed",
                LocalRecurringOrderFulfillmentStatus.Cancelled => "cancelled",
                LocalRecurringOrderFulfillmentStatus.Delayed => "delayed",
                _ => "resolved"
            };
            if (result.HasScheduledShipment)
            {
                status = status == "clean" ? "scheduled" : $"{status} scheduled";
            }

            string reason = string.IsNullOrWhiteSpace(result.Reason) ? string.Empty : $" ({result.Reason})";
            weeklySummarySegments.Add(
                $"{result.Relationship.SellerDisplayName}->{result.Relationship.BuyerDisplayName} {result.FulfilledUnits}/{result.RequestedUnits} {result.Template.BuyerCategoryId} {status}, health {Mathf.RoundToInt(result.Relationship.RelationshipHealth01 * 100f)}{reason}");
        }

        private static int CalculateSupplierTrustAdjustedUnitPriceCents(int baseUnitPriceCents, SupplierTrustStatusSummary supplierStatus)
        {
            int basePrice = Mathf.Max(0, baseUnitPriceCents);
            if (basePrice <= 0)
            {
                return 0;
            }

            float multiplier = 1f;
            if (supplierStatus.Preferred || supplierStatus.TermsReadiness01 >= PreferredTermsReadinessThreshold01)
            {
                multiplier = PreferredSupplierUnitPriceMultiplier;
            }
            else if (supplierStatus.TermsReadiness01 >= StrongTermsReadinessThreshold01)
            {
                multiplier = StrongTermsUnitPriceMultiplier;
            }
            else if (supplierStatus.TermsReadiness01 <= WeakTermsReadinessThreshold01)
            {
                multiplier = WeakTermsUnitPriceMultiplier;
            }

            if (!supplierStatus.Preferred && supplierStatus.EmergencyOrderStrain01 >= StrainedEmergencyOrderThreshold01)
            {
                multiplier += EmergencyStrainSurcharge;
            }

            return Mathf.Max(1, Mathf.RoundToInt(basePrice * multiplier));
        }

        private static int CalculateSupplierTrustReliableUnits(
            int requestedUnits,
            int sellerStockUnits,
            SupplierTrustStatusSummary supplierStatus)
        {
            int requested = Mathf.Max(0, requestedUnits);
            int stocked = Mathf.Max(0, sellerStockUnits);
            int available = Mathf.Min(requested, stocked);
            if (available <= 0)
            {
                return 0;
            }

            if (supplierStatus.SourcingReliability01 >= LowSourcingReliabilityThreshold01)
            {
                return available;
            }

            float trustT = Mathf.Clamp01(supplierStatus.SourcingReliability01 / LowSourcingReliabilityThreshold01);
            float fillRate = Mathf.Lerp(MinimumLowTrustFillRate01, MaximumLowTrustFillRate01, trustT);
            int reliableUnits = Mathf.FloorToInt(requested * fillRate);
            return Mathf.Clamp(reliableUnits, 1, available);
        }

        private static string BuildShortFulfillmentReason(
            LocalRecurringOrderTemplate template,
            int requestedUnits,
            int sellerStockUnits,
            SupplierTrustStatusSummary supplierStatus)
        {
            if (Mathf.Max(0, sellerStockUnits) >= Mathf.Max(0, requestedUnits)
                && supplierStatus.SourcingReliability01 < LowSourcingReliabilityThreshold01)
            {
                return $"supplier trust limited {template.SellerCategoryId}";
            }

            return $"seller short {template.SellerCategoryId}";
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (cents / 100f).ToString("N2");
        }
    }
}
