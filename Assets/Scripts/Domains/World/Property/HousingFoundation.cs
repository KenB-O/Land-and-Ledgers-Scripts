using System;
using System.Collections.Generic;
using LandLedgers.Population;
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
        /// <summary>
        /// Pass VIII Worker 2 (additive): the property agreement this
        /// occupancy was created under (empty = none — e.g. informal kin
        /// hosting or a legacy record). Lets closure, transfer and the
        /// absence sweep resolve occupancies through their agreements
        /// instead of orphaning them.
        /// </summary>
        public string AgreementId = string.Empty;

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
        /// <summary>
        /// Pass III Worker B (additive): payment terms for rent/board tenancies.
        /// Null = this agreement carries no payment schedule (e.g. kin hosting,
        /// employer lodging with no charge, owner occupation). When set, the
        /// tenancy payment loop turns the terms into due events; money moves
        /// only through HouseholdLedger + FinancialObligationAuthority —
        /// there is no second rent-cash authority (Canon XXXI Rev XXIII §1.1,
        /// one authority per consequential fact).
        /// </summary>
        public TenancyPaymentTerms PaymentTerms;

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

        /// <summary>
        /// Pass VIII Worker 2 (additive): commercial lodging arrangements
        /// are capacity-guarded — a room cannot be rented beyond its beds.
        /// Private/voluntary arrangements (kin hosting, doubled-up, camp,
        /// precarious) stay permissive per Canon 12.7 freedom with
        /// consequence: overcrowding there is recorded as unsuitable by the
        /// shelter-suitability evaluator, never refused at the door.
        /// </summary>
        public static bool IsCommercialLodgingArrangement(AccommodationArrangement arrangement)
        {
            return arrangement == AccommodationArrangement.Rental
                || arrangement == AccommodationArrangement.RoomRental
                || arrangement == AccommodationArrangement.BoardingOrLodging
                || arrangement == AccommodationArrangement.TransientLodging
                || arrangement == AccommodationArrangement.EmployerLodging;
        }

        /// <summary>
        /// Pass VIII Worker 2 (additive): the guarded occupancy path for
        /// tenancies and boarding. Refuses unknown spaces, unstated
        /// arrangements, and — for commercial lodging arrangements —
        /// occupancy beyond the space's sleeping capacity. Links the
        /// occupancy to its property agreement so closure, transfer and
        /// the absence sweep can resolve it. Returns null on success, the
        /// refusal reason otherwise; the created occupancy comes back via
        /// the out parameter.
        /// </summary>
        public string TryOccupyUnderAgreement(
            int personId, int householdId, string spaceId,
            AccommodationArrangement arrangement, int dayIndex, string agreementId,
            List<string> diag, out ResidentialOccupancy occupancy)
        {
            diag = diag ?? diagnostics;
            occupancy = null;
            if (!spaces.TryGetValue(spaceId, out AccommodationSpace space))
                return $"HousingAuthority.TryOccupyUnderAgreement: unknown space '{spaceId}' — a household cannot occupy nonexistent accommodation.";
            if (arrangement == AccommodationArrangement.Unspecified)
                return "HousingAuthority.TryOccupyUnderAgreement: the arrangement must be stated — arrangements are not tiers.";
            if (IsCommercialLodgingArrangement(arrangement))
            {
                int others = 0;
                foreach (ResidentialOccupancy occ in occupancies.Values)
                {
                    if (occ.IsCurrent && occ.PersonId != personId
                        && string.Equals(occ.SpaceId, spaceId, StringComparison.Ordinal))
                        others++;
                }
                if (others + 1 > Math.Max(0, space.SleepingCapacity))
                    return $"HousingAuthority.TryOccupyUnderAgreement: '{spaceId}' sleeps {space.SleepingCapacity} " +
                           $"and already holds {others} — a room cannot be rented beyond capacity (Canon 12.7 commercial lodging).";
            }
            occupancy = Occupy(personId, householdId, spaceId, arrangement, dayIndex, diag);
            if (occupancy == null)
                return $"HousingAuthority.TryOccupyUnderAgreement: occupancy recording failed for P{personId} in '{spaceId}'.";
            occupancy.AgreementId = agreementId ?? string.Empty;
            return null;
        }

        /// <summary>Pass VIII Worker 2 (additive): current occupants of one space, in record order.</summary>
        public int OccupantCount(string spaceId)
        {
            if (string.IsNullOrWhiteSpace(spaceId)) return 0;
            int count = 0;
            foreach (ResidentialOccupancy occ in occupancies.Values)
            {
                if (occ.IsCurrent && string.Equals(occ.SpaceId, spaceId, StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Pass VIII Worker 2 (additive): every current occupancy in the
        /// authority, in record order. The absence sweep and diagnostics
        /// consume this; it never invents state.
        /// </summary>
        public List<ResidentialOccupancy> AllCurrentOccupancies()
        {
            var result = new List<ResidentialOccupancy>();
            foreach (ResidentialOccupancy occ in occupancies.Values)
            {
                if (occ != null && occ.IsCurrent) result.Add(occ);
            }
            return result;
        }

        /// <summary>
        /// Pass VIII Worker 2 (additive): real accommodation availability —
        /// every space with zero current occupants, in space-id order. The
        /// housing opportunity adapter and the migration worker consume
        /// this; it reports what exists, never what is wanted.
        /// </summary>
        public List<AccommodationSpace> VacantSpaces()
        {
            var result = new List<AccommodationSpace>();
            foreach (AccommodationSpace space in spaces.Values)
            {
                if (space != null && OccupantCount(space.SpaceId) == 0)
                    result.Add(space);
            }
            result.Sort((a, b) => string.Compare(a.SpaceId, b.SpaceId, StringComparison.Ordinal));
            return result;
        }

        /// <summary>
        /// Pass VIII Worker 2 (additive): capacity changes are real edits,
        /// not silent field writes. Growing is always allowed; shrinking
        /// below the current occupant count is refused — people are moved
        /// out first, the beds are not redefined under them. Returns null
        /// on success, the refusal otherwise.
        /// </summary>
        public string ChangeSpaceCapacity(string spaceId, int newCapacity, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!spaces.TryGetValue(spaceId, out AccommodationSpace space))
                return $"HousingAuthority.ChangeSpaceCapacity: unknown space '{spaceId}'.";
            if (newCapacity < 0)
                return $"HousingAuthority.ChangeSpaceCapacity: capacity cannot be negative (got {newCapacity}).";
            int occupants = OccupantCount(spaceId);
            if (newCapacity < occupants)
                return $"HousingAuthority.ChangeSpaceCapacity: '{spaceId}' holds {occupants} current occupant(s) — " +
                       "shrink the space only after people move out, never under them.";
            int old = space.SleepingCapacity;
            space.SleepingCapacity = newCapacity;
            diag.Add($"HousingAuthority: '{spaceId}' capacity {old} -> {newCapacity} (day {dayIndex}).");
            return null;
        }

        /// <summary>Pass VIII Worker 2 (additive): agreement lookup.</summary>
        public PropertyAgreement FindAgreement(string agreementId)
        {
            if (string.IsNullOrWhiteSpace(agreementId)) return null;
            agreements.TryGetValue(agreementId, out PropertyAgreement agreement);
            return agreement;
        }

        /// <summary>
        /// Pass VIII Worker 2 (additive): agreements whose target is one
        /// space or building, in agreement-id order.
        /// </summary>
        public List<PropertyAgreement> AgreementsForTarget(string targetId)
        {
            var result = new List<PropertyAgreement>();
            foreach (PropertyAgreement agreement in agreements.Values)
            {
                if (agreement != null && string.Equals(agreement.SpaceOrParcelId, targetId, StringComparison.Ordinal))
                    result.Add(agreement);
            }
            result.Sort((a, b) => string.Compare(a.AgreementId, b.AgreementId, StringComparison.Ordinal));
            return result;
        }

        /// <summary>
        /// Pass VIII Worker 2 (additive): ends a tenancy. The agreement
        /// keeps its id (FinancialObligationAuthority obligations stay
        /// linked — no orphaned debt); optionally the occupancies created
        /// under it end too, freeing the space. Idempotent: ending an
        /// already-ended agreement is a diagnostic, not an error. Returns
        /// null on success, the refusal otherwise.
        /// </summary>
        public string EndAgreement(string agreementId, int dayIndex, bool endLinkedOccupancies, string reason, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!agreements.TryGetValue(agreementId, out PropertyAgreement agreement))
                return $"HousingAuthority.EndAgreement: unknown agreement '{agreementId}'.";
            if (agreement.EndDayIndex >= 0 && agreement.EndDayIndex <= dayIndex)
            {
                diag.Add($"HousingAuthority: agreement '{agreementId}' already ended (day {agreement.EndDayIndex}) — no double end.");
                return null;
            }
            agreement.EndDayIndex = dayIndex;
            diag.Add($"HousingAuthority: agreement '{agreementId}' ended day {dayIndex} — {reason}.");
            if (endLinkedOccupancies)
            {
                foreach (ResidentialOccupancy occ in occupancies.Values)
                {
                    if (occ.IsCurrent && string.Equals(occ.AgreementId, agreementId, StringComparison.Ordinal))
                    {
                        occ.UntilDayIndex = dayIndex;
                        diag.Add($"HousingAuthority: occupancy '{occ.OccupancyId}' (P{occ.PersonId}) ended with agreement '{agreementId}' — space '{occ.SpaceId}' freed.");
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Pass VIII Worker 2 (additive): property transfer. The agreement
        /// keeps its id and its occupants keep their beds — occupancy is a
        /// fact, not a right the transfer revokes. Only the grantor name
        /// and, when terms exist, the rent payee move to the new holder.
        /// Arrears obligations in FinancialObligationAuthority stay linked
        /// by agreement id. Returns null on success, the refusal otherwise.
        /// </summary>
        public string TransferAgreement(string agreementId, string newGrantorName, string newPayeeHolderId, int dayIndex, string reason, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!agreements.TryGetValue(agreementId, out PropertyAgreement agreement))
                return $"HousingAuthority.TransferAgreement: unknown agreement '{agreementId}'.";
            if (string.IsNullOrWhiteSpace(newGrantorName))
                return "HousingAuthority.TransferAgreement: the new grantor must be named.";
            if (agreement.EndDayIndex >= 0 && agreement.EndDayIndex <= dayIndex)
                return $"HousingAuthority.TransferAgreement: agreement '{agreementId}' already ended (day {agreement.EndDayIndex}) — transfer a live tenancy, not a closed one.";
            string oldGrantor = agreement.GrantorName;
            agreement.GrantorName = newGrantorName;
            string payeeNote = "no payment terms — nothing to redirect";
            if (agreement.PaymentTerms != null && !string.IsNullOrWhiteSpace(newPayeeHolderId))
            {
                string oldPayee = agreement.PaymentTerms.PayeeHolderId;
                agreement.PaymentTerms.PayeeHolderId = newPayeeHolderId;
                payeeNote = $"rent payee '{oldPayee}' -> '{newPayeeHolderId}'";
            }
            diag.Add($"HousingAuthority: agreement '{agreementId}' transferred '{oldGrantor}' -> '{newGrantorName}' (day {dayIndex}, {reason}); " +
                     $"occupants and obligation links unchanged; {payeeNote}.");
            return null;
        }

        /// <summary>Pass VIII Worker 2 (additive): the outcome of closing one building.</summary>
        [Serializable]
        public sealed class BuildingClosureResult
        {
            public string BuildingId = string.Empty;
            public int OccupanciesEnded;
            public int AgreementsEnded;
            public int AssignmentsEnded;
            public List<string> EndedAgreementIds = new List<string>();
            public string Refusal = string.Empty;

            public BuildingClosureResult() { }
        }

        /// <summary>
        /// Pass VIII Worker 2 (additive): business closure / property
        /// shutdown for one building. Ends every current occupancy in its
        /// spaces (departure frees each space), ends every agreement
        /// targeting its spaces or the building itself, and closes active
        /// functional assignments. History is kept — nothing is deleted, so
        /// no occupant or obligation is orphaned. Returns the closure
        /// record, with Refusal set when the building is unknown.
        /// </summary>
        public BuildingClosureResult CloseBuilding(string buildingId, int dayIndex, string reason, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var result = new BuildingClosureResult { BuildingId = buildingId ?? string.Empty };
            if (!buildings.ContainsKey(buildingId))
            {
                result.Refusal = $"HousingAuthority.CloseBuilding: unknown building '{buildingId}'.";
                return result;
            }
            var spaceIds = new List<string>();
            foreach (AccommodationSpace space in SpacesForBuilding(buildingId))
            {
                if (space == null) continue;
                spaceIds.Add(space.SpaceId);
                foreach (ResidentialOccupancy occ in CurrentOccupants(space.SpaceId))
                {
                    occ.UntilDayIndex = dayIndex;
                    result.OccupanciesEnded++;
                    diag.Add($"HousingAuthority: occupancy '{occ.OccupancyId}' (P{occ.PersonId}) ended — building '{buildingId}' closed (day {dayIndex}, {reason}).");
                }
            }
            var agreementIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string spaceId in spaceIds)
                foreach (PropertyAgreement agreement in AgreementsForTarget(spaceId))
                    agreementIds.Add(agreement.AgreementId);
            foreach (PropertyAgreement agreement in AgreementsForTarget(buildingId))
                agreementIds.Add(agreement.AgreementId);
            foreach (string agreementId in agreementIds)
            {
                string refusal = EndAgreement(agreementId, dayIndex, endLinkedOccupancies: true, reason: $"building '{buildingId}' closed — {reason}", diag);
                if (refusal == null)
                {
                    result.AgreementsEnded++;
                    result.EndedAgreementIds.Add(agreementId);
                }
                else
                {
                    diag.Add(refusal);
                }
            }
            foreach (FunctionalSpaceAssignment assignment in assignments.Values)
            {
                if (assignment.IsActive && (spaceIds.Contains(assignment.SpaceId)
                    || string.Equals(assignment.SpaceId, buildingId, StringComparison.Ordinal)))
                {
                    assignment.UntilDayIndex = dayIndex;
                    result.AssignmentsEnded++;
                    diag.Add($"HousingAuthority: functional assignment '{assignment.AssignmentId}' closed with building '{buildingId}'.");
                }
            }
            diag.Add($"HousingAuthority: building '{buildingId}' closed (day {dayIndex}, {reason}) — " +
                     $"{result.OccupanciesEnded} occupanc(ies), {result.AgreementsEnded} agreement(s), {result.AssignmentsEnded} assignment(s) ended; history kept.");
            return result;
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
        /// Phase D: every current occupancy for one person (a person holds
        /// one bed at a time by Occupy's rule, but the read side lists what
        /// exists rather than assuming).
        /// </summary>
        public List<ResidentialOccupancy> CurrentOccupanciesForPerson(int personId)
        {
            var result = new List<ResidentialOccupancy>();
            foreach (ResidentialOccupancy occ in occupancies.Values)
            {
                if (occ.IsCurrent && occ.PersonId == personId)
                    result.Add(occ);
            }
            return result;
        }

        /// <summary>
        /// Phase D: the spaces one person currently occupies (read-side for
        /// the residential-condition registry).
        /// </summary>
        public List<AccommodationSpace> CurrentSpacesForPerson(int personId)
        {
            var result = new List<AccommodationSpace>();
            foreach (ResidentialOccupancy occ in CurrentOccupanciesForPerson(personId))
            {
                AccommodationSpace space = FindSpace(occ.SpaceId);
                if (space != null) result.Add(space);
            }
            return result;
        }

        /// <summary>Phase D: space lookup for suitability evaluation.</summary>
        public AccommodationSpace FindSpace(string spaceId)
        {
            if (string.IsNullOrWhiteSpace(spaceId)) return null;
            spaces.TryGetValue(spaceId, out AccommodationSpace space);
            return space;
        }

        /// <summary>
        /// Phase D: every accommodation space defined for one building, in
        /// definition order. The construction executor uses this to place
        /// the commissioning household into its completed home.
        /// </summary>
        public List<AccommodationSpace> SpacesForBuilding(string buildingId)
        {
            var result = new List<AccommodationSpace>();
            foreach (AccommodationSpace space in spaces.Values)
            {
                if (space != null && string.Equals(space.BuildingId, buildingId, StringComparison.Ordinal))
                    result.Add(space);
            }
            result.Sort((a, b) => string.Compare(a.SpaceId, b.SpaceId, StringComparison.Ordinal));
            return result;
        }

        /// <summary>
        /// Phase D: ends a person's current occupancies (move-out, eviction
        /// after notice, death) — history stays in the record.
        /// </summary>
        public void EndOccupanciesForPerson(int personId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (ResidentialOccupancy occ in CurrentOccupanciesForPerson(personId))
            {
                occ.UntilDayIndex = dayIndex;
                diag.Add($"HousingAuthority: P{personId} occupancy '{occ.OccupancyId}' in '{occ.SpaceId}' ended (day {dayIndex}).");
            }
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

        /// <summary>
        /// Pass III Worker B (additive read): building lookup for the
        /// footprint linker and tenancy tenant resolution.
        /// </summary>
        public Building FindBuilding(string buildingId)
        {
            if (string.IsNullOrWhiteSpace(buildingId)) return null;
            buildings.TryGetValue(buildingId, out Building building);
            return building;
        }

        /// <summary>
        /// Pass III Worker B (additive read): every registered building, in
        /// registration order. Read-only view for linkers and diagnostics.
        /// </summary>
        public List<Building> AllBuildings()
        {
            return new List<Building>(buildings.Values);
        }

        /// <summary>
        /// Pass III Worker B (additive): attaches rent/board payment terms to
        /// an existing property agreement. The agreement stays the legal
        /// record; the terms only add the payment schedule that the tenancy
        /// payment loop turns into due events. Canon XXXI Part VI §§6.1–6.8:
        /// housing/tenancy obligations are real and payable from actual cash.
        /// Returns null on success, diagnostic on refusal.
        /// </summary>
        public string AttachPaymentTerms(string agreementId, TenancyPaymentTerms terms, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(agreementId) || !agreements.TryGetValue(agreementId, out PropertyAgreement agreement))
                return $"HousingAuthority.AttachPaymentTerms: unknown agreement '{agreementId}'.";
            if (terms == null)
                return "HousingAuthority.AttachPaymentTerms: terms are required.";
            if (terms.AmountCents <= 0)
                return $"HousingAuthority.AttachPaymentTerms: amount must be positive (got {terms.AmountCents}c).";
            if (terms.PeriodDays <= 0)
                return $"HousingAuthority.AttachPaymentTerms: period must be positive days (got {terms.PeriodDays}).";
            if (terms.DueDayOfPeriod < 1 || terms.DueDayOfPeriod > terms.PeriodDays)
                return $"HousingAuthority.AttachPaymentTerms: due day {terms.DueDayOfPeriod} is outside the {terms.PeriodDays}-day period.";
            if (terms.StartDay < 0)
                return "HousingAuthority.AttachPaymentTerms: schedule start day cannot be negative.";
            if (string.IsNullOrWhiteSpace(terms.PayeeHolderId))
                return "HousingAuthority.AttachPaymentTerms: the payee (person/business/government id) must be named — rent is owed to someone real.";
            if (agreement.PaymentTerms != null)
                return $"HousingAuthority.AttachPaymentTerms: agreement '{agreementId}' already carries payment terms — replace via a new agreement, not a silent overwrite.";
            agreement.PaymentTerms = terms;
            diag.Add($"HousingAuthority: payment terms attached to '{agreementId}' — {terms.AmountCents}c every {terms.PeriodDays}d, payee '{terms.PayeeHolderId}'.");
            return null;
        }

        /// <summary>
        /// Pass III Worker B (additive read): every agreement carrying
        /// payment terms — the tenancy payment loop's work list.
        /// </summary>
        public List<PropertyAgreement> AgreementsWithPaymentTerms()
        {
            var result = new List<PropertyAgreement>();
            foreach (PropertyAgreement agreement in agreements.Values)
            {
                if (agreement != null && agreement.PaymentTerms != null)
                    result.Add(agreement);
            }
            result.Sort((a, b) => string.Compare(a.AgreementId, b.AgreementId, StringComparison.Ordinal));
            return result;
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

        /// <summary>
        /// Pass XII Worker F (copy-on-load, E-DEFECT-02 pattern):
        /// occupancies are mutated in place (a new occupancy for a
        /// person ends the current one by writing UntilDayIndex), so
        /// storing the DTO's occupancy objects would let a run corrupt
        /// the caller's snapshot — a second restore of the same
        /// in-memory DTO would load already-ended occupancies.
        /// Occupancies are therefore copied. Buildings, spaces,
        /// assignments and agreements have no in-place mutator in
        /// this authority (they are replaced, never edited) and stay
        /// stored-direct; re-audit them if a mutator is ever added.
        /// Clear-then-load is unchanged.
        /// </summary>
        public void LoadFromSaveDto(HousingSaveDto dto)
        {
            buildings.Clear(); spaces.Clear(); occupancies.Clear(); assignments.Clear(); agreements.Clear();
            if (dto == null) return;
            // Pass XII Worker F (copy-on-load, E-DEFECT-02 round 2): the
            // first Worker F sweep cloned occupancies only, but buildings,
            // spaces, assignments and agreements are ALSO mutated in place
            // (ChangeSpaceCapacity writes SleepingCapacity, EndAgreement
            // writes EndDayIndex, CloseBuildingWithHistory writes
            // UntilDayIndex) — so a double restore of the same DTO would
            // reload already-edited records. Clone all five collections.
            // PaymentTerms is itself mutated in place (PayeeHolderId
            // reassignment), so it is deep-copied, not shared.
            if (dto.Buildings != null)
                foreach (var b in dto.Buildings)
                {
                    if (b == null || string.IsNullOrWhiteSpace(b.BuildingId)) continue;
                    buildings[b.BuildingId] = new Building
                    {
                        BuildingId = b.BuildingId,
                        ParcelId = b.ParcelId,
                        Kind = b.Kind,
                        Condition01 = b.Condition01,
                        BuiltDayIndex = b.BuiltDayIndex,
                    };
                }
            if (dto.Spaces != null)
                foreach (var s in dto.Spaces)
                {
                    if (s == null || string.IsNullOrWhiteSpace(s.SpaceId)) continue;
                    spaces[s.SpaceId] = new AccommodationSpace
                    {
                        SpaceId = s.SpaceId,
                        BuildingId = s.BuildingId,
                        SleepingCapacity = s.SleepingCapacity,
                    };
                }
            foreach (var o in dto.Occupancies)
            {
                if (o == null || string.IsNullOrWhiteSpace(o.OccupancyId)) continue;
                occupancies[o.OccupancyId] = new ResidentialOccupancy
                {
                    OccupancyId = o.OccupancyId,
                    PersonId = o.PersonId,
                    HouseholdId = o.HouseholdId,
                    SpaceId = o.SpaceId ?? string.Empty,
                    Arrangement = o.Arrangement,
                    SinceDayIndex = o.SinceDayIndex,
                    UntilDayIndex = o.UntilDayIndex,
                    AgreementId = o.AgreementId ?? string.Empty,
                };
            }
            if (dto.Assignments != null)
                foreach (var a in dto.Assignments)
                {
                    if (a == null || string.IsNullOrWhiteSpace(a.AssignmentId)) continue;
                    assignments[a.AssignmentId] = new FunctionalSpaceAssignment
                    {
                        AssignmentId = a.AssignmentId,
                        SpaceId = a.SpaceId,
                        BusinessInstanceId = a.BusinessInstanceId,
                        OperationDescription = a.OperationDescription,
                        SinceDayIndex = a.SinceDayIndex,
                        UntilDayIndex = a.UntilDayIndex,
                    };
                }
            if (dto.Agreements != null)
                foreach (var a in dto.Agreements)
                {
                    if (a == null || string.IsNullOrWhiteSpace(a.AgreementId)) continue;
                    agreements[a.AgreementId] = new PropertyAgreement
                    {
                        AgreementId = a.AgreementId,
                        Kind = a.Kind,
                        GrantorName = a.GrantorName,
                        GranteeName = a.GranteeName,
                        SpaceOrParcelId = a.SpaceOrParcelId,
                        Terms = a.Terms,
                        StartDayIndex = a.StartDayIndex,
                        EndDayIndex = a.EndDayIndex,
                        LinkedInstrumentId = a.LinkedInstrumentId,
                        PaymentTerms = a.PaymentTerms == null ? null : new TenancyPaymentTerms
                        {
                            AmountCents = a.PaymentTerms.AmountCents,
                            PeriodDays = a.PaymentTerms.PeriodDays,
                            DueDayOfPeriod = a.PaymentTerms.DueDayOfPeriod,
                            StartDay = a.PaymentTerms.StartDay,
                            PayeeHolderId = a.PaymentTerms.PayeeHolderId,
                            TenantHouseholdId = a.PaymentTerms.TenantHouseholdId,
                        },
                    };
                }
        }
        #endregion
    }
}
