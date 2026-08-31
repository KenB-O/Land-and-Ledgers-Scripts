using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace LandLedgers.World
{
    public enum RegionalTerrainRuntimeQualityPreset
    {
        Custom = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    [DisallowMultipleComponent]
    public sealed class RegionalTerrainTileView : MonoBehaviour
    {
        private const string TileRootName = "Regional Terrain Tiles";
        private const string WaterRootName = "Regional Watercourses";
        private const int FallbackLayerCount = 4;
        private const string GeneratedTerrainPrefix = "Regional Terrain ";
        private const string GeneratedWaterPrefix = "Regional Watercourse ";
        private const string RouteRootName = "Regional Route Evidence";
        private const string ScenicRootName = "Regional Scenic Vegetation";
        private const string GeneratedRoutePrefix = "Regional Route Corridor ";
        private const string GeneratedScenicPrefix = "Regional Scenic ";
        private const string SettlementEvidenceRootName = "Regional Settlement Evidence";
        private const string ParcelEvidenceRootName = "Regional Parcel Evidence";
        private const string GeneratedSettlementEvidencePrefix = "Regional Settlement Evidence ";
        private const string GeneratedParcelEvidencePrefix = "Regional Parcel Evidence ";

        [Header("Runtime Quality Preset")]
        [Tooltip("Applies a safe runtime cost profile over the detailed settings below. Custom leaves the authored values untouched.")]
        [SerializeField] private RegionalTerrainRuntimeQualityPreset runtimeQualityPreset = RegionalTerrainRuntimeQualityPreset.Medium;
        [Tooltip("When enabled, preset caps are applied only during Play Mode so editor-authored values remain visible and tunable.")]
        [SerializeField] private bool applyQualityPresetOnlyAtRuntime = true;
        [Tooltip("Keeps expensive evidence layers disabled when a lower preset is selected, while preserving the underlying regional data.")]
        [SerializeField] private bool qualityPresetControlsEvidenceLayers = true;

        [SerializeField] private TownWorldController townWorld;
        [SerializeField] private Transform tileRoot;
        [SerializeField, Min(17)] private int heightmapResolution = 33;
        [SerializeField, Min(1f)] private float heightScaleMeters = 48f;
        [SerializeField] private bool buildOnStart;
        [Tooltip("Prevents Start/bootstrap refreshes from clearing and rebuilding the same terrain twice in one Play Mode frame.")]
        [SerializeField] private bool skipDuplicateRuntimeRebuildInSameFrame = true;
        [SerializeField] private bool useOpeningTownLocalSpace = true;
        [Tooltip("Editor terrain creation can trigger Unity culling warnings. Leave this off unless you explicitly want an edit-mode terrain preview.")]
        [SerializeField] private bool allowEditorTerrainRebuild;
        [SerializeField] private bool logSkippedEditorTerrainRebuild = true;
        [SerializeField] private bool buildOnlyTilesNearOpeningAnchor = true;
        [SerializeField, Min(64f)] private float openingAnchorTileRadiusMeters = 1280f;
        [SerializeField] private Material terrainMaterial;

        [Header("Regional Evidence Inspection")]
        [Tooltip("Keeps runtime inspection summaries compact enough for the console while still naming the strongest generated evidence records.")]
        [SerializeField, Range(1, 12)] private int evidenceInspectionDigestItems = 5;
        [SerializeField, Min(32f)] private float evidenceInspectionSearchRadiusMeters = 640f;
        [SerializeField] private bool includeParcelAuthorityInEvidenceInspection = true;

        [Header("Runtime Fallback Assets")]
        [Tooltip("Creates temporary in-memory materials, layers, textures, and primitive evidence props when authored assets have not been assigned yet. This keeps the regional world playable while final art is still being wired.")]
        [SerializeField] private bool createRuntimeFallbackAssets = true;
        [Tooltip("Assigns a simple runtime Terrain material when Terrain Material is empty. Instanced terrain stays disabled for runtime fallback materials to avoid HDRP green-sheet rendering issues.")]
        [SerializeField] private bool assignRuntimeTerrainMaterialWhenMissing = true;
        [SerializeField] private bool logRuntimeFallbackAssets;
        [SerializeField] private Color runtimeTerrainMaterialColor = new(0.38f, 0.43f, 0.30f, 1f);
        [SerializeField] private GameObject scenicTreePrefab;
        [SerializeField] private GameObject scenicScrubPrefab;
        [SerializeField] private GameObject scenicWetReedPrefab;
        [SerializeField] private GameObject scenicRockPrefab;
        [SerializeField] private GameObject scenicGrassClumpPrefab;
        [SerializeField] private GameObject settlementEvidencePrefab;
        [SerializeField] private GameObject parcelStakePrefab;

        [Header("Terrain Shape")]
        [Tooltip("Raises very low serialized heightmap settings to a useful runtime preview resolution without forcing scene authors to update older components by hand.")]
        [SerializeField] private bool upgradeLowRuntimeHeightmapResolution = true;
        [SerializeField, Min(17)] private int minimumRuntimeHeightmapResolution = 65;
        [Tooltip("Applies a conservative vertical multiplier to the generated regional heightfield. Keep this below 1 until buildings and roads become fully height-aware.")]
        [SerializeField, Range(0.05f, 1f)] private float terrainVerticalStrength = 0.38f;
        [Tooltip("Prevents the terrain from looking completely flat when an older serialized component still has a very low vertical strength.")]
        [SerializeField, Min(0f)] private float minimumVisibleReliefMeters = 18f;
        [SerializeField, Range(0f, 1f)] private float broadReliefWeight = 0.46f;
        [SerializeField, Range(0f, 1f)] private float ridgeReliefWeight = 0.22f;
        [SerializeField, Range(0f, 1f)] private float detailReliefWeight = 0.16f;
        [SerializeField, Range(0f, 1f)] private float highlandGradientWeight = 0.18f;
        [SerializeField, Range(0f, 1f)] private float watercourseCutStrength = 0.30f;
        [SerializeField, Range(0f, 1f)] private float waterBankCutStrength = 0.16f;
        [SerializeField, Range(0f, 1f)] private float routeGradeStrength = 0.055f;
        [SerializeField, Range(0f, 1f)] private float routeShoulderStrength = 0.035f;
        [SerializeField, Range(0f, 1f)] private float settlementShelfStrength = 0.10f;
        [Tooltip("Applies gentle grading to high-readiness town/edge parcels without turning the wider terrain into a flat build surface.")]
        [SerializeField, Range(0f, 1f)] private float parcelPadGradeStrength = 0.075f;
        [Tooltip("Blend distance outside town/edge parcel bounds where grading feathers back into natural terrain.")]
        [SerializeField, Min(1f)] private float parcelPadShoulderMeters = 34f;
        [SerializeField, Range(0f, 1f)] private float parcelPadDirtStrength = 0.28f;
        [SerializeField, Range(0, 4)] private int heightSmoothingPasses = 1;
        [SerializeField] private bool forceValidUnityTerrainResolution = true;

        [Header("Terrain Painting")]
        [Tooltip("Optional authored terrain layers. If empty, the runtime view creates simple in-memory grass/dirt/wetland/rock layers so the generated terrain has readable material variation.")]
        [SerializeField] private TerrainLayer[] terrainLayers;
        [SerializeField] private bool generateRuntimeFallbackLayers = true;
        [SerializeField] private bool paintTerrainAlphamaps = true;
        [SerializeField, Range(16, 512)] private int alphamapResolution = 96;
        [SerializeField] private Color fallbackGrassColor = new(0.34f, 0.42f, 0.28f, 1f);
        [SerializeField] private Color fallbackRoadDirtColor = new(0.58f, 0.43f, 0.25f, 1f);
        [SerializeField] private Color fallbackWetlandColor = new(0.18f, 0.31f, 0.25f, 1f);
        [SerializeField] private Color fallbackRockColor = new(0.42f, 0.39f, 0.33f, 1f);
        [Tooltip("Keep off by default for HDRP/runtime-generated terrain unless a tested HDRP TerrainLit material/layer setup is assigned. The default instanced path can render as large green sheets on some projects.")]
        [SerializeField] private bool drawInstancedTerrain;

        [Header("Terrain Details / Ground Dressing")]
        [SerializeField] private bool paintRuntimeGrassDetails = true;
        [SerializeField, Range(32, 1024)] private int detailResolution = 256;
        [SerializeField, Range(4, 64)] private int detailResolutionPerPatch = 16;
        [SerializeField, Range(0, 16)] private int maxGrassDensity = 5;
        [SerializeField, Min(0.01f)] private float grassMinHeightMeters = 0.35f;
        [SerializeField, Min(0.01f)] private float grassMaxHeightMeters = 0.9f;
        [SerializeField] private Color runtimeGrassHealthyColor = new(0.36f, 0.48f, 0.25f, 1f);
        [SerializeField] private Color runtimeGrassDryColor = new(0.46f, 0.40f, 0.23f, 1f);

        [Header("Opening Town Height Alignment")]
        [Tooltip("Offsets generated terrain so the opening town anchor shelf sits at Opening Town Target Y instead of floating above the flat town grid.")]
        [SerializeField] private bool normalizeOpeningTownHeight = true;
        [SerializeField] private float openingTownTargetY = 0f;
        [Tooltip("Terrain samples inside this radius are flattened toward the opening town anchor height before the global Y offset is applied.")]
        [SerializeField, Min(0f)] private float openingTownFlattenRadiusMeters = 220f;
        [Tooltip("Additional distance over which the town shelf blends back into regional terrain.")]
        [SerializeField, Min(1f)] private float openingTownBlendRadiusMeters = 420f;
        [SerializeField] private bool logTerrainBuildSummary = true;
        [SerializeField] private bool logRuntimeTerrainBuildTiming = true;

        [Header("Watercourses")]
        [SerializeField] private bool buildWatercourseMeshes = true;
        [Tooltip("Authored river/water prefab used as the primary visible watercourse representation. When empty, generated ribbon meshes are used as the safe fallback.")]
        [SerializeField] private GameObject authoredRiverPrefab;
        [SerializeField] private bool useAuthoredRiverPrefab = true;
        [Tooltip("Keeps the generated ribbon mesh visible in addition to authored river prefab segments. Normally leave this off so debug/fallback water does not replace the authored water.")]
        [SerializeField] private bool buildFallbackWaterMeshWithAuthoredRiverPrefab;
        [Tooltip("When the authored river prefab is missing, generated fallback ribbon meshes stay disabled by default because a bad material or oversized ribbon can obscure the town. Enable only for deliberate debug previews.")]
        [SerializeField] private bool buildFallbackWaterMeshesWhenAuthoredRiverPrefabMissing;
        [SerializeField] private bool warnWhenAuthoredRiverPrefabMissing = true;
        [Tooltip("Expected width of one authored river prefab segment before parent scaling is applied.")]
        [SerializeField, Min(0.1f)] private float authoredRiverPrefabNominalWidthMeters = 10f;
        [Tooltip("Expected length of one authored river prefab segment before parent scaling is applied.")]
        [SerializeField, Min(0.1f)] private float authoredRiverPrefabNominalLengthMeters = 10f;
        [Tooltip("Long generated watercourse edges are split into repeated authored prefab segments no longer than this.")]
        [SerializeField, Min(1f)] private float authoredRiverSegmentMaxLengthMeters = 96f;
        [SerializeField] private Vector3 authoredRiverPrefabEulerOffset;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Color runtimeWaterColor = new(0.12f, 0.34f, 0.48f, 0.88f);
        [SerializeField, Min(1f)] private float waterRibbonMinWidthMeters = 8f;
        [SerializeField, Min(1f)] private float waterRibbonMaxWidthMeters = 48f;
        [SerializeField, Range(0.05f, 1.25f)] private float waterRibbonWidthMultiplier = 0.45f;
        [SerializeField] private float waterSurfaceOffsetMeters = 0.08f;
        [SerializeField] private bool addWaterMeshColliders;

        [Header("Route / Freight Evidence")]
        [SerializeField] private bool buildRouteCorridorMeshes = true;
        [SerializeField] private Material routeMaterial;
        [SerializeField] private Color runtimeRouteColor = new(0.52f, 0.38f, 0.20f, 0.72f);
        [SerializeField, Min(1f)] private float routeRibbonMinWidthMeters = 6f;
        [SerializeField, Min(1f)] private float routeRibbonMaxWidthMeters = 34f;
        [SerializeField, Range(0.05f, 1.25f)] private float routeRibbonWidthMultiplier = 0.42f;
        [SerializeField] private float routeSurfaceOffsetMeters = 0.055f;
        [SerializeField] private bool routeRibbonReceivesShadows;
        [SerializeField] private bool suppressRouteRibbonsInsideOpeningTown = true;
        [SerializeField, Min(0f)] private float routeOpeningTownExclusionPaddingMeters = 36f;

        [Header("Scenic Vegetation Evidence")]
        [Tooltip("Builds cheap runtime vegetation props from regional vegetation zones. These are visual evidence only; future logging should use data-backed tree/worksite records, not these props as authority.")]
        [SerializeField] private bool buildScenicVegetationProps = true;
        [SerializeField, Range(0, 800)] private int maxScenicPropsPerRebuild = 180;
        [SerializeField, Min(8f)] private float scenicPropSpacingMeters = 82f;
        [SerializeField, Range(0f, 1f)] private float scenicPropDensityMultiplier = 0.62f;
        [SerializeField, Min(0f)] private float scenicPropAvoidTownRadiusMeters = 95f;
        [SerializeField, Range(0f, 1f)] private float scenicPropAvoidRouteInfluence = 0.42f;
        [SerializeField, Range(0f, 1f)] private float scenicPropAvoidParcelPadInfluence = 0.55f;
        [SerializeField, Min(0.01f)] private float scenicPropMinScale = 0.55f;
        [SerializeField, Min(0.01f)] private float scenicPropMaxScale = 1.35f;
        [SerializeField] private Color scenicTreeTrunkColor = new(0.28f, 0.18f, 0.10f, 1f);
        [SerializeField] private Color scenicTreeFoliageColor = new(0.24f, 0.38f, 0.18f, 1f);
        [SerializeField] private Color scenicScrubColor = new(0.31f, 0.34f, 0.17f, 1f);
        [SerializeField] private Color scenicWetReedColor = new(0.35f, 0.39f, 0.20f, 1f);
        [SerializeField] private Color scenicRockColor = new(0.34f, 0.32f, 0.28f, 1f);

        [Header("Settlement / Parcel Evidence")]
        [Tooltip("Builds small, cheap markers for generated settlement nodes so regional settlement data has visible world evidence before full building-out exists.")]
        [SerializeField] private bool buildSettlementEvidenceProps = true;
        [SerializeField, Range(0, 64)] private int maxSettlementEvidenceProps = 24;
        [SerializeField] private float settlementEvidenceSurfaceOffsetMeters = 0.06f;
        [SerializeField] private Color settlementEvidenceColor = new(0.70f, 0.55f, 0.32f, 1f);
        [Tooltip("Builds simple survey stakes on high-readiness parcels near the active region. These are visual evidence only and do not make parcels owned, listed, or purchasable.")]
        [SerializeField] private bool buildParcelBoundaryStakes = true;
        [SerializeField, Range(0, 256)] private int maxParcelBoundaryStakes = 72;
        [SerializeField] private bool parcelBoundaryStakesOnlyNearOpeningAnchor = true;
        [SerializeField, Min(0.1f)] private float parcelStakeHeightMeters = 1.25f;
        [SerializeField, Min(0.01f)] private float parcelStakeRadiusMeters = 0.06f;
        [SerializeField] private float parcelStakeSurfaceOffsetMeters = 0.05f;
        [SerializeField] private Color parcelStakeColor = new(0.62f, 0.46f, 0.25f, 1f);
        [Tooltip("Hard cap for runtime active terrain tile creation. Protects play mode from accidentally building a very large regional map at once.")]
        [SerializeField, Range(1, 256)] private int maxRuntimeTilesPerRebuild = 64;

        [Header("Runtime Surface / Collider")]
        [Tooltip("Ensures generated Unity Terrain tiles have TerrainCollider components assigned to the generated TerrainData.")]
        [SerializeField] private bool ensureTerrainCollider = true;
        [Tooltip("Registers the first generated TerrainCollider with TownWorldController so systems that used the old ground quad can resolve the generated terrain surface at runtime.")]
        [SerializeField] private bool registerFirstTerrainColliderWithTownWorld = true;
        [Tooltip("Keeps the generated terrain tile parent at world origin with identity rotation/scale so tiles do not inherit offsets from the authoring object and appear to jump to 0,0 incorrectly.")]
        [SerializeField] private bool forceTileRootWorldIdentity = true;
        [Tooltip("Detaches the generated terrain root from the authoring object when forcing world identity. This avoids inherited scale/rotation turning Terrain tiles into vertical sheets or snapping them around scene origin.")]
        [SerializeField] private bool detachTileRootWhenForcingIdentity = true;
        [SerializeField] private bool logTilePlacementDetails;
        [Tooltip("When true, Clear Regional Terrain Tiles only removes generated terrain/water children instead of wiping every child under the assigned root. Keep this on if Tile Root is a shared scene object.")]
        [SerializeField] private bool destroyOnlyGeneratedChildren = true;

        private readonly Dictionary<string, Terrain> terrainsByTileId = new();
        private readonly List<GameObject> generatedWaterObjects = new();
        private TerrainLayer[] runtimeFallbackTerrainLayers;
        private Texture2D[] runtimeFallbackTerrainTextures;
        private Material runtimeTerrainMaterial;
        private DetailPrototype[] runtimeGrassDetailPrototypes;
        private Texture2D runtimeGrassDetailTexture;
        private Material runtimeWaterMaterial;
        private Material runtimeRouteMaterial;
        private Material runtimeTreeTrunkMaterial;
        private Material runtimeTreeFoliageMaterial;
        private Material runtimeScrubMaterial;
        private Material runtimeWetReedMaterial;
        private Material runtimeRockMaterial;
        private Material runtimeSettlementEvidenceMaterial;
        private Material runtimeParcelStakeMaterial;
        private int runtimeFallbackAssetUseCount;
        private int lastImmediateRebuildFrame = -1;
        private bool lastImmediateRebuildSkippedAsDuplicate;
        private bool runtimeGenerationEnabled = true;
#if UNITY_EDITOR
        private bool editorRebuildQueued;
#endif

        public IReadOnlyDictionary<string, Terrain> TerrainsByTileId => terrainsByTileId;
        public bool AllowEditorTerrainRebuild => allowEditorTerrainRebuild;
        public bool AllowsTerrainRendererCreationNow => Application.isPlaying || allowEditorTerrainRebuild;
        public bool BuildOnlyTilesNearOpeningAnchor => buildOnlyTilesNearOpeningAnchor;
        public float OpeningAnchorTileRadiusMeters => Mathf.Max(64f, openingAnchorTileRadiusMeters);
        public int EffectiveHeightmapResolution => ResolveHeightmapResolution();
        public float EffectiveHeightScaleMeters => Mathf.Max(minimumVisibleReliefMeters, Mathf.Max(1f, heightScaleMeters) * Mathf.Clamp(terrainVerticalStrength, 0.05f, 1f));
        public int EffectiveDetailResolution => ResolveDetailResolution();
        public float LastAppliedTerrainYOffsetMeters { get; private set; }
        public int LastConsideredTileCount { get; private set; }
        public int LastBuiltTileCount { get; private set; }
        public int LastSkippedByRadiusCount { get; private set; }
        public int LastSkippedByRuntimeTileCapCount { get; private set; }
        public int LastBuiltWatercourseCount { get; private set; }
        public int LastBuiltRouteCorridorCount { get; private set; }
        public int LastBuiltScenicPropCount { get; private set; }
        public int LastBuiltSettlementEvidenceCount { get; private set; }
        public int LastBuiltParcelStakeCount { get; private set; }
        public TerrainCollider LastRegisteredTerrainCollider { get; private set; }
        public string LastTerrainColliderRegistrationStatus => lastTerrainColliderRegistrationStatus;
        public string LastWatercourseVisualStatus { get; private set; } = "No watercourse visuals built.";
        public int LastRuntimeFallbackAssetUseCount { get; private set; }
        public bool LastImmediateRebuildSkippedAsDuplicate => lastImmediateRebuildSkippedAsDuplicate;

        private string lastTerrainColliderRegistrationStatus = "No terrain collider registration attempted.";
        private bool hasWarnedMissingAuthoredRiverPrefab;

        public Vector3 PreviewSceneWorldForRegionalPoint(Vector2 regionalPoint)
        {
            return ToSceneWorld(regionalPoint);
        }

        private void Reset()
        {
            TryAssignDefaultAuthoredRiverPrefabInEditor();
        }

        private void OnValidate()
        {
            TryAssignDefaultAuthoredRiverPrefabInEditor();
        }

        private void Start()
        {
            TryAssignDefaultAuthoredRiverPrefabInEditor();
            if (buildOnStart && runtimeGenerationEnabled)
            {
                RebuildTiles();
            }
        }

        public void SetRuntimeGenerationEnabled(bool enabled)
        {
            runtimeGenerationEnabled = enabled;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void TryAssignDefaultAuthoredRiverPrefabInEditor()
        {
#if UNITY_EDITOR
            if (authoredRiverPrefab != null)
            {
                return;
            }

            authoredRiverPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Water/River.prefab");
#endif
        }

        [ContextMenu("Rebuild Regional Terrain Tiles")]
        public void RebuildTiles()
        {
            if (Application.isPlaying && !runtimeGenerationEnabled)
            {
                Debug.Log("[RegionalTerrain] Runtime terrain rebuild skipped because WorldStartupMode is UsePreAuthoredTerrain.", this);
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!AllowsTerrainRendererCreationNow)
                {
                    if (logSkippedEditorTerrainRebuild)
                    {
                        Debug.Log("[RegionalTerrain] Editor terrain rebuild skipped. Enter Play Mode to build generated terrain, or enable Allow Editor Terrain Rebuild on RegionalTerrainTileView for a manual editor preview.", this);
                    }
                    return;
                }

                QueueEditorTerrainRebuild();
                return;
            }
#endif

            RebuildTilesImmediate();
        }

#if UNITY_EDITOR
        private void QueueEditorTerrainRebuild()
        {
            if (editorRebuildQueued)
            {
                return;
            }

            editorRebuildQueued = true;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                editorRebuildQueued = false;
                if (this == null)
                {
                    return;
                }

                RebuildTilesImmediate();
            };
        }
#endif

        private void RebuildTilesImmediate()
        {
            Stopwatch totalTimer = ShouldLogRuntimeTerrainBuildTiming() ? Stopwatch.StartNew() : null;
            Stopwatch stepTimer = totalTimer != null ? Stopwatch.StartNew() : null;
            StringBuilder timing = totalTimer != null ? new StringBuilder() : null;

            RegionalWorldState state = ResolveState();
            if (state == null)
            {
                if (logTerrainBuildSummary)
                {
                    Debug.LogWarning("[RegionalTerrain] Rebuild skipped because no RegionalWorldState is available yet. Generate the town/regional foundation before rebuilding terrain.", this);
                }

                return;
            }

            if (Application.isPlaying && skipDuplicateRuntimeRebuildInSameFrame && UnityEngine.Time.frameCount == lastImmediateRebuildFrame)
            {
                lastImmediateRebuildSkippedAsDuplicate = true;
                if (logTerrainBuildSummary)
                {
                    Debug.Log("[RegionalTerrain] Duplicate runtime rebuild skipped because another terrain rebuild already completed this frame. This avoids bootstrap/start double-build churn.", this);
                }

                return;
            }

            lastImmediateRebuildFrame = Application.isPlaying ? UnityEngine.Time.frameCount : -1;
            lastImmediateRebuildSkippedAsDuplicate = false;
            runtimeFallbackAssetUseCount = 0;
            LastRuntimeFallbackAssetUseCount = 0;

            Transform root = EnsureTileRoot();
            ClearTiles();
            AppendTerrainBuildTiming(timing, stepTimer, "clear");

            Vector2 openingAnchor = ResolveOpeningAnchorMeters(state);
            float anchorHeight01 = SampleRegionalHeight01Base(state, openingAnchor);
            LastAppliedTerrainYOffsetMeters = normalizeOpeningTownHeight
                ? openingTownTargetY - anchorHeight01 * EffectiveHeightScaleMeters
                : 0f;
            AppendTerrainBuildTiming(timing, stepTimer, "anchor");

            TerrainCollider firstTerrainCollider = null;
            int considered = 0;
            int skippedByRadius = 0;
            int skippedByRuntimeCap = 0;
            int created = 0;
            for (int i = 0; i < state.TerrainTiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = state.TerrainTiles[i];
                if (tile == null)
                {
                    continue;
                }

                considered++;
                if (!ShouldBuildTile(state, tile))
                {
                    skippedByRadius++;
                    continue;
                }

                if (created >= ResolveRuntimeTileCap())
                {
                    skippedByRuntimeCap++;
                    continue;
                }

                Terrain terrain = CreateTerrainTile(state, tile, root, openingAnchor, anchorHeight01, LastAppliedTerrainYOffsetMeters);
                terrainsByTileId[tile.TileId] = terrain;
                terrain.gameObject.SetActive(tile.Active);
                TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
                if (firstTerrainCollider == null && terrainCollider != null)
                {
                    firstTerrainCollider = terrainCollider;
                }

                created++;
            }
            AppendTerrainBuildTiming(timing, stepTimer, "terrain tiles");

            LastBuiltWatercourseCount = ShouldBuildWatercourseEvidence()
                ? BuildWatercourseMeshes(state, root, openingAnchor, anchorHeight01, LastAppliedTerrainYOffsetMeters)
                : 0;
            AppendTerrainBuildTiming(timing, stepTimer, "water");
            LastBuiltRouteCorridorCount = ShouldBuildRouteEvidence()
                ? BuildRouteCorridorMeshes(state, root, openingAnchor, anchorHeight01, LastAppliedTerrainYOffsetMeters)
                : 0;
            AppendTerrainBuildTiming(timing, stepTimer, "routes");
            LastBuiltScenicPropCount = ShouldBuildScenicEvidence()
                ? BuildScenicVegetationProps(state, root, openingAnchor, anchorHeight01, LastAppliedTerrainYOffsetMeters)
                : 0;
            AppendTerrainBuildTiming(timing, stepTimer, "scenic");
            LastBuiltSettlementEvidenceCount = ShouldBuildSettlementEvidence()
                ? BuildSettlementEvidenceProps(state, root, openingAnchor, anchorHeight01, LastAppliedTerrainYOffsetMeters)
                : 0;
            AppendTerrainBuildTiming(timing, stepTimer, "settlements");
            LastBuiltParcelStakeCount = ShouldBuildParcelEvidence()
                ? BuildParcelBoundaryStakes(state, root, openingAnchor, anchorHeight01, LastAppliedTerrainYOffsetMeters)
                : 0;
            AppendTerrainBuildTiming(timing, stepTimer, "parcels");
            LastRuntimeFallbackAssetUseCount = runtimeFallbackAssetUseCount;

            LastConsideredTileCount = considered;
            LastBuiltTileCount = created;
            LastSkippedByRadiusCount = skippedByRadius;
            LastSkippedByRuntimeTileCapCount = skippedByRuntimeCap;
            RegisterGeneratedTerrainCollider(firstTerrainCollider);
            AppendTerrainBuildTiming(timing, stepTimer, "collider registration");

            if (logTerrainBuildSummary)
            {
                string colliderRead = LastRegisteredTerrainCollider != null
                    ? $"registered collider '{LastRegisteredTerrainCollider.name}'"
                    : "no terrain collider registered";
                Debug.Log($"[RegionalTerrain] Built {created}/{considered} tile(s), skipped {skippedByRadius} by anchor radius, runtime cap skipped {skippedByRuntimeCap}, water visuals {LastBuiltWatercourseCount} ({LastWatercourseVisualStatus}), route ribbons {LastBuiltRouteCorridorCount}, scenic props {LastBuiltScenicPropCount}, settlement markers {LastBuiltSettlementEvidenceCount}, parcel stakes {LastBuiltParcelStakeCount}, runtime fallback assets {LastRuntimeFallbackAssetUseCount}, quality={BuildEffectiveQualitySummary()}, heightmap={EffectiveHeightmapResolution}, alphamap={ResolveAlphamapResolution()}, details={(ShouldPaintRuntimeGrassDetails() ? EffectiveDetailResolution.ToString() : "off")}, heightScale={EffectiveHeightScaleMeters:0.##}m, openingAnchorHeight={anchorHeight01 * EffectiveHeightScaleMeters:0.##}m, terrainYOffset={LastAppliedTerrainYOffsetMeters:0.##}m, normalizeTownHeight={normalizeOpeningTownHeight}, {colliderRead}, registration status {LastTerrainColliderRegistrationStatus}.", this);
            }

            if (totalTimer != null)
            {
                totalTimer.Stop();
                Debug.Log($"[RegionalTerrain] RebuildTilesImmediate timing total {totalTimer.ElapsedMilliseconds} ms ({timing}).", this);
            }
        }

        [ContextMenu("Log Regional Terrain Runtime Diagnostics")]
        public void LogRegionalTerrainRuntimeDiagnostics()
        {
            Debug.Log(BuildRuntimeDiagnosticSummary(), this);
        }

        public string BuildRuntimeDiagnosticSummary()
        {
            RegionalWorldState state = ResolveState();
            string stateRead = state != null
                ? $"regional world ready, tiles {state.TerrainTiles.Count}, watercourses {state.Watercourses.Count}, routes {state.RouteCorridors.Count}, parcels {state.SurveyParcels.Count}, settlements {state.Settlements.Count}"
                : "regional world missing";
            string rootRead = tileRoot != null
                ? $"tile root '{tileRoot.name}' at {tileRoot.position}, scale {tileRoot.lossyScale}"
                : "tile root missing; runtime root will be created on rebuild";
            string colliderRead = LastRegisteredTerrainCollider != null
                ? $"registered terrain collider '{LastRegisteredTerrainCollider.name}' ({LastTerrainColliderRegistrationStatus})"
                : $"no terrain collider registered yet ({LastTerrainColliderRegistrationStatus})";
            string terrainRead = $"terrain tiles live {terrainsByTileId.Count}, last built {LastBuiltTileCount}/{LastConsideredTileCount}, radius skipped {LastSkippedByRadiusCount}, cap skipped {LastSkippedByRuntimeTileCapCount}";
            string evidenceRead = $"water {LastBuiltWatercourseCount} ({LastWatercourseVisualStatus}), routes {LastBuiltRouteCorridorCount}, scenic {LastBuiltScenicPropCount}, settlements {LastBuiltSettlementEvidenceCount}, parcel stakes {LastBuiltParcelStakeCount}";
            string qualityRead = $"{BuildEffectiveQualitySummary()}, heightmap {EffectiveHeightmapResolution}, alphamap {ResolveAlphamapResolution()}, heightScale {EffectiveHeightScaleMeters:0.##}m, detail {(ShouldPaintRuntimeGrassDetails() ? EffectiveDetailResolution.ToString() : "off")}, fallback assets last used {LastRuntimeFallbackAssetUseCount}";
            string duplicateRead = LastImmediateRebuildSkippedAsDuplicate
                ? "last duplicate rebuild request was skipped"
                : "no duplicate rebuild skipped on last request";
            string editorRead = AllowsTerrainRendererCreationNow
                ? "terrain renderer creation currently allowed"
                : "terrain renderer creation currently blocked until Play Mode or explicit editor preview";
            TownWorldController controller = ResolveTownWorld();
            string conformanceRead = controller != null
                ? controller.BuildRuntimeTerrainVisualConformanceSummary()
                : "TownWorldController not resolved for visual conformance read";

            return "[RegionalTerrain] Runtime diagnostics\n"
                + $"- State: {stateRead}\n"
                + $"- Root: {rootRead}\n"
                + $"- Tiles: {terrainRead}\n"
                + $"- Evidence: {evidenceRead}\n"
                + $"- Quality: {qualityRead}\n"
                + $"- Vertical offset: {LastAppliedTerrainYOffsetMeters:0.##}m, normalize opening town height {normalizeOpeningTownHeight}\n"
                + $"- Collider: {colliderRead}\n"
                + $"- Town visuals: {conformanceRead}\n"
                + $"- Editor/runtime: {editorRead}; {duplicateRead}.";
        }


        [ContextMenu("Log Regional Evidence Inspection Digest")]
        public void LogRegionalEvidenceInspectionDigest()
        {
            Debug.Log(BuildRegionalEvidenceInspectionDigest(), this);
        }

        [ContextMenu("Log Nearest Regional Evidence At This Object")]
        public void LogNearestRegionalEvidenceAtThisObject()
        {
            Debug.Log(BuildNearestRegionalEvidenceInspection(transform.position, evidenceInspectionSearchRadiusMeters), this);
        }

        public string BuildRegionalEvidenceInspectionDigest(int maxItems = -1)
        {
            RegionalWorldState state = ResolveState();
            if (state == null)
            {
                return "[RegionalTerrain] Evidence inspection unavailable: no regional world is available.";
            }

            int limit = Mathf.Clamp(maxItems > 0 ? maxItems : evidenceInspectionDigestItems, 1, 12);
            StringBuilder builder = new();
            builder.AppendLine("[RegionalTerrain] Regional evidence inspection digest");
            builder.AppendLine($"- Quality: {BuildEffectiveQualitySummary()}");
            builder.AppendLine($"- Terrain runtime: built {LastBuiltTileCount}/{LastConsideredTileCount} tiles, radius skipped {LastSkippedByRadiusCount}, cap skipped {LastSkippedByRuntimeTileCapCount}, fallback assets {LastRuntimeFallbackAssetUseCount}");
            builder.AppendLine($"- Evidence counts: water {LastBuiltWatercourseCount} ({LastWatercourseVisualStatus}), routes {LastBuiltRouteCorridorCount}, scenic {LastBuiltScenicPropCount}, settlements {LastBuiltSettlementEvidenceCount}, parcel stakes {LastBuiltParcelStakeCount}");
            builder.AppendLine($"- Regional records: tiles {state.TerrainTiles.Count}, watercourses {state.Watercourses.Count}, routes {state.RouteCorridors.Count}, parcels {state.SurveyParcels.Count}, settlements {state.Settlements.Count}");
            AppendTopTileReadouts(builder, state, limit);
            AppendTopRouteReadouts(builder, state, limit);
            AppendTopParcelReadouts(builder, state, limit);
            AppendTopSettlementReadouts(builder, state, limit);
            return builder.ToString().Trim();
        }

        public string BuildNearestRegionalEvidenceInspection(Vector3 sceneWorld, float maxDistanceMeters = -1f)
        {
            RegionalWorldState state = ResolveState();
            if (state == null)
            {
                return "[RegionalTerrain] Nearest evidence inspection unavailable: no regional world is available.";
            }

            float maxDistance = maxDistanceMeters > 0f ? maxDistanceMeters : Mathf.Max(32f, evidenceInspectionSearchRadiusMeters);
            Vector2 regionalPoint = SceneWorldToRegionalPoint(sceneWorld);
            StringBuilder builder = new();
            builder.AppendLine($"[RegionalTerrain] Nearest evidence at scene {sceneWorld.x:0.#}/{sceneWorld.z:0.#} -> regional {regionalPoint.x:0.#}/{regionalPoint.y:0.#}");
            RegionalTerrainTileRecord tile = FindNearestTile(state, regionalPoint, out float tileDistance);
            if (tile != null)
            {
                builder.AppendLine("Tile: " + BuildTileInspectionLine(tile, tileDistance));
            }

            RegionalSurveyParcelRecord parcel = FindNearestParcel(state, regionalPoint, maxDistance, out float parcelDistance);
            if (parcel != null)
            {
                builder.AppendLine("Parcel: " + BuildParcelInspectionLine(state, parcel, parcelDistance));
            }
            else
            {
                builder.AppendLine($"Parcel: none within {maxDistance:0}m.");
            }

            RegionalRouteCorridorRecord route = FindNearestRoute(state, regionalPoint, maxDistance, out float routeDistance);
            if (route != null)
            {
                builder.AppendLine("Route: " + BuildRouteInspectionLine(route, routeDistance));
            }
            else
            {
                builder.AppendLine($"Route: none within {maxDistance:0}m.");
            }

            RegionalWatercourseRecord water = FindNearestWatercourse(state, regionalPoint, maxDistance, out float waterDistance);
            if (water != null)
            {
                builder.AppendLine("Water: " + BuildWatercourseInspectionLine(water, waterDistance));
            }
            else
            {
                builder.AppendLine($"Water: none within {maxDistance:0}m.");
            }

            RegionalSettlementRecord settlement = FindNearestSettlement(state, regionalPoint, maxDistance, out float settlementDistance);
            if (settlement != null)
            {
                builder.AppendLine("Settlement: " + BuildSettlementInspectionLine(settlement, settlementDistance));
            }
            else
            {
                builder.AppendLine($"Settlement: none within {maxDistance:0}m.");
            }

            return builder.ToString().Trim();
        }

        public string BuildTerrainTileInspectionSummary(string tileId)
        {
            RegionalWorldState state = ResolveState();
            if (state == null)
            {
                return "[RegionalTerrain] Tile inspection unavailable: no regional world is available.";
            }

            RegionalTerrainTileRecord tile = FindTileById(state, tileId);
            return tile != null
                ? "[RegionalTerrain] " + BuildTileInspectionLine(tile, 0f)
                : $"[RegionalTerrain] Tile '{tileId}' was not found.";
        }

        public string BuildRegionalRouteInspectionSummary(string corridorId)
        {
            RegionalWorldState state = ResolveState();
            if (state == null)
            {
                return "[RegionalTerrain] Route inspection unavailable: no regional world is available.";
            }

            RegionalRouteCorridorRecord route = FindRouteById(state, corridorId);
            return route != null
                ? "[RegionalTerrain] " + BuildRouteInspectionLine(route, 0f)
                : $"[RegionalTerrain] Route corridor '{corridorId}' was not found.";
        }

        public string BuildRegionalParcelInspectionSummary(string parcelId)
        {
            RegionalWorldState state = ResolveState();
            if (state == null)
            {
                return "[RegionalTerrain] Parcel inspection unavailable: no regional world is available.";
            }

            RegionalSurveyParcelRecord parcel = FindParcelById(state, parcelId);
            return parcel != null
                ? "[RegionalTerrain] " + BuildParcelInspectionLine(state, parcel, 0f)
                : $"[RegionalTerrain] Parcel '{parcelId}' was not found.";
        }

        public string BuildRegionalSettlementInspectionSummary(string settlementId)
        {
            RegionalWorldState state = ResolveState();
            if (state == null)
            {
                return "[RegionalTerrain] Settlement inspection unavailable: no regional world is available.";
            }

            RegionalSettlementRecord settlement = FindSettlementById(state, settlementId);
            return settlement != null
                ? "[RegionalTerrain] " + BuildSettlementInspectionLine(settlement, 0f)
                : $"[RegionalTerrain] Settlement '{settlementId}' was not found.";
        }

        private void AppendTopTileReadouts(StringBuilder builder, RegionalWorldState state, int limit)
        {
            builder.AppendLine("Top terrain tiles:");
            int emitted = 0;
            for (int rank = 0; rank < limit; rank++)
            {
                RegionalTerrainTileRecord best = null;
                float bestScore = -1f;
                for (int i = 0; i < state.TerrainTiles.Count; i++)
                {
                    RegionalTerrainTileRecord tile = state.TerrainTiles[i];
                    if (tile == null || HasAlreadyEmittedTile(builder, tile.TileId))
                    {
                        continue;
                    }

                    float score = tile.ActivationReadiness01 * 0.34f + tile.RouteActivationReadiness01 * 0.24f + tile.SettlementInfluence01 * 0.18f + tile.RouteInfluence01 * 0.14f + (1f - tile.FreightBurdenInfluence01) * 0.10f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = tile;
                    }
                }

                if (best == null)
                {
                    break;
                }

                emitted++;
                builder.AppendLine($"  {emitted}. {BuildTileInspectionLine(best, 0f)}");
            }

            if (emitted == 0)
            {
                builder.AppendLine("  none generated.");
            }
        }

        private void AppendTopRouteReadouts(StringBuilder builder, RegionalWorldState state, int limit)
        {
            builder.AppendLine("Top route/freight evidence:");
            int emitted = 0;
            for (int rank = 0; rank < limit; rank++)
            {
                RegionalRouteCorridorRecord best = null;
                float bestScore = -1f;
                for (int i = 0; i < state.RouteCorridors.Count; i++)
                {
                    RegionalRouteCorridorRecord route = state.RouteCorridors[i];
                    if (route == null || HasAlreadyEmittedRoute(builder, route.CorridorId))
                    {
                        continue;
                    }

                    float score = route.Practicality01 * 0.26f + route.SeasonalReliability01 * 0.22f + route.FreightBurden01 * 0.20f + route.BridgeOrFordNeed01 * 0.12f + route.MudSeasonRisk01 * 0.10f + route.GradeBurden01 * 0.10f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = route;
                    }
                }

                if (best == null)
                {
                    break;
                }

                emitted++;
                builder.AppendLine($"  {emitted}. {BuildRouteInspectionLine(best, 0f)}");
            }

            if (emitted == 0)
            {
                builder.AppendLine("  none generated.");
            }
        }

        private void AppendTopParcelReadouts(StringBuilder builder, RegionalWorldState state, int limit)
        {
            builder.AppendLine("Top parcel evidence:");
            int emitted = 0;
            for (int rank = 0; rank < limit; rank++)
            {
                RegionalSurveyParcelRecord best = null;
                float bestScore = -1f;
                for (int i = 0; i < state.SurveyParcels.Count; i++)
                {
                    RegionalSurveyParcelRecord parcel = state.SurveyParcels[i];
                    if (parcel == null || HasAlreadyEmittedParcel(builder, parcel.ParcelId))
                    {
                        continue;
                    }

                    float score = parcel.DevelopmentReadiness01 * 0.24f + parcel.FreightAccess01 * 0.18f + parcel.ResourceSuitability01 * 0.18f + parcel.BuildSuitability01 * 0.14f + parcel.FarmSuitability01 * 0.10f + parcel.CorridorInfluence01 * 0.10f + (1f - parcel.SupportBurden01) * 0.06f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = parcel;
                    }
                }

                if (best == null)
                {
                    break;
                }

                emitted++;
                builder.AppendLine($"  {emitted}. {BuildParcelInspectionLine(state, best, 0f)}");
            }

            if (emitted == 0)
            {
                builder.AppendLine("  none generated.");
            }
        }

        private void AppendTopSettlementReadouts(StringBuilder builder, RegionalWorldState state, int limit)
        {
            builder.AppendLine("Top settlement evidence:");
            int emitted = 0;
            for (int rank = 0; rank < limit; rank++)
            {
                RegionalSettlementRecord best = null;
                float bestScore = -1f;
                for (int i = 0; i < state.Settlements.Count; i++)
                {
                    RegionalSettlementRecord settlement = state.Settlements[i];
                    if (settlement == null || HasAlreadyEmittedSettlement(builder, settlement.SettlementId))
                    {
                        continue;
                    }

                    float score = settlement.RegionalGravity01 * 0.28f + settlement.FreightSupport01 * 0.20f + settlement.ServiceGravity01 * 0.18f + settlement.SuccessionReadiness01 * 0.14f + settlement.FootholdChallenge01 * 0.10f + (1f - settlement.DeclineRisk01) * 0.10f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = settlement;
                    }
                }

                if (best == null)
                {
                    break;
                }

                emitted++;
                builder.AppendLine($"  {emitted}. {BuildSettlementInspectionLine(best, 0f)}");
            }

            if (emitted == 0)
            {
                builder.AppendLine("  none generated.");
            }
        }

        private static bool HasAlreadyEmittedTile(StringBuilder builder, string id) => builder != null && !string.IsNullOrWhiteSpace(id) && builder.ToString().Contains($"{id} | ");
        private static bool HasAlreadyEmittedRoute(StringBuilder builder, string id) => builder != null && !string.IsNullOrWhiteSpace(id) && builder.ToString().Contains($"{id} | ");
        private static bool HasAlreadyEmittedParcel(StringBuilder builder, string id) => builder != null && !string.IsNullOrWhiteSpace(id) && builder.ToString().Contains($"{id} | ");
        private static bool HasAlreadyEmittedSettlement(StringBuilder builder, string id) => builder != null && !string.IsNullOrWhiteSpace(id) && builder.ToString().Contains($"{id} | ");

        private string BuildTileInspectionLine(RegionalTerrainTileRecord tile, float distanceMeters)
        {
            string live = terrainsByTileId.ContainsKey(tile.TileId) ? "live terrain" : "regional record";
            string distance = distanceMeters > 0f ? $", {distanceMeters:0}m away" : string.Empty;
            return $"{tile.TileId} | {tile.DominantBiomeKind} | {live}{distance}; active {tile.Active}; readiness {tile.ActivationReadiness01:P0}; route readiness {tile.RouteActivationReadiness01:P0}; wet {tile.Wetness01:P0}; rough {tile.Ruggedness01:P0}; freight burden influence {tile.FreightBurdenInfluence01:P0}; {tile.BuildReadinessSummary()}";
        }

        private static string BuildRouteInspectionLine(RegionalRouteCorridorRecord route, float distanceMeters)
        {
            string distance = distanceMeters > 0f ? $", {distanceMeters:0}m away" : string.Empty;
            return $"{route.CorridorId} | {route.Kind}{distance}; length {route.LengthMeters / 1000f:0.0}km; practical {route.Practicality01:P0}; reliable {route.SeasonalReliability01:P0}; freight burden {route.FreightBurden01:P0}; crossings {route.WaterCrossingCount}; mud {route.MudSeasonRisk01:P0}; grade {route.GradeBurden01:P0}; {route.BuildRoutePlanningReadout()}";
        }

        private static string BuildWatercourseInspectionLine(RegionalWatercourseRecord water, float distanceMeters)
        {
            string distance = distanceMeters > 0f ? $", {distanceMeters:0}m away" : string.Empty;
            return $"{water.WatercourseId} | {water.Kind}{distance}; flow {water.FlowStrength01:P0}; influence {water.InfluenceWidthMeters:0}m; floodplain {water.FloodplainWidthMeters:0}m; points {water.Points.Count}.";
        }

        private string BuildParcelInspectionLine(RegionalWorldState state, RegionalSurveyParcelRecord parcel, float distanceMeters)
        {
            string distance = distanceMeters > 0f ? $", {distanceMeters:0}m away" : string.Empty;
            string authority = string.Empty;
            if (includeParcelAuthorityInEvidenceInspection)
            {
                RegionalParcelAuthorityReadout readout = RegionalParcelAuthority.Evaluate(state, parcel);
                authority = $"; authority {readout.RecommendedUseLabel}; hooks {string.Join(", ", readout.FutureSystemHooks)}";
            }

            return $"{parcel.ParcelId} | {parcel.ParcelKind}{distance}; {parcel.Acreage:0.#} acres; access {parcel.AccessQuality}/{parcel.FrontageClass}; build {parcel.BuildSuitability01:P0}; farm {parcel.FarmSuitability01:P0}; resource {parcel.ResourceSuitability01:P0}; freight {parcel.FreightAccess01:P0}; support burden {parcel.SupportBurden01:P0}{authority}.";
        }

        private static string BuildSettlementInspectionLine(RegionalSettlementRecord settlement, float distanceMeters)
        {
            string distance = distanceMeters > 0f ? $", {distanceMeters:0}m away" : string.Empty;
            return $"{settlement.SettlementId} | {settlement.Label}/{settlement.Character}{distance}; pop {settlement.Population}; households {settlement.HouseholdCount}; businesses {settlement.BusinessCount}; gravity {settlement.RegionalGravity01:P0}; service {settlement.ServiceGravity01:P0}; freight {settlement.FreightSupport01:P0}; support burden {settlement.SupportBurden01:P0}; decline {settlement.DeclineRisk01:P0}; succession {settlement.SuccessionRole}.";
        }

        private Vector2 SceneWorldToRegionalPoint(Vector3 sceneWorld)
        {
            TownWorldController controller = ResolveTownWorld();
            if (useOpeningTownLocalSpace && controller != null)
            {
                return controller.SceneWorldToRegionalPoint(sceneWorld);
            }

            return new Vector2(sceneWorld.x, sceneWorld.z);
        }

        private static RegionalTerrainTileRecord FindTileById(RegionalWorldState state, string tileId)
        {
            if (state == null || string.IsNullOrWhiteSpace(tileId))
            {
                return null;
            }

            for (int i = 0; i < state.TerrainTiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = state.TerrainTiles[i];
                if (tile != null && string.Equals(tile.TileId, tileId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return tile;
                }
            }

            return null;
        }

        private static RegionalRouteCorridorRecord FindRouteById(RegionalWorldState state, string corridorId)
        {
            if (state == null || string.IsNullOrWhiteSpace(corridorId))
            {
                return null;
            }

            for (int i = 0; i < state.RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = state.RouteCorridors[i];
                if (route != null && string.Equals(route.CorridorId, corridorId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return route;
                }
            }

            return null;
        }

        private static RegionalSurveyParcelRecord FindParcelById(RegionalWorldState state, string parcelId)
        {
            if (state == null || string.IsNullOrWhiteSpace(parcelId))
            {
                return null;
            }

            for (int i = 0; i < state.SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = state.SurveyParcels[i];
                if (parcel != null && string.Equals(parcel.ParcelId, parcelId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return parcel;
                }
            }

            return null;
        }

        private static RegionalSettlementRecord FindSettlementById(RegionalWorldState state, string settlementId)
        {
            if (state == null || string.IsNullOrWhiteSpace(settlementId))
            {
                return null;
            }

            for (int i = 0; i < state.Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = state.Settlements[i];
                if (settlement != null && string.Equals(settlement.SettlementId, settlementId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return settlement;
                }
            }

            return null;
        }

        private static RegionalTerrainTileRecord FindNearestTile(RegionalWorldState state, Vector2 point, out float distanceMeters)
        {
            distanceMeters = float.MaxValue;
            RegionalTerrainTileRecord best = null;
            if (state == null)
            {
                return null;
            }

            for (int i = 0; i < state.TerrainTiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = state.TerrainTiles[i];
                if (tile == null)
                {
                    continue;
                }

                float distance = DistanceToRect(point, tile.BoundsMeters);
                if (distance < distanceMeters)
                {
                    distanceMeters = distance;
                    best = tile;
                }
            }

            return best;
        }

        private static RegionalSurveyParcelRecord FindNearestParcel(RegionalWorldState state, Vector2 point, float maxDistanceMeters, out float distanceMeters)
        {
            distanceMeters = float.MaxValue;
            RegionalSurveyParcelRecord best = null;
            if (state == null)
            {
                return null;
            }

            for (int i = 0; i < state.SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = state.SurveyParcels[i];
                if (parcel == null)
                {
                    continue;
                }

                float distance = DistanceToRect(point, parcel.BoundsMeters);
                if (distance < distanceMeters)
                {
                    distanceMeters = distance;
                    best = parcel;
                }
            }

            return best != null && distanceMeters <= maxDistanceMeters ? best : null;
        }

        private static RegionalRouteCorridorRecord FindNearestRoute(RegionalWorldState state, Vector2 point, float maxDistanceMeters, out float distanceMeters)
        {
            distanceMeters = float.MaxValue;
            RegionalRouteCorridorRecord best = null;
            if (state == null)
            {
                return null;
            }

            for (int i = 0; i < state.RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = state.RouteCorridors[i];
                if (route == null)
                {
                    continue;
                }

                float distance = DistanceToPolyline(point, route.Points);
                if (distance < distanceMeters)
                {
                    distanceMeters = distance;
                    best = route;
                }
            }

            return best != null && distanceMeters <= maxDistanceMeters ? best : null;
        }

        private static RegionalWatercourseRecord FindNearestWatercourse(RegionalWorldState state, Vector2 point, float maxDistanceMeters, out float distanceMeters)
        {
            distanceMeters = float.MaxValue;
            RegionalWatercourseRecord best = null;
            if (state == null)
            {
                return null;
            }

            for (int i = 0; i < state.Watercourses.Count; i++)
            {
                RegionalWatercourseRecord water = state.Watercourses[i];
                if (water == null)
                {
                    continue;
                }

                float distance = DistanceToPolyline(point, water.Points);
                if (distance < distanceMeters)
                {
                    distanceMeters = distance;
                    best = water;
                }
            }

            return best != null && distanceMeters <= maxDistanceMeters ? best : null;
        }

        private static RegionalSettlementRecord FindNearestSettlement(RegionalWorldState state, Vector2 point, float maxDistanceMeters, out float distanceMeters)
        {
            distanceMeters = float.MaxValue;
            RegionalSettlementRecord best = null;
            if (state == null)
            {
                return null;
            }

            for (int i = 0; i < state.Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = state.Settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                float distance = Vector2.Distance(point, settlement.CenterMeters);
                if (distance < distanceMeters)
                {
                    distanceMeters = distance;
                    best = settlement;
                }
            }

            return best != null && distanceMeters <= maxDistanceMeters ? best : null;
        }

        private static float DistanceToRect(Vector2 point, Rect rect)
        {
            float dx = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
            float dy = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static float DistanceToPolyline(Vector2 point, IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count == 0)
            {
                return float.MaxValue;
            }

            if (points.Count == 1)
            {
                return Vector2.Distance(point, points[0]);
            }

            float best = float.MaxValue;
            for (int i = 1; i < points.Count; i++)
            {
                best = Mathf.Min(best, DistanceToSegment(point, points[i - 1], points[i]));
            }

            return best;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denominator = Mathf.Max(0.0001f, Vector2.Dot(ab, ab));
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
            return Vector2.Distance(point, a + ab * t);
        }


        public bool SetTileActive(string tileId, bool active)
        {
            if (string.IsNullOrWhiteSpace(tileId) || !terrainsByTileId.TryGetValue(tileId, out Terrain terrain) || terrain == null)
            {
                return false;
            }

            terrain.gameObject.SetActive(active);
            return true;
        }

        [ContextMenu("Clear Regional Terrain Tiles")]
        public void ClearTiles()
        {
            terrainsByTileId.Clear();
            generatedWaterObjects.Clear();
            Transform root = EnsureTileRoot();
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (!destroyOnlyGeneratedChildren || IsGeneratedTerrainChild(child))
                {
                    DestroyUnityObject(child.gameObject);
                }
            }
        }

        private Terrain CreateTerrainTile(RegionalWorldState state, RegionalTerrainTileRecord tile, Transform root, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters)
        {
            int resolution = EffectiveHeightmapResolution;
            TerrainData data = new()
            {
                heightmapResolution = resolution,
                alphamapResolution = ResolveAlphamapResolution(),
                size = new Vector3(tile.BoundsMeters.width, EffectiveHeightScaleMeters, tile.BoundsMeters.height)
            };

            float[,] heights = new float[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float x01 = x / (float)(resolution - 1);
                    float z01 = z / (float)(resolution - 1);
                    Vector2 point = new(
                        Mathf.Lerp(tile.BoundsMeters.xMin, tile.BoundsMeters.xMax, x01),
                        Mathf.Lerp(tile.BoundsMeters.yMin, tile.BoundsMeters.yMax, z01));
                    float baseHeight = SampleRegionalHeight01Base(state, point);
                    heights[z, x] = Mathf.Clamp01(ApplyOpeningTownShelf(point, openingAnchorMeters, baseHeight, anchorHeight01));
                }
            }

            SmoothHeightsInPlace(heights, heightSmoothingPasses);
            data.name = $"RegionalTerrainData_{tile.TileId}";
            data.SetHeights(0, 0, heights);
            TerrainLayer[] resolvedLayers = ResolveTerrainLayers();
            if (resolvedLayers != null && resolvedLayers.Length > 0)
            {
                data.terrainLayers = resolvedLayers;
                if (ShouldPaintTerrainAlphamaps())
                {
                    PaintAlphamaps(state, tile, data, resolvedLayers.Length, openingAnchorMeters);
                }
            }

            if (ShouldPaintRuntimeGrassDetails())
            {
                PaintRuntimeGrassDetails(state, tile, data, openingAnchorMeters);
            }

            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            float readiness = Mathf.Max(tile.ActivationReadiness01, tile.RouteActivationReadiness01);
            terrainObject.name = $"Regional Terrain {tile.TileId} ({tile.DominantBiomeKind}, ready {readiness:P0}, route {tile.RouteInfluence01:P0})";
            terrainObject.layer = gameObject.layer;

            Vector3 sceneOrigin = ToSceneWorld(new Vector2(tile.BoundsMeters.xMin, tile.BoundsMeters.yMin));
            Vector3 worldPosition = new(sceneOrigin.x, terrainYOffsetMeters, sceneOrigin.z);
            terrainObject.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
            terrainObject.transform.localScale = Vector3.one;
            terrainObject.transform.SetParent(root, true);

            Terrain terrain = terrainObject.GetComponent<Terrain>();
            Material resolvedTerrainMaterial = ResolveTerrainMaterial();
            if (resolvedTerrainMaterial != null)
            {
                terrain.materialTemplate = resolvedTerrainMaterial;
            }

            // Runtime fallback terrain materials deliberately avoid instancing because the
            // default HDRP terrain instanced path can render as unsupported green sheets
            // before an authored TerrainLit setup is wired.
            terrain.drawInstanced = drawInstancedTerrain && terrainMaterial != null && resolvedTerrainMaterial == terrainMaterial;
            terrain.allowAutoConnect = true;
            terrain.Flush();

            TerrainCollider collider = terrainObject.GetComponent<TerrainCollider>();
            if (collider == null && ensureTerrainCollider)
            {
                collider = terrainObject.AddComponent<TerrainCollider>();
            }

            if (collider != null)
            {
                collider.terrainData = data;
                collider.enabled = true;
            }

            if (logTilePlacementDetails)
            {
                Debug.Log($"[RegionalTerrain] Tile {tile.TileId} world position {worldPosition}, local position {terrainObject.transform.localPosition}, root '{root.name}' world {root.position}, root scale {root.lossyScale}, terrain size {data.size}, drawInstanced {terrain.drawInstanced}.", terrainObject);
            }

            return terrain;
        }


        private bool ShouldApplyQualityPreset()
        {
            if (runtimeQualityPreset == RegionalTerrainRuntimeQualityPreset.Custom)
            {
                return false;
            }

            return !applyQualityPresetOnlyAtRuntime || Application.isPlaying;
        }

        private int ResolveRuntimeTileCap()
        {
            int cap = Mathf.Max(1, maxRuntimeTilesPerRebuild);
            if (!ShouldApplyQualityPreset())
            {
                return cap;
            }

            int presetCap = runtimeQualityPreset switch
            {
                RegionalTerrainRuntimeQualityPreset.Low => 16,
                RegionalTerrainRuntimeQualityPreset.Medium => 48,
                RegionalTerrainRuntimeQualityPreset.High => 96,
                _ => cap
            };

            return Mathf.Min(cap, presetCap);
        }

        private int ResolvePresetHeightmapCap()
        {
            return runtimeQualityPreset switch
            {
                RegionalTerrainRuntimeQualityPreset.Low => 65,
                RegionalTerrainRuntimeQualityPreset.Medium => 129,
                RegionalTerrainRuntimeQualityPreset.High => 257,
                _ => Mathf.Max(17, heightmapResolution)
            };
        }

        private int ResolveAlphamapResolution()
        {
            int requested = Mathf.Max(16, alphamapResolution);
            if (!ShouldApplyQualityPreset())
            {
                return requested;
            }

            int presetCap = runtimeQualityPreset switch
            {
                RegionalTerrainRuntimeQualityPreset.Low => 48,
                RegionalTerrainRuntimeQualityPreset.Medium => 96,
                RegionalTerrainRuntimeQualityPreset.High => 192,
                _ => requested
            };

            return Mathf.Min(requested, presetCap);
        }

        private int ResolveDetailResolution()
        {
            int requested = Mathf.Max(32, detailResolution);
            if (!ShouldApplyQualityPreset())
            {
                return requested;
            }

            int presetCap = runtimeQualityPreset switch
            {
                RegionalTerrainRuntimeQualityPreset.Low => 96,
                RegionalTerrainRuntimeQualityPreset.Medium => 192,
                RegionalTerrainRuntimeQualityPreset.High => 384,
                _ => requested
            };

            return Mathf.Min(requested, presetCap);
        }

        private bool ShouldPaintTerrainAlphamaps()
        {
            return paintTerrainAlphamaps && (!ShouldApplyQualityPreset() || runtimeQualityPreset != RegionalTerrainRuntimeQualityPreset.Low);
        }

        private bool ShouldPaintRuntimeGrassDetails()
        {
            if (!paintRuntimeGrassDetails)
            {
                return false;
            }

            return !ShouldApplyQualityPreset() || runtimeQualityPreset != RegionalTerrainRuntimeQualityPreset.Low;
        }

        private bool ShouldBuildWatercourseEvidence()
        {
            return buildWatercourseMeshes;
        }

        private bool ShouldBuildRouteEvidence()
        {
            return buildRouteCorridorMeshes && (!qualityPresetControlsEvidenceLayers || !ShouldApplyQualityPreset() || runtimeQualityPreset != RegionalTerrainRuntimeQualityPreset.Low);
        }

        private bool ShouldBuildScenicEvidence()
        {
            return buildScenicVegetationProps && (!qualityPresetControlsEvidenceLayers || !ShouldApplyQualityPreset() || runtimeQualityPreset != RegionalTerrainRuntimeQualityPreset.Low);
        }

        private bool ShouldBuildSettlementEvidence()
        {
            return buildSettlementEvidenceProps;
        }

        private bool ShouldBuildParcelEvidence()
        {
            return buildParcelBoundaryStakes && (!qualityPresetControlsEvidenceLayers || !ShouldApplyQualityPreset() || runtimeQualityPreset == RegionalTerrainRuntimeQualityPreset.High || runtimeQualityPreset == RegionalTerrainRuntimeQualityPreset.Custom);
        }

        private int ResolveScenicPropCap()
        {
            int cap = Mathf.Clamp(maxScenicPropsPerRebuild, 0, 800);
            if (!ShouldApplyQualityPreset())
            {
                return cap;
            }

            int presetCap = runtimeQualityPreset switch
            {
                RegionalTerrainRuntimeQualityPreset.Low => 0,
                RegionalTerrainRuntimeQualityPreset.Medium => 120,
                RegionalTerrainRuntimeQualityPreset.High => 320,
                _ => cap
            };

            return Mathf.Min(cap, presetCap);
        }

        private int ResolveParcelStakeCap()
        {
            int cap = Mathf.Clamp(maxParcelBoundaryStakes, 0, 256);
            if (!ShouldApplyQualityPreset())
            {
                return cap;
            }

            int presetCap = runtimeQualityPreset switch
            {
                RegionalTerrainRuntimeQualityPreset.Low => 0,
                RegionalTerrainRuntimeQualityPreset.Medium => 32,
                RegionalTerrainRuntimeQualityPreset.High => 96,
                _ => cap
            };

            return Mathf.Min(cap, presetCap);
        }

        public string BuildEffectiveQualitySummary()
        {
            string applied = ShouldApplyQualityPreset() ? "applied" : "authored values";
            return $"{runtimeQualityPreset} ({applied}), tile cap {ResolveRuntimeTileCap()}, scenic cap {ResolveScenicPropCap()}, parcel stake cap {ResolveParcelStakeCap()}";
        }

        private bool ShouldLogRuntimeTerrainBuildTiming()
        {
            return logRuntimeTerrainBuildTiming && Application.isPlaying;
        }

        private static void AppendTerrainBuildTiming(StringBuilder builder, Stopwatch timer, string label)
        {
            if (builder == null || timer == null)
            {
                return;
            }

            timer.Stop();
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(label);
            builder.Append(' ');
            builder.Append(timer.ElapsedMilliseconds);
            builder.Append(" ms");
            timer.Restart();
        }

        private int ResolveHeightmapResolution()
        {
            int requested = Mathf.Max(17, heightmapResolution);
            if (upgradeLowRuntimeHeightmapResolution)
            {
                requested = Mathf.Max(requested, Mathf.Max(17, minimumRuntimeHeightmapResolution));
            }

            if (ShouldApplyQualityPreset())
            {
                requested = Mathf.Min(requested, ResolvePresetHeightmapCap());
            }

            return forceValidUnityTerrainResolution ? ToValidUnityTerrainResolution(requested) : requested;
        }

        private static int ToValidUnityTerrainResolution(int requested)
        {
            int clamped = Mathf.Clamp(requested, 17, 4097);
            int power = 16;
            while (power + 1 < clamped && power < 4096)
            {
                power <<= 1;
            }

            return Mathf.Clamp(power + 1, 17, 4097);
        }

        private static void SmoothHeightsInPlace(float[,] heights, int passes)
        {
            if (heights == null || passes <= 0)
            {
                return;
            }

            int rows = heights.GetLength(0);
            int columns = heights.GetLength(1);
            if (rows < 3 || columns < 3)
            {
                return;
            }

            float[,] buffer = new float[rows, columns];
            int safePasses = Mathf.Clamp(passes, 0, 4);
            for (int pass = 0; pass < safePasses; pass++)
            {
                for (int z = 0; z < rows; z++)
                {
                    for (int x = 0; x < columns; x++)
                    {
                        if (z == 0 || x == 0 || z == rows - 1 || x == columns - 1)
                        {
                            buffer[z, x] = heights[z, x];
                            continue;
                        }

                        float center = heights[z, x] * 4f;
                        float cardinal = heights[z - 1, x] + heights[z + 1, x] + heights[z, x - 1] + heights[z, x + 1];
                        float diagonal = heights[z - 1, x - 1] + heights[z - 1, x + 1] + heights[z + 1, x - 1] + heights[z + 1, x + 1];
                        buffer[z, x] = Mathf.Clamp01((center + cardinal * 2f + diagonal) / 16f);
                    }
                }

                for (int z = 0; z < rows; z++)
                {
                    for (int x = 0; x < columns; x++)
                    {
                        heights[z, x] = buffer[z, x];
                    }
                }
            }
        }

        private void PaintRuntimeGrassDetails(RegionalWorldState state, RegionalTerrainTileRecord tile, TerrainData data, Vector2 openingAnchorMeters)
        {
            if (data == null || maxGrassDensity <= 0)
            {
                return;
            }

            DetailPrototype[] prototypes = ResolveRuntimeGrassDetailPrototypes();
            if (prototypes == null || prototypes.Length == 0)
            {
                return;
            }

            int resolution = EffectiveDetailResolution;
            int perPatch = Mathf.Clamp(detailResolutionPerPatch, 4, 64);
            data.detailPrototypes = prototypes;
            data.SetDetailResolution(resolution, perPatch);
            int[,] layer = new int[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float x01 = x / (float)Mathf.Max(1, resolution - 1);
                    float z01 = z / (float)Mathf.Max(1, resolution - 1);
                    Vector2 point = new(
                        Mathf.Lerp(tile.BoundsMeters.xMin, tile.BoundsMeters.xMax, x01),
                        Mathf.Lerp(tile.BoundsMeters.yMin, tile.BoundsMeters.yMax, z01));
                    layer[z, x] = ResolveGrassDensity(state, point, openingAnchorMeters);
                }
            }

            data.SetDetailLayer(0, 0, 0, layer);
        }

        private int ResolveGrassDensity(RegionalWorldState state, Vector2 point, Vector2 openingAnchorMeters)
        {
            float water = CalculateWaterInfluence01(state, point, out _);
            float route = CalculateRouteInfluence01(state, point);
            float settlement = CalculateSettlementInfluence01(state, point, 280f);
            float parcelGrade = CalculateParcelPadInfluence01(state, point);
            float height = SampleRegionalHeight01Base(state, point);
            float townClear = Mathf.Clamp01(1f - Vector2.Distance(point, openingAnchorMeters) / Mathf.Max(1f, openingTownFlattenRadiusMeters + 90f));
            float roughPrairie = Mathf.Clamp01(1f - route * 1.2f - settlement * 0.9f - parcelGrade * 0.75f - water * 0.55f - townClear * 0.85f);
            float heightPreference = Mathf.Clamp01(1f - Mathf.Abs(height - 0.46f) * 1.8f);
            float noise = Mathf.PerlinNoise(point.x * 0.045f + 17.3f, point.y * 0.045f + 41.9f);
            float density01 = roughPrairie * Mathf.Lerp(0.45f, 1f, noise) * Mathf.Lerp(0.55f, 1f, heightPreference);
            return Mathf.Clamp(Mathf.RoundToInt(density01 * maxGrassDensity), 0, maxGrassDensity);
        }

        private DetailPrototype[] ResolveRuntimeGrassDetailPrototypes()
        {
            if (runtimeGrassDetailPrototypes != null && runtimeGrassDetailPrototypes.Length > 0)
            {
                return runtimeGrassDetailPrototypes;
            }

            if (!createRuntimeFallbackAssets)
            {
                return null;
            }

            RecordRuntimeFallbackAssetUse("grass detail texture/prototype");
            runtimeGrassDetailTexture = CreateRuntimeGrassTexture();
            DetailPrototype grass = new()
            {
                prototypeTexture = runtimeGrassDetailTexture,
                renderMode = DetailRenderMode.GrassBillboard,
                healthyColor = runtimeGrassHealthyColor,
                dryColor = runtimeGrassDryColor,
                minWidth = 0.22f,
                maxWidth = 0.48f,
                minHeight = Mathf.Max(0.01f, grassMinHeightMeters),
                maxHeight = Mathf.Max(grassMinHeightMeters, grassMaxHeightMeters),
                noiseSpread = 0.65f
            };
            runtimeGrassDetailPrototypes = new[] { grass };
            return runtimeGrassDetailPrototypes;
        }

        private static Texture2D CreateRuntimeGrassTexture()
        {
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime Regional Grass Detail Texture",
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.clear);
            texture.SetPixel(1, 0, Color.white);
            texture.SetPixel(0, 1, Color.white);
            texture.SetPixel(1, 1, Color.clear);
            texture.Apply(false, true);
            return texture;
        }

        private void PaintAlphamaps(RegionalWorldState state, RegionalTerrainTileRecord tile, TerrainData data, int layerCount, Vector2 openingAnchorMeters)
        {
            if (data == null || layerCount <= 0)
            {
                return;
            }

            int alphaResolution = data.alphamapResolution;
            float[,,] maps = new float[alphaResolution, alphaResolution, layerCount];
            for (int z = 0; z < alphaResolution; z++)
            {
                for (int x = 0; x < alphaResolution; x++)
                {
                    float x01 = x / (float)Mathf.Max(1, alphaResolution - 1);
                    float z01 = z / (float)Mathf.Max(1, alphaResolution - 1);
                    Vector2 point = new(
                        Mathf.Lerp(tile.BoundsMeters.xMin, tile.BoundsMeters.xMax, x01),
                        Mathf.Lerp(tile.BoundsMeters.yMin, tile.BoundsMeters.yMax, z01));
                    WriteLayerWeights(state, point, openingAnchorMeters, maps, z, x, layerCount);
                }
            }

            data.SetAlphamaps(0, 0, maps);
        }

        private void WriteLayerWeights(RegionalWorldState state, Vector2 point, Vector2 openingAnchorMeters, float[,,] maps, int z, int x, int layerCount)
        {
            float water = CalculateWaterInfluence01(state, point, out _);
            float route = CalculateRouteInfluence01(state, point);
            float settlement = CalculateSettlementInfluence01(state, point, 260f);
            float parcelGrade = CalculateParcelPadInfluence01(state, point);
            float baseHeight = SampleRegionalHeight01Base(state, point);
            float distanceToTown = Vector2.Distance(point, openingAnchorMeters);
            float townDirt = Mathf.Clamp01(1f - distanceToTown / Mathf.Max(1f, openingTownFlattenRadiusMeters + 80f)) * 0.35f;
            float rock = Mathf.Clamp01((baseHeight - 0.54f) * 3.2f + CalculateRidgeNoise01(state, point) * 0.35f);
            float wetland = Mathf.Clamp01(water * 1.2f + Mathf.Clamp01(0.42f - baseHeight) * 0.6f);
            float dirt = Mathf.Clamp01(route * 1.35f + settlement * 0.45f + townDirt + parcelGrade * parcelPadDirtStrength);
            float grass = Mathf.Max(0.05f, 1f - dirt * 0.72f - wetland * 0.65f - rock * 0.42f);

            float[] weights = new float[layerCount];
            weights[0] = grass;
            if (layerCount > 1)
            {
                weights[1] = dirt;
            }
            else
            {
                weights[0] += dirt;
            }

            if (layerCount > 2)
            {
                weights[2] = wetland;
            }
            else
            {
                weights[0] += wetland;
            }

            if (layerCount > 3)
            {
                weights[3] = rock;
            }
            else
            {
                weights[0] += rock;
            }

            float total = 0f;
            for (int i = 0; i < layerCount; i++)
            {
                total += Mathf.Max(0f, weights[i]);
            }

            if (total <= 0.0001f)
            {
                maps[z, x, 0] = 1f;
                return;
            }

            for (int i = 0; i < layerCount; i++)
            {
                maps[z, x, i] = Mathf.Max(0f, weights[i]) / total;
            }
        }

        private float SampleRegionalHeight01Base(RegionalWorldState state, Vector2 point)
        {
            float broad = Mathf.PerlinNoise(state.Seed * 0.019f + point.x * 0.00036f, state.Seed * 0.023f + point.y * 0.00036f);
            float ridgeNoise = CalculateRidgeNoise01(state, point);
            float detail = Mathf.PerlinNoise(state.Seed * 0.071f + point.x * 0.0024f, state.Seed * 0.053f + point.y * 0.0024f);
            float micro = Mathf.PerlinNoise(state.Seed * 0.119f + point.x * 0.0065f, state.Seed * 0.101f + point.y * 0.0065f);
            float waterInfluence = CalculateWaterInfluence01(state, point, out _);
            float routeInfluence = CalculateRouteInfluence01(state, point);
            float waterCut = waterInfluence * watercourseCutStrength;
            float bankCut = Mathf.SmoothStep(0f, 1f, waterInfluence) * waterBankCutStrength;
            float settlementShelf = CalculateSettlementInfluence01(state, point, 320f) * settlementShelfStrength;
            float parcelPadGrade = CalculateParcelPadInfluence01(state, point) * parcelPadGradeStrength;
            float routeGrade = routeInfluence * routeGradeStrength;
            float routeShoulder = Mathf.SmoothStep(0f, 1f, routeInfluence) * routeShoulderStrength;
            float gradient = point.x / Mathf.Max(1f, state.RegionSizeMeters.x);

            float weighted = broad * broadReliefWeight
                + ridgeNoise * ridgeReliefWeight
                + detail * detailReliefWeight
                + micro * 0.055f
                + gradient * highlandGradientWeight
                - waterCut
                - bankCut
                - settlementShelf
                - parcelPadGrade
                - routeGrade
                - routeShoulder
                + 0.16f;
            return Mathf.Clamp01(weighted);
        }

        private float CalculateRidgeNoise01(RegionalWorldState state, Vector2 point)
        {
            float ridge = Mathf.PerlinNoise(state.Seed * 0.037f + point.x * 0.00072f, state.Seed * 0.041f + point.y * 0.00072f);
            return 1f - Mathf.Abs(ridge * 2f - 1f);
        }

        private float CalculateWaterInfluence01(RegionalWorldState state, Vector2 point, out RegionalWatercourseRecord nearestWatercourse)
        {
            nearestWatercourse = null;
            float strongest = 0f;
            if (state == null || state.Watercourses == null)
            {
                return 0f;
            }

            for (int i = 0; i < state.Watercourses.Count; i++)
            {
                RegionalWatercourseRecord water = state.Watercourses[i];
                if (water == null || water.Points == null || water.Points.Count < 2)
                {
                    continue;
                }

                float best = float.MaxValue;
                for (int p = 1; p < water.Points.Count; p++)
                {
                    best = Mathf.Min(best, DistancePointToSegment(point, water.Points[p - 1], water.Points[p]));
                }

                float influence = Mathf.Clamp01(1f - best / Mathf.Max(1f, water.InfluenceWidthMeters));
                if (influence > strongest)
                {
                    strongest = influence;
                    nearestWatercourse = water;
                }
            }

            return strongest;
        }

        private float CalculateSettlementInfluence01(RegionalWorldState state, Vector2 point, float radiusMeters)
        {
            float strongest = 0f;
            if (state == null || state.Settlements == null)
            {
                return 0f;
            }

            for (int i = 0; i < state.Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = state.Settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                float distance = Vector2.Distance(point, settlement.CenterMeters);
                float permanence = Mathf.Lerp(0.65f, 1.15f, settlement.Permanence01);
                strongest = Mathf.Max(strongest, Mathf.Clamp01(1f - distance / Mathf.Max(1f, radiusMeters)) * permanence);
            }

            return Mathf.Clamp01(strongest);
        }

        private float CalculateRouteInfluence01(RegionalWorldState state, Vector2 point)
        {
            float strongest = 0f;
            if (state == null || state.RouteCorridors == null)
            {
                return 0f;
            }

            for (int i = 0; i < state.RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = state.RouteCorridors[i];
                if (route == null || route.Points == null || route.Points.Count < 2)
                {
                    continue;
                }

                float best = float.MaxValue;
                for (int p = 1; p < route.Points.Count; p++)
                {
                    best = Mathf.Min(best, DistancePointToSegment(point, route.Points[p - 1], route.Points[p]));
                }

                float corridorWidth = route.Kind == RegionalRouteCorridorKind.OpeningFootholdSpine ? 58f : 34f;
                float influence = Mathf.Clamp01(1f - best / corridorWidth) * Mathf.Lerp(0.78f, 1.16f, route.Practicality01);
                strongest = Mathf.Max(strongest, influence);
            }

            return Mathf.Clamp01(strongest);
        }

        private float ApplyOpeningTownShelf(Vector2 point, Vector2 openingAnchorMeters, float baseHeight01, float anchorHeight01)
        {
            if (!normalizeOpeningTownHeight)
            {
                return baseHeight01;
            }

            float flattenRadius = Mathf.Max(0f, openingTownFlattenRadiusMeters);
            float blendRadius = Mathf.Max(1f, openingTownBlendRadiusMeters);
            float distance = Vector2.Distance(point, openingAnchorMeters);
            if (distance <= flattenRadius)
            {
                return anchorHeight01;
            }

            float blend01 = Mathf.Clamp01((distance - flattenRadius) / blendRadius);
            float smoothBlend = blend01 * blend01 * (3f - 2f * blend01);
            return Mathf.Lerp(anchorHeight01, baseHeight01, smoothBlend);
        }

        private int BuildWatercourseMeshes(RegionalWorldState state, Transform root, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters)
        {
            LastWatercourseVisualStatus = "No watercourse records available.";
            if (state == null || state.Watercourses == null || state.Watercourses.Count == 0)
            {
                return 0;
            }

            bool useAuthoredPrefab = ShouldUseAuthoredRiverPrefab();
            if (!useAuthoredPrefab && useAuthoredRiverPrefab && authoredRiverPrefab == null)
            {
                WarnMissingAuthoredRiverPrefabOnce();
                if (!buildFallbackWaterMeshesWhenAuthoredRiverPrefabMissing)
                {
                    LastWatercourseVisualStatus = "Authored River prefab missing; fallback ribbon meshes disabled.";
                    return 0;
                }

                LastWatercourseVisualStatus = "Authored River prefab missing; generated fallback ribbon meshes used.";
            }
            else
            {
                LastWatercourseVisualStatus = useAuthoredPrefab
                    ? $"Authored River prefab '{authoredRiverPrefab.name}' used."
                    : "Generated fallback ribbon meshes used.";
            }

            Transform waterRoot = EnsureChildRoot(root, WaterRootName);
            int built = 0;
            int authoredSegmentCount = 0;
            int fallbackMeshCount = 0;
            for (int i = 0; i < state.Watercourses.Count; i++)
            {
                RegionalWatercourseRecord water = state.Watercourses[i];
                if (!ShouldBuildWatercourse(state, water))
                {
                    continue;
                }

                int segmentCount = 0;
                GameObject waterObject = useAuthoredPrefab
                    ? CreateAuthoredWatercoursePrefabSegments(state, water, waterRoot, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters, out segmentCount)
                    : CreateWatercourseMesh(state, water, waterRoot, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters);
                if (waterObject != null)
                {
                    generatedWaterObjects.Add(waterObject);
                    authoredSegmentCount += useAuthoredPrefab ? segmentCount : 0;
                    fallbackMeshCount += useAuthoredPrefab ? 0 : 1;
                    built++;
                }

                if (useAuthoredPrefab && buildFallbackWaterMeshWithAuthoredRiverPrefab)
                {
                    GameObject fallbackWaterObject = CreateWatercourseMesh(state, water, waterRoot, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters);
                    if (fallbackWaterObject != null)
                    {
                        fallbackWaterObject.name += " (fallback ribbon)";
                        generatedWaterObjects.Add(fallbackWaterObject);
                        fallbackMeshCount++;
                    }
                }
            }

            if (built <= 0)
            {
                LastWatercourseVisualStatus = useAuthoredPrefab
                    ? $"Authored River prefab '{authoredRiverPrefab.name}' assigned, but no watercourses passed the active build filters."
                    : LastWatercourseVisualStatus;
            }
            else if (useAuthoredPrefab)
            {
                string fallbackRead = fallbackMeshCount > 0 ? $", fallback ribbon meshes {fallbackMeshCount}" : string.Empty;
                LastWatercourseVisualStatus = $"Authored River prefab '{authoredRiverPrefab.name}' used for {authoredSegmentCount} segment(s){fallbackRead}.";
            }
            else if (fallbackMeshCount > 0)
            {
                LastWatercourseVisualStatus = $"Generated fallback ribbon meshes used for {fallbackMeshCount} watercourse(s).";
            }

            return built;
        }

        private bool ShouldUseAuthoredRiverPrefab()
        {
            return useAuthoredRiverPrefab && authoredRiverPrefab != null;
        }

        private void WarnMissingAuthoredRiverPrefabOnce()
        {
            if (!warnWhenAuthoredRiverPrefabMissing || hasWarnedMissingAuthoredRiverPrefab)
            {
                return;
            }

            hasWarnedMissingAuthoredRiverPrefab = true;
            string fallbackRead = buildFallbackWaterMeshesWhenAuthoredRiverPrefabMissing
                ? "Generated watercourse ribbon meshes are being used as fallback water visuals."
                : "Fallback ribbon meshes are disabled to avoid oversized or bad-material placeholder water. Assign Assets/Environment/Water/River.prefab to the RegionalTerrainTileView authored River prefab slot, or enable the missing-prefab fallback flag for deliberate debug previews.";
            Debug.LogWarning($"[RegionalTerrain] Authored River prefab is not assigned. {fallbackRead}", this);
        }

        private bool ShouldBuildWatercourse(RegionalWorldState state, RegionalWatercourseRecord water)
        {
            if (!buildOnlyTilesNearOpeningAnchor || state == null || water == null || water.Points == null || water.Points.Count == 0)
            {
                return water != null && water.Points != null && water.Points.Count >= 2;
            }

            Vector2 anchor = ResolveOpeningAnchorMeters(state);
            float maxDistance = Mathf.Max(64f, openingAnchorTileRadiusMeters) + Mathf.Max(waterRibbonMaxWidthMeters, water.InfluenceWidthMeters);
            for (int i = 0; i < water.Points.Count; i++)
            {
                if (Vector2.Distance(anchor, water.Points[i]) <= maxDistance)
                {
                    return water.Points.Count >= 2;
                }
            }

            return false;
        }

        private GameObject CreateWatercourseMesh(RegionalWorldState state, RegionalWatercourseRecord water, Transform waterRoot, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters)
        {
            if (water == null || water.Points == null || water.Points.Count < 2)
            {
                return null;
            }

            float width = Mathf.Clamp(water.InfluenceWidthMeters * waterRibbonWidthMultiplier, waterRibbonMinWidthMeters, Mathf.Max(waterRibbonMinWidthMeters, waterRibbonMaxWidthMeters));
            List<Vector3> vertices = new(water.Points.Count * 2);
            List<Vector2> uvs = new(water.Points.Count * 2);
            List<int> triangles = new((water.Points.Count - 1) * 6);
            for (int i = 0; i < water.Points.Count; i++)
            {
                Vector2 point = water.Points[i];
                Vector2 previous = i > 0 ? water.Points[i - 1] : point;
                Vector2 next = i < water.Points.Count - 1 ? water.Points[i + 1] : point;
                Vector2 tangent = next - previous;
                if (tangent.sqrMagnitude <= 0.0001f)
                {
                    tangent = Vector2.right;
                }

                tangent.Normalize();
                Vector2 normal = new(-tangent.y, tangent.x);
                Vector2 left = point + normal * (width * 0.5f);
                Vector2 right = point - normal * (width * 0.5f);
                float y = SampleWorldHeightMeters(state, point, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters) + waterSurfaceOffsetMeters;
                Vector3 leftWorld = ToSceneWorld(left);
                Vector3 rightWorld = ToSceneWorld(right);
                leftWorld.y = y;
                rightWorld.y = y;
                vertices.Add(leftWorld);
                vertices.Add(rightWorld);
                float v = i / (float)Mathf.Max(1, water.Points.Count - 1);
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));

                if (i > 0)
                {
                    int baseIndex = i * 2;
                    triangles.Add(baseIndex - 2);
                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex - 1);
                    triangles.Add(baseIndex - 1);
                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + 1);
                }
            }

            Mesh mesh = new()
            {
                name = $"RegionalWatercourseMesh_{water.WatercourseId}"
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject waterObject = new($"{GeneratedWaterPrefix}{water.WatercourseId}");
            waterObject.transform.SetParent(waterRoot, false);
            waterObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            waterObject.transform.localScale = Vector3.one;
            MeshFilter filter = waterObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = waterObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ResolveWaterMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (addWaterMeshColliders)
            {
                MeshCollider collider = waterObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
            }

            return waterObject;
        }

        private GameObject CreateAuthoredWatercoursePrefabSegments(
            RegionalWorldState state,
            RegionalWatercourseRecord water,
            Transform waterRoot,
            Vector2 openingAnchorMeters,
            float anchorHeight01,
            float terrainYOffsetMeters,
            out int segmentCount)
        {
            segmentCount = 0;
            if (authoredRiverPrefab == null || water == null || water.Points == null || water.Points.Count < 2)
            {
                return null;
            }

            float width = Mathf.Clamp(water.InfluenceWidthMeters * waterRibbonWidthMultiplier, waterRibbonMinWidthMeters, Mathf.Max(waterRibbonMinWidthMeters, waterRibbonMaxWidthMeters));
            float nominalWidth = Mathf.Max(0.1f, authoredRiverPrefabNominalWidthMeters);
            float nominalLength = Mathf.Max(0.1f, authoredRiverPrefabNominalLengthMeters);
            float maxSegmentLength = Mathf.Max(1f, authoredRiverSegmentMaxLengthMeters);

            GameObject waterObject = new($"{GeneratedWaterPrefix}{water.WatercourseId}");
            waterObject.transform.SetParent(waterRoot, false);
            waterObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            waterObject.transform.localScale = Vector3.one;

            for (int i = 1; i < water.Points.Count; i++)
            {
                Vector2 start = water.Points[i - 1];
                Vector2 end = water.Points[i];
                Vector2 delta = end - start;
                float length = delta.magnitude;
                if (length <= 0.01f)
                {
                    continue;
                }

                int subdivisions = Mathf.Max(1, Mathf.CeilToInt(length / maxSegmentLength));
                for (int segmentIndex = 0; segmentIndex < subdivisions; segmentIndex++)
                {
                    float t0 = segmentIndex / (float)subdivisions;
                    float t1 = (segmentIndex + 1) / (float)subdivisions;
                    Vector2 segmentStart = Vector2.Lerp(start, end, t0);
                    Vector2 segmentEnd = Vector2.Lerp(start, end, t1);
                    Vector2 segmentMidpoint = (segmentStart + segmentEnd) * 0.5f;
                    Vector2 segmentDelta = segmentEnd - segmentStart;
                    float segmentLength = segmentDelta.magnitude;
                    if (segmentLength <= 0.01f)
                    {
                        continue;
                    }

                    float y = SampleWorldHeightMeters(state, segmentMidpoint, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters) + waterSurfaceOffsetMeters;
                    Vector3 scenePoint = ToSceneWorld(segmentMidpoint);
                    scenePoint.y = y;

                    float yaw = Mathf.Atan2(segmentDelta.x, segmentDelta.y) * Mathf.Rad2Deg;
                    Quaternion rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(authoredRiverPrefabEulerOffset);
                    Vector3 scale = new(
                        Mathf.Max(0.01f, width / nominalWidth),
                        1f,
                        Mathf.Max(0.01f, segmentLength / nominalLength));

                    GameObject segmentRoot = new($"Authored River Segment {segmentCount + 1:00}");
                    segmentRoot.transform.SetParent(waterObject.transform, false);
                    segmentRoot.transform.SetPositionAndRotation(scenePoint, rotation);
                    segmentRoot.transform.localScale = scale;

                    GameObject instance = Instantiate(authoredRiverPrefab, segmentRoot.transform);
                    instance.name = authoredRiverPrefab.name + " (authored regional water)";
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    instance.transform.localScale = authoredRiverPrefab.transform.localScale;
                    segmentCount++;
                }
            }

            if (segmentCount <= 0)
            {
                DestroyUnityObject(waterObject);
                return null;
            }

            return waterObject;
        }

        private float SampleWorldHeightMeters(RegionalWorldState state, Vector2 point, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters)
        {
            float baseHeight = SampleRegionalHeight01Base(state, point);
            float finalHeight01 = Mathf.Clamp01(ApplyOpeningTownShelf(point, openingAnchorMeters, baseHeight, anchorHeight01));
            return finalHeight01 * EffectiveHeightScaleMeters + terrainYOffsetMeters;
        }



        private int BuildRouteCorridorMeshes(RegionalWorldState state, Transform root, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters)
        {
            if (state == null || state.RouteCorridors == null || state.RouteCorridors.Count == 0)
            {
                return 0;
            }

            Transform routeRoot = EnsureChildRoot(root, RouteRootName);
            int built = 0;
            for (int i = 0; i < state.RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = state.RouteCorridors[i];
                if (!ShouldBuildRouteCorridor(state, route))
                {
                    continue;
                }

                GameObject routeObject = CreateRouteCorridorMesh(state, route, routeRoot, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters);
                if (routeObject != null)
                {
                    built++;
                }
            }

            return built;
        }

        private bool ShouldBuildRouteCorridor(RegionalWorldState state, RegionalRouteCorridorRecord route)
        {
            if (route == null || route.Points == null || route.Points.Count < 2)
            {
                return false;
            }

            if (RouteCrossesOpeningTownExclusion(state, route))
            {
                return false;
            }

            if (!buildOnlyTilesNearOpeningAnchor || state == null)
            {
                return true;
            }

            Vector2 anchor = ResolveOpeningAnchorMeters(state);
            float maxDistance = Mathf.Max(64f, openingAnchorTileRadiusMeters) + Mathf.Max(routeRibbonMaxWidthMeters, 96f);
            for (int i = 0; i < route.Points.Count; i++)
            {
                if (Vector2.Distance(anchor, route.Points[i]) <= maxDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool RouteCrossesOpeningTownExclusion(RegionalWorldState state, RegionalRouteCorridorRecord route)
        {
            if (!suppressRouteRibbonsInsideOpeningTown || state == null || route == null || route.Points == null || route.Points.Count < 2)
            {
                return false;
            }

            TownWorldController controller = ResolveTownWorld();
            TownGenerationSettings settings = controller != null ? controller.Settings : null;
            if (settings == null)
            {
                return false;
            }

            float padding = routeOpeningTownExclusionPaddingMeters + ResolveRouteRibbonWidth(route) * 0.5f;
            float halfWidth = settings.gridWidthCells * settings.cellSizeMeters * 0.5f + padding;
            float halfDepth = settings.gridDepthCells * settings.cellSizeMeters * 0.5f + padding;
            Vector2 center = ResolveOpeningAnchorMeters(state);
            Rect exclusion = new(center.x - halfWidth, center.y - halfDepth, halfWidth * 2f, halfDepth * 2f);

            for (int i = 1; i < route.Points.Count; i++)
            {
                if (SegmentIntersectsRect(route.Points[i - 1], route.Points[i], exclusion))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentIntersectsRect(Vector2 start, Vector2 end, Rect rect)
        {
            if (rect.Contains(start) || rect.Contains(end))
            {
                return true;
            }

            Vector2 bottomLeft = new(rect.xMin, rect.yMin);
            Vector2 bottomRight = new(rect.xMax, rect.yMin);
            Vector2 topRight = new(rect.xMax, rect.yMax);
            Vector2 topLeft = new(rect.xMin, rect.yMax);
            return SegmentsIntersect(start, end, bottomLeft, bottomRight)
                || SegmentsIntersect(start, end, bottomRight, topRight)
                || SegmentsIntersect(start, end, topRight, topLeft)
                || SegmentsIntersect(start, end, topLeft, bottomLeft);
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(b - a, c - a);
            float d2 = Cross(b - a, d - a);
            float d3 = Cross(d - c, a - c);
            float d4 = Cross(d - c, b - c);

            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
                && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
            {
                return true;
            }

            const float epsilon = 0.0001f;
            return Mathf.Abs(d1) <= epsilon && PointOnSegment(c, a, b)
                || Mathf.Abs(d2) <= epsilon && PointOnSegment(d, a, b)
                || Mathf.Abs(d3) <= epsilon && PointOnSegment(a, c, d)
                || Mathf.Abs(d4) <= epsilon && PointOnSegment(b, c, d);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static bool PointOnSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            return point.x >= Mathf.Min(start.x, end.x) - 0.0001f
                && point.x <= Mathf.Max(start.x, end.x) + 0.0001f
                && point.y >= Mathf.Min(start.y, end.y) - 0.0001f
                && point.y <= Mathf.Max(start.y, end.y) + 0.0001f;
        }

        private GameObject CreateRouteCorridorMesh(RegionalWorldState state, RegionalRouteCorridorRecord route, Transform routeRoot, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters)
        {
            if (route == null || route.Points == null || route.Points.Count < 2)
            {
                return null;
            }

            float width = ResolveRouteRibbonWidth(route);
            List<Vector3> vertices = new(route.Points.Count * 2);
            List<Vector2> uvs = new(route.Points.Count * 2);
            List<int> triangles = new((route.Points.Count - 1) * 6);
            for (int i = 0; i < route.Points.Count; i++)
            {
                Vector2 point = route.Points[i];
                Vector2 previous = i > 0 ? route.Points[i - 1] : point;
                Vector2 next = i < route.Points.Count - 1 ? route.Points[i + 1] : point;
                Vector2 tangent = next - previous;
                if (tangent.sqrMagnitude <= 0.0001f)
                {
                    tangent = Vector2.right;
                }

                tangent.Normalize();
                Vector2 normal = new(-tangent.y, tangent.x);
                Vector2 left = point + normal * (width * 0.5f);
                Vector2 right = point - normal * (width * 0.5f);
                float y = SampleWorldHeightMeters(state, point, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters) + routeSurfaceOffsetMeters;
                Vector3 leftWorld = ToSceneWorld(left);
                Vector3 rightWorld = ToSceneWorld(right);
                leftWorld.y = y;
                rightWorld.y = y;
                vertices.Add(leftWorld);
                vertices.Add(rightWorld);
                float v = i / (float)Mathf.Max(1, route.Points.Count - 1);
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));

                if (i > 0)
                {
                    int baseIndex = i * 2;
                    triangles.Add(baseIndex - 2);
                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex - 1);
                    triangles.Add(baseIndex - 1);
                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + 1);
                }
            }

            Mesh mesh = new()
            {
                name = $"RegionalRouteCorridorMesh_{route.CorridorId}"
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject routeObject = new($"{GeneratedRoutePrefix}{route.CorridorId} ({route.Kind}, burden {route.FreightBurden01:P0})");
            routeObject.transform.SetParent(routeRoot, false);
            routeObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            routeObject.transform.localScale = Vector3.one;
            MeshFilter filter = routeObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = routeObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ResolveRouteMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = routeRibbonReceivesShadows;
            return routeObject;
        }

        private float ResolveRouteRibbonWidth(RegionalRouteCorridorRecord route)
        {
            float baseWidth = route != null && route.Kind == RegionalRouteCorridorKind.OpeningFootholdSpine ? 32f : 18f;
            float burdenBonus = route != null ? Mathf.Lerp(0f, 10f, route.FreightBurden01) : 0f;
            return Mathf.Clamp((baseWidth + burdenBonus) * routeRibbonWidthMultiplier, routeRibbonMinWidthMeters, Mathf.Max(routeRibbonMinWidthMeters, routeRibbonMaxWidthMeters));
        }

        private int BuildScenicVegetationProps(RegionalWorldState state, Transform root, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters)
        {
            if (state == null || state.VegetationZones == null || state.VegetationZones.Count == 0 || ResolveScenicPropCap() <= 0)
            {
                return 0;
            }

            Transform scenicRoot = EnsureChildRoot(root, ScenicRootName);
            int built = 0;
            for (int i = 0; i < state.VegetationZones.Count && built < ResolveScenicPropCap(); i++)
            {
                RegionalVegetationZoneRecord zone = state.VegetationZones[i];
                if (!ShouldBuildVegetationZone(state, zone))
                {
                    continue;
                }

                built += PopulateScenicVegetationZone(state, zone, scenicRoot, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters, ResolveScenicPropCap() - built);
            }

            return built;
        }

        private bool ShouldBuildVegetationZone(RegionalWorldState state, RegionalVegetationZoneRecord zone)
        {
            if (zone == null || zone.Density01 <= 0.05f)
            {
                return false;
            }

            if (!buildOnlyTilesNearOpeningAnchor || state == null)
            {
                return true;
            }

            Vector2 anchor = ResolveOpeningAnchorMeters(state);
            Vector2 closest = new(
                Mathf.Clamp(anchor.x, zone.BoundsMeters.xMin, zone.BoundsMeters.xMax),
                Mathf.Clamp(anchor.y, zone.BoundsMeters.yMin, zone.BoundsMeters.yMax));
            return Vector2.Distance(anchor, closest) <= Mathf.Max(64f, openingAnchorTileRadiusMeters) + scenicPropSpacingMeters;
        }

        private int PopulateScenicVegetationZone(RegionalWorldState state, RegionalVegetationZoneRecord zone, Transform scenicRoot, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters, int budget)
        {
            if (budget <= 0)
            {
                return 0;
            }

            float spacing = Mathf.Max(8f, scenicPropSpacingMeters);
            int built = 0;
            int xCount = Mathf.Max(1, Mathf.CeilToInt(zone.BoundsMeters.width / spacing));
            int zCount = Mathf.Max(1, Mathf.CeilToInt(zone.BoundsMeters.height / spacing));
            for (int z = 0; z < zCount && built < budget; z++)
            {
                for (int x = 0; x < xCount && built < budget; x++)
                {
                    Vector2 point = ResolveScenicSamplePoint(state, zone, x, z, xCount, zCount);
                    if (!ShouldPlaceScenicProp(state, zone, point, openingAnchorMeters))
                    {
                        continue;
                    }

                    float height = SampleWorldHeightMeters(state, point, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters);
                    GameObject prop = CreateScenicVegetationProp(state, zone, point, height, scenicRoot);
                    if (prop != null)
                    {
                        built++;
                    }
                }
            }

            return built;
        }

        private Vector2 ResolveScenicSamplePoint(RegionalWorldState state, RegionalVegetationZoneRecord zone, int x, int z, int xCount, int zCount)
        {
            float x01 = (x + 0.5f) / Mathf.Max(1f, xCount);
            float z01 = (z + 0.5f) / Mathf.Max(1f, zCount);
            float jitterX = Mathf.PerlinNoise(state.Seed * 0.137f + x * 1.73f, state.Seed * 0.149f + z * 2.11f) - 0.5f;
            float jitterZ = Mathf.PerlinNoise(state.Seed * 0.163f + x * 2.41f, state.Seed * 0.179f + z * 1.67f) - 0.5f;
            float spacing = Mathf.Max(8f, scenicPropSpacingMeters);
            return new Vector2(
                Mathf.Clamp(Mathf.Lerp(zone.BoundsMeters.xMin, zone.BoundsMeters.xMax, x01) + jitterX * spacing * 0.62f, zone.BoundsMeters.xMin, zone.BoundsMeters.xMax),
                Mathf.Clamp(Mathf.Lerp(zone.BoundsMeters.yMin, zone.BoundsMeters.yMax, z01) + jitterZ * spacing * 0.62f, zone.BoundsMeters.yMin, zone.BoundsMeters.yMax));
        }

        private bool ShouldPlaceScenicProp(RegionalWorldState state, RegionalVegetationZoneRecord zone, Vector2 point, Vector2 openingAnchorMeters)
        {
            if (Vector2.Distance(point, openingAnchorMeters) <= scenicPropAvoidTownRadiusMeters)
            {
                return false;
            }

            float route = CalculateRouteInfluence01(state, point);
            if (route >= scenicPropAvoidRouteInfluence)
            {
                return false;
            }

            float parcelPad = CalculateParcelPadInfluence01(state, point);
            if (parcelPad >= scenicPropAvoidParcelPadInfluence)
            {
                return false;
            }

            float noise = Mathf.PerlinNoise(state.Seed * 0.211f + point.x * 0.017f, state.Seed * 0.223f + point.y * 0.017f);
            float threshold = Mathf.Lerp(0.88f, 0.22f, Mathf.Clamp01(zone.Density01 * scenicPropDensityMultiplier));
            if (noise < threshold)
            {
                return false;
            }

            if (zone.VegetationKind == RegionalVegetationKind.RiparianTrees || zone.VegetationKind == RegionalVegetationKind.WetGround)
            {
                float water = CalculateWaterInfluence01(state, point, out _);
                return water >= 0.08f || zone.Wetness01 >= 0.45f;
            }

            return true;
        }

        private GameObject CreateScenicVegetationProp(RegionalWorldState state, RegionalVegetationZoneRecord zone, Vector2 regionalPoint, float groundY, Transform scenicRoot)
        {
            Vector3 scenePoint = ToSceneWorld(regionalPoint);
            scenePoint.y = groundY;
            float noise = Mathf.PerlinNoise(state.Seed * 0.251f + regionalPoint.x * 0.031f, state.Seed * 0.263f + regionalPoint.y * 0.031f);
            float scale = Mathf.Lerp(Mathf.Max(0.01f, scenicPropMinScale), Mathf.Max(scenicPropMinScale, scenicPropMaxScale), noise);
            GameObject root = new($"{GeneratedScenicPrefix}{zone.VegetationKind} {zone.ZoneId}");
            root.transform.SetParent(scenicRoot, false);
            root.transform.SetPositionAndRotation(scenePoint, Quaternion.Euler(0f, noise * 360f, 0f));
            root.transform.localScale = Vector3.one;

            switch (zone.VegetationKind)
            {
                case RegionalVegetationKind.RiparianTrees:
                case RegionalVegetationKind.TimberStand:
                    CreateTreeProp(root.transform, scale, zone.VegetationKind == RegionalVegetationKind.RiparianTrees ? 0.85f : 1.18f);
                    break;
                case RegionalVegetationKind.BrushAndScrub:
                    CreateScrubProp(root.transform, scale);
                    break;
                case RegionalVegetationKind.WetGround:
                    CreateWetReedProp(root.transform, scale);
                    break;
                case RegionalVegetationKind.SparseRoughCountry:
                    CreateRockProp(root.transform, scale);
                    break;
                default:
                    CreateGrassClumpProp(root.transform, scale);
                    break;
            }

            return root;
        }

        private void CreateTreeProp(Transform root, float scale, float heightMultiplier)
        {
            if (TryInstantiateRuntimePrefabChild(scenicTreePrefab, root, "authored tree fallback", Vector3.zero, Quaternion.identity, Vector3.one * Mathf.Max(0.01f, scale)))
            {
                return;
            }

            float trunkHeight = Mathf.Lerp(3.0f, 6.2f, Mathf.Clamp01(scale / Mathf.Max(0.01f, scenicPropMaxScale))) * heightMultiplier;
            float trunkRadius = Mathf.Lerp(0.18f, 0.42f, scale);
            GameObject trunk = CreatePrimitiveChild(root, PrimitiveType.Cylinder, "Trunk", ResolveTreeTrunkMaterial());
            trunk.transform.localPosition = new Vector3(0f, trunkHeight * 0.5f, 0f);
            trunk.transform.localScale = new Vector3(trunkRadius, trunkHeight * 0.5f, trunkRadius);

            GameObject canopy = CreatePrimitiveChild(root, PrimitiveType.Sphere, "Canopy", ResolveTreeFoliageMaterial());
            canopy.transform.localPosition = new Vector3(0f, trunkHeight, 0f);
            canopy.transform.localScale = new Vector3(2.5f * scale, 1.5f * scale, 2.5f * scale);
        }

        private void CreateScrubProp(Transform root, float scale)
        {
            if (TryInstantiateRuntimePrefabChild(scenicScrubPrefab, root, "authored scrub fallback", Vector3.zero, Quaternion.identity, Vector3.one * Mathf.Max(0.01f, scale)))
            {
                return;
            }

            GameObject scrub = CreatePrimitiveChild(root, PrimitiveType.Sphere, "Scrub", ResolveScrubMaterial());
            scrub.transform.localPosition = new Vector3(0f, 0.38f * scale, 0f);
            scrub.transform.localScale = new Vector3(1.8f * scale, 0.65f * scale, 1.5f * scale);
        }

        private void CreateWetReedProp(Transform root, float scale)
        {
            if (TryInstantiateRuntimePrefabChild(scenicWetReedPrefab, root, "authored wet reed fallback", Vector3.zero, Quaternion.identity, Vector3.one * Mathf.Max(0.01f, scale)))
            {
                return;
            }

            for (int i = 0; i < 3; i++)
            {
                GameObject reed = CreatePrimitiveChild(root, PrimitiveType.Cylinder, $"Reed {i + 1}", ResolveWetReedMaterial());
                float offset = (i - 1) * 0.34f * scale;
                reed.transform.localPosition = new Vector3(offset, 0.6f * scale, -offset * 0.45f);
                reed.transform.localScale = new Vector3(0.055f * scale, 0.6f * scale, 0.055f * scale);
            }
        }

        private void CreateRockProp(Transform root, float scale)
        {
            if (TryInstantiateRuntimePrefabChild(scenicRockPrefab, root, "authored rock fallback", Vector3.zero, Quaternion.identity, Vector3.one * Mathf.Max(0.01f, scale)))
            {
                return;
            }

            GameObject rock = CreatePrimitiveChild(root, PrimitiveType.Sphere, "Rock", ResolveRockMaterial());
            rock.transform.localPosition = new Vector3(0f, 0.18f * scale, 0f);
            rock.transform.localScale = new Vector3(1.6f * scale, 0.34f * scale, 1.1f * scale);
        }

        private void CreateGrassClumpProp(Transform root, float scale)
        {
            if (TryInstantiateRuntimePrefabChild(scenicGrassClumpPrefab, root, "authored grass clump fallback", Vector3.zero, Quaternion.identity, Vector3.one * Mathf.Max(0.01f, scale)))
            {
                return;
            }

            GameObject clump = CreatePrimitiveChild(root, PrimitiveType.Sphere, "Grass Clump", ResolveScrubMaterial());
            clump.transform.localPosition = new Vector3(0f, 0.18f * scale, 0f);
            clump.transform.localScale = new Vector3(1.25f * scale, 0.28f * scale, 1.25f * scale);
        }

        private bool TryInstantiateRuntimePrefabChild(GameObject prefab, Transform parent, string fallbackName, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            if (prefab == null)
            {
                return false;
            }

            GameObject instance = Instantiate(prefab, parent);
            instance.name = prefab.name + " (runtime regional evidence)";
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = localScale;
            EnsureRenderersHaveFallbackMaterials(instance, fallbackName);
            return true;
        }

        private void EnsureRenderersHaveFallbackMaterials(GameObject instance, string fallbackLabel)
        {
            if (instance == null || !createRuntimeFallbackAssets)
            {
                return;
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                if (materials == null || materials.Length == 0)
                {
                    materials = new[] { ResolveScrubMaterial() };
                    changed = true;
                }

                for (int m = 0; m < materials.Length; m++)
                {
                    if (materials[m] == null)
                    {
                        materials[m] = ResolveScrubMaterial();
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    RecordRuntimeFallbackAssetUse(fallbackLabel + " material repair");
                }
            }
        }

        private GameObject CreatePrimitiveChild(Transform parent, PrimitiveType primitiveType, string name, Material material)
        {
            RecordRuntimeFallbackAssetUse($"primitive {name}");
            GameObject child = GameObject.CreatePrimitive(primitiveType);
            child.name = name;
            child.transform.SetParent(parent, false);
            Collider collider = child.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyUnityObject(collider);
            }

            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            return child;
        }


        private int BuildSettlementEvidenceProps(RegionalWorldState state, Transform root, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters)
        {
            if (state == null || state.Settlements == null || state.Settlements.Count == 0 || maxSettlementEvidenceProps <= 0)
            {
                return 0;
            }

            Transform settlementRoot = EnsureChildRoot(root, SettlementEvidenceRootName);
            int built = 0;
            for (int i = 0; i < state.Settlements.Count && built < maxSettlementEvidenceProps; i++)
            {
                RegionalSettlementRecord settlement = state.Settlements[i];
                if (!ShouldBuildSettlementEvidence(state, settlement, openingAnchorMeters))
                {
                    continue;
                }

                float y = SampleWorldHeightMeters(state, settlement.CenterMeters, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters) + settlementEvidenceSurfaceOffsetMeters;
                GameObject marker = CreateSettlementEvidenceProp(settlement, y, settlementRoot);
                if (marker != null)
                {
                    built++;
                }
            }

            return built;
        }

        private bool ShouldBuildSettlementEvidence(RegionalWorldState state, RegionalSettlementRecord settlement, Vector2 openingAnchorMeters)
        {
            if (settlement == null)
            {
                return false;
            }

            if (!buildOnlyTilesNearOpeningAnchor || state == null)
            {
                return true;
            }

            float radius = Mathf.Max(64f, openingAnchorTileRadiusMeters) + 360f;
            return Vector2.Distance(openingAnchorMeters, settlement.CenterMeters) <= radius;
        }

        private GameObject CreateSettlementEvidenceProp(RegionalSettlementRecord settlement, float groundY, Transform settlementRoot)
        {
            Vector3 scenePoint = ToSceneWorld(settlement.CenterMeters);
            scenePoint.y = groundY;
            GameObject marker = new($"{GeneratedSettlementEvidencePrefix}{settlement.SettlementId} ({settlement.Label}, pop {settlement.Population})");
            marker.transform.SetParent(settlementRoot, false);
            marker.transform.SetPositionAndRotation(scenePoint, Quaternion.identity);
            marker.transform.localScale = Vector3.one;

            float scale = Mathf.Lerp(0.75f, 1.45f, Mathf.Clamp01(settlement.RegionalGravity01));
            if (TryInstantiateRuntimePrefabChild(settlementEvidencePrefab, marker.transform, "authored settlement evidence", Vector3.zero, Quaternion.identity, Vector3.one * scale))
            {
                return marker;
            }


            GameObject basePad = CreatePrimitiveChild(marker.transform, PrimitiveType.Cube, "settlement pad", ResolveSettlementEvidenceMaterial());
            basePad.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            basePad.transform.localScale = new Vector3(3.5f * scale, 0.07f, 2.2f * scale);

            GameObject post = CreatePrimitiveChild(marker.transform, PrimitiveType.Cylinder, "settlement post", ResolveSettlementEvidenceMaterial());
            post.transform.localPosition = new Vector3(0f, 0.75f * scale, 0f);
            post.transform.localScale = new Vector3(0.11f * scale, 0.75f * scale, 0.11f * scale);

            if (settlement.ServiceDeficit01 >= 0.45f || settlement.DeclineRisk01 >= 0.42f)
            {
                GameObject warningStone = CreatePrimitiveChild(marker.transform, PrimitiveType.Sphere, "pressure marker", ResolveRockMaterial());
                warningStone.transform.localPosition = new Vector3(1.25f * scale, 0.18f * scale, 0.72f * scale);
                warningStone.transform.localScale = new Vector3(0.55f * scale, 0.22f * scale, 0.42f * scale);
            }

            if (settlement.FreightSupport01 >= 0.48f)
            {
                GameObject crate = CreatePrimitiveChild(marker.transform, PrimitiveType.Cube, "freight crate marker", ResolveRouteMaterial());
                crate.transform.localPosition = new Vector3(-1.12f * scale, 0.24f * scale, -0.45f * scale);
                crate.transform.localScale = new Vector3(0.55f * scale, 0.48f * scale, 0.55f * scale);
            }

            return marker;
        }

        private int BuildParcelBoundaryStakes(RegionalWorldState state, Transform root, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters)
        {
            if (state == null || state.SurveyParcels == null || state.SurveyParcels.Count == 0 || ResolveParcelStakeCap() <= 0)
            {
                return 0;
            }

            Transform parcelRoot = EnsureChildRoot(root, ParcelEvidenceRootName);
            int built = 0;
            for (int i = 0; i < state.SurveyParcels.Count && built < ResolveParcelStakeCap(); i++)
            {
                RegionalSurveyParcelRecord parcel = state.SurveyParcels[i];
                if (!ShouldBuildParcelStakeEvidence(parcel, openingAnchorMeters))
                {
                    continue;
                }

                built += CreateParcelCornerStakes(state, parcel, parcelRoot, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters, ResolveParcelStakeCap() - built);
            }

            return built;
        }

        private bool ShouldBuildParcelStakeEvidence(RegionalSurveyParcelRecord parcel, Vector2 openingAnchorMeters)
        {
            if (parcel == null)
            {
                return false;
            }

            if (parcelBoundaryStakesOnlyNearOpeningAnchor)
            {
                Vector2 closest = new(
                    Mathf.Clamp(openingAnchorMeters.x, parcel.BoundsMeters.xMin, parcel.BoundsMeters.xMax),
                    Mathf.Clamp(openingAnchorMeters.y, parcel.BoundsMeters.yMin, parcel.BoundsMeters.yMax));
                if (Vector2.Distance(openingAnchorMeters, closest) > Mathf.Max(64f, openingAnchorTileRadiusMeters) + parcelPadShoulderMeters)
                {
                    return false;
                }
            }

            if (IsParcelPadCandidate(parcel))
            {
                return true;
            }

            return parcel.RemoteSuitability != RegionalRemoteSuitability.None
                || parcel.DevelopmentReadiness01 >= 0.58f
                || parcel.FreightAccess01 >= 0.62f;
        }

        private int CreateParcelCornerStakes(RegionalWorldState state, RegionalSurveyParcelRecord parcel, Transform parcelRoot, Vector2 openingAnchorMeters, float anchorHeight01, float terrainYOffsetMeters, int budget)
        {
            if (budget <= 0 || parcel == null)
            {
                return 0;
            }

            Vector2[] corners =
            {
                new(parcel.BoundsMeters.xMin, parcel.BoundsMeters.yMin),
                new(parcel.BoundsMeters.xMax, parcel.BoundsMeters.yMin),
                new(parcel.BoundsMeters.xMax, parcel.BoundsMeters.yMax),
                new(parcel.BoundsMeters.xMin, parcel.BoundsMeters.yMax)
            };

            int built = 0;
            for (int i = 0; i < corners.Length && built < budget; i++)
            {
                Vector2 corner = corners[i];
                float y = SampleWorldHeightMeters(state, corner, openingAnchorMeters, anchorHeight01, terrainYOffsetMeters) + parcelStakeSurfaceOffsetMeters;
                Vector3 scenePoint = ToSceneWorld(corner);
                scenePoint.y = y;

                GameObject stakeRoot = new($"{GeneratedParcelEvidencePrefix}{parcel.ParcelId} corner {i + 1}");
                stakeRoot.transform.SetParent(parcelRoot, false);
                stakeRoot.transform.SetPositionAndRotation(scenePoint, Quaternion.identity);
                stakeRoot.transform.localScale = Vector3.one;

                if (!TryInstantiateRuntimePrefabChild(parcelStakePrefab, stakeRoot.transform, "authored survey stake", new Vector3(0f, parcelStakeHeightMeters * 0.5f, 0f), Quaternion.identity, Vector3.one))
                {
                    GameObject stake = CreatePrimitiveChild(stakeRoot.transform, PrimitiveType.Cylinder, "survey stake", ResolveParcelStakeMaterial());
                    stake.transform.localPosition = new Vector3(0f, parcelStakeHeightMeters * 0.5f, 0f);
                    stake.transform.localScale = new Vector3(parcelStakeRadiusMeters, parcelStakeHeightMeters * 0.5f, parcelStakeRadiusMeters);
                }
                built++;
            }

            return built;
        }
        private float CalculateParcelPadInfluence01(RegionalWorldState state, Vector2 point)
        {
            float strongest = 0f;
            if (state == null || state.SurveyParcels == null)
            {
                return 0f;
            }

            for (int i = 0; i < state.SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = state.SurveyParcels[i];
                if (parcel == null || !IsParcelPadCandidate(parcel))
                {
                    continue;
                }

                float distance = DistancePointToRect(point, parcel.BoundsMeters);
                float influence = Mathf.Clamp01(1f - distance / Mathf.Max(1f, parcelPadShoulderMeters));
                if (parcel.BoundsMeters.Contains(point))
                {
                    influence = Mathf.Max(influence, Mathf.Lerp(0.35f, 0.92f, parcel.DevelopmentReadiness01));
                }

                strongest = Mathf.Max(strongest, influence * Mathf.Lerp(0.55f, 1.15f, parcel.BuildSuitability01));
            }

            return Mathf.Clamp01(strongest);
        }

        private static bool IsParcelPadCandidate(RegionalSurveyParcelRecord parcel)
        {
            if (parcel == null)
            {
                return false;
            }

            return parcel.ParcelKind == RegionalParcelKind.TownPlatCore
                || parcel.ParcelKind == RegionalParcelKind.TownPlatEdge
                || parcel.ParcelKind == RegionalParcelKind.EdgeExpansion;
        }

        private static float DistancePointToRect(Vector2 point, Rect rect)
        {
            float dx = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
            float dy = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
        private static Vector2 ResolveOpeningAnchorMeters(RegionalWorldState state)
        {
            if (state == null)
            {
                return Vector2.zero;
            }

            if (state.AnchorTown != null)
            {
                return state.AnchorTown.CenterMeters;
            }

            return state.RegionCenterMeters;
        }

        private bool ShouldBuildTile(RegionalWorldState state, RegionalTerrainTileRecord tile)
        {
            if (!buildOnlyTilesNearOpeningAnchor || state == null || tile == null)
            {
                return true;
            }

            Vector2 anchor = ResolveOpeningAnchorMeters(state);
            Vector2 closest = new(
                Mathf.Clamp(anchor.x, tile.BoundsMeters.xMin, tile.BoundsMeters.xMax),
                Mathf.Clamp(anchor.y, tile.BoundsMeters.yMin, tile.BoundsMeters.yMax));
            float distance = Vector2.Distance(anchor, closest);
            return distance <= Mathf.Max(64f, openingAnchorTileRadiusMeters);
        }

        private Vector3 ToSceneWorld(Vector2 regionalPoint)
        {
            TownWorldController controller = ResolveTownWorld();
            if (useOpeningTownLocalSpace && controller != null)
            {
                return controller.RegionalPointToSceneWorld(regionalPoint, 0f);
            }

            return new Vector3(regionalPoint.x, 0f, regionalPoint.y);
        }

        private RegionalWorldState ResolveState()
        {
            TownWorldController controller = ResolveTownWorld();
            return controller != null ? controller.RegionalWorld : null;
        }

        private TownWorldController ResolveTownWorld()
        {
            if (townWorld == null)
            {
                townWorld = FindAnyObjectByType<TownWorldController>();
            }

            return townWorld;
        }

        private Transform EnsureTileRoot()
        {
            if (tileRoot != null)
            {
                NormalizeTileRootTransform(tileRoot);
                return tileRoot;
            }

            Transform existing = transform.Find(TileRootName);
            if (existing != null)
            {
                tileRoot = existing;
                NormalizeTileRootTransform(tileRoot);
                return tileRoot;
            }

            GameObject rootObject = new(TileRootName);
            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rootObject.transform.localScale = Vector3.one;
            rootObject.transform.SetParent(transform, true);
            tileRoot = rootObject.transform;
            NormalizeTileRootTransform(tileRoot);
            return tileRoot;
        }

        private Transform EnsureChildRoot(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null)
            {
                existing.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                existing.localScale = Vector3.one;
                return existing;
            }

            GameObject child = new(childName);
            child.transform.SetParent(parent, false);
            child.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            child.transform.localScale = Vector3.one;
            return child.transform;
        }

        private static bool IsGeneratedTerrainChild(Transform child)
        {
            if (child == null)
            {
                return false;
            }

            string childName = child.name ?? string.Empty;
            if (childName.StartsWith(GeneratedTerrainPrefix)
                || childName.StartsWith(WaterRootName)
                || childName.StartsWith(GeneratedWaterPrefix)
                || childName.StartsWith(RouteRootName)
                || childName.StartsWith(GeneratedRoutePrefix)
                || childName.StartsWith(ScenicRootName)
                || childName.StartsWith(GeneratedScenicPrefix)
                || childName.StartsWith(SettlementEvidenceRootName)
                || childName.StartsWith(GeneratedSettlementEvidencePrefix)
                || childName.StartsWith(ParcelEvidenceRootName)
                || childName.StartsWith(GeneratedParcelEvidencePrefix))
            {
                return true;
            }

            return child.GetComponent<Terrain>() != null || child.GetComponent<TerrainCollider>() != null;
        }

        private void NormalizeTileRootTransform(Transform root)
        {
            if (!forceTileRootWorldIdentity || root == null)
            {
                return;
            }

            if (detachTileRootWhenForcingIdentity && root.parent != null)
            {
                root.SetParent(null, true);
            }

            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;
        }

        private TerrainLayer[] ResolveTerrainLayers()
        {
            if (terrainLayers != null && terrainLayers.Length > 0)
            {
                List<TerrainLayer> validLayers = null;
                for (int i = 0; i < terrainLayers.Length; i++)
                {
                    if (terrainLayers[i] == null)
                    {
                        continue;
                    }

                    validLayers ??= new List<TerrainLayer>();
                    validLayers.Add(terrainLayers[i]);
                }

                if (validLayers != null && validLayers.Count > 0)
                {
                    return validLayers.ToArray();
                }
            }

            if (!generateRuntimeFallbackLayers || !createRuntimeFallbackAssets)
            {
                return null;
            }

            RecordRuntimeFallbackAssetUse("terrain layers/textures");

            if (runtimeFallbackTerrainLayers == null || runtimeFallbackTerrainLayers.Length != FallbackLayerCount)
            {
                runtimeFallbackTerrainTextures = new[]
                {
                    CreateRuntimeTexture("Runtime Regional Grass Texture", fallbackGrassColor),
                    CreateRuntimeTexture("Runtime Regional Road Dirt Texture", fallbackRoadDirtColor),
                    CreateRuntimeTexture("Runtime Regional Wetland Texture", fallbackWetlandColor),
                    CreateRuntimeTexture("Runtime Regional Rock Texture", fallbackRockColor)
                };

                runtimeFallbackTerrainLayers = new[]
                {
                    CreateRuntimeLayer("Runtime Regional Grass Layer", runtimeFallbackTerrainTextures[0], 18f),
                    CreateRuntimeLayer("Runtime Regional Road Dirt Layer", runtimeFallbackTerrainTextures[1], 9f),
                    CreateRuntimeLayer("Runtime Regional Wetland Layer", runtimeFallbackTerrainTextures[2], 14f),
                    CreateRuntimeLayer("Runtime Regional Rock Layer", runtimeFallbackTerrainTextures[3], 20f)
                };
            }

            return runtimeFallbackTerrainLayers;
        }

        private static Texture2D CreateRuntimeTexture(string name, Color color)
        {
            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false, true)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private static TerrainLayer CreateRuntimeLayer(string name, Texture2D texture, float tileSizeMeters)
        {
            return new TerrainLayer
            {
                name = name,
                diffuseTexture = texture,
                tileSize = new Vector2(Mathf.Max(1f, tileSizeMeters), Mathf.Max(1f, tileSizeMeters)),
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private Material ResolveTerrainMaterial()
        {
            if (terrainMaterial != null)
            {
                return terrainMaterial;
            }

            if (!assignRuntimeTerrainMaterialWhenMissing || !createRuntimeFallbackAssets)
            {
                return null;
            }

            if (runtimeTerrainMaterial == null)
            {
                Shader shader = Shader.Find("HDRP/TerrainLit")
                    ?? Shader.Find("Nature/Terrain/Standard")
                    ?? Shader.Find("HDRP/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");
                if (shader == null)
                {
                    return null;
                }

                runtimeTerrainMaterial = new Material(shader)
                {
                    name = "Runtime Regional Terrain Material",
                    hideFlags = HideFlags.HideAndDontSave
                };
                ApplyMaterialColor(runtimeTerrainMaterial, runtimeTerrainMaterialColor);
                RecordRuntimeFallbackAssetUse("terrain material");
            }

            return runtimeTerrainMaterial;
        }

        private Material ResolveWaterMaterial()
        {
            if (waterMaterial != null)
            {
                return waterMaterial;
            }

            if (!createRuntimeFallbackAssets)
            {
                return null;
            }

            if (runtimeWaterMaterial == null)
            {
                RecordRuntimeFallbackAssetUse("water material");
                Shader shader = Shader.Find("HDRP/Unlit")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? Shader.Find("Standard");
                runtimeWaterMaterial = new Material(shader)
                {
                    name = "Runtime Regional Water Material",
                    hideFlags = HideFlags.HideAndDontSave
                };
                ApplyMaterialColor(runtimeWaterMaterial, runtimeWaterColor);
                runtimeWaterMaterial.renderQueue = 3000;
            }

            return runtimeWaterMaterial;
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private Material ResolveRouteMaterial()
        {
            if (routeMaterial != null)
            {
                return routeMaterial;
            }

            runtimeRouteMaterial ??= CreateRuntimeMaterial("Runtime Regional Route Material", runtimeRouteColor, 2450);
            return runtimeRouteMaterial;
        }

        private Material ResolveTreeTrunkMaterial()
        {
            runtimeTreeTrunkMaterial ??= CreateRuntimeMaterial("Runtime Scenic Tree Trunk Material", scenicTreeTrunkColor, 2000);
            return runtimeTreeTrunkMaterial;
        }

        private Material ResolveTreeFoliageMaterial()
        {
            runtimeTreeFoliageMaterial ??= CreateRuntimeMaterial("Runtime Scenic Tree Foliage Material", scenicTreeFoliageColor, 2000);
            return runtimeTreeFoliageMaterial;
        }

        private Material ResolveScrubMaterial()
        {
            runtimeScrubMaterial ??= CreateRuntimeMaterial("Runtime Scenic Scrub Material", scenicScrubColor, 2000);
            return runtimeScrubMaterial;
        }

        private Material ResolveWetReedMaterial()
        {
            runtimeWetReedMaterial ??= CreateRuntimeMaterial("Runtime Scenic Wet Reed Material", scenicWetReedColor, 2000);
            return runtimeWetReedMaterial;
        }

        private Material ResolveSettlementEvidenceMaterial()
        {
            runtimeSettlementEvidenceMaterial ??= CreateRuntimeMaterial("Runtime Regional Settlement Evidence Material", settlementEvidenceColor, 2000);
            return runtimeSettlementEvidenceMaterial;
        }

        private Material ResolveParcelStakeMaterial()
        {
            runtimeParcelStakeMaterial ??= CreateRuntimeMaterial("Runtime Regional Parcel Stake Material", parcelStakeColor, 2000);
            return runtimeParcelStakeMaterial;
        }

        private Material ResolveRockMaterial()
        {
            runtimeRockMaterial ??= CreateRuntimeMaterial("Runtime Scenic Rock Material", scenicRockColor, 2000);
            return runtimeRockMaterial;
        }

        private Material CreateRuntimeMaterial(string materialName, Color color, int renderQueue)
        {
            if (!createRuntimeFallbackAssets)
            {
                return null;
            }

            RecordRuntimeFallbackAssetUse(materialName);
            Shader shader = Shader.Find("HDRP/Unlit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            Material material = new(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = renderQueue
            };
            ApplyMaterialColor(material, color);
            return material;
        }

        private void RecordRuntimeFallbackAssetUse(string assetDescription)
        {
            if (!createRuntimeFallbackAssets)
            {
                return;
            }

            runtimeFallbackAssetUseCount++;
            if (logRuntimeFallbackAssets && !string.IsNullOrWhiteSpace(assetDescription))
            {
                Debug.Log($"[RegionalTerrain] Runtime fallback created/used: {assetDescription}.", this);
            }
        }

        private void RegisterGeneratedTerrainCollider(TerrainCollider terrainCollider)
        {
            LastRegisteredTerrainCollider = null;
            if (!registerFirstTerrainColliderWithTownWorld)
            {
                lastTerrainColliderRegistrationStatus = "terrain collider registration disabled";
                return;
            }

            if (terrainCollider == null)
            {
                lastTerrainColliderRegistrationStatus = "no generated TerrainCollider available to register";
                return;
            }

            TownWorldController controller = ResolveTownWorld();
            if (controller == null)
            {
                lastTerrainColliderRegistrationStatus = "TownWorldController not resolved for terrain collider registration";
                return;
            }

            controller.SetRuntimeTerrainCollider(terrainCollider, true);
            LastRegisteredTerrainCollider = terrainCollider;
            lastTerrainColliderRegistrationStatus = $"registered terrain collider '{terrainCollider.name}' with TownWorldController";
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denominator = Mathf.Max(0.0001f, Vector2.Dot(ab, ab));
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
            return Vector2.Distance(point, a + ab * t);
        }

        private static void DestroyUnityObject(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
