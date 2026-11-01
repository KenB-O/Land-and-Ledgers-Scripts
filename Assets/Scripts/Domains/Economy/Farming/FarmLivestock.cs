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
            List<string> diagnostics,
            // Pass VIII (production networks, additive, default-off): the
            // butcher's operating cash. When wired, the butcher pays the price
            // from it (TryBuyLivestock refuses when short) — the farm's
            // SaleProceeds inflow then has a matching outflow (no manufactured
            // revenue). Unwired keeps the legacy no-payment path.
            LandLedgers.Economy.BusinessRuntimeState buyerCash = null)
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

            // Pass VIII: the production ownership convention is "farm:" +
            // business id (FarmBootstrap; CullFlow requires it). The legacy
            // unprefixed form is still accepted for older records — but a
            // farm never sells another farm's animal either way.
            bool ownedByFarm = string.Equals(animal.OwnerId, farmBusinessId, StringComparison.Ordinal)
                || string.Equals(animal.OwnerId, "farm:" + farmBusinessId, StringComparison.Ordinal);
            if (animal.OwnerKind != AnimalOwnerKind.Business || !ownedByFarm)
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

            // Pass VIII: atomic ownership — snapshot the farm's open records
            // BEFORE the butcher appends its own, and close only the farm's
            // records after acceptance. Closing first left the animal
            // ownerless when the purchase refused; closing everything after
            // would also close the butcher's just-appended record.
            var farmOpenRecords = new List<AnimalOwnershipRecord>();
            foreach (AnimalOwnershipRecord record in animal.OwnershipHistory)
            {
                if (record.IsCurrent)
                {
                    farmOpenRecords.Add(record);
                }
            }

            if (!butcher.TryBuyLivestock(registry, animalId, price, dayIndex, diagnostics, buyerCash))
            {
                diagnostics.Add($"Butcher declined animal {animalId} — sale refused, farm keeps the animal.");
                return null;
            }

            foreach (AnimalOwnershipRecord record in farmOpenRecords)
            {
                record.EndDayIndex = dayIndex;
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
