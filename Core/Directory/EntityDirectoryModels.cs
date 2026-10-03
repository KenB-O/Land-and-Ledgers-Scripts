using System;

namespace LandLedgers.Directory
{
    /// <summary>
    /// Typed entity kinds for cross-domain references (3A-D02 §6.2). Matches the domain
    /// registries: Population (Person, Household), World (Parcel, Building, AccommodationSpace),
    /// Business (Business), Labor (Employment), Housing (Occupancy), PropertyRights (PropertyRight),
    /// plus Shipment for logistics references.
    /// </summary>
    public enum EntityKind
    {
        Unknown = 0,
        Person = 1,
        Household = 2,
        Business = 3,
        Building = 4,
        Parcel = 5,
        AccommodationSpace = 6,
        Employment = 7,
        Occupancy = 8,
        PropertyRight = 9,
        Shipment = 10,
    }

    /// <summary>
    /// A generic cross-domain entity reference: kind + stable id. Per 3A-D02 §6.3, generic
    /// references are justified ONLY where the relationship legitimately accepts multiple
    /// endpoint kinds (e.g. a property-right holder). Most state should keep typed fields;
    /// this is the exception, not the standard storage type.
    /// </summary>
    [Serializable]
    public struct EntityRef : IEquatable<EntityRef>
    {
        public EntityKind Kind;
        public string StableId;

        public static EntityRef ForPerson(int personId)
        {
            return new EntityRef { Kind = EntityKind.Person, StableId = personId.ToString() };
        }

        public static EntityRef ForHousehold(int householdId)
        {
            return new EntityRef { Kind = EntityKind.Household, StableId = householdId.ToString() };
        }

        public static EntityRef ForBusiness(string businessInstanceId)
        {
            return new EntityRef { Kind = EntityKind.Business, StableId = businessInstanceId ?? string.Empty };
        }

        public static EntityRef ForBuilding(int buildingId)
        {
            return new EntityRef { Kind = EntityKind.Building, StableId = buildingId.ToString() };
        }

        public static EntityRef ForEmployment(string employmentId)
        {
            return new EntityRef { Kind = EntityKind.Employment, StableId = employmentId ?? string.Empty };
        }

        public bool IsValid => Kind != EntityKind.Unknown && !string.IsNullOrWhiteSpace(StableId);

        public bool Equals(EntityRef other)
        {
            return Kind == other.Kind && string.Equals(StableId, other.StableId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is EntityRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ (StableId != null ? StableId.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return $"{Kind}:{StableId}";
        }
    }

    /// <summary>
    /// Reference classes from 3A-D02 §7. Formalizes the currently distributed/inconsistent
    /// missing-reference behavior instead of forcing one global response.
    /// </summary>
    public enum ReferenceClass
    {
        Unspecified = 0,

        /// <summary>
        /// Without this reference the object cannot validly execute (e.g. Employment -> employee).
        /// Missing: retain for diagnostics, mark Invalid/Quarantined, BLOCK execution, do not
        /// invent a replacement.
        /// </summary>
        Required = 1,

        /// <summary>
        /// The object remains valid without it (e.g. optional manager). Missing: normalize to
        /// null/unresolved, diagnostic where useful, continue.
        /// </summary>
        Optional = 2,

        /// <summary>
        /// Describes something that legitimately existed in the past (e.g. dissolved business).
        /// Missing live target: retain the historical target or an identity snapshot; never
        /// silently drop the history.
        /// </summary>
        Historical = 3,

        /// <summary>
        /// Useful but unresolved identity must not corrupt the owning entity (e.g. candidate
        /// lead). May remain unresolved or be dropped per domain policy; dependent action
        /// unavailable.
        /// </summary>
        Soft = 4,

        /// <summary>
        /// Never authoritative (e.g. Household -> current resident display list, Business ->
        /// employee index). Rebuild after load; anything treating it as authority is a bug.
        /// </summary>
        Derived = 5,
    }

    /// <summary>Outcome of validating one declared reference.</summary>
    public enum ReferenceValidationOutcome
    {
        Valid = 0,
        InvalidQuarantined = 1,
        NormalizedUnresolved = 2,
        HistoricalRetained = 3,
        SoftUnresolved = 4,
        DerivedNotAuthoritative = 5,
    }

    /// <summary>One declared cross-domain reference with its reference class.</summary>
    [Serializable]
    public struct ReferenceDeclaration
    {
        public EntityRef From;
        public EntityRef To;
        public ReferenceClass ReferenceClass;
        public string Description;
    }

    /// <summary>Result of validating one declared reference against the directory.</summary>
    [Serializable]
    public sealed class EntityReferenceDiagnostic
    {
        public ReferenceDeclaration Declaration;
        public ReferenceValidationOutcome Outcome;
        public string Details = string.Empty;
    }
}
