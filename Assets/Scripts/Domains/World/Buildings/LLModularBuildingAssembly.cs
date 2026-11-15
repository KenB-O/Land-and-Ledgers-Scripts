using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LandLedgers.World;

namespace LandAndLedgers.Buildings
{
    public enum LLBuildingPerimeterSide { North, East, South, West }
    public enum LLBuildingPerimeterType { SolidWall, SingleDoor, DoubleDoor, SingleWindow, MultiWindow, OpenPassage }

    [Serializable]
    public sealed class LLBuildingPerimeterSegment
    {
        public Vector2Int cell;
        public LLBuildingPerimeterSide side;
        public LLBuildingPerimeterType type = LLBuildingPerimeterType.SolidWall;
        public string moduleCatalogId = string.Empty;
        public int floorIndex;

        public string Key => $"{cell.x},{cell.y}:{side}";
    }

    [Serializable]
    public sealed class LLModularBuildingFloor
    {
        public int floorIndex;
        public string displayName = "Ground Floor";
        public float elevationLocal = 0.4f;
        public bool requiresVerticalConnectionFromFloorBelow;
    }

    /// <summary>
    /// Building-level metadata for a composed structure. The root remains one building under
    /// BuildingFootprintAuthority; child modules are physical prefab instances, not economic stock.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Land & Ledgers/World/Modular Building Assembly")]
    public sealed class LLModularBuildingAssembly : MonoBehaviour
    {
        [SerializeField] private string assemblyId = "modular-building";
        [SerializeField] private string sourceFamilyKey = "";
        [SerializeField, Min(0.1f)] private float cellSizeMeters = 2f;
        [SerializeField, Min(0.1f)] private float storeyHeightMeters = 2f;
        [SerializeField] private Vector2Int footprintCells = new(2, 2);
        [SerializeField] private List<Vector2Int> occupiedFootprintCells = new();
        [SerializeField] private List<LLBuildingPerimeterSegment> perimeterSegments = new();
        [SerializeField] private List<LLModularBuildingFloor> floors = new() { new LLModularBuildingFloor() };
        [SerializeField] private bool requireUnscaledModuleRoots = true;
        [SerializeField] private bool requireOneSourceFamily = true;

        public string AssemblyId => assemblyId;
        public string SourceFamilyKey => sourceFamilyKey;
        public float CellSizeMeters => cellSizeMeters;
        public float StoreyHeightMeters => storeyHeightMeters;
        public Vector2Int FootprintCells => footprintCells;
        public IReadOnlyList<LLModularBuildingFloor> Floors => floors;
        public IReadOnlyList<Vector2Int> OccupiedFootprintCells => occupiedFootprintCells;
        public IReadOnlyList<LLBuildingPerimeterSegment> PerimeterSegments => perimeterSegments;

        public void Configure(string id, string family, float cellSize, float storeyHeight,
            Vector2Int footprint, List<LLModularBuildingFloor> authoredFloors)
        {
            assemblyId = string.IsNullOrWhiteSpace(id) ? "modular-building" : id;
            sourceFamilyKey = family ?? string.Empty;
            cellSizeMeters = Mathf.Max(0.1f, cellSize);
            storeyHeightMeters = Mathf.Max(0.1f, storeyHeight);
            footprintCells = new Vector2Int(Mathf.Max(1, footprint.x), Mathf.Max(1, footprint.y));
            floors = authoredFloors ?? new List<LLModularBuildingFloor>();
            occupiedFootprintCells = CreateRectangleCells(footprintCells.x, footprintCells.y);
            perimeterSegments = GetPerimeter(occupiedFootprintCells);
        }

        public void ConfigureLayout(string id, string family, float cellSize, float storeyHeight,
            List<Vector2Int> occupiedCells, List<LLModularBuildingFloor> authoredFloors,
            List<LLBuildingPerimeterSegment> authoredPerimeter)
        {
            if (occupiedCells == null || occupiedCells.Count == 0)
                throw new ArgumentException("A modular footprint must contain at least one occupied cell.", nameof(occupiedCells));
            int width = occupiedCells.Max(cell => cell.x) - occupiedCells.Min(cell => cell.x) + 1;
            int depth = occupiedCells.Max(cell => cell.y) - occupiedCells.Min(cell => cell.y) + 1;
            Configure(id, family, cellSize, storeyHeight, new Vector2Int(width, depth), authoredFloors);
            occupiedFootprintCells = occupiedCells.OrderBy(cell => cell.y).ThenBy(cell => cell.x).ToList();
            perimeterSegments = authoredPerimeter ?? GetPerimeter(occupiedFootprintCells);
        }

        public static List<Vector2Int> CreateRectangleCells(int width, int depth)
        {
            var cells = new List<Vector2Int>(Mathf.Max(1, width) * Mathf.Max(1, depth));
            for (int z = 0; z < Mathf.Max(1, depth); z++)
                for (int x = 0; x < Mathf.Max(1, width); x++) cells.Add(new Vector2Int(x, z));
            return cells;
        }

        public static bool AreCellsConnected(IReadOnlyCollection<Vector2Int> cells)
        {
            if (cells == null || cells.Count == 0) return false;
            var remaining = new HashSet<Vector2Int>(cells);
            if (remaining.Count != cells.Count) return false;
            var visited = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            Vector2Int start = remaining.First();
            queue.Enqueue(start);
            visited.Add(start);
            Vector2Int[] offsets = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                foreach (Vector2Int offset in offsets)
                {
                    Vector2Int next = current + offset;
                    if (remaining.Contains(next) && visited.Add(next)) queue.Enqueue(next);
                }
            }
            return visited.Count == remaining.Count;
        }

        public static List<LLBuildingPerimeterSegment> GetPerimeter(IReadOnlyCollection<Vector2Int> cells)
        {
            var result = new List<LLBuildingPerimeterSegment>();
            if (cells == null) return result;
            var occupied = new HashSet<Vector2Int>(cells);
            foreach (Vector2Int cell in occupied.OrderBy(item => item.y).ThenBy(item => item.x))
            {
                AddIfExterior(result, occupied, cell, LLBuildingPerimeterSide.North, cell + Vector2Int.up);
                AddIfExterior(result, occupied, cell, LLBuildingPerimeterSide.East, cell + Vector2Int.right);
                AddIfExterior(result, occupied, cell, LLBuildingPerimeterSide.South, cell + Vector2Int.down);
                AddIfExterior(result, occupied, cell, LLBuildingPerimeterSide.West, cell + Vector2Int.left);
            }
            return result;
        }

        private static void AddIfExterior(List<LLBuildingPerimeterSegment> result, HashSet<Vector2Int> occupied,
            Vector2Int cell, LLBuildingPerimeterSide side, Vector2Int neighbor)
        {
            if (!occupied.Contains(neighbor)) result.Add(new LLBuildingPerimeterSegment { cell = cell, side = side });
        }

        public static float SnapToCell(float coordinate, float cellSize, float origin = 0f)
        {
            float cell = Mathf.Max(0.1f, cellSize);
            return origin + Mathf.Round((coordinate - origin) / cell) * cell;
        }

        public static Vector3 RotateAxisAlignedBoundsSize(Vector3 size, Vector3Int quarterTurns)
        {
            Quaternion rotation = Quaternion.Euler(new Vector3(quarterTurns.x * 90f, quarterTurns.y * 90f, quarterTurns.z * 90f));
            Vector3 x = rotation * Vector3.right * size.x;
            Vector3 y = rotation * Vector3.up * size.y;
            Vector3 z = rotation * Vector3.forward * size.z;
            return new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        }

        public static float GetFloorModuleCenterY(float walkableSurfaceElevation, float transformedThickness)
            => walkableSurfaceElevation - Mathf.Max(0f, transformedThickness) * 0.5f;

        public bool TryValidate(out List<string> issues)
        {
            issues = new List<string>();
            if (GetComponent<BuildingFootprintAuthority>() == null)
                issues.Add("Building root has no BuildingFootprintAuthority.");
            if (floors == null || floors.Count == 0)
                issues.Add("Assembly has no authored floor elevations.");
            if (footprintCells.x < 1 || footprintCells.y < 1)
                issues.Add("Footprint must contain at least one grid cell in each horizontal axis.");
            if (occupiedFootprintCells != null && occupiedFootprintCells.Count > 0)
            {
                if (!AreCellsConnected(occupiedFootprintCells))
                    issues.Add("Occupied footprint cells must be unique and orthogonally connected.");
                int expectedPerimeterCount = GetPerimeter(occupiedFootprintCells).Count;
                if (perimeterSegments == null || perimeterSegments.Count != expectedPerimeterCount)
                    issues.Add($"Perimeter metadata has {perimeterSegments?.Count ?? 0} segments; the occupied footprint requires {expectedPerimeterCount}.");
                else
                {
                    HashSet<string> expectedKeys = new HashSet<string>(GetPerimeter(occupiedFootprintCells).Select(segment => segment.Key), StringComparer.Ordinal);
                    HashSet<string> actualKeys = new HashSet<string>(perimeterSegments.Where(segment => segment != null).Select(segment => segment.Key), StringComparer.Ordinal);
                    if (!expectedKeys.SetEquals(actualKeys))
                        issues.Add("Perimeter metadata does not match the actual occupied-cell boundary.");
                }
            }

            LLModularBuildingPiece[] pieces = GetComponentsInChildren<LLModularBuildingPiece>(true);
            if (pieces.Length == 0)
                issues.Add("Assembly contains no LLModularBuildingPiece children.");

            LLModularBuildingPlacement[] placements = GetComponentsInChildren<LLModularBuildingPlacement>(true);
            if (placements.Length > 0 && placements.Length != pieces.Length)
                issues.Add($"Assembly has {pieces.Length} module prefabs but {placements.Length} per-instance placement records.");
            if (placements.Length > 0)
            {
                for (int i = 0; i < placements.Length; i++)
                {
                    LLModularBuildingPlacement placement = placements[i];
                    if (placement.Module == null || placement.Module.gameObject != placement.gameObject)
                        issues.Add($"Placement '{placement.name}' does not reference the module on its own prefab instance.");
                    if (!HasFloor(placement.FloorIndex))
                        issues.Add($"Placement '{placement.name}' references missing floor {placement.FloorIndex}.");
                }
            }

            if (!TryValidateModules(pieces, cellSizeMeters, sourceFamilyKey, requireOneSourceFamily,
                    requireUnscaledModuleRoots, transform, out List<string> moduleIssues))
                issues.AddRange(moduleIssues);

            InteriorFloorAuthoring[] authoredFloors = GetComponent<BuildingFootprintAuthority>()?.InteriorFloors?.ToArray();
            if (authoredFloors != null && floors != null)
            {
                for (int i = 0; i < floors.Count; i++)
                {
                    LLModularBuildingFloor floor = floors[i];
                    if (floor == null) continue;
                    bool found = false;
                    for (int f = 0; f < authoredFloors.Length; f++)
                    {
                        if (authoredFloors[f] != null && authoredFloors[f].floorIndex == floor.floorIndex
                            && authoredFloors[f].deriveWalkableFromFloorGeometry)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found) issues.Add($"Floor {floor.floorIndex} has no geometry-derived InteriorFloorAuthoring on BuildingFootprintAuthority.");
                    if (floor.floorIndex > 0 && floor.requiresVerticalConnectionFromFloorBelow)
                        issues.Add($"Floor {floor.floorIndex} requires a stair/vertical path integration; a Stair module alone does not create navigation connectivity.");
                }
            }

            return issues.Count == 0;
        }

        public static bool TryValidateModules(IReadOnlyList<LLModularBuildingPiece> pieces, float assemblyCellSize,
            string requiredFamily, bool requireOneFamily, bool requireUnscaledRoots, Transform assemblyRoot,
            out List<string> issues)
        {
            issues = new List<string>();
            if (pieces == null || pieces.Count == 0)
            {
                issues.Add("No architectural modules were supplied.");
                return false;
            }

            string expectedFamily = requiredFamily ?? string.Empty;
            for (int i = 0; i < pieces.Count; i++)
            {
                LLModularBuildingPiece piece = pieces[i];
                if (piece == null) { issues.Add($"Module {i} is missing its LLModularBuildingPiece metadata."); continue; }
                if (!Mathf.Approximately(piece.cellSizeMeters, assemblyCellSize))
                    issues.Add($"{piece.name}: {piece.cellSizeMeters:F3}m module grid does not match {assemblyCellSize:F3}m assembly grid.");
                if (requireUnscaledRoots && !ApproximatelyOne(piece.transform.localScale))
                    issues.Add($"{piece.name}: root scale is not 1; avoid stacking scale normalization.");
                if (requireOneFamily)
                {
                    string family = piece.extractedFromBuildingName;
                    if (string.IsNullOrWhiteSpace(family)) family = piece.sourceBuildingAssetPath;
                    if (string.IsNullOrWhiteSpace(expectedFamily)) expectedFamily = family;
                    if (string.IsNullOrWhiteSpace(family) || !string.Equals(expectedFamily, family, StringComparison.OrdinalIgnoreCase))
                        issues.Add($"{piece.name}: source family '{(string.IsNullOrWhiteSpace(family) ? "unspecified" : family)}' is not compatible with '{(string.IsNullOrWhiteSpace(expectedFamily) ? "unspecified" : expectedFamily)}'.");
                }
                if (assemblyRoot != null && piece.transform != assemblyRoot && !piece.transform.IsChildOf(assemblyRoot))
                    issues.Add($"{piece.name}: module is outside the assembly root.");
            }
            return issues.Count == 0;
        }

        public bool TryGetFloorElevation(int floorIndex, out float elevation)
        {
            if (floors != null)
            {
                for (int i = 0; i < floors.Count; i++)
                {
                    if (floors[i] == null || floors[i].floorIndex != floorIndex) continue;
                    elevation = floors[i].elevationLocal;
                    return true;
                }
            }
            elevation = 0f;
            return false;
        }

        private bool HasFloor(int floorIndex)
        {
            if (floors == null) return false;
            for (int i = 0; i < floors.Count; i++)
                if (floors[i] != null && floors[i].floorIndex == floorIndex) return true;
            return false;
        }

        private static bool ApproximatelyOne(Vector3 scale)
        {
            return Mathf.Abs(scale.x - 1f) < 0.001f && Mathf.Abs(scale.y - 1f) < 0.001f && Mathf.Abs(scale.z - 1f) < 0.001f;
        }
    }
}
