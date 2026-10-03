using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-4 Group C: rentable assets held by a business — the livery keeper's
    /// rental rigs and teams (Canon Part V: LiveryFreight). Renting out reserves
    /// through the unified authority (Tech X §3.8): a rented rig cannot be
    /// double-booked, and the renter's custody is recorded as a grant
    /// (Tech X §3.7) so requirement checks see legitimate access.
    /// </summary>
    [Serializable]
    public sealed class RentalFleet
    {
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
        /// Rents out a fleet asset: reserves it and records the custody grant.
        /// Returns null on success, the refusal reason otherwise.
        /// </summary>
        public string RentOut(
            string assetId, string renterKind, string renterId, string renterName,
            int dueDayIndex, int feeCents, string termsNote,
            EquipmentReservationService reservations,
            EquipmentAccessResolver accessResolver,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!fleetAssetIds.Contains(assetId))
                return $"RentalFleet: asset {assetId} is not in this fleet.";
            if (IsRentedOut(assetId))
                return $"RentalFleet: asset {assetId} is already rented out — no double-booking (Tech X §3.8).";
            string refusal = reservations != null
                ? reservations.Reserve(EquipmentReservationService.AssetKey(assetId), renterId, "rental: " + assetId)
                : null;
            if (refusal != null)
            {
                diagnostics.Add(refusal);
                return refusal;
            }
            activeRentals.Add(new RentalRecord
            {
                AssetId = assetId, RenterKind = renterKind, RenterId = renterId,
                RenterName = renterName, DueDayIndex = dueDayIndex,
                FeeCents = Math.Max(0, feeCents), TermsNote = termsNote ?? string.Empty,
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
            diagnostics.Add($"RentalFleet: {assetId} rented to {renterName} for {feeCents}c, due day {dueDayIndex}.");
            return null;
        }

        /// <summary>
        /// Returns a rented asset: releases the reservation. The return condition
        /// is recorded in diagnostics — damage found on return is the renter's
        /// liability to settle through the business's normal repair/damage flow.
        /// </summary>
        public string Return(string assetId, float conditionOnReturn, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var rental = activeRentals.Find(r => r.AssetId == assetId);
            if (rental == null)
                return $"RentalFleet: asset {assetId} is not currently rented out.";
            activeRentals.Remove(rental);
            diagnostics.Add($"RentalFleet: {assetId} returned by {rental.RenterName} at condition {conditionOnReturn:0.00}.");
            return null;
        }
    }
}
