using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-1: the unified reservation authority for equipment-side assets
    /// (Tech X §3.8) — significant assets, toolkits, and workstation instances.
    /// Generalizes the EQU-3 DraftPowerService pattern: one authority, no
    /// double-booking. Draft animals stay with DraftPowerService (identity lives
    /// in the HF-2 registry); everything equipment-side reserves here.
    ///
    /// The per-asset Reserve() methods remain for single-asset call sites; this
    /// service is the recommended path whenever more than one asset class is in
    /// play, because it refuses cross-class double-booking through one key space.
    /// </summary>
    public sealed class EquipmentReservationService
    {
        private readonly HashSet<string> reservedKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> reservationPurpose = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string AssetKey(string assetId) => "asset:" + assetId;
        public static string KitKey(string kitInstanceId) => "kit:" + kitInstanceId;
        public static string WorkstationKey(string workstationInstanceId) => "workstation:" + workstationInstanceId;

        public bool IsReserved(string key) =>
            !string.IsNullOrWhiteSpace(key) && reservedKeys.Contains(key);

        /// <summary>Reserves one key. Returns null on success, refusal reason otherwise.</summary>
        public string Reserve(string key, string reserverId, string purpose)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "EquipmentReservationService: reservation key is required.";
            if (string.IsNullOrWhiteSpace(reserverId))
                return "EquipmentReservationService: reserver id is required.";
            if (reservedKeys.Contains(key))
            {
                string what = reservationPurpose.TryGetValue(key, out var p) ? p : "unknown purpose";
                return $"Reservation refused: {key} is already reserved ({what}) — no double-booking (Tech X §3.8).";
            }
            reservedKeys.Add(key);
            reservationPurpose[key] = purpose ?? string.Empty;
            return null;
        }

        /// <summary>
        /// Reserves several keys atomically: all succeed or none do. A plow team
        /// that can't get both the plow and the workstation holds neither.
        /// </summary>
        public string ReserveAll(List<string> keys, string reserverId, string purpose, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (keys == null || keys.Count == 0)
            {
                diagnostics.Add("EquipmentReservationService: nothing to reserve.");
                return "EquipmentReservationService: nothing to reserve.";
            }
            foreach (string key in keys)
            {
                if (IsReserved(key))
                {
                    string reason = $"Reservation refused: {key} is already reserved — atomic set not taken (Tech X §3.8).";
                    diagnostics.Add(reason);
                    return reason;
                }
            }
            foreach (string key in keys)
            {
                string refusal = Reserve(key, reserverId, purpose);
                if (refusal != null)
                {
                    // Roll back the ones we just took (shouldn't happen after the pre-check, but stay honest).
                    foreach (string taken in keys)
                    {
                        if (taken == key) break;
                        Release(taken);
                    }
                    diagnostics.Add(refusal);
                    return refusal;
                }
            }
            return null;
        }

        public void Release(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            reservedKeys.Remove(key);
            reservationPurpose.Remove(key);
        }

        public void ReleaseAll(IEnumerable<string> keys)
        {
            if (keys == null) return;
            foreach (string key in keys) Release(key);
        }

        public int ReservedCount => reservedKeys.Count;
    }
}
