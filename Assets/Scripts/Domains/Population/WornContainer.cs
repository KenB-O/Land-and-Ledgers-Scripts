// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Physical hands campaign P2a — one worn container kind, a satchel (plan
// section 1). A satchel is worn over one shoulder on a strap, across the
// body, and leaves both hands free. The worn relationship is recorded on
// the container itself as WornByPersonId, set only by Wear and cleared
// only by Remove.
//
// The container holds references only (LotKey plus BatchId per entry): it
// never duplicates lot contents and never becomes a second inventory.
// Capacity is caller-supplied at construction on four axes and never
// changed afterwards; a container built without a capacity carries
// AllUnknown and every placement into it refuses. Fit is never computed
// here: Place delegates to PhysicalCapacityEvaluator with the WornSatchel
// leg. Condition travels on the entry, derived from the optional
// PerishableLotState SaveDto the caller passes through; the container
// never stores a SaveDto and never owns perishable state. Ownership never
// moves here; the hand authority transfer methods own the move and the
// CustodyTransferLog records custody only.
//
// Category B on landing: production code, not yet wired into any live
// path. No production caller owns containers yet (P4), save wiring waits
// for P6, and no authored container measurement exists (gap G4, open), so
// no production placement can pass fit until that gap is answered. All
// capacity numbers in tests are test-only fixtures, never authored
// measurements.

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;

namespace LandLedgers.Population
{
    /// <summary>
    /// The one worn container kind in this slice. Unknown means no kind
    /// was supplied; Satchel is the only worn kind. Every other kind stays
    /// Unknown until a later P2 slice adds it.
    /// </summary>
    public enum WornContainerKind
    {
        Unknown = 0,
        Satchel = 1,
    }

    /// <summary>
    /// Caller-supplied capacity on four axes. MaxUnits null means the units
    /// axis is Unknown. A measure that is Unknown means that axis is
    /// Unknown. AllUnknown refuses every placement: Unknown is a refusal,
    /// never a number.
    /// </summary>
    public struct WornContainerCapacity
    {
        public WornContainerCapacity(
            int? maxUnits, PhysicalMeasure maxMassKg, PhysicalMeasure maxVolumeLiters,
            PhysicalDimensions interiorDimensions)
        {
            MaxUnits = maxUnits;
            MaxMassKg = maxMassKg;
            MaxVolumeLiters = maxVolumeLiters;
            InteriorDimensions = interiorDimensions;
        }

        public int? MaxUnits { get; }
        public PhysicalMeasure MaxMassKg { get; }
        public PhysicalMeasure MaxVolumeLiters { get; }
        public PhysicalDimensions InteriorDimensions { get; }

        public static WornContainerCapacity AllUnknown =>
            new WornContainerCapacity(
                null, PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalDimensions.Unknown);
    }

    /// <summary>
    /// One contained lot, immutable plain data. It carries references only
    /// (LotKey, BatchId, item id, units) plus the condition the lot carried
    /// when placed — never lot contents, so the container never duplicates
    /// the lot and never becomes a second inventory.
    /// </summary>
    public sealed class WornContainerEntry
    {
        public WornContainerEntry(
            LotKey lotKey, string batchId, string itemId, int units, PersonItemCondition condition)
        {
            LotKey = lotKey;
            BatchId = batchId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            Units = units;
            Condition = condition;
        }

        public LotKey LotKey { get; }
        public string BatchId { get; }
        public string ItemId { get; }
        public int Units { get; }
        public PersonItemCondition Condition { get; }
    }

    /// <summary>
    /// Plain DTO copy of one container. It carries no operation method, so
    /// a reader of the snapshot cannot mutate container state through it,
    /// mirroring the HandOccupancy DTO rule.
    /// </summary>
    public sealed class WornContainerSnapshot
    {
        public WornContainerSnapshot(
            string containerId, WornContainerKind kind, int wornByPersonId,
            WornContainerCapacity capacity, IEnumerable<WornContainerEntry> entries)
        {
            ContainerId = containerId ?? string.Empty;
            Kind = kind;
            WornByPersonId = wornByPersonId;
            Capacity = capacity;
            Entries = entries != null
                ? new List<WornContainerEntry>(entries).AsReadOnly()
                : new List<WornContainerEntry>().AsReadOnly();
        }

        public string ContainerId { get; }
        public WornContainerKind Kind { get; }
        public int WornByPersonId { get; }
        public WornContainerCapacity Capacity { get; }
        public IReadOnlyList<WornContainerEntry> Entries { get; }
    }

    /// <summary>The outcome of one container operation. A refusal always carries a named reason.</summary>
    public sealed class WornContainerOperationResult
    {
        private WornContainerOperationResult(bool succeeded, string reason, LotKey lotKey, string batchId)
        {
            Succeeded = succeeded;
            Reason = reason ?? string.Empty;
            LotKey = lotKey;
            BatchId = batchId ?? string.Empty;
        }

        public static WornContainerOperationResult Ok(string reason, LotKey lotKey, string batchId) =>
            new WornContainerOperationResult(true, reason, lotKey, batchId);

        public static WornContainerOperationResult Ok(string reason) =>
            new WornContainerOperationResult(true, reason, LotKey.Empty, string.Empty);

        public static WornContainerOperationResult Refused(string reason) =>
            new WornContainerOperationResult(false, reason, LotKey.Empty, string.Empty);

        public bool Succeeded { get; }
        public string Reason { get; }

        /// <summary>The lot the operation acted on, or Empty when none.</summary>
        public LotKey LotKey { get; }

        /// <summary>The batch id of LotKey, or empty when none.</summary>
        public string BatchId { get; }
    }

    /// <summary>
    /// One worn container (this slice: a satchel). ContainerId is
    /// caller-supplied and unique per container within the scope that
    /// creates it; no registry enforces global uniqueness in P2a. Capacity
    /// is supplied at construction and never changed afterwards. Contents
    /// are a private list of entries, exposed read-only through
    /// <see cref="Entries"/> and the <see cref="Current"/> snapshot. The
    /// container holds no log: logging happens in the hand authority
    /// transfer methods, which own the transfer.
    /// </summary>
    public sealed class WornContainer
    {
        private readonly List<WornContainerEntry> contents = new List<WornContainerEntry>();

        /// <summary>Creates a container with AllUnknown capacity: every placement into it refuses.</summary>
        public WornContainer(string containerId, WornContainerKind kind)
            : this(containerId, kind, WornContainerCapacity.AllUnknown)
        {
        }

        public WornContainer(string containerId, WornContainerKind kind, WornContainerCapacity capacity)
        {
            ContainerId = containerId ?? string.Empty;
            Kind = kind;
            WornByPersonId = -1;
            Capacity = capacity;
        }

        public string ContainerId { get; }
        public WornContainerKind Kind { get; }

        /// <summary>The wearer person id, or -1 when unworn. Set only by Wear, cleared only by Remove.</summary>
        public int WornByPersonId { get; private set; }

        public WornContainerCapacity Capacity { get; }

        public IReadOnlyList<WornContainerEntry> Entries => contents.AsReadOnly();

        /// <summary>A copy of the current container state. The snapshot is plain data; mutating it changes nothing.</summary>
        public WornContainerSnapshot Current =>
            new WornContainerSnapshot(ContainerId, Kind, WornByPersonId, Capacity, contents);

        /// <summary>True when the container holds this lot.</summary>
        public bool Contains(LotKey lotKey) => EntryFor(lotKey) != null;

        /// <summary>The entry for this lot, or null when the container does not hold it.</summary>
        public WornContainerEntry EntryFor(LotKey lotKey)
        {
            if (lotKey.IsEmpty) return null;
            foreach (WornContainerEntry entry in contents)
            {
                if (entry.LotKey.Equals(lotKey)) return entry;
            }

            return null;
        }

        /// <summary>The units summed across every contained entry.</summary>
        public int TotalUnits
        {
            get
            {
                int total = 0;
                foreach (WornContainerEntry entry in contents)
                {
                    total += Math.Max(0, entry.Units);
                }

                return total;
            }
        }

        /// <summary>The current contents as a physical load (item id and units lines), for the fit query.</summary>
        public PhysicalLoad CurrentLoad
        {
            get
            {
                var lines = new List<(string ItemId, int Units)>();
                foreach (WornContainerEntry entry in contents)
                {
                    lines.Add((entry.ItemId, entry.Units));
                }

                return new PhysicalLoad(lines);
            }
        }

        /// <summary>
        /// Wears the container for one person. Refuses when it is already
        /// worn by a different person, naming the current wearer. Succeeds
        /// idempotently when the same person wears it again.
        /// </summary>
        public WornContainerOperationResult Wear(int personId)
        {
            if (WornByPersonId != -1 && WornByPersonId != personId)
            {
                return WornContainerOperationResult.Refused(
                    "WornContainer: satchel '" + ContainerId + "' wear by person " + personId
                    + " refused — it is already worn by person " + WornByPersonId + "; remove it first.");
            }

            WornByPersonId = personId;
            return WornContainerOperationResult.Ok(
                "WornContainer: satchel '" + ContainerId + "' is worn by person " + personId + ".");
        }

        /// <summary>
        /// Removes the container. Refuses when it is unworn. On success it
        /// clears the wearer to -1 and keeps the contents inside: an unworn
        /// container with contents is invisible to every person carried
        /// view until worn again (a placed-container location is P4 or P6
        /// work, not fixed here).
        /// </summary>
        public WornContainerOperationResult Remove()
        {
            if (WornByPersonId == -1)
            {
                return WornContainerOperationResult.Refused(
                    "WornContainer: satchel '" + ContainerId + "' remove refused — it is unworn; there is nothing to remove.");
            }

            WornByPersonId = -1;
            return WornContainerOperationResult.Ok(
                "WornContainer: satchel '" + ContainerId + "' was removed; its contents stay inside.");
        }

        /// <summary>
        /// Places one lot into the container. Refusal order is fixed: the
        /// worn-by check, then the circular-nesting guard, then the
        /// duplicate guard, then the fit check through the evaluator. It
        /// mutates nothing on any refusal. On success it appends one entry
        /// whose Condition comes from the supplied SaveDto through
        /// FromPerishableCondition, or Unknown when none is supplied. The
        /// container never stores a SaveDto and never owns perishable state.
        /// A placement refuses unless the evaluator verdict is Fits, and
        /// the refusal carries the verdict reason verbatim.
        /// </summary>
        public WornContainerOperationResult Place(
            int personId, LotKey lotKey, string batchId, string itemId, int units,
            PhysicalCapacityEvaluator evaluator, int dayIndex,
            PerishableLotState.SaveDto? saveDto = null)
        {
            if (WornByPersonId == -1)
            {
                return WornContainerOperationResult.Refused(
                    "WornContainer: person " + personId + " place of lot " + lotKey + " into satchel '" + ContainerId
                    + "' refused — the satchel is unworn; wear it first.");
            }

            if (WornByPersonId != personId)
            {
                return WornContainerOperationResult.Refused(
                    "WornContainer: person " + personId + " place of lot " + lotKey + " into satchel '" + ContainerId
                    + "' refused — the satchel is worn by person " + WornByPersonId + ", not person " + personId + ".");
            }

            LotKey ownKey = new LotKey(CustodyTransferLog.ContainerAuthority, ContainerId);
            if (lotKey.Equals(ownKey))
            {
                return WornContainerOperationResult.Refused(
                    "WornContainer: place of lot " + lotKey + " into satchel '" + ContainerId
                    + "' refused — circular nesting: the lot key names the container itself, so the container cannot be placed inside itself.");
            }

            if (Contains(lotKey))
            {
                return WornContainerOperationResult.Refused(
                    "WornContainer: place of lot " + lotKey + " into satchel '" + ContainerId
                    + "' refused — the satchel already holds that lot; one lot cannot be placed twice.");
            }

            if (evaluator == null)
            {
                return WornContainerOperationResult.Refused(
                    "WornContainer: place of lot " + lotKey + " into satchel '" + ContainerId
                    + "' refused — no capacity evaluator was supplied, so fit cannot be checked.");
            }

            var lines = new List<(string ItemId, int Units)>();
            foreach (WornContainerEntry entry in contents)
            {
                lines.Add((entry.ItemId, entry.Units));
            }

            lines.Add((itemId ?? string.Empty, units));
            var query = new PhysicalCapacityQuery(
                PhysicalContainerKind.WornSatchel, ContainerId, new PhysicalLoad(lines), dayIndex);
            var context = new PhysicalCapacityEvaluationContext { ContainerCapacity = Capacity };
            PhysicalCapacityVerdict verdict = evaluator.Evaluate(query, context);
            if (!verdict.Fits)
            {
                return WornContainerOperationResult.Refused(verdict.Reason);
            }

            PersonItemCondition condition = PersonItemCondition.Unknown;
            if (saveDto.HasValue)
            {
                condition = PersonInventoryReadModel.FromPerishableCondition(
                    PerishableLotState.FromSaveDto(saveDto.Value).PhysicalCondition);
            }

            contents.Add(new WornContainerEntry(lotKey, batchId ?? string.Empty, itemId ?? string.Empty, units, condition));
            return WornContainerOperationResult.Ok(
                "WornContainer: lot " + lotKey + " placed into satchel '" + ContainerId + "' for person " + personId + ".",
                lotKey, batchId ?? string.Empty);
        }

        /// <summary>
        /// Takes one lot out of the container. Refuses when the container
        /// does not hold that lot. On success it removes the entry in the
        /// same operation. The caller reads the entry through EntryFor
        /// before calling Take, so Take needs only to remove and report.
        /// </summary>
        public WornContainerOperationResult Take(LotKey lotKey)
        {
            WornContainerEntry entry = EntryFor(lotKey);
            if (entry == null)
            {
                return WornContainerOperationResult.Refused(
                    "WornContainer: take of lot " + lotKey + " from satchel '" + ContainerId
                    + "' refused — the satchel does not hold that lot.");
            }

            contents.Remove(entry);
            return WornContainerOperationResult.Ok(
                "WornContainer: lot " + lotKey + " taken from satchel '" + ContainerId + "'.",
                lotKey, entry.BatchId);
        }
    }
}
