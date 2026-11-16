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
    /// the household holds a parcel and can fund the cheapest adequate
    /// design whose packages all fit the single-trip bound. It never builds
    /// anything: every queued request still passes the driver's full
    /// commission gates, is funded from real cash, and is built from real
    /// materials and real labour. Business expansion, replacement,
    /// relocation and new enterprise have no real signals in the tree, so
    /// they are not auto-detected — they remain available as explicit
    /// requests with a stated reason. Reads authorities only; it writes
    /// nothing but the requests it returns and one summary diagnostic line.
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
        /// no parcel, cannot fund the cheapest adequate design, or have no
        /// adequate design at all.
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
            int skippedCannotFund = 0;
            int skippedNoDesign = 0;
            int skippedShipment = 0;
            int skippedUnevaluated = 0;
            int skippedNoMembers = 0;
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
                if (TryOvercrowding(household, memberCount, out string overcrowdingNotes))
                {
                    reason = NpcConstructionReason.Overcrowding;
                    notes = overcrowdingNotes;
                }
                else if (TryFormation(household, memberCount, sharingGroups, out string formationNotes))
                {
                    reason = NpcConstructionReason.HouseholdFormation;
                    notes = formationNotes;
                }
                else
                {
                    skippedUnevaluated++;
                    continue;
                }
                IReadOnlyList<string> parcels = titles != null
                    ? titles.ParcelsHeldBy("household:" + household.id) : null;
                if (parcels == null || parcels.Count == 0)
                {
                    skippedNoParcel++;
                    continue;
                }
                string parcelId = parcels[0];
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
                $"no design {skippedNoDesign}, shipment-blocked {skippedShipment}, no evidenced need {skippedUnevaluated}, no members {skippedNoMembers}).");
            return requests;
        }

        private bool TryOvercrowding(HouseholdState household, int memberCount, out string notes)
        {
            notes = string.Empty;
            if (housing == null || findPerson == null) return false;
            var members = new List<PersonState>();
            IReadOnlyList<int> memberIds = householdMembers != null
                ? householdMembers(household.id) : null;
            if (memberIds == null) return false;
            foreach (int memberId in memberIds)
            {
                if (memberId < 0) continue;
                PersonState person = findPerson(memberId);
                if (person != null) members.Add(person);
            }
            if (members.Count == 0) return false;
            var probe = new List<string>();
            HouseholdShelterSuitability verdict = suitability.Evaluate(household.id, members, housing, probe);
            if (verdict == null || verdict.GapPlaces <= 0) return false;
            // No occupancy data reads as a gap for everyone: a household
            // counts as overcrowded only when at least one member actually
            // occupies a space and the housing is still insufficient.
            if (verdict.UncoveredPersonIds == null || verdict.UncoveredPersonIds.Count >= members.Count) return false;
            notes = $"overcrowded by {verdict.GapPlaces} sleeping place(s) " +
                $"({verdict.Composition.RequiredSleepingPlaces} needed, {verdict.ProvidedSleepingPlaces} provided)";
            return true;
        }

        private bool TryFormation(HouseholdState household, int memberCount,
            Dictionary<int, List<HouseholdState>> sharingGroups, out string notes)
        {
            notes = string.Empty;
            if (household.homeBuildingId <= 0) return false;
            if (!sharingGroups.TryGetValue(household.homeBuildingId, out List<HouseholdState> group)
                || group == null || group.Count < 2) return false;
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
            if (smallest == null || smallest.id != household.id) return false;
            notes = $"shares home building {household.homeBuildingId} with {group.Count - 1} other household(s); no home of its own";
            return true;
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
