using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Butcher;
using LandLedgers.Population;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Farming
{
    /// <summary>
    /// FRM-2: an executed farm → butcher live-animal sale. Per-head pricing, ordinary
    /// commerce with named counterparties — never anonymous. Ownership moves through
    /// the HF-2 registry (append-only history, pedigree intact, IDs never reused);
    /// the butcher slaughters through the BIZ-4 flow afterward.
    /// </summary>
    [Serializable]
    public sealed class FarmLivestockSale
    {
        public string SaleId = string.Empty;
        public string AnimalId = string.Empty;
        public AnimalSpecies Species = AnimalSpecies.Unspecified;
        public int PriceCents;
        public int DayIndex;
        public string FarmBusinessId = string.Empty;
        public string FarmBusinessName = string.Empty;
        public string ButcherBusinessId = string.Empty;
    }

    /// <summary>
    /// FRM-2: farm → butcher livestock sales. The animal must be farm-owned (business
    /// ownership by the selling farm) at sale time. The transfer appends to the
    /// append-only ownership history (HF-2), keeps the pedigree links intact, and
    /// records SaleProceeds on the farm household's ledger (Canon XIII 13.2).
    /// </summary>
    public static class FarmLivestockMarket
    {
        /// <summary>
        /// Executes a farm → butcher live-animal sale. Returns the sale record, or null
        /// with a diagnostic when the sale cannot proceed.
        /// </summary>
        public static FarmLivestockSale ExecuteSale(
            AnimalRegistry registry,
            EntityId animalId,
            string farmBusinessId,
            string farmBusinessName,
            ButcherRuntime butcher,
            string butcherBusinessId,
            int priceCents,
            int dayIndex,
            HouseholdLedger sellerLedger,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (registry == null)
            {
                diagnostics.Add("No animal registry — ownership cannot transfer.");
                return null;
            }

            AnimalState animal = registry.GetAnimal(animalId);
            if (animal == null)
            {
                diagnostics.Add($"Animal {animalId} not found in the registry.");
                return null;
            }

            if (!animal.IsActive)
            {
                diagnostics.Add($"Animal {animalId} is not active — it cannot be sold.");
                return null;
            }

            if (animal.OwnerKind != AnimalOwnerKind.Business
                || !string.Equals(animal.OwnerId, farmBusinessId, StringComparison.Ordinal))
            {
                diagnostics.Add($"Animal {animalId} is not owned by farm '{farmBusinessId}' " +
                    $"(owned by {animal.OwnerKind}:{animal.OwnerId}).");
                return null;
            }

            if (butcher == null)
            {
                diagnostics.Add("No butcher runtime to receive the animal.");
                return null;
            }

            if (sellerLedger == null)
            {
                diagnostics.Add("No seller household ledger — sale proceeds require provenance (Canon 13.2).");
                return null;
            }

            int price = Mathf.Max(0, priceCents);

            // Close the farm's open ownership record, then transfer through the BIZ-4
            // buying flow, which appends the butcher's ownership record.
            foreach (AnimalOwnershipRecord record in animal.OwnershipHistory)
            {
                if (record.IsCurrent)
                {
                    record.EndDayIndex = dayIndex;
                }
            }

            if (!butcher.TryBuyLivestock(registry, animalId, price, dayIndex, diagnostics))
            {
                diagnostics.Add($"Butcher declined animal {animalId}.");
                return null;
            }

            string animalKey = animalId.ToString();
            string problem = sellerLedger.RecordInflow(
                dayIndex,
                price,
                HouseholdIncomeSource.SaleProceeds,
                animalKey,
                $"sold {animal.Species} {animalKey} to butcher {butcherBusinessId} for {price}c",
                butcherBusinessId ?? string.Empty);
            if (problem != null)
            {
                diagnostics.Add($"Sale completed but ledger rejected the inflow: {problem}");
            }

            var sale = new FarmLivestockSale
            {
                SaleId = $"FLS-{dayIndex}-{animalKey}",
                AnimalId = animalKey,
                Species = animal.Species,
                PriceCents = price,
                DayIndex = dayIndex,
                FarmBusinessId = farmBusinessId ?? string.Empty,
                FarmBusinessName = farmBusinessName ?? string.Empty,
                ButcherBusinessId = butcherBusinessId ?? string.Empty,
            };

            diagnostics.Add($"Sold {animal.Species} {animalKey} from {farmBusinessName} " +
                $"to butcher {butcherBusinessId} for {price}c per head.");
            return sale;
        }
    }
}
