using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// Lane 9e: retail stock reservations on the EXISTING unified reservation
    /// authority (IUnifiedReservationAuthority / EquipmentReservationService).
    /// No second reservation system: the authority's own capacity +
    /// first-wins + time-window semantics do the work, with one resource
    /// key per (supplier, item) and one key per yard lot.
    ///
    /// How it is used (HouseholdShoppingLoop): EvaluateCandidates nets the
    /// live holds out of ItemStockUnits at the departure instant (available
    /// to promise); the trip then reserves its planned store units at commit
    /// (owner = trip id, window = departure to return + margin) and releases
    /// on abort, completion, or right after the counter settlement. The
    /// settle-time stock check and refund path remain the backstop for
    /// legacy / direct-sale paths that never reserve.
    ///
    /// Capacity is the live stock: re-registering it on every read keeps
    /// the authority in step with sales and restocks. A stock drop below
    /// already-held totals floors available at 0 (fail-closed, never
    /// negative). Capacity registration needs the concrete service (the
    /// EquipmentCapacityService precedent: cast, skip when absent); a
    /// missing registration leaves the exclusive default capacity of 1,
    /// so a multi-unit hold is refused rather than over-booked.
    /// </summary>
    public static class RetailStockReservation
    {
        public const float GameSecondsPerDay = 86400f;

        /// <summary>Absolute game seconds for a (day, minute-of-day) pair, matching TimeManager.</summary>
        public static float GameSeconds(int dayIndex, int minuteOfDay)
        {
            return Math.Max(0, dayIndex) * GameSecondsPerDay + Math.Max(0, minuteOfDay) * 60f;
        }

        /// <summary>
        /// Units of this supplier's item that are free to promise at the given
        /// instant: live stock minus live holds overlapping that instant.
        /// Null authority means unwired (no net-out; the settle-time check
        /// remains the only guard).
        /// </summary>
        public static int AvailableUnits(
            IUnifiedReservationAuthority authority,
            string supplierBusinessId,
            string itemId,
            int currentStockUnits,
            float atGameSeconds)
        {
            int stock = Math.Max(0, currentStockUnits);
            if (authority == null || stock <= 0
                || string.IsNullOrWhiteSpace(supplierBusinessId)
                || string.IsNullOrWhiteSpace(itemId))
            {
                return stock;
            }

            string key = ReservationKeyConventions.RetailStock(supplierBusinessId, itemId);
            RegisterCapacity(authority, key, stock);
            return (int)Math.Floor(Math.Max(0f, authority.AvailableCapacity(key, atGameSeconds)));
        }

        /// <summary>
        /// Reserves planned store units for one trip. Returns the reservation
        /// id, or null when the authority is absent, the request is empty,
        /// or the authority refuses (another trip holds the units). A
        /// refusal names the conflicting holder in diagnostics; the caller
        /// must fail the trip honestly — never sell against a refused hold.
        /// currentStockUnits is re-read at commit time so the capacity the
        /// hold is checked against is the stock at commit, not at evaluation.
        /// </summary>
        public static string TryReserveUnits(
            IUnifiedReservationAuthority authority,
            string supplierBusinessId,
            string itemId,
            int units,
            int currentStockUnits,
            string ownerTaskId,
            string ownerLabel,
            float startGameSeconds,
            float endGameSeconds,
            List<string> diagnostics)
        {
            if (authority == null || units <= 0 || currentStockUnits <= 0
                || string.IsNullOrWhiteSpace(supplierBusinessId)
                || string.IsNullOrWhiteSpace(itemId)
                || string.IsNullOrWhiteSpace(ownerTaskId))
            {
                return null;
            }

            string key = ReservationKeyConventions.RetailStock(supplierBusinessId, itemId);
            RegisterCapacity(authority, key, Math.Max(0, currentStockUnits));
            var request = new ReservationRequest(ownerTaskId, ownerLabel ?? string.Empty);
            request.Items.Add(new ReservationItem(key, units, startGameSeconds, endGameSeconds));
            string refusal = authority.TryReserveAll(request, out ReservationHandle handle);
            if (refusal != null)
            {
                diagnostics?.Add($"Retail stock reservation refused for '{supplierBusinessId}:{itemId}' x{units}: {refusal}");
                return null;
            }

            return handle != null ? handle.ReservationId : null;
        }

        /// <summary>
        /// Units of one yard lot held by the authority at the given instant,
        /// for the quote/sale split: a quote reserves, the sale withdraws
        /// only the unreserved remainder. Null authority means unwired.
        /// </summary>
        public static int ReservedUnitsForLot(
            IUnifiedReservationAuthority authority,
            string lotId,
            int lotUnits,
            float atGameSeconds)
        {
            int units = Math.Max(0, lotUnits);
            if (authority == null || units <= 0 || string.IsNullOrWhiteSpace(lotId))
            {
                return 0;
            }

            string key = ReservationKeyConventions.Lot(lotId);
            RegisterCapacity(authority, key, units);
            float available = Math.Max(0f, authority.AvailableCapacity(key, atGameSeconds));
            return Math.Max(0, units - (int)Math.Floor(available));
        }

        /// <summary>
        /// Store units held by one shopping-trip owner for one SKU at the
        /// given instant. This read-only view lets household need forecasts
        /// credit the correct in-flight trip while keeping its stock hold
        /// unavailable to every other household.
        /// </summary>
        public static int ReservedUnitsForOwner(
            IUnifiedReservationAuthority authority,
            string supplierBusinessId,
            string itemId,
            string ownerTaskId,
            float atGameSeconds)
        {
            if (authority == null || string.IsNullOrWhiteSpace(supplierBusinessId)
                || string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(ownerTaskId))
            {
                return 0;
            }

            string resourceKey = ReservationKeyConventions.RetailStock(supplierBusinessId, itemId);
            List<ReservationHandle> handles = authority.ReservationsForTask(ownerTaskId);
            if (handles == null) return 0;

            int reserved = 0;
            foreach (ReservationHandle handle in handles)
            {
                if (handle?.Items == null) continue;
                foreach (ReservationItem reservation in handle.Items)
                {
                    if (reservation == null
                        || !string.Equals(reservation.ResourceKey, resourceKey, StringComparison.Ordinal)
                        || reservation.StartGameSeconds > atGameSeconds
                        || atGameSeconds >= reservation.EndGameSeconds)
                    {
                        continue;
                    }

                    reserved += (int)Math.Floor(Math.Max(0f, reservation.Quantity));
                }
            }

            return Math.Max(0, reserved);
        }

        /// <summary>Releases one reservation by id. No-op when unknown or unwired (idempotent).</summary>
        public static void Release(IUnifiedReservationAuthority authority, string reservationId)
        {
            if (authority == null || string.IsNullOrWhiteSpace(reservationId))
            {
                return;
            }

            authority.Release(reservationId);
        }

        /// <summary>
        /// Units of one yard lot held by OTHER owners at the given instant:
        /// the caller's own quote hold never blocks its own sale (the
        /// authority's own first-wins check already treats same-owner holds
        /// as one logical holder). Null authority means unwired.
        /// </summary>
        public static int ReservedUnitsForLotExcludingOwner(
            IUnifiedReservationAuthority authority,
            string lotId,
            int lotUnits,
            float atGameSeconds,
            string excludeOwnerTaskId)
        {
            int total = ReservedUnitsForLot(authority, lotId, lotUnits, atGameSeconds);
            if (total <= 0 || authority == null || string.IsNullOrWhiteSpace(excludeOwnerTaskId))
            {
                return total;
            }

            int own = 0;
            string key = ReservationKeyConventions.Lot(lotId);
            foreach (ReservationHandle handle in authority.ReservationsForTask(excludeOwnerTaskId))
            {
                if (handle == null || handle.Items == null) continue;
                foreach (ReservationItem item in handle.Items)
                {
                    if (item == null) continue;
                    if (!string.Equals(item.ResourceKey, key, StringComparison.Ordinal)) continue;
                    if (item.StartGameSeconds <= atGameSeconds && atGameSeconds < item.EndGameSeconds)
                    {
                        own += (int)Math.Floor(item.Quantity);
                    }
                }
            }

            return Math.Max(0, total - own);
        }

        private static void RegisterCapacity(IUnifiedReservationAuthority authority, string resourceKey, float capacity)
        {
            if (capacity <= 0f)
            {
                return;
            }

            if (authority is EquipmentReservationService concrete)
            {
                concrete.RegisterResourceCapacity(resourceKey, capacity);
            }
        }
    }
}
