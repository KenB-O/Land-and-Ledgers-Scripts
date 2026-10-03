using System;
using System.Collections.Generic;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Economy.Farming
{
    /// <summary>
    /// FRM-1: a farm produce lot with provenance (Tech X Part IV — lot/batch
    /// provenance; aggregate quantity alone is insufficient). Eggs, butter,
    /// produce, or any other farm output: the general store "can buy whatever",
    /// so productKind is an open string, not a fixed goods list.
    /// </summary>
    [Serializable]
    public sealed class FarmProduceLot
    {
        [SerializeField]
        private string lotId = string.Empty;

        [SerializeField]
        private string productKind = string.Empty;

        [SerializeField, Min(0)]
        private int quantityUnits;

        [SerializeField]
        private int producedDayIndex;

        [SerializeField]
        private string farmBusinessId = string.Empty;

        [SerializeField]
        private string farmBusinessName = string.Empty;

        [SerializeField]
        private bool sold;

        public string LotId => lotId ?? string.Empty;
        public string ProductKind => productKind ?? string.Empty;
        public int QuantityUnits => Mathf.Max(0, quantityUnits);
        public int ProducedDayIndex => producedDayIndex;
        public string FarmBusinessId => farmBusinessId ?? string.Empty;
        public string FarmBusinessName => farmBusinessName ?? string.Empty;
        public bool Sold => sold;

        public FarmProduceLot(string lotId, string productKind, int quantityUnits,
            int producedDayIndex, string farmBusinessId, string farmBusinessName)
        {
            this.lotId = lotId ?? string.Empty;
            this.productKind = productKind ?? string.Empty;
            this.quantityUnits = Mathf.Max(0, quantityUnits);
            this.producedDayIndex = producedDayIndex;
            this.farmBusinessId = farmBusinessId ?? string.Empty;
            this.farmBusinessName = farmBusinessName ?? string.Empty;
        }

        internal void MarkSold()
        {
            sold = true;
        }
    }

    /// <summary>
    /// FRM-1: an executed farm → general store produce sale. Ordinary commerce with
    /// a named counterparty on both sides — never an anonymous aggregate transfer.
    /// </summary>
    [Serializable]
    public sealed class FarmProduceSale
    {
        public string SaleId = string.Empty;
        public string LotId = string.Empty;
        public string ProductKind = string.Empty;
        public int Units;
        public int PricePerUnitCents;
        public int TotalCents;
        public int DayIndex;
        public string FarmBusinessId = string.Empty;
        public string StoreBusinessId = string.Empty;
        public string StoreCategoryId = string.Empty;
    }

    /// <summary>
    /// FRM-1: farm produce → general store purchasing. The store's produce-purchasing
    /// capability (Tech X §3.1) accepts any lot kind: the category is ensured from the
    /// product kind, so the store can buy whatever farms bring. Every sale records
    /// provenance on both sides — SaleProceeds on the farm household's ledger
    /// (Canon XIII 13.2) and the spend on the store's transfer costs.
    /// Embodied note: the journey/delivery leg is pending (no journey model yet);
    /// the transaction itself is real and fully recorded — nothing is faked.
    /// </summary>
    public static class FarmProduceMarket
    {
        /// <summary>
        /// Maps an arbitrary product kind to a store category id. Open-ended by
        /// design: new farm products never require code changes.
        /// </summary>
        public static string CategoryForProductKind(string productKind)
        {
            string kind = (productKind ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(kind))
            {
                return "produce_general";
            }

            // Normalize: letters/digits only, collapse the rest to underscores.
            var chars = new List<char>(kind.Length + 8);
            bool lastUnderscore = true;
            foreach (char c in kind)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    chars.Add(c);
                    lastUnderscore = false;
                }
                else if (!lastUnderscore)
                {
                    chars.Add('_');
                    lastUnderscore = true;
                }
            }

            string slug = new string(chars.ToArray()).Trim('_');
            if (string.IsNullOrEmpty(slug))
            {
                slug = "general";
            }

            return "produce_" + slug;
        }

        /// <summary>
        /// Executes a farm → store produce sale. Returns the sale record, or null
        /// with a diagnostic when the sale cannot proceed (lot already sold, store
        /// cannot afford, no ledger for provenance).
        /// </summary>
        public static FarmProduceSale ExecuteSale(
            FarmProduceLot lot,
            BusinessRuntimeState storeState,
            string storeBusinessId,
            string storeBusinessName,
            int pricePerUnitCents,
            int dayIndex,
            int storeCashBufferCents,
            HouseholdLedger sellerLedger,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (lot == null)
            {
                diagnostics.Add("No produce lot offered.");
                return null;
            }

            if (lot.Sold)
            {
                diagnostics.Add($"Lot '{lot.LotId}' was already sold — lots sell exactly once (provenance).");
                return null;
            }

            if (lot.QuantityUnits <= 0)
            {
                diagnostics.Add($"Lot '{lot.LotId}' has no units to sell.");
                return null;
            }

            if (storeState == null)
            {
                diagnostics.Add("No store runtime state to receive the produce.");
                return null;
            }

            if (sellerLedger == null)
            {
                diagnostics.Add("No seller household ledger — sale proceeds require provenance (Canon 13.2).");
                return null;
            }

            int unitPrice = Mathf.Max(0, pricePerUnitCents);
            int totalCents = lot.QuantityUnits * unitPrice;

            int spendable = Mathf.Max(0, storeState.CurrentCashCents - Mathf.Max(0, storeCashBufferCents));
            if (totalCents > spendable)
            {
                diagnostics.Add($"Store cannot afford {lot.QuantityUnits} {lot.ProductKind} " +
                    $"for {totalCents}c (spendable {spendable}c after reserve).");
                return null;
            }

            // The store can buy whatever: ensure a category for this product kind.
            string categoryId = CategoryForProductKind(lot.ProductKind);
            storeState.EnsureCategoryStock(categoryId, 0, Mathf.Max(lot.QuantityUnits, 100));
            int accepted = storeState.AddCategoryStockUnits(categoryId, lot.QuantityUnits);
            if (accepted <= 0)
            {
                diagnostics.Add($"Store could not receive '{lot.ProductKind}' (category '{categoryId}' full).");
                return null;
            }

            int acceptedTotal = accepted * unitPrice;
            storeState.AddWeeklyLocalTransferCost(acceptedTotal,
                $"bought {accepted} {lot.ProductKind} from {lot.FarmBusinessName} for {acceptedTotal}c (lot {lot.LotId})");

            string problem = sellerLedger.RecordInflow(
                dayIndex,
                acceptedTotal,
                HouseholdIncomeSource.SaleProceeds,
                lot.LotId,
                $"sold {accepted} {lot.ProductKind} to {storeBusinessName} (lot {lot.LotId})",
                storeBusinessName);
            if (problem != null)
            {
                // Ledger rejected the inflow: unwind the stock movement so the sale is
                // atomic — nothing half-recorded (Canon 13.2).
                storeState.TryConsumeCategoryStockUnits(categoryId, accepted, out _);
                diagnostics.Add($"Sale unwound: {problem}");
                return null;
            }

            lot.MarkSold();

            var sale = new FarmProduceSale
            {
                SaleId = $"FPS-{dayIndex}-{lot.LotId}",
                LotId = lot.LotId,
                ProductKind = lot.ProductKind,
                Units = accepted,
                PricePerUnitCents = unitPrice,
                TotalCents = acceptedTotal,
                DayIndex = dayIndex,
                FarmBusinessId = lot.FarmBusinessId,
                StoreBusinessId = storeBusinessId ?? string.Empty,
                StoreCategoryId = categoryId,
            };

            diagnostics.Add($"Sold {accepted} {lot.ProductKind} ({lot.LotId}) from " +
                $"{lot.FarmBusinessName} to {storeBusinessName} for {acceptedTotal}c.");
            return sale;
        }
    }
}
