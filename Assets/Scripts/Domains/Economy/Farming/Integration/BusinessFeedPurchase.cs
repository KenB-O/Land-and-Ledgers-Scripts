using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.Economy.Farming.Integration
{
    /// <summary>
    /// A counter purchase by one business from a staffed General Store. The
    /// named source-lot splits remain in the shopper's custody batch until the
    /// return leg succeeds and ReceiveAtBusiness is called.
    /// </summary>
    public static class BusinessFeedPurchase
    {
        /// <summary>
        /// Whole packages needed to reach a reserve target. Existing feed units
        /// include saved partial-package carry; packages are rounded up at the
        /// explicit care-unit conversion boundary.
        /// </summary>
        public static int PackagesToTarget(int availableFeedUnits, int dailyFeedUnitsRequired,
            int targetDays, int feedUnitsPerPackage)
        {
            if (dailyFeedUnitsRequired <= 0 || targetDays <= 0 || feedUnitsPerPackage <= 0) return 0;
            int target = checked(dailyFeedUnitsRequired * targetDays);
            int deficit = Math.Max(0, target - Math.Max(0, availableFeedUnits));
            return deficit == 0 ? 0 : (deficit + feedUnitsPerPackage - 1) / feedUnitsPerPackage;
        }

        [Serializable]
        public sealed class Batch
        {
            public string TransactionId = string.Empty;
            public string SellerBusinessId = string.Empty;
            public string BuyerBusinessId = string.Empty;
            public string ItemId = string.Empty;
            public string CategoryId = string.Empty;
            public string UnitLabel = string.Empty;
            public int Units;
            public int UnitPriceCents;
            public int TotalPriceCents;
            public int DayIndex;
            public List<SourceLotPart> SourceLots = new List<SourceLotPart>();
        }

        [Serializable]
        public sealed class SourceLotPart
        {
            public string LotId = string.Empty;
            public string Provenance = string.Empty;
            public string UnitLabel = string.Empty;
            public int Units;
        }

        public sealed class Result
        {
            public bool Succeeded;
            public bool Duplicate;
            public string Refusal = string.Empty;
            public Batch Batch;
        }

        public static Result PurchaseAtStore(
            string transactionId,
            IItemLevelGeneralStoreTradingPort store,
            BusinessRuntimeState sellerRuntime,
            BusinessRuntimeState buyerRuntime,
            IBusinessIdentityCashPort buyerCash,
            string itemId,
            string categoryId,
            string unitLabel,
            int units,
            int dayIndex)
        {
            var result = new Result();
            if (string.IsNullOrWhiteSpace(transactionId) || store == null || sellerRuntime == null
                || buyerRuntime == null || buyerCash == null || string.IsNullOrWhiteSpace(itemId)
                || string.IsNullOrWhiteSpace(categoryId) || string.IsNullOrWhiteSpace(unitLabel) || units <= 0)
            {
                result.Refusal = "Business feed purchase needs transaction identity, both businesses, cash authority, SKU, unit, and positive quantity.";
                return result;
            }
            if (store.StoreBusinessId != sellerRuntime.BusinessId || buyerCash.BusinessId != buyerRuntime.BusinessId
                || string.Equals(sellerRuntime.BusinessId, buyerRuntime.BusinessId, StringComparison.Ordinal))
            {
                result.Refusal = "Seller, buyer, and cash-port identities do not match distinct businesses.";
                return result;
            }
            if (!store.UsesItemLevelInventory(itemId))
            {
                result.Refusal = $"General Store does not offer identified item '{itemId}'.";
                return result;
            }
            int price = store.ItemPricePerUnitCents(itemId);
            if (price <= 0)
            {
                result.Refusal = $"General Store has no positive owner-set price for '{itemId}'.";
                return result;
            }
            if (store.ItemStockUnits(itemId) < units)
            {
                result.Refusal = $"General Store has {store.ItemStockUnits(itemId)} '{itemId}' units; {units} requested.";
                return result;
            }

            var batch = new Batch
            {
                TransactionId = transactionId,
                SellerBusinessId = sellerRuntime.BusinessId,
                BuyerBusinessId = buyerRuntime.BusinessId,
                ItemId = itemId,
                CategoryId = categoryId,
                UnitLabel = unitLabel,
                Units = units,
                UnitPriceCents = price,
                TotalPriceCents = checked(price * units),
                DayIndex = dayIndex,
            };

            int unassigned = units;
            foreach (BusinessRetailItemLotState lot in sellerRuntime.RetailItemLots)
            {
                if (unassigned <= 0) break;
                if (lot == null || !string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.UnitLabel, unitLabel, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.OwnerBusinessId, sellerRuntime.BusinessId, StringComparison.Ordinal)
                    || !string.Equals(lot.CustodianBusinessId, sellerRuntime.BusinessId, StringComparison.Ordinal)) continue;
                int take = Math.Min(unassigned, lot.AvailableUnits);
                if (take <= 0) continue;
                batch.SourceLots.Add(new SourceLotPart
                {
                    LotId = lot.LotId,
                    Provenance = lot.Provenance,
                    UnitLabel = lot.UnitLabel,
                    Units = take,
                });
                unassigned -= take;
            }
            if (unassigned != 0)
            {
                result.Refusal = $"Identified '{itemId}' lots cannot prove {units} available '{unitLabel}' units.";
                return result;
            }

            string debitRefusal = buyerCash.TryDebit(batch.TotalPriceCents,
                $"purchase {units} {unitLabel}(s) of {itemId} from {store.StoreName} ({transactionId})");
            if (debitRefusal != null)
            {
                result.Refusal = $"Buyer payment refused: {debitRefusal}";
                return result;
            }

            if (!store.TryConsumeItemStockUnits(itemId, units, out int consumed) || consumed != units)
            {
                string refundRefusal = buyerCash.Credit(batch.TotalPriceCents,
                    $"refund refused stock purchase {transactionId}");
                result.Refusal = $"Store stock changed before checkout ({consumed}/{units} consumed). " +
                    (refundRefusal == null ? "Buyer debit reversed." : $"Buyer refund failed: {refundRefusal}");
                return result;
            }

            // For this production port, this posts the proceeds and sales
            // observation through the seller's BusinessRuntimeState authority.
            store.RecordItemCashSettlement(categoryId, units, batch.TotalPriceCents);
            result.Batch = batch;
            result.Succeeded = true;
            return result;
        }

        public static Result ReceiveAtBusiness(Batch batch, BusinessRuntimeState destination)
        {
            var result = new Result { Batch = batch };
            if (batch == null || destination == null || string.IsNullOrWhiteSpace(batch.TransactionId)
                || destination.BusinessId != batch.BuyerBusinessId || batch.Units <= 0
                || batch.SourceLots == null || batch.SourceLots.Count == 0)
            {
                result.Refusal = "Purchased feed receipt has no valid batch or matching destination business.";
                return result;
            }

            destination.EnsureCategoryStock(batch.CategoryId, 0, 0);
            CategoryStockState category = destination.GetCategoryStock(batch.CategoryId);
            if (category == null)
            {
                result.Refusal = $"Destination business has no '{batch.CategoryId}' inventory authority.";
                return result;
            }
            if (category.TargetStockUnits > 0
                && category.CurrentStockUnits + batch.Units > category.TargetStockUnits)
            {
                result.Refusal = $"Destination '{batch.CategoryId}' capacity cannot accept {batch.Units} units.";
                return result;
            }

            bool allLotsAlreadyReceived = true;
            foreach (SourceLotPart part in batch.SourceLots)
            {
                if (part == null || part.Units <= 0) continue;
                string lotId = BuildDestinationLotId(batch, part);
                BusinessRetailItemLotState existing = destination.FindRetailItemLot(lotId);
                if (existing == null) allLotsAlreadyReceived = false;
                if (existing != null && (!string.Equals(existing.ItemId, batch.ItemId, StringComparison.OrdinalIgnoreCase)
                    || existing.QuantityUnits != part.Units
                    || existing.OwnerBusinessId != destination.BusinessId
                    || existing.CustodianBusinessId != destination.BusinessId))
                {
                    result.Refusal = $"Existing receipt lot '{lotId}' does not match this purchased custody batch.";
                    return result;
                }
            }

            foreach (SourceLotPart part in batch.SourceLots)
            {
                if (part == null || part.Units <= 0) continue;
                string lotId = BuildDestinationLotId(batch, part);
                if (destination.FindRetailItemLot(lotId) != null) continue;
                string refusal = destination.TryAddRetailItemLot(
                    lotId, batch.ItemId, batch.CategoryId, part.UnitLabel, part.Units,
                    destination.BusinessId, destination.BusinessId,
                    $"Purchased by {batch.BuyerBusinessId} from {batch.SellerBusinessId}; " +
                    $"source lot '{part.LotId}': {part.Provenance}; transaction '{batch.TransactionId}'.",
                    batch.DayIndex);
                if (refusal != null)
                {
                    result.Refusal = refusal;
                    return result;
                }
            }

            result.Succeeded = destination.GetRetailItemStockUnits(batch.ItemId, batch.CategoryId) >= batch.Units
                && DestinationLotsMatch(batch, destination);
            result.Duplicate = result.Succeeded && allLotsAlreadyReceived;
            if (!result.Succeeded) result.Refusal = "Destination has not received the complete purchased batch.";
            return result;
        }

        private static bool DestinationLotsMatch(Batch batch, BusinessRuntimeState destination)
        {
            int received = 0;
            foreach (SourceLotPart part in batch.SourceLots)
            {
                if (part == null || part.Units <= 0) continue;
                BusinessRetailItemLotState lot = destination.FindRetailItemLot(BuildDestinationLotId(batch, part));
                if (lot == null || lot.AvailableUnits != part.Units) return false;
                received += lot.AvailableUnits;
            }
            return received == batch.Units;
        }

        private static string BuildDestinationLotId(Batch batch, SourceLotPart part) =>
            $"feed-purchase:{batch.TransactionId}:{part.LotId}";
    }
}
