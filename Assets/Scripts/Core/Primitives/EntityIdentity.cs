using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Primitives
{
    /// <summary>
    /// HF-1: typed entity kinds for the universal identity framework.
    ///
    /// NUMERIC VALUES ARE PERMANENT. Never renumber an existing kind and never reuse a
    /// retired value: persisted saves, diagnostics and cross-references encode these values.
    /// New first-class kinds are added here with the next free value below CustomBase.
    /// One-off / mod kinds use the extension range at or above CustomBase, declared as named
    /// constants in code and registered once at startup via
    /// <see cref="EntityKindCatalog.RegisterCustomKind"/>.
    /// </summary>
    public enum EntityKind
    {
        Unspecified = 0,

        /// <summary>Person (Tech X §2.2 TECH LOCK: PersonId never changes).</summary>
        Person = 1,

        /// <summary>Household domestic unit.</summary>
        Household = 2,

        /// <summary>Individual animal (Tech X Part III: AnimalId never reused).</summary>
        Animal = 3,

        /// <summary>Business/economic entity instance.</summary>
        Business = 4,

        /// <summary>Building/structure.</summary>
        Building = 5,

        /// <summary>Logistics shipment.</summary>
        Shipment = 6,

        /// <summary>Perishable/economic lot or batch.</summary>
        Lot = 7,

        /// <summary>Land plot/parcel.</summary>
        Plot = 8,

        /// <summary>Household membership record (PKG-8).</summary>
        Membership = 9,

        /// <summary>Wage-employment relationship (PKG-6).</summary>
        EmploymentRelationship = 10,

        /// <summary>Livestock cohort (Tech X §3.4).</summary>
        AnimalCohort = 11,

        /// <summary>Agreement/contract.</summary>
        Contract = 12,

        /// <summary>Work task / activity unit.</summary>
        WorkTask = 13,

        /// <summary>
        /// Extension range base. Custom kinds MUST use values &gt;= CustomBase, declared as
        /// named constants (e.g. <c>public const int VehicleKind = 1000;</c>) and registered
        /// via <see cref="EntityKindCatalog.RegisterCustomKind"/>.
        /// </summary>
        CustomBase = 1000,
    }

    /// <summary>
    /// HF-1: typed, never-reused entity identity. An EntityId is an <see cref="EntityKind"/>
    /// plus a per-kind sequence value. IDs are unique within a kind; the kind tag makes
    /// cross-kind references unambiguous (P12 is a Person, H12 a Household).
    ///
    /// Sequences only advance. There is no rewind/delete API: deleting an entity retires its
    /// ID permanently (Tech X §2.2 / §3.2 TECH LOCKs).
    /// </summary>
    [Serializable]
    public struct EntityId : IEquatable<EntityId>
    {
        public EntityKind Kind;
        public int Id;

        public static readonly EntityId Invalid = new EntityId { Kind = EntityKind.Unspecified, Id = -1 };

        public static EntityId For(EntityKind kind, int id)
        {
            if (kind == EntityKind.Unspecified)
            {
                throw new ArgumentException("EntityId requires a specified EntityKind.", nameof(kind));
            }

            if (id < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "EntityId sequence value cannot be negative.");
            }

            return new EntityId { Kind = kind, Id = id };
        }

        /// <summary>
        /// Migration bridge for legacy unkinded integer IDs: tags a pre-existing int with its
        /// entity kind without changing the numeric value. Use once per legacy id space.
        /// </summary>
        public static EntityId FromLegacyInt(EntityKind kind, int legacyId)
        {
            return For(kind, legacyId);
        }

        public bool IsValid => Kind != EntityKind.Unspecified && Id >= 0;

        public bool Equals(EntityId other)
        {
            return Kind == other.Kind && Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            return obj is EntityId other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ Id;
            }
        }

        public static bool operator ==(EntityId left, EntityId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EntityId left, EntityId right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            if (!IsValid)
            {
                return "Invalid";
            }

            return EntityKindCatalog.TryGetPrefix(Kind, out string prefix)
                ? $"{prefix}{Id}"
                : $"K{(int)Kind}:{Id}";
        }
    }

    /// <summary>
    /// HF-1: static catalog of entity-kind display metadata plus the documented adoption path
    /// for custom kinds.
    ///
    /// ADOPTION PATH for a new object kind:
    /// 1. If the kind is first-class and permanent, add it to <see cref="EntityKind"/> with the
    ///    next free value below <see cref="EntityKind.CustomBase"/> and a prefix below.
    /// 2. Otherwise declare <c>public const int MyKind = 1000;</c> (or the next free value at
    ///    or above CustomBase) and call <see cref="RegisterCustomKind"/> once at startup
    ///    (bootstrap / static initializer), before any allocation or save/load.
    /// 3. Allocate through <see cref="EntityIdRegistry"/>; persist cursors through
    ///    <c>EntityIdSaveAdapter</c> (already wired into the save envelope).
    /// </summary>
    public static class EntityKindCatalog
    {
        private static readonly Dictionary<EntityKind, string> prefixes = new Dictionary<EntityKind, string>
        {
            { EntityKind.Person, "P" },
            { EntityKind.Household, "H" },
            { EntityKind.Animal, "A" },
            { EntityKind.Business, "B" },
            { EntityKind.Building, "BLD" },
            { EntityKind.Shipment, "S" },
            { EntityKind.Lot, "L" },
            { EntityKind.Plot, "PL" },
            { EntityKind.Membership, "M" },
            { EntityKind.EmploymentRelationship, "E" },
            { EntityKind.AnimalCohort, "C" },
            { EntityKind.Contract, "K" },
            { EntityKind.WorkTask, "T" },
        };

        private static readonly Dictionary<EntityKind, string> displayNames = new Dictionary<EntityKind, string>
        {
            { EntityKind.Person, "Person" },
            { EntityKind.Household, "Household" },
            { EntityKind.Animal, "Animal" },
            { EntityKind.Business, "Business" },
            { EntityKind.Building, "Building" },
            { EntityKind.Shipment, "Shipment" },
            { EntityKind.Lot, "Lot" },
            { EntityKind.Plot, "Plot" },
            { EntityKind.Membership, "Membership" },
            { EntityKind.EmploymentRelationship, "Employment" },
            { EntityKind.AnimalCohort, "Cohort" },
            { EntityKind.Contract, "Contract" },
            { EntityKind.WorkTask, "Work task" },
        };

        /// <summary>
        /// Registers a custom entity kind in the extension range. Call once at startup before
        /// any allocation or save/load involving the kind.
        /// </summary>
        public static void RegisterCustomKind(int kindValue, string displayName, string prefix)
        {
            if (kindValue < (int)EntityKind.CustomBase)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kindValue),
                    $"Custom entity kinds must use values >= {(int)EntityKind.CustomBase}; {kindValue} is reserved for first-class kinds.");
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Custom kind requires a display name.", nameof(displayName));
            }

            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("Custom kind requires a diagnostic prefix.", nameof(prefix));
            }

            var kind = (EntityKind)kindValue;
            if (prefixes.ContainsKey(kind))
            {
                throw new InvalidOperationException($"Entity kind value {kindValue} is already registered.");
            }

            foreach (KeyValuePair<EntityKind, string> entry in prefixes)
            {
                if (string.Equals(entry.Value, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Diagnostic prefix '{prefix}' is already used by entity kind {(int)entry.Key}.");
                }
            }

            prefixes[kind] = prefix;
            displayNames[kind] = displayName;
        }

        public static bool TryGetPrefix(EntityKind kind, out string prefix)
        {
            return prefixes.TryGetValue(kind, out prefix);
        }

        public static string GetDisplayName(EntityKind kind)
        {
            return displayNames.TryGetValue(kind, out string name) ? name : $"Kind {(int)kind}";
        }

        public static bool IsKnown(EntityKind kind)
        {
            return kind != EntityKind.Unspecified && prefixes.ContainsKey(kind);
        }
    }

    /// <summary>
    /// HF-1: one persisted per-kind sequence cursor. Serialized through the save envelope
    /// (<c>EntityIdSaveAdapter</c>); sorted by kind value for deterministic output.
    /// </summary>
    [Serializable]
    public sealed class EntityIdCursor
    {
        public EntityKind Kind;
        public int NextId;

        public EntityIdCursor()
        {
        }

        public EntityIdCursor(EntityKind kind, int nextId)
        {
            Kind = kind;
            NextId = nextId;
        }
    }

    /// <summary>
    /// HF-1: the universal per-kind ID allocator registry.
    ///
    /// Each kind owns an independent <see cref="SequentialIdAllocator"/> sequence: IDs are
    /// unique within a kind and NEVER reused (there is deliberately no rewind or delete
    /// API — retiring an entity leaves its sequence value permanently consumed).
    ///
    /// LEGACY COEXISTENCE: Person/Household/Building still allocate through their existing
    /// M1 allocators (PopulationState, TownWorldController), which remain the authority for
    /// those kinds so persisted values are preserved exactly. This registry mirrors those
    /// cursors via <see cref="SyncFromLegacy"/> at save/load boundaries and is the direct
    /// authority for every new kind (Animal, Membership, EmploymentRelationship, ...).
    /// </summary>
    public sealed class EntityIdRegistry
    {
        private readonly Dictionary<EntityKind, SequentialIdAllocator> allocators =
            new Dictionary<EntityKind, SequentialIdAllocator>();

        /// <summary>Allocates the next ID for a kind. The sequence only advances.</summary>
        public EntityId Allocate(EntityKind kind)
        {
            return EntityId.For(kind, GetOrCreateAllocator(kind).AllocateNext());
        }

        /// <summary>
        /// Adopts an externally-managed sequence position (e.g. a legacy allocator's next
        /// value). Never moves a cursor backward: the maximum wins.
        /// </summary>
        public void SeedKind(EntityKind kind, int nextId)
        {
            ValidateKind(kind);
            GetOrCreateAllocator(kind).SeedAtLeast(nextId);
        }

        /// <summary>
        /// Mirrors a legacy allocator's next value into this registry without disturbing the
        /// legacy authority. Used at save/load boundaries for Person/Household/Building.
        /// </summary>
        public void SyncFromLegacy(EntityKind kind, int legacyNextId)
        {
            SeedKind(kind, legacyNextId);
        }

        /// <summary>
        /// Load path only: restores a cursor to its exact persisted value. Not for gameplay use.
        /// </summary>
        public void RestoreCursor(EntityKind kind, int persistedNextId)
        {
            ValidateKind(kind);
            GetOrCreateAllocator(kind).RestoreExact(persistedNextId);
        }

        public int PeekNext(EntityKind kind)
        {
            ValidateKind(kind);
            return GetOrCreateAllocator(kind).NextId;
        }

        /// <summary>Deterministic snapshot of all known cursors, sorted by kind value.</summary>
        public List<EntityIdCursor> SnapshotCursors()
        {
            var cursors = new List<EntityIdCursor>(allocators.Count);
            foreach (KeyValuePair<EntityKind, SequentialIdAllocator> entry in allocators)
            {
                cursors.Add(new EntityIdCursor(entry.Key, entry.Value.NextId));
            }

            cursors.Sort((left, right) => ((int)left.Kind).CompareTo((int)right.Kind));
            return cursors;
        }

        /// <summary>Load path: restores cursors from a snapshot. Unknown/duplicate entries rejected.</summary>
        public List<string> RestoreCursors(IEnumerable<EntityIdCursor> cursors)
        {
            var diagnostics = new List<string>();
            if (cursors == null)
            {
                return diagnostics;
            }

            var seen = new HashSet<EntityKind>();
            foreach (EntityIdCursor cursor in cursors)
            {
                if (cursor == null)
                {
                    continue;
                }

                if (cursor.Kind == EntityKind.Unspecified)
                {
                    diagnostics.Add("Ignored entity-ID cursor with Unspecified kind.");
                    continue;
                }

                if (!seen.Add(cursor.Kind))
                {
                    diagnostics.Add($"Duplicate entity-ID cursor for kind {(int)cursor.Kind}; first wins.");
                    continue;
                }

                try
                {
                    RestoreCursor(cursor.Kind, cursor.NextId);
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"Rejected entity-ID cursor for kind {(int)cursor.Kind}: {ex.Message}");
                }
            }

            return diagnostics;
        }

        private SequentialIdAllocator GetOrCreateAllocator(EntityKind kind)
        {
            if (!allocators.TryGetValue(kind, out SequentialIdAllocator allocator))
            {
                allocator = new SequentialIdAllocator();
                allocators[kind] = allocator;
            }

            return allocator;
        }

        private static void ValidateKind(EntityKind kind)
        {
            if (kind == EntityKind.Unspecified)
            {
                throw new ArgumentException("EntityKind.Unspecified cannot allocate IDs.", nameof(kind));
            }
        }
    }
}
