// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent G, slice G-3 (expanded campaign 2026-11-16, W-G4 Workstream 3):
// the production binding for the F4 install leg. F4's service drives a
// purchase through quote -> order -> pay -> ship -> deliver -> receive ->
// install, but its install handler had no production binding (W-G4: the
// handler was referenced only inside FurniturePurchase.cs) and the
// register-level install had no production caller outside D-7's transfer
// leg. This file binds it: the handler registers a received asset in the
// buyer's furnishing register and installs it in the buyer's building
// through HouseholdFurnishingRegister.TryInstall, with the capability
// the registries read (B1 SleepingPlace, B4 DiningTable/DiningSeat, B5
// StorageZone) assigned at install time — no production code ever assigns
// a binding's capability afterwards, so an install without one leaves a
// piece the sleep/dining/storage registries cannot see.
//
// Two entry points share one install core (TryInstallHouseholdAsset):
// the F4 service's handler (a store-bought piece, source Purchase, or a
// secondhand piece, source Secondhand — the seller's binding is ended and
// the asset deregistered from the seller first) and the custody-piece leg
// (TryInstallCustodyPieces: a workshop-sold piece already household-owned
// in the buyer's register, source Manufacture — the G-1 sale leaves it in
// household custody, uninstalled).
//
// Free-placement selection: a scene placement is free for an asset kind
// when it belongs to the buyer's building, its definition is the asset
// kind or the never-configured default ("furniture.unspecified"), and its
// placement id matches no live binding in any hub register. The register
// itself does NOT enforce placement exclusivity (TryInstall checks the
// asset, not the placement), so the bound check is this binding's job —
// without it two assets could share one placement. A placement whose
// definition names a different kind is never adopted (a bed does not go
// on a chair spot). Scene contents are unverifiable from this repo (no
// scene here): the Unity checklist (plan §9.8) asks Codex to verify homes
// contain unbound placements whose definitions match the first-wave kinds
// or are unspecified.

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.World;

namespace LandLedgers.Economy.GenericBusiness
{
    /// <summary>
    /// G-3: the production install binding for household furniture — the
    /// F4 service's install handler, bound to the household furnishing
    /// register, plus the custody-piece install leg for workshop-sold
    /// pieces. Unity-free (plain state only), so it is harness-compiled
    /// and harness-tested; the daily driver (SimulationDrivers) binds the
    /// scene seams (placements, bound check, building authority).
    /// </summary>
    public static class FurnitureInstallBinding
    {
        /// <summary>
        /// The definition id of a placement that was never configured in
        /// the editor — an empty slot, free for any furniture kind. A
        /// placement's Reset sets the definition from its name, and the
        /// scene authoring sets "furniture.&lt;prefabname&gt;"; a placement
        /// that never went through either keeps this default.
        /// </summary>
        public const string UnspecifiedDefinitionId = "furniture.unspecified";

        /// <summary>
        /// The capability a first-wave furniture kind grants when installed
        /// — the capability the deriving registries read (B1 sleeping,
        /// B4 dining, B5 storage). True when the kind is a known first-wave
        /// kind; false (capability None) for anything else, including
        /// cooking-stove: stoves are out of the first wave (F0 — cast iron,
        /// not carpentry), F4's Quote refuses them, and no workshop recipe
        /// makes one, so no stove can reach this binding. A future wave
        /// that adds stoves extends this map deliberately, never silently.
        /// Crate installs with None — placed, no capability granted (B0).
        /// </summary>
        public static bool TryCapabilityForKind(string kind, out FurnishingCapabilityKind capability)
        {
            capability = FurnishingCapabilityKind.None;
            if (string.IsNullOrWhiteSpace(kind)) return false;
            switch (kind.Trim())
            {
                case FurnitureItemSpecs.BedId:
                case FurnitureItemSpecs.BedrollId:
                    capability = FurnishingCapabilityKind.SleepingPlace;
                    return true;
                case FurnitureItemSpecs.TableId:
                    capability = FurnishingCapabilityKind.DiningTable;
                    return true;
                case FurnitureItemSpecs.ChairId:
                    capability = FurnishingCapabilityKind.DiningSeat;
                    return true;
                case FurnitureItemSpecs.CabinetSmallId:
                case FurnitureItemSpecs.CabinetLargeId:
                case FurnitureItemSpecs.ShelfId:
                    capability = FurnishingCapabilityKind.StorageZone;
                    return true;
                case FurnitureItemSpecs.CrateId:
                    capability = FurnishingCapabilityKind.None;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A free scene placement for one asset kind in one building: the
        /// placement belongs to the building, its definition is the asset
        /// kind or the never-configured default, and its placement id
        /// matches no live binding (the caller's bound check — the register
        /// does not enforce placement exclusivity, so this is the guard
        /// against two assets sharing one placement). First match in
        /// scene order wins. False with the reason when no placement
        /// qualifies — never a guessed spot, never a placement from
        /// another building, never an already-bound one.
        /// </summary>
        public static bool TryFindFreePlacement(
            string assetKind,
            BuildingFootprintAuthority building,
            IEnumerable<BuildingEquipmentPlacement> scenePlacements,
            Func<string, bool> placementIsBound,
            out BuildingEquipmentPlacement placement,
            out string refusal)
        {
            placement = null;
            refusal = string.Empty;
            if (string.IsNullOrWhiteSpace(assetKind))
            {
                refusal = "FurnitureInstallBinding: a free placement needs a furniture kind — refusing a placement for nothing.";
                return false;
            }
            if (building == null)
            {
                refusal = "FurnitureInstallBinding: a free placement needs a building authority — furniture is installed in a real building, never nowhere.";
                return false;
            }
            if (scenePlacements == null)
            {
                refusal = "FurnitureInstallBinding: a free placement needs the scene placements — the scene was not supplied.";
                return false;
            }
            if (placementIsBound == null)
            {
                refusal = "FurnitureInstallBinding: a free placement needs the bound check — a placement cannot be proven free without it.";
                return false;
            }
            string wanted = assetKind.Trim();
            foreach (BuildingEquipmentPlacement candidate in scenePlacements)
            {
                if (candidate == null) continue;
                if (!ReferenceEquals(candidate.BuildingAuthority, building)) continue;
                string definition = candidate.DefinitionId;
                bool unspecified = string.IsNullOrWhiteSpace(definition)
                    || string.Equals(definition.Trim(), UnspecifiedDefinitionId, StringComparison.Ordinal);
                if (!unspecified && !string.Equals(definition.Trim(), wanted, StringComparison.Ordinal)) continue;
                if (placementIsBound(candidate.PlacementId ?? string.Empty)) continue;
                placement = candidate;
                return true;
            }
            refusal = $"FurnitureInstallBinding: no free placement for '{wanted}' in this building — every matching spot is bound, belongs to another building, or names a different kind.";
            return false;
        }

        /// <summary>
        /// The shared install core: registers the asset in the household's
        /// furnishing register (re-registering replaces, so an already
        /// registered piece — a G-1 workshop sale — is fine), assigns the
        /// kind's capability, picks a free placement, and installs through
        /// the register. The acquisition source is the caller's: Purchase
        /// for a store buy, Secondhand for a household sale, Manufacture
        /// for a workshop piece. Returns null on success, the refusal
        /// otherwise — nothing is ever marked installed that was not.
        /// </summary>
        public static string TryInstallHouseholdAsset(
            HouseholdFurnishingRegister register,
            EquipmentAsset asset,
            BuildingFootprintAuthority building,
            int buildingInstanceId,
            IEnumerable<BuildingEquipmentPlacement> scenePlacements,
            Func<string, bool> placementIsBound,
            FurnishingAcquisitionSource source,
            string sourceId,
            out FurnishingPlacementBinding binding)
        {
            binding = null;
            if (register == null)
                return "FurnitureInstallBinding: a household furnishing register is required — install needs the household's register.";
            if (asset == null)
                return "FurnitureInstallBinding: an asset is required — nothing to install.";
            if (source == FurnishingAcquisitionSource.Unspecified)
                return $"FurnitureInstallBinding: asset {asset.AssetId} has no acquisition source — physical placement is never proof of ownership.";
            string registerRefusal = register.TryRegisterAsset(asset);
            if (registerRefusal != null)
                return $"FurnitureInstallBinding: asset {asset.AssetId} could not be registered — {registerRefusal}";
            if (!TryCapabilityForKind(asset.Kind, out FurnishingCapabilityKind capability))
                return $"FurnitureInstallBinding: asset {asset.AssetId} is kind '{asset.Kind}', which has no install capability mapped — unknown or out-of-wave kinds are never installed on a guessed capability.";
            if (!TryFindFreePlacement(asset.Kind, building, scenePlacements, placementIsBound,
                    out BuildingEquipmentPlacement placement, out string placementRefusal))
                return placementRefusal;
            string installRefusal = register.TryInstall(asset.AssetId, placement, building, buildingInstanceId,
                placement.FloorIndex, capability, string.Empty, source, sourceId ?? string.Empty, out binding);
            if (installRefusal != null)
                return $"FurnitureInstallBinding: asset {asset.AssetId} install refused — {installRefusal}";
            return null;
        }

        /// <summary>
        /// The custody-piece leg: installs every workshop-made piece the
        /// household owns in custody (registered, uninstalled, usable,
        /// unreserved, MadeByBusinessId set — the G-1 sale's post-sale
        /// state) with source Manufacture and the workshop business as the
        /// source id. A piece that is not workshop-made, already installed,
        /// unusable, or reserved is skipped, never forced. One refusal
        /// never stops the remaining pieces: each refusal is recorded
        /// loudly in the diagnostics and the leg continues. Returns the
        /// installed count.
        /// </summary>
        public static int TryInstallCustodyPieces(
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            int buildingInstanceId,
            IEnumerable<BuildingEquipmentPlacement> scenePlacements,
            Func<string, bool> placementIsBound,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (register == null || register.Assets == null) return 0;
            var custody = new List<EquipmentAsset>(register.Assets);
            int installed = 0;
            foreach (EquipmentAsset asset in custody)
            {
                if (asset == null) continue;
                if (register.LiveBindingFor(asset.AssetId) != null) continue;
                if (string.IsNullOrWhiteSpace(asset.MadeByBusinessId)) continue;
                if (!asset.IsUsable)
                {
                    diag.Add($"FurnitureInstallBinding: custody piece '{asset.AssetId}' ({asset.Kind}) is unusable (condition {asset.Condition01}) — left in custody, never installed worn out.");
                    continue;
                }
                if (asset.IsReserved)
                {
                    diag.Add($"FurnitureInstallBinding: custody piece '{asset.AssetId}' ({asset.Kind}) is reserved by {asset.ReservedBy} — left in custody, never installed out from under its reservation.");
                    continue;
                }
                string refusal = TryInstallHouseholdAsset(register, asset, building, buildingInstanceId,
                    scenePlacements, placementIsBound, FurnishingAcquisitionSource.Manufacture,
                    asset.MadeByBusinessId, out FurnishingPlacementBinding binding);
                if (refusal != null)
                {
                    diag.Add($"FurnitureInstallBinding: custody piece '{asset.AssetId}' ({asset.Kind}) install refused — {refusal}; it stays in household custody.");
                    continue;
                }
                installed++;
                diag.Add($"FurnitureInstallBinding: household {register.HouseholdId} installed workshop piece '{asset.AssetId}' ({asset.Kind}, made by {asset.MadeByBusinessId}) in building {buildingInstanceId} (placement {binding?.PlacementId}, capability {binding?.CapabilityKind}).");
            }
            return installed;
        }
    }

    /// <summary>
    /// G-3: the production binding for F4's InstallPurchasedFurnitureHandler
    /// delegate — the seam F4 left for G. One handler serves one purchase:
    /// it closes over the buyer's register, building, and scene seams, and
    /// the acquisition source (Purchase for a store buy, Secondhand for a
    /// household sale). Manufacture is refused here — workshop pieces do
    /// not go through the F4 service; they install through the custody
    /// leg (FurnitureInstallBinding.TryInstallCustodyPieces).
    ///
    /// Secondhand: the F4 service transfers ownership to the buyer at
    /// receipt, but the seller's register still holds the asset (and its
    /// live binding, when the piece was installed). The handler ends the
    /// seller's binding — through the seller's scene placement, so its
    /// economic record is cleared too — and deregisters the asset from the
    /// seller before registering it with the buyer. When the seller's
    /// placement is not in the scene the binding still ends (the placement
    /// component is not available to clear); the stale economic record is
    /// recorded loudly in the diagnostics, never hidden. When the seller's
    /// register does not hold the asset at all (it was never registered),
    /// there is nothing to deregister and the install proceeds — the F4
    /// service guarantees ownership, not registration.
    /// </summary>
    public sealed class FurniturePurchaseInstallHandler
    {
        private readonly HouseholdFurnishingRegister buyerRegister;
        private readonly BuildingFootprintAuthority building;
        private readonly int buildingInstanceId;
        private readonly IEnumerable<BuildingEquipmentPlacement> scenePlacements;
        private readonly Func<string, bool> placementIsBound;
        private readonly FurnishingAcquisitionSource source;
        private readonly HouseholdFurnishingRegister sellerRegister;
        private readonly List<string> diagnostics;

        public FurniturePurchaseInstallHandler(
            HouseholdFurnishingRegister buyerRegister,
            BuildingFootprintAuthority building,
            int buildingInstanceId,
            IEnumerable<BuildingEquipmentPlacement> scenePlacements,
            Func<string, bool> placementIsBound,
            FurnishingAcquisitionSource source,
            HouseholdFurnishingRegister sellerRegister,
            List<string> diagnostics)
        {
            this.buyerRegister = buyerRegister;
            this.building = building;
            this.buildingInstanceId = buildingInstanceId;
            this.scenePlacements = scenePlacements;
            this.placementIsBound = placementIsBound;
            this.source = source;
            this.sellerRegister = sellerRegister;
            this.diagnostics = diagnostics;
        }

        /// <summary>
        /// Installs one received asset in the buyer's building, exactly as
        /// F4 calls it: once per received asset, only after receipt. Returns
        /// null on success, the refusal otherwise — the purchase stays
        /// Received, never falsely marked installed.
        /// </summary>
        public string Install(EquipmentAsset asset, string purchaseId)
        {
            if (asset == null)
                return "FurniturePurchaseInstallHandler: no asset supplied — nothing to install.";
            if (buyerRegister == null)
                return $"FurniturePurchaseInstallHandler: no buyer register for asset {asset.AssetId} — install needs the household's register.";
            if (source != FurnishingAcquisitionSource.Purchase
                && source != FurnishingAcquisitionSource.Secondhand)
                return $"FurniturePurchaseInstallHandler: source {source} is not a purchase source — workshop pieces install through the custody leg, never through the F4 handler.";
            if (source == FurnishingAcquisitionSource.Secondhand && sellerRegister != null)
            {
                string sellerRefusal = EndSellerHolding(asset);
                if (sellerRefusal != null)
                    return sellerRefusal;
            }
            string refusal = FurnitureInstallBinding.TryInstallHouseholdAsset(buyerRegister, asset, building,
                buildingInstanceId, scenePlacements, placementIsBound, source, purchaseId ?? string.Empty,
                out FurnishingPlacementBinding binding);
            if (refusal != null)
                return refusal;
            diagnostics?.Add($"FurniturePurchaseInstallHandler: purchase '{purchaseId}' installed '{asset.AssetId}' ({asset.Kind}) in building {buildingInstanceId} (placement {binding?.PlacementId}, capability {binding?.CapabilityKind}, source {source}).");
            return null;
        }

        private string EndSellerHolding(EquipmentAsset asset)
        {
            if (sellerRegister.FindAsset(asset.AssetId) == null)
            {
                diagnostics?.Add($"FurniturePurchaseInstallHandler: asset {asset.AssetId} was never registered to the seller household — nothing to deregister; the F4 service guarantees ownership, not registration.");
                return null;
            }
            FurnishingPlacementBinding live = sellerRegister.LiveBindingFor(asset.AssetId);
            if (live != null)
            {
                BuildingEquipmentPlacement sellerPlacement = null;
                if (scenePlacements != null)
                {
                    foreach (BuildingEquipmentPlacement candidate in scenePlacements)
                    {
                        if (candidate != null && string.Equals(candidate.PlacementId, live.PlacementId, StringComparison.Ordinal))
                        {
                            sellerPlacement = candidate;
                            break;
                        }
                    }
                }
                string removeRefusal = sellerRegister.TryRemove(asset.AssetId, sellerPlacement);
                if (removeRefusal != null)
                    return $"FurniturePurchaseInstallHandler: seller's installed asset {asset.AssetId} could not be removed — {removeRefusal}";
                if (sellerPlacement == null)
                {
                    diagnostics?.Add($"FurniturePurchaseInstallHandler: seller's binding for asset {asset.AssetId} ended, but its placement '{live.PlacementId}' is not in the scene — the placement's economic record could not be cleared and stays stale until the scene is repaired.");
                }
            }
            string deregisterRefusal = sellerRegister.TryDeregisterAsset(asset.AssetId);
            if (deregisterRefusal != null)
                return $"FurniturePurchaseInstallHandler: asset {asset.AssetId} could not be deregistered from the seller — {deregisterRefusal}";
            return null;
        }
    }
}
