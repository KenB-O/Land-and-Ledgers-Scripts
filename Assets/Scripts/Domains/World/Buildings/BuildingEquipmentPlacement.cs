using System;
using UnityEngine;

namespace LandLedgers.World
{
    /// <summary>
    /// Scene-instance record for a physical object installed in a building.
    /// This records location and obstruction only; it grants no ownership,
    /// inventory, or business permissions.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Land & Ledgers/World/Building Equipment Placement")]
    public sealed class BuildingEquipmentPlacement : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string placementId;
        [SerializeField] private string definitionId = "furniture.unspecified";
        [SerializeField] private string displayName = "Placed Equipment";
        [SerializeField] private int floorIndex;
        [SerializeField, Min(0f)] private float navigationClearanceMeters = 0.2f;
        [SerializeField, Tooltip("Optional explicit scene-instance association for equipment that is not parented under its building. This is a physical placement link only; it grants no ownership.")]
        private BuildingFootprintAuthority buildingAuthority;
        [SerializeField, Tooltip("Optional external authority references. Blank values do not imply free or business-owned equipment.")]
        private string economicOwnerId = string.Empty;
        [SerializeField, Tooltip("Optional acquisition or opening-endowment record ID.")]
        private string acquisitionSourceId = string.Empty;

        private BuildingFootprintAuthority cachedBuilding;
        private Matrix4x4 lastWorldMatrix;
        private bool hasWorldMatrix;

        public string PlacementId => placementId;
        public string DefinitionId => definitionId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public int FloorIndex => floorIndex;
        public float NavigationClearanceMeters => Mathf.Max(0f, navigationClearanceMeters);
        public BuildingFootprintAuthority BuildingAuthority => ResolveBuildingAuthority();
        public string EconomicOwnerId => economicOwnerId;
        public string AcquisitionSourceId => acquisitionSourceId;

        private void Reset()
        {
            EnsureStableId();
            displayName = name;
            definitionId = name.ToLowerInvariant().Replace(' ', '.');
        }

        private void OnEnable()
        {
            EnsureStableId();
            RefreshBuildingAssociation();
            CaptureWorldMatrix();
        }

        private void OnDisable()
        {
            BuildingFootprintAuthority previous = cachedBuilding;
            cachedBuilding = null;
            if (previous != null) previous.UnregisterEquipmentPlacement(this);
        }

        private void OnValidate()
        {
            navigationClearanceMeters = Mathf.Max(0f, navigationClearanceMeters);
            EnsureStableId();
            RefreshBuildingAssociation();
        }

        private void OnTransformParentChanged() => RefreshBuildingAssociation();

        private void Update()
        {
            Matrix4x4 current = transform.localToWorldMatrix;
            if (hasWorldMatrix && current == lastWorldMatrix) return;
            lastWorldMatrix = current;
            hasWorldMatrix = true;
            RefreshBuildingAssociation();
        }

        private void CaptureWorldMatrix()
        {
            lastWorldMatrix = transform.localToWorldMatrix;
            hasWorldMatrix = true;
        }

        private void EnsureStableId()
        {
            if (!string.IsNullOrWhiteSpace(placementId) || Application.isPlaying) return;
            placementId = Guid.NewGuid().ToString("N");
        }

        private BuildingFootprintAuthority ResolveBuildingAuthority()
        {
            return buildingAuthority != null ? buildingAuthority : GetComponentInParent<BuildingFootprintAuthority>();
        }

        private void RefreshBuildingAssociation()
        {
            BuildingFootprintAuthority resolved = ResolveBuildingAuthority();
            if (cachedBuilding == resolved)
            {
                if (cachedBuilding != null) cachedBuilding.InvalidateEquipmentPlacementCache();
                return;
            }

            BuildingFootprintAuthority previous = cachedBuilding;
            cachedBuilding = resolved;
            if (previous != null) previous.UnregisterEquipmentPlacement(this);
            if (cachedBuilding != null) cachedBuilding.RegisterEquipmentPlacement(this);
        }

        public void Configure(string stableId, string equipmentDefinitionId, string label, int assignedFloor)
        {
            placementId = string.IsNullOrWhiteSpace(stableId) ? Guid.NewGuid().ToString("N") : stableId;
            definitionId = string.IsNullOrWhiteSpace(equipmentDefinitionId) ? "furniture.unspecified" : equipmentDefinitionId;
            displayName = string.IsNullOrWhiteSpace(label) ? name : label;
            floorIndex = assignedFloor;
            RefreshBuildingAssociation();
            CaptureWorldMatrix();
        }

        /// <summary>
        /// Associates this independently placed scene/runtime object with one
        /// building instance. Passing null removes an explicit association;
        /// a parent BuildingFootprintAuthority, if present, remains the fallback.
        /// This does not acquire, transfer, or create the equipment.
        /// </summary>
        public void AssociateWithBuilding(BuildingFootprintAuthority authority)
        {
            buildingAuthority = authority;
            RefreshBuildingAssociation();
        }

        /// <summary>Tests a floor-local point against this object's rendered physical footprint.</summary>
        internal bool BlocksInteriorPoint(BuildingFootprintAuthority building, Vector3 buildingLocalPoint, int requestedFloor)
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy || requestedFloor != floorIndex
                || building == null || ResolveBuildingAuthority() != building) return false;

            Vector3 worldPoint = building.transform.TransformPoint(buildingLocalPoint);
            Vector3 placementPoint = transform.InverseTransformPoint(worldPoint);
            Renderer[] renderers = GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return false;

            float scaleX = transform.TransformVector(Vector3.right).magnitude;
            float scaleZ = transform.TransformVector(Vector3.forward).magnitude;
            float clearanceX = scaleX > 0.0001f ? NavigationClearanceMeters / scaleX : 0f;
            float clearanceZ = scaleZ > 0.0001f ? NavigationClearanceMeters / scaleZ : 0f;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds local = renderer.localBounds;
                Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                for (int x = 0; x <= 1; x++)
                    for (int y = 0; y <= 1; y++)
                        for (int z = 0; z <= 1; z++)
                        {
                            Vector3 corner = new(x == 0 ? local.min.x : local.max.x,
                                y == 0 ? local.min.y : local.max.y,
                                z == 0 ? local.min.z : local.max.z);
                            Vector3 inPlacement = transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                            min = Vector3.Min(min, inPlacement);
                            max = Vector3.Max(max, inPlacement);
                        }

                if (placementPoint.x >= min.x - clearanceX && placementPoint.x <= max.x + clearanceX
                    && placementPoint.z >= min.z - clearanceZ && placementPoint.z <= max.z + clearanceZ)
                    return true;
            }

            return false;
        }

        public bool TryGetBuildingLocalPlacement(out Vector3 localPosition, out Quaternion localRotation,
            out BuildingFootprintAuthority building)
        {
            building = ResolveBuildingAuthority();
            if (building == null || transform == building.transform)
            {
                localPosition = default;
                localRotation = Quaternion.identity;
                return false;
            }

            localPosition = building.transform.InverseTransformPoint(transform.position);
            localRotation = Quaternion.Inverse(building.transform.rotation) * transform.rotation;
            return true;
        }
    }
}
