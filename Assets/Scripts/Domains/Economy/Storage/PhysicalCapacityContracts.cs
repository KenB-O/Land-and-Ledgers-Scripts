// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice F2 — shared capacity queries (P9 plan section 2.4,
// section 4 Slice F2; Canon XXXI section 7.1A capacity axes; Technical XIV
// section 6.2 storage lock). One query shape, many authorities. The
// evaluator DELEGATES ONLY: it stores no capacity of its own, renders no
// verdict of its own, and returns the underlying authority's verdict on
// every leg. Priority V1: the shopping loop consults it at settle time
// with its live carry cap supplied; it is also constructed in tests
// against the real authorities (the P1 Slice 0/6 real-authority,
// test-side driver pattern).
//
// Unknown-first rule (P9 plan section 2.4): the load resolves through the
// F1 spec registry, and a query returns Unknown — a refusal, not a number —
// when any item's mass or volume is Unknown ON AN AXIS THE CONTAINER
// BOUNDS. A container that bounds only item count can still answer for
// Unknown-mass items on the count axis only (the same per-axis rule
// PhysicalStorageZone.CanFit already implements). An Unknown measure is
// NEVER silently treated as zero.
//
// Canon XXXI section 7.1A refinement (v3.1 reconciliation): store capacity
// is tracked as Total, Allocated or Reserved, and Occupied, with
// merchandise space dedicated by product line rather than perfectly
// fungible every moment. The BusinessStorage leg therefore exposes the
// 7.1A axes where the underlying authority supports them — Total and
// Occupied from BusinessCapacityState — and returns Unknown where it does
// not (Allocated: no business-level allocated-storage figure exists in
// BusinessCapacityState). Exact capacities remain open until the
// merchandise catalogue and building footprints are tested; no capacity
// number is authored here.
//
// Technical XIV section 6.2 TECH LOCK: cargo storage requires an actual
// storage FunctionalSpace or Asset with capacity, access, custody rules and
// handling labour and equipment. A carrier without suitable storage may haul
// freight but may not silently accumulate warehouse inventory. The Wagon
// leg therefore answers HAUL capacity only, never warehouse accumulation:
// a wagon query says whether the wagon can carry the load, never whether
// goods may be stored in it.
//
// Risk C-1 (P9 plan section 6): the category pool and the identified
// retail lots are two views of the same goods and must never be summed
// into one stock figure. The BusinessStorage leg reports them separately
// (Occupied from the category pool, IdentifiedLotUnits from the identified
// lots for the load's items) and provides no combined figure.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Transport;
using LandLedgers.Population;

namespace LandLedgers.Economy.Storage
{
    /// <summary>Which authority answers a capacity query. HorsePack is reserved: no authority exists yet.</summary>
    public enum PhysicalContainerKind
    {
        Equipment = 0,
        BusinessStorage = 1,
        StorageZone = 2,
        Wagon = 3,
        OnFootPerson = 4,
        /// <summary>Reserved: no horse-pack capacity exists in source; queries return "no authority".</summary>
        HorsePack = 5,
        /// <summary>P2a: a worn satchel, caller-supplied capacity on four axes; see EvaluateWornSatchel.</summary>
        WornSatchel = 6,
        /// <summary>P2b: a handheld pail, caller-supplied capacity on four axes; see EvaluateHandheldPail.</summary>
        HandheldPail = 7,
    }

    /// <summary>
    /// A load to be carried or stored: a list of (ItemId, Units) lines.
    /// Resolution through the F1 registry happens in <see cref="Resolve"/>; a
    /// load itself carries no mass or volume until resolved.
    /// </summary>
    public struct PhysicalLoad
    {
        private readonly List<(string ItemId, int Units)> lines;

        public IReadOnlyList<(string ItemId, int Units)> Lines => lines;

        public PhysicalLoad(IEnumerable<(string ItemId, int Units)> lines)
        {
            this.lines = lines != null
                ? new List<(string ItemId, int Units)>(lines)
                : new List<(string ItemId, int Units)>();
        }

        public static PhysicalLoad Empty => new PhysicalLoad(new (string, int)[0]);

        /// <summary>
        /// Resolves the load through <see cref="PhysicalItemSpecRegistry"/>: Known
        /// total mass/volume only where every item's spec is Known on that axis;
        /// otherwise Unknown, naming the first unknown item on each axis (line
        /// order). A line with zero or negative units contributes nothing and
        /// names nothing. An item with no registered spec is Unknown on both
        /// axes. No value is invented for an unknown item.
        /// </summary>
        public PhysicalLoadResolution Resolve()
        {
            int totalUnits = 0;
            float massSumKg = 0f;
            float volumeSumLiters = 0f;
            bool massKnown = true;
            bool volumeKnown = true;
            string firstUnknownMassItemId = null;
            string firstUnknownVolumeItemId = null;
            string firstUnknownItemId = null;

            if (lines != null)
            {
                foreach ((string ItemId, int Units) line in lines)
                {
                    if (line.Units <= 0) continue;
                    totalUnits += line.Units;

                    bool itemUnknown = false;
                    if (!PhysicalItemSpecRegistry.TryGet(line.ItemId, out PhysicalItemSpec spec))
                    {
                        // No spec at all: Unknown on both axes.
                        itemUnknown = true;
                        massKnown = false;
                        volumeKnown = false;
                        if (firstUnknownMassItemId == null) firstUnknownMassItemId = line.ItemId;
                        if (firstUnknownVolumeItemId == null) firstUnknownVolumeItemId = line.ItemId;
                    }
                    else
                    {
                        if (!spec.UnitMassKg.IsKnown)
                        {
                            itemUnknown = true;
                            massKnown = false;
                            if (firstUnknownMassItemId == null) firstUnknownMassItemId = line.ItemId;
                        }
                        else
                        {
                            massSumKg += line.Units * spec.UnitMassKg.Value;
                        }

                        if (!spec.UnitVolumeLiters.IsKnown)
                        {
                            itemUnknown = true;
                            volumeKnown = false;
                            if (firstUnknownVolumeItemId == null) firstUnknownVolumeItemId = line.ItemId;
                        }
                        else
                        {
                            volumeSumLiters += line.Units * spec.UnitVolumeLiters.Value;
                        }
                    }

                    if (itemUnknown && firstUnknownItemId == null) firstUnknownItemId = line.ItemId;
                }
            }

            return new PhysicalLoadResolution(
                totalUnits,
                massKnown ? PhysicalMeasure.Known(massSumKg) : PhysicalMeasure.Unknown,
                volumeKnown ? PhysicalMeasure.Known(volumeSumLiters) : PhysicalMeasure.Unknown,
                volumeKnown ? PhysicalMeasure.Known(volumeSumLiters / 1000f) : PhysicalMeasure.Unknown,
                firstUnknownMassItemId,
                firstUnknownVolumeItemId,
                firstUnknownItemId);
        }
    }

    /// <summary>
    /// A resolved load: Known totals only where every item is Known on that
    /// axis, otherwise Unknown with the first unknown item named (line order).
    /// Volume is carried in both litres (the spec unit) and cubic metres (the
    /// zone and wagon unit; 1 m3 = 1000 L).
    /// </summary>
    public struct PhysicalLoadResolution
    {
        public int TotalUnits { get; }
        public PhysicalMeasure TotalMassKg { get; }
        public PhysicalMeasure TotalVolumeLiters { get; }
        public PhysicalMeasure TotalVolumeCubicMeters { get; }
        public string FirstUnknownMassItemId { get; }
        public string FirstUnknownVolumeItemId { get; }
        public string FirstUnknownItemId { get; }

        public bool IsMassKnown => TotalMassKg.IsKnown;
        public bool IsVolumeKnown => TotalVolumeLiters.IsKnown;
        public bool IsFullyKnown => IsMassKnown && IsVolumeKnown;

        public PhysicalLoadResolution(
            int totalUnits,
            PhysicalMeasure totalMassKg,
            PhysicalMeasure totalVolumeLiters,
            PhysicalMeasure totalVolumeCubicMeters,
            string firstUnknownMassItemId,
            string firstUnknownVolumeItemId,
            string firstUnknownItemId)
        {
            TotalUnits = totalUnits;
            TotalMassKg = totalMassKg;
            TotalVolumeLiters = totalVolumeLiters;
            TotalVolumeCubicMeters = totalVolumeCubicMeters;
            FirstUnknownMassItemId = firstUnknownMassItemId;
            FirstUnknownVolumeItemId = firstUnknownVolumeItemId;
            FirstUnknownItemId = firstUnknownItemId;
        }
    }

    /// <summary>
    /// One capacity question, in the shape every container authority answers:
    /// which container, what load, on which day. Read-only; the answering
    /// authority evaluates it. ContainerKey is the equipment key, business id,
    /// zone id, wagon asset id, or person id, per <see cref="PhysicalContainerKind"/>.
    /// </summary>
    public struct PhysicalCapacityQuery
    {
        public PhysicalContainerKind ContainerKind { get; }
        public string ContainerKey { get; }
        public PhysicalLoad Load { get; }
        public int DayIndex { get; }

        public PhysicalCapacityQuery(PhysicalContainerKind containerKind, string containerKey, PhysicalLoad load, int dayIndex)
        {
            ContainerKind = containerKind;
            ContainerKey = containerKey ?? string.Empty;
            Load = load;
            DayIndex = dayIndex;
        }
    }

    /// <summary>
    /// The inputs to a person-carry question (P9 plan section 2.4). Age comes
    /// from the birth date via <see cref="AgeAuthority"/>, never from the
    /// cached age field. Labour capacity comes from
    /// <see cref="PersonHealthState"/>; nutrition from
    /// <see cref="PersonNutritionState"/>. Body size, strength and fatigue
    /// have no authority in source, so they are present as
    /// <see cref="PhysicalMeasure.Unknown"/> and named by
    /// <see cref="MissingInputNames"/> — never derived, never guessed.
    /// EquipmentHeld lists the equipment keys the person legitimately holds,
    /// resolved via <see cref="EquipmentAccessResolver"/> (see
    /// <see cref="ResolveEquipmentHeld"/>). There is deliberately no
    /// demographic field on this type: no carry rule in this campaign varies
    /// by any demographic attribute.
    /// </summary>
    public struct PersonCarryInputs
    {
        public int PersonId { get; }
        public int AgeYears { get; }
        public int LaborCapacityPercent { get; }
        public float Nutrition01 { get; }
        public PhysicalMeasure Fatigue { get; }
        public PhysicalMeasure BodySize { get; }
        public PhysicalMeasure Strength { get; }
        public IReadOnlyList<string> EquipmentHeld { get; }

        public PersonCarryInputs(
            int personId,
            int ageYears,
            int laborCapacityPercent,
            float nutrition01,
            PhysicalMeasure fatigue,
            PhysicalMeasure bodySize,
            PhysicalMeasure strength,
            IEnumerable<string> equipmentHeld)
        {
            PersonId = personId;
            AgeYears = ageYears;
            LaborCapacityPercent = laborCapacityPercent;
            Nutrition01 = nutrition01;
            Fatigue = fatigue;
            BodySize = bodySize;
            Strength = strength;
            EquipmentHeld = equipmentHeld != null
                ? new List<string>(equipmentHeld).AsReadOnly()
                : new List<string>().AsReadOnly();
        }

        /// <summary>
        /// Names the carry inputs that are still Unknown and therefore block
        /// any carry-limit answer. In this slice that is always body size,
        /// strength and fatigue; a later slice that authors a value removes
        /// that name from this list by supplying a Known measure.
        /// </summary>
        public IReadOnlyList<string> MissingInputNames
        {
            get
            {
                var missing = new List<string>();
                if (!BodySize.IsKnown) missing.Add("body size");
                if (!Strength.IsKnown) missing.Add("strength");
                if (!Fatigue.IsKnown) missing.Add("fatigue");
                return missing;
            }
        }

        /// <summary>
        /// Builds the inputs from a <see cref="PersonState"/>. Age is derived
        /// from the authoritative birth date on the given Gregorian date via
        /// <see cref="AgeAuthority"/>; the cached age field is never read.
        /// Body size, strength and fatigue stay Unknown: no authority in
        /// source supplies them.
        /// </summary>
        public static PersonCarryInputs FromPersonState(
            PersonState person,
            int currentYear,
            int currentMonth,
            int currentDay,
            IEnumerable<string> equipmentHeld)
        {
            if (person == null) throw new ArgumentNullException(nameof(person));
            return new PersonCarryInputs(
                person.id,
                person.GetTrueAge(currentYear, currentMonth, currentDay),
                person.health != null ? person.health.laborCapacityPercent : 100,
                person.nutrition != null ? person.nutrition.nutrition01 : 1f,
                PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown,
                equipmentHeld);
        }

        /// <summary>
        /// The equipment keys a holder legitimately holds, from the candidate
        /// kinds, via <see cref="EquipmentAccessResolver"/>: owned kinds
        /// first, then unexpired grants. Never invents access; a kind with no
        /// recorded ownership or grant is not listed. The result is what
        /// <see cref="FromPersonState"/> takes as its equipment-held list.
        /// </summary>
        public static IReadOnlyList<string> ResolveEquipmentHeld(
            EquipmentAccessResolver resolver,
            string holderKind,
            string holderId,
            IEnumerable<string> candidateKinds,
            Func<string, string, List<string>> ownedKindsFor,
            int dayIndex)
        {
            var held = new List<string>();
            if (resolver == null || candidateKinds == null) return held;
            foreach (string kind in candidateKinds)
            {
                if (string.IsNullOrWhiteSpace(kind)) continue;
                if (resolver.HasAccess(holderKind, holderId, kind, ownedKindsFor, dayIndex, out _))
                    held.Add(kind);
            }
            return held;
        }
    }

    /// <summary>
    /// The caller-supplied demand for one capability operation in P3.
    /// Every field is supplied by the caller for that one operation.
    /// No demand value is read from any table, registry, or item spec.
    /// An Unknown measure means that axis is not demanded. A zero
    /// carry duration means duration is not demanded. An empty
    /// equipment kind means no equipment is demanded. A demand with
    /// nothing demanded always passes the consult, so the wrappers
    /// stay usable as a plain allocate path with an explicit empty
    /// demand.
    /// </summary>
    public struct PhysicalCapabilityDemand
    {
        public PhysicalMeasure MinimumLiftStrength { get; }
        public PhysicalMeasure MinimumCarryStrength { get; }
        public PhysicalMeasure MinimumBodySize { get; }
        public PhysicalMeasure MaximumFatigue { get; }
        public int CarryDurationMinutes { get; }
        public bool NeedsSizeAndHandlingVerdict { get; }
        public string RequiredEquipmentKind { get; }

        public PhysicalCapabilityDemand(
            PhysicalMeasure minimumLiftStrength,
            PhysicalMeasure minimumCarryStrength,
            PhysicalMeasure minimumBodySize,
            PhysicalMeasure maximumFatigue,
            int carryDurationMinutes,
            bool needsSizeAndHandlingVerdict,
            string requiredEquipmentKind)
        {
            MinimumLiftStrength = minimumLiftStrength;
            MinimumCarryStrength = minimumCarryStrength;
            MinimumBodySize = minimumBodySize;
            MaximumFatigue = maximumFatigue;
            CarryDurationMinutes = carryDurationMinutes;
            NeedsSizeAndHandlingVerdict = needsSizeAndHandlingVerdict;
            RequiredEquipmentKind = requiredEquipmentKind ?? string.Empty;
        }

        public static PhysicalCapabilityDemand None =>
            new PhysicalCapabilityDemand(
                PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown,
                PhysicalMeasure.Unknown,
                0,
                false,
                string.Empty);
    }

    /// <summary>
    /// The outcome of one capability consult in P3. Reason names the
    /// person, the demand that was asked for, and each missing input
    /// or the demand that failed. MissingInputs lists the demanded
    /// but missing names in consult order, namely carry duration,
    /// object size and handling, body size, strength, and fatigue.
    /// The person context fields echo the inputs the consult ran
    /// with. Age, labor capacity, and nutrition gate nothing in P3.
    /// They are echoed only so a future owner decision can see them.
    /// </summary>
    public struct PhysicalCapabilityVerdict
    {
        public PhysicalCapacityOutcome Outcome { get; }
        public string Reason { get; }
        public IReadOnlyList<string> MissingInputs { get; }
        public int PersonId { get; }
        public int AgeYears { get; }
        public int LaborCapacityPercent { get; }
        public float Nutrition01 { get; }

        public bool Fits => Outcome == PhysicalCapacityOutcome.Fits;

        public PhysicalCapabilityVerdict(
            PhysicalCapacityOutcome outcome,
            string reason,
            IReadOnlyList<string> missingInputs,
            int personId,
            int ageYears,
            int laborCapacityPercent,
            float nutrition01)
        {
            Outcome = outcome;
            Reason = reason ?? string.Empty;
            if (missingInputs != null)
            {
                MissingInputs = new List<string>(missingInputs).AsReadOnly();
            }
            else
            {
                MissingInputs = new List<string>().AsReadOnly();
            }

            PersonId = personId;
            AgeYears = ageYears;
            LaborCapacityPercent = laborCapacityPercent;
            Nutrition01 = nutrition01;
        }
    }

    /// <summary>The four answers a capacity query can return.</summary>
    public enum PhysicalCapacityOutcome
    {
        /// <summary>The authority accepts the load.</summary>
        Fits = 0,
        /// <summary>The authority refuses the load (over a limit it enforces).</summary>
        DoesNotFit = 1,
        /// <summary>
        /// No number can be returned: a needed mass, volume, limit or input is
        /// Unknown, or no authority is connected. A refusal, not a number.
        /// </summary>
        Unknown = 2,
        /// <summary>No authority exists for this container kind at all.</summary>
        NoAuthority = 3,
    }

    /// <summary>
    /// The Canon XXXI section 7.1A capacity axes for one business's storage,
    /// as the underlying authority supports them. Total and Occupied come from
    /// <see cref="BusinessCapacityState"/> (sums of category targets and of
    /// category current stock). Allocated is Unknown: no business-level
    /// allocated-storage figure exists in source. IdentifiedLotUnits is the
    /// identified-lot view for the load's items only, reported separately
    /// from Occupied — the two views are never summed (P9 risk C-1).
    /// A null axis is Unknown, never zero.
    /// </summary>
    public struct BusinessStorageCapacityAxes
    {
        public int? TotalUnits { get; }
        public int? AllocatedUnits { get; }
        public int? OccupiedUnits { get; }
        public int? IdentifiedLotUnits { get; }

        public BusinessStorageCapacityAxes(int? totalUnits, int? allocatedUnits, int? occupiedUnits, int? identifiedLotUnits)
        {
            TotalUnits = totalUnits;
            AllocatedUnits = allocatedUnits;
            OccupiedUnits = occupiedUnits;
            IdentifiedLotUnits = identifiedLotUnits;
        }

        public static BusinessStorageCapacityAxes AllUnknown =>
            new BusinessStorageCapacityAxes(null, null, null, null);
    }

    /// <summary>
    /// One capacity verdict: the underlying authority's answer, echoed. The
    /// evaluator never renders a verdict of its own; <see cref="Reason"/>
    /// names the authority and, for Unknown, the missing item or input.
    /// <see cref="StorageAxes"/> is populated only for BusinessStorage
    /// queries; every other leg leaves it all-Unknown.
    /// </summary>
    public struct PhysicalCapacityVerdict
    {
        public PhysicalCapacityOutcome Outcome { get; }
        public string Reason { get; }
        public PhysicalContainerKind ContainerKind { get; }
        public PhysicalLoadResolution Load { get; }
        public BusinessStorageCapacityAxes StorageAxes { get; }

        public bool Fits => Outcome == PhysicalCapacityOutcome.Fits;

        public PhysicalCapacityVerdict(
            PhysicalCapacityOutcome outcome,
            PhysicalContainerKind containerKind,
            PhysicalLoadResolution load,
            string reason,
            BusinessStorageCapacityAxes storageAxes)
        {
            Outcome = outcome;
            ContainerKind = containerKind;
            Load = load;
            Reason = reason ?? string.Empty;
            StorageAxes = storageAxes;
        }

        public PhysicalCapacityVerdict(
            PhysicalCapacityOutcome outcome,
            PhysicalContainerKind containerKind,
            PhysicalLoadResolution load,
            string reason)
            : this(outcome, containerKind, load, reason, BusinessStorageCapacityAxes.AllUnknown)
        {
        }

        public static PhysicalCapacityVerdict Accepted(
            PhysicalContainerKind kind, PhysicalLoadResolution load, string reason) =>
            new PhysicalCapacityVerdict(PhysicalCapacityOutcome.Fits, kind, load, reason);

        public static PhysicalCapacityVerdict Refused(
            PhysicalContainerKind kind, PhysicalLoadResolution load, string reason) =>
            new PhysicalCapacityVerdict(PhysicalCapacityOutcome.DoesNotFit, kind, load, reason);

        public static PhysicalCapacityVerdict Unknown(
            PhysicalContainerKind kind, PhysicalLoadResolution load, string reason) =>
            new PhysicalCapacityVerdict(PhysicalCapacityOutcome.Unknown, kind, load, reason);

        public static PhysicalCapacityVerdict NoAuthority(
            PhysicalContainerKind kind, PhysicalLoadResolution load, string reason) =>
            new PhysicalCapacityVerdict(PhysicalCapacityOutcome.NoAuthority, kind, load, reason);
    }

    /// <summary>
    /// Per-evaluation inputs the query shape does not carry: the equipment
    /// condition and game time the equipment service needs, the registered
    /// equipment descriptor (the service exposes no descriptor accessor, so
    /// the caller supplies the descriptor it registered — the evaluator
    /// stores nothing), the business category a BusinessStorage load targets,
    /// the lot identity a StorageZone refusal names, and the person-carry
    /// inputs for an OnFootPerson query. All fields optional; unset means
    /// the documented default (condition 1.0, time 0, no descriptor, no
    /// category, no lot identity, no carry inputs, no on-foot carry cap).
    /// </summary>
    public struct PhysicalCapacityEvaluationContext
    {
        /// <summary>Equipment condition 0..1; null means 1.0 (full condition).</summary>
        public float? EquipmentCondition01 { get; set; }
        public float AtGameSeconds { get; set; }
        public EquipmentCapacityDescriptor EquipmentDescriptor { get; set; }
        public string BusinessCategoryId { get; set; }
        public string ZoneLotAuthorityId { get; set; }
        public string ZoneLotId { get; set; }
        public PersonCarryInputs? CarryInputs { get; set; }

        /// <summary>
        /// Priority V1: the live on-foot carry cap in units, supplied by
        /// the caller that owns the cap (the shopping loop supplies
        /// ShoppingLoopOptions.CarryCapacityUnits). Null (the default)
        /// means no cap was supplied and the OnFootPerson leg keeps its
        /// Slice 2 Unknown answer. This is the caller's own live rule,
        /// passed in — the evaluator still authors no person limit of
        /// its own, and mass/volume stay Unknown either way.
        /// </summary>
        public int? OnFootCarryCapUnits { get; set; }

        /// <summary>
        /// P2a: the caller-supplied worn-container capacity for a WornSatchel
        /// query. Null (the default) means no capacity was supplied and the
        /// leg returns Unknown naming the missing container capacity. The
        /// evaluator still authors no container measurement of its own.
        /// </summary>
        public WornContainerCapacity? ContainerCapacity { get; set; }

        public static PhysicalCapacityEvaluationContext Default => new PhysicalCapacityEvaluationContext();
    }

    /// <summary>
    /// The shared capacity evaluator (P9 plan section 2.4). DELEGATES ONLY:
    /// constructed with the real authorities and returns their verdicts, never
    /// its own. It stores no capacity value of its own — only the authority
    /// references. Priority V1: the shopping loop consults it at settle time.
    ///
    /// Legs:
    /// - Equipment: <see cref="IEquipmentCapacityService.CanAccept"/>, which
    ///   routes Transport models to its internal CanAcceptTransport leg. The
    ///   governing scalar mirrors the service's own limit preference (count
    ///   before mass for Container; mass for Transport); volume and footprint
    ///   are never enforced by CanAccept, so Unknown volume never blocks an
    ///   equipment answer.
    /// - BusinessStorage: category target via
    ///   <see cref="BusinessRuntimeState.CanReceiveShipment"/> plus the 7.1A
    ///   axes from <see cref="BusinessCapacityState"/>.
    /// - StorageZone: <see cref="PhysicalStorageZone"/> CanFit semantics
    ///   (CanFit itself is private, so the check is re-implemented against
    ///   the zone's public definition and placements with the same per-axis
    ///   refusals) — and it places nothing.
    /// - Wagon: <see cref="WagonAuthority.CanCarry"/> — haul capacity only,
    ///   never warehouse accumulation (Technical XIV section 6.2).
    /// - OnFootPerson: Unknown when no carry cap is supplied — no authored
    ///   limit exists; the live shopping-loop carry cap is untouched and is
    ///   not consulted here. Priority V1: when the caller supplies its live
    ///   carry cap (PhysicalCapacityEvaluationContext.OnFootCarryCapUnits),
    ///   the leg answers on the units axis only against that cap.
    /// - HorsePack: "no authority" — no horse-pack capacity exists in source.
    /// - WornSatchel (P2a): the caller-supplied container capacity in the
    ///   evaluation context, checked per axis (units, mass, volume,
    ///   dimensions) with Unknown refusing, naming the missing axis.
    /// - HandheldPail (P2b): the same caller-supplied container capacity
    ///   check as WornSatchel, with the kind and wording changed to the
    ///   handheld pail and every other rule identical.
    /// - Capability consult (P3), see EvaluateCapability, answers one
    ///   caller-supplied demand per operation and adds no leg to the
    ///   Evaluate switch above.
    ///
    /// Priority V1: the shopping loop consults this evaluator at settle
    /// time (HouseholdShoppingLoop, before InHand custody is assigned)
    /// with its live carry cap supplied, and the meal-adjacent flows
    /// resolve lot identities through LotIdentityResolver. The evaluator
    /// itself still delegates only and authors no limit of its own.
    /// </summary>
    public sealed class PhysicalCapacityEvaluator
    {
        private readonly IEquipmentCapacityService equipmentCapacity;
        private readonly Func<string, BusinessRuntimeState> businessById;
        private readonly Func<string, PhysicalStorageZone> zoneById;
        private readonly Func<string, EquipmentAsset> wagonById;

        public PhysicalCapacityEvaluator(
            IEquipmentCapacityService equipmentCapacity,
            Func<string, BusinessRuntimeState> businessById,
            Func<string, PhysicalStorageZone> zoneById,
            Func<string, EquipmentAsset> wagonById)
        {
            this.equipmentCapacity = equipmentCapacity;
            this.businessById = businessById;
            this.zoneById = zoneById;
            this.wagonById = wagonById;
        }

        /// <summary>Evaluates one query by dispatching to its container's leg.</summary>
        public PhysicalCapacityVerdict Evaluate(PhysicalCapacityQuery query, PhysicalCapacityEvaluationContext context)
        {
            PhysicalLoadResolution resolved = query.Load.Resolve();
            switch (query.ContainerKind)
            {
                case PhysicalContainerKind.Equipment:
                    return EvaluateEquipment(query, context, resolved);
                case PhysicalContainerKind.BusinessStorage:
                    return EvaluateBusinessStorage(query, context, resolved);
                case PhysicalContainerKind.StorageZone:
                    return EvaluateStorageZone(query, context, resolved);
                case PhysicalContainerKind.Wagon:
                    return EvaluateWagon(query, context, resolved);
                case PhysicalContainerKind.OnFootPerson:
                    return EvaluateOnFootPerson(query, context, resolved);
                case PhysicalContainerKind.WornSatchel:
                    return EvaluateWornSatchel(query, context, resolved);
                case PhysicalContainerKind.HandheldPail:
                    return EvaluateHandheldPail(query, context, resolved);
                case PhysicalContainerKind.HorsePack:
                default:
                    return PhysicalCapacityVerdict.NoAuthority(
                        query.ContainerKind, resolved,
                        "HorsePack has no authority: no horse-pack capacity exists in source.");
            }
        }

        /// <summary>
        /// P3 capability consult. One consult for one operation, with
        /// the demand supplied by the caller for that operation. Axes
        /// are checked in one fixed order and the first failure is
        /// returned. Duration demanded refuses Unknown, because no
        /// fatigue accumulation rule exists. Size and handling demanded
        /// refuses Unknown, because no size class or handling rule
        /// exists. A demanded lift or carry minimum with Unknown
        /// strength refuses Unknown naming strength. A demanded body
        /// size minimum with Unknown body size refuses Unknown naming
        /// body size. A demanded fatigue maximum with Unknown fatigue
        /// refuses Unknown naming fatigue. A demanded equipment kind
        /// the person does not hold refuses DoesNotFit naming the kind.
        /// Known strength below a demanded minimum, Known body size
        /// below a demanded minimum, and Known fatigue above a demanded
        /// maximum each refuse DoesNotFit naming the demand that
        /// failed. Otherwise the consult returns Fits. Holding the
        /// demanded equipment kind never changes a number. Age, labor
        /// capacity, and nutrition gate nothing and are echoed on the
        /// verdict only. The consult reads no demographic field and no
        /// hidden trait. It adds no field to the evaluator.
        /// </summary>
        public PhysicalCapabilityVerdict EvaluateCapability(
            PersonCarryInputs inputs, PhysicalCapabilityDemand demand)
        {
            var missing = new List<string>();
            if (demand.CarryDurationMinutes > 0)
            {
                missing.Add("carry duration");
            }

            if (demand.NeedsSizeAndHandlingVerdict)
            {
                missing.Add("object size and handling");
            }

            if (demand.MinimumBodySize.IsKnown && !inputs.BodySize.IsKnown)
            {
                missing.Add("body size");
            }

            bool strengthDemanded = demand.MinimumLiftStrength.IsKnown || demand.MinimumCarryStrength.IsKnown;
            if (strengthDemanded && !inputs.Strength.IsKnown)
            {
                missing.Add("strength");
            }

            if (demand.MaximumFatigue.IsKnown && !inputs.Fatigue.IsKnown)
            {
                missing.Add("fatigue");
            }

            string demanded = DemandedCapabilityDescription(demand);
            string person = "Person " + inputs.PersonId + " capability";

            if (missing.Count > 0)
            {
                return new PhysicalCapabilityVerdict(
                    PhysicalCapacityOutcome.Unknown,
                    person + " is unknown - missing " + string.Join(", ", missing) + " for the demanded " + demanded + ".",
                    missing,
                    inputs.PersonId,
                    inputs.AgeYears,
                    inputs.LaborCapacityPercent,
                    inputs.Nutrition01);
            }

            if (!string.IsNullOrWhiteSpace(demand.RequiredEquipmentKind) && !EquipmentHeldContains(inputs, demand.RequiredEquipmentKind))
            {
                return new PhysicalCapabilityVerdict(
                    PhysicalCapacityOutcome.DoesNotFit,
                    person + " does not fit - required equipment " + demand.RequiredEquipmentKind + " is not held.",
                    missing,
                    inputs.PersonId,
                    inputs.AgeYears,
                    inputs.LaborCapacityPercent,
                    inputs.Nutrition01);
            }

            if (demand.MinimumLiftStrength.IsKnown && inputs.Strength.Value < demand.MinimumLiftStrength.Value)
            {
                return new PhysicalCapabilityVerdict(
                    PhysicalCapacityOutcome.DoesNotFit,
                    person + " does not fit - strength " + inputs.Strength.Value + " is below the demanded lift minimum " + demand.MinimumLiftStrength.Value + ".",
                    missing,
                    inputs.PersonId,
                    inputs.AgeYears,
                    inputs.LaborCapacityPercent,
                    inputs.Nutrition01);
            }

            if (demand.MinimumCarryStrength.IsKnown && inputs.Strength.Value < demand.MinimumCarryStrength.Value)
            {
                return new PhysicalCapabilityVerdict(
                    PhysicalCapacityOutcome.DoesNotFit,
                    person + " does not fit - strength " + inputs.Strength.Value + " is below the demanded carry minimum " + demand.MinimumCarryStrength.Value + ".",
                    missing,
                    inputs.PersonId,
                    inputs.AgeYears,
                    inputs.LaborCapacityPercent,
                    inputs.Nutrition01);
            }

            if (demand.MinimumBodySize.IsKnown && inputs.BodySize.Value < demand.MinimumBodySize.Value)
            {
                return new PhysicalCapabilityVerdict(
                    PhysicalCapacityOutcome.DoesNotFit,
                    person + " does not fit - body size " + inputs.BodySize.Value + " is below the demanded body size minimum " + demand.MinimumBodySize.Value + ".",
                    missing,
                    inputs.PersonId,
                    inputs.AgeYears,
                    inputs.LaborCapacityPercent,
                    inputs.Nutrition01);
            }

            if (demand.MaximumFatigue.IsKnown && inputs.Fatigue.Value > demand.MaximumFatigue.Value)
            {
                return new PhysicalCapabilityVerdict(
                    PhysicalCapacityOutcome.DoesNotFit,
                    person + " does not fit - fatigue " + inputs.Fatigue.Value + " is above the demanded fatigue maximum " + demand.MaximumFatigue.Value + ".",
                    missing,
                    inputs.PersonId,
                    inputs.AgeYears,
                    inputs.LaborCapacityPercent,
                    inputs.Nutrition01);
            }

            string fitsReason;
            if (demanded == "nothing")
            {
                fitsReason = person + " fits - no capability demand was made.";
            }
            else
            {
                fitsReason = person + " fits - demanded " + demanded + ".";
            }

            return new PhysicalCapabilityVerdict(
                PhysicalCapacityOutcome.Fits,
                fitsReason,
                missing,
                inputs.PersonId,
                inputs.AgeYears,
                inputs.LaborCapacityPercent,
                inputs.Nutrition01);
        }

        private static bool EquipmentHeldContains(PersonCarryInputs inputs, string kind)
        {
            if (inputs.EquipmentHeld == null) return false;
            foreach (string held in inputs.EquipmentHeld)
            {
                if (string.Equals(held, kind, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static string DemandedCapabilityDescription(PhysicalCapabilityDemand demand)
        {
            var demanded = new List<string>();
            if (demand.MinimumLiftStrength.IsKnown)
            {
                demanded.Add("lift strength");
            }

            if (demand.MinimumCarryStrength.IsKnown)
            {
                demanded.Add("carry strength");
            }

            if (demand.MinimumBodySize.IsKnown)
            {
                demanded.Add("body size");
            }

            if (demand.MaximumFatigue.IsKnown)
            {
                demanded.Add("fatigue");
            }

            if (demand.CarryDurationMinutes > 0)
            {
                demanded.Add("carry duration");
            }

            if (demand.NeedsSizeAndHandlingVerdict)
            {
                demanded.Add("object size and handling");
            }

            if (!string.IsNullOrWhiteSpace(demand.RequiredEquipmentKind))
            {
                demanded.Add("equipment " + demand.RequiredEquipmentKind);
            }

            if (demanded.Count == 0) return "nothing";
            return string.Join(", ", demanded);
        }

        /// <summary>
        /// Equipment leg: delegates to
        /// <see cref="IEquipmentCapacityService.CanAccept"/> (Transport models
        /// reach the service's internal CanAcceptTransport leg through it).
        /// The governing scalar follows the service's own limit preference:
        /// Container enforces item count when a count limit exists, otherwise
        /// mass when a mass limit exists; Transport enforces mass; every other
        /// model enforces the requested quantity in units. Unknown is returned
        /// (naming the item) only when the governing axis needs a mass the
        /// registry does not have. Without a descriptor the evaluator passes
        /// item count, matching the service's unregistered-key path.
        /// </summary>
        public PhysicalCapacityVerdict EvaluateEquipment(
            PhysicalCapacityQuery query, PhysicalCapacityEvaluationContext context, PhysicalLoadResolution resolved)
        {
            const PhysicalContainerKind kind = PhysicalContainerKind.Equipment;
            if (equipmentCapacity == null)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "No equipment capacity authority is connected.");
            }
            if (string.IsNullOrWhiteSpace(query.ContainerKey))
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Equipment query needs a non-empty equipment key.");
            }

            EquipmentCapacityDescriptor descriptor = context.EquipmentDescriptor;
            float requested;
            string governingAxis;
            if (descriptor != null && descriptor.Model == EquipmentCapacityModel.Transport
                && descriptor.Envelope.MaxMassKg > 0f)
            {
                if (!resolved.IsMassKnown)
                {
                    return PhysicalCapacityVerdict.Unknown(kind, resolved,
                        $"Cannot verify equipment mass capacity: item '{resolved.FirstUnknownMassItemId}' mass is unknown.");
                }
                requested = resolved.TotalMassKg.Value;
                governingAxis = "mass kg";
            }
            else if (descriptor != null && descriptor.Model == EquipmentCapacityModel.Container
                && descriptor.Envelope.MaxItemCount <= 0 && descriptor.Envelope.MaxMassKg > 0f)
            {
                if (!resolved.IsMassKnown)
                {
                    return PhysicalCapacityVerdict.Unknown(kind, resolved,
                        $"Cannot verify equipment mass capacity: item '{resolved.FirstUnknownMassItemId}' mass is unknown.");
                }
                requested = resolved.TotalMassKg.Value;
                governingAxis = "mass kg";
            }
            else
            {
                requested = resolved.TotalUnits;
                governingAxis = "units";
            }

            float condition01 = context.EquipmentCondition01 ?? 1f;
            bool accepted = equipmentCapacity.CanAccept(
                query.ContainerKey, condition01, requested, context.AtGameSeconds);
            return accepted
                ? PhysicalCapacityVerdict.Accepted(kind, resolved,
                    $"Equipment '{query.ContainerKey}' accepts the load per EquipmentCapacityService.CanAccept (requested {requested} {governingAxis}).")
                : PhysicalCapacityVerdict.Refused(kind, resolved,
                    $"Equipment '{query.ContainerKey}' refuses the load per EquipmentCapacityService.CanAccept (requested {requested} {governingAxis}).");
        }

        /// <summary>
        /// BusinessStorage leg: delegates the verdict to
        /// <see cref="BusinessRuntimeState.CanReceiveShipment"/> — the
        /// read-only category-target check — and exposes the Canon XXXI
        /// section 7.1A axes from <see cref="BusinessCapacityState"/>: Total
        /// is StorageCapacityUnits, Occupied is CurrentStoredUnits, Allocated
        /// is Unknown (no business-level allocated-storage figure exists).
        /// IdentifiedLotUnits is the identified-lot view for the load's items
        /// only, reported separately from Occupied and never summed with it
        /// (P9 risk C-1). Note the authority's own boundary:
        /// CanReceiveShipment refuses a category that is already full; it does
        /// not refuse a load larger than the remaining headroom. The evaluator
        /// mirrors that verdict exactly and does not tighten it.
        /// </summary>
        public PhysicalCapacityVerdict EvaluateBusinessStorage(
            PhysicalCapacityQuery query, PhysicalCapacityEvaluationContext context, PhysicalLoadResolution resolved)
        {
            const PhysicalContainerKind kind = PhysicalContainerKind.BusinessStorage;
            if (businessById == null)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "No business storage authority is connected.");
            }
            BusinessRuntimeState state = businessById(query.ContainerKey);
            if (state == null)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    $"No business runtime found for '{query.ContainerKey}'.");
            }
            if (string.IsNullOrWhiteSpace(context.BusinessCategoryId))
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Business storage query needs a category id.");
            }

            var axes = new BusinessStorageCapacityAxes(
                state.Capacity.StorageCapacityUnits,
                null,
                state.Capacity.CurrentStoredUnits,
                IdentifiedLotUnitsForLoad(state, query.Load));
            bool canReceive = state.CanReceiveShipment(
                context.BusinessCategoryId, resolved.TotalUnits, out string blockedReason);
            return canReceive
                ? new PhysicalCapacityVerdict(PhysicalCapacityOutcome.Fits, kind, resolved,
                    $"Business '{query.ContainerKey}' category '{context.BusinessCategoryId}' accepts the load per BusinessRuntimeState.CanReceiveShipment.",
                    axes)
                : new PhysicalCapacityVerdict(PhysicalCapacityOutcome.DoesNotFit, kind, resolved,
                    blockedReason, axes);
        }

        /// <summary>
        /// StorageZone leg: <see cref="PhysicalStorageZone"/> CanFit semantics
        /// (CanFit is private, so the per-axis check is applied here against
        /// the zone's public definition and placements with the same refusal
        /// conditions). It places nothing: the zone's placements are read,
        /// never written. Unknown mass or volume on an axis the zone bounds
        /// refuses with the item named; an axis the zone leaves unbounded
        /// never blocks.
        /// </summary>
        public PhysicalCapacityVerdict EvaluateStorageZone(
            PhysicalCapacityQuery query, PhysicalCapacityEvaluationContext context, PhysicalLoadResolution resolved)
        {
            const PhysicalContainerKind kind = PhysicalContainerKind.StorageZone;
            if (zoneById == null)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "No storage zone authority is connected.");
            }
            PhysicalStorageZone zone = zoneById(query.ContainerKey);
            if (zone == null || zone.Definition == null)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    $"No storage zone found for '{query.ContainerKey}'.");
            }

            StorageCapacityLimit massLimit = zone.Definition.MassCapacityKg;
            StorageCapacityLimit volumeLimit = zone.Definition.VolumeCapacityCubicMeters;

            if (massLimit.IsKnown)
            {
                float used = 0f;
                foreach (StoredLotReference placed in zone.Placements)
                {
                    if (placed == null || !placed.Load.HasMass)
                    {
                        return PhysicalCapacityVerdict.Refused(kind, resolved,
                            "Cannot verify mass capacity: an existing placed lot has unknown mass.");
                    }
                    used += placed.Load.MassKg;
                }
                if (!resolved.IsMassKnown)
                {
                    return PhysicalCapacityVerdict.Unknown(kind, resolved,
                        $"Cannot verify mass capacity: item '{resolved.FirstUnknownMassItemId}' mass is unknown.");
                }
                if (used + resolved.TotalMassKg.Value > massLimit.Value + 0.0001f)
                {
                    return PhysicalCapacityVerdict.Refused(kind, resolved,
                        $"Mass capacity exceeded: {used + resolved.TotalMassKg.Value} kg requested, {massLimit.Value} kg available.");
                }
            }

            if (volumeLimit.IsKnown)
            {
                float used = 0f;
                foreach (StoredLotReference placed in zone.Placements)
                {
                    if (placed == null || !placed.Load.HasVolume)
                    {
                        return PhysicalCapacityVerdict.Refused(kind, resolved,
                            "Cannot verify volume capacity: an existing placed lot has unknown volume.");
                    }
                    used += placed.Load.VolumeCubicMeters;
                }
                if (!resolved.IsVolumeKnown)
                {
                    return PhysicalCapacityVerdict.Unknown(kind, resolved,
                        $"Cannot verify volume capacity: item '{resolved.FirstUnknownVolumeItemId}' volume is unknown.");
                }
                if (used + resolved.TotalVolumeCubicMeters.Value > volumeLimit.Value + 0.0001f)
                {
                    return PhysicalCapacityVerdict.Refused(kind, resolved,
                        $"Volume capacity exceeded: {used + resolved.TotalVolumeCubicMeters.Value} m3 requested, {volumeLimit.Value} m3 available.");
                }
            }

            return PhysicalCapacityVerdict.Accepted(kind, resolved,
                $"Storage zone '{query.ContainerKey}' accepts the load per PhysicalStorageZone.CanFit semantics (nothing placed).");
        }

        /// <summary>
        /// Wagon leg: delegates to <see cref="WagonAuthority.CanCarry"/> on
        /// the wagon asset. Both mass and volume must be Known — the wagon
        /// bounds both — so an Unknown item refuses naming the item. The
        /// verdict is HAUL capacity only (Technical XIV section 6.2): a wagon
        /// may haul freight but may not silently accumulate warehouse
        /// inventory, so this leg never answers a storage question.
        /// </summary>
        public PhysicalCapacityVerdict EvaluateWagon(
            PhysicalCapacityQuery query, PhysicalCapacityEvaluationContext context, PhysicalLoadResolution resolved)
        {
            const PhysicalContainerKind kind = PhysicalContainerKind.Wagon;
            if (wagonById == null)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "No wagon authority is connected.");
            }
            EquipmentAsset wagon = wagonById(query.ContainerKey);
            if (wagon == null)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    $"No wagon asset found for '{query.ContainerKey}'.");
            }
            if (!resolved.IsMassKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    $"Cannot verify wagon mass capacity: item '{resolved.FirstUnknownMassItemId}' mass is unknown.");
            }
            if (!resolved.IsVolumeKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    $"Cannot verify wagon volume capacity: item '{resolved.FirstUnknownVolumeItemId}' volume is unknown.");
            }
            if (!wagon.IsWagon || !wagon.IsUsable)
            {
                return PhysicalCapacityVerdict.Refused(kind, resolved,
                    $"Wagon '{query.ContainerKey}' is not a usable wagon (WagonAuthority.CanCarry).");
            }
            bool canCarry = WagonAuthority.CanCarry(
                wagon, resolved.TotalMassKg.Value, resolved.TotalVolumeCubicMeters.Value);
            return canCarry
                ? PhysicalCapacityVerdict.Accepted(kind, resolved,
                    $"Wagon '{query.ContainerKey}' can haul the load per WagonAuthority.CanCarry " +
                    $"({resolved.TotalMassKg.Value} kg of {wagon.PayloadMassCapacityKg} kg, " +
                    $"{resolved.TotalVolumeCubicMeters.Value} m3 of {wagon.CargoVolumeCapacityM3} m3). " +
                    "Haul capacity only: a wagon may haul freight but may not silently accumulate warehouse inventory.")
                : PhysicalCapacityVerdict.Refused(kind, resolved,
                    $"Wagon '{query.ContainerKey}' cannot haul the load per WagonAuthority.CanCarry " +
                    $"({resolved.TotalMassKg.Value} kg of {wagon.PayloadMassCapacityKg} kg, " +
                    $"{resolved.TotalVolumeCubicMeters.Value} m3 of {wagon.CargoVolumeCapacityM3} m3).");
        }

        /// <summary>
        /// Worn satchel leg (P2a): the caller-supplied capacity in the
        /// evaluation context is the whole authority — the leg reads
        /// capacity only from there and needs none of the four authorities
        /// the evaluator constructor takes. Axes are checked in order
        /// (units, mass, volume, dimensions) and the first failure is
        /// returned, mirroring PhysicalStorageZone CanFit. Unknown on any
        /// axis refuses, naming the missing axis or the first unknown
        /// item; dimensions are checked per item against the interior box,
        /// never summed across contents. No capacity supplied in the
        /// context returns Unknown naming the missing container capacity.
        /// </summary>
        public PhysicalCapacityVerdict EvaluateWornSatchel(
            PhysicalCapacityQuery query, PhysicalCapacityEvaluationContext context, PhysicalLoadResolution resolved)
        {
            const PhysicalContainerKind kind = PhysicalContainerKind.WornSatchel;
            if (!context.ContainerCapacity.HasValue)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Worn satchel '" + query.ContainerKey + "' has no container capacity: no capacity was supplied in the evaluation context.");
            }

            WornContainerCapacity capacity = context.ContainerCapacity.Value;

            if (!capacity.MaxUnits.HasValue)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Worn satchel '" + query.ContainerKey + "' units capacity is unknown: no units capacity was supplied for the satchel.");
            }

            if (resolved.TotalUnits > capacity.MaxUnits.Value)
            {
                return PhysicalCapacityVerdict.Refused(kind, resolved,
                    $"Worn satchel '{query.ContainerKey}' refuses the load: {resolved.TotalUnits} units requested, {capacity.MaxUnits.Value} units allowed (units capacity).");
            }

            if (!capacity.MaxMassKg.IsKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Worn satchel '" + query.ContainerKey + "' mass capacity is unknown: no mass capacity was supplied for the satchel.");
            }

            if (!resolved.IsMassKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    $"Cannot verify worn satchel mass capacity: item '{resolved.FirstUnknownMassItemId}' mass is unknown.");
            }

            if (resolved.TotalMassKg.Value > capacity.MaxMassKg.Value + 0.0001f)
            {
                return PhysicalCapacityVerdict.Refused(kind, resolved,
                    $"Worn satchel '{query.ContainerKey}' refuses the load: {resolved.TotalMassKg.Value} kg requested, {capacity.MaxMassKg.Value} kg allowed (mass capacity).");
            }

            if (!capacity.MaxVolumeLiters.IsKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Worn satchel '" + query.ContainerKey + "' volume capacity is unknown: no volume capacity was supplied for the satchel.");
            }

            if (!resolved.IsVolumeKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    $"Cannot verify worn satchel volume capacity: item '{resolved.FirstUnknownVolumeItemId}' volume is unknown.");
            }

            if (resolved.TotalVolumeLiters.Value > capacity.MaxVolumeLiters.Value + 0.0001f)
            {
                return PhysicalCapacityVerdict.Refused(kind, resolved,
                    $"Worn satchel '{query.ContainerKey}' refuses the load: {resolved.TotalVolumeLiters.Value} liters requested, {capacity.MaxVolumeLiters.Value} liters allowed (volume capacity).");
            }

            if (!capacity.InteriorDimensions.IsKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Worn satchel '" + query.ContainerKey + "' dimensions are unknown: no interior dimensions were supplied for the satchel.");
            }

            PhysicalDimensions interior = capacity.InteriorDimensions;
            if (query.Load.Lines != null)
            {
                foreach ((string ItemId, int Units) line in query.Load.Lines)
                {
                    if (line.Units <= 0) continue;
                    if (!PhysicalItemSpecRegistry.TryGet(line.ItemId, out PhysicalItemSpec spec)
                        || !spec.DimensionsMeters.IsKnown)
                    {
                        return PhysicalCapacityVerdict.Unknown(kind, resolved,
                            $"Cannot verify worn satchel dimensions: item '{line.ItemId}' dimensions are unknown.");
                    }

                    PhysicalDimensions item = spec.DimensionsMeters;
                    if (item.Length.Value > interior.Length.Value + 0.0001f)
                    {
                        return PhysicalCapacityVerdict.Refused(kind, resolved,
                            $"Worn satchel '{query.ContainerKey}' refuses the load: item '{line.ItemId}' is {item.Length.Value} m long, longer than the {interior.Length.Value} m interior length (dimensions capacity).");
                    }

                    if (item.Width.Value > interior.Width.Value + 0.0001f)
                    {
                        return PhysicalCapacityVerdict.Refused(kind, resolved,
                            $"Worn satchel '{query.ContainerKey}' refuses the load: item '{line.ItemId}' is {item.Width.Value} m wide, wider than the {interior.Width.Value} m interior width (dimensions capacity).");
                    }

                    if (item.Height.Value > interior.Height.Value + 0.0001f)
                    {
                        return PhysicalCapacityVerdict.Refused(kind, resolved,
                            $"Worn satchel '{query.ContainerKey}' refuses the load: item '{line.ItemId}' is {item.Height.Value} m tall, taller than the {interior.Height.Value} m interior height (dimensions capacity).");
                    }
                }
            }

            return PhysicalCapacityVerdict.Accepted(kind, resolved,
                $"Worn satchel '{query.ContainerKey}' accepts the load ({resolved.TotalUnits} units, {resolved.TotalMassKg.Value} kg, {resolved.TotalVolumeLiters.Value} liters) within its caller-supplied capacity.");
        }

        /// <summary>
        /// Handheld pail leg (P2b): mirrors EvaluateWornSatchel with the
        /// kind and the wording changed to the handheld pail and every
        /// other rule identical. The caller-supplied capacity in the
        /// evaluation context is the whole authority — the leg reads
        /// capacity only from there and needs none of the four authorities
        /// the evaluator constructor takes. Axes are checked in order
        /// (units, mass, volume, dimensions) and the first failure is
        /// returned, mirroring PhysicalStorageZone CanFit. Unknown on any
        /// axis refuses, naming the missing axis or the first unknown
        /// item; dimensions are checked per item against the interior box,
        /// never summed across contents. No capacity supplied in the
        /// context returns Unknown naming the missing container capacity.
        /// </summary>
        public PhysicalCapacityVerdict EvaluateHandheldPail(
            PhysicalCapacityQuery query, PhysicalCapacityEvaluationContext context, PhysicalLoadResolution resolved)
        {
            const PhysicalContainerKind kind = PhysicalContainerKind.HandheldPail;
            if (!context.ContainerCapacity.HasValue)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Handheld pail '" + query.ContainerKey + "' has no container capacity: no capacity was supplied in the evaluation context.");
            }

            WornContainerCapacity capacity = context.ContainerCapacity.Value;

            if (!capacity.MaxUnits.HasValue)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Handheld pail '" + query.ContainerKey + "' units capacity is unknown: no units capacity was supplied for the pail.");
            }

            if (resolved.TotalUnits > capacity.MaxUnits.Value)
            {
                return PhysicalCapacityVerdict.Refused(kind, resolved,
                    $"Handheld pail '{query.ContainerKey}' refuses the load: {resolved.TotalUnits} units requested, {capacity.MaxUnits.Value} units allowed (units capacity).");
            }

            if (!capacity.MaxMassKg.IsKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Handheld pail '" + query.ContainerKey + "' mass capacity is unknown: no mass capacity was supplied for the pail.");
            }

            if (!resolved.IsMassKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    $"Cannot verify handheld pail mass capacity: item '{resolved.FirstUnknownMassItemId}' mass is unknown.");
            }

            if (resolved.TotalMassKg.Value > capacity.MaxMassKg.Value + 0.0001f)
            {
                return PhysicalCapacityVerdict.Refused(kind, resolved,
                    $"Handheld pail '{query.ContainerKey}' refuses the load: {resolved.TotalMassKg.Value} kg requested, {capacity.MaxMassKg.Value} kg allowed (mass capacity).");
            }

            if (!capacity.MaxVolumeLiters.IsKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Handheld pail '" + query.ContainerKey + "' volume capacity is unknown: no volume capacity was supplied for the pail.");
            }

            if (!resolved.IsVolumeKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    $"Cannot verify handheld pail volume capacity: item '{resolved.FirstUnknownVolumeItemId}' volume is unknown.");
            }

            if (resolved.TotalVolumeLiters.Value > capacity.MaxVolumeLiters.Value + 0.0001f)
            {
                return PhysicalCapacityVerdict.Refused(kind, resolved,
                    $"Handheld pail '{query.ContainerKey}' refuses the load: {resolved.TotalVolumeLiters.Value} liters requested, {capacity.MaxVolumeLiters.Value} liters allowed (volume capacity).");
            }

            if (!capacity.InteriorDimensions.IsKnown)
            {
                return PhysicalCapacityVerdict.Unknown(kind, resolved,
                    "Handheld pail '" + query.ContainerKey + "' dimensions are unknown: no interior dimensions were supplied for the pail.");
            }

            PhysicalDimensions interior = capacity.InteriorDimensions;
            if (query.Load.Lines != null)
            {
                foreach ((string ItemId, int Units) line in query.Load.Lines)
                {
                    if (line.Units <= 0) continue;
                    if (!PhysicalItemSpecRegistry.TryGet(line.ItemId, out PhysicalItemSpec spec)
                        || !spec.DimensionsMeters.IsKnown)
                    {
                        return PhysicalCapacityVerdict.Unknown(kind, resolved,
                            $"Cannot verify handheld pail dimensions: item '{line.ItemId}' dimensions are unknown.");
                    }

                    PhysicalDimensions item = spec.DimensionsMeters;
                    if (item.Length.Value > interior.Length.Value + 0.0001f)
                    {
                        return PhysicalCapacityVerdict.Refused(kind, resolved,
                            $"Handheld pail '{query.ContainerKey}' refuses the load: item '{line.ItemId}' is {item.Length.Value} m long, longer than the {interior.Length.Value} m interior length (dimensions capacity).");
                    }

                    if (item.Width.Value > interior.Width.Value + 0.0001f)
                    {
                        return PhysicalCapacityVerdict.Refused(kind, resolved,
                            $"Handheld pail '{query.ContainerKey}' refuses the load: item '{line.ItemId}' is {item.Width.Value} m wide, wider than the {interior.Width.Value} m interior width (dimensions capacity).");
                    }

                    if (item.Height.Value > interior.Height.Value + 0.0001f)
                    {
                        return PhysicalCapacityVerdict.Refused(kind, resolved,
                            $"Handheld pail '{query.ContainerKey}' refuses the load: item '{line.ItemId}' is {item.Height.Value} m tall, taller than the {interior.Height.Value} m interior height (dimensions capacity).");
                    }
                }
            }

            return PhysicalCapacityVerdict.Accepted(kind, resolved,
                $"Handheld pail '{query.ContainerKey}' accepts the load ({resolved.TotalUnits} units, {resolved.TotalMassKg.Value} kg, {resolved.TotalVolumeLiters.Value} liters) within its caller-supplied capacity.");
        }

        /// <summary>
        /// OnFootPerson leg: Unknown when no carry cap is supplied — no
        /// authored on-foot carry limit exists in source, and the missing
        /// inputs (body size, strength, fatigue) are named in the reason.
        /// The live shopping-loop carry cap stays the live rule and is
        /// deliberately not consulted here: this query answers the
        /// person-carry question, not the shopping rule.
        ///
        /// Priority V1: when the caller supplies its live carry cap in
        /// units (PhysicalCapacityEvaluationContext.OnFootCarryCapUnits),
        /// the leg answers on the units axis only — Fits when the load
        /// is within the cap, DoesNotFit when it exceeds it. Mass and
        /// volume stay Unknown either way (no authored person mass or
        /// volume limit exists), and the cap itself is the caller's own
        /// live rule, passed in — the evaluator still authors no person
        /// limit of its own.
        /// </summary>
        public PhysicalCapacityVerdict EvaluateOnFootPerson(
            PhysicalCapacityQuery query, PhysicalCapacityEvaluationContext context, PhysicalLoadResolution resolved)
        {
            const PhysicalContainerKind kind = PhysicalContainerKind.OnFootPerson;
            if (context.OnFootCarryCapUnits.HasValue)
            {
                int cap = Math.Max(1, context.OnFootCarryCapUnits.Value);
                if (resolved.TotalUnits <= cap)
                {
                    return PhysicalCapacityVerdict.Accepted(kind, resolved,
                        $"On-foot carry: {resolved.TotalUnits} units within the live carry cap of {cap} units " +
                        "(ShoppingLoopOptions.CarryCapacityUnits). Mass and volume remain Unknown — no authored person mass or volume limit exists.");
                }

                return PhysicalCapacityVerdict.Refused(kind, resolved,
                    $"On-foot carry: {resolved.TotalUnits} units exceeds the live carry cap of {cap} units " +
                    "(ShoppingLoopOptions.CarryCapacityUnits). Mass and volume remain Unknown — no authored person mass or volume limit exists.");
            }

            string missing;
            if (context.CarryInputs.HasValue)
            {
                IReadOnlyList<string> names = context.CarryInputs.Value.MissingInputNames;
                missing = names.Count > 0 ? string.Join(", ", names) : "none recorded";
            }
            else
            {
                missing = "body size, strength, fatigue (no person carry inputs supplied)";
            }
            return PhysicalCapacityVerdict.Unknown(kind, resolved,
                $"No authored on-foot carry limit exists. Missing inputs: {missing}.");
        }

        private static int? IdentifiedLotUnitsForLoad(BusinessRuntimeState state, PhysicalLoad load)
        {
            // The identified-lot view for the load's items only (available
            // units per item id, each item counted once). Reported separately
            // from the category-pool Occupied figure; never summed with it
            // (P9 risk C-1).
            int total = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (load.Lines != null)
            {
                foreach ((string ItemId, int Units) line in load.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line.ItemId) || !seen.Add(line.ItemId)) continue;
                    total += state.GetRetailItemStockUnits(line.ItemId);
                }
            }
            return total;
        }
    }
}
