using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using UnityEngine;

namespace LandLedgers.Economy.Wheelwright
{
    /// <summary>
    /// D3A: one worn wagon the shop bought to flip. Canon §7.2F: "A shop may
    /// buy broken or worn wagons/equipment, repair them and resell them, or
    /// dismantle uneconomic assets for parts/scrap. Used-equipment commerce
    /// therefore grows naturally out of the maintenance and repair systems."
    /// Refurbishment (same section) "combines multiple repairs to restore a
    /// used asset for continued use or resale" — the asset keeps its identity
    /// and its maintenance history feeds diligence/resale (Tech X §3.3).
    /// </summary>
    [Serializable]
    public sealed class UsedWagonRecord
    {
        public EquipmentAsset Asset;
        public int AcquisitionCents;      // what the shop paid the seller
        public string SellerName = string.Empty;
        public int AcquiredDayIndex;
        public bool RefurbishedForResale;
        public int RefurbCostCents;       // materials the refurbishment consumed, at cost
        public int RefurbDayIndex = -1;

        public int TotalCostBasisCents => Math.Max(0, AcquisitionCents) + Math.Max(0, RefurbCostCents);
    }

    /// <summary>
    /// D3A: the shop's used-wagon yard — bought wrecks awaiting refurbishment
    /// or the dismantling bench. Pure record-keeping; the WheelwrightRuntime
    /// performs the material-consuming work (it owns the lumber/ironwork).
    ///
    /// Money note: the runtime books revenue counters, not a shop cash
    /// ledger — acquisition cost is tracked here as cost basis, so resale
    /// margin (price − acquisition − refurb cost) is real. The actual cash
    /// movement for a purchase is the operator's step outside this runtime.
    /// </summary>
    public sealed class WheelwrightUsedWagonYard
    {
        /// <summary>Calibration: the shop only buys wagons worn below this. Tuning, not canon.</summary>
        public const float WornAcquisitionCeiling01 = 0.75f;

        /// <summary>Calibration: dismantling yields this many usable wheels. Tuning, not canon.</summary>
        public const int DismantleWheelYield = 2;

        /// <summary>Calibration: dismantling yields this many usable axles. Tuning, not canon.</summary>
        public const int DismantleAxleYield = 1;

        /// <summary>Wagon kinds the yard trades (the shop's own build recipes).</summary>
        public static readonly string[] WagonKinds = { "farm-wagon", "freight-wagon", "spring-wagon", "dray" };

        private readonly Dictionary<string, UsedWagonRecord> records =
            new Dictionary<string, UsedWagonRecord>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, UsedWagonRecord> Records => records;
        public int Count => records.Count;

        public static bool IsWagonKind(string kind)
        {
            foreach (string k in WagonKinds)
                if (string.Equals(k, kind, StringComparison.Ordinal))
                    return true;
            return false;
        }

        public UsedWagonRecord Find(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId)) return null;
            records.TryGetValue(assetId, out UsedWagonRecord record);
            return record;
        }

        /// <summary>
        /// Takes a worn wagon into the yard. Refuses pristine wagons (nothing
        /// to flip), non-wagons, reserved assets, and anonymous sellers.
        /// </summary>
        public string AcquireWornWagon(EquipmentAsset asset, int acquisitionCents,
            string sellerName, string shopBusinessId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (asset == null) return "WheelwrightUsedWagonYard: no wagon supplied.";
            if (!IsWagonKind(asset.Kind))
                return $"WheelwrightUsedWagonYard: '{asset.Kind}' is not a wagon kind — the yard flips wagons, not everything.";
            if (asset.Condition01 >= WornAcquisitionCeiling01)
                return $"WheelwrightUsedWagonYard: {asset.AssetId} is at {asset.Condition01:P0} — buy worn wagons to flip, not sound ones.";
            if (asset.IsReserved)
                return $"WheelwrightUsedWagonYard: {asset.AssetId} is reserved by {asset.ReservedBy} — no double-dealing (Tech X §3.8).";
            if (acquisitionCents < 0) return "WheelwrightUsedWagonYard: acquisition price cannot be negative.";
            if (string.IsNullOrWhiteSpace(sellerName))
                return "WheelwrightUsedWagonYard: the seller must be named — no anonymous wrecks.";
            if (records.ContainsKey(asset.AssetId))
                return $"WheelwrightUsedWagonYard: {asset.AssetId} is already in the yard.";

            string transferRejection = asset.TransferOwnership("business", shopBusinessId,
                $"used-wagon purchase from {sellerName}");
            if (transferRejection != null)
                return $"WheelwrightUsedWagonYard: {transferRejection}";

            records[asset.AssetId] = new UsedWagonRecord
            {
                Asset = asset,
                AcquisitionCents = acquisitionCents,
                SellerName = sellerName,
                AcquiredDayIndex = dayIndex,
            };
            diagnostics.Add(
                $"WheelwrightUsedWagonYard: bought {asset.DisplayName} ({asset.AssetId}) at {asset.Condition01:P0} " +
                $"from {sellerName} for {acquisitionCents}c.");
            return null;
        }

        public string RemoveRecord(string assetId)
        {
            if (!records.Remove(assetId))
                return $"WheelwrightUsedWagonYard: '{assetId}' is not in the yard.";
            return null;
        }

        // ---------- save DTO (inside the owning yard class) ----------

        [Serializable]
        public sealed class UsedWagonYardDto
        {
            public List<UsedWagonRecord> Records = new List<UsedWagonRecord>();
        }

        public UsedWagonYardDto ToSaveDto()
        {
            var dto = new UsedWagonYardDto();
            foreach (var kv in records)
                dto.Records.Add(kv.Value);
            return dto;
        }

        public void LoadFromSaveDto(UsedWagonYardDto dto)
        {
            records.Clear();
            if (dto == null) return;
            foreach (UsedWagonRecord r in dto.Records)
            {
                if (r == null || r.Asset == null || string.IsNullOrWhiteSpace(r.Asset.AssetId)) continue;
                records[r.Asset.AssetId] = r;
            }
        }
    }
}
