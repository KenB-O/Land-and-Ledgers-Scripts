using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// B6 (Agent B, expanded campaign 2026-11-16): the itemized
    /// property-contents contract Agent D's staged acquisition flows
    /// consume (D plan §4; D-6 attaches this type to AcquisitionDealState
    /// and NpcAcquisitionPlan — D defines NO second selection type).
    ///
    /// Modes: All / No Movable / Custom. No Furnished/Unfurnished binary
    /// anywhere in the contract or the API. Contents value is a COMPONENT
    /// of the agreed price, never a flat furnishing surcharge and never
    /// double-counted: the selection exposes IncludedFixtureValueCents
    /// separately so the structural valuation can exclude the value of
    /// included fixtures, and CheckConservation pins price == structure
    /// + contents, cent for cent.
    ///
    /// Valuation is condition-weighted (Canon §11.6, the same shape as
    /// HouseholdFurnishingRegister.TransferableValueCents): agreed value
    /// = round(replacement cost x condition); unusable assets
    /// (condition at or below 0.05) contribute 0c. Replacement costs come
    /// from the CALLER's price data (Agent F's prices) — never invented
    /// here. A kind with no supplied cost is unpriced, contributes 0c,
    /// and is recorded in UnpricedIncludedAssetIds; agreement-time
    /// validation with requirePriced refuses it loudly.
    ///
    /// Seller authority is validated at agreement AND re-validated at
    /// close (Revalidate): an included item that is missing, no longer
    /// owned by the seller, removed from the building, or whose condition
    /// changed (agreed value stale) fails loudly — the deal fails and
    /// unwinds, never silently drops the item. Excluded-item drift is
    /// reported as notes, not failures: an excluded item the seller sold
    /// or moved is the seller's own property leaving, not a breach of
    /// the contract — but the parties are told.
    ///
    /// Excluded-item disposition defaults (B6, owner-correctable; D plan
    /// §7 Q3 still open): an excluded movable goes with the seller
    /// (RemovedBySeller); an excluded fixture stays in place as the
    /// seller's property until removed (HeldInPlace).
    ///
    /// No save DTO is added here: D-6 persists the selection inside the
    /// deal DTOs it owns. The type is [Serializable] with plain fields
    /// so D can embed it directly.
    /// </summary>
    public enum ContentsSelectionMode
    {
        Unspecified = 0,
        /// <summary>Every eligible item in the building is included.</summary>
        All = 1,
        /// <summary>Fixtures only; every movable stays with the seller.</summary>
        NoMovable = 2,
        /// <summary>Caller-chosen included set.</summary>
        Custom = 3,
    }

    /// <summary>
    /// B6: what happens to an excluded item. The arrangement is recorded
    /// on the line — an excluded item is never silently left behind.
    /// </summary>
    public enum ExcludedItemDisposition
    {
        Unspecified = 0,
        /// <summary>The seller takes the item when they leave.</summary>
        RemovedBySeller = 1,
        /// <summary>The item stays in the building as the seller's property until removed.</summary>
        HeldInPlace = 2,
    }

    /// <summary>
    /// B6: one eligible contents item, as D reads it from B's register —
    /// stable id, kind (Fixture | Movable), current owner, condition,
    /// and removal/custody state. D builds NO second registry; it reads
    /// this listing. Only items INSTALLED in the building are eligible:
    /// assets in household custody are not part of the property and stay
    /// with the seller without a line.
    /// </summary>
    [Serializable]
    public sealed class FurnishingContentsItem
    {
        public string AssetId = string.Empty;
        /// <summary>The asset kind string (e.g. "furniture.bed") — the key the caller's price func values by.</summary>
        public string AssetKind = string.Empty;
        public FurnishingFixtureKind Kind;
        public string OwnerKind = string.Empty;
        public string OwnerId = string.Empty;
        public float Condition01;
        public int BuildingInstanceId;
        public bool Installed;
        public string LocationId = string.Empty;
        public bool IsUsable;
    }

    /// <summary>
    /// B6: one itemized line of a contents selection. The agreed value
    /// and condition are frozen at agreement; Revalidate compares the
    /// live asset against them.
    /// </summary>
    [Serializable]
    public sealed class FurnishingContentsLine
    {
        public string AssetId = string.Empty;
        public FurnishingFixtureKind Kind;
        public bool Included;
        public int AgreedValueCents;
        public float Condition01AtAgreement;
        public ExcludedItemDisposition Disposition;
        public string ConditionNote = string.Empty;
    }

    /// <summary>
    /// B6: the itemized contents selection for one property sale. Built
    /// only through BuildAll / BuildNoMovable / BuildCustom so the mode,
    /// the fixture/movable default and the valuation stay consistent.
    /// </summary>
    [Serializable]
    public sealed class PropertyContentsSelection
    {
        public ContentsSelectionMode Mode;
        public int BuildingInstanceId;
        public string SellerOwnerKind = string.Empty;
        public string SellerOwnerId = string.Empty;
        public int AgreedDayIndex;
        public List<FurnishingContentsLine> Lines = new List<FurnishingContentsLine>();
        /// <summary>Eligible items the contract could not classify — never silently dropped, flagged by Validate.</summary>
        public List<string> SkippedUnclassifiedAssetIds = new List<string>();
        /// <summary>Ids the caller asked to include that are not eligible items for this building — flagged by Validate.</summary>
        public List<string> SkippedUnknownAssetIds = new List<string>();
        public int TotalContentsValueCents;
        public int IncludedFixtureValueCents;
        public int IncludedMovableValueCents;
        /// <summary>Included items with no supplied replacement cost — value 0c, not a silent price.</summary>
        public List<string> UnpricedIncludedAssetIds = new List<string>();

        /// <summary>
        /// B6: the read interface D consumes — enumerate the eligible items
        /// associated with one building instance (by building id), each with
        /// stable id, kind, current owner, condition, and removal/custody
        /// state. Only installed bindings in that building are listed;
        /// custody items and other buildings' items are not eligible.
        /// Unclassifiable kinds are listed with Kind Unspecified so the
        /// caller can see them; the builders skip them loudly.
        /// </summary>
        public static List<FurnishingContentsItem> ListEligibleContents(
            HouseholdFurnishingRegister register, int buildingInstanceId)
        {
            var items = new List<FurnishingContentsItem>();
            if (register == null || register.Assets == null) return items;
            foreach (EquipmentAsset asset in register.Assets)
            {
                if (asset == null) continue;
                FurnishingPlacementBinding binding = register.LiveBindingFor(asset.AssetId);
                if (binding == null || binding.BuildingInstanceId != buildingInstanceId) continue;
                items.Add(new FurnishingContentsItem
                {
                    AssetId = asset.AssetId,
                    AssetKind = asset.Kind,
                    Kind = FurnishingClassification.Classify(asset.Kind),
                    OwnerKind = asset.OwnerKind,
                    OwnerId = asset.OwnerId,
                    Condition01 = asset.Condition01,
                    BuildingInstanceId = buildingInstanceId,
                    Installed = true,
                    LocationId = asset.LocationId,
                    IsUsable = asset.IsUsable,
                });
            }
            return items;
        }

        /// <summary>
        /// B6: condition-weighted value for one item (Canon §11.6). An
        /// unusable asset contributes 0c. A kind with no supplied
        /// replacement cost (null func, or a non-positive cost) is
        /// unpriced — priced is false and the value is 0c, never a guess.
        /// </summary>
        public static int ValueCentsFor(
            string assetKind, float condition01,
            Func<string, int> replacementCostCentsForKind, out bool priced)
        {
            priced = false;
            if (condition01 <= 0.05f) return 0;
            if (replacementCostCentsForKind == null) return 0;
            int cost = Math.Max(0, replacementCostCentsForKind(assetKind));
            if (cost <= 0) return 0;
            priced = true;
            return (int)Math.Round(cost * Mathf.Clamp01(condition01));
        }

        /// <summary>B6: every eligible item is included. Unclassifiable items are skipped and recorded.</summary>
        public static PropertyContentsSelection BuildAll(
            List<FurnishingContentsItem> items, string sellerOwnerKind, string sellerOwnerId,
            int buildingInstanceId, int dayIndex, Func<string, int> replacementCostCentsForKind)
        {
            return Build(items, sellerOwnerKind, sellerOwnerId, buildingInstanceId, dayIndex,
                replacementCostCentsForKind, ContentsSelectionMode.All, null);
        }

        /// <summary>B6: fixtures only; every movable stays with the seller (RemovedBySeller).</summary>
        public static PropertyContentsSelection BuildNoMovable(
            List<FurnishingContentsItem> items, string sellerOwnerKind, string sellerOwnerId,
            int buildingInstanceId, int dayIndex, Func<string, int> replacementCostCentsForKind)
        {
            return Build(items, sellerOwnerKind, sellerOwnerId, buildingInstanceId, dayIndex,
                replacementCostCentsForKind, ContentsSelectionMode.NoMovable, null);
        }

        /// <summary>
        /// B6: caller-chosen included set. Every other eligible item is
        /// excluded with the default disposition (movable → RemovedBySeller,
        /// fixture → HeldInPlace). Ids that are not eligible items for this
        /// building are recorded in SkippedUnknownAssetIds, never invented
        /// into lines.
        /// </summary>
        public static PropertyContentsSelection BuildCustom(
            List<FurnishingContentsItem> items, IEnumerable<string> includedAssetIds,
            string sellerOwnerKind, string sellerOwnerId,
            int buildingInstanceId, int dayIndex, Func<string, int> replacementCostCentsForKind)
        {
            return Build(items, sellerOwnerKind, sellerOwnerId, buildingInstanceId, dayIndex,
                replacementCostCentsForKind, ContentsSelectionMode.Custom, includedAssetIds);
        }

        private static PropertyContentsSelection Build(
            List<FurnishingContentsItem> items, string sellerOwnerKind, string sellerOwnerId,
            int buildingInstanceId, int dayIndex, Func<string, int> replacementCostCentsForKind,
            ContentsSelectionMode mode, IEnumerable<string> includedAssetIds)
        {
            var selection = new PropertyContentsSelection
            {
                Mode = mode,
                BuildingInstanceId = buildingInstanceId,
                SellerOwnerKind = sellerOwnerKind ?? string.Empty,
                SellerOwnerId = sellerOwnerId ?? string.Empty,
                AgreedDayIndex = dayIndex,
            };
            var wanted = includedAssetIds != null
                ? new HashSet<string>(includedAssetIds, StringComparer.Ordinal)
                : null;
            if (items != null)
            {
                foreach (FurnishingContentsItem item in items)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.AssetId)) continue;
                    if (item.Kind == FurnishingFixtureKind.Unspecified)
                    {
                        selection.SkippedUnclassifiedAssetIds.Add(item.AssetId);
                        continue;
                    }
                    bool included;
                    switch (mode)
                    {
                        case ContentsSelectionMode.All:
                            included = true;
                            break;
                        case ContentsSelectionMode.NoMovable:
                            included = item.Kind == FurnishingFixtureKind.Fixture;
                            break;
                        default:
                            included = wanted != null && wanted.Contains(item.AssetId);
                            break;
                    }
                    var line = new FurnishingContentsLine
                    {
                        AssetId = item.AssetId,
                        Kind = item.Kind,
                        Included = included,
                        Condition01AtAgreement = item.Condition01,
                    };
                    if (included)
                    {
                        line.AgreedValueCents = ValueCentsFor(
                            item.AssetKind, item.Condition01, replacementCostCentsForKind, out bool priced);
                        if (!priced) selection.UnpricedIncludedAssetIds.Add(item.AssetId);
                        if (!item.IsUsable) line.ConditionNote = "unusable — contributes 0c.";
                        else if (!priced) line.ConditionNote = "no replacement cost supplied — unpriced.";
                    }
                    else
                    {
                        line.AgreedValueCents = 0;
                        line.Disposition = item.Kind == FurnishingFixtureKind.Fixture
                            ? ExcludedItemDisposition.HeldInPlace
                            : ExcludedItemDisposition.RemovedBySeller;
                    }
                    selection.Lines.Add(line);
                }
            }
            if (wanted != null)
            {
                foreach (string id in wanted)
                {
                    bool found = false;
                    foreach (FurnishingContentsLine line in selection.Lines)
                    {
                        if (line != null && line.AssetId == id) { found = true; break; }
                    }
                    if (!found) selection.SkippedUnknownAssetIds.Add(id);
                }
            }
            selection.RecomputeTotals(replacementCostCentsForKind);
            return selection;
        }

        /// <summary>
        /// B6: recompute the totals from the lines. The build path already
        /// valued each line from the live asset kind; this sums the stored
        /// agreed values (the price func is accepted for callers that
        /// re-value, and ignored here — values are frozen at agreement,
        /// and Revalidate, not a recompute, catches staleness).
        /// </summary>
        public void RecomputeTotals(Func<string, int> replacementCostCentsForKind)
        {
            long total = 0;
            long fixture = 0;
            long movable = 0;
            if (Lines != null)
            {
                foreach (FurnishingContentsLine line in Lines)
                {
                    if (line == null || !line.Included) continue;
                    total += line.AgreedValueCents;
                    if (line.Kind == FurnishingFixtureKind.Fixture) fixture += line.AgreedValueCents;
                    else if (line.Kind == FurnishingFixtureKind.Movable) movable += line.AgreedValueCents;
                }
            }
            TotalContentsValueCents = (int)Math.Min(total, int.MaxValue);
            IncludedFixtureValueCents = (int)Math.Min(fixture, int.MaxValue);
            IncludedMovableValueCents = (int)Math.Min(movable, int.MaxValue);
        }

        /// <summary>
        /// B6: agreement-time validation. Empty means the selection may be
        /// agreed. requirePriced refuses included items with no supplied
        /// replacement cost — a closing contract cannot carry an unpriced
        /// item; a negotiation draft validates with requirePriced false.
        /// </summary>
        public List<string> Validate(HouseholdFurnishingRegister register, bool requirePriced)
        {
            var problems = new List<string>();
            if (Mode == ContentsSelectionMode.Unspecified)
                problems.Add("PropertyContentsSelection: mode is unspecified — choose All, No Movable, or Custom.");
            if (string.IsNullOrWhiteSpace(SellerOwnerKind) || string.IsNullOrWhiteSpace(SellerOwnerId))
                problems.Add("PropertyContentsSelection: seller authority is missing — the seller must be named.");
            if (Lines == null || Lines.Count == 0)
                problems.Add("PropertyContentsSelection: no itemized lines — a contents selection with no lines agrees to nothing.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (Lines != null)
            {
                foreach (FurnishingContentsLine line in Lines)
                {
                    if (line == null) { problems.Add("PropertyContentsSelection: null line."); continue; }
                    if (string.IsNullOrWhiteSpace(line.AssetId))
                    {
                        problems.Add("PropertyContentsSelection: line with no asset id.");
                        continue;
                    }
                    if (!seen.Add(line.AssetId))
                    {
                        problems.Add($"PropertyContentsSelection: duplicate line for asset {line.AssetId} — one line per asset.");
                        continue;
                    }
                    if (line.Kind == FurnishingFixtureKind.Unspecified)
                        problems.Add($"PropertyContentsSelection: line for asset {line.AssetId} has no fixture/movable classification.");
                    EquipmentAsset asset = register != null ? register.FindAsset(line.AssetId) : null;
                    if (asset == null)
                    {
                        problems.Add($"PropertyContentsSelection: line asset {line.AssetId} is not in the seller's register.");
                        continue;
                    }
                    if (asset.OwnerKind != SellerOwnerKind || asset.OwnerId != SellerOwnerId)
                        problems.Add($"PropertyContentsSelection: line asset {line.AssetId} is owned by {asset.OwnerKind}:{asset.OwnerId}, not the seller {SellerOwnerKind}:{SellerOwnerId} — the seller has no authority to transfer it.");
                    FurnishingPlacementBinding binding = register.LiveBindingFor(line.AssetId);
                    if (binding == null || binding.BuildingInstanceId != BuildingInstanceId)
                        problems.Add($"PropertyContentsSelection: line asset {line.AssetId} is not installed in building {BuildingInstanceId} — contents cover installed items only.");
                    if (line.AgreedValueCents < 0)
                        problems.Add($"PropertyContentsSelection: line asset {line.AssetId} has a negative agreed value.");
                    if (line.Included)
                    {
                        if (requirePriced && UnpricedIncludedAssetIds != null && UnpricedIncludedAssetIds.Contains(line.AssetId))
                            problems.Add($"PropertyContentsSelection: included asset {line.AssetId} has no supplied replacement cost — the contents value is incomplete.");
                        if (line.Disposition != ExcludedItemDisposition.Unspecified)
                            problems.Add($"PropertyContentsSelection: included asset {line.AssetId} carries an excluded-item disposition — included items have none.");
                    }
                    else
                    {
                        if (line.Disposition == ExcludedItemDisposition.Unspecified)
                            problems.Add($"PropertyContentsSelection: excluded asset {line.AssetId} has no recorded removal/custody disposition.");
                        if (line.AgreedValueCents != 0)
                            problems.Add($"PropertyContentsSelection: excluded asset {line.AssetId} carries an agreed value — excluded items contribute 0c.");
                    }
                }
            }
            if (SkippedUnclassifiedAssetIds != null)
            {
                foreach (string id in SkippedUnclassifiedAssetIds)
                    problems.Add($"PropertyContentsSelection: asset {id} in building {BuildingInstanceId} could not be classified fixture/movable — the selection does not cover the whole building.");
            }
            if (SkippedUnknownAssetIds != null)
            {
                foreach (string id in SkippedUnknownAssetIds)
                    problems.Add($"PropertyContentsSelection: included id {id} is not an eligible item for building {BuildingInstanceId}.");
            }
            if (register != null)
            {
                foreach (FurnishingContentsItem item in ListEligibleContents(register, BuildingInstanceId))
                {
                    if (item == null || item.Kind == FurnishingFixtureKind.Unspecified) continue;
                    bool covered = false;
                    if (Lines != null)
                    {
                        foreach (FurnishingContentsLine line in Lines)
                        {
                            if (line != null && line.AssetId == item.AssetId) { covered = true; break; }
                        }
                    }
                    if (!covered)
                        problems.Add($"PropertyContentsSelection: eligible asset {item.AssetId} installed in building {BuildingInstanceId} after the selection was built — the selection is stale.");
                }
            }
            return problems;
        }

        /// <summary>
        /// B6: re-validation at close. Returns the failures — an included
        /// item that is missing, no longer owned by the seller, removed
        /// from the building, or whose condition changed (agreed value
        /// stale, re-agree). On any failure the deal FAILS LOUDLY and
        /// unwinds; never silently drop an item. Excluded-item drift is
        /// reported in notes, not failures: an excluded item the seller
        /// sold or moved is the seller's own property leaving.
        /// </summary>
        public List<string> Revalidate(HouseholdFurnishingRegister register, out List<string> notes)
        {
            notes = new List<string>();
            var problems = new List<string>();
            if (Lines == null) return problems;
            foreach (FurnishingContentsLine line in Lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.AssetId)) continue;
                EquipmentAsset asset = register != null ? register.FindAsset(line.AssetId) : null;
                if (asset == null)
                {
                    Report(line, $"asset {line.AssetId} no longer exists.", problems, notes);
                    continue;
                }
                if (asset.OwnerKind != SellerOwnerKind || asset.OwnerId != SellerOwnerId)
                {
                    Report(line, $"asset {line.AssetId} is no longer owned by the seller {SellerOwnerKind}:{SellerOwnerId} (now {asset.OwnerKind}:{asset.OwnerId}).", problems, notes);
                    continue;
                }
                FurnishingPlacementBinding binding = register.LiveBindingFor(line.AssetId);
                if (binding == null || binding.BuildingInstanceId != BuildingInstanceId)
                {
                    Report(line, $"asset {line.AssetId} is no longer installed in building {BuildingInstanceId}.", problems, notes);
                    continue;
                }
                if (line.Included && Math.Abs(asset.Condition01 - line.Condition01AtAgreement) > 0.001f)
                {
                    problems.Add($"PropertyContentsSelection: included asset {line.AssetId} condition changed from {line.Condition01AtAgreement:0.000} to {asset.Condition01:0.000} since agreement — the agreed value {line.AgreedValueCents}c is stale, re-agree.");
                }
            }
            if (register != null)
            {
                foreach (FurnishingContentsItem item in ListEligibleContents(register, BuildingInstanceId))
                {
                    if (item == null || item.Kind == FurnishingFixtureKind.Unspecified) continue;
                    bool covered = false;
                    if (Lines != null)
                    {
                        foreach (FurnishingContentsLine line in Lines)
                        {
                            if (line != null && line.AssetId == item.AssetId) { covered = true; break; }
                        }
                    }
                    if (!covered)
                        notes.Add($"PropertyContentsSelection: eligible asset {item.AssetId} was installed in building {BuildingInstanceId} after agreement — it is not part of this selection.");
                }
            }
            return problems;
        }

        private static void Report(
            FurnishingContentsLine line, string detail, List<string> problems, List<string> notes)
        {
            if (line.Included) problems.Add("PropertyContentsSelection: " + detail);
            else notes.Add("PropertyContentsSelection: excluded " + detail);
        }

        /// <summary>
        /// B6: conservation — price == structure + contents, cent for
        /// cent. Returns null when balanced, else a loud string naming
        /// all three numbers. The caller supplies the structure value
        /// and MUST exclude the value of included fixtures from it
        /// (IncludedFixtureValueCents) — otherwise the fixtures are
        /// double-counted. Contents value is never a flat surcharge.
        /// </summary>
        public static string CheckConservation(int structureValueCents, int contentsValueCents, int agreedPriceCents)
        {
            long expected = (long)structureValueCents + contentsValueCents;
            if (agreedPriceCents == expected) return null;
            long diff = (long)agreedPriceCents - expected;
            return $"PropertyContentsSelection: agreed price {agreedPriceCents}c does not equal structure {structureValueCents}c + contents {contentsValueCents}c ({expected}c) — off by {diff}c.";
        }
    }
}
