using System;
using System.Collections.Generic;
using LandLedgers.World;
using LandLedgers.World.Regional;
using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    /// <summary>
    /// Installs the authored physical representations for the static regional
    /// town definitions. It changes only runtime instances: the town prefab,
    /// its GUID, materials, scale, and building children remain authored data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalRegionalTownRuntime : MonoBehaviour
    {
        public const float MetresPerMile = 1609.344f;

        private readonly Dictionary<string, Transform> destinationTransforms =
            new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> destinationBusinessIds =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> townInstances =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        public bool Initialize(GameObject authoredTownPrefab, TownWorldController townWorld)
        {
            if (authoredTownPrefab == null || townWorld == null)
            {
                Debug.LogWarning("[RegionalTowns] Physical regional towns were not installed: the authored town prefab or town-world authority is missing.", this);
                return false;
            }

            destinationTransforms.Clear();
            destinationBusinessIds.Clear();
            int registeredSites = 0;
            for (int townIndex = 0; townIndex < RegionalTownRegistry.All.Length; townIndex++)
            {
                RegionalTownRegistry.RegionalTownDefinition town = RegionalTownRegistry.All[townIndex];
                if (town == null || string.IsNullOrWhiteSpace(town.TownId) || town.Sites == null) continue;
                GameObject townObject = EnsureTownInstance(town, authoredTownPrefab, townWorld);
                if (townObject == null) return false;

                for (int siteIndex = 0; siteIndex < town.Sites.Length; siteIndex++)
                {
                    RegionalTownRegistry.RegionalBusinessSite site = town.Sites[siteIndex];
                    if (!TryGetSiteBinding(town, site, out string locationId, out Vector3 localPosition))
                        return false;
                    Transform building = FindBuilding(townObject.transform, siteIndex + 1);
                    if (building == null)
                    {
                        Debug.LogWarning($"[RegionalTowns] {town.DisplayName} has no authored Building {siteIndex + 1} for business '{site.BusinessId}'.", this);
                        return false;
                    }

                    Collider triggerCollider = building.GetComponent<Collider>();
                    if (triggerCollider == null || !triggerCollider.isTrigger)
                    {
                        Debug.LogWarning($"[RegionalTowns] {town.DisplayName} Building {siteIndex + 1} ('{site.BusinessId}') has no authored trigger collider; destination registration refused.", building);
                        return false;
                    }

                    // The site coordinates are authoritative regional miles.
                    // Position only the runtime instance's cubes; preserve each
                    // authored scale, rotation, mesh, and material.
                    building.localPosition = localPosition;
                    PhysicalRegionalDestinationTrigger arrival =
                        building.GetComponent<PhysicalRegionalDestinationTrigger>()
                        ?? building.gameObject.AddComponent<PhysicalRegionalDestinationTrigger>();
                    arrival.Bind(locationId, site.BusinessId);
                    destinationTransforms[locationId] = building;
                    destinationBusinessIds[locationId] = site.BusinessId;
                    registeredSites++;
                }
            }

            Debug.Log($"[RegionalTowns] Installed {townInstances.Count} authored physical regional towns and {registeredSites} business arrival triggers (1 world unit = 1 metre).", this);
            return true;
        }

        /// <summary>Repositions existing views after world restore without recreating transport or destinations.</summary>
        public void RefreshWorldFrame(TownWorldController townWorld)
        {
            if (townWorld == null) return;
            foreach (var town in RegionalTownRegistry.All)
            {
                if (town == null || !townInstances.TryGetValue(town.TownId, out var instance) || instance == null) continue;
                Vector3 centre = RegionalMilesToWorldPoint(townWorld, town.CentreEastMiles, town.CentreNorthMiles);
                if (instance.transform.position != centre) instance.transform.position = centre;
            }
        }

        public bool TryGetDestination(string locationId, out Transform destination, out string businessId)
        {
            businessId = string.Empty;
            if (string.IsNullOrWhiteSpace(locationId)
                || !destinationTransforms.TryGetValue(locationId, out destination)
                || destination == null)
            {
                destination = null;
                return false;
            }
            destinationBusinessIds.TryGetValue(locationId, out businessId);
            return !string.IsNullOrWhiteSpace(businessId);
        }

        public static bool TryGetSiteBinding(
            RegionalTownRegistry.RegionalTownDefinition town,
            RegionalTownRegistry.RegionalBusinessSite site,
            out string locationId,
            out Vector3 townLocalPosition)
        {
            locationId = string.Empty;
            townLocalPosition = Vector3.zero;
            if (town == null || site == null || string.IsNullOrWhiteSpace(town.TownId)
                || string.IsNullOrWhiteSpace(site.BusinessId))
                return false;

            locationId = RegionalTownRegistry.SiteLocationId(town, site);
            townLocalPosition = new Vector3(
                site.LocalEastMiles * MetresPerMile,
                2.5f,
                site.LocalNorthMiles * MetresPerMile);
            return true;
        }

        public static Vector3 RegionalMilesToWorldPoint(
            TownWorldController townWorld,
            float eastMiles,
            float northMiles,
            float y = 0f)
        {
            if (townWorld == null) return new Vector3(eastMiles * MetresPerMile, y, northMiles * MetresPerMile);
            return townWorld.RegionalPointToSceneWorld(
                new Vector2(eastMiles * MetresPerMile, northMiles * MetresPerMile), y);
        }

        private GameObject EnsureTownInstance(
            RegionalTownRegistry.RegionalTownDefinition town,
            GameObject prefab,
            TownWorldController townWorld)
        {
            Vector3 centre = RegionalMilesToWorldPoint(townWorld,
                town.CentreEastMiles, town.CentreNorthMiles, 0f);
            if (townInstances.TryGetValue(town.TownId, out GameObject existing) && existing != null)
            {
                // A restored world may have a different regional-to-scene offset
                // from the temporary NewGame that created this runtime instance.
                if (existing.transform.position != centre) existing.transform.position = centre;
                return existing;
            }

            var townObject = Instantiate(prefab, centre, Quaternion.identity);
            townObject.name = $"{town.DisplayName} — Physical Regional Town";
            townInstances[town.TownId] = townObject;
            return townObject;
        }

        private static Transform FindBuilding(Transform townRoot, int ordinal)
        {
            if (townRoot == null) return null;
            string expectedName = "Building " + ordinal;
            for (int i = 0; i < townRoot.childCount; i++)
            {
                Transform child = townRoot.GetChild(i);
                if (string.Equals(child.name, expectedName, StringComparison.Ordinal)) return child;
            }
            return null;
        }
    }
}
