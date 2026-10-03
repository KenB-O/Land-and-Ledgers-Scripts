using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.DraftPower
{
    /// <summary>
    /// EQU-3: farm → buyer horse sales. The FRM-2 flow is butcher-specific, so horses
    /// get their own trade following the same pattern: registry ownership transfer
    /// (history never rewritten), per-head pricing, ledger provenance. No free horses —
    /// a plow team is bought from a rancher or another farm, or bred (HF-2 births).
    /// </summary>
    public sealed class HorseSale
    {
        public string SaleId = string.Empty;
        public string AnimalId = string.Empty;
        public int PriceCents;
        public int DayIndex;
        public string SellerBusinessId = string.Empty;
        public string SellerBusinessName = string.Empty;
        public string BuyerBusinessId = string.Empty;
        public string BuyerBusinessName = string.Empty;
    }

    public static class HorseTrade
    {
        /// <summary>Draft-capable species for field work (Canon §9.3B).</summary>
        public static bool IsDraftCapable(AnimalState animal)
        {
            if (animal == null || !animal.IsActive) return false;
            return animal.Species == AnimalSpecies.Horse
                || animal.Species == AnimalSpecies.Other; // mules/oxen until species extend
        }

        public static HorseSale ExecuteSale(
            AnimalRegistry registry,
            EntityId horseId,
            string sellerBusinessId,
            string sellerBusinessName,
            string buyerBusinessId,
            string buyerBusinessName,
            int priceCents,
            int dayIndex,
            HouseholdLedger sellerLedger,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (registry == null)
            {
                diagnostics.Add("HorseTrade: no animal registry — ownership cannot transfer.");
                return null;
            }

            AnimalState horse = registry.GetAnimal(horseId);
            if (horse == null)
            {
                diagnostics.Add($"HorseTrade: animal {horseId} not found in the registry.");
                return null;
            }
            if (!horse.IsActive)
            {
                diagnostics.Add($"HorseTrade: animal {horseId} is not active — it cannot be sold.");
                return null;
            }
            if (horse.Species != AnimalSpecies.Horse)
            {
                diagnostics.Add($"HorseTrade: animal {horseId} is {horse.Species}, not a horse.");
                return null;
            }
            if (horse.OwnerKind != AnimalOwnerKind.Business
                || !string.Equals(horse.OwnerId, sellerBusinessId, StringComparison.Ordinal))
            {
                diagnostics.Add($"HorseTrade: animal {horseId} is not owned by '{sellerBusinessId}' " +
                    $"(owned by {horse.OwnerKind}:{horse.OwnerId}).");
                return null;
            }
            if (string.IsNullOrWhiteSpace(buyerBusinessId))
            {
                diagnostics.Add("HorseTrade: buyer business id is required — no anonymous buyers.");
                return null;
            }
            if (sellerLedger == null)
            {
                diagnostics.Add("HorseTrade: no seller ledger — sale proceeds require provenance (Canon 13.2).");
                return null;
            }

            int price = Math.Max(0, priceCents);
            string problem = registry.TransferOwnership(
                horseId, AnimalOwnerKind.Business, buyerBusinessId, dayIndex,
                $"sold to {buyerBusinessName ?? buyerBusinessId} for {price}c");
            if (problem != null)
            {
                diagnostics.Add($"HorseTrade: ownership transfer failed — {problem}");
                return null;
            }

            string horseKey = horseId.ToString();
            string ledgerProblem = sellerLedger.RecordInflow(
                dayIndex, price, HouseholdIncomeSource.SaleProceeds, horseKey,
                $"sold horse {horseKey} to {buyerBusinessName ?? buyerBusinessId} for {price}c",
                buyerBusinessId ?? string.Empty);
            if (ledgerProblem != null)
                diagnostics.Add($"HorseTrade: sale completed but ledger rejected the inflow: {ledgerProblem}");

            var sale = new HorseSale
            {
                SaleId = $"HRS-{dayIndex}-{horseKey}",
                AnimalId = horseKey,
                PriceCents = price,
                DayIndex = dayIndex,
                SellerBusinessId = sellerBusinessId ?? string.Empty,
                SellerBusinessName = sellerBusinessName ?? string.Empty,
                BuyerBusinessId = buyerBusinessId ?? string.Empty,
                BuyerBusinessName = buyerBusinessName ?? string.Empty,
            };
            diagnostics.Add($"HorseTrade: sold horse {horseKey} from {sellerBusinessName} to {buyerBusinessName} for {price}c.");
            return sale;
        }
    }
}
