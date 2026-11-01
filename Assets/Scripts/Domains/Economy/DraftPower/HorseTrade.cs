using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Equipment;
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
        /// <summary>
        /// Draft-capable species for field work (Canon §9.3B), gated on real
        /// condition (Pass VII P1-5): a lame, starved, or exhausted animal is
        /// not draft-capable no matter its species. Mules and oxen are
        /// first-class draft species alongside horses.
        /// </summary>
        public static bool IsDraftCapable(AnimalState animal)
        {
            if (animal == null || !animal.IsActive) return false;
            bool speciesOk = animal.Species == AnimalSpecies.Horse
                || animal.Species == AnimalSpecies.Mule
                || animal.Species == AnimalSpecies.Ox
                || animal.Species == AnimalSpecies.Other; // mules/oxen until species extend
            if (!speciesOk) return false;
            return HorseAuthority.MeetsWorkCondition(animal);
        }

        /// <summary>
        /// Pass VII Workstream B (P2-14): a sale ends the horse's old life
        /// cleanly. When a reservation authority is supplied, every live
        /// reservation on the horse is released first — a sold horse keeps no
        /// stale holds — and the local use mirror is cleared. When a rental
        /// service is supplied, an active rental is terminated (the renter's
        /// term ends early, recorded loudly). Ownership transfers through the
        /// registry (history never rewritten) and custody passes to the buyer.
        /// Both parameters are optional so existing call sites keep working;
        /// callers that hold live reservations must supply the authority.
        /// </summary>
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
            List<string> diagnostics,
            IUnifiedReservationAuthority reservations = null,
            HorseRentalService rentals = null)
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

            // A sold horse keeps no stale reservations: release every live
            // hold on the animal key, then clear the local use mirror. The new
            // owner starts with a free horse, not the old owner's commitments.
            if (reservations != null)
            {
                reservations.ReleaseAllForResource(
                    ReservationKeyConventions.Animal(horseId.ToString()));
                horse.CurrentUse = HorseUseKind.None;
                horse.CurrentAssignmentId = string.Empty;
                horse.CurrentReservationId = string.Empty;
                diagnostics.Add($"HorseTrade: live reservations on {horseId} released by sale — no stale holds.");
            }
            else
            {
                diagnostics.Add(
                    $"HorseTrade: no reservation authority supplied — live holds on {horseId} were NOT cleared. " +
                    "Callers with live reservations must supply the authority.");
            }

            // An active rental ends with the sale: the renter's term is cut short.
            rentals?.TerminateForSale(horseId, dayIndex, diagnostics);

            string problem = registry.TransferOwnership(
                horseId, AnimalOwnerKind.Business, buyerBusinessId, dayIndex,
                $"sold to {buyerBusinessName ?? buyerBusinessId} for {price}c");
            if (problem != null)
            {
                diagnostics.Add($"HorseTrade: ownership transfer failed — {problem}");
                return null;
            }

            // Custody passes to the buyer with the ownership.
            string custodyProblem = HorseAuthority.SetCustody(horse, "business", buyerBusinessId);
            if (custodyProblem != null)
                diagnostics.Add($"HorseTrade: custody transfer noted a problem — {custodyProblem}");

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
