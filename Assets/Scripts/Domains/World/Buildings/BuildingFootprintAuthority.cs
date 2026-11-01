using System.Collections.Generic;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LandLedgers.World
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Land & Ledgers/World/Building Footprint Authority")]
    public sealed class BuildingFootprintAuthority : MonoBehaviour
    {
        [Header("Authoring Mode")]
        [SerializeField]
        [Tooltip("When enabled, the footprint is resolved from the four authored corner points instead of the legacy width/depth integers.")]
        private bool useCornerAuthoring = true;

        [SerializeField, Min(0.1f)]
        [Tooltip("Cell size used to convert authored local-space corner dimensions into minimum grid cells. Match the town grid cell size; the current default is 2 meters.")]
        private float authoringCellSizeMeters = 2f;

        [Header("Legacy Integer Fallback")]
        [SerializeField, Min(1)]
        [Tooltip("Fallback minimum width used when corner authoring is disabled.")]
        private int minimumFootprintWidthCells = 1;

        [SerializeField, Min(1)]
        [Tooltip("Fallback minimum depth used when corner authoring is disabled.")]
        private int minimumFootprintDepthCells = 1;

        [Header("Corner Authoring")]
        [SerializeField]
        private Vector3 frontLeftCornerLocal = new(-2f, 0f, 2f);

        [SerializeField]
        private Vector3 frontRightCornerLocal = new(2f, 0f, 2f);

        [SerializeField]
        private Vector3 backRightCornerLocal = new(2f, 0f, -2f);

        [SerializeField]
        private Vector3 backLeftCornerLocal = new(-2f, 0f, -2f);

        [Header("Front Authoring")]
        [SerializeField]
        [Tooltip("When enabled, the brown front handle is kept centered on the authored front edge with the configured outward offset.")]
        private bool autoCenterFrontDoorPoint = true;

        [SerializeField, Min(0f)]
        [Tooltip("How far outward from the authored front edge the brown front point should sit.")]
        private float frontDoorOutwardOffsetMeters = 0.35f;

        [SerializeField]
        [Tooltip("Brown front authoring point. TownWorldController can use this as the authored front-door anchor when no named prefab marker exists.")]
        private Vector3 frontDoorLocalPoint = new(0f, 0f, 2.35f);

        [SerializeField]
        [Tooltip("When enabled, placement code may treat the brown front point as the exact frontage/road-access point to pin to the nearest road edge instead of centering the building in the parcel.")]
        private bool useFrontPointAsRoadAccessAnchor = true;

        [Header("Renderer Cross-Check")]
        [SerializeField]
        [Tooltip("When enabled, authoring warnings compare the footprint handles against building-shell renderers. Helper, marker, terrain, route, collider, and extreme outlier renderers are ignored so imported prefabs do not produce impossible footprint warnings.")]
        private bool warnWhenRendererBoundsExceedCorners = true;

        [SerializeField, Min(0)]
        [Tooltip("Small slack for renderer/corner mismatch warnings. This prevents trim, bevels, and small material overhangs from being reported as footprint-authority problems.")]
        private int rendererBoundsWarningSlackCells = 2;

        [SerializeField, Min(16f)]
        [Tooltip("Local-space renderer bounds larger than this many meters, or many times larger than the authored handles, are treated as imported-helper/outlier bounds and ignored for footprint warnings.")]
        private float rendererBoundsOutlierFloorMeters = 64f;

        public bool UsesCornerAuthoring => useCornerAuthoring;
        public float AuthoringCellSizeMeters => Mathf.Max(0.1f, authoringCellSizeMeters);
        public int MinimumFootprintWidthCells => MinimumFootprintSizeCells.x;
        public int MinimumFootprintDepthCells => MinimumFootprintSizeCells.y;
        public Vector2Int MinimumFootprintSizeCells => ResolveMinimumFootprintSizeCells();
        public Vector3 FrontDoorLocalPoint => frontDoorLocalPoint;
        public bool UsesFrontPointAsRoadAccessAnchor => useFrontPointAsRoadAccessAnchor;
        public Vector3 RoadAccessLocalPoint => useFrontPointAsRoadAccessAnchor ? frontDoorLocalPoint : FrontEdgeCenterLocalPoint;
        public Vector3 FrontEdgeCenterLocalPoint => (frontLeftCornerLocal + frontRightCornerLocal) * 0.5f;
        public Vector3 BackEdgeCenterLocalPoint => (backLeftCornerLocal + backRightCornerLocal) * 0.5f;
        public Vector3 FrontOutwardLocalDirection => ResolveFrontOutwardLocalDirection();

        public bool TryGetRoadAccessLocalPose(out Vector3 localPoint, out Vector3 localForward)
        {
            localPoint = RoadAccessLocalPoint;
            localForward = ResolveFrontOutwardLocalDirection();
            return localForward.sqrMagnitude > 0.0001f;
        }

        public Vector3 ResolveRoadAccessWorldPoint(Vector3 rootWorldPosition, Quaternion rootWorldRotation, Vector3 rootWorldScale)
        {
            Vector3 scaledLocalPoint = Vector3.Scale(RoadAccessLocalPoint, rootWorldScale);
            return rootWorldPosition + rootWorldRotation * scaledLocalPoint;
        }

        public Vector3 ResolveRootPositionForRoadAccess(Vector3 targetRoadAccessWorldPoint, Quaternion rootWorldRotation, Vector3 rootWorldScale)
        {
            Vector3 scaledLocalPoint = Vector3.Scale(RoadAccessLocalPoint, rootWorldScale);
            return targetRoadAccessWorldPoint - rootWorldRotation * scaledLocalPoint;
        }

        public Vector3 GetCornerLocalPoint(int index)
        {
            return index switch
            {
                0 => frontLeftCornerLocal,
                1 => frontRightCornerLocal,
                2 => backRightCornerLocal,
                3 => backLeftCornerLocal,
                _ => Vector3.zero
            };
        }
        public Vector2Int ReconcileWithDefinitionFootprint(Vector2Int definitionFootprintSizeCells)
        {
            return ReconcileWithDefinitionFootprint(definitionFootprintSizeCells, out _, out _, out _);
        }

        public Vector2Int ReconcileWithDefinitionFootprint(
            Vector2Int definitionFootprintSizeCells,
            out Vector2Int authorityMinimumSizeCells,
            out bool expandedByAuthority,
            out string note)
        {
            Vector2Int definitionMinimum = SanitizeCellSize(definitionFootprintSizeCells);
            authorityMinimumSizeCells = SanitizeCellSize(MinimumFootprintSizeCells);
            Vector2Int resolved = new(
                Mathf.Max(definitionMinimum.x, authorityMinimumSizeCells.x),
                Mathf.Max(definitionMinimum.y, authorityMinimumSizeCells.y));

            expandedByAuthority = resolved.x > definitionMinimum.x || resolved.y > definitionMinimum.y;
            note = expandedByAuthority
                ? $"Prefab footprint authority expanded the grid claim from definition {definitionMinimum.x}x{definitionMinimum.y} to {resolved.x}x{resolved.y}. Visual prefab scale remains authored."
                : $"Definition footprint {definitionMinimum.x}x{definitionMinimum.y} satisfies prefab footprint authority minimum {authorityMinimumSizeCells.x}x{authorityMinimumSizeCells.y}. Visual prefab scale remains authored.";
            return resolved;
        }

        public string BuildRuntimeAuthoritySummary(Vector2Int definitionFootprintSizeCells)
        {
            Vector2Int resolved = ReconcileWithDefinitionFootprint(
                definitionFootprintSizeCells,
                out Vector2Int authorityMinimum,
                out bool expandedByAuthority,
                out _);

            Vector2Int definitionMinimum = SanitizeCellSize(definitionFootprintSizeCells);
            string mode = useCornerAuthoring ? "corner-authored" : "integer fallback";
            string expansion = expandedByAuthority ? "expanded grid claim" : "definition-sized grid claim";
            return $"Footprint authority: {mode} minimum {authorityMinimum.x}x{authorityMinimum.y}; definition {definitionMinimum.x}x{definitionMinimum.y}; resolved placement {resolved.x}x{resolved.y}; {expansion}; visual scale preserved.";
        }

        public bool TryBuildAuthoringWarningSummary(out string warningSummary)
        {
            List<string> warnings = new();
            float cellSize = AuthoringCellSizeMeters;

            if (!useCornerAuthoring)
            {
                warnings.Add("corner authoring is disabled; runtime minimum comes from integer fallback and renderer bounds");
            }

            Vector3 frontEdge = frontRightCornerLocal - frontLeftCornerLocal;
            Vector3 backEdge = backRightCornerLocal - backLeftCornerLocal;
            Vector3 depthLeft = frontLeftCornerLocal - backLeftCornerLocal;
            Vector3 depthRight = frontRightCornerLocal - backRightCornerLocal;
            Vector3 frontToBack = FrontEdgeCenterLocalPoint - BackEdgeCenterLocalPoint;

            if (frontEdge.magnitude < cellSize * 0.25f || backEdge.magnitude < cellSize * 0.25f)
            {
                warnings.Add("front/back edge span is nearly collapsed");
            }

            if (depthLeft.magnitude < cellSize * 0.25f || depthRight.magnitude < cellSize * 0.25f)
            {
                warnings.Add("side/depth edge span is nearly collapsed");
            }

            if (frontToBack.sqrMagnitude < 0.01f)
            {
                warnings.Add("front direction is ambiguous because front and back centers are nearly overlapping");
            }
            else
            {
                Vector3 outward = frontToBack.normalized;
                float frontDoorSignedDistance = Vector3.Dot(frontDoorLocalPoint - FrontEdgeCenterLocalPoint, outward);
                if (frontDoorSignedDistance < -0.05f)
                {
                    warnings.Add("front-door point sits behind the authored front edge");
                }
            }

            if (warnWhenRendererBoundsExceedCorners
                && TryResolveRendererMinimumFootprintSizeCells(out Vector2Int rendererMinimum, out RendererBoundsRead rendererRead))
            {
                Vector2Int authoredMinimum = MinimumFootprintSizeCells;
                int slack = Mathf.Max(0, rendererBoundsWarningSlackCells);
                if (rendererMinimum.x > authoredMinimum.x + slack || rendererMinimum.y > authoredMinimum.y + slack)
                {
                    warnings.Add($"building-shell renderer bounds suggest about {rendererMinimum.x}x{rendererMinimum.y} cells, larger than authored minimum {authoredMinimum.x}x{authoredMinimum.y}; {rendererRead.BuildSummary()}");
                }
                else if (rendererRead.IgnoredRendererCount > 0 && rendererRead.IncludedRendererCount == 0)
                {
                    warnings.Add($"renderer footprint cross-check skipped because only helper/outlier renderers were found; {rendererRead.BuildSummary()}");
                }
            }

            if (warnings.Count == 0)
            {
                warningSummary = string.Empty;
                return false;
            }

            StringBuilder builder = new();
            for (int i = 0; i < warnings.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(warnings[i]);
            }

            warningSummary = builder.ToString();
            return true;
        }


        public void SetCornerLocalPoint(int index, Vector3 localPoint)
        {
            switch (index)
            {
                case 0:
                    frontLeftCornerLocal = localPoint;
                    break;
                case 1:
                    frontRightCornerLocal = localPoint;
                    break;
                case 2:
                    backRightCornerLocal = localPoint;
                    break;
                case 3:
                    backLeftCornerLocal = localPoint;
                    break;
            }

            NormalizeAuthoring();
        }

        public void SetFrontDoorLocalPoint(Vector3 localPoint)
        {
            frontDoorLocalPoint = localPoint;
            NormalizeAuthoring();
        }

        [ContextMenu("Fit Authoring Corners To Renderer Bounds")]
        private void FitAuthoringCornersToRendererBounds()
        {
            if (!TryGetLocalRendererBounds(out Bounds bounds))
            {
                return;
            }

            float y = 0f;
            frontLeftCornerLocal = new Vector3(bounds.min.x, y, bounds.max.z);
            frontRightCornerLocal = new Vector3(bounds.max.x, y, bounds.max.z);
            backRightCornerLocal = new Vector3(bounds.max.x, y, bounds.min.z);
            backLeftCornerLocal = new Vector3(bounds.min.x, y, bounds.min.z);
            NormalizeAuthoring();
        }

        private void Reset()
        {
            if (TryGetLocalRendererBounds(out Bounds bounds))
            {
                float y = 0f;
                frontLeftCornerLocal = new Vector3(bounds.min.x, y, bounds.max.z);
                frontRightCornerLocal = new Vector3(bounds.max.x, y, bounds.max.z);
                backRightCornerLocal = new Vector3(bounds.max.x, y, bounds.min.z);
                backLeftCornerLocal = new Vector3(bounds.min.x, y, bounds.min.z);
            }

            NormalizeAuthoring();
        }

        private void OnValidate()
        {
            NormalizeAuthoring();
        }

        private void OnDrawGizmosSelected()
        {
            DrawAuthoringGizmos();
        }

        private void NormalizeAuthoring()
        {
            authoringCellSizeMeters = Mathf.Max(0.1f, authoringCellSizeMeters);
            minimumFootprintWidthCells = Mathf.Max(1, minimumFootprintWidthCells);
            minimumFootprintDepthCells = Mathf.Max(1, minimumFootprintDepthCells);
            frontDoorOutwardOffsetMeters = Mathf.Max(0f, frontDoorOutwardOffsetMeters);

            FlattenPointToGround(ref frontLeftCornerLocal);
            FlattenPointToGround(ref frontRightCornerLocal);
            FlattenPointToGround(ref backRightCornerLocal);
            FlattenPointToGround(ref backLeftCornerLocal);
            FlattenPointToGround(ref frontDoorLocalPoint);

            if (autoCenterFrontDoorPoint)
            {
                frontDoorLocalPoint = ResolveDefaultFrontDoorLocalPoint();
            }
        }

        private Vector3 ResolveFrontOutwardLocalDirection()
        {
            Vector3 outward = FrontEdgeCenterLocalPoint - BackEdgeCenterLocalPoint;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return outward.normalized;
        }

        private Vector2Int ResolveMinimumFootprintSizeCells()
        {
            if (useCornerAuthoring)
            {
                return ResolveCornerMinimumFootprintSizeCells();
            }

            Vector2Int authoredMinimum = new(
                Mathf.Max(1, minimumFootprintWidthCells),
                Mathf.Max(1, minimumFootprintDepthCells));

            if (!TryResolveRendererMinimumFootprintSizeCells(out Vector2Int rendererMinimum))
            {
                return authoredMinimum;
            }

            return new Vector2Int(
                Mathf.Max(authoredMinimum.x, rendererMinimum.x),
                Mathf.Max(authoredMinimum.y, rendererMinimum.y));
        }

        private Vector2Int ResolveCornerMinimumFootprintSizeCells()
        {
            Vector3 min = frontLeftCornerLocal;
            Vector3 max = frontLeftCornerLocal;
            Encapsulate(ref min, ref max, frontRightCornerLocal);
            Encapsulate(ref min, ref max, backRightCornerLocal);
            Encapsulate(ref min, ref max, backLeftCornerLocal);

            float widthMeters = Mathf.Abs(max.x - min.x);
            float depthMeters = Mathf.Abs(max.z - min.z);
            float cellSize = AuthoringCellSizeMeters;

            return new Vector2Int(
                Mathf.Max(1, Mathf.CeilToInt(widthMeters / cellSize)),
                Mathf.Max(1, Mathf.CeilToInt(depthMeters / cellSize)));
        }

        private bool TryResolveRendererMinimumFootprintSizeCells(out Vector2Int size)
        {
            return TryResolveRendererMinimumFootprintSizeCells(out size, out _);
        }

        private bool TryResolveRendererMinimumFootprintSizeCells(out Vector2Int size, out RendererBoundsRead rendererRead)
        {
            size = default;
            if (!TryGetLocalRendererBounds(out Bounds bounds, out rendererRead))
            {
                return false;
            }

            float cellSize = AuthoringCellSizeMeters;
            size = new Vector2Int(
                Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(bounds.size.x) / cellSize)),
                Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(bounds.size.z) / cellSize)));
            return true;
        }

        private Vector3 ResolveDefaultFrontDoorLocalPoint()
        {
            Vector3 frontCenter = FrontEdgeCenterLocalPoint;
            Vector3 backCenter = BackEdgeCenterLocalPoint;
            Vector3 outward = ResolveFrontOutwardLocalDirection();
            return frontCenter + outward * frontDoorOutwardOffsetMeters;
        }

        private bool TryGetLocalRendererBounds(out Bounds bounds)
        {
            return TryGetLocalRendererBounds(out bounds, out _);
        }

        private bool TryGetLocalRendererBounds(out Bounds bounds, out RendererBoundsRead rendererRead)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            bounds = default;
            rendererRead = new RendererBoundsRead(renderers != null ? renderers.Length : 0);

            if (renderers == null)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!ShouldUseRendererForFootprintBounds(renderer, out string ignoredReason))
                {
                    rendererRead.RecordIgnored(renderer, ignoredReason);
                    continue;
                }

                if (!TryBuildRendererLocalBounds(renderer, out Bounds rendererLocalBounds))
                {
                    rendererRead.RecordIgnored(renderer, "invalid renderer bounds");
                    continue;
                }

                if (IsRendererBoundsOutlier(rendererLocalBounds, out string outlierReason))
                {
                    rendererRead.RecordIgnored(renderer, outlierReason);
                    continue;
                }

                rendererRead.RecordIncluded(renderer);
                if (!initialized)
                {
                    bounds = rendererLocalBounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(rendererLocalBounds);
                }
            }

            return initialized;
        }

        private static bool ShouldUseRendererForFootprintBounds(Renderer renderer, out string ignoredReason)
        {
            ignoredReason = string.Empty;
            if (renderer == null)
            {
                ignoredReason = "missing renderer";
                return false;
            }

            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
            {
                ignoredReason = "non-building renderer";
                return false;
            }

            string objectName = renderer.gameObject != null ? renderer.gameObject.name.ToLowerInvariant() : string.Empty;
            string rendererName = renderer.name != null ? renderer.name.ToLowerInvariant() : string.Empty;
            string combined = objectName + " " + rendererName;
            string[] helperTokens =
            {
                "anchor", "marker", "gizmo", "handle", "bounds", "collider", "trigger", "proxy",
                "preview", "icon", "terrain", "water", "route", "scenic", "evidence", "parcel",
                "settlement", "debug", "lod1", "lod2", "lod3", "shadow"
            };

            for (int i = 0; i < helperTokens.Length; i++)
            {
                if (combined.Contains(helperTokens[i]))
                {
                    ignoredReason = $"helper renderer '{helperTokens[i]}'";
                    return false;
                }
            }

            return true;
        }

        private bool TryBuildRendererLocalBounds(Renderer renderer, out Bounds localBounds)
        {
            localBounds = default;
            if (renderer == null)
            {
                return false;
            }

            Vector3 worldCenter = renderer.bounds.center;
            Vector3 worldExtents = renderer.bounds.extents;
            if (!IsFinite(worldCenter) || !IsFinite(worldExtents) || worldExtents.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            Vector3[] worldCorners =
            {
                worldCenter + new Vector3(-worldExtents.x, -worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3(-worldExtents.x, -worldExtents.y, worldExtents.z),
                worldCenter + new Vector3(-worldExtents.x, worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3(-worldExtents.x, worldExtents.y, worldExtents.z),
                worldCenter + new Vector3(worldExtents.x, -worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3(worldExtents.x, -worldExtents.y, worldExtents.z),
                worldCenter + new Vector3(worldExtents.x, worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3(worldExtents.x, worldExtents.y, worldExtents.z)
            };

            bool initialized = false;
            for (int cornerIndex = 0; cornerIndex < worldCorners.Length; cornerIndex++)
            {
                Vector3 localCorner = transform.InverseTransformPoint(worldCorners[cornerIndex]);
                if (!IsFinite(localCorner))
                {
                    continue;
                }

                if (!initialized)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }

            return initialized;
        }

        private bool IsRendererBoundsOutlier(Bounds localBounds, out string reason)
        {
            reason = string.Empty;
            float maxDimension = Mathf.Max(Mathf.Abs(localBounds.size.x), Mathf.Abs(localBounds.size.z));
            if (maxDimension <= 0f)
            {
                reason = "empty local footprint bounds";
                return true;
            }

            Vector2 cornerSize = ResolveCornerFootprintSizeMeters();
            float authoredMax = Mathf.Max(cornerSize.x, cornerSize.y);
            float limit = Mathf.Max(rendererBoundsOutlierFloorMeters, Mathf.Max(AuthoringCellSizeMeters * 8f, authoredMax * 6f));
            if (maxDimension > limit)
            {
                reason = $"outlier bounds {maxDimension:0.#}m exceeds {limit:0.#}m diagnostic limit";
                return true;
            }

            return false;
        }

        private Vector2 ResolveCornerFootprintSizeMeters()
        {
            Vector3 min = frontLeftCornerLocal;
            Vector3 max = frontLeftCornerLocal;
            Encapsulate(ref min, ref max, frontRightCornerLocal);
            Encapsulate(ref min, ref max, backRightCornerLocal);
            Encapsulate(ref min, ref max, backLeftCornerLocal);
            return new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.z - min.z));
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private struct RendererBoundsRead
        {
            private string sampleIgnoredRenderer;
            private string sampleIgnoredReason;

            public RendererBoundsRead(int totalRendererCount)
            {
                TotalRendererCount = Mathf.Max(0, totalRendererCount);
                IncludedRendererCount = 0;
                IgnoredRendererCount = 0;
                sampleIgnoredRenderer = string.Empty;
                sampleIgnoredReason = string.Empty;
            }

            public int TotalRendererCount { get; }
            public int IncludedRendererCount { get; private set; }
            public int IgnoredRendererCount { get; private set; }

            public void RecordIncluded(Renderer renderer)
            {
                IncludedRendererCount++;
            }

            public void RecordIgnored(Renderer renderer, string reason)
            {
                IgnoredRendererCount++;
                if (string.IsNullOrWhiteSpace(sampleIgnoredRenderer))
                {
                    sampleIgnoredRenderer = renderer != null ? renderer.name : string.Empty;
                    sampleIgnoredReason = reason ?? string.Empty;
                }
            }

            public string BuildSummary()
            {
                string ignored = IgnoredRendererCount > 0
                    ? ", ignored " + IgnoredRendererCount + (string.IsNullOrWhiteSpace(sampleIgnoredRenderer) ? string.Empty : $" (sample {sampleIgnoredRenderer}: {sampleIgnoredReason})")
                    : string.Empty;
                return $"renderer cross-check used {IncludedRendererCount}/{TotalRendererCount} renderer(s){ignored}";
            }
        }

        private void DrawAuthoringGizmos()
        {
            const float sphereScale = 0.14f;
            float radius = Mathf.Max(0.08f, AuthoringCellSizeMeters * sphereScale);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = transform.localToWorldMatrix;

            Color outlineColor = new Color(0.2f, 0.9f, 0.25f, 1f);
            Color frontPointColor = new Color(0.45f, 0.24f, 0.06f, 1f);

            Vector3[] corners =
            {
                frontLeftCornerLocal,
                frontRightCornerLocal,
                backRightCornerLocal,
                backLeftCornerLocal
            };

            Gizmos.color = outlineColor;
            for (int i = 0; i < corners.Length; i++)
            {
                Gizmos.DrawSphere(corners[i], radius);
                Gizmos.DrawLine(corners[i], corners[(i + 1) % corners.Length]);
            }

            Gizmos.color = frontPointColor;
            Gizmos.DrawSphere(frontDoorLocalPoint, radius * 0.9f);
            Gizmos.DrawLine(FrontEdgeCenterLocalPoint, frontDoorLocalPoint);

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private static Vector2Int SanitizeCellSize(Vector2Int size)
        {
            return new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        }

        private static void FlattenPointToGround(ref Vector3 point)
        {
            point.y = 0f;
        }

        private static void Encapsulate(ref Vector3 min, ref Vector3 max, Vector3 point)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

#if UNITY_EDITOR
        internal void EditorRefreshAfterHandleMove(bool movedCornerHandle)
        {
            if (movedCornerHandle && autoCenterFrontDoorPoint)
            {
                frontDoorLocalPoint = ResolveDefaultFrontDoorLocalPoint();
            }

            NormalizeAuthoring();
            EditorUtility.SetDirty(this);
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(BuildingFootprintAuthority))]
    internal sealed class BuildingFootprintAuthorityEditor : Editor
    {
        private static readonly Color MovableCornerHandleColor = new(0.2f, 0.9f, 0.25f, 1f);
        private static readonly Color LockedCornerHandleColor = new(0.9f, 0.15f, 0.12f, 1f);
        private static readonly Color FrontHandleColor = new(0.45f, 0.24f, 0.06f, 1f);

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            BuildingFootprintAuthority authority = (BuildingFootprintAuthority)target;
            if (authority == null)
            {
                return;
            }

            Vector2Int minimum = authority.MinimumFootprintSizeCells;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"Runtime grid-claim minimum: {minimum.x}x{minimum.y} cells. This component claims placement footprint only; it does not scale or normalize the visual prefab.",
                MessageType.Info);

            if (authority.TryBuildAuthoringWarningSummary(out string warningSummary))
            {
                EditorGUILayout.HelpBox(warningSummary, MessageType.Warning);
            }
        }

        private void OnSceneGUI()
        {
            BuildingFootprintAuthority authority = (BuildingFootprintAuthority)target;
            if (authority == null)
            {
                return;
            }

            serializedObject.Update();

            bool movedCornerHandle = false;
            bool movedFrontHandle = false;

            SerializedProperty frontLeftCorner = serializedObject.FindProperty("frontLeftCornerLocal");
            SerializedProperty frontRightCorner = serializedObject.FindProperty("frontRightCornerLocal");
            SerializedProperty backRightCorner = serializedObject.FindProperty("backRightCornerLocal");
            SerializedProperty backLeftCorner = serializedObject.FindProperty("backLeftCornerLocal");

            movedCornerHandle |= DrawMoveHandle(frontLeftCorner, authority, MovableCornerHandleColor);
            DrawLockedHandle(frontRightCorner, authority, LockedCornerHandleColor);
            movedCornerHandle |= DrawMoveHandle(backRightCorner, authority, MovableCornerHandleColor);
            DrawLockedHandle(backLeftCorner, authority, LockedCornerHandleColor);
            movedFrontHandle |= DrawFreeMoveHandle(serializedObject.FindProperty("frontDoorLocalPoint"), authority, FrontHandleColor);

            if (movedCornerHandle || movedFrontHandle)
            {
                if (movedCornerHandle)
                {
                    SyncLockedCornersToEditableDiagonal(frontLeftCorner, frontRightCorner, backRightCorner, backLeftCorner);
                }

                serializedObject.ApplyModifiedProperties();
                authority.EditorRefreshAfterHandleMove(movedCornerHandle);
            }
            else
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static bool DrawFreeMoveHandle(SerializedProperty property, BuildingFootprintAuthority authority, Color color)
        {
            return DrawMoveHandle(property, authority, color);
        }

        private static bool DrawMoveHandle(SerializedProperty property, BuildingFootprintAuthority authority, Color color)
        {
            if (property == null || authority == null)
            {
                return false;
            }

            Transform transform = authority.transform;
            Vector3 worldPosition = transform.TransformPoint(property.vector3Value);
            float handleSize = Mathf.Max(0.06f, HandleUtility.GetHandleSize(worldPosition) * 0.08f);

            using (new Handles.DrawingScope(color))
            {
                EditorGUI.BeginChangeCheck();
                Vector3 updatedWorldPosition = Handles.Slider2D(
                    worldPosition,
                    transform.up,
                    transform.right,
                    transform.forward,
                    handleSize,
                    Handles.SphereHandleCap,
                    Vector2.zero);

                if (!EditorGUI.EndChangeCheck())
                {
                    return false;
                }

                Undo.RecordObject(authority, "Move Building Footprint Authoring Handle");
                property.vector3Value = transform.InverseTransformPoint(updatedWorldPosition);
                return true;
            }
        }

        private static void DrawLockedHandle(SerializedProperty property, BuildingFootprintAuthority authority, Color color)
        {
            if (property == null || authority == null)
            {
                return;
            }

            Transform transform = authority.transform;
            Vector3 worldPosition = transform.TransformPoint(property.vector3Value);
            float handleSize = Mathf.Max(0.06f, HandleUtility.GetHandleSize(worldPosition) * 0.08f);

            using (new Handles.DrawingScope(color))
            {
                Handles.SphereHandleCap(0, worldPosition, Quaternion.identity, handleSize, EventType.Repaint);
            }
        }

        private static void SyncLockedCornersToEditableDiagonal(
            SerializedProperty frontLeftCorner,
            SerializedProperty frontRightCorner,
            SerializedProperty backRightCorner,
            SerializedProperty backLeftCorner)
        {
            if (frontLeftCorner == null || frontRightCorner == null || backRightCorner == null || backLeftCorner == null)
            {
                return;
            }

            Vector3 frontLeft = frontLeftCorner.vector3Value;
            Vector3 backRight = backRightCorner.vector3Value;
            frontRightCorner.vector3Value = new Vector3(backRight.x, frontLeft.y, frontLeft.z);
            backLeftCorner.vector3Value = new Vector3(frontLeft.x, backRight.y, backRight.z);
        }
    }
#endif
}
