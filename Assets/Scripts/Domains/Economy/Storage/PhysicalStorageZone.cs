using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Storage
{
    /// <summary>Describes exposure at a physical storage location.</summary>
    public enum StorageExposure
    {
        Indoor = 0,
        Covered = 1,
        Outdoor = 2,
    }

    /// <summary>A capacity axis remains unknown until real authored data is supplied.</summary>
    [Serializable]
    public struct StorageCapacityLimit
    {
        [SerializeField] private bool isKnown;
        [SerializeField, Min(0f)] private float value;

        public bool IsKnown => isKnown && IsFinite(value) && value >= 0f;
        public float Value => IsKnown ? value : 0f;

        public StorageCapacityLimit(bool known, float value)
        {
            isKnown = known;
            this.value = known && IsFinite(value) && value >= 0f ? value : 0f;
            if (known && (!IsFinite(value) || value < 0f)) isKnown = false;
        }

        public static StorageCapacityLimit Unknown => new StorageCapacityLimit(false, 0f);
        public static StorageCapacityLimit Known(float value) => new StorageCapacityLimit(true, value);

        private static bool IsFinite(float number) => !float.IsNaN(number) && !float.IsInfinity(number);
    }

    /// <summary>Caller-supplied physical dimensions for one existing inventory lot.</summary>
    [Serializable]
    public struct StorageLoadMeasurement
    {
        [SerializeField] private bool hasMass;
        [SerializeField, Min(0f)] private float massKg;
        [SerializeField] private bool hasVolume;
        [SerializeField, Min(0f)] private float volumeCubicMeters;

        public bool HasMass => hasMass && IsValid(massKg);
        public float MassKg => HasMass ? massKg : 0f;
        public bool HasVolume => hasVolume && IsValid(volumeCubicMeters);
        public float VolumeCubicMeters => HasVolume ? volumeCubicMeters : 0f;

        public StorageLoadMeasurement(bool hasMass, float massKg, bool hasVolume, float volumeCubicMeters)
        {
            this.hasMass = hasMass && IsValid(massKg);
            this.massKg = this.hasMass ? massKg : 0f;
            this.hasVolume = hasVolume && IsValid(volumeCubicMeters);
            this.volumeCubicMeters = this.hasVolume ? volumeCubicMeters : 0f;
        }

        public static StorageLoadMeasurement Unknown => new StorageLoadMeasurement(false, 0f, false, 0f);
        public static StorageLoadMeasurement MassOnly(float kilograms) => new StorageLoadMeasurement(true, kilograms, false, 0f);
        public static StorageLoadMeasurement VolumeOnly(float cubicMeters) => new StorageLoadMeasurement(false, 0f, true, cubicMeters);
        public static StorageLoadMeasurement MassAndVolume(float kilograms, float cubicMeters) =>
            new StorageLoadMeasurement(true, kilograms, true, cubicMeters);

        private static bool IsValid(float number) => !float.IsNaN(number) && !float.IsInfinity(number) && number >= 0f;
    }

    /// <summary>
    /// Stable reference to a lot owned by another inventory authority. This model
    /// records where that lot is physically placed; it does not copy its item,
    /// quantity, owner, custody, reservations, or transaction history.
    /// </summary>
    [Serializable]
    public sealed class StoredLotReference
    {
        [SerializeField] private string inventoryAuthorityId = string.Empty;
        [SerializeField] private string lotId = string.Empty;
        [SerializeField] private StorageLoadMeasurement load;

        public string InventoryAuthorityId => inventoryAuthorityId ?? string.Empty;
        public string LotId => lotId ?? string.Empty;
        public StorageLoadMeasurement Load => load;

        public StoredLotReference(string inventoryAuthorityId, string lotId, StorageLoadMeasurement load)
        {
            this.inventoryAuthorityId = (inventoryAuthorityId ?? string.Empty).Trim();
            this.lotId = (lotId ?? string.Empty).Trim();
            this.load = load;
        }

        public bool IdentifiesSameLot(string authorityId, string candidateLotId) =>
            string.Equals(InventoryAuthorityId, authorityId?.Trim(), StringComparison.Ordinal)
            && string.Equals(LotId, candidateLotId?.Trim(), StringComparison.Ordinal);
    }

    /// <summary>Storage-zone configuration; absent limits deliberately remain unknown.</summary>
    [Serializable]
    public sealed class PhysicalStorageZoneDefinition
    {
        [SerializeField] private string storageZoneId = string.Empty;
        [SerializeField] private string locationId = string.Empty;
        [SerializeField] private StorageExposure exposure;
        [SerializeField] private StorageCapacityLimit massCapacityKg;
        [SerializeField] private StorageCapacityLimit volumeCapacityCubicMeters;

        public string StorageZoneId => storageZoneId ?? string.Empty;
        public string LocationId => locationId ?? string.Empty;
        public StorageExposure Exposure => exposure;
        public StorageCapacityLimit MassCapacityKg => massCapacityKg;
        public StorageCapacityLimit VolumeCapacityCubicMeters => volumeCapacityCubicMeters;

        public PhysicalStorageZoneDefinition(string storageZoneId, string locationId, StorageExposure exposure,
            StorageCapacityLimit massCapacityKg, StorageCapacityLimit volumeCapacityCubicMeters)
        {
            this.storageZoneId = (storageZoneId ?? string.Empty).Trim();
            this.locationId = (locationId ?? string.Empty).Trim();
            this.exposure = exposure;
            this.massCapacityKg = massCapacityKg;
            this.volumeCapacityCubicMeters = volumeCapacityCubicMeters;
        }
    }

    /// <summary>
    /// Minimal physical-location and capacity ledger for references to existing
    /// inventory lots. It never becomes an inventory registry or changes stock.
    /// </summary>
    [Serializable]
    public sealed class PhysicalStorageZone
    {
        [SerializeField] private PhysicalStorageZoneDefinition definition;
        [SerializeField] private List<StoredLotReference> placements = new List<StoredLotReference>();
        [NonSerialized] private Func<string, string, bool> existingLotLookup;

        public PhysicalStorageZoneDefinition Definition => definition;
        public IReadOnlyList<StoredLotReference> Placements => placements;

        public PhysicalStorageZone(PhysicalStorageZoneDefinition definition,
            Func<string, string, bool> existingLotLookup = null)
        {
            this.definition = definition;
            this.existingLotLookup = existingLotLookup;
        }

        /// <summary>
        /// Adds a physical location reference. When a capacity axis is bounded,
        /// every existing and incoming lot must have a measurement for that axis;
        /// otherwise placement is refused rather than assuming a size.
        /// </summary>
        public bool TryPlace(StoredLotReference lot, out string refusal)
        {
            refusal = string.Empty;
            if (definition == null || string.IsNullOrWhiteSpace(definition.StorageZoneId)
                || string.IsNullOrWhiteSpace(definition.LocationId))
            {
                refusal = "Storage zone needs stable zone and location identifiers.";
                return false;
            }
            if (lot == null || string.IsNullOrWhiteSpace(lot.InventoryAuthorityId)
                || string.IsNullOrWhiteSpace(lot.LotId))
            {
                refusal = "Placement must reference an existing lot by inventory authority and lot id.";
                return false;
            }
            if (existingLotLookup == null)
            {
                refusal = "Cannot place lot: no existing-inventory lot lookup is connected.";
                return false;
            }
            if (!existingLotLookup(lot.InventoryAuthorityId, lot.LotId))
            {
                refusal = $"Cannot place lot: {lot.InventoryAuthorityId}/{lot.LotId} does not resolve in its inventory authority.";
                return false;
            }
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i] != null && placements[i].IdentifiesSameLot(lot.InventoryAuthorityId, lot.LotId))
                {
                    refusal = "This lot is already placed in the storage zone.";
                    return false;
                }
            }

            if (!CanFit(lot.Load, out refusal)) return false;
            placements.Add(lot);
            return true;
        }

        public bool Remove(string inventoryAuthorityId, string lotId)
        {
            int index = placements.FindIndex(p => p != null && p.IdentifiesSameLot(inventoryAuthorityId, lotId));
            if (index < 0) return false;
            placements.RemoveAt(index);
            return true;
        }

        public bool Contains(string inventoryAuthorityId, string lotId) =>
            placements.Exists(p => p != null && p.IdentifiesSameLot(inventoryAuthorityId, lotId));

        public StorageCapacitySnapshot CapacitySnapshot => new StorageCapacitySnapshot(
            SumMass(out bool massKnown), massKnown,
            SumVolume(out bool volumeKnown), volumeKnown,
            definition == null ? StorageCapacityLimit.Unknown : definition.MassCapacityKg,
            definition == null ? StorageCapacityLimit.Unknown : definition.VolumeCapacityCubicMeters);

        private bool CanFit(StorageLoadMeasurement incoming, out string refusal)
        {
            refusal = string.Empty;
            StorageCapacityLimit massLimit = definition.MassCapacityKg;
            StorageCapacityLimit volumeLimit = definition.VolumeCapacityCubicMeters;

            if (massLimit.IsKnown)
            {
                float used = 0f;
                for (int i = 0; i < placements.Count; i++)
                {
                    if (placements[i] == null || !placements[i].Load.HasMass)
                    {
                        refusal = "Cannot verify mass capacity: an existing placed lot has unknown mass.";
                        return false;
                    }
                    used += placements[i].Load.MassKg;
                }
                if (!incoming.HasMass)
                {
                    refusal = "Cannot verify mass capacity: incoming lot mass is unknown.";
                    return false;
                }
                if (used + incoming.MassKg > massLimit.Value + 0.0001f)
                {
                    refusal = $"Mass capacity exceeded: {used + incoming.MassKg} kg requested, {massLimit.Value} kg available.";
                    return false;
                }
            }

            if (volumeLimit.IsKnown)
            {
                float used = 0f;
                for (int i = 0; i < placements.Count; i++)
                {
                    if (placements[i] == null || !placements[i].Load.HasVolume)
                    {
                        refusal = "Cannot verify volume capacity: an existing placed lot has unknown volume.";
                        return false;
                    }
                    used += placements[i].Load.VolumeCubicMeters;
                }
                if (!incoming.HasVolume)
                {
                    refusal = "Cannot verify volume capacity: incoming lot volume is unknown.";
                    return false;
                }
                if (used + incoming.VolumeCubicMeters > volumeLimit.Value + 0.0001f)
                {
                    refusal = $"Volume capacity exceeded: {used + incoming.VolumeCubicMeters} m3 requested, {volumeLimit.Value} m3 available.";
                    return false;
                }
            }

            return true;
        }

        private float SumMass(out bool known)
        {
            float total = 0f;
            known = true;
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i] == null || !placements[i].Load.HasMass) { known = false; continue; }
                total += placements[i].Load.MassKg;
            }
            return total;
        }

        private float SumVolume(out bool known)
        {
            float total = 0f;
            known = true;
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i] == null || !placements[i].Load.HasVolume) { known = false; continue; }
                total += placements[i].Load.VolumeCubicMeters;
            }
            return total;
        }
    }

    public readonly struct StorageCapacitySnapshot
    {
        public readonly float CurrentMassKg;
        public readonly bool CurrentMassKnown;
        public readonly float CurrentVolumeCubicMeters;
        public readonly bool CurrentVolumeKnown;
        public readonly StorageCapacityLimit MassCapacityKg;
        public readonly StorageCapacityLimit VolumeCapacityCubicMeters;

        public StorageCapacitySnapshot(float currentMassKg, bool currentMassKnown, float currentVolumeCubicMeters,
            bool currentVolumeKnown, StorageCapacityLimit massCapacityKg, StorageCapacityLimit volumeCapacityCubicMeters)
        {
            CurrentMassKg = currentMassKg;
            CurrentMassKnown = currentMassKnown;
            CurrentVolumeCubicMeters = currentVolumeCubicMeters;
            CurrentVolumeKnown = currentVolumeKnown;
            MassCapacityKg = massCapacityKg;
            VolumeCapacityCubicMeters = volumeCapacityCubicMeters;
        }
    }
}
