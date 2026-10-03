using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Delivery
{
    /// <summary>
    /// FVS-3: a lot that can ride a delivery job. Implemented by MilkLot
    /// (FVS-2) and FarmProduceLot (FRM-1, additive). Perishables age in
    /// transit; only saleable units settle at delivery.
    /// </summary>
    public interface ITransitLot
    {
        string TransitLotId { get; }
        int TransitQuantityUnits { get; }
        void AgeInTransit(int transitDays);
        int TransitSaleableUnits(int dayIndex);
        void MarkTransitConsumed();
    }

    /// <summary>
    /// FVS-3: a buyer of dairy lots. Finite capacity per product kind —
    /// there is no invisible infinite buyer (Canon §9.2, Tech X §8.5).
    /// </summary>
    public interface IDairyBuyer
    {
        string BuyerBusinessId { get; }
        string BuyerName { get; }
        int CapacityFor(string productKind);
        int CommittedFor(string productKind);
        /// <summary>Returns null on acceptance, or a diagnostic on refusal.</summary>
        string ReceiveDairy(string productKind, int units, string lotId, int dayIndex);
    }

    /// <summary>
    /// FVS-3: in-memory slice implementation of a finite-capacity dairy buyer
    /// (e.g. the general store's dairy counter). The real store wiring replaces
    /// this against BusinessRuntimeState; the capacity discipline stays.
    /// </summary>
    [Serializable]
    public sealed class DairyBuyerStock : IDairyBuyer
    {
        public string BuyerBusinessId { get; private set; }
        public string BuyerName { get; private set; }

        private readonly Dictionary<string, int> capacity =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> committed =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> onHand =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public DairyBuyerStock() { }

        public DairyBuyerStock(string buyerBusinessId, string buyerName)
        {
            BuyerBusinessId = buyerBusinessId ?? string.Empty;
            BuyerName = buyerName ?? string.Empty;
        }

        public void SetCapacity(string productKind, int units)
        {
            if (string.IsNullOrWhiteSpace(productKind)) return;
            capacity[productKind.Trim().ToLowerInvariant()] = Math.Max(0, units);
        }

        public int CapacityFor(string productKind)
        {
            if (string.IsNullOrWhiteSpace(productKind)) return 0;
            int cap;
            return capacity.TryGetValue(productKind.Trim().ToLowerInvariant(), out cap) ? cap : 0;
        }

        public int CommittedFor(string productKind)
        {
            if (string.IsNullOrWhiteSpace(productKind)) return 0;
            int c;
            return committed.TryGetValue(productKind.Trim().ToLowerInvariant(), out c) ? c : 0;
        }

        public int OnHand(string productKind)
        {
            if (string.IsNullOrWhiteSpace(productKind)) return 0;
            int h;
            return onHand.TryGetValue(productKind.Trim().ToLowerInvariant(), out h) ? h : 0;
        }

        public string ReceiveDairy(string productKind, int units, string lotId, int dayIndex)
        {
            if (units <= 0) return $"Refused lot {lotId}: no units.";
            string key = (productKind ?? string.Empty).Trim().ToLowerInvariant();
            int room = CapacityFor(key) - CommittedFor(key);
            if (room < units)
            {
                return $"Refused lot {lotId}: {BuyerName} can take {Math.Max(0, room)} more {productKind} " +
                       $"(capacity {CapacityFor(key)}, committed {CommittedFor(key)}) — finite buyer capacity (Canon §9.2).";
            }
            committed[key] = CommittedFor(key) + units;
            onHand[key] = OnHand(key) + units;
            return null;
        }
    }

    /// <summary>
    /// FVS-3: an executed dairy sale (milk/butter/cream lots → buyer). Mirrors
    /// the FRM-1 provenance discipline: named counterparties, seller ledger
    /// inflow with lot-id source references (Canon XIII §13.2), lots consumed
    /// exactly once.
    /// </summary>
    [Serializable]
    public sealed class DairySale
    {
        public string SaleId = string.Empty;
        public string ProductKind = string.Empty;
        public List<string> LotIds = new List<string>();
        public int QuantityUnits;
        public int TotalCents;
        public string SellerFarmId = string.Empty;
        public string BuyerBusinessId = string.Empty;
        public string BuyerName = string.Empty;
        public int DayIndex;
    }

    /// <summary>
    /// FVS-3: dairy sale execution. Used as the DeliveryService sale executor
    /// for milk/butter so settlement happens ON DELIVERY, not at agreement.
    /// </summary>
    public static class DairyMarket
    {
        /// <summary>
        /// Executes a dairy sale for already-delivered lots. Returns the sale,
        /// or null with diagnostics when it cannot proceed.
        /// </summary>
        public static DairySale ExecuteDairySale(
            List<ITransitLot> lots,
            string productKind,
            IDairyBuyer buyer,
            string sellerFarmId,
            int pricePerUnitCents,
            int dayIndex,
            HouseholdLedger sellerLedger,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();

            if (lots == null || lots.Count == 0)
            {
                diagnostics.Add("DairyMarket: no lots offered.");
                return null;
            }
            if (buyer == null)
            {
                diagnostics.Add("DairyMarket: no buyer — dairy requires a real customer (Canon §9.1).");
                return null;
            }
            if (sellerLedger == null)
            {
                diagnostics.Add("DairyMarket: no seller household ledger — proceeds require provenance (Canon 13.2).");
                return null;
            }

            int totalUnits = 0;
            var lotIds = new List<string>();
            foreach (var lot in lots)
            {
                if (lot == null) continue;
                int saleable = lot.TransitSaleableUnits(dayIndex);
                if (saleable <= 0)
                {
                    diagnostics.Add($"DairyMarket: lot {lot.TransitLotId} has no saleable units on arrival — spoiled in transit, seller bears the loss.");
                    return null;
                }
                totalUnits += saleable;
                lotIds.Add(lot.TransitLotId);
            }

            if (totalUnits <= 0)
            {
                diagnostics.Add("DairyMarket: no saleable units to sell.");
                return null;
            }

            string refusal = buyer.ReceiveDairy(productKind, totalUnits, string.Join(",", lotIds), dayIndex);
            if (refusal != null)
            {
                diagnostics.Add("DairyMarket: " + refusal);
                return null;
            }

            int totalCents = totalUnits * Math.Max(0, pricePerUnitCents);
            string inflowRejection = sellerLedger.RecordInflow(dayIndex, totalCents,
                HouseholdIncomeSource.SaleProceeds,
                string.Join(",", lotIds),
                $"Dairy sale: {totalUnits} {productKind} to {buyer.BuyerName} (lots {string.Join(",", lotIds)})",
                buyer.BuyerName);
            if (inflowRejection != null)
            {
                diagnostics.Add("DairyMarket: seller ledger refused the inflow: " + inflowRejection);
                return null;
            }

            foreach (var lot in lots)
            {
                if (lot != null) lot.MarkTransitConsumed();
            }

            return new DairySale
            {
                SaleId = "dairy-sale-" + dayIndex + "-" + Math.Abs(string.Join(",", lotIds).GetHashCode()),
                ProductKind = productKind ?? string.Empty,
                LotIds = lotIds,
                QuantityUnits = totalUnits,
                TotalCents = totalCents,
                SellerFarmId = sellerFarmId ?? string.Empty,
                BuyerBusinessId = buyer.BuyerBusinessId,
                BuyerName = buyer.BuyerName,
                DayIndex = dayIndex,
            };
        }
    }
}
