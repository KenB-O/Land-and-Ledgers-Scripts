using System;
using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// D-8: the NPC buyer's real inputs to a contents negotiation. WantedKinds
    /// is the ordered list of movable kinds the buyer wants, most wanted
    /// first; an empty list means the buyer wants every eligible movable.
    /// BuyerMaxCents is the total capital the buyer will spend on the
    /// structure and the contents together: a hard cap, never exceeded.
    /// MovingCostCents is what it would cost the buyer to move their own
    /// furniture into the house instead of buying the seller's contents.
    /// That is the buyer's alternative to the contents, so a seller counter
    /// that asks more than replacement cost plus the moving cost is refused.
    /// The moving cost is a real input from the caller, never invented here.
    /// </summary>
    [Serializable]
    public sealed class NpcContentsBuyerRequest
    {
        public List<string> WantedKinds = new List<string>();
        public int BuyerMaxCents;
        public int MovingCostCents;
        public int DayIndex;
    }

    /// <summary>
    /// D-8: the outcome of an NPC contents negotiation. The agreed price is
    /// the negotiation's outcome, never the F6 anchor: the F6 anchor
    /// (structure-only value plus the requested contents value) is the
    /// asking price the buyer opens below. The implied structure value is
    /// the agreed price minus the agreed contents value, so conservation
    /// (D-6) holds by construction: the structure absorbs the discount and
    /// the contents keep their frozen agreed values.
    /// </summary>
    [Serializable]
    public sealed class NpcContentsNegotiationResult
    {
        public bool Agreed;
        public int AgreedPriceCents;
        public PropertyContentsSelection AgreedSelection;
        public int ImpliedStructureValueCents;
        public string FailureReason;
        public List<string> Rounds = new List<string>();
    }

    /// <summary>
    /// D-8: the buyer's selection logic and the contents valuation helpers
    /// used by the bounded-round negotiation on NpcPropertyAcquisitionService.
    /// No cash moves here and no title moves here: the negotiation is pure
    /// computation. The caller builds the acquisition plan from the agreed
    /// result and closes it through the existing D-6/D-7 path, which is
    /// where cash and title actually move.
    ///
    /// F6's FurnitureValuation lives on the F branch, not here, so this
    /// class never references it: the caller supplies the structure-only
    /// value and the replacement-cost function as plain values, exactly as
    /// D-6 consumes structureValueCents as a caller-supplied int?.
    /// </summary>
    public static class NpcContentsNegotiation
    {
        /// <summary>
        /// Builds the contents selection the buyer asks for, from the
        /// seller's register. Fixtures are the floor: every priced fixture
        /// is included, because a house sells with its stove and shelves
        /// unless the buyer says otherwise. Wanted movables are added in
        /// priority order while the running total (structure plus contents)
        /// stays inside the buyer's capital; an unaffordable movable is
        /// skipped and the next wanted kind is tried.
        ///
        /// An item with no replacement cost (a stove under F6 pricing) or a
        /// wrecked item (condition at or below 0.05, which B6 records as
        /// unpriced) is never included: a fixture is excluded with
        /// HeldInPlace, a movable with RemovedBySeller, and the exclusion is
        /// recorded in notes. A wrecked item is a salvage candidate, not a
        /// valued contents item (F6); failing the whole negotiation because
        /// the house has a stove would make the negotiation useless.
        ///
        /// Returns null with a failure reason when the buyer cannot proceed
        /// at all: no register, no price data, no capital, a structure
        /// value below zero, or an unclassifiable item in the building. An
        /// unclassifiable item fails loudly because nobody can agree what
        /// it is worth or whether it stays: the classification gap has to
        /// be fixed before the contents can be negotiated.
        ///
        /// The fixtures-included price is the floor: when even structure
        /// plus the priced fixtures exceeds the buyer's capital, the buyer
        /// walks away before bidding.
        /// </summary>
        public static PropertyContentsSelection BuildBuyerContentsRequest(
            HouseholdFurnishingRegister register,
            int buildingInstanceId,
            string sellerOwnerKind,
            string sellerOwnerId,
            int structureOnlyCents,
            NpcContentsBuyerRequest request,
            Func<string, int> replacementCostCentsForKind,
            List<string> notes,
            out string failureReason)
        {
            failureReason = null;
            if (register == null)
            {
                failureReason = "the buyer cannot see the contents: the seller has no furnishing register.";
                return null;
            }
            if (request == null)
            {
                failureReason = "the buyer made no request: no wants, no capital, no moving cost.";
                return null;
            }
            if (replacementCostCentsForKind == null)
            {
                failureReason = "the buyer cannot value the contents: no replacement-cost data was supplied.";
                return null;
            }
            if (structureOnlyCents < 0)
            {
                failureReason = "the structure value is " + structureOnlyCents + "c: a negative structure value cannot anchor a negotiation.";
                return null;
            }
            if (request.BuyerMaxCents <= 0)
            {
                failureReason = "the buyer has no capital for the purchase: BuyerMaxCents is " + request.BuyerMaxCents + "c.";
                return null;
            }
            if (request.MovingCostCents < 0)
            {
                failureReason = "the moving cost is " + request.MovingCostCents + "c: a negative moving cost is not a real input.";
                return null;
            }

            List<FurnishingContentsItem> eligible =
                PropertyContentsSelection.ListEligibleContents(register, buildingInstanceId);
            if (eligible.Count == 0)
            {
                failureReason = "the building holds no eligible contents: the caller should treat this as a bare sale, not a negotiation.";
                return null;
            }
            if (notes == null) notes = new List<string>();

            // An unclassifiable item fails the request loudly: B6 refuses it
            // at agreement too, and nobody can negotiate what it is.
            foreach (FurnishingContentsItem item in eligible)
            {
                if (item.Kind == FurnishingFixtureKind.Unspecified)
                {
                    failureReason = "asset " + item.AssetId + " (kind " + item.AssetKind + ") in building "
                        + buildingInstanceId + " could not be classified fixture or movable: "
                        + "the contents cannot be negotiated until it is classified.";
                    return null;
                }
            }

            // Priced fixtures are the floor: the buyer takes them all.
            // A fixture with no replacement cost, or a wrecked one, is
            // excluded (HeldInPlace) with a recorded note.
            var includedIds = new List<string>();
            var unpricedFixtureIds = new List<string>();
            int fixtureValue = 0;
            foreach (FurnishingContentsItem item in eligible)
            {
                if (item.Kind != FurnishingFixtureKind.Fixture) continue;
                int value = PropertyContentsSelection.ValueCentsFor(item.AssetKind, item.Condition01,
                    replacementCostCentsForKind, out bool priced);
                if (!priced)
                {
                    unpricedFixtureIds.Add(item.AssetId);
                    notes.Add("fixture " + item.AssetId + " (" + item.AssetKind + ") is not valued "
                        + (item.Condition01 <= 0.05f
                            ? "because it is wrecked (condition " + item.Condition01.ToString("F2") + "): a salvage candidate, not a contents item."
                            : "because it has no replacement cost: it stays with the seller, held in place."));
                    continue;
                }
                includedIds.Add(item.AssetId);
                fixtureValue += value;
            }

            if (structureOnlyCents + fixtureValue > request.BuyerMaxCents)
            {
                failureReason = "the buyer cannot afford even the fixtures-included price: structure "
                    + structureOnlyCents + "c plus fixtures " + fixtureValue + "c is "
                    + (structureOnlyCents + fixtureValue) + "c against a maximum of "
                    + request.BuyerMaxCents + "c. The buyer walks away before bidding.";
                return null;
            }

            // Wanted movables in the buyer's priority order: the wanted
            // kinds first, in the order the buyer named them, then — only
            // when the buyer named no kinds at all (wants every movable) —
            // every eligible movable in register order. An empty wanted
            // list means the buyer wants every eligible movable.
            var wantedKinds = new List<string>(request.WantedKinds ?? new List<string>());
            bool wantsAll = wantedKinds.Count == 0;
            var candidates = new List<FurnishingContentsItem>();
            if (wantsAll)
            {
                foreach (FurnishingContentsItem item in eligible)
                {
                    if (item.Kind == FurnishingFixtureKind.Movable) candidates.Add(item);
                }
            }
            else
            {
                foreach (string kind in wantedKinds)
                {
                    foreach (FurnishingContentsItem item in eligible)
                    {
                        if (item.Kind == FurnishingFixtureKind.Movable && item.AssetKind == kind) candidates.Add(item);
                    }
                }
                foreach (FurnishingContentsItem item in eligible)
                {
                    if (item.Kind != FurnishingFixtureKind.Movable) continue;
                    if (!candidates.Contains(item))
                    {
                        notes.Add("movable " + item.AssetId + " (" + item.AssetKind + ") is not wanted by the buyer: excluded.");
                    }
                }
            }
            int runningTotal = structureOnlyCents + fixtureValue;
            foreach (FurnishingContentsItem item in candidates)
            {
                int value = PropertyContentsSelection.ValueCentsFor(item.AssetKind, item.Condition01,
                    replacementCostCentsForKind, out bool priced);
                if (!priced)
                {
                    notes.Add("movable " + item.AssetId + " (" + item.AssetKind + ") is not valued "
                        + (item.Condition01 <= 0.05f
                            ? "because it is wrecked (condition " + item.Condition01.ToString("F2") + "): a salvage candidate, not a contents item."
                            : "because it has no replacement cost: the seller removes it.")
                        + " Excluded.");
                    continue;
                }
                if (runningTotal + value > request.BuyerMaxCents)
                {
                    notes.Add("movable " + item.AssetId + " (" + item.AssetKind + ", " + value
                        + "c) does not fit the buyer's remaining capital: skipped, the next wanted kind is tried.");
                    continue;
                }
                includedIds.Add(item.AssetId);
                runningTotal += value;
            }

            // Build through the B6 contract. BuildAll when everything
            // eligible is included, BuildNoMovable when the included set is
            // exactly the priced fixtures, BuildCustom otherwise: the custom
            // path is the only one that can carry an unpriced fixture as an
            // exclusion, because the other two builders include every
            // fixture they are given.
            PropertyContentsSelection selection;
            bool allIncluded = includedIds.Count == eligible.Count;
            // Fixtures only: no unpriced fixture exists (one would force
            // the custom path), something is included, every included item
            // is a fixture, and every fixture is included.
            bool fixturesOnly = unpricedFixtureIds.Count == 0 && includedIds.Count > 0;
            if (fixturesOnly)
            {
                foreach (FurnishingContentsItem item in eligible)
                {
                    if (item.Kind != FurnishingFixtureKind.Fixture) continue;
                    if (!includedIds.Contains(item.AssetId)) { fixturesOnly = false; break; }
                }
            }
            if (fixturesOnly)
            {
                foreach (string id in includedIds)
                {
                    FurnishingContentsItem found = eligible.Find(i => i.AssetId == id);
                    if (found == null || found.Kind != FurnishingFixtureKind.Fixture)
                    {
                        fixturesOnly = false;
                        break;
                    }
                }
            }
            if (allIncluded)
            {
                selection = PropertyContentsSelection.BuildAll(eligible, sellerOwnerKind, sellerOwnerId,
                    buildingInstanceId, request.DayIndex, replacementCostCentsForKind);
            }
            else if (fixturesOnly)
            {
                selection = PropertyContentsSelection.BuildNoMovable(eligible, sellerOwnerKind, sellerOwnerId,
                    buildingInstanceId, request.DayIndex, replacementCostCentsForKind);
            }
            else
            {
                selection = PropertyContentsSelection.BuildCustom(eligible, includedIds, sellerOwnerKind, sellerOwnerId,
                    buildingInstanceId, request.DayIndex, replacementCostCentsForKind);
            }

            // The builder must never carry an unpriced included item: every
            // unpriced item was excluded above. A non-empty list here is a
            // logic error in this method, not a negotiation outcome.
            if (selection.UnpricedIncludedAssetIds.Count > 0)
            {
                failureReason = "internal error: the buyer request carried unpriced included items ("
                    + string.Join(", ", selection.UnpricedIncludedAssetIds)
                    + "). Nothing was agreed.";
                return null;
            }
            if (selection.SkippedUnclassifiedAssetIds.Count > 0 || selection.SkippedUnknownAssetIds.Count > 0)
            {
                failureReason = "internal error: the buyer request skipped items the builder could not place ("
                    + string.Join(", ", selection.SkippedUnclassifiedAssetIds)
                    + string.Join(", ", selection.SkippedUnknownAssetIds)
                    + "). Nothing was agreed.";
                return null;
            }
            return selection;
        }

        /// <summary>
        /// The replacement cost of the included items in a selection, from
        /// the eligible items list (the selection's lines carry no asset
        /// kind, so the kind comes from the eligible list). The buyer's
        /// contents ceiling is this plus their moving cost: a seller counter
        /// above it is refused, because the buyer would rather bring their
        /// own furniture.
        /// </summary>
        public static int ReplacementValueCents(
            PropertyContentsSelection selection,
            List<FurnishingContentsItem> eligibleItems,
            Func<string, int> replacementCostCentsForKind)
        {
            if (selection == null || eligibleItems == null || replacementCostCentsForKind == null) return 0;
            int total = 0;
            foreach (FurnishingContentsLine line in selection.Lines)
            {
                if (!line.Included) continue;
                FurnishingContentsItem item = eligibleItems.Find(i => i.AssetId == line.AssetId);
                if (item == null) continue;
                total += Math.Max(0, replacementCostCentsForKind(item.AssetKind));
            }
            return total;
        }
    }
}
