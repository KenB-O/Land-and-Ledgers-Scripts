// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Physical hands campaign P2b — one handheld container kind, a pail (plan
// section 1). A pail is carried in the hand by a handle, it holds its
// contents inside while carried, and setting it down frees the hand. The
// held-by relationship is recorded on the container itself as
// HeldByPersonId plus HeldHand, set only by PickUp and cleared only by
// SetDown, and updated together by the authority move.
//
// The container is held as a lot in a hand slot, using the container own
// key scheme from P2a: a LotKey built from the ContainerAuthority word
// and the container id, with an empty batch id (a container is not a
// batch). The hand requirement of the container itself is caller-supplied
// on each pick up call, mirroring the G1 decision, never derived from
// mass, name, or item category.
//
// The container holds references only (LotKey plus BatchId per entry): it
// never duplicates lot contents and never becomes a second inventory.
// Capacity reuses WornContainerCapacity, the caller-supplied capacity on
// four axes (its name says Worn only because P2a created it; it carries
// no worn semantics), supplied at construction and never changed
// afterwards; a container built without a capacity carries AllUnknown
// and every placement into it refuses. Fit is never computed here:
// Place delegates to PhysicalCapacityEvaluator with the HandheldPail
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
    /// The one handheld container kind in this slice. Unknown means no kind
    /// was supplied; Pail is the only handheld kind. Every other kind stays
    /// Unknown until a later P2 slice adds it.
    /// </summary>
    public enum HandheldContainerKind
    {
        Unknown = 0,
        Pail = 1,
    }

    /// <summary>
    /// Plain DTO copy of one handheld container. It carries no operation
    /// method, so a reader of the snapshot cannot mutate container state
    /// through it, mirroring the HandOccupancy DTO rule. HeldHand null
    /// means the container is set down.
    /// </summary>
    public sealed class HandheldContainerSnapshot
    {
        public HandheldContainerSnapshot(
            string containerId, HandheldContainerKind kind, int heldByPersonId,
            HandSlot? heldHand, WornContainerCapacity capacity,
            IEnumerable<WornContainerEntry> entries)
        {
            ContainerId = containerId ?? string.Empty;
            Kind = kind;
            HeldByPersonId = heldByPersonId;
            HeldHand = heldHand;
            Capacity = capacity;
            Entries = entries != null
                ? new List<WornContainerEntry>(entries).AsReadOnly()
                : new List<WornContainerEntry>().AsReadOnly();
        }

        public string ContainerId { get; }
        public HandheldContainerKind Kind { get; }
        public int HeldByPersonId { get; }
        public HandSlot? HeldHand { get; }
        public WornContainerCapacity Capacity { get; }
        public IReadOnlyList<WornContainerEntry> Entries { get; }
    }

    /// <summary>The outcome of one handheld container operation. A refusal always carries a named reason.</summary>
    public sealed class HandheldContainerOperationResult
    {
        private HandheldContainerOperationResult(bool succeeded, string reason, LotKey lotKey, string batchId)
        {
            Succeeded = succeeded;
            Reason = reason ?? string.Empty;
            LotKey = lotKey;
            BatchId = batchId ?? string.Empty;
        }

        public static HandheldContainerOperationResult Ok(string reason, LotKey lotKey, string batchId) =>
            new HandheldContainerOperationResult(true, reason, lotKey, batchId);

        public static HandheldContainerOperationResult Ok(string reason) =>
            new HandheldContainerOperationResult(true, reason, LotKey.Empty, string.Empty);

        public static HandheldContainerOperationResult Refused(string reason) =>
            new HandheldContainerOperationResult(false, reason, LotKey.Empty, string.Empty);

        public bool Succeeded { get; }
        public string Reason { get; }

        /// <summary>The lot the operation acted on, or Empty when none.</summary>
        public LotKey LotKey { get; }

        /// <summary>The batch id of LotKey, or empty when none.</summary>
        public string BatchId { get; }
    }

    /// <summary>
    /// One handheld container (this slice: a pail). ContainerId is
    /// caller-supplied and unique per container within the scope that
    /// creates it; no registry enforces global uniqueness in P2b, and a
    /// satchel id colliding with a pail id would collide on own keys,
    /// because both kinds build own keys from the same ContainerAuthority
    /// word, so callers keep the two id spaces apart. Capacity is supplied
    /// at construction and never changed afterwards. Contents are a
    /// private list of entries, exposed read-only through
    /// <see cref="Entries"/> and the <see cref="Current"/> snapshot. The
    /// container holds no log: logging happens in the hand authority
    /// transfer methods, which own the transfer.
    /// </summary>
    public sealed class HandheldContainer
    {
        private readonly List<WornContainerEntry> contents = new List<WornContainerEntry>();

        /// <summary>Creates a container with AllUnknown capacity: every placement into it refuses.</summary>
        public HandheldContainer(string containerId, HandheldContainerKind kind)
            : this(containerId, kind, WornContainerCapacity.AllUnknown)
        {
        }

        public HandheldContainer(string containerId, HandheldContainerKind kind, WornContainerCapacity capacity)
        {
            ContainerId = containerId ?? string.Empty;
            Kind = kind;
            HeldByPersonId = -1;
            HeldHand = null;
            Capacity = capacity;
        }

        public string ContainerId { get; }
        public HandheldContainerKind Kind { get; }

        /// <summary>The holder person id, or -1 when set down. Set only by PickUp, cleared only by SetDown.</summary>
        public int HeldByPersonId { get; private set; }

        /// <summary>The hand holding the container, or null when set down. Set only by PickUp, cleared only by SetDown.</summary>
        public HandSlot? HeldHand { get; private set; }

        public WornContainerCapacity Capacity { get; }

        public IReadOnlyList<WornContainerEntry> Entries => contents.AsReadOnly();

        /// <summary>A copy of the current container state. The snapshot is plain data; mutating it changes nothing.</summary>
        public HandheldContainerSnapshot Current =>
            new HandheldContainerSnapshot(ContainerId, Kind, HeldByPersonId, HeldHand, Capacity, contents);

        /// <summary>The container's own key: the ContainerAuthority word plus the container id.</summary>
        public LotKey OwnKey => new LotKey(CustodyTransferLog.ContainerAuthority, ContainerId);

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
        /// Picks the container up for one person into one hand. Refuses
        /// when it is already held by a different person, naming the
        /// current holder. Succeeds idempotently when the same person
        /// picks it up again into the same hand. Refuses when the same
        /// person holds it in the other hand, naming the current hand.
        /// </summary>
        public HandheldContainerOperationResult PickUp(int personId, HandSlot hand)
        {
            if (HeldByPersonId != -1 && HeldByPersonId != personId)
            {
                return HandheldContainerOperationResult.Refused(
                    "HandheldContainer: pail '" + ContainerId + "' pick up by person " + personId
                    + " refused — it is already held by person " + HeldByPersonId + "; set it down first.");
            }

            if (HeldByPersonId == personId && HeldHand.HasValue)
            {
                if (HeldHand.Value == hand)
                {
                    return HandheldContainerOperationResult.Ok(
                        "HandheldContainer: pail '" + ContainerId + "' is already held by person " + personId
                        + " in the " + (hand == HandSlot.Left ? "left" : "right") + " hand.");
                }

                return HandheldContainerOperationResult.Refused(
                    "HandheldContainer: pail '" + ContainerId + "' pick up by person " + personId
                    + " into the " + (hand == HandSlot.Left ? "left" : "right") + " hand refused — it is already held in the "
                    + (HeldHand.Value == HandSlot.Left ? "left" : "right") + " hand; move it between hands instead.");
            }

            HeldByPersonId = personId;
            HeldHand = hand;
            return HandheldContainerOperationResult.Ok(
                "HandheldContainer: pail '" + ContainerId + "' is held by person " + personId
                + " in the " + (hand == HandSlot.Left ? "left" : "right") + " hand.");
        }

        /// <summary>
        /// Sets the container down. Refuses when it is already set down.
        /// On success it clears the holder to -1 and the held hand to
        /// null and keeps the contents inside: a set-down container with
        /// contents is invisible to every person carried view until
        /// picked up again (a placed-container location is P4 or P6
        /// work, not fixed here).
        /// </summary>
        public HandheldContainerOperationResult SetDown()
        {
            if (HeldByPersonId == -1)
            {
                return HandheldContainerOperationResult.Refused(
                    "HandheldContainer: pail '" + ContainerId + "' set down refused — it is already set down; there is nothing to set down.");
            }

            HeldByPersonId = -1;
            HeldHand = null;
            return HandheldContainerOperationResult.Ok(
                "HandheldContainer: pail '" + ContainerId + "' was set down; its contents stay inside.");
        }

        /// <summary>
        /// Places one lot into the container. Refusal order is fixed: the
        /// held-by check, then the circular-nesting guard, then the
        /// duplicate guard, then the null evaluator check, then the fit
        /// check through the evaluator. The held-by check allows two
        /// states and refuses a third: held by the acting person passes,
        /// set down passes, held by a different person refuses naming
        /// the holder. It mutates nothing on any refusal. On success it
        /// appends one entry whose Condition comes from the supplied
        /// SaveDto through FromPerishableCondition, or Unknown when none
        /// is supplied. The container never stores a SaveDto and never
        /// owns perishable state. A placement refuses unless the
        /// evaluator verdict is Fits, and the refusal carries the verdict
        /// reason verbatim.
        /// </summary>
        public HandheldContainerOperationResult Place(
            int personId, LotKey lotKey, string batchId, string itemId, int units,
            PhysicalCapacityEvaluator evaluator, int dayIndex,
            PerishableLotState.SaveDto? saveDto = null)
        {
            if (HeldByPersonId != -1 && HeldByPersonId != personId)
            {
                return HandheldContainerOperationResult.Refused(
                    "HandheldContainer: person " + personId + " place of lot " + lotKey + " into pail '" + ContainerId
                    + "' refused — the pail is held by person " + HeldByPersonId + ", not person " + personId + ".");
            }

            LotKey ownKey = OwnKey;
            if (lotKey.Equals(ownKey))
            {
                return HandheldContainerOperationResult.Refused(
                    "HandheldContainer: place of lot " + lotKey + " into pail '" + ContainerId
                    + "' refused — circular nesting: the lot key names the container itself, so the container cannot be placed inside itself.");
            }

            if (Contains(lotKey))
            {
                return HandheldContainerOperationResult.Refused(
                    "HandheldContainer: place of lot " + lotKey + " into pail '" + ContainerId
                    + "' refused — the pail already holds that lot; one lot cannot be placed twice.");
            }

            if (evaluator == null)
            {
                return HandheldContainerOperationResult.Refused(
                    "HandheldContainer: place of lot " + lotKey + " into pail '" + ContainerId
                    + "' refused — no capacity evaluator was supplied, so fit cannot be checked.");
            }

            var lines = new List<(string ItemId, int Units)>();
            foreach (WornContainerEntry entry in contents)
            {
                lines.Add((entry.ItemId, entry.Units));
            }

            lines.Add((itemId ?? string.Empty, units));
            var query = new PhysicalCapacityQuery(
                PhysicalContainerKind.HandheldPail, ContainerId, new PhysicalLoad(lines), dayIndex);
            var context = new PhysicalCapacityEvaluationContext { ContainerCapacity = Capacity };
            PhysicalCapacityVerdict verdict = evaluator.Evaluate(query, context);
            if (!verdict.Fits)
            {
                return HandheldContainerOperationResult.Refused(verdict.Reason);
            }

            PersonItemCondition condition = PersonItemCondition.Unknown;
            if (saveDto.HasValue)
            {
                condition = PersonInventoryReadModel.FromPerishableCondition(
                    PerishableLotState.FromSaveDto(saveDto.Value).PhysicalCondition);
            }

            contents.Add(new WornContainerEntry(lotKey, batchId ?? string.Empty, itemId ?? string.Empty, units, condition));
            return HandheldContainerOperationResult.Ok(
                "HandheldContainer: lot " + lotKey + " placed into pail '" + ContainerId + "' for person " + personId + ".",
                lotKey, batchId ?? string.Empty);
        }

        /// <summary>
        /// Takes one lot out of the container. Refuses when the container
        /// does not hold that lot. On success it removes the entry in the
        /// same operation. The caller reads the entry through EntryFor
        /// before calling Take, so Take needs only to remove and report.
        /// </summary>
        public HandheldContainerOperationResult Take(LotKey lotKey)
        {
            WornContainerEntry entry = EntryFor(lotKey);
            if (entry == null)
            {
                return HandheldContainerOperationResult.Refused(
                    "HandheldContainer: take of lot " + lotKey + " from pail '" + ContainerId
                    + "' refused — the pail does not hold that lot.");
            }

            contents.Remove(entry);
            return HandheldContainerOperationResult.Ok(
                "HandheldContainer: lot " + lotKey + " taken from pail '" + ContainerId + "'.",
                lotKey, entry.BatchId);
        }
    }
}
