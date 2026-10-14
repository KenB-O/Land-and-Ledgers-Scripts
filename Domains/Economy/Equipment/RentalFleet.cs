using System;
using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-4 Group C: rentable assets held by a business — the livery keeper's
    /// rental rigs and teams (Canon Part V: LiveryFreight). Renting out reserves
    /// through the unified authority (Tech X §3.8): a rented rig cannot be
    /// double-booked, and the renter's custody is recorded as a grant
    /// (Tech X §3.7) so requirement checks see legitimate access.
    ///
    /// G-03: the rental fee is real money, not a recorded number. RentOut
    /// debits the renter's household ledger refusal-first (insufficient funds
    /// means no reservation is held) and credits the owning business runtime;
    /// Return settles assessed damage liability through the same legitimate
    /// cash legs. No payer authority and a positive fee means no rental —
    /// revenue is never manufactured.
    /// </summary>
    [Serializable]
    public sealed class RentalFleet
    {
        /// <summary>G-03: revenue category for rental fees and damage settlements.</summary>
        public const string RentalRevenueCategoryId = "equipment-rental";

        public string BusinessInstanceId = string.Empty;

        [Serializable]
        public sealed class RentalRecord
        {
            public string AssetId = string.Empty;
            public string RenterKind = string.Empty;
            public string RenterId = string.Empty;
            public string RenterName = string.Empty;
            public int DueDayIndex = -1;
            public int FeeCents;
            public string TermsNote = string.Empty;
            /// <summary>
            /// G-03: asset condition 0..1 when it left the fleet; negative =
            /// unknown (legacy rentals). Basis for the damage assessment on return.
            /// </summary>
            public float ConditionOut = -1f;
        }

        private readonly List<string> fleetAssetIds = new List<string>();
        private readonly List<RentalRecord> activeRentals = new List<RentalRecord>();

        public IReadOnlyList<string> FleetAssetIds => fleetAssetIds;
        public IReadOnlyList<RentalRecord> ActiveRentals => activeRentals;

        public void AddToFleet(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId) || fleetAssetIds.Contains(assetId)) return;
            fleetAssetIds.Add(assetId);
        }

        public bool IsRentedOut(string assetId) =>
            activeRentals.Exists(r => r.AssetId == assetId);

        /// <summary>
        /// Rents out a fleet asset: collects the fee through the legitimate cash
        /// authorities, reserves it, and records the custody grant.
        /// Returns null on success, the refusal reason otherwise.
        ///
        /// G-03 cash legs (refusal-first): the renter's household ledger is
        /// debited BEFORE any lasting state is taken; when the ledger refuses
        /// (insufficient funds) any hold already taken is released, so no
        /// reservation is held and nothing moves. The owning business runtime
        /// is credited only after a successful debit — exactly-once settlement.
        /// A positive fee with no cash authorities is refused: no payer, no
        /// rental. Zero-fee rentals (loaned/comped rigs) need no authorities.
        /// Reservation semantics are otherwise untouched.
        /// </summary>
        public string RentOut(
            string assetId, string renterKind, string renterId, string renterName,
            int dueDayIndex, int feeCents, string termsNote,
            EquipmentReservationService reservations,
            EquipmentAccessResolver accessResolver,
            List<string> diagnostics,
            HouseholdLedger renterLedger = null,
            BusinessRuntimeState ownerRuntime = null,
            int dayIndex = 0,
            float conditionAtRent = -1f)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!fleetAssetIds.Contains(assetId))
                return $"RentalFleet: asset {assetId} is not in this fleet.";
            if (IsRentedOut(assetId))
                return $"RentalFleet: asset {assetId} is already rented out — no double-booking (Tech X §3.8).";
            int fee = Math.Max(0, feeCents);
            if (fee > 0)
            {
                if (renterLedger == null)
                    return $"RentalFleet: asset {assetId} fee is {fee}c but no renter household ledger was supplied — cannot collect; nothing rented.";
                if (ownerRuntime == null)
                    return $"RentalFleet: asset {assetId} fee is {fee}c but no owning-business runtime was supplied — cannot credit; nothing rented.";
            }
            string refusal = reservations != null
                ? reservations.Reserve(EquipmentReservationService.AssetKey(assetId), renterId, "rental: " + assetId)
                : null;
            if (refusal != null)
            {
                diagnostics.Add(refusal);
                return refusal;
            }
            if (fee > 0)
            {
                string paymentRejection = renterLedger.RecordOutflow(dayIndex, fee,
                    $"rental fee: {assetId} to {renterName}, due day {dueDayIndex}",
                    BusinessInstanceId);
                if (paymentRejection != null)
                {
                    // Refusal-first: the hold is released, so no reservation is
                    // held and nothing moved.
                    reservations?.Release(EquipmentReservationService.AssetKey(assetId));
                    diagnostics.Add($"RentalFleet: {renterName} cannot pay the {fee}c rental fee — no reservation taken. {paymentRejection}");
                    return paymentRejection;
                }
                ownerRuntime.RecordDailyServiceRevenue(RentalRevenueCategoryId, 1, fee);
                diagnostics.Add($"RentalFleet: {fee}c rental fee collected from {renterName} to {BusinessInstanceId}.");
            }
            activeRentals.Add(new RentalRecord
            {
                AssetId = assetId, RenterKind = renterKind, RenterId = renterId,
                RenterName = renterName, DueDayIndex = dueDayIndex,
                FeeCents = fee, TermsNote = termsNote ?? string.Empty,
                ConditionOut = conditionAtRent,
            });
            accessResolver?.AddGrant(new EquipmentAccessGrant
            {
                AssetId = assetId,
                EquipmentKind = "rental-asset",
                HolderKind = renterKind, HolderId = renterId,
                Custody = EquipmentCustodyKind.Rented,
                GranterName = renterName,
                ExpiryDayIndex = dueDayIndex,
                TermsNote = termsNote ?? string.Empty,
            });
            diagnostics.Add($"RentalFleet: {assetId} rented to {renterName} for {fee}c, due day {dueDayIndex}.");
            return null;
        }

        /// <summary>
        /// Returns a rented asset: releases the reservation. The return condition
        /// is assessed against the condition recorded at rent-out; damage found on
        /// return is the renter's liability and settles through the same legitimate
        /// cash legs as the fee (renter ledger debit refusal-first, owning business
        /// credit) — the business's normal repair/damage flow, with the damage
        /// value basis supplied by the caller (the flow that knows the asset's
        /// value). When the renter cannot cover the damage, the liability is
        /// recorded loudly as UNPAID rather than silently forgiven or
        /// manufactured; the physical return still completes — the rig is back.
        /// Without a value basis or cash authorities, damage is logged, never
        /// invented.
        ///
        /// Pass VIII Worker 7: the unified-authority hold taken by RentOut is
        /// released here when the reservations service is supplied. Without
        /// the release the returned rig stayed reserved forever — it could
        /// never be rented or used again (a leaked hold on a shared asset).
        /// </summary>
        public string Return(string assetId, float conditionOnReturn, List<string> diagnostics,
            EquipmentReservationService reservations = null,
            HouseholdLedger renterLedger = null,
            BusinessRuntimeState ownerRuntime = null,
            int damageValueBasisCents = 0,
            int dayIndex = 0)
        {
            diagnostics = diagnostics ?? new List<string>();
            var rental = activeRentals.Find(r => r.AssetId == assetId);
            if (rental == null)
                return $"RentalFleet: asset {assetId} is not currently rented out.";
            activeRentals.Remove(rental);
            if (reservations != null)
            {
                reservations.Release(EquipmentReservationService.AssetKey(assetId));
                diagnostics.Add($"RentalFleet: {assetId} reservation released — the rig is rentable again.");
            }
            else
            {
                diagnostics.Add($"RentalFleet: {assetId} returned WITHOUT a reservations service — " +
                    "the unified-authority hold was NOT released; pass the service to free the rig.");
            }
            diagnostics.Add($"RentalFleet: {assetId} returned by {rental.RenterName} at condition {conditionOnReturn:0.00}.");

            // G-03: damage liability settlement through the existing damage flow.
            float conditionLoss = rental.ConditionOut >= 0f
                ? Math.Max(0f, rental.ConditionOut - conditionOnReturn)
                : 0f;
            if (conditionLoss > 0.001f)
            {
                int basis = Math.Max(0, damageValueBasisCents);
                int damageCents = (int)Math.Round(conditionLoss * basis, MidpointRounding.AwayFromZero);
                if (damageCents > 0 && basis > 0 && renterLedger != null && ownerRuntime != null)
                {
                    string damageRejection = renterLedger.RecordOutflow(dayIndex, damageCents,
                        $"rental damage liability: {assetId} returned at condition {conditionOnReturn:0.00} " +
                        $"vs {rental.ConditionOut:0.00} at rent-out",
                        BusinessInstanceId);
                    if (damageRejection == null)
                    {
                        ownerRuntime.RecordDailyServiceRevenue(RentalRevenueCategoryId, 1, damageCents);
                        diagnostics.Add($"RentalFleet: {damageCents}c damage liability settled by {rental.RenterName} to {BusinessInstanceId}.");
                    }
                    else
                    {
                        diagnostics.Add($"RentalFleet: {rental.RenterName} cannot cover the {damageCents}c damage liability — " +
                            $"recorded UNPAID, not forgiven. {damageRejection}");
                    }
                }
                else
                {
                    diagnostics.Add($"RentalFleet: {assetId} returned damaged (condition {rental.ConditionOut:0.00} → {conditionOnReturn:0.00}) — " +
                        "no damage value basis or cash authorities supplied; liability logged, never invented.");
                }
            }
            return null;
        }
    }
}
