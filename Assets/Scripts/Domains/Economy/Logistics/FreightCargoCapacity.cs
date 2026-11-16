// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// DEF-016 (Decision 10): the single authority for freight trip math —
// how many units of a cargo fit one wagon, and how many wagon trips a
// load needs. The quote calculator, the construction freight plan
// factory, and the freight coordinator all use this, so the trip count
// is never re-derived three ways. Pure static, no Unity objects.
//
// A zero per-unit measure means that dimension does not bind (a
// weightless or volumeless cargo fits any number of units on that
// dimension); a zero wagon capacity binds to zero. MaxUnitsPerTrip
// returns 0 when the wagon cannot carry this cargo at all, and
// TripsNeeded returns -1 for the same case.

using System;
using LandLedgers.Economy.Blacksmith;

namespace LandLedgers.Economy.Logistics
{
    public static class FreightCargoCapacity
    {
        /// <summary>
        /// Validates a cargo specification. Moved verbatim from
        /// FreightShipmentService.ValidateCargoSpec so the quote
        /// calculator (which prices before any shipment exists) and the
        /// shipment service share one rule and one wording.
        /// </summary>
        public static string ValidateCargoSpec(FreightCargoSpec cargo)
        {
            if (cargo == null) return "FreightShipmentService: cargo specification is missing.";
            if (float.IsNaN(cargo.MassKgPerUnit) || float.IsInfinity(cargo.MassKgPerUnit) || cargo.MassKgPerUnit < 0f)
                return "FreightShipmentService: cargo mass per unit must be a finite, non-negative value; shipment owns zero cargo.";
            if (float.IsNaN(cargo.VolumeM3PerUnit) || float.IsInfinity(cargo.VolumeM3PerUnit) || cargo.VolumeM3PerUnit < 0f)
                return "FreightShipmentService: cargo volume per unit must be a finite, non-negative value; shipment owns zero cargo.";
            return null;
        }

        /// <summary>
        /// Validates a wagon's capacities. A wagon with a negative or
        /// non-finite capacity is corrupt data, never a wagon.
        /// </summary>
        public static string ValidateWagonCapacity(float massCapacityKg, float volumeCapacityM3)
        {
            if (float.IsNaN(massCapacityKg) || float.IsInfinity(massCapacityKg) || massCapacityKg < 0f
                || float.IsNaN(volumeCapacityM3) || float.IsInfinity(volumeCapacityM3) || volumeCapacityM3 < 0f)
                return "FreightCargoCapacity: wagon capacities must be finite, non-negative values.";
            return null;
        }

        /// <summary>
        /// How many units of this cargo fit one wagon: the tighter of the
        /// mass bound and the volume bound (the shipment service enforces
        /// both). A zero per-unit measure does not bind that dimension.
        /// Returns 0 when the wagon cannot carry even one unit.
        /// </summary>
        public static int MaxUnitsPerTrip(FreightCargoSpec cargo, EquipmentAsset wagon)
        {
            if (cargo == null || wagon == null) return 0;
            return MaxUnitsPerTrip(cargo, wagon.PayloadMassCapacityKg, wagon.CargoVolumeCapacityM3);
        }

        /// <summary>Capacity-only form, for callers that hold raw capacities (the quote calculator).</summary>
        public static int MaxUnitsPerTrip(FreightCargoSpec cargo, float massCapacityKg, float volumeCapacityM3)
        {
            if (cargo == null) return 0;
            if (ValidateCargoSpec(cargo) != null) return 0;
            if (ValidateWagonCapacity(massCapacityKg, volumeCapacityM3) != null) return 0;
            int byMass = cargo.MassKgPerUnit > 0f
                ? (int)Math.Floor(massCapacityKg / cargo.MassKgPerUnit)
                : int.MaxValue;
            int byVolume = cargo.VolumeM3PerUnit > 0f
                ? (int)Math.Floor(volumeCapacityM3 / cargo.VolumeM3PerUnit)
                : int.MaxValue;
            int perTrip = Math.Min(byMass, byVolume);
            return Math.Max(0, perTrip);
        }

        /// <summary>
        /// How many wagon trips a load of this size needs. Returns -1 when
        /// the wagon cannot carry this cargo at all (MaxUnitsPerTrip 0),
        /// 0 when there is nothing to carry.
        /// </summary>
        public static int TripsNeeded(int quantityUnits, FreightCargoSpec cargo, EquipmentAsset wagon)
        {
            if (quantityUnits <= 0) return 0;
            int perTrip = MaxUnitsPerTrip(cargo, wagon);
            if (perTrip <= 0) return -1;
            // Ceiling division in long arithmetic: perTrip can be
            // int.MaxValue (a cargo that binds on neither dimension),
            // and quantityUnits + perTrip - 1 overflows an int there.
            return (int)((quantityUnits + (long)perTrip - 1) / perTrip);
        }

        /// <summary>Capacity-only form, for callers that hold raw capacities (the quote calculator).</summary>
        public static int TripsNeeded(int quantityUnits, FreightCargoSpec cargo,
            float massCapacityKg, float volumeCapacityM3)
        {
            if (quantityUnits <= 0) return 0;
            int perTrip = MaxUnitsPerTrip(cargo, massCapacityKg, volumeCapacityM3);
            if (perTrip <= 0) return -1;
            // Ceiling division in long arithmetic — see the overload above.
            return (int)((quantityUnits + (long)perTrip - 1) / perTrip);
        }
    }
}
