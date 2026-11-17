using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.Carpentry
{
    /// <summary>One household the workshop may sell a finished piece to: its ledger, its furnishing register, and its ranked B6 wants.</summary>
    public sealed class CarpentryFurnitureSaleCandidate
    {
        public int HouseholdId;
        public HouseholdLedger Ledger;
        public HouseholdFurnishingRegister Furnishings;
        public List<FurnishingWant> Wants = new List<FurnishingWant>();
        /// <summary>G-3: the household's home building instance id — the delivery + install location for a store purchase.</summary>
        public int HomeBuildingId = -1;
    }

    /// <summary>
    /// G-1 (expanded campaign 2026-11-16, W-G4 Workstream 1): the
    /// workshop→household sale of finished furniture — the transfer/sale
    /// path for workshop-made pieces.
    ///
    /// CHOSEN PATH (proposed default for the owner to correct): a direct
    /// over-the-counter sale from the workshop's finished stock to a
    /// household that wants the piece, at the F2 retail baseline price
    /// (FurnitureValuation.ReplacementCostCentsForKind — the authored
    /// retail price per kind, never guessed). Demand comes from the B6
    /// household furnishing evaluator's ranked wants; the buyer pays
    /// once through its HouseholdLedger and the workshop is credited
    /// through its business cash port; title moves through
    /// EquipmentAsset.TransferOwnership (the F4 secondhand mechanism)
    /// and the piece is deregistered from the workshop and registered
    /// in the buying household's furnishing register, where it can then
    /// be installed (B0's install path takes registered assets).
    ///
    /// NOT the F2 retail lines: those sell the store's generic
    /// inventory, not EquipmentAssets, and the F4 service that could
    /// move assets through the store is driverless (Workstream 3) — a
    /// workshop piece cannot sit on a store retail line. NOT a
    /// commission leg: no commission system exists, and the workshop's
    /// authored production model is make-to-stock; building a
    /// make-to-order commission system is a structural fork for the
    /// owner to rule. One sale per household per day (a household
    /// buying furniture is a big event); a piece is sold to the
    /// highest-priority affordable want for its kind, oldest finished
    /// piece first. Shelf, crate and bedroll pieces sell the same way
    /// when a household wants them — the B6 evaluator wants beds,
    /// tables, chairs and cabinets, so shelf/crate/bedroll pieces wait
    /// until a want names them (no want is invented for them here).
    ///
    /// The workshop's own household may buy (Don's household is a
    /// separate economic actor from his business; the sale is priced at
    /// the retail baseline, arm's length), as may the player household.
    /// A reserved piece is never sold out from under its reservation,
    /// and an unpriced kind is never sold (price 0 refuses).
    /// </summary>
    public static class CarpentryFurnitureSale
    {
        /// <summary>
        /// Sells the workshop's finished furniture to wanting households.
        /// Returns true when every finished piece was either sold or
        /// legitimately skipped (no wanting buyer, unpriced kind,
        /// reserved piece); false with the error when a sale that moved
        /// cash could not be completed cleanly — the error names the
        /// asset so the dispute can be reconciled, never hidden.
        /// </summary>
        public static bool TrySellFinishedPieces(
            BusinessInstanceState workshop,
            BusinessEquipmentRegister workshopRegister,
            IBusinessCashPort workshopCash,
            IReadOnlyList<CarpentryFurnitureSaleCandidate> candidates,
            int dayIndex,
            List<string> diag,
            out string error)
        {
            error = string.Empty;
            diag = diag ?? new List<string>();
            if (workshop == null || workshopRegister == null || workshopCash == null || candidates == null)
            {
                error = "Carpentry furniture sale requires a workshop, its equipment register, its cash port, and buyer candidates.";
                return false;
            }
            var production = new CarpentryProduction(workshop, workshopRegister);
            var soldHouseholds = new HashSet<int>();
            foreach (EquipmentAsset asset in production.FinishedFurnitureAssets())
            {
                if (asset == null) continue;
                if (!string.Equals(asset.OwnerKind, "business", StringComparison.Ordinal)
                    || !string.Equals(asset.OwnerId, workshop.InstanceId, StringComparison.Ordinal))
                {
                    diag.Add($"CarpentryFurnitureSale: piece '{asset.AssetId}' ({asset.Kind}) is not workshop-owned ({asset.OwnerKind}:{asset.OwnerId}) — left in the register, not sold.");
                    continue;
                }
                if (asset.IsReserved)
                {
                    diag.Add($"CarpentryFurnitureSale: piece '{asset.AssetId}' ({asset.Kind}) is reserved by {asset.ReservedBy} — not sold out from under its reservation.");
                    continue;
                }
                int priceCents = FurnitureValuation.ReplacementCostCentsForKind(asset.Kind);
                if (priceCents <= 0)
                {
                    diag.Add($"CarpentryFurnitureSale: piece '{asset.AssetId}' ({asset.Kind}) has no authored retail baseline — unpriced, never sold at a guessed price.");
                    continue;
                }
                if (!TryFindBuyer(asset, priceCents, candidates, soldHouseholds, out CarpentryFurnitureSaleCandidate buyer, out FurnishingWant want))
                {
                    diag.Add($"CarpentryFurnitureSale: piece '{asset.AssetId}' ({asset.Kind}, {priceCents}c) has no wanting household that can afford it today — it stays in workshop stock.");
                    continue;
                }
                if (!TrySellOne(workshop, workshopRegister, workshopCash, asset, priceCents, buyer, want, dayIndex, diag, out string saleError))
                {
                    error = saleError;
                    return false;
                }
                soldHouseholds.Add(buyer.HouseholdId);
            }
            return true;
        }

        private static bool TryFindBuyer(
            EquipmentAsset asset,
            int priceCents,
            IReadOnlyList<CarpentryFurnitureSaleCandidate> candidates,
            HashSet<int> soldHouseholds,
            out CarpentryFurnitureSaleCandidate buyer,
            out FurnishingWant want)
        {
            buyer = null;
            want = null;
            foreach (CarpentryFurnitureSaleCandidate candidate in candidates)
            {
                if (candidate == null || candidate.Ledger == null || candidate.Furnishings == null
                    || soldHouseholds.Contains(candidate.HouseholdId)) continue;
                if (candidate.Ledger.GetBalanceCents() < priceCents) continue;
                FurnishingWant best = BestWantForKind(candidate.Wants, asset.Kind);
                if (best == null) continue;
                if (want == null
                    || best.Priority < want.Priority
                    || (best.Priority == want.Priority && best.PersonsAffected > want.PersonsAffected)
                    || (best.Priority == want.Priority && best.PersonsAffected == want.PersonsAffected
                        && candidate.HouseholdId < buyer.HouseholdId))
                {
                    buyer = candidate;
                    want = best;
                }
            }
            return buyer != null && want != null;
        }

        private static FurnishingWant BestWantForKind(List<FurnishingWant> wants, string kind)
        {
            if (wants == null) return null;
            FurnishingWant best = null;
            foreach (FurnishingWant candidate in wants)
            {
                if (candidate == null || !candidate.Affordable) continue;
                if (!string.Equals(candidate.ItemKind, kind, StringComparison.OrdinalIgnoreCase)) continue;
                if (best == null
                    || candidate.Priority < best.Priority
                    || (candidate.Priority == best.Priority && candidate.PersonsAffected > best.PersonsAffected))
                    best = candidate;
            }
            return best;
        }

        /// <summary>
        /// One sale, F4 Pay order: buyer pays once through its ledger,
        /// the workshop is credited, title transfers, the piece leaves
        /// the workshop register and enters the household register. A
        /// refused credit refunds the buyer in full; a refused transfer
        /// refunds the buyer and reverses the workshop credit. The
        /// transfer refusal branch is near-impossible by construction
        /// (reserved and non-workshop-owned pieces are screened before
        /// any cash moves), but it is handled, never assumed away.
        /// </summary>
        private static bool TrySellOne(
            BusinessInstanceState workshop,
            BusinessEquipmentRegister workshopRegister,
            IBusinessCashPort workshopCash,
            EquipmentAsset asset,
            int priceCents,
            CarpentryFurnitureSaleCandidate buyer,
            FurnishingWant want,
            int dayIndex,
            List<string> diag,
            out string error)
        {
            error = string.Empty;
            string outflowRefusal = buyer.Ledger.RecordOutflow(dayIndex, priceCents,
                $"workshop furniture purchase {asset.AssetId} ({asset.Kind})", workshop.InstanceId);
            if (outflowRefusal != null)
            {
                diag.Add($"CarpentryFurnitureSale: household {buyer.HouseholdId} could not pay {priceCents}c for '{asset.AssetId}' — {outflowRefusal}; nothing moved.");
                return true;
            }
            string creditRefusal = workshopCash.Credit(priceCents,
                $"workshop furniture sale {asset.AssetId} ({asset.Kind}) to household {buyer.HouseholdId}");
            if (creditRefusal != null)
            {
                string refundRefusal = buyer.Ledger.RecordInflow(dayIndex, priceCents,
                    HouseholdIncomeSource.OtherDocumented, asset.AssetId,
                    $"refund of refused workshop furniture sale {asset.AssetId}: {creditRefusal}", workshop.InstanceId);
                if (refundRefusal != null)
                {
                    error = $"CarpentryFurnitureSale DISPUTED: workshop furniture sale '{asset.AssetId}' credit refused ({creditRefusal}) and the buyer refund also refused ({refundRefusal}) — the buyer outflow of {priceCents}c stands, recorded here, never hidden.";
                    return false;
                }
                diag.Add($"CarpentryFurnitureSale: workshop furniture sale '{asset.AssetId}' credit refused ({creditRefusal}) — buyer refunded {priceCents}c in full, piece stays in workshop stock.");
                return true;
            }
            string transferRefusal = asset.TransferOwnership(
                HouseholdFurnishingRegister.OwnerKindHousehold,
                HouseholdFurnishingRegister.OwnerIdForHousehold(buyer.HouseholdId),
                $"workshop furniture sale {asset.AssetId}", dayIndex);
            if (transferRefusal != null)
            {
                string refundRefusal = buyer.Ledger.RecordInflow(dayIndex, priceCents,
                    HouseholdIncomeSource.OtherDocumented, asset.AssetId,
                    $"refund of refused workshop furniture sale {asset.AssetId}: {transferRefusal}", workshop.InstanceId);
                string reverseRefusal = workshopCash.TryDebit(priceCents,
                    $"reversal: refused workshop furniture sale {asset.AssetId}");
                if (refundRefusal != null || reverseRefusal != null)
                {
                    error = $"CarpentryFurnitureSale DISPUTED: workshop furniture sale '{asset.AssetId}' transfer refused ({transferRefusal}); buyer refund {(refundRefusal == null ? "succeeded" : "FAILED: " + refundRefusal)}; workshop credit reversal {(reverseRefusal == null ? "succeeded" : "FAILED: " + reverseRefusal)}. Reconcile before retrying.";
                    return false;
                }
                diag.Add($"CarpentryFurnitureSale: workshop furniture sale '{asset.AssetId}' transfer refused ({transferRefusal}) — buyer refunded and workshop credit reversed in full, piece stays in workshop stock.");
                return true;
            }
            asset.LocationId = HouseholdFurnishingRegister.HouseholdCustodyLocationId(buyer.HouseholdId);
            string deregisterRefusal = workshopRegister.TryDeregisterAsset(asset.AssetId);
            if (deregisterRefusal != null)
            {
                // Near-impossible by construction (the asset came from
                // this register in this same call): the sale is otherwise
                // complete, so it stands and the stale workshop reference
                // is logged loudly — the cap counts one extra piece until
                // the reference is repaired, bounded to one piece.
                diag.Add($"CarpentryFurnitureSale DISPUTE: piece '{asset.AssetId}' sold to household {buyer.HouseholdId} but could not be deregistered from the workshop register ({deregisterRefusal}) — the sale stands; repair the stale workshop reference.");
            }
            string registerRefusal = buyer.Furnishings.TryRegisterAsset(asset);
            if (registerRefusal != null)
            {
                error = $"CarpentryFurnitureSale DISPUTED: piece '{asset.AssetId}' was paid for ({priceCents}c) and transferred to household {buyer.HouseholdId}, but the household register refused it ({registerRefusal}) — the asset is household-owned but unregistered. Re-register it; do not re-sell it.";
                return false;
            }
            diag.Add($"CarpentryFurnitureSale: day {dayIndex}: workshop sold '{asset.Kind}' ({asset.AssetId}) to household {buyer.HouseholdId} for {priceCents}c (want: {want.Reason}, priority {want.Priority}, {want.PersonsAffected} person(s) affected) — piece is now household-owned in household custody.");
            return true;
        }
    }
}
