using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.World.Property;

namespace LandLedgers.Economy
{
    /// <summary>
    /// DEF-011 (Decision 7): the commissioning trigger, owned outside the
    /// driver. Once a day it scans households for legitimate construction
    /// needs the game can actually evidence — overcrowding (occupancy-backed)
    /// and household formation (a household sharing its home building with
    /// another) — and queues ONE explicit request per household, only when
    /// the household holds a vacant parcel and can fund the cheapest adequate
    /// design whose packages are shippable by multi-trip freight at the
    /// single-trip bound (D-1: the wagon must be able to carry the cargo
    /// at all — the trip count is recorded, never a refusal by itself).
    /// It never builds
    /// anything: every queued request still passes the driver's full
    /// commission gates, is funded from real cash, and is built from real
    /// materials and real labour. Business expansion, replacement,
    /// relocation and new enterprise have no real signals in the tree, so
    /// they are not auto-detected — they remain available as explicit
    /// requests with a stated reason. Reads authorities only; it writes
    /// nothing but the requests it returns, one summary diagnostic line,
    /// and (D-0) one trace line per household skipped for no evidenced
    /// need. The D-0 diagnostics change no decision: the queue conditions
    /// are identical with and without them.
    /// </summary>
    public sealed class NpcConstructionNeedEvaluator
    {
        private readonly NpcConstructionCommissioner commissioner;
        private readonly BuildingDesignCatalog catalog;
        private readonly IConstructionMaterialPricePort pricePort;
        private readonly CreditCashBridge cash;
        private readonly HousingAuthority housing;
        private readonly TitleAuthority titles;
        private readonly Func<int, IReadOnlyList<int>> householdMembers;
        private readonly Func<int, PersonState> findPerson;
        private readonly Func<int, bool> hasLiveProject;
        private readonly Func<int, bool> hasPendingRequest;
        private readonly HouseholdShelterSuitabilityEvaluator suitability =
            new HouseholdShelterSuitabilityEvaluator();

        public NpcConstructionNeedEvaluator(
            NpcConstructionCommissioner commissioner,
            BuildingDesignCatalog catalog,
            IConstructionMaterialPricePort pricePort,
            CreditCashBridge cash,
            HousingAuthority housing,
            TitleAuthority titles,
            Func<int, IReadOnlyList<int>> householdMembers,
            Func<int, PersonState> findPerson,
            Func<int, bool> hasLiveProject,
            Func<int, bool> hasPendingRequest)
        {
            this.commissioner = commissioner;
            this.catalog = catalog;
            this.pricePort = pricePort;
            this.cash = cash;
            this.housing = housing;
            this.titles = titles;
            this.householdMembers = householdMembers;
            this.findPerson = findPerson;
            this.hasLiveProject = hasLiveProject;
            this.hasPendingRequest = hasPendingRequest;
        }

        /// <summary>
        /// Scans the households once and returns the day's explicit
        /// commission requests, at most one per household. Households are
        /// skipped (counted in the summary) when they have no active
        /// members, already have a live project or a pending request, hold
        /// no parcel, hold no vacant parcel (D-15), cannot fund the cheapest
        /// adequate design, or have no adequate design at all. Households
        /// skipped for no evidenced need
        /// are split three ways in the summary (no-occupancy-data,
        /// suitably-housed, no-sharing) and each gets one diagnostic trace
        /// line naming its housing evidence, parcels held and cash, so one
        /// Unity run distinguishes a blind system (H1) from genuinely
        /// suitable housing (H2). The diagnostics are read-only: they
        /// change no queue decision.
        /// </summary>
        public List<NpcConstructionRequest> EvaluateDay(
            IReadOnlyList<HouseholdState> households,
            int dayIndex,
            int? maxUnitsPerTrip,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            var requests = new List<NpcConstructionRequest>();
            if (households == null || commissioner == null || catalog == null || pricePort == null)
            {
                diag.Add("NpcConstructionNeedEvaluator: no households, commissioner, catalog or price port — nothing evaluated.");
                return requests;
            }

            var memberCounts = new Dictionary<int, int>();
            var sharingGroups = new Dictionary<int, List<HouseholdState>>();
            foreach (HouseholdState household in households)
            {
                if (household == null || household.lifecycleState != HouseholdLifecycleState.Active) continue;
                IReadOnlyList<int> members = householdMembers != null
                    ? householdMembers(household.id) : null;
                int count = 0;
                if (members != null)
                    foreach (int memberId in members)
                        if (memberId >= 0) count++;
                memberCounts[household.id] = count;
                if (household.homeBuildingId > 0)
                {
                    if (!sharingGroups.TryGetValue(household.homeBuildingId, out List<HouseholdState> group))
                    {
                        group = new List<HouseholdState>();
                        sharingGroups[household.homeBuildingId] = group;
                    }
                    group.Add(household);
                }
            }

            int skippedLiveOrPending = 0;
            int skippedNoParcel = 0;
            int skippedNoVacantParcel = 0;
            int skippedCannotFund = 0;
            int skippedNoDesign = 0;
            int skippedShipment = 0;
            int skippedUnevaluated = 0;
            int skippedNoMembers = 0;
            // D-0: the "no evidenced need" total is split three ways so one
            // Unity run distinguishes H1 (system blind, no occupancy data)
            // from H2 (suitably housed, zero genuinely correct). The three
            // sub-counts always sum to skippedUnevaluated. Partition rule,
            // by the furthest signal evaluation: no occupancy data at all
            // (overcrowding probe unevidenced) -> no-occupancy-data;
            // suitably housed but no shared building (formation never had a
            // group to evaluate) -> no-sharing; suitably housed and sharing
            // but not the smallest household (the offshoot, if any, is the
            // one that would queue) -> suitably-housed.
            int skippedNoOccupancyData = 0;
            int skippedSuitablyHoused = 0;
            int skippedNoSharing = 0;
            foreach (HouseholdState household in households)
            {
                if (household == null || household.lifecycleState != HouseholdLifecycleState.Active) continue;
                if (!memberCounts.TryGetValue(household.id, out int memberCount) || memberCount <= 0)
                {
                    skippedNoMembers++;
                    continue;
                }
                if ((hasLiveProject != null && hasLiveProject(household.id))
                    || (hasPendingRequest != null && hasPendingRequest(household.id)))
                {
                    skippedLiveOrPending++;
                    continue;
                }
                NpcConstructionReason reason;
                string notes;
                OvercrowdingSignal overcrowding = ProbeOvercrowding(household, memberCount,
                    out HouseholdShelterSuitability verdict, out int occupancyCount,
                    out string overcrowdingNotes);
                FormationSignal formation = ProbeFormation(household, memberCount, sharingGroups,
                    out string formationNotes);
                if (overcrowding == OvercrowdingSignal.Overcrowded)
                {
                    reason = NpcConstructionReason.Overcrowding;
                    notes = overcrowdingNotes;
                }
                else if (formation == FormationSignal.Formation)
                {
                    reason = NpcConstructionReason.HouseholdFormation;
                    notes = formationNotes;
                }
                else
                {
                    skippedUnevaluated++;
                    string bucket = ClassifyNoEvidence(overcrowding, formation);
                    if (bucket == "no-occupancy-data") skippedNoOccupancyData++;
                    else if (bucket == "no-sharing") skippedNoSharing++;
                    else skippedSuitablyHoused++;
                    TraceNoEvidence(dayIndex, household, memberCount, verdict, occupancyCount,
                        overcrowding, formation, diag);
                    continue;
                }
                IReadOnlyList<string> parcels = titles != null
                    ? titles.ParcelsHeldBy("household:" + household.id) : null;
                if (parcels == null || parcels.Count == 0)
                {
                    skippedNoParcel++;
                    continue;
                }
                // D-15 (F-D11-5): prefer a vacant parcel among the held
                // ones. A household whose only held parcel is its home
                // parcel queued under the old first-parcel pick and refused
                // at Commission under the D-2 vacancy gate, retrying daily;
                // it now skips here with a counted, traced reason instead.
                string parcelId = SelectVacantParcel(parcels, dayIndex, household, diag);
                if (parcelId == null)
                {
                    skippedNoVacantParcel++;
                    TraceNoVacantParcel(dayIndex, household, parcels, diag);
                    continue;
                }
                int funds = FundsFor(household.id);
                string designId = SelectCheapestViableDesign(memberCount, funds, maxUnitsPerTrip,
                    reason, notes, dayIndex, out string selectionSkip);
                if (designId == null)
                {
                    if (selectionSkip == "cannot-fund") skippedCannotFund++;
                    else if (selectionSkip == "shipment") skippedShipment++;
                    else skippedNoDesign++;
                    continue;
                }
                var memberIds = new List<int>();
                IReadOnlyList<int> members = householdMembers != null
                    ? householdMembers(household.id) : null;
                if (members != null)
                    foreach (int memberId in members)
                        if (memberId >= 0) memberIds.Add(memberId);
                requests.Add(new NpcConstructionRequest
                {
                    HouseholdId = household.id,
                    DesignId = designId,
                    ParcelId = parcelId,
                    MemberPersonIds = memberIds,
                    Arrangement = AccommodationArrangement.OwnerOccupied,
                    Reason = reason,
                    ReasonNotes = notes,
                });
            }

            diag.Add($"NpcConstructionNeedEvaluator day {dayIndex}: scanned {memberCounts.Count} active household(s), queued {requests.Count} request(s) " +
                $"(live/pending {skippedLiveOrPending}, no parcel {skippedNoParcel}, cannot fund {skippedCannotFund}, " +
                $"no design {skippedNoDesign}, shipment-blocked {skippedShipment}, no evidenced need {skippedUnevaluated} (no-occupancy-data {skippedNoOccupancyData}, suitably-housed {skippedSuitablyHoused}, no-sharing {skippedNoSharing}), no members {skippedNoMembers}, no vacant parcel {skippedNoVacantParcel}).");
            // D-15: the "no vacant parcel" token sits last so the D-0
            // summary-token assertions ("live/pending 0, no parcel 0, cannot
            // fund 0, ..." and "..., no members 0") still match verbatim.
            return requests;
        }

        /// <summary>
        /// D-0: what the overcrowding probe evidenced. Unavailable means the
        /// probe could not run at all; NoOccupancyData is the D5-guard case
        /// (a gap for everyone, because no member occupies any space);
        /// Suitable means the household's occupancies cover its requirement;
        /// Overcrowded is a real evidenced need.
        /// </summary>
        private enum OvercrowdingSignal
        {
            Unavailable,
            NoOccupancyData,
            Suitable,
            Overcrowded,
        }

        /// <summary>
        /// D-0: what the formation probe evidenced. Formation means this
        /// household is the smallest sharing the home building (the
        /// offshoot); the other three are the distinct reasons it did not
        /// fire.
        /// </summary>
        private enum FormationSignal
        {
            NoHomeBuilding,
            NoSharing,
            NotSmallest,
            Formation,
        }

        /// <summary>
        /// D-0: partitions a no-evidence skip into exactly one of the three
        /// summary sub-counts (see the EvaluateDay comment for the rule).
        /// Overcrowded is unreachable here — it queues — so the final else
        /// covers only the suitable-and-sharing-but-not-smallest case.
        /// </summary>
        private static string ClassifyNoEvidence(OvercrowdingSignal overcrowding, FormationSignal formation)
        {
            if (overcrowding == OvercrowdingSignal.Unavailable
                || overcrowding == OvercrowdingSignal.NoOccupancyData)
                return "no-occupancy-data";
            if (formation == FormationSignal.NoSharing || formation == FormationSignal.NoHomeBuilding)
                return "no-sharing";
            return "suitably-housed";
        }

        /// <summary>
        /// D-0 diagnostic only: one trace line per household skipped for no
        /// evidenced need, naming everything the next Unity run needs to
        /// distinguish H1 (zero occupancies, system blind) from H2
        /// (suitably housed, zero genuinely correct): home building, member
        /// and occupancy counts, provided sleeping places, gap, uncovered
        /// persons, parcels held and cash. Parcels and cash are read here
        /// even though the parcel/funding gates never ran for these
        /// households — the dump also answers whether those gates would
        /// pass if a need were evidenced. When the suitability verdict is
        /// null (probe unavailable) the numeric fields report the evidenced
        /// values (all zero) and the parenthetical names the probe state.
        /// Reads authorities only; it changes no state and queues nothing.
        /// </summary>
        private void TraceNoEvidence(int dayIndex, HouseholdState household, int memberCount,
            HouseholdShelterSuitability verdict, int occupancyCount,
            OvercrowdingSignal overcrowding, FormationSignal formation, List<string> diag)
        {
            int provided = verdict != null ? verdict.ProvidedSleepingPlaces : 0;
            int gap = verdict != null ? verdict.GapPlaces : 0;
            int uncovered = verdict != null ? verdict.UncoveredPersonIds.Count : memberCount;
            IReadOnlyList<string> parcels = titles != null
                ? titles.ParcelsHeldBy("household:" + household.id) : null;
            int parcelCount = parcels != null ? parcels.Count : 0;
            int funds = FundsFor(household.id);
            diag.Add($"NpcConstructionNeedEvaluator day {dayIndex}: H{household.id} no evidenced need - " +
                $"home building {household.homeBuildingId}, members {memberCount}, occupancies {occupancyCount}, " +
                $"provided {provided} sleeping place(s), gap {gap}, uncovered {uncovered} person(s), " +
                $"parcels held {parcelCount}, cash {funds}c " +
                $"(overcrowding: {DescribeOvercrowding(overcrowding, verdict)}; formation: {DescribeFormation(formation)}).");
        }

        /// <summary>
        /// D-15 (F-D11-5): among the held parcels, prefer the first with no
        /// registered building — the D-2 vacancy signal
        /// (HousingAuthority.BuildingsForParcel, the same read
        /// AuthorizeBuild uses). A parcel with any registered building is
        /// not vacant; a closed building still holds its parcel (there is
        /// no demolition system). Returns null when every held parcel
        /// carries a building. When no housing authority is supplied the
        /// vacancy check cannot run — the D-2 precedent applies: the first
        /// held parcel is returned and the unchecked choice is recorded in
        /// diagnostics, so a missing housing authority never newly blocks a
        /// commission (the D-1 missing-wagon precedent). The held order is
        /// the order ParcelsHeldBy returns, the same order the old
        /// first-parcel pick used — no new ordering is assumed. Reads
        /// authorities only; it queues nothing.
        /// </summary>
        private string SelectVacantParcel(IReadOnlyList<string> parcels, int dayIndex,
            HouseholdState household, List<string> diag)
        {
            if (housing == null)
            {
                diag.Add($"NpcConstructionNeedEvaluator day {dayIndex}: H{household.id} vacancy unchecked - " +
                    $"no housing authority supplied, queuing on first held parcel '{parcels[0]}' without a vacancy check.");
                return parcels[0];
            }
            foreach (string parcelId in parcels)
            {
                List<Building> existing = housing.BuildingsForParcel(parcelId);
                if (existing == null || existing.Count == 0) return parcelId;
            }
            return null;
        }

        /// <summary>
        /// D-15 diagnostic only: one trace line per household skipped because
        /// every held parcel carries a registered building, naming the parcels
        /// held and the first building on each — so one Unity run distinguishes
        /// "has parcels but all occupied" from "has no parcels" (no parcel)
        /// and from "cannot fund". Reads authorities only; it changes no state
        /// and queues nothing.
        /// </summary>
        private void TraceNoVacantParcel(int dayIndex, HouseholdState household,
            IReadOnlyList<string> parcels, List<string> diag)
        {
            var held = new List<string>();
            foreach (string parcelId in parcels)
            {
                string first = string.Empty;
                if (housing != null)
                {
                    List<Building> existing = housing.BuildingsForParcel(parcelId);
                    if (existing != null && existing.Count > 0 && existing[0] != null)
                        first = existing[0].BuildingId;
                }
                held.Add(string.IsNullOrEmpty(first) ? parcelId : parcelId + " (" + first + ")");
            }
            diag.Add($"NpcConstructionNeedEvaluator day {dayIndex}: H{household.id} no vacant parcel - " +
                $"holds {parcels.Count} parcel(s), all occupied: {string.Join(", ", held)}.");
        }

        private static string DescribeOvercrowding(OvercrowdingSignal signal, HouseholdShelterSuitability verdict)
        {
            switch (signal)
            {
                case OvercrowdingSignal.Overcrowded:
                    return $"overcrowded by {verdict.GapPlaces} place(s)";
                case OvercrowdingSignal.Suitable:
                    return "suitable";
                case OvercrowdingSignal.NoOccupancyData:
                    return "no occupancy data";
                default:
                    return "probe unavailable";
            }
        }

        private static string DescribeFormation(FormationSignal signal)
        {
            switch (signal)
            {
                case FormationSignal.Formation:
                    return "offshoot household";
                case FormationSignal.NotSmallest:
                    return "shares a home but is not the smallest household";
                case FormationSignal.NoHomeBuilding:
                    return "no home building";
                default:
                    return "no shared building";
            }
        }

        private OvercrowdingSignal ProbeOvercrowding(HouseholdState household, int memberCount,
            out HouseholdShelterSuitability verdict, out int occupancyCount, out string notes)
        {
            notes = string.Empty;
            verdict = null;
            occupancyCount = 0;
            if (housing == null || findPerson == null) return OvercrowdingSignal.Unavailable;
            var members = new List<PersonState>();
            IReadOnlyList<int> memberIds = householdMembers != null
                ? householdMembers(household.id) : null;
            if (memberIds == null) return OvercrowdingSignal.Unavailable;
            foreach (int memberId in memberIds)
            {
                if (memberId < 0) continue;
                PersonState person = findPerson(memberId);
                if (person != null) members.Add(person);
            }
            if (members.Count == 0) return OvercrowdingSignal.Unavailable;
            var probe = new List<string>();
            verdict = suitability.Evaluate(household.id, members, housing, probe);
            if (verdict == null) return OvercrowdingSignal.Unavailable;
            int livingCount = 0;
            foreach (PersonState member in members)
                if (member != null && member.deathDayIndex < 0) livingCount++;
            // The verdict lists living members with no occupancy, so the
            // occupancy count is the living members minus the uncovered.
            occupancyCount = Math.Max(0, livingCount - verdict.UncoveredPersonIds.Count);
            if (verdict.GapPlaces <= 0) return OvercrowdingSignal.Suitable;
            // No occupancy data reads as a gap for everyone: a household
            // counts as overcrowded only when at least one member actually
            // occupies a space and the housing is still insufficient.
            if (verdict.UncoveredPersonIds == null || verdict.UncoveredPersonIds.Count >= members.Count)
                return OvercrowdingSignal.NoOccupancyData;
            notes = $"overcrowded by {verdict.GapPlaces} sleeping place(s) " +
                $"({verdict.Composition.RequiredSleepingPlaces} needed, {verdict.ProvidedSleepingPlaces} provided)";
            return OvercrowdingSignal.Overcrowded;
        }

        private FormationSignal ProbeFormation(HouseholdState household, int memberCount,
            Dictionary<int, List<HouseholdState>> sharingGroups, out string notes)
        {
            notes = string.Empty;
            if (household.homeBuildingId <= 0) return FormationSignal.NoHomeBuilding;
            if (!sharingGroups.TryGetValue(household.homeBuildingId, out List<HouseholdState> group)
                || group == null || group.Count < 2) return FormationSignal.NoSharing;
            // The established household keeps the existing home; the
            // offshoot — the smallest household sharing the building, ties
            // broken by lower household id — needs a home of its own.
            HouseholdState smallest = null;
            int smallestCount = int.MaxValue;
            foreach (HouseholdState other in group)
            {
                if (other == null) continue;
                int otherCount = memberCountsFor(other.id);
                if (smallest == null || otherCount < smallestCount
                    || (otherCount == smallestCount && other.id < smallest.id))
                {
                    smallest = other;
                    smallestCount = otherCount;
                }
            }
            if (smallest == null || smallest.id != household.id) return FormationSignal.NotSmallest;
            notes = $"shares home building {household.homeBuildingId} with {group.Count - 1} other household(s); no home of its own";
            return FormationSignal.Formation;
        }

        private int memberCountsFor(int householdId)
        {
            // Local helper kept trivial: the caller precomputed counts, but
            // this path is only reached for sharing groups, so a direct
            // lookup is cheap and keeps the grouping code honest.
            IReadOnlyList<int> members = householdMembers != null
                ? householdMembers(householdId) : null;
            int count = 0;
            if (members != null)
                foreach (int memberId in members)
                    if (memberId >= 0) count++;
            return count;
        }

        private int FundsFor(int householdId)
        {
            if (cash == null) return 0;
            IRealCashStore store = cash.FindStore("household:" + householdId);
            return store != null ? store.ReadBalanceCents() : 0;
        }

        private string SelectCheapestViableDesign(int memberCount, int funds, int? maxUnitsPerTrip,
            NpcConstructionReason reason, string notes, int dayIndex, out string skipKind)
        {
            skipKind = "no-design";
            var pricedAdequate = new List<KeyValuePair<string, int>>();
            foreach (BuildingDesign design in catalog.AllDesigns())
            {
                if (design == null) continue;
                int capacity = 0;
                foreach (BuildingDesignSpaceSpec spec in design.SpaceSpecs ?? new List<BuildingDesignSpaceSpec>())
                    capacity += Math.Max(0, spec != null ? spec.SleepingCapacity : 0);
                if (capacity < Math.Max(1, memberCount)) continue;
                int estimate = World.Property.NpcPropertyDecisionEngine.EstimateBuildCostCents(design, pricePort);
                if (estimate < 0) continue;
                pricedAdequate.Add(new KeyValuePair<string, int>(design.DesignId, estimate));
            }
            if (pricedAdequate.Count == 0) return null;
            pricedAdequate.Sort((a, b) =>
            {
                int byEstimate = a.Value.CompareTo(b.Value);
                return byEstimate != 0 ? byEstimate
                    : string.CompareOrdinal(a.Key, b.Key);
            });
            if (pricedAdequate[0].Value > funds)
            {
                skipKind = "cannot-fund";
                return null;
            }
            foreach (KeyValuePair<string, int> candidate in pricedAdequate)
            {
                var probe = new List<string>();
                NpcConstructionEvaluation evaluation = commissioner.EvaluateCommission(
                    catalog, candidate.Key, Math.Max(1, memberCount), pricePort, funds,
                    maxUnitsPerTrip, reason, notes, dayIndex, probe);
                if (evaluation != null && evaluation.IsViable) return candidate.Key;
            }
            skipKind = "shipment";
            return null;
        }
    }
}
