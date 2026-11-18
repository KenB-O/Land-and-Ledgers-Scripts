using UnityEngine;
using LandLedgers.World;

namespace LandLedgers.Orchestration
{
    /// <summary>Runtime presentation identity; it owns only its optional activity-point cleanup.</summary>
    [DisallowMultipleComponent]
    public sealed class BusinessInteriorFixturePresentation : MonoBehaviour
    {
        [SerializeField] private string businessInstanceId = string.Empty;
        [SerializeField] private string assetId = string.Empty;
        [SerializeField] private string activityLocationId = string.Empty;
        [SerializeField] private BuildingFootprintAuthority building;
        [SerializeField] private bool ownsActivityLocation;

        public string BusinessInstanceId => businessInstanceId;
        public string AssetId => assetId;

        public void Configure(string businessId, string equipmentAssetId, BuildingFootprintAuthority authority, string activityId)
        {
            businessInstanceId = businessId ?? string.Empty;
            assetId = equipmentAssetId ?? string.Empty;
            building = authority;
            activityLocationId = activityId ?? string.Empty;
        }

        public void MarkOwnsActivityLocation() => ownsActivityLocation = true;
        public void ClearActivityLocation() => activityLocationId = string.Empty;

        private void OnDestroy()
        {
            if (ownsActivityLocation && building != null && !string.IsNullOrWhiteSpace(activityLocationId))
                building.UnregisterRuntimeActivityLocation(activityLocationId);
        }
    }
}
