using System;
using System.Collections.Generic;
using System.Globalization;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// B0 (Agent B, expanded campaign 2026-11-16): closed list of legitimate
    /// acquisition sources for household furniture and equipment (plan §3.2).
    /// Ownership comes ONLY from one of these. Physical placement is never
    /// proof of ownership: installing an item with no source here is refused
    /// loudly, and a placement with no binding is reported as unauthorised,
    /// never silently adopted.
    /// </summary>
    public enum FurnishingAcquisitionSource
    {
        Unspecified = 0,
        ScenarioEndowment = 1,
        Manufacture = 2,
        Purchase = 3,
        Transfer = 4,
        Secondhand = 5,
    }

    /// <summary>
    /// B0: what an installed item enables, by reference — never free text.
    /// The reference id is a workstation definition id or a storage zone id,
    /// depending on the kind. None means the item is placed with no
    /// capability granted yet (B1/B2/B5 set the capability when they bind
    /// sleeping places, stove workstations and storage zones).
    /// </summary>
    public enum FurnishingCapabilityKind
    {
        None = 0,
        SleepingPlace = 1,
        StoveWorkstation = 2,
        DiningSeat = 3,
        StorageZone = 4,
        WorkSurface = 5,
        /// <summary>B4: a dining table — installed, it makes the building's chairs dining seats.</summary>
        DiningTable = 6,
    }

    /// <summary>
    /// B0: one row per installed item, owned by the household register. The
    /// asset id is the single source of identity — the scene representation
    /// and the inventory record are the same item because there is exactly
    /// one asset record and at most one live binding per asset. Moving an
    /// item ends the old binding and creates a new one with the same asset
    /// id; selling transfers ownership and ends the seller's binding; the
    /// buyer installs through the normal install path. Removing an item ends
    /// the binding; the asset stays owned and its LocationId becomes the
    /// household's custody location — never deleted, never duplicated.
    /// </summary>
    [Serializable]
    public sealed class FurnishingPlacementBinding
    {
        public string BindingId = string.Empty;
        public string AssetId = string.Empty;
        public int BuildingInstanceId;
        public string PlacementId = string.Empty;
        public int FloorIndex;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public string DefinitionId = string.Empty;
        public FurnishingCapabilityKind CapabilityKind;
        public string CapabilityReferenceId = string.Empty;
        public FurnishingAcquisitionSource AcquisitionSource;
        public string AcquisitionSourceId = string.Empty;
        public string ReservationId = string.Empty;
    }

    /// <summary>
    /// B0: the household-scoped analogue of BusinessEquipmentRegister — the
    /// holder for a household's owned furniture and equipment assets plus
    /// their placement bindings. Reuses EquipmentAsset as the single asset
    /// authority (identity, kind, condition, owner, location, reservation,
    /// provenance, ownership history, transfer, wear, repair); there is no
    /// second asset class and no second ownership record.
    ///
    /// Identity conventions (B0, documented for the owner to correct):
    /// household ids are the int ids from HouseholdState / HouseholdLedger
    /// (not strings); an asset's OwnerId for a household-owned asset is
    /// that int rendered invariant-culture. LocationId is physical truth:
    /// "building:&lt;instanceId&gt;" while installed, "household:&lt;id&gt;"
    /// while in household custody. Homes start empty unless the scenario
    /// explicitly authors an endowment (O-2 adopted default) — no free
    /// furniture, and install never furnishes a building automatically.
    /// </summary>
    [Serializable]
    public sealed class HouseholdFurnishingRegister
    {
        public int HouseholdId;
        public List<EquipmentAsset> Assets = new List<EquipmentAsset>();
        public List<FurnishingPlacementBinding> Bindings = new List<FurnishingPlacementBinding>();

        public const string OwnerKindHousehold = "household";

        public static string OwnerIdForHousehold(int householdId)
        {
            return householdId.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildingLocationId(int buildingInstanceId)
        {
            return "building:" + buildingInstanceId.ToString(CultureInfo.InvariantCulture);
        }

        public static string HouseholdCustodyLocationId(int householdId)
        {
            return "household:" + householdId.ToString(CultureInfo.InvariantCulture);
        }

        public EquipmentAsset FindAsset(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId) || Assets == null) return null;
            foreach (EquipmentAsset asset in Assets)
            {
                if (asset != null && asset.AssetId == assetId) return asset;
            }
            return null;
        }

        /// <summary>The live binding for one asset, or null when it is not installed. At most one live binding per asset.</summary>
        public FurnishingPlacementBinding LiveBindingFor(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId) || Bindings == null) return null;
            foreach (FurnishingPlacementBinding binding in Bindings)
            {
                if (binding != null && binding.AssetId == assetId) return binding;
            }
            return null;
        }

        /// <summary>
        /// Registers a household-owned asset. Dedupes by asset id (re-registering
        /// replaces, like BusinessEquipmentRegister). Refuses loudly when the
        /// asset is null, has no id, is owned by someone else, or is not a
        /// household asset — two households cannot own the same asset id, and
        /// a borrowed or scene-authored item cannot enter the register without
        /// a legitimate transfer first. Returns null on success.
        /// </summary>
        public string TryRegisterAsset(EquipmentAsset asset)
        {
            if (asset == null)
                return "HouseholdFurnishingRegister.TryRegisterAsset: asset is required.";
            if (string.IsNullOrWhiteSpace(asset.AssetId))
                return "HouseholdFurnishingRegister.TryRegisterAsset: asset id is required.";
            if (asset.OwnerKind != OwnerKindHousehold || asset.OwnerId != OwnerIdForHousehold(HouseholdId))
                return $"HouseholdFurnishingRegister.TryRegisterAsset: asset {asset.AssetId} is owned by {asset.OwnerKind}:{asset.OwnerId}, not household {HouseholdId} — two households cannot own the same asset id.";
            Assets.RemoveAll(a => a != null && a.AssetId == asset.AssetId);
            Assets.Add(asset);
            return null;
        }

        /// <summary>
        /// Removes an asset from the register. Refuses while a live binding
        /// exists — end the binding (remove, move, or transfer) first, so a
        /// binding can never outlive its asset. Returns null on success.
        /// </summary>
        public string TryDeregisterAsset(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId))
                return "HouseholdFurnishingRegister.TryDeregisterAsset: asset id is required.";
            if (FindAsset(assetId) == null)
                return $"HouseholdFurnishingRegister.TryDeregisterAsset: asset {assetId} is not registered to household {HouseholdId}.";
            if (LiveBindingFor(assetId) != null)
                return $"HouseholdFurnishingRegister.TryDeregisterAsset: asset {assetId} is still installed — end its binding (remove, move, or transfer) before deregistering.";
            Assets.RemoveAll(a => a != null && a.AssetId == assetId);
            return null;
        }

        /// <summary>
        /// Installs a registered asset in a building instance: validate, then
        /// create the scene placement record (Configure with the stable binding
        /// id and the furniture.&lt;kind&gt; definition id, AssociateWithBuilding),
        /// create the binding, set the asset LocationId to the building, and
        /// populate the placement's economic owner / acquisition source from
        /// the binding. Install never grants free inventory, never duplicates
        /// the asset, and never furnishes a building automatically.
        /// The placement point is checked walkable and unobstructed when the
        /// placement is not yet associated with the building; a placement
        /// already scene-associated with the building skips the point check
        /// because its own footprint legitimately occupies its point, and
        /// floor reachability for pre-associated placements was validated at
        /// authoring time (Codex checklist). Returns null on success.
        /// </summary>
        public string TryInstall(
            string assetId,
            BuildingEquipmentPlacement placement,
            BuildingFootprintAuthority building,
            int buildingInstanceId,
            int floorIndex,
            FurnishingCapabilityKind capabilityKind,
            string capabilityReferenceId,
            FurnishingAcquisitionSource acquisitionSource,
            string acquisitionSourceId,
            out FurnishingPlacementBinding binding)
        {
            binding = null;
            EquipmentAsset asset = FindAsset(assetId);
            if (asset == null)
                return $"HouseholdFurnishingRegister.TryInstall: asset {assetId} is not registered to household {HouseholdId} — install requires a registered, household-owned asset.";
            if (acquisitionSource == FurnishingAcquisitionSource.Unspecified)
                return $"HouseholdFurnishingRegister.TryInstall: asset {assetId} has no acquisition source — physical placement is never proof of ownership.";
            if (placement == null)
                return "HouseholdFurnishingRegister.TryInstall: placement is required.";
            if (building == null)
                return "HouseholdFurnishingRegister.TryInstall: building authority is required.";
            if (floorIndex < 0)
                return $"HouseholdFurnishingRegister.TryInstall: floor index {floorIndex} is invalid.";
            if (LiveBindingFor(assetId) != null)
                return $"HouseholdFurnishingRegister.TryInstall: asset {assetId} is already installed — use move, not a second install.";
            BuildingFootprintAuthority placementBuilding = placement.BuildingAuthority;
            if (placementBuilding != null && placementBuilding != building)
                return $"HouseholdFurnishingRegister.TryInstall: placement {placement.PlacementId} belongs to a different building instance.";

            Vector3 localPosition;
            Quaternion localRotation;
            if (placementBuilding == building)
            {
                if (!placement.TryGetBuildingLocalPlacement(out localPosition, out localRotation, out BuildingFootprintAuthority resolved)
                    || resolved != building)
                    return $"HouseholdFurnishingRegister.TryInstall: could not resolve the building-local placement for asset {assetId}.";
            }
            else
            {
                localPosition = building.transform.InverseTransformPoint(placement.transform.position);
                localRotation = Quaternion.Inverse(building.transform.rotation) * placement.transform.rotation;
                if (!building.TryEvaluateInteriorLocalPoint(localPosition, floorIndex, null, out string blockedReason))
                    return $"HouseholdFurnishingRegister.TryInstall: placement point for asset {assetId} is not walkable — {blockedReason}";
            }

            var created = new FurnishingPlacementBinding
            {
                BindingId = Guid.NewGuid().ToString("N"),
                AssetId = asset.AssetId,
                BuildingInstanceId = buildingInstanceId,
                PlacementId = string.Empty,
                FloorIndex = floorIndex,
                LocalPosition = localPosition,
                LocalRotation = localRotation,
                DefinitionId = asset.Kind ?? string.Empty,
                CapabilityKind = capabilityKind,
                CapabilityReferenceId = capabilityReferenceId ?? string.Empty,
                AcquisitionSource = acquisitionSource,
                AcquisitionSourceId = acquisitionSourceId ?? string.Empty,
                ReservationId = string.Empty,
            };
            placement.Configure(created.BindingId, created.DefinitionId, asset.DisplayName, floorIndex);
            placement.AssociateWithBuilding(building);
            placement.SetEconomicRecord(asset.OwnerId, created.AcquisitionSourceId);
            created.PlacementId = placement.PlacementId;
            Bindings.Add(created);
            asset.LocationId = BuildingLocationId(buildingInstanceId);
            binding = created;
            return null;
        }

        /// <summary>
        /// Moves an installed asset: ends the old binding and creates a new
        /// one with the same asset id — no duplicate asset, no second
        /// ownership record. The acquisition source carries forward (a move
        /// does not re-acquire the item); passing CapabilityKind.None keeps
        /// the old capability. The caller moves the scene object to its new
        /// position before calling, and passes the retired placement
        /// component when it is a different component from the new one, so
        /// its economic record is cleared. Returns null on success.
        /// </summary>
        public string TryMove(
            string assetId,
            BuildingEquipmentPlacement newPlacement,
            BuildingFootprintAuthority newBuilding,
            int newBuildingInstanceId,
            int newFloorIndex,
            out FurnishingPlacementBinding newBinding,
            FurnishingCapabilityKind capabilityKind = FurnishingCapabilityKind.None,
            string capabilityReferenceId = null,
            BuildingEquipmentPlacement oldPlacement = null)
        {
            newBinding = null;
            FurnishingPlacementBinding oldBinding = LiveBindingFor(assetId);
            if (oldBinding == null)
                return $"HouseholdFurnishingRegister.TryMove: asset {assetId} is not installed — use install, not move.";
            FurnishingAcquisitionSource source = oldBinding.AcquisitionSource;
            string sourceId = oldBinding.AcquisitionSourceId;
            FurnishingCapabilityKind keptCapability = capabilityKind == FurnishingCapabilityKind.None
                ? oldBinding.CapabilityKind : capabilityKind;
            string keptReference = capabilityKind == FurnishingCapabilityKind.None
                ? oldBinding.CapabilityReferenceId : (capabilityReferenceId ?? string.Empty);
            Bindings.Remove(oldBinding);
            string error = TryInstall(assetId, newPlacement, newBuilding, newBuildingInstanceId, newFloorIndex,
                keptCapability, keptReference, source, sourceId, out newBinding);
            if (error != null)
            {
                Bindings.Add(oldBinding);
                EquipmentAsset asset = FindAsset(assetId);
                if (asset != null)
                    asset.LocationId = BuildingLocationId(oldBinding.BuildingInstanceId);
                newBinding = null;
                return error;
            }
            if (oldPlacement != null && oldPlacement != newPlacement)
                oldPlacement.ClearEconomicRecord();
            return null;
        }

        /// <summary>
        /// Removes an installed asset from its building: ends the binding and
        /// returns the asset to household custody. The asset stays owned —
        /// never deleted, never duplicated. The caller retires the placement
        /// component (pass it to clear its economic record). Returns null on
        /// success.
        /// </summary>
        public string TryRemove(string assetId, BuildingEquipmentPlacement placement = null)
        {
            FurnishingPlacementBinding binding = LiveBindingFor(assetId);
            if (binding == null)
                return $"HouseholdFurnishingRegister.TryRemove: asset {assetId} is not installed in household {HouseholdId}.";
            Bindings.Remove(binding);
            if (placement != null)
                placement.ClearEconomicRecord();
            EquipmentAsset asset = FindAsset(assetId);
            if (asset != null)
                asset.LocationId = HouseholdCustodyLocationId(HouseholdId);
            return null;
        }

        /// <summary>
        /// Sells (or otherwise transfers) an asset: transfers title through
        /// EquipmentAsset.TransferOwnership (which refuses a reserved asset)
        /// and ends the seller's binding; the buyer installs through the
        /// normal install path. LocationId is left as-is — the item is still
        /// physically where it is until it is moved; the binding end severs
        /// only the seller's installation record. Returns null on success.
        /// </summary>
        public string TryTransferOwnership(string assetId, string newOwnerKind, string newOwnerId, string reason, int dayIndex)
        {
            EquipmentAsset asset = FindAsset(assetId);
            if (asset == null)
                return $"HouseholdFurnishingRegister.TryTransferOwnership: asset {assetId} is not registered to household {HouseholdId}.";
            string error = asset.TransferOwnership(newOwnerKind, newOwnerId, reason, dayIndex);
            if (error != null)
                return error;
            FurnishingPlacementBinding binding = LiveBindingFor(assetId);
            if (binding != null)
                Bindings.Remove(binding);
            return null;
        }

        /// <summary>
        /// Canon §11.6: condition-weighted transferable value. Assets at or
        /// below the usable threshold contribute nothing. Replacement costs
        /// come from the caller's price data (never invented here); unknown
        /// kinds contribute zero, loudly documented by the caller.
        /// </summary>
        public int TransferableValueCents(Func<string, int> replacementCostCentsForAssetKind)
        {
            long total = 0;
            if (Assets != null)
            {
                foreach (EquipmentAsset asset in Assets)
                {
                    if (asset == null || !asset.IsUsable) continue;
                    int cost = replacementCostCentsForAssetKind != null
                        ? Math.Max(0, replacementCostCentsForAssetKind(asset.Kind)) : 0;
                    total += (long)Math.Round(cost * asset.Condition01);
                }
            }
            return (int)Math.Min(total, int.MaxValue);
        }

        /// <summary>
        /// Canon §11.5: capital replacement need — equipment below half
        /// condition will need capital to replace. Calibration: the 0.5
        /// threshold and the linear (1 - condition) shape are tuning, not canon.
        /// </summary>
        public int ReplacementNeedCents(Func<string, int> replacementCostCentsForAssetKind)
        {
            const float wornThreshold = 0.5f;
            long total = 0;
            if (Assets != null)
            {
                foreach (EquipmentAsset asset in Assets)
                {
                    if (asset == null || asset.Condition01 >= wornThreshold) continue;
                    int cost = replacementCostCentsForAssetKind != null
                        ? Math.Max(0, replacementCostCentsForAssetKind(asset.Kind)) : 0;
                    total += (long)Math.Round(cost * (1f - asset.Condition01));
                }
            }
            return (int)Math.Min(total, int.MaxValue);
        }

        /// <summary>
        /// Structural diagnostics: assets that do not belong to this
        /// household, bindings with no asset, bindings with no acquisition
        /// source, and duplicate live bindings for one asset. Empty means
        /// the register is internally consistent. A stale entry left behind
        /// after a transfer (the seller still holding an asset it no longer
        /// owns) is reported here — the staged sale flow must deregister
        /// from the seller.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (Assets != null)
            {
                foreach (EquipmentAsset asset in Assets)
                {
                    if (asset == null) { problems.Add($"HouseholdFurnishingRegister {HouseholdId}: null asset entry."); continue; }
                    if (string.IsNullOrWhiteSpace(asset.AssetId))
                        problems.Add($"HouseholdFurnishingRegister {HouseholdId}: asset with no id.");
                    if (asset.OwnerKind != OwnerKindHousehold || asset.OwnerId != OwnerIdForHousehold(HouseholdId))
                        problems.Add($"HouseholdFurnishingRegister {HouseholdId}: asset {asset.AssetId} is owned by {asset.OwnerKind}:{asset.OwnerId}, not this household.");
                }
            }
            if (Bindings != null)
            {
                var seenAssets = new HashSet<string>(StringComparer.Ordinal);
                foreach (FurnishingPlacementBinding binding in Bindings)
                {
                    if (binding == null) { problems.Add($"HouseholdFurnishingRegister {HouseholdId}: null binding entry."); continue; }
                    if (string.IsNullOrWhiteSpace(binding.BindingId) || string.IsNullOrWhiteSpace(binding.AssetId)
                        || string.IsNullOrWhiteSpace(binding.PlacementId))
                        problems.Add($"HouseholdFurnishingRegister {HouseholdId}: binding with missing id (binding '{binding.BindingId}', asset '{binding.AssetId}', placement '{binding.PlacementId}').");
                    if (FindAsset(binding.AssetId) == null)
                        problems.Add($"HouseholdFurnishingRegister {HouseholdId}: binding {binding.BindingId} references asset {binding.AssetId}, which is not in this register.");
                    if (binding.AcquisitionSource == FurnishingAcquisitionSource.Unspecified)
                        problems.Add($"HouseholdFurnishingRegister {HouseholdId}: binding {binding.BindingId} has no acquisition source.");
                    if (!string.IsNullOrWhiteSpace(binding.AssetId) && !seenAssets.Add(binding.AssetId))
                        problems.Add($"HouseholdFurnishingRegister {HouseholdId}: asset {binding.AssetId} has more than one live binding.");
                }
            }
            return problems;
        }

        /// <summary>
        /// Reports scene placements with no live binding in this register —
        /// unauthorised placements, never silently adopted — plus placements
        /// whose recorded owner or acquisition source disagrees with their
        /// binding. The caller supplies the placements found in a building
        /// instance; this register judges only its own bindings. Empty means
        /// every supplied placement is accounted for.
        /// </summary>
        public List<string> FindUnauthorisedPlacements(IEnumerable<BuildingEquipmentPlacement> placements)
        {
            var problems = new List<string>();
            if (placements == null) return problems;
            foreach (BuildingEquipmentPlacement placement in placements)
            {
                if (placement == null) continue;
                FurnishingPlacementBinding binding = null;
                if (Bindings != null)
                {
                    foreach (FurnishingPlacementBinding candidate in Bindings)
                    {
                        if (candidate != null && candidate.BindingId == placement.PlacementId)
                        {
                            binding = candidate;
                            break;
                        }
                    }
                }
                if (binding == null)
                {
                    problems.Add($"Unauthorised placement {placement.PlacementId} ({placement.DefinitionId}): no furnishing binding in household {HouseholdId} — physical placement is not proof of ownership.");
                    continue;
                }
                EquipmentAsset asset = FindAsset(binding.AssetId);
                if (asset == null)
                {
                    problems.Add($"Placement {placement.PlacementId}: binding {binding.BindingId} references asset {binding.AssetId}, which is not in household {HouseholdId}.");
                    continue;
                }
                if (placement.EconomicOwnerId != asset.OwnerId)
                    problems.Add($"Placement {placement.PlacementId}: recorded owner '{placement.EconomicOwnerId}' disagrees with bound asset owner '{asset.OwnerId}'.");
                if (placement.AcquisitionSourceId != binding.AcquisitionSourceId)
                    problems.Add($"Placement {placement.PlacementId}: recorded acquisition source '{placement.AcquisitionSourceId}' disagrees with binding '{binding.AcquisitionSourceId}'.");
            }
            return problems;
        }
    }
}
