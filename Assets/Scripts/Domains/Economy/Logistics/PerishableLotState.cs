// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority E Slice 5 — perishable lot condition foundation (directive P2,
// Canon XXXI 4.5 and 4.6 CANON LOCKs, Technical XIV 4.1 and 4.2 TECH
// LOCKs). The condition of one lot, as a set of SEPARATE dimensions that
// are never collapsed into one number: physical condition/quality,
// food-safety (use-hazard) state plus evidence, expected remaining
// useful life, storage/handling history, and perceived condition vs
// known condition as separate fields. There is NO single freshness
// percentage anywhere in this type.
//
// Canon XXXI 4.5: perishable degradation and food-safety risk are
// separate causal states, neither represented only by an expiry timer;
// taste and saleability can deteriorate before complete spoilage;
// contamination can produce safety risk before obvious sensory failure;
// mixing new and old stock cannot rejuvenate old product; losses and
// downgraded sales stay visible in inventory and ledgers. Canon 4.6:
// preservation transforms and does not reset — successful preservation
// changes future deterioration and does not erase earlier damage;
// prepared foods are physical batches with quantity, age and condition.
// Technical XIV 4.1: meaningful perishables require lot/batch
// provenance; aggregate quantity alone is insufficient when age,
// condition or safety can differ. The field shape below follows the
// Technical 4.1 suggested state fields: LotId; Item/Species/ProductId;
// source transaction/animal/batch; owner/business; location/container;
// produced/acquired timestamp; quantity/units; condition/quality
// dimensions; safety-risk state/evidence; storage/handling history;
// reservations; current process link; disposition. Technical 4.2:
// output inherits relevant source history instead of receiving fresh
// age and condition by default (Split and TransferredTo inherit the
// source condition exactly).
//
// No authored numbers: no per-item mass, sack weight, person carry
// limit, or shelf-life number exists anywhere in v3.1, and this type
// invents none. The ONLY authored aging rule in the tree is
// BakeryGoodsCondition Fresh/DayOld/Stale plus AgeToDay
// (BakeryShopRuntime.cs), and ApplyElapsedTime applies ONLY that rule,
// for bakery bread only. The F1 spec (PhysicalItemSpec) carries no
// aging parameter at all — Perishable there is a data flag only — so
// there is no spec parameter for deterioration to consult; where no
// authored parameter exists the condition does NOT change and remaining
// useful life stays Unknown. The storageConditions argument is a stable
// signature for a future authored rule: today no authored storage rule
// exists, so it cannot change any outcome.
//
// UNWIRED BY DESIGN: plain data plus pure functions. No flow consults
// this type, no driver is activated, no shopping loop, Bakery driver,
// or live flow is touched. The state holds no copy of any quantity
// between calls beyond its own Units field, which is set at
// construction and changed only by explicit, recorded events
// (deterioration never changes Units; spoilage and split do, and each
// appends a handling event). SpoilageRecord entries are accounting
// only: recording a loss does NOT remove units from any live authority
// in this slice — no authority carries the state yet, and attachment
// to authority DTOs is a downstream step. The state is immutable: every
// operation returns a new state and leaves the original untouched.

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;

namespace LandLedgers.Economy.Logistics
{
    /// <summary>
    /// Physical condition / quality of one lot. Unknown is the honest
    /// answer where no authority carries a condition — never a default
    /// Fresh. Fresh/DayOld/Stale are the Bakery bread-lot conditions
    /// (BakeryGoodsCondition), translated 1:1 by
    /// <see cref="PerishableLotState.FromBakeryCondition"/>.
    /// </summary>
    public enum PerishablePhysicalCondition
    {
        Unknown = 0,
        Fresh = 1,
        DayOld = 2,
        Stale = 3,
    }

    /// <summary>
    /// Food-safety / use-hazard state of one lot, separate from its
    /// physical condition (Canon XXXI 4.5: contamination can produce
    /// safety risk before obvious sensory failure, and taste can
    /// deteriorate before complete spoilage). Unknown where no safety
    /// evidence exists. The evidence lives on the state beside it
    /// (<see cref="PerishableLotState.SafetyEvidence"/>), never inside
    /// the enum.
    /// </summary>
    public enum PerishableSafetyState
    {
        Unknown = 0,
        Safe = 1,
        Suspect = 2,
        Unsafe = 3,
    }

    /// <summary>What has happened to the lot as a whole (Technical XIV 4.1 disposition).</summary>
    public enum PerishableDisposition
    {
        Unknown = 0,
        InStock = 1,
        Reserved = 2,
        InProcess = 3,
        Consumed = 4,
        Sold = 5,
        Spoiled = 6,
        Discarded = 7,
    }

    /// <summary>
    /// Expected remaining useful life: Known(days) or Unknown. Unknown
    /// is the answer wherever no authored shelf-life exists — which is
    /// everywhere in v3.1 — so a Known value here is always caller- or
    /// authority-supplied data, never a value this type invented.
    /// A Known value must be finite and non-negative; anything else
    /// becomes Unknown, never a stored number.
    /// </summary>
    [Serializable]
    public struct UsefulLifeEstimate
    {
        private readonly bool isKnown;
        private readonly double days;

        public bool IsKnown => isKnown && !double.IsNaN(days) && !double.IsInfinity(days) && days >= 0.0;
        public double Days => IsKnown ? days : 0.0;

        public UsefulLifeEstimate(bool known, double days)
        {
            isKnown = known;
            this.days = known && !double.IsNaN(days) && !double.IsInfinity(days) && days >= 0.0 ? days : 0.0;
            if (known && (double.IsNaN(days) || double.IsInfinity(days) || days < 0.0)) isKnown = false;
        }

        public static UsefulLifeEstimate Unknown => new UsefulLifeEstimate(false, 0.0);
        public static UsefulLifeEstimate Known(double days) => new UsefulLifeEstimate(true, days);
    }

    /// <summary>
    /// One storage/handling event in a lot's append-only history
    /// (Technical XIV 4.1 storage/handling history). Kind is a short
    /// vocabulary ("transfer", "split", "spoilage", "preservation");
    /// Detail is free text. History is append-only: events are never
    /// edited or removed, and preservation never erases earlier damage.
    /// </summary>
    [Serializable]
    public struct PerishableHandlingEvent
    {
        public PerishableHandlingEvent(int dayIndex, string kind, string detail)
        {
            DayIndex = dayIndex;
            Kind = kind ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public int DayIndex { get; }
        public string Kind { get; }
        public string Detail { get; }
    }

    /// <summary>
    /// One spoilage accounting entry (lot, units lost, cause, day,
    /// evidence). Accounting only: creating a record removes units from
    /// NO live authority in this slice — the authorities do not carry
    /// the state yet, and applying a recorded loss to an authority is a
    /// downstream step. Units lost plus units remaining equal units
    /// before; nothing is created.
    /// </summary>
    [Serializable]
    public struct SpoilageRecord
    {
        public SpoilageRecord(LotKey lotKey, int unitsLost, string cause, int dayIndex, string evidence)
        {
            LotKey = lotKey;
            UnitsLost = unitsLost;
            Cause = cause ?? string.Empty;
            DayIndex = dayIndex;
            Evidence = evidence ?? string.Empty;
        }

        public LotKey LotKey { get; }
        public int UnitsLost { get; }
        public string Cause { get; }
        public int DayIndex { get; }
        public string Evidence { get; }
    }

    /// <summary>
    /// The perishable condition of one lot. Immutable: every operation
    /// returns a new state. The lot is identified by its
    /// <see cref="LotKey"/> (authority id + lot id, never a bare lot
    /// id); an Empty key means the state has not yet been attached to an
    /// authority lot — attachment to authority DTOs is downstream, and
    /// no key is invented here.
    /// </summary>
    public sealed class PerishableLotState
    {
        private readonly List<PerishableHandlingEvent> handlingHistory;

        /// <summary>
        /// Constructs a state for a lot. Conditions start Unknown, the
        /// safety state starts Unknown, remaining useful life starts
        /// Unknown, history starts empty, disposition starts InStock.
        /// AsOfDay starts at the produced/acquired day: no time has
        /// elapsed for the lot yet.
        /// </summary>
        public PerishableLotState(LotKey key, string itemId, int units, int producedOrAcquiredDay)
            : this(key, itemId, units, producedOrAcquiredDay, producedOrAcquiredDay,
                  false, 0.0, string.Empty, string.Empty, string.Empty,
                  PerishablePhysicalCondition.Unknown, PerishablePhysicalCondition.Unknown,
                  PerishableSafetyState.Unknown, string.Empty, UsefulLifeEstimate.Unknown,
                  new List<PerishableHandlingEvent>(), string.Empty, string.Empty,
                  PerishableDisposition.InStock, string.Empty)
        {
        }

        /// <summary>
        /// Constructs a state for a lot whose authority carries a
        /// simulation-time timestamp for its production/acquisition
        /// (game seconds). The day still governs the authored aging
        /// rule; the timestamp is provenance, carried verbatim.
        /// </summary>
        public PerishableLotState(LotKey key, string itemId, int units, int producedOrAcquiredDay,
            double producedOrAcquiredGameSeconds)
            : this(key, itemId, units, producedOrAcquiredDay, producedOrAcquiredDay,
                  true, producedOrAcquiredGameSeconds, string.Empty, string.Empty, string.Empty,
                  PerishablePhysicalCondition.Unknown, PerishablePhysicalCondition.Unknown,
                  PerishableSafetyState.Unknown, string.Empty, UsefulLifeEstimate.Unknown,
                  new List<PerishableHandlingEvent>(), string.Empty, string.Empty,
                  PerishableDisposition.InStock, string.Empty)
        {
        }

        private PerishableLotState(
            LotKey key, string itemId, int units, int producedOrAcquiredDay, double asOfDay,
            bool hasGameSeconds, double producedOrAcquiredGameSeconds,
            string sourceTransactionOrBatch, string ownerOrBusiness, string locationOrContainer,
            PerishablePhysicalCondition physicalCondition, PerishablePhysicalCondition perceivedCondition,
            PerishableSafetyState safetyState, string safetyEvidence,
            UsefulLifeEstimate remainingUsefulLife,
            List<PerishableHandlingEvent> history,
            string reservations, string currentProcessLink,
            PerishableDisposition disposition, string preservationMethod)
        {
            if (units < 0) throw new ArgumentOutOfRangeException(nameof(units), "a lot cannot hold negative units");
            Key = key;
            ItemId = itemId ?? string.Empty;
            Units = units;
            ProducedOrAcquiredDay = producedOrAcquiredDay;
            AsOfDay = asOfDay;
            HasProducedOrAcquiredGameSeconds = hasGameSeconds;
            ProducedOrAcquiredGameSeconds = producedOrAcquiredGameSeconds;
            SourceTransactionOrBatch = sourceTransactionOrBatch ?? string.Empty;
            OwnerOrBusiness = ownerOrBusiness ?? string.Empty;
            LocationOrContainer = locationOrContainer ?? string.Empty;
            PhysicalCondition = physicalCondition;
            PerceivedCondition = perceivedCondition;
            SafetyState = safetyState;
            SafetyEvidence = safetyEvidence ?? string.Empty;
            RemainingUsefulLife = remainingUsefulLife;
            handlingHistory = history ?? new List<PerishableHandlingEvent>();
            Reservations = reservations ?? string.Empty;
            CurrentProcessLink = currentProcessLink ?? string.Empty;
            Disposition = disposition;
            PreservationMethod = preservationMethod ?? string.Empty;
        }

        /// <summary>The lot this state describes (authority + lot id).</summary>
        public LotKey Key { get; }

        /// <summary>The item id in the owning authority's own vocabulary.</summary>
        public string ItemId { get; }

        /// <summary>
        /// The lot's quantity in its own unit. Set at construction and
        /// changed only by explicit, recorded events (spoilage, split)
        /// — never by deterioration, transfer, or preservation.
        /// </summary>
        public int Units { get; }

        /// <summary>The simulation day the lot was produced or acquired (the age anchor).</summary>
        public int ProducedOrAcquiredDay { get; }

        /// <summary>
        /// The simulation day (fractional days allowed) this state is
        /// evaluated as of. Advanced only by
        /// <see cref="ApplyElapsedTime"/>.
        /// </summary>
        public double AsOfDay { get; }

        /// <summary>Whether the producing/acquiring authority carries a game-seconds timestamp for this lot.</summary>
        public bool HasProducedOrAcquiredGameSeconds { get; }

        /// <summary>The game-seconds timestamp, meaningful only when <see cref="HasProducedOrAcquiredGameSeconds"/> is true.</summary>
        public double ProducedOrAcquiredGameSeconds { get; }

        /// <summary>Source transaction, animal, or batch (Technical XIV 4.1).</summary>
        public string SourceTransactionOrBatch { get; }

        /// <summary>Owner or business holding the lot (Technical XIV 4.1).</summary>
        public string OwnerOrBusiness { get; }

        /// <summary>Location or container holding the lot (Technical XIV 4.1).</summary>
        public string LocationOrContainer { get; }

        /// <summary>Known physical condition / quality — never a single freshness number.</summary>
        public PerishablePhysicalCondition PhysicalCondition { get; }

        /// <summary>
        /// Perceived condition — what an observer reports — kept
        /// separate from <see cref="PhysicalCondition"/>: the two can
        /// differ and are reported separately, never reconciled.
        /// </summary>
        public PerishablePhysicalCondition PerceivedCondition { get; }

        /// <summary>Food-safety / use-hazard state, separate from physical condition (Canon XXXI 4.5).</summary>
        public PerishableSafetyState SafetyState { get; }

        /// <summary>Evidence for <see cref="SafetyState"/> (what was observed, by whom, when — free text).</summary>
        public string SafetyEvidence { get; }

        /// <summary>Expected remaining useful life; Unknown wherever no authored shelf-life exists.</summary>
        public UsefulLifeEstimate RemainingUsefulLife { get; }

        /// <summary>Append-only storage/handling history, oldest first (a fresh snapshot each call).</summary>
        public IReadOnlyList<PerishableHandlingEvent> HandlingHistory =>
            new List<PerishableHandlingEvent>(handlingHistory).AsReadOnly();

        /// <summary>Reservations against this lot (free text; empty means none).</summary>
        public string Reservations { get; }

        /// <summary>Current process link, e.g. a dough batch id (Technical XIV 4.1).</summary>
        public string CurrentProcessLink { get; }

        /// <summary>Disposition of the lot as a whole (Technical XIV 4.1).</summary>
        public PerishableDisposition Disposition { get; }

        /// <summary>The preservation method applied, if any; empty means none. Preservation transforms, never resets (Canon 4.6).</summary>
        public string PreservationMethod { get; }

        /// <summary>Whether any preservation has been applied to this lot.</summary>
        public bool IsPreserved => !string.IsNullOrEmpty(PreservationMethod);

        /// <summary>
        /// 1:1 translation of a Bakery bread lot's condition vocabulary
        /// into this layer's: Fresh, DayOld and Stale map to themselves;
        /// Unspecified — the lot carries no condition — maps to Unknown.
        /// Translation only: it invents no condition for any lot.
        /// </summary>
        public static PerishablePhysicalCondition FromBakeryCondition(BakeryGoodsCondition condition)
        {
            switch (condition)
            {
                case BakeryGoodsCondition.Fresh: return PerishablePhysicalCondition.Fresh;
                case BakeryGoodsCondition.DayOld: return PerishablePhysicalCondition.DayOld;
                case BakeryGoodsCondition.Stale: return PerishablePhysicalCondition.Stale;
                default: return PerishablePhysicalCondition.Unknown;
            }
        }

        /// <summary>Returns a state with the known and perceived physical conditions set; every other dimension untouched.</summary>
        public PerishableLotState WithConditions(PerishablePhysicalCondition known, PerishablePhysicalCondition perceived) =>
            new PerishableLotState(Key, ItemId, Units, ProducedOrAcquiredDay, AsOfDay,
                HasProducedOrAcquiredGameSeconds, ProducedOrAcquiredGameSeconds,
                SourceTransactionOrBatch, OwnerOrBusiness, LocationOrContainer,
                known, perceived, SafetyState, SafetyEvidence, RemainingUsefulLife,
                CopyHistory(), Reservations, CurrentProcessLink, Disposition, PreservationMethod);

        /// <summary>Returns a state with the safety state and its evidence set; every other dimension untouched.</summary>
        public PerishableLotState WithSafety(PerishableSafetyState safety, string evidence) =>
            new PerishableLotState(Key, ItemId, Units, ProducedOrAcquiredDay, AsOfDay,
                HasProducedOrAcquiredGameSeconds, ProducedOrAcquiredGameSeconds,
                SourceTransactionOrBatch, OwnerOrBusiness, LocationOrContainer,
                PhysicalCondition, PerceivedCondition, safety, evidence, RemainingUsefulLife,
                CopyHistory(), Reservations, CurrentProcessLink, Disposition, PreservationMethod);

        /// <summary>
        /// Returns a state with the quantity set; every other dimension
        /// untouched, and no history event appended. This is the
        /// bookkeeping sync for authorities that decrement a lot in
        /// place (a sale, a dispense, a meal): the authority's own
        /// record — sale record, dispense line, consumption record —
        /// is the event, so no second event is invented here. The
        /// split that moves units to a NEW lot is recorded instead by
        /// <see cref="Split"/>, which appends a split event to both
        /// parts. A negative quantity is refused: a lot never holds
        /// negative stock.
        /// </summary>
        public PerishableLotState WithUnits(int units)
        {
            if (units < 0) throw new ArgumentOutOfRangeException(nameof(units), "a lot cannot hold negative units");
            return new PerishableLotState(Key, ItemId, units, ProducedOrAcquiredDay, AsOfDay,
                HasProducedOrAcquiredGameSeconds, ProducedOrAcquiredGameSeconds,
                SourceTransactionOrBatch, OwnerOrBusiness, LocationOrContainer,
                PhysicalCondition, PerceivedCondition, SafetyState, SafetyEvidence,
                RemainingUsefulLife, CopyHistory(),
                Reservations, CurrentProcessLink, Disposition, PreservationMethod);
        }

        /// <summary>
        /// Returns a state with the Technical XIV 4.1 provenance fields
        /// set (source transaction or batch, owner or business, location
        /// or container); every other dimension untouched, and no history
        /// event appended — the provenance is the creation record, not a
        /// later event. Used when a creation site attaches a state to a
        /// lot it just made (a bake names its dough batch, a Millbrook
        /// order names its order id). Nulls become empty strings, never
        /// a fabricated value.
        /// </summary>
        public PerishableLotState WithSource(string sourceTransactionOrBatch, string ownerOrBusiness, string locationOrContainer) =>
            new PerishableLotState(Key, ItemId, Units, ProducedOrAcquiredDay, AsOfDay,
                HasProducedOrAcquiredGameSeconds, ProducedOrAcquiredGameSeconds,
                sourceTransactionOrBatch ?? string.Empty, ownerOrBusiness ?? string.Empty,
                locationOrContainer ?? string.Empty,
                PhysicalCondition, PerceivedCondition, SafetyState, SafetyEvidence,
                RemainingUsefulLife, CopyHistory(),
                Reservations, CurrentProcessLink, Disposition, PreservationMethod);

        /// <summary>
        /// Deterministic deterioration over simulated elapsed time: a
        /// pure function of simulation time (never wall clock, never
        /// randomness), so equivalent tick sequences give equivalent
        /// states. Deterioration applies ONLY where an authored
        /// parameter exists — today, only the BakeryGoodsCondition
        /// AgeToDay rule for bakery bread (ItemId
        /// <see cref="BakeryBreadCatalog.BreadLoafId"/>): age is the
        /// whole-day difference between the new as-of day and the
        /// produced day, Fresh at age 0 or less, DayOld at age 1, Stale
        /// at age 2 or more, exactly as AgeToDay computes it. Where no
        /// authored parameter exists the condition does NOT change and
        /// remaining useful life stays Unknown; the model never invents
        /// a decay rate. Elapsed time alone never creates a safety
        /// state, never changes the quantity, and never appends a
        /// handling event. storageConditions is accepted for a future
        /// authored rule and changes nothing today: no authored storage
        /// rule exists in v3.1.
        /// </summary>
        public static PerishableLotState ApplyElapsedTime(
            PerishableLotState state, double elapsedSimulationDays, string storageConditions)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (double.IsNaN(elapsedSimulationDays) || double.IsInfinity(elapsedSimulationDays) || elapsedSimulationDays < 0.0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSimulationDays),
                    "elapsed simulation time must be a finite, non-negative number of days");

            double asOfDay = state.AsOfDay + elapsedSimulationDays;
            PerishablePhysicalCondition condition = state.PhysicalCondition;
            if (string.Equals(state.ItemId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal))
            {
                int age = (int)Math.Floor(asOfDay) - state.ProducedOrAcquiredDay;
                condition = age <= 0 ? PerishablePhysicalCondition.Fresh
                    : age == 1 ? PerishablePhysicalCondition.DayOld
                    : PerishablePhysicalCondition.Stale;
            }

            return new PerishableLotState(state.Key, state.ItemId, state.Units, state.ProducedOrAcquiredDay, asOfDay,
                state.HasProducedOrAcquiredGameSeconds, state.ProducedOrAcquiredGameSeconds,
                state.SourceTransactionOrBatch, state.OwnerOrBusiness, state.LocationOrContainer,
                condition, state.PerceivedCondition, state.SafetyState, state.SafetyEvidence,
                state.RemainingUsefulLife, state.CopyHistory(),
                state.Reservations, state.CurrentProcessLink, state.Disposition, state.PreservationMethod);
        }

        /// <summary>
        /// Applies a preservation method (Canon XXXI 4.6: preservation
        /// transforms and does not reset). The condition is NEVER
        /// improved — DayOld stays DayOld, Stale stays Stale — and the
        /// age is untouched; the preservation is recorded as a handling
        /// event and in <see cref="PreservationMethod"/>. No authored
        /// preservation rule changes the deterioration rate, so a later
        /// <see cref="ApplyElapsedTime"/> still uses the same authored
        /// age rule; the method is recorded for the slice that authors
        /// one.
        /// </summary>
        public PerishableLotState ApplyPreservation(string method, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(method))
                throw new ArgumentException("a preservation needs a named method", nameof(method));
            var history = CopyHistory();
            history.Add(new PerishableHandlingEvent(dayIndex, "preservation", "preserved by " + method));
            return new PerishableLotState(Key, ItemId, Units, ProducedOrAcquiredDay, AsOfDay,
                HasProducedOrAcquiredGameSeconds, ProducedOrAcquiredGameSeconds,
                SourceTransactionOrBatch, OwnerOrBusiness, LocationOrContainer,
                PhysicalCondition, PerceivedCondition, SafetyState, SafetyEvidence,
                RemainingUsefulLife, history,
                Reservations, CurrentProcessLink, Disposition, method);
        }

        /// <summary>
        /// Records a transfer of the whole lot to a new location or
        /// container: the condition dimensions, safety state, remaining
        /// life, and quantity are unchanged — a transfer never improves
        /// or resets a condition — and the transfer is appended to the
        /// handling history. Ownership is NOT changed here; ownership
        /// stays on the lot's owning authority.
        /// </summary>
        public PerishableLotState TransferredTo(string newLocationOrContainer, int dayIndex, string note)
        {
            var history = CopyHistory();
            history.Add(new PerishableHandlingEvent(dayIndex, "transfer", note ?? string.Empty));
            return new PerishableLotState(Key, ItemId, Units, ProducedOrAcquiredDay, AsOfDay,
                HasProducedOrAcquiredGameSeconds, ProducedOrAcquiredGameSeconds,
                SourceTransactionOrBatch, OwnerOrBusiness, newLocationOrContainer ?? string.Empty,
                PhysicalCondition, PerceivedCondition, SafetyState, SafetyEvidence,
                RemainingUsefulLife, history,
                Reservations, CurrentProcessLink, Disposition, PreservationMethod);
        }

        /// <summary>
        /// Splits off a partial quantity (Technical XIV 4.2: output
        /// inherits source history). Both resulting states inherit the
        /// source condition, safety state, remaining life, and history
        /// exactly — no averaging, no fresh start — and each records
        /// the split as a handling event. Units conserve exactly:
        /// remaining plus transferred equal the units before.
        /// GAP, recorded not papered over: the split-off part keeps the
        /// source LotKey until the receiving authority assigns its own
        /// lot id; this type invents no lot id. The receiving
        /// authority's provenance-key rule (F4) is where the new key
        /// is established.
        /// </summary>
        public (PerishableLotState Remaining, PerishableLotState Transferred) Split(int units, int dayIndex)
        {
            if (units <= 0 || units > Units)
                throw new ArgumentOutOfRangeException(nameof(units),
                    "a split must move a positive quantity no larger than the lot holds");
            var remainingHistory = CopyHistory();
            remainingHistory.Add(new PerishableHandlingEvent(dayIndex, "split",
                "split off " + units + " units"));
            var transferredHistory = CopyHistory();
            transferredHistory.Add(new PerishableHandlingEvent(dayIndex, "split",
                "split from lot: " + units + " units"));
            var remaining = new PerishableLotState(Key, ItemId, Units - units, ProducedOrAcquiredDay, AsOfDay,
                HasProducedOrAcquiredGameSeconds, ProducedOrAcquiredGameSeconds,
                SourceTransactionOrBatch, OwnerOrBusiness, LocationOrContainer,
                PhysicalCondition, PerceivedCondition, SafetyState, SafetyEvidence,
                RemainingUsefulLife, remainingHistory,
                Reservations, CurrentProcessLink, Disposition, PreservationMethod);
            var transferred = new PerishableLotState(Key, ItemId, units, ProducedOrAcquiredDay, AsOfDay,
                HasProducedOrAcquiredGameSeconds, ProducedOrAcquiredGameSeconds,
                SourceTransactionOrBatch, OwnerOrBusiness, LocationOrContainer,
                PhysicalCondition, PerceivedCondition, SafetyState, SafetyEvidence,
                RemainingUsefulLife, transferredHistory,
                Reservations, CurrentProcessLink, Disposition, PreservationMethod);
            return (remaining, transferred);
        }

        /// <summary>
        /// Creates a spoilage accounting entry and returns the updated
        /// state: the lost units leave THIS state (units lost plus units
        /// remaining equal units before) and the loss is appended to the
        /// handling history. A fully spoiled lot is disposed Spoiled.
        /// The record removes units from NO live authority in this
        /// slice — the authorities do not carry the state yet; applying
        /// a recorded loss to an authority is downstream.
        /// </summary>
        public SpoilageRecord RecordSpoilage(int unitsLost, string cause, int dayIndex, string evidence,
            out PerishableLotState updated)
        {
            if (unitsLost <= 0 || unitsLost > Units)
                throw new ArgumentOutOfRangeException(nameof(unitsLost),
                    "a spoilage record must record a positive loss no larger than the lot holds");
            var record = new SpoilageRecord(Key, unitsLost, cause ?? string.Empty, dayIndex, evidence ?? string.Empty);
            var history = CopyHistory();
            history.Add(new PerishableHandlingEvent(dayIndex, "spoilage",
                "lost " + unitsLost + " units (" + (cause ?? string.Empty) + ")"));
            int remainingUnits = Units - unitsLost;
            updated = new PerishableLotState(Key, ItemId, remainingUnits, ProducedOrAcquiredDay, AsOfDay,
                HasProducedOrAcquiredGameSeconds, ProducedOrAcquiredGameSeconds,
                SourceTransactionOrBatch, OwnerOrBusiness, LocationOrContainer,
                PhysicalCondition, PerceivedCondition, SafetyState, SafetyEvidence,
                RemainingUsefulLife, history,
                Reservations, CurrentProcessLink,
                remainingUnits == 0 ? PerishableDisposition.Spoiled : Disposition,
                PreservationMethod);
            return record;
        }

        /// <summary>
        /// Field-by-field equality, including the handling history in
        /// order. Used by the tick-subdivision and save/load tests:
        /// two states are equal only when every dimension agrees.
        /// </summary>
        public bool EqualsState(PerishableLotState other)
        {
            if (other == null) return false;
            if (!Key.Equals(other.Key)) return false;
            if (!string.Equals(ItemId, other.ItemId, StringComparison.Ordinal)) return false;
            if (Units != other.Units) return false;
            if (ProducedOrAcquiredDay != other.ProducedOrAcquiredDay) return false;
            if (AsOfDay != other.AsOfDay) return false;
            if (HasProducedOrAcquiredGameSeconds != other.HasProducedOrAcquiredGameSeconds) return false;
            if (ProducedOrAcquiredGameSeconds != other.ProducedOrAcquiredGameSeconds) return false;
            if (!string.Equals(SourceTransactionOrBatch, other.SourceTransactionOrBatch, StringComparison.Ordinal)) return false;
            if (!string.Equals(OwnerOrBusiness, other.OwnerOrBusiness, StringComparison.Ordinal)) return false;
            if (!string.Equals(LocationOrContainer, other.LocationOrContainer, StringComparison.Ordinal)) return false;
            if (PhysicalCondition != other.PhysicalCondition) return false;
            if (PerceivedCondition != other.PerceivedCondition) return false;
            if (SafetyState != other.SafetyState) return false;
            if (!string.Equals(SafetyEvidence, other.SafetyEvidence, StringComparison.Ordinal)) return false;
            if (RemainingUsefulLife.IsKnown != other.RemainingUsefulLife.IsKnown) return false;
            if (RemainingUsefulLife.IsKnown && RemainingUsefulLife.Days != other.RemainingUsefulLife.Days) return false;
            if (handlingHistory.Count != other.handlingHistory.Count) return false;
            for (int i = 0; i < handlingHistory.Count; i++)
            {
                if (handlingHistory[i].DayIndex != other.handlingHistory[i].DayIndex) return false;
                if (!string.Equals(handlingHistory[i].Kind, other.handlingHistory[i].Kind, StringComparison.Ordinal)) return false;
                if (!string.Equals(handlingHistory[i].Detail, other.handlingHistory[i].Detail, StringComparison.Ordinal)) return false;
            }
            if (!string.Equals(Reservations, other.Reservations, StringComparison.Ordinal)) return false;
            if (!string.Equals(CurrentProcessLink, other.CurrentProcessLink, StringComparison.Ordinal)) return false;
            if (Disposition != other.Disposition) return false;
            if (!string.Equals(PreservationMethod, other.PreservationMethod, StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>
        /// The state's own plain-data form, for the deterministic
        /// save/load round-trip (the F5 Capture/Export plus Load/Import
        /// pattern, at the state's own level: no authority carries the
        /// state yet). Handling history round-trips as parallel arrays.
        /// </summary>
        [Serializable]
        public struct SaveDto
        {
            public string AuthorityId;
            public string LotId;
            public string ItemId;
            public int Units;
            public int ProducedOrAcquiredDay;
            public double AsOfDay;
            public bool HasProducedOrAcquiredGameSeconds;
            public double ProducedOrAcquiredGameSeconds;
            public string SourceTransactionOrBatch;
            public string OwnerOrBusiness;
            public string LocationOrContainer;
            public int PhysicalCondition;
            public int PerceivedCondition;
            public int SafetyState;
            public string SafetyEvidence;
            public bool RemainingLifeKnown;
            public double RemainingLifeDays;
            public string[] HandlingKinds;
            public int[] HandlingDays;
            public string[] HandlingDetails;
            public string Reservations;
            public string CurrentProcessLink;
            public int Disposition;
            public string PreservationMethod;
        }

        /// <summary>Captures this state into its plain-data form.</summary>
        public SaveDto CaptureSaveDto()
        {
            var kinds = new string[handlingHistory.Count];
            var days = new int[handlingHistory.Count];
            var details = new string[handlingHistory.Count];
            for (int i = 0; i < handlingHistory.Count; i++)
            {
                kinds[i] = handlingHistory[i].Kind;
                days[i] = handlingHistory[i].DayIndex;
                details[i] = handlingHistory[i].Detail;
            }
            return new SaveDto
            {
                AuthorityId = Key.AuthorityId,
                LotId = Key.LotId,
                ItemId = ItemId,
                Units = Units,
                ProducedOrAcquiredDay = ProducedOrAcquiredDay,
                AsOfDay = AsOfDay,
                HasProducedOrAcquiredGameSeconds = HasProducedOrAcquiredGameSeconds,
                ProducedOrAcquiredGameSeconds = ProducedOrAcquiredGameSeconds,
                SourceTransactionOrBatch = SourceTransactionOrBatch,
                OwnerOrBusiness = OwnerOrBusiness,
                LocationOrContainer = LocationOrContainer,
                PhysicalCondition = (int)PhysicalCondition,
                PerceivedCondition = (int)PerceivedCondition,
                SafetyState = (int)SafetyState,
                SafetyEvidence = SafetyEvidence,
                RemainingLifeKnown = RemainingUsefulLife.IsKnown,
                RemainingLifeDays = RemainingUsefulLife.IsKnown ? RemainingUsefulLife.Days : 0.0,
                HandlingKinds = kinds,
                HandlingDays = days,
                HandlingDetails = details,
                Reservations = Reservations,
                CurrentProcessLink = CurrentProcessLink,
                Disposition = (int)Disposition,
                PreservationMethod = PreservationMethod,
            };
        }

        /// <summary>
        /// Restores a state from its plain-data form. A default DTO
        /// restores to an empty-key, zero-unit, all-Unknown state rather
        /// than a fabrication; a DTO with ragged history arrays restores
        /// the longest consistent prefix. Negative units in a DTO are
        /// clamped to zero (a DTO never carries negative stock).
        /// </summary>
        public static PerishableLotState FromSaveDto(SaveDto dto)
        {
            var history = new List<PerishableHandlingEvent>();
            int kinds = dto.HandlingKinds != null ? dto.HandlingKinds.Length : 0;
            int days = dto.HandlingDays != null ? dto.HandlingDays.Length : 0;
            int details = dto.HandlingDetails != null ? dto.HandlingDetails.Length : 0;
            int count = Math.Min(kinds, Math.Min(days, details));
            for (int i = 0; i < count; i++)
            {
                history.Add(new PerishableHandlingEvent(
                    dto.HandlingDays[i],
                    dto.HandlingKinds[i] ?? string.Empty,
                    dto.HandlingDetails[i] ?? string.Empty));
            }
            return new PerishableLotState(
                new LotKey(dto.AuthorityId ?? string.Empty, dto.LotId ?? string.Empty),
                dto.ItemId ?? string.Empty,
                Math.Max(0, dto.Units),
                dto.ProducedOrAcquiredDay,
                dto.AsOfDay,
                dto.HasProducedOrAcquiredGameSeconds,
                dto.ProducedOrAcquiredGameSeconds,
                dto.SourceTransactionOrBatch ?? string.Empty,
                dto.OwnerOrBusiness ?? string.Empty,
                dto.LocationOrContainer ?? string.Empty,
                (PerishablePhysicalCondition)dto.PhysicalCondition,
                (PerishablePhysicalCondition)dto.PerceivedCondition,
                (PerishableSafetyState)dto.SafetyState,
                dto.SafetyEvidence ?? string.Empty,
                dto.RemainingLifeKnown ? UsefulLifeEstimate.Known(dto.RemainingLifeDays) : UsefulLifeEstimate.Unknown,
                history,
                dto.Reservations ?? string.Empty,
                dto.CurrentProcessLink ?? string.Empty,
                (PerishableDisposition)dto.Disposition,
                dto.PreservationMethod ?? string.Empty);
        }

        private List<PerishableHandlingEvent> CopyHistory() =>
            new List<PerishableHandlingEvent>(handlingHistory);
    }
}
