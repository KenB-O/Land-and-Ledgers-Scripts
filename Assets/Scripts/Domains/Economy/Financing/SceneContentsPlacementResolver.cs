using System;
using System.Collections.Generic;
using LandLedgers.World;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// G-2 (W-G4 Workstream 2, expanded campaign 2026-11-16): the
    /// production IContentsPlacementResolver. Resolves a transferred
    /// asset's scene placement by its stable placement id (a Guid,
    /// unique per placement component) and returns the placement with
    /// the building authority it belongs to — the same placement
    /// authority the register-level install uses
    /// (HouseholdFurnishingRegister binds an asset to a
    /// BuildingEquipmentPlacement; the transfer installs through that
    /// same path).
    ///
    /// The buildingInstanceId parameter is NOT independently verifiable
    /// at the scene level: a BuildingFootprintAuthority carries no int
    /// instance id (the manager's BeginPlayerContentsSelection doc
    /// already states "Building carries no int instance id"), and no
    /// int-to-authority registry exists anywhere in the codebase. The
    /// building binding is proven one level up, in
    /// PropertyContentsTransfer.CanTransfer, which checks the seller's
    /// live binding for the asset against the selection's
    /// BuildingInstanceId before the resolver is ever called — a
    /// placement in the wrong building cannot reach this resolver with
    /// a matching binding. This resolver therefore matches on the
    /// placement id alone and returns the placement's own authority as
    /// the building, so the transfer's cross-check
    /// (placement.BuildingAuthority != building) cannot fire on a pair
    /// this resolver produced. The parameter stays in the signature
    /// because the D-7 contract fixes it; inventing a verification for
    /// it would be inventing a mapping.
    ///
    /// The scene enumeration is the caller's job (an injected source):
    /// the manager supplies the live scene placements, tests supply a
    /// list. A resolver with no source cannot resolve, so the source is
    /// required at construction — a null source fails loudly there,
    /// never silently at transfer time.
    /// </summary>
    public sealed class SceneContentsPlacementResolver : IContentsPlacementResolver
    {
        private readonly Func<IEnumerable<BuildingEquipmentPlacement>> placementSource;

        public SceneContentsPlacementResolver(Func<IEnumerable<BuildingEquipmentPlacement>> placementSource)
        {
            if (placementSource == null)
                throw new ArgumentNullException(nameof(placementSource));
            this.placementSource = placementSource;
        }

        public bool TryResolvePlacement(string placementId, int buildingInstanceId,
            out BuildingEquipmentPlacement placement, out BuildingFootprintAuthority building)
        {
            placement = null;
            building = null;
            if (string.IsNullOrWhiteSpace(placementId)) return false;
            foreach (BuildingEquipmentPlacement candidate in placementSource())
            {
                if (candidate == null) continue;
                if (!string.Equals(candidate.PlacementId, placementId, StringComparison.Ordinal)) continue;
                BuildingFootprintAuthority authority = candidate.BuildingAuthority;
                if (authority == null) continue;
                placement = candidate;
                building = authority;
                return true;
            }
            return false;
        }
    }
}
