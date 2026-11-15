using System;
using LandLedgers.World;
using UnityEngine;

namespace LandAndLedgers.Buildings
{
    /// <summary>Stable per-instance placement metadata for a reusable module prefab.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Land & Ledgers/World/Modular Building Placement")]
    public sealed class LLModularBuildingPlacement : MonoBehaviour
    {
        [SerializeField] private string placementId;
        [SerializeField] private string moduleCatalogId;
        [SerializeField] private LLModularBuildingPiece module;
        [SerializeField] private int floorIndex;
        [SerializeField] private Vector2Int gridCell;
        [SerializeField] private Vector3Int quarterTurns;
        private Matrix4x4 lastWorldMatrix;
        private bool hasWorldMatrix;

        public string PlacementId => placementId;
        public string ModuleCatalogId => moduleCatalogId;
        public LLModularBuildingPiece Module => module;
        public int FloorIndex => floorIndex;
        public Vector2Int GridCell => gridCell;
        public Vector3Int QuarterTurns => quarterTurns;

        public void Configure(LLModularBuildingPiece sourceModule, int assignedFloor, Vector2Int cell, Vector3Int axisQuarterTurns)
            => ConfigureStable(sourceModule, assignedFloor, cell, axisQuarterTurns, sourceModule != null ? sourceModule.pieceId : string.Empty, Guid.NewGuid().ToString("N"));

        public void ConfigureStable(LLModularBuildingPiece sourceModule, int assignedFloor, Vector2Int cell,
            Vector3Int axisQuarterTurns, string stableCatalogId, string stablePlacementId)
        {
            // The placement identity is derived from design coordinates so regenerating the same
            // design reconstructs the same structured placement records.
            placementId = string.IsNullOrWhiteSpace(stablePlacementId) ? Guid.NewGuid().ToString("N") : stablePlacementId;
            moduleCatalogId = stableCatalogId ?? string.Empty;
            module = sourceModule;
            floorIndex = Mathf.Max(0, assignedFloor);
            gridCell = cell;
            quarterTurns = new Vector3Int(Normalize(axisQuarterTurns.x), Normalize(axisQuarterTurns.y), Normalize(axisQuarterTurns.z));
            RefreshBuildingGeometry();
            CaptureWorldMatrix();
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(placementId) && !Application.isPlaying)
                placementId = Guid.NewGuid().ToString("N");
            moduleCatalogId ??= string.Empty;
            floorIndex = Mathf.Max(0, floorIndex);
            quarterTurns = new Vector3Int(Normalize(quarterTurns.x), Normalize(quarterTurns.y), Normalize(quarterTurns.z));
            RefreshBuildingGeometry();
        }

        private void OnEnable() => CaptureWorldMatrix();

        private void OnTransformParentChanged()
        {
            hasWorldMatrix = false;
            RefreshBuildingGeometry();
        }

        private void Update()
        {
            Matrix4x4 current = transform.localToWorldMatrix;
            if (hasWorldMatrix && current == lastWorldMatrix) return;
            lastWorldMatrix = current;
            hasWorldMatrix = true;
            RefreshBuildingGeometry();
        }

        public void RefreshBuildingGeometry()
        {
            BuildingFootprintAuthority authority = GetComponentInParent<BuildingFootprintAuthority>();
            if (authority != null) authority.RefreshDerivedInteriorGeometry();
        }

        private void CaptureWorldMatrix()
        {
            lastWorldMatrix = transform.localToWorldMatrix;
            hasWorldMatrix = true;
        }

        private static int Normalize(int value) => ((value % 4) + 4) % 4;
    }
}
