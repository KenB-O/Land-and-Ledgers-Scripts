using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// T2F: accommodation arrangements are ARRANGEMENTS, not tiers (canon).
    /// Owner-occupied, rental, room rental, boarding/lodging, employer lodging,
    /// kin hosting, doubled-up/shared, transient lodging, temporary camp,
    /// precarious accommodation, no stable shelter.
    /// </summary>
    public enum AccommodationArrangement
    {
        Unspecified = 0,
        OwnerOccupied = 1,
        Rental = 2,
        RoomRental = 3,
        BoardingOrLodging = 4,
        EmployerLodging = 5,
        KinHosting = 6,
        DoubledUpOrShared = 7,
        TransientLodging = 8,
        TemporaryCamp = 9,
        Precarious = 10,
        NoStableShelter = 11,
    }

    /// <summary>T2F: a building on a parcel.</summary>
    [Serializable]
    public sealed class Building
    {
        public string BuildingId = string.Empty;
        public string ParcelId = string.Empty;
        public string Kind = string.Empty; // house, barn, store, bunkhouse...
        public float Condition01 = 1f;
        public int BuiltDayIndex = -1;

        public Building() { }
    }

    /// <summary>
    /// T2F: usable space BELOW building granularity. A double house is one
    /// Building with two independent accommodation units (canon) — this class
    /// is what makes that representable.
    /// </summary>
    [Serializable]
    public sealed class AccommodationSpace
    {
        public string SpaceId = string.Empty;
        public string BuildingId = string.Empty;
        /// <summary>Beds/persons this space can sleep.</summary>
        public int SleepingCapacity;

        public AccommodationSpace() { }
    }

    /// <summary>
    /// T2F: who sleeps where. Residential Occupancy is DISTINCT from
    /// Accommodation Space, Building, Parcel — and from household membership:
    /// the person and the household are recorded SEPARATELY and may differ
    /// (a member may sleep elsewhere; a non-member may occupy).
    /// Canon chain: Person → Household → Residential Occupancy →
    /// Accommodation Space → Building → Parcel.
    /// </summary>
    [Serializable]
    public sealed class ResidentialOccupancy
    {
        public string OccupancyId = string.Empty;
        public int PersonId = -1;
        public int HouseholdId = -1; // recorded separately from the person's membership
        public string SpaceId = string.Empty;
        public AccommodationArrangement Arrangement = AccommodationArrangement.Unspecified;
        public int SinceDayIndex;
        public int UntilDayIndex = -1; // -1 = current

        public bool IsCurrent => UntilDayIndex < 0;

        public ResidentialOccupancy() { }
    }

    /// <summary>
    /// T2F: business-use space. One property may host several businesses and
    /// one business may use several sites (canon) — each assignment is one
    /// record, and a space holds one active assignment (no double-booking).
    /// </summary>
    [Serializable]
    public sealed class FunctionalSpaceAssignment
    {
        public string AssignmentId = string.Empty;
        public string SpaceId = string.Empty; // accommodation space OR building id when whole-building
        public string BusinessInstanceId = string.Empty;
        public string OperationDescription = string.Empty;
        public int SinceDayIndex;
        public int UntilDayIndex = -1;

        public bool IsActive => UntilDayIndex < 0;

        public FunctionalSpaceAssignment() { }
    }

    /// <summary>
    /// T2F: a property agreement — lease, rental, boarding terms. Parties are
    /// named; terms are recorded as written. Money agreements reference T2A
    /// instrument ids where they exist.
    /// </summary>
    [Serializable]
    public sealed class PropertyAgreement
    {
        public string AgreementId = string.Empty;
        public string Kind = string.Empty; // lease, rental, boarding, license
        public string GrantorName = string.Empty;
        public string GranteeName = string.Empty;
        public string SpaceOrParcelId = string.Empty;
        public string Terms = string.Empty;
        public int StartDayIndex;
        public int EndDayIndex = -1; // -1 = open-ended
        public string LinkedInstrumentId = string.Empty; // T2A instrument, when money is secured

        public PropertyAgreement() { }
    }

    /// <summary>
    /// T2F: the housing foundation — buildings, sub-building spaces,
    /// occupancy (distinct from membership), business-use assignments, and
    /// agreements. Includes the construction handoff: a completed building
    /// (FVS-4 FarmConstruction, builder projects) registers here.
    /// </summary>
    public sealed class HousingAuthority
    {
        private readonly Dictionary<string, Building> buildings =
            new Dictionary<string, Building>(StringComparer.Ordinal);
        private readonly Dictionary<string, AccommodationSpace> spaces =
            new Dictionary<string, AccommodationSpace>(StringComparer.Ordinal);
        private readonly Dictionary<string, ResidentialOccupancy> occupancies =
            new Dictionary<string, ResidentialOccupancy>(StringComparer.Ordinal);
        private readonly Dictionary<string, FunctionalSpaceAssignment> assignments =
            new Dictionary<string, FunctionalSpaceAssignment>(StringComparer.Ordinal);
        private readonly Dictionary<string, PropertyAgreement> agreements =
            new Dictionary<string, PropertyAgreement>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// T2F: construction handoff — a completed building enters the
        /// property system with its parcel link and provenance.
        /// </summary>
        public Building RegisterBuilding(string buildingId, string parcelId, string kind,
            int builtDayIndex, string provenance, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(buildingId) || string.IsNullOrWhiteSpace(parcelId))
            {
                diag.Add("HousingAuthority.RegisterBuilding: building and parcel ids required.");
                return null;
            }
            if (buildings.ContainsKey(buildingId))
            {
                diag.Add($"HousingAuthority.RegisterBuilding: building '{buildingId}' already registered.");
                return null;
            }
            var building = new Building
            {
                BuildingId = buildingId, ParcelId = parcelId,
                Kind = kind ?? string.Empty, BuiltDayIndex = builtDayIndex,
            };
            buildings[buildingId] = building;
            diag.Add($"HousingAuthority: building '{buildingId}' ({kind}) on parcel '{parcelId}' — {provenance}.");
            return building;
        }

        public AccommodationSpace DefineSpace(string buildingId, int sleepingCapacity, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!buildings.ContainsKey(buildingId))
            {
                diag.Add($"HousingAuthority.DefineSpace: unknown building '{buildingId}' — spaces hang off real buildings.");
                return null;
            }
            var space = new AccommodationSpace
            {
                SpaceId = $"space-{buildingId}-{sequence++}",
                BuildingId = buildingId,
                SleepingCapacity = Math.Max(0, sleepingCapacity),
            };
            spaces[space.SpaceId] = space;
            return space;
        }

        /// <summary>
        /// T2F: records who sleeps where. Person and household are separate
        /// fields — occupancy is NOT membership.
        /// </summary>
        public ResidentialOccupancy Occupy(
            int personId, int householdId, string spaceId,
            AccommodationArrangement arrangement, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!spaces.TryGetValue(spaceId, out AccommodationSpace space))
            {
                diag.Add($"HousingAuthority.Occupy: unknown space '{spaceId}'.");
                return null;
            }
            if (arrangement == AccommodationArrangement.Unspecified)
            {
                diag.Add("HousingAuthority.Occupy: the arrangement must be stated — arrangements are not tiers.");
                return null;
            }
            // End any current occupancy for this person (one bed at a time).
            foreach (ResidentialOccupancy occ in occupancies.Values)
            {
                if (occ.PersonId == personId && occ.IsCurrent) occ.UntilDayIndex = dayIndex;
            }
            var occupancy = new ResidentialOccupancy
            {
                OccupancyId = $"occ-{sequence++}",
                PersonId = personId,
                HouseholdId = householdId,
                SpaceId = spaceId,
                Arrangement = arrangement,
                SinceDayIndex = dayIndex,
            };
            occupancies[occupancy.OccupancyId] = occupancy;
            diag.Add($"HousingAuthority: P{personId} (H{householdId}) occupies '{spaceId}' as {arrangement}.");
            return occupancy;
        }

        public List<ResidentialOccupancy> CurrentOccupants(string spaceId)
        {
            var result = new List<ResidentialOccupancy>();
            foreach (ResidentialOccupancy occ in occupancies.Values)
            {
                if (occ.IsCurrent && string.Equals(occ.SpaceId, spaceId, StringComparison.Ordinal))
                    result.Add(occ);
            }
            return result;
        }

        /// <summary>
        /// T2F: assigns business use of a space. One active assignment per
        /// space — no double-booking. (Premises FIT is BIZ-1's job; this is
        /// the occupancy record.)
        /// </summary>
        public string AssignFunctionalSpace(
            string spaceId, string businessInstanceId, string operationDescription,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(spaceId) || string.IsNullOrWhiteSpace(businessInstanceId))
                return "HousingAuthority.AssignFunctionalSpace: space and business required.";
            foreach (FunctionalSpaceAssignment existing in assignments.Values)
            {
                if (existing.IsActive && string.Equals(existing.SpaceId, spaceId, StringComparison.Ordinal))
                    return $"HousingAuthority.AssignFunctionalSpace: '{spaceId}' already hosts '{existing.BusinessInstanceId}' — one active assignment per space.";
            }
            var assignment = new FunctionalSpaceAssignment
            {
                AssignmentId = $"fsa-{sequence++}",
                SpaceId = spaceId,
                BusinessInstanceId = businessInstanceId,
                OperationDescription = operationDescription ?? string.Empty,
                SinceDayIndex = dayIndex,
            };
            assignments[assignment.AssignmentId] = assignment;
            diag.Add($"HousingAuthority: '{spaceId}' assigned to '{businessInstanceId}' ({operationDescription}).");
            return null;
        }

        public PropertyAgreement RecordAgreement(
            string kind, string grantorName, string granteeName,
            string spaceOrParcelId, string terms, int startDayIndex, int endDayIndex,
            string linkedInstrumentId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(grantorName) || string.IsNullOrWhiteSpace(granteeName))
            {
                diag.Add("HousingAuthority.RecordAgreement: grantor and grantee must be named.");
                return null;
            }
            var agreement = new PropertyAgreement
            {
                AgreementId = $"pag-{sequence++}",
                Kind = kind ?? string.Empty,
                GrantorName = grantorName,
                GranteeName = granteeName,
                SpaceOrParcelId = spaceOrParcelId ?? string.Empty,
                Terms = terms ?? string.Empty,
                StartDayIndex = startDayIndex,
                EndDayIndex = endDayIndex,
                LinkedInstrumentId = linkedInstrumentId ?? string.Empty,
            };
            agreements[agreement.AgreementId] = agreement;
            diag.Add($"HousingAuthority: {kind} agreement {agreement.AgreementId} — {grantorName} → {granteeName}.");
            return agreement;
        }

        #region Save / Load
        [Serializable]
        public sealed class HousingSaveDto
        {
            public List<Building> Buildings = new List<Building>();
            public List<AccommodationSpace> Spaces = new List<AccommodationSpace>();
            public List<ResidentialOccupancy> Occupancies = new List<ResidentialOccupancy>();
            public List<FunctionalSpaceAssignment> Assignments = new List<FunctionalSpaceAssignment>();
            public List<PropertyAgreement> Agreements = new List<PropertyAgreement>();
        }

        public HousingSaveDto CaptureSaveDto()
        {
            return new HousingSaveDto
            {
                Buildings = new List<Building>(buildings.Values),
                Spaces = new List<AccommodationSpace>(spaces.Values),
                Occupancies = new List<ResidentialOccupancy>(occupancies.Values),
                Assignments = new List<FunctionalSpaceAssignment>(assignments.Values),
                Agreements = new List<PropertyAgreement>(agreements.Values),
            };
        }

        public void LoadFromSaveDto(HousingSaveDto dto)
        {
            buildings.Clear(); spaces.Clear(); occupancies.Clear(); assignments.Clear(); agreements.Clear();
            if (dto == null) return;
            foreach (var b in dto.Buildings) buildings[b.BuildingId] = b;
            foreach (var s in dto.Spaces) spaces[s.SpaceId] = s;
            foreach (var o in dto.Occupancies) occupancies[o.OccupancyId] = o;
            foreach (var a in dto.Assignments) assignments[a.AssignmentId] = a;
            foreach (var a in dto.Agreements) agreements[a.AgreementId] = a;
        }
        #endregion
    }
}
