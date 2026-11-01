// Generic Business Reconstruction, section 8.
// Physical floor-space capacity: scalable, per-floor, caller-data
// driven. There is NO invented universal occupancy limit in this
// model. Capacity is computed from data the caller (building
// metadata / prefab authoring, via Unity AI) actually supplies:
// gross area per floor, usable fraction per floor, equipment
// footprints, workstation clearance, access/egress reserve, and
// storage. If no data is supplied for a factor, that factor imposes
// no constraint (it is unmodelled, not zero) - the model says so
// in its reasons instead of inventing a limit.
// Units: square metres (sqm) for area. Existing premises code uses
// square feet in places (PremisesRequirement.MinimumAreaSqFt);
// Unity AI must convert (1 sqm = 10.7639 sqft) when mapping, and
// must not mix the two units. This class works in sqm throughout.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    public sealed class BuildingFloor
    {
        public int FloorIndex;
        public float GrossAreaSqM;
        public float UsableFraction01 = 1f;
        public string Use = string.Empty;

        public float UsableAreaSqM
        {
            get
            {
                float f = UsableFraction01;
                if (f < 0f) f = 0f;
                if (f > 1f) f = 1f;
                return Math.Max(0f, GrossAreaSqM) * f;
            }
        }
    }

    public sealed class SpaceReservation
    {
        public string ReservationId = string.Empty;
        public string Activity = string.Empty;
        public int FloorIndex;
        public float AreaSqM;
        public string EquipmentId = string.Empty;
    }

    public sealed class PremisesCapacity
    {
        private readonly List<BuildingFloor> floors = new List<BuildingFloor>();
        private readonly List<SpaceReservation> reservations = new List<SpaceReservation>();
        private readonly List<Tuple<string, string>> incompatiblePairs = new List<Tuple<string, string>>();

        public float AccessEgressReserveSqM;
        public float StorageReserveSqM;

        public IReadOnlyList<BuildingFloor> Floors => floors;
        public IReadOnlyList<SpaceReservation> Reservations => reservations;

        public void AddFloor(BuildingFloor floor)
        {
            if (floor == null) return;
            floors.RemoveAll(f => f.FloorIndex == floor.FloorIndex);
            floors.Add(floor);
        }

        /// <summary>
        /// Declare two activities incompatible on the same floor
        /// (caller data, e.g. from building authoring). With no declared
        /// pairs, activities are compatible: mixed use is allowed by
        /// default, and only real conflicts block. This is the opposite
        /// of a universal occupancy rule.
        /// </summary>
        public void DeclareIncompatible(string activityA, string activityB)
        {
            if (string.IsNullOrWhiteSpace(activityA) || string.IsNullOrWhiteSpace(activityB)) return;
            incompatiblePairs.Add(Tuple.Create(activityA.Trim(), activityB.Trim()));
        }

        public float UsableAreaSqM(int floorIndex)
        {
            foreach (BuildingFloor f in floors)
            {
                if (f.FloorIndex == floorIndex) return f.UsableAreaSqM;
            }
            return 0f;
        }

        public float ReservedAreaSqM(int floorIndex)
        {
            float total = 0f;
            foreach (SpaceReservation r in reservations)
            {
                if (r.FloorIndex == floorIndex) total += Math.Max(0f, r.AreaSqM);
            }
            return total;
        }

        public float FreeAreaSqM(int floorIndex)
        {
            float free = UsableAreaSqM(floorIndex) - ReservedAreaSqM(floorIndex);
            if (floorIndex == 0)
            {
                free -= Math.Max(0f, AccessEgressReserveSqM) + Math.Max(0f, StorageReserveSqM);
            }
            return free;
        }

        public float TotalFreeAreaSqM()
        {
            float total = 0f;
            foreach (BuildingFloor f in floors) total += FreeAreaSqM(f.FloorIndex);
            return total;
        }

        /// <summary>
        /// Try to place equipment / a work area on a floor. Refuses
        /// with a named reason when: the floor does not exist, the
        /// area exceeds free usable space, the reservation id is
        /// already used, or a declared incompatible activity already
        /// occupies that floor. Otherwise the reservation is recorded
        /// and the space is no longer free. A 10 m x 20 m building is
        /// 200 sqm gross per floor; hundreds of chairs at, say, 1 sqm
        /// each plus clearance will simply not fit, by arithmetic,
        /// not by an invented chair limit.
        /// </summary>
        public bool TryReserve(SpaceReservation reservation, out string reason)
        {
            reason = string.Empty;
            if (reservation == null || string.IsNullOrWhiteSpace(reservation.ReservationId))
            {
                reason = "Space reservation needs an id.";
                return false;
            }
            bool floorExists = false;
            foreach (BuildingFloor f in floors)
            {
                if (f.FloorIndex == reservation.FloorIndex) { floorExists = true; break; }
            }
            if (!floorExists)
            {
                reason = $"Floor {reservation.FloorIndex} does not exist in this building.";
                return false;
            }
            foreach (SpaceReservation existing in reservations)
            {
                if (string.Equals(existing.ReservationId, reservation.ReservationId, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"Reservation id already used: {reservation.ReservationId}.";
                    return false;
                }
            }
            foreach (Tuple<string, string> pair in incompatiblePairs)
            {
                foreach (SpaceReservation existing in reservations)
                {
                    if (existing.FloorIndex != reservation.FloorIndex) continue;
                    bool conflict = (string.Equals(pair.Item1, reservation.Activity, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(pair.Item2, existing.Activity, StringComparison.OrdinalIgnoreCase))
                        || (string.Equals(pair.Item2, reservation.Activity, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(pair.Item1, existing.Activity, StringComparison.OrdinalIgnoreCase));
                    if (conflict)
                    {
                        reason = $"Activity {reservation.Activity} is incompatible with {existing.Activity} on floor {reservation.FloorIndex}.";
                        return false;
                    }
                }
            }
            if (reservation.AreaSqM > FreeAreaSqM(reservation.FloorIndex) + 0.001f)
            {
                reason = $"Insufficient usable space on floor {reservation.FloorIndex}: need {reservation.AreaSqM} sqm, have {FreeAreaSqM(reservation.FloorIndex)} sqm free.";
                return false;
            }
            reservations.Add(reservation);
            return true;
        }

        public bool Release(string reservationId)
        {
            if (string.IsNullOrWhiteSpace(reservationId)) return false;
            int removed = reservations.RemoveAll(r => string.Equals(r.ReservationId, reservationId.Trim(), StringComparison.OrdinalIgnoreCase));
            return removed > 0;
        }
    }
}
