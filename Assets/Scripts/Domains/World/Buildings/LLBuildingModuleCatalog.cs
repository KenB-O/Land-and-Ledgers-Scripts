using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LandAndLedgers.Buildings
{
    /// <summary>Catalog-facing metadata. The module index is into the catalog's existing Modules list.</summary>
    [Serializable]
    public sealed class LLBuildingModuleCatalogEntry
    {
        public string catalogId = string.Empty;
        public string displayName = string.Empty;
        public string categoryPath = string.Empty;
        public string sourceFamily = string.Empty;
        public string legacyPieceId = string.Empty;
        public string legacySourceKey = string.Empty;
        public string materialVariant = string.Empty;
        public Vector3 measuredRendererBounds;
        public Vector3Int recommendedAxisQuarterTurns;
        public string openingClearance = string.Empty;
        public int moduleIndex = -1;
    }

    /// <summary>
    /// Central categorized index of reusable architectural piece prefabs. Module assets remain at their
    /// source-family paths so extractor regeneration, LOD links and older path references remain valid.
    /// </summary>
    [CreateAssetMenu(fileName = "Building Materials Module Catalog", menuName = "Land & Ledgers/World/Building Module Catalog")]
    public sealed class LLBuildingModuleCatalog : ScriptableObject
    {
        [SerializeField] private string sourceFolder = "Assets/Environment/Building Materials/Auto Extracted";
        [SerializeField] private string libraryFolder = "Assets/Environment/Building Materials/Modules";
        [SerializeField] private List<LLModularBuildingPiece> modules = new();
        [SerializeField] private List<LLBuildingModuleCatalogEntry> entries = new();

        public string SourceFolder => sourceFolder;
        public string LibraryFolder => libraryFolder;
        public IReadOnlyList<LLModularBuildingPiece> Modules => modules;
        public IReadOnlyList<LLBuildingModuleCatalogEntry> Entries => entries;

        public void SetIndex(string folder, List<LLModularBuildingPiece> indexedModules)
        {
            sourceFolder = folder;
            modules = (indexedModules ?? new List<LLModularBuildingPiece>())
                .Where(module => module != null)
                .Distinct()
                .OrderBy(module => module.extractedFromBuildingName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(module => module.name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(module => module.primaryMaterialName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            entries = new List<LLBuildingModuleCatalogEntry>(modules.Count);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < modules.Count; i++)
            {
                LLModularBuildingPiece module = modules[i];
                if (module == null) continue;
                Bounds bounds = GetRendererBounds(module.gameObject);
                string family = ResolveSourceFamily(module);
                string categoryPath = GetCategoryPath(module, bounds);
                string baseId = Slug(family) + "/" + Slug(module.name) + "/" + Slug(module.primaryMaterialName);
                if (string.IsNullOrWhiteSpace(Slug(module.primaryMaterialName)))
                    baseId = Slug(family) + "/" + Slug(module.name);
                string canonicalId = baseId;
                int suffix = 2;
                while (!ids.Add(canonicalId)) canonicalId = baseId + "~" + suffix++;

                Vector3Int adapter = GetRecommendedAxisQuarterTurns(module.category, bounds.size);
                string clearance = IsDoorwayLike(module)
                    ? GetDoorwayOpeningClearance(module.gameObject, bounds)
                    : string.Empty;
                string dimensions = $"{module.gridWidth * module.cellSizeMeters:0.##}×{module.gridDepth * module.cellSizeMeters:0.##}m cell footprint";
                string material = string.IsNullOrWhiteSpace(module.primaryMaterialName) ? "material unspecified" : module.primaryMaterialName;
                string displayName = $"{family} · {categoryPath} · {module.name} · {dimensions} · {material}";

                entries.Add(new LLBuildingModuleCatalogEntry
                {
                    catalogId = canonicalId,
                    displayName = displayName,
                    categoryPath = categoryPath,
                    sourceFamily = family,
                    legacyPieceId = module.pieceId ?? string.Empty,
                    legacySourceKey = module.sourceKey ?? string.Empty,
                    materialVariant = module.primaryMaterialName ?? string.Empty,
                    measuredRendererBounds = bounds.size,
                    recommendedAxisQuarterTurns = adapter,
                    openingClearance = clearance,
                    moduleIndex = i
                });
            }
        }

        public LLModularBuildingPiece ResolveModule(LLBuildingModuleCatalogEntry entry)
        {
            return entry != null && entry.moduleIndex >= 0 && entry.moduleIndex < modules.Count
                ? modules[entry.moduleIndex]
                : null;
        }

        public List<string> GetCategoryPaths()
        {
            return entries.Where(entry => entry != null)
                .Select(entry => entry.categoryPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IEnumerable<LLBuildingModuleCatalogEntry> GetEntries(string categoryPath, string sourceFamily = null)
        {
            return entries.Where(entry => entry != null
                && (string.IsNullOrWhiteSpace(categoryPath) || string.Equals(entry.categoryPath, categoryPath, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(sourceFamily) || string.Equals(entry.sourceFamily, sourceFamily, StringComparison.OrdinalIgnoreCase)));
        }

        public bool TryResolveCatalogId(string catalogId, out LLModularBuildingPiece module)
        {
            LLBuildingModuleCatalogEntry entry = entries.FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.catalogId, catalogId, StringComparison.OrdinalIgnoreCase));
            module = ResolveModule(entry);
            return module != null;
        }

        public bool TryGetCatalogId(LLModularBuildingPiece module, out string catalogId)
        {
            LLBuildingModuleCatalogEntry entry = entries.FirstOrDefault(candidate => candidate != null
                && ResolveModule(candidate) == module);
            catalogId = entry != null ? entry.catalogId : string.Empty;
            return entry != null;
        }

        /// <summary>Legacy IDs are retained as aliases; ambiguous legacy keys intentionally do not pick a winner.</summary>
        public bool TryResolveLegacyPieceId(string legacyId, out LLModularBuildingPiece module)
        {
            module = null;
            if (string.IsNullOrWhiteSpace(legacyId)) return false;
            List<LLBuildingModuleCatalogEntry> matches = entries.Where(entry => entry != null
                && (string.Equals(entry.legacyPieceId, legacyId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.legacySourceKey, legacyId, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matches.Count != 1) return false;
            module = ResolveModule(matches[0]);
            return module != null;
        }

        public List<LLBuildingModuleCatalogEntry> FindLegacyPieceIdMatches(string legacyId)
        {
            if (string.IsNullOrWhiteSpace(legacyId)) return new List<LLBuildingModuleCatalogEntry>();
            return entries.Where(entry => entry != null
                && (string.Equals(entry.legacyPieceId, legacyId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.legacySourceKey, legacyId, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        public static string ResolveSourceFamily(LLModularBuildingPiece module)
        {
            if (module == null) return "Unknown source";
            if (!string.IsNullOrWhiteSpace(module.extractedFromBuildingName)) return module.extractedFromBuildingName;
            if (!string.IsNullOrWhiteSpace(module.sourceBuildingAssetPath))
            {
                string path = module.sourceBuildingAssetPath.Replace('\\', '/');
                string file = path.Substring(path.LastIndexOf('/') + 1);
                int extension = file.LastIndexOf('.');
                return extension > 0 ? file.Substring(0, extension) : file;
            }
            return "Unassigned source";
        }

        public static string GetCategoryPath(LLModularBuildingPiece module)
        {
            if (module == null) return "Unclassified";
            Bounds bounds = GetRendererBounds(module.gameObject);
            return GetCategoryPath(module, bounds);
        }

        private static string GetCategoryPath(LLModularBuildingPiece module, Bounds bounds)
        {
            string identity = (module.name + " " + module.sourceMeshName + " " + module.sourceFamilyKey).ToLowerInvariant();
            bool wallGeometry = IsWallGeometry(bounds.size);
            switch (module.category)
            {
                case LLModuleCategory.Floor: return "Floors";
                case LLModuleCategory.Foundation: return "Foundations";
                case LLModuleCategory.Wall:
                    if (!wallGeometry) return "Other/Wall-Labeled Geometry";
                    if ((identity.Contains("corner") || identity.Contains("angled") || identity.Contains("angle"))
                        && HasCornerProportions(bounds.size))
                        return "Walls/Corners";
                    if (identity.Contains("window")) return "Walls/Windows";
                    if (identity.Contains("door")) return "Walls/Doorways";
                    return "Walls/Solid";
                case LLModuleCategory.Door: return "Doors";
                case LLModuleCategory.Window: return wallGeometry ? "Walls/Windows" : "Windows";
                case LLModuleCategory.Roof: return "Roofs";
                case LLModuleCategory.Stair: return "Stairs";
                case LLModuleCategory.Porch:
                    return identity.Contains("balcony") ? "Balconies" : "Porches";
                case LLModuleCategory.Trim: return "Trim";
                case LLModuleCategory.Ceiling: return "Ceilings";
                case LLModuleCategory.Detail: return "Details";
                default: return "Other";
            }
        }

        public static Vector3Int GetRecommendedAxisQuarterTurns(LLModuleCategory category, Vector3 measuredBounds)
        {
            if (category != LLModuleCategory.Floor) return Vector3Int.zero;
            float smallest = Mathf.Min(measuredBounds.x, Mathf.Min(measuredBounds.y, measuredBounds.z));
            if (measuredBounds.y <= smallest + 0.02f) return Vector3Int.zero;
            if (measuredBounds.z <= smallest + 0.02f) return new Vector3Int(3, 0, 0);
            if (measuredBounds.x <= smallest + 0.02f) return new Vector3Int(0, 0, 1);
            return Vector3Int.zero;
        }

        private static Bounds GetRendererBounds(GameObject root)
        {
            Renderer[] renderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return hasBounds ? bounds : new Bounds(root != null ? root.transform.position : Vector3.zero, Vector3.zero);
        }

        private static bool IsDoorwayLike(LLModularBuildingPiece module)
        {
            string identity = (module.name + " " + module.sourceMeshName + " " + module.sourceFamilyKey).ToLowerInvariant();
            return module.category == LLModuleCategory.Door
                || (identity.Contains("door") && IsWallGeometry(GetRendererBounds(module.gameObject).size));
        }

        private static bool IsWallGeometry(Vector3 size)
        {
            // Source building meshes include both Y-up and Z-up families. Treat walls as planar
            // regardless of source up-axis; assembly adapters handle orientation separately.
            GetSortedDimensions(size, out float longest, out float middle, out float shortest);
            return longest >= 1.2f && middle >= 0.35f && shortest <= 1.1f;
        }

        private static bool HasCornerProportions(Vector3 size)
        {
            GetSortedDimensions(size, out float longest, out float middle, out _);
            return middle >= 1f && longest / Mathf.Max(0.01f, middle) < 3.5f;
        }

        private static void GetSortedDimensions(Vector3 size, out float longest, out float middle, out float shortest)
        {
            float x = Mathf.Abs(size.x);
            float y = Mathf.Abs(size.y);
            float z = Mathf.Abs(size.z);
            longest = Mathf.Max(x, Mathf.Max(y, z));
            shortest = Mathf.Min(x, Mathf.Min(y, z));
            middle = x + y + z - longest - shortest;
        }

        private static string GetDoorwayOpeningClearance(GameObject root, Bounds renderBounds)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0) return "unverified: no collider-defined opening";
            Vector3 size = renderBounds.size;
            bool wallAlongX = size.x >= size.z;
            Vector3 center = renderBounds.center;
            Bounds aperture = wallAlongX
                ? new Bounds(new Vector3(center.x, renderBounds.min.y + 1f, center.z), new Vector3(0.9f, 2f, Mathf.Max(0.1f, size.z + 0.05f)))
                : new Bounds(new Vector3(center.x, renderBounds.min.y + 1f, center.z), new Vector3(Mathf.Max(0.1f, size.x + 0.05f), 2f, 0.9f));
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                if (collider.bounds.Intersects(aperture)) return "blocked: collider overlaps nominal 0.9m × 2.0m opening; requires geometry-specific authoring";
            }
            return "candidate only: no collider overlaps nominal opening; doorway traversal still requires a route test";
        }

        private static string Slug(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            StringBuilder result = new StringBuilder(value.Length);
            bool separator = false;
            foreach (char c in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) { if (separator && result.Length > 0) result.Append('_'); result.Append(c); separator = false; }
                else separator = true;
            }
            return result.ToString().Trim('_');
        }
    }
}
