using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Population;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// D-7: resolves a contents item's scene placement for the closing
    /// transfer. The seller binding's placement id is the stable key — the
    /// placement component's id is the binding id the install minted
    /// (B0). The driver binds the production implementation (scene lookup
    /// through the building authority's registered placements); tests
    /// fake it. The transfer never invents a placement and never moves
    /// an item whose placement cannot be found — that is a loud
    /// pre-flight failure, not a silent drop.
    /// </summary>
    public interface IContentsPlacementResolver
    {
        bool TryResolvePlacement(string placementId, int buildingInstanceId,
            out BuildingEquipmentPlacement placement, out BuildingFootprintAuthority building);
    }

    /// <summary>
    /// D-7: the outcome of one contents closing transfer. The lists name
    /// what STANDS after the call (post-rollback on failure): on success
    /// every included item stands with the buyer, every excluded movable
    /// stands removed to seller custody, every excluded fixture stands
    /// held in place. On failure with a clean rollback the transferred
    /// and removed lists are empty (the items stand with the seller as
    /// before); on failure with a dirty rollback they name the items
    /// that could NOT be restored, and RollbackProblems says why.
    /// </summary>
    [Serializable]
    public sealed class PropertyContentsTransferResult
    {
        public List<string> TransferredAssetIds = new List<string>();
        public List<string> RemovedBySellerAssetIds = new List<string>();
        public List<string> HeldInPlaceAssetIds = new List<string>();
        public List<string> Problems = new List<string>();
        public List<string> RollbackProblems = new List<string>();
        public List<string> Notes = new List<string>();
        public bool Succeeded => Problems.Count == 0;

        public PropertyContentsTransferResult() { }
    }

    /// <summary>
    /// D-7: the contents closing transfer (plan §4.1, §5). One transfer
    /// function, both flows (NPC and player close call it) — one writer
    /// per item, no duplicate ownership authority. Included items move
    /// seller register → buyer register through the owning authority's
    /// single transfer function (HouseholdFurnishingRegister.
    /// TryTransferOwnership, which refuses a reserved asset) and the
    /// buyer installs through the normal install path, in the same
    /// building, at the same placement, carrying the seller binding's
    /// capability forward (a bed stays a sleeping place, a stove stays
    /// a stove workstation — the item's function in the building does
    /// not change because its owner did). The acquisition source on the
    /// buyer's binding is Purchase with the acquisition record id: the
    /// contents are part of the property purchase, never a free
    /// endowment. Excluded movables are removed by the seller to seller
    /// custody (TryRemove); excluded fixtures stay installed as the
    /// seller's property (HeldInPlace) — the binding, the ownership, and
    /// the placement's economic record are all untouched. Conservation
    /// holds through the transfer: the agreed values are frozen at
    /// agreement, Revalidate fails the close on any included-item
    /// condition change, and the close re-checks price == structure +
    /// contents with the close-time structure value (NPC flow).
    ///
    /// Failure: CanTransfer pre-flights the whole selection before any
    /// mutation (reserved asset, unresolvable placement, buyer-register
    /// conflict all fail the deal before money moves). TransferAtClose
    /// re-runs the pre-flight, then executes line by line; on any
    /// mid-transfer failure it rolls the completed items back in
    /// reverse order (ownership back, re-register, re-install from the
    /// captured seller binding) and reports exactly what moved and
    /// what did not. A rollback restores an EQUIVALENT binding (a new
    /// binding id is minted on re-install); the asset id is the stable
    /// identity, and no other record references a binding id.
    ///
    /// Stove fuel (B2): the fuel in a stove was never priced in the
    /// contents, so it does not transfer to the buyer. Before a stove
    /// leaves the seller's register its fuel returns to the seller's
    /// reserve (TryReturnStoveFuelToReserve) — the sale flow must
    /// settle it before the asset leaves the register, or the seller's
    /// stove registry carries orphaned fuel. The stove transfers empty;
    /// the seller keeps their firewood. When no seller stove registry
    /// is wired the settlement is skipped with a loud note (the fuel
    /// becomes orphaned in the seller's registry, visible in its
    /// Validate, retrievable later — never silently lost); the driver
    /// must wire the registries (G wiring).
    ///
    /// Excluded-item drift is a note, never a failure: an excluded item
    /// the seller already sold or moved is the seller's own property
    /// leaving, and the transfer does not touch what is no longer the
    /// seller's. A removal whose placement cannot be resolved still
    /// ends the binding (the item is out of the building) and notes
    /// that the placement's economic record could not be cleared — the
    /// driver retires the component.
    /// </summary>
    public static class PropertyContentsTransfer
    {
        private enum TransferStage
        {
            Captured = 0,
            OwnershipTransferred = 1,
            DeregisteredFromSeller = 2,
            RegisteredToBuyer = 3,
            InstalledToBuyer = 4,
            RemovedFromSeller = 5,
        }

        private sealed class CompletedTransfer
        {
            public string AssetId = string.Empty;
            public EquipmentAsset Asset;
            public FurnishingPlacementBinding SellerBinding;
            public BuildingEquipmentPlacement Placement;
            public BuildingFootprintAuthority Building;
            public TransferStage Stage = TransferStage.Captured;
            public int FuelUnitsReturned;
            public bool WasRemoval;
        }

        /// <summary>
        /// D-7: pre-flight — can this selection transfer, as it stands?
        /// No mutation. Empty means the transfer can run. Excluded-item
        /// drift is reported in notes, not problems.
        /// </summary>
        public static List<string> CanTransfer(
            PropertyContentsSelection selection,
            HouseholdFurnishingRegister seller,
            HouseholdFurnishingRegister buyer,
            IContentsPlacementResolver resolver,
            out List<string> notes)
        {
            notes = new List<string>();
            var problems = new List<string>();
            if (selection == null)
            {
                problems.Add("PropertyContentsTransfer: no contents selection — nothing to transfer.");
                return problems;
            }
            if (seller == null)
            {
                problems.Add("PropertyContentsTransfer: no seller furnishing register — the transfer has no source.");
                return problems;
            }
            if (buyer == null)
            {
                problems.Add("PropertyContentsTransfer: no buyer furnishing register — the transfer has nowhere to install the contents.");
                return problems;
            }
            if (resolver == null)
            {
                problems.Add("PropertyContentsTransfer: no placement resolver — the transfer cannot find the scene placements.");
                return problems;
            }
            if (selection.Lines == null || selection.Lines.Count == 0)
            {
                problems.Add("PropertyContentsTransfer: the selection has no lines — a contents selection with no lines transfers nothing.");
                return problems;
            }
            foreach (FurnishingContentsLine line in selection.Lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.AssetId))
                {
                    problems.Add("PropertyContentsTransfer: selection line with no asset id.");
                    continue;
                }
                EquipmentAsset asset = seller.FindAsset(line.AssetId);
                if (line.Included)
                {
                    if (asset == null)
                    {
                        problems.Add($"PropertyContentsTransfer: included asset {line.AssetId} is not in the seller's register.");
                        continue;
                    }
                    if (asset.OwnerKind != selection.SellerOwnerKind || asset.OwnerId != selection.SellerOwnerId)
                    {
                        problems.Add($"PropertyContentsTransfer: included asset {line.AssetId} is owned by {asset.OwnerKind}:{asset.OwnerId}, not the seller {selection.SellerOwnerKind}:{selection.SellerOwnerId} — the seller has no authority to transfer it.");
                        continue;
                    }
                    if (asset.IsReserved)
                    {
                        problems.Add($"PropertyContentsTransfer: included asset {line.AssetId} is reserved by {asset.ReservedBy} — a reserved asset cannot transfer.");
                        continue;
                    }
                    FurnishingPlacementBinding binding = seller.LiveBindingFor(line.AssetId);
                    if (binding == null || binding.BuildingInstanceId != selection.BuildingInstanceId)
                    {
                        problems.Add($"PropertyContentsTransfer: included asset {line.AssetId} is not installed in building {selection.BuildingInstanceId}.");
                        continue;
                    }
                    if (buyer.FindAsset(line.AssetId) != null)
                    {
                        problems.Add($"PropertyContentsTransfer: included asset {line.AssetId} is already registered to the buyer — two households cannot own the same asset id.");
                        continue;
                    }
                    if (buyer.LiveBindingFor(line.AssetId) != null)
                    {
                        problems.Add($"PropertyContentsTransfer: included asset {line.AssetId} is already installed in the buyer's register.");
                        continue;
                    }
                    if (!resolver.TryResolvePlacement(binding.PlacementId, selection.BuildingInstanceId,
                        out BuildingEquipmentPlacement placement, out BuildingFootprintAuthority building)
                        || placement == null || building == null)
                    {
                        problems.Add($"PropertyContentsTransfer: included asset {line.AssetId} placement {binding.PlacementId} cannot be resolved — the buyer cannot install through the normal install path.");
                        continue;
                    }
                    BuildingFootprintAuthority placementBuilding = placement.BuildingAuthority;
                    if (placementBuilding != null && placementBuilding != building)
                    {
                        problems.Add($"PropertyContentsTransfer: included asset {line.AssetId} placement {binding.PlacementId} belongs to a different building instance — the buyer cannot install it here.");
                    }
                }
                else
                {
                    if (line.Disposition == ExcludedItemDisposition.Unspecified)
                    {
                        problems.Add($"PropertyContentsTransfer: excluded asset {line.AssetId} has no recorded removal/custody disposition — an excluded item is never silently left behind.");
                        continue;
                    }
                    if (asset == null)
                    {
                        notes.Add($"PropertyContentsTransfer: excluded asset {line.AssetId} is already gone from the seller's register — nothing to remove or hold.");
                        continue;
                    }
                    if (asset.OwnerKind != selection.SellerOwnerKind || asset.OwnerId != selection.SellerOwnerId)
                    {
                        notes.Add($"PropertyContentsTransfer: excluded asset {line.AssetId} is no longer owned by the seller — it is not removed by this transfer.");
                        continue;
                    }
                    if (line.Disposition == ExcludedItemDisposition.RemovedBySeller)
                    {
                        FurnishingPlacementBinding binding = seller.LiveBindingFor(line.AssetId);
                        if (binding == null)
                        {
                            notes.Add($"PropertyContentsTransfer: excluded asset {line.AssetId} is already out of the building — nothing to remove.");
                            continue;
                        }
                        if (!resolver.TryResolvePlacement(binding.PlacementId, selection.BuildingInstanceId,
                            out BuildingEquipmentPlacement ignoredPlacement, out BuildingFootprintAuthority ignoredBuilding))
                        {
                            notes.Add($"PropertyContentsTransfer: excluded asset {line.AssetId} placement {binding.PlacementId} cannot be resolved — the binding still ends, but the placement's economic record cannot be cleared; the driver retires the component.");
                        }
                    }
                }
            }
            return problems;
        }

        /// <summary>
        /// D-7: executes the closing transfer. Runs CanTransfer first —
        /// on pre-flight failure nothing has mutated and the problems are
        /// returned. acquisitionSourceId is the acquisition record id
        /// (e.g. "property-acquisition:plan-1") stamped on the buyer's
        /// bindings. sellerStoves / sellerHousehold settle stove fuel
        /// (null = settlement skipped with a loud note; see the class
        /// doc). diag collects the transfer's own record; it is never
        /// null on return.
        /// </summary>
        public static PropertyContentsTransferResult TransferAtClose(
            PropertyContentsSelection selection,
            HouseholdFurnishingRegister seller,
            HouseholdFurnishingRegister buyer,
            IContentsPlacementResolver resolver,
            string acquisitionSourceId,
            int dayIndex,
            HouseholdStoveRegistry sellerStoves,
            HouseholdState sellerHousehold,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new PropertyContentsTransferResult();
            List<string> problems = CanTransfer(selection, seller, buyer, resolver, out List<string> notes);
            result.Notes.AddRange(notes);
            foreach (string note in notes)
                diag.Add("PropertyContentsTransfer: " + note);
            if (problems.Count > 0)
            {
                result.Problems.AddRange(problems);
                return result;
            }

            var completed = new List<CompletedTransfer>();
            foreach (FurnishingContentsLine line in selection.Lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.AssetId)) continue;
                string problem;
                if (line.Included)
                {
                    problem = TransferIncludedLine(selection, seller, buyer, resolver, line,
                        acquisitionSourceId, dayIndex, sellerStoves, sellerHousehold, diag, completed, result);
                }
                else if (line.Disposition == ExcludedItemDisposition.RemovedBySeller)
                {
                    problem = RemoveExcludedLine(selection, seller, resolver, line,
                        sellerStoves, sellerHousehold, diag, completed, result);
                }
                else
                {
                    // HeldInPlace: the fixture stays installed as the
                    // seller's property — binding, ownership, and the
                    // placement's economic record are all untouched.
                    result.HeldInPlaceAssetIds.Add(line.AssetId);
                    diag.Add($"PropertyContentsTransfer: excluded fixture {line.AssetId} held in place as the seller's property in building {selection.BuildingInstanceId}.");
                    continue;
                }
                if (problem != null)
                {
                    result.Problems.Add(problem);
                    break;
                }
            }
            if (result.Problems.Count > 0)
            {
                Rollback(completed, seller, buyer, dayIndex, sellerStoves, sellerHousehold, diag, result);
            }
            return result;
        }

        private static string TransferIncludedLine(
            PropertyContentsSelection selection,
            HouseholdFurnishingRegister seller,
            HouseholdFurnishingRegister buyer,
            IContentsPlacementResolver resolver,
            FurnishingContentsLine line,
            string acquisitionSourceId,
            int dayIndex,
            HouseholdStoveRegistry sellerStoves,
            HouseholdState sellerHousehold,
            List<string> diag,
            List<CompletedTransfer> completed,
            PropertyContentsTransferResult result)
        {
            EquipmentAsset asset = seller.FindAsset(line.AssetId);
            FurnishingPlacementBinding sellerBinding = seller.LiveBindingFor(line.AssetId);
            if (asset == null || sellerBinding == null)
                return $"PropertyContentsTransfer: included asset {line.AssetId} vanished between the pre-flight and the transfer.";
            var captured = new CompletedTransfer
            {
                AssetId = line.AssetId,
                Asset = asset,
                SellerBinding = CloneBinding(sellerBinding),
                Stage = TransferStage.Captured,
            };
            completed.Add(captured);

            string fuelProblem = SettleStoveFuel(line.AssetId, asset, sellerStoves, sellerHousehold, diag, out int fuelReturned);
            if (fuelProblem != null)
                return fuelProblem;
            captured.FuelUnitsReturned = fuelReturned;

            if (!resolver.TryResolvePlacement(sellerBinding.PlacementId, selection.BuildingInstanceId,
                out BuildingEquipmentPlacement placement, out BuildingFootprintAuthority building)
                || placement == null || building == null)
                return $"PropertyContentsTransfer: included asset {line.AssetId} placement {sellerBinding.PlacementId} cannot be resolved at transfer time.";
            captured.Placement = placement;
            captured.Building = building;

            // Ownership through the single authority, then the register
            // moves, then the buyer installs through the normal install
            // path — same building, same placement, the seller binding's
            // capability carried forward.
            string transferProblem = seller.TryTransferOwnership(line.AssetId,
                HouseholdFurnishingRegister.OwnerKindHousehold,
                HouseholdFurnishingRegister.OwnerIdForHousehold(buyer.HouseholdId),
                $"property acquisition — contents transfer at closing (source {acquisitionSourceId})", dayIndex);
            if (transferProblem != null)
                return $"PropertyContentsTransfer: included asset {line.AssetId} ownership transfer refused — {transferProblem}";
            captured.Stage = TransferStage.OwnershipTransferred;

            // The staged sale flow must deregister from the seller (B0):
            // a seller still holding a transferred asset is a stale entry.
            string deregisterProblem = seller.TryDeregisterAsset(line.AssetId);
            if (deregisterProblem != null)
                return $"PropertyContentsTransfer: included asset {line.AssetId} could not be deregistered from the seller — {deregisterProblem}";
            captured.Stage = TransferStage.DeregisteredFromSeller;

            string registerProblem = buyer.TryRegisterAsset(asset);
            if (registerProblem != null)
                return $"PropertyContentsTransfer: included asset {line.AssetId} could not be registered to the buyer — {registerProblem}";
            captured.Stage = TransferStage.RegisteredToBuyer;

            string installProblem = buyer.TryInstall(line.AssetId, placement, building,
                selection.BuildingInstanceId, sellerBinding.FloorIndex,
                sellerBinding.CapabilityKind, sellerBinding.CapabilityReferenceId,
                FurnishingAcquisitionSource.Purchase, acquisitionSourceId ?? string.Empty,
                out FurnishingPlacementBinding buyerBinding);
            if (installProblem != null)
                return $"PropertyContentsTransfer: included asset {line.AssetId} could not be installed for the buyer — {installProblem}";
            captured.Stage = TransferStage.InstalledToBuyer;

            result.TransferredAssetIds.Add(line.AssetId);
            diag.Add($"PropertyContentsTransfer: included asset {line.AssetId} transferred to household {buyer.HouseholdId}, installed in building {selection.BuildingInstanceId} (capability {sellerBinding.CapabilityKind}).");
            return null;
        }

        private static string RemoveExcludedLine(
            PropertyContentsSelection selection,
            HouseholdFurnishingRegister seller,
            IContentsPlacementResolver resolver,
            FurnishingContentsLine line,
            HouseholdStoveRegistry sellerStoves,
            HouseholdState sellerHousehold,
            List<string> diag,
            List<CompletedTransfer> completed,
            PropertyContentsTransferResult result)
        {
            EquipmentAsset asset = seller.FindAsset(line.AssetId);
            FurnishingPlacementBinding sellerBinding = seller.LiveBindingFor(line.AssetId);
            // Excluded drift (already gone, or already the seller's own
            // property leaving) is a note at close, not a failure — the
            // pre-flight already recorded it; there is nothing to do.
            if (asset == null
                || asset.OwnerKind != selection.SellerOwnerKind || asset.OwnerId != selection.SellerOwnerId
                || sellerBinding == null)
            {
                result.Notes.Add($"PropertyContentsTransfer: excluded asset {line.AssetId} already left the seller's building — nothing removed by this transfer.");
                return null;
            }
            var captured = new CompletedTransfer
            {
                AssetId = line.AssetId,
                Asset = asset,
                SellerBinding = CloneBinding(sellerBinding),
                Stage = TransferStage.Captured,
                WasRemoval = true,
            };
            completed.Add(captured);

            // A stove leaving the register settles its fuel first, even
            // on the removal path (a stove is a fixture, so this is a
            // contradiction case — settle it anyway rather than orphan it).
            string fuelProblem = SettleStoveFuel(line.AssetId, asset, sellerStoves, sellerHousehold, diag, out int fuelReturned);
            if (fuelProblem != null)
                return fuelProblem;
            captured.FuelUnitsReturned = fuelReturned;

            BuildingEquipmentPlacement placement = null;
            if (resolver.TryResolvePlacement(sellerBinding.PlacementId, selection.BuildingInstanceId,
                out BuildingEquipmentPlacement resolved, out BuildingFootprintAuthority resolvedBuilding)
                && resolved != null)
            {
                placement = resolved;
                captured.Placement = resolved;
                captured.Building = resolvedBuilding;
            }
            else
            {
                diag.Add($"PropertyContentsTransfer: excluded asset {line.AssetId} placement {sellerBinding.PlacementId} cannot be resolved — the binding still ends, but the placement's economic record cannot be cleared; the driver retires the component.");
            }

            string removeProblem = seller.TryRemove(line.AssetId, placement);
            if (removeProblem != null)
                return $"PropertyContentsTransfer: excluded asset {line.AssetId} could not be removed by the seller — {removeProblem}";
            captured.Stage = TransferStage.RemovedFromSeller;

            result.RemovedBySellerAssetIds.Add(line.AssetId);
            diag.Add($"PropertyContentsTransfer: excluded movable {line.AssetId} removed by the seller to household {seller.HouseholdId} custody.");
            return null;
        }

        /// <summary>
        /// D-7: stove fuel settlement — the fuel in a stove returns to
        /// the seller's reserve before the stove leaves the register.
        /// Returns null on success (or when there is nothing to settle),
        /// else the loud reason. fuelUnitsReturned is the amount moved
        /// (0 when the stove was empty or no registry was wired).
        /// </summary>
        private static string SettleStoveFuel(
            string assetId, EquipmentAsset asset,
            HouseholdStoveRegistry sellerStoves, HouseholdState sellerHousehold,
            List<string> diag, out int fuelUnitsReturned)
        {
            fuelUnitsReturned = 0;
            if (asset == null || !string.Equals(asset.Kind, HouseholdStoveRegistry.StoveAssetKind, StringComparison.Ordinal))
                return null;
            if (sellerStoves == null || sellerHousehold == null)
            {
                diag.Add($"PropertyContentsTransfer: stove {assetId} fuel settlement skipped — no seller stove registry wired; any fuel in the stove becomes orphaned in the seller's registry until the driver settles it (G wiring).");
                return null;
            }
            fuelUnitsReturned = Math.Max(0, sellerStoves.FuelWoodUnitsOnHand(assetId));
            string problem = sellerStoves.TryReturnStoveFuelToReserve(assetId, sellerHousehold, diag);
            if (problem != null)
                return $"PropertyContentsTransfer: stove {assetId} fuel could not be settled — {problem}";
            if (fuelUnitsReturned > 0)
                diag.Add($"PropertyContentsTransfer: returned {fuelUnitsReturned} fuel-wood unit(s) from stove {assetId} to the seller's reserve — the stove transfers empty; the fuel was never priced in the contents.");
            return null;
        }

        private static void Rollback(
            List<CompletedTransfer> completed,
            HouseholdFurnishingRegister seller,
            HouseholdFurnishingRegister buyer,
            int dayIndex,
            HouseholdStoveRegistry sellerStoves,
            HouseholdState sellerHousehold,
            List<string> diag,
            PropertyContentsTransferResult result)
        {
            for (int i = completed.Count - 1; i >= 0; i--)
            {
                CompletedTransfer item = completed[i];
                string problem = RollbackOne(item, seller, buyer, dayIndex, sellerStoves, sellerHousehold, diag);
                if (problem != null)
                {
                    result.RollbackProblems.Add(problem);
                    diag.Add("PropertyContentsTransfer: ROLLBACK INCOMPLETE — " + problem);
                }
                else
                {
                    result.TransferredAssetIds.Remove(item.AssetId);
                    result.RemovedBySellerAssetIds.Remove(item.AssetId);
                    diag.Add($"PropertyContentsTransfer: rolled back {item.AssetId} to the seller.");
                }
            }
        }

        private static string RollbackOne(
            CompletedTransfer item,
            HouseholdFurnishingRegister seller,
            HouseholdFurnishingRegister buyer,
            int dayIndex,
            HouseholdStoveRegistry sellerStoves,
            HouseholdState sellerHousehold,
            List<string> diag)
        {
            FurnishingPlacementBinding captured = item.SellerBinding;
            if (captured == null)
                return $"PropertyContentsTransfer: cannot roll back {item.AssetId} — no captured seller binding.";
            if (item.WasRemoval)
            {
                if (item.Stage == TransferStage.Captured)
                    // The removal never ran — but a stove's fuel may
                    // already have been returned to the reserve; re-feed
                    // it so the stove stands exactly as before.
                    return RefuelRolledBackStove(item, sellerStoves, sellerHousehold, diag);
                if (item.Stage != TransferStage.RemovedFromSeller) return null;
                if (item.Placement == null || item.Building == null)
                    return $"PropertyContentsTransfer: cannot roll back removed asset {item.AssetId} — its placement was never resolved.";
                string installProblem = seller.TryInstall(item.AssetId, item.Placement, item.Building,
                    captured.BuildingInstanceId, captured.FloorIndex,
                    captured.CapabilityKind, captured.CapabilityReferenceId,
                    captured.AcquisitionSource, captured.AcquisitionSourceId,
                    out FurnishingPlacementBinding restored);
                if (installProblem != null)
                    return $"PropertyContentsTransfer: removed asset {item.AssetId} could not be re-installed for the seller — {installProblem}";
                return RefuelRolledBackStove(item, sellerStoves, sellerHousehold, diag);
            }

            switch (item.Stage)
            {
                case TransferStage.Captured:
                    // Nothing left the seller — but a stove's fuel may
                    // already have been returned to the reserve; re-feed
                    // it so the stove stands exactly as before.
                    return RefuelRolledBackStove(item, sellerStoves, sellerHousehold, diag);
                case TransferStage.OwnershipTransferred:
                    // Buyer-owned, still in the seller's register, no
                    // binding: transfer it back through the seller's
                    // register, then re-install from the captured binding.
                    string backProblem = seller.TryTransferOwnership(item.AssetId,
                        HouseholdFurnishingRegister.OwnerKindHousehold,
                        HouseholdFurnishingRegister.OwnerIdForHousehold(seller.HouseholdId),
                        "contents transfer rolled back", dayIndex);
                    if (backProblem != null)
                        return $"PropertyContentsTransfer: asset {item.AssetId} could not be transferred back to the seller — {backProblem}";
                    break;
                case TransferStage.DeregisteredFromSeller:
                    // Buyer-owned, in no register: the asset authority
                    // itself is the only handle — transfer it back, then
                    // re-register and re-install.
                    string directProblem = item.Asset.TransferOwnership(
                        HouseholdFurnishingRegister.OwnerKindHousehold,
                        HouseholdFurnishingRegister.OwnerIdForHousehold(seller.HouseholdId),
                        "contents transfer rolled back", dayIndex);
                    if (directProblem != null)
                        return $"PropertyContentsTransfer: asset {item.AssetId} could not be transferred back to the seller — {directProblem}";
                    string registerProblem = seller.TryRegisterAsset(item.Asset);
                    if (registerProblem != null)
                        return $"PropertyContentsTransfer: asset {item.AssetId} could not be re-registered to the seller — {registerProblem}";
                    break;
                case TransferStage.RegisteredToBuyer:
                case TransferStage.InstalledToBuyer:
                    // Buyer-owned in the buyer's register: transfer it
                    // back through the buyer's register (which ends the
                    // buyer's binding), deregister from the buyer,
                    // re-register to the seller.
                    string buyerBackProblem = buyer.TryTransferOwnership(item.AssetId,
                        HouseholdFurnishingRegister.OwnerKindHousehold,
                        HouseholdFurnishingRegister.OwnerIdForHousehold(seller.HouseholdId),
                        "contents transfer rolled back", dayIndex);
                    if (buyerBackProblem != null)
                        return $"PropertyContentsTransfer: asset {item.AssetId} could not be transferred back to the seller — {buyerBackProblem}";
                    string buyerDeregisterProblem = buyer.TryDeregisterAsset(item.AssetId);
                    if (buyerDeregisterProblem != null)
                        return $"PropertyContentsTransfer: asset {item.AssetId} could not be deregistered from the buyer — {buyerDeregisterProblem}";
                    string sellerRegisterProblem = seller.TryRegisterAsset(item.Asset);
                    if (sellerRegisterProblem != null)
                        return $"PropertyContentsTransfer: asset {item.AssetId} could not be re-registered to the seller — {sellerRegisterProblem}";
                    break;
                default:
                    return $"PropertyContentsTransfer: cannot roll back {item.AssetId} — unknown transfer stage {(int)item.Stage}.";
            }

            if (item.Placement == null || item.Building == null)
                return $"PropertyContentsTransfer: asset {item.AssetId} is back with the seller but cannot be re-installed — its placement was never resolved.";
            string reinstallProblem = seller.TryInstall(item.AssetId, item.Placement, item.Building,
                captured.BuildingInstanceId, captured.FloorIndex,
                captured.CapabilityKind, captured.CapabilityReferenceId,
                captured.AcquisitionSource, captured.AcquisitionSourceId,
                out FurnishingPlacementBinding restoredBinding);
            if (reinstallProblem != null)
                return $"PropertyContentsTransfer: asset {item.AssetId} is back with the seller but could not be re-installed — {reinstallProblem}";
            return RefuelRolledBackStove(item, sellerStoves, sellerHousehold, diag);
        }

        private static string RefuelRolledBackStove(
            CompletedTransfer item,
            HouseholdStoveRegistry sellerStoves,
            HouseholdState sellerHousehold,
            List<string> diag)
        {
            if (item.FuelUnitsReturned <= 0) return null;
            if (sellerStoves == null || sellerHousehold == null)
                return $"PropertyContentsTransfer: stove {item.AssetId} rolled back empty — its {item.FuelUnitsReturned} fuel-wood unit(s) stay in the seller's reserve (no stove registry wired to re-feed it).";
            string feedProblem = sellerStoves.TryFeedStove(item.AssetId, item.FuelUnitsReturned, sellerHousehold, diag);
            if (feedProblem != null)
                return $"PropertyContentsTransfer: stove {item.AssetId} rolled back but its {item.FuelUnitsReturned} fuel-wood unit(s) could not be re-fed — {feedProblem} (the fuel stays in the seller's reserve).";
            return null;
        }

        private static FurnishingPlacementBinding CloneBinding(FurnishingPlacementBinding binding)
        {
            if (binding == null) return null;
            return new FurnishingPlacementBinding
            {
                BindingId = binding.BindingId,
                AssetId = binding.AssetId,
                BuildingInstanceId = binding.BuildingInstanceId,
                PlacementId = binding.PlacementId,
                FloorIndex = binding.FloorIndex,
                LocalPosition = binding.LocalPosition,
                LocalRotation = binding.LocalRotation,
                DefinitionId = binding.DefinitionId,
                CapabilityKind = binding.CapabilityKind,
                CapabilityReferenceId = binding.CapabilityReferenceId,
                AcquisitionSource = binding.AcquisitionSource,
                AcquisitionSourceId = binding.AcquisitionSourceId,
                ReservationId = binding.ReservationId,
            };
        }
    }
}
