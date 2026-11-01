using System;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Transport;

namespace LandLedgers.Economy.Freight
{
    /// <summary>
    /// Pass IV (Worker 2): wagon reconciliation — the documented adapter between
    /// the legacy freight-pool wagon model (<see cref="FreightWagon"/>) and the
    /// durable equipment-asset registry (<see cref="EquipmentAsset"/>, owned by
    /// <see cref="TransportAssetRegistry"/>).
    ///
    /// Audit finding: FreightWagon and EquipmentAsset wagons persisted
    /// independently with their own condition and reservation state — two truths
    /// for one physical wagon. Reconciliation rule (enforced by
    /// FreightResourcePool.AttachWagonRegistry/MigrateWagonsToRegistry):
    ///
    /// - IDENTITY: one wagon = one AssetId = the FreightWagon.WagonId. Migration
    ///   is additive and idempotent: legacy wagons missing from the registry are
    ///   imported once; registry assets are never overwritten by legacy copies.
    /// - CONDITION: the EquipmentAsset is the single truth. An attached
    ///   FreightWagon reads and writes condition through its asset
    ///   (FreightWagon.AttachAsset); the legacy serialized condition field is a
    ///   migration carrier, not a decision input.
    /// - RESERVATION: the unified reservation authority (key "asset:{wagonId}")
    ///   plus the asset's ReservedBy are the single truth. The pool's legacy
    ///   reservedWagonIds list is a save-compat mirror, re-derived from the
    ///   authority on capture.
    /// - OPERATIONAL STATUS (Available/OnRoute/InRepair/Retired) stays with the
    ///   freight pool: it is pool workflow state, and EquipmentAsset carries no
    ///   equivalent field. Status never contradicts reservation truth — a wagon
    ///   whose asset key is held cannot be selected as Available.
    ///
    /// Capacity mapping (documented, reversible): FreightWagon.CapacityUnits
    /// (default 600, mass-ish units) maps to PayloadMassCapacityKg 1:1, and to
    /// CargoVolumeCapacityM3 at CapacityUnits / VolumeDivisor (600 → 4 m³, the
    /// WagonAuthority default volume).
    ///
    /// Canon: XXXI Part III §§3.1-3.7 (one physical asset, one truth).
    /// </summary>
    public static class FreightWagonAssetAdapter
    {
        /// <summary>CapacityUnits per cubic meter of cargo volume (600 → 4 m³).</summary>
        public const float VolumeDivisor = 150f;

        /// <summary>Equipment kind assigned to migrated freight wagons.</summary>
        public const string MigratedWagonKind = "farm-freight-wagon";

        /// <summary>
        /// Converts a legacy freight wagon into a registry asset. Condition,
        /// capacity, and identity carry over; ownership is left empty (pool
        /// wagons predate ownership tracking — never invented).
        /// </summary>
        public static EquipmentAsset ToEquipmentAsset(FreightWagon wagon)
        {
            if (wagon == null) return null;
            var asset = new EquipmentAsset
            {
                AssetId = wagon.WagonId,
                Kind = MigratedWagonKind,
                DisplayName = string.IsNullOrWhiteSpace(wagon.DisplayName)
                    ? "Freight wagon " + wagon.WagonId
                    : wagon.DisplayName,
                Condition01 = wagon.Condition01,
                WagonSubtype = WagonSubtype.GeneralFarmFreight.ToString(),
                PayloadMassCapacityKg = Math.Max(1f, wagon.CapacityUnits),
                CargoVolumeCapacityM3 = Math.Max(0.1f, wagon.CapacityUnits / VolumeDivisor),
            };
            asset.RecordMaintenance(
                $"Migrated from legacy freight-pool wagon '{wagon.WagonId}' (Pass IV wagon reconciliation).", -1);
            return asset;
        }

        /// <summary>
        /// Builds a legacy-view FreightWagon over a registry asset (read model
        /// for pre-reconciliation readers). Condition comes from the asset.
        /// </summary>
        public static FreightWagon ViewFromAsset(EquipmentAsset asset)
        {
            if (asset == null) return null;
            var wagon = new FreightWagon(
                asset.AssetId,
                string.IsNullOrWhiteSpace(asset.DisplayName) ? asset.AssetId : asset.DisplayName,
                Math.Max(1, (int)asset.PayloadMassCapacityKg));
            wagon.AttachAsset(asset);
            return wagon;
        }

        /// <summary>
        /// Derives pool workflow status from asset truth: a held/assigned wagon
        /// is OnRoute; an unusable one needs repair; otherwise Available.
        /// Retired has no asset equivalent and stays pool-side.
        /// </summary>
        public static FreightWagonStatus DeriveStatus(EquipmentAsset asset, FreightWagonStatus current)
        {
            if (current == FreightWagonStatus.Retired) return FreightWagonStatus.Retired;
            if (asset == null) return current;
            if (asset.IsReserved || asset.IsTransportAssigned) return FreightWagonStatus.OnRoute;
            if (!asset.IsUsable) return FreightWagonStatus.InRepair;
            return FreightWagonStatus.Available;
        }
    }
}
