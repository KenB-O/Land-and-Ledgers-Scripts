using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LandAndLedgers.Buildings
{
    public enum LLModuleCategory
    {
        Wall,
        Floor,
        Ceiling,
        Door,
        Window,
        Foundation,
        Roof,
        Stair,
        Porch,
        Trim,
        Detail,
        Other
    }

    public enum LLColliderAuthoringMode
    {
        Auto,
        SingleBox,
        MeshPerSubMesh
    }

    public enum LLSizeOverrideMode
    {
        None,
        TargetSpanMeters,
        GridFootprint
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class LLModularBuildingPiece : MonoBehaviour
    {
        [Header("Identity")]
        public string pieceId = "wall_wood_a";
        public string sourceKey = string.Empty;
        public string sourceFamilyKey = string.Empty;
        public string sourceBuildingAssetPath = string.Empty;
        public string extractedFromBuildingName = string.Empty;
        public string referenceTemplatePath = string.Empty;
        public string sourceMeshName = string.Empty;
        public string primaryMaterialName = string.Empty;
        public string lod0AssetPath = string.Empty;
        public string lod1AssetPath = string.Empty;
        public string outputPrefabPath = string.Empty;
        public LLModuleCategory category = LLModuleCategory.Wall;
        public LLColliderAuthoringMode colliderMode = LLColliderAuthoringMode.Auto;

        [Header("Grid / Snap")]
        [Min(0.1f)] public float cellSizeMeters = 2f;
        [Min(1)] public int gridWidth = 1;
        [Min(1)] public int gridHeight = 1;
        [Min(1)] public int gridDepth = 1;

        [Header("Normalization")]
        public bool normalizedToGrid = true;
        public float sourceHorizontalSpanMeters = 0f;
        public float targetHorizontalSpanMeters = 2f;
        public float appliedUniformScale = 1f;

        [Header("Manual Override")]
        public LLSizeOverrideMode sizeOverrideMode = LLSizeOverrideMode.None;
        [Min(0.1f)] public float overrideTargetHorizontalSpanMeters = 2f;
        [Min(1)] public int overrideGridWidth = 1;
        [Min(1)] public int overrideGridDepth = 1;
        public bool overrideCategoryAndCollider = false;
        public LLModuleCategory overrideCategory = LLModuleCategory.Other;
        public LLColliderAuthoringMode overrideColliderMode = LLColliderAuthoringMode.Auto;

        [Header("LOD Roots")]
        public Transform lod0Root;
        public Transform lod1Root;
        public Transform collisionRoot;

        [Header("LOD Thresholds")]
        [Range(0.01f, 1f)] public float lod0ScreenRelativeHeight = 0.28f;
        [Range(0f, 1f)] public float lod1ScreenRelativeHeight = 0f;
        [Range(0f, 1f)] public float lodFadeWidth = 0.03f;

        [Header("Render / Collision")]
        public bool enableCrossFade = true;
        public bool neverCullWhileVisible = true;
        public bool recalculateBounds = true;
        public bool castShadowsOnLod0 = true;
        public bool receiveShadowsOnLod0 = true;
        public bool castShadowsOnLod1 = false;
        public bool receiveShadowsOnLod1 = false;
        public bool keepCollidersAlwaysEnabled = true;

        public Vector3 WorldSize => new Vector3(
            gridWidth * cellSizeMeters,
            gridHeight * cellSizeMeters,
            gridDepth * cellSizeMeters);

        public void AutoAssignRootsByName()
        {
            lod0Root = transform.Find("LOD0");
            lod1Root = transform.Find("LOD1");
            collisionRoot = transform.Find("Collision");
        }

        public void RebuildLODGroup()
        {
            AutoAssignRootsByName();

            LODGroup group = GetComponent<LODGroup>();
            if (group == null)
            {
                group = gameObject.AddComponent<LODGroup>();
            }

            Renderer[] lod0Renderers = GetRenderers(lod0Root);
            Renderer[] lod1Renderers = GetRenderers(lod1Root);
            ApplyRendererSettings(lod0Renderers, castShadowsOnLod0, receiveShadowsOnLod0);
            ApplyRendererSettings(lod1Renderers, castShadowsOnLod1, receiveShadowsOnLod1);

            List<LOD> lods = new List<LOD>(2);
            if (lod0Renderers.Length > 0)
            {
                LOD lod0 = new LOD(lod0ScreenRelativeHeight, lod0Renderers);
                lod0.fadeTransitionWidth = lodFadeWidth;
                lods.Add(lod0);
            }

            if (lod1Renderers.Length > 0)
            {
                float finalHeight = neverCullWhileVisible ? 0f : lod1ScreenRelativeHeight;
                LOD lod1 = new LOD(finalHeight, lod1Renderers);
                lod1.fadeTransitionWidth = neverCullWhileVisible ? 0f : lodFadeWidth;
                lods.Add(lod1);
            }

            if (lods.Count == 0)
            {
                Debug.LogWarning("[LLModularBuildingPiece] No renderers found for " + name + ".", this);
                return;
            }

            group.animateCrossFading = enableCrossFade;
            group.fadeMode = enableCrossFade ? LODFadeMode.CrossFade : LODFadeMode.None;
            group.SetLODs(lods.ToArray());

            if (recalculateBounds)
            {
                group.RecalculateBounds();
            }

            SetCollisionState(true);
        }

        public void SetCollisionState(bool enabledState)
        {
            if (!keepCollidersAlwaysEnabled || collisionRoot == null)
            {
                return;
            }

            Collider[] colliders = collisionRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = enabledState;
            }
        }

        [ContextMenu("Apply Size Override Now")]
        public void ApplySizeOverrideNow()
        {
            if (!TryApplySizeOverrideNow(logWarnings: true))
            {
                Debug.LogWarning("[LLModularBuildingPiece] Could not apply size override for " + name + ".", this);
                return;
            }

            AutoAssignRootsByName();
            RebuildLODGroup();
        }

        [ContextMenu("Set 2m Default")]
        public void ResetOverrideToDefault2m()
        {
            sizeOverrideMode = LLSizeOverrideMode.None;
            overrideTargetHorizontalSpanMeters = 2f;
            overrideGridWidth = 1;
            overrideGridDepth = 1;
            targetHorizontalSpanMeters = 2f;
            gridWidth = 1;
            gridDepth = 1;
            normalizedToGrid = true;
        }


        [ContextMenu("Apply Shadow Settings")]
        public void ApplyShadowSettingsNow()
        {
            AutoAssignRootsByName();
            ApplyRendererSettings(GetRenderers(lod0Root), castShadowsOnLod0, receiveShadowsOnLod0);
            ApplyRendererSettings(GetRenderers(lod1Root), castShadowsOnLod1, receiveShadowsOnLod1);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
                if (lod0Root != null) EditorUtility.SetDirty(lod0Root);
                if (lod1Root != null) EditorUtility.SetDirty(lod1Root);
                EditorUtility.SetDirty(gameObject);
            }
#endif
        }

        [ContextMenu("Shadows On")]
        public void SetShadowsOn()
        {
            castShadowsOnLod0 = true;
            receiveShadowsOnLod0 = true;
            castShadowsOnLod1 = true;
            receiveShadowsOnLod1 = true;
            ApplyShadowSettingsNow();
        }

        [ContextMenu("Shadows Off")]
        public void SetShadowsOff()
        {
            castShadowsOnLod0 = false;
            receiveShadowsOnLod0 = false;
            castShadowsOnLod1 = false;
            receiveShadowsOnLod1 = false;
            ApplyShadowSettingsNow();
        }

        public bool TryApplySizeOverrideNow(bool logWarnings = false)
        {
            AutoAssignRootsByName();

            Transform measuredRoot = lod0Root != null ? lod0Root : transform;
            Renderer[] measuredRenderers = GetRenderers(measuredRoot);
            if (measuredRenderers.Length == 0)
            {
                if (logWarnings)
                {
                    Debug.LogWarning("[LLModularBuildingPiece] No LOD0 renderers found to measure on " + name + ".", this);
                }
                return false;
            }

            if (!TryGetCombinedBounds(measuredRenderers, out Bounds bounds))
            {
                if (logWarnings)
                {
                    Debug.LogWarning("[LLModularBuildingPiece] Failed to calculate bounds on " + name + ".", this);
                }
                return false;
            }

            float currentHorizontalSpan = Mathf.Max(bounds.size.x, bounds.size.z);
            if (currentHorizontalSpan <= 0.0001f)
            {
                if (logWarnings)
                {
                    Debug.LogWarning("[LLModularBuildingPiece] Current horizontal span is zero on " + name + ".", this);
                }
                return false;
            }

            float desiredSpan = 2f;
            switch (sizeOverrideMode)
            {
                case LLSizeOverrideMode.TargetSpanMeters:
                    desiredSpan = Mathf.Max(0.1f, overrideTargetHorizontalSpanMeters);
                    targetHorizontalSpanMeters = desiredSpan;
                    break;

                case LLSizeOverrideMode.GridFootprint:
                    gridWidth = Mathf.Max(1, overrideGridWidth);
                    gridDepth = Mathf.Max(1, overrideGridDepth);
                    desiredSpan = Mathf.Max(gridWidth, gridDepth) * Mathf.Max(0.1f, cellSizeMeters);
                    targetHorizontalSpanMeters = desiredSpan;
                    break;

                default:
                    desiredSpan = Mathf.Max(0.1f, targetHorizontalSpanMeters > 0f ? targetHorizontalSpanMeters : 2f);
                    targetHorizontalSpanMeters = desiredSpan;
                    break;
            }

            if (overrideCategoryAndCollider)
            {
                category = overrideCategory;
                colliderMode = overrideColliderMode;
            }

            float scaleMultiplier = desiredSpan / currentHorizontalSpan;
            sourceHorizontalSpanMeters = currentHorizontalSpan;
            appliedUniformScale *= scaleMultiplier;
            normalizedToGrid = true;

            ApplyUniformScaleToRoot(lod0Root, scaleMultiplier);
            ApplyUniformScaleToRoot(lod1Root, scaleMultiplier);
            ApplyUniformScaleToRoot(collisionRoot, scaleMultiplier);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
                if (lod0Root != null) EditorUtility.SetDirty(lod0Root);
                if (lod1Root != null) EditorUtility.SetDirty(lod1Root);
                if (collisionRoot != null) EditorUtility.SetDirty(collisionRoot);
                EditorUtility.SetDirty(gameObject);
            }
#endif

            return true;
        }

        public static LLModuleCategory InferCategoryFromName(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return LLModuleCategory.Other;
            }

            string lower = sourceName.ToLowerInvariant();
            List<(string keyword, LLModuleCategory category)> candidates = new List<(string keyword, LLModuleCategory category)>
            {
                ("window", LLModuleCategory.Window),
                ("door", LLModuleCategory.Door),
                ("floor", LLModuleCategory.Floor),
                ("ceiling", LLModuleCategory.Ceiling),
                ("roof", LLModuleCategory.Roof),
                ("foundation", LLModuleCategory.Foundation),
                ("stair", LLModuleCategory.Stair),
                ("balcony", LLModuleCategory.Porch),
                ("porch", LLModuleCategory.Porch),
                ("trim", LLModuleCategory.Trim),
                ("profile", LLModuleCategory.Trim),
                ("pillar", LLModuleCategory.Detail),
                ("beam", LLModuleCategory.Detail),
                ("detail", LLModuleCategory.Detail),
                ("wall", LLModuleCategory.Wall),
            };

            int bestIndex = int.MaxValue;
            LLModuleCategory bestCategory = LLModuleCategory.Other;
            for (int i = 0; i < candidates.Count; i++)
            {
                int index = lower.IndexOf(candidates[i].keyword, StringComparison.Ordinal);
                if (index >= 0 && index < bestIndex)
                {
                    bestIndex = index;
                    bestCategory = candidates[i].category;
                }
            }

            return bestCategory;
        }

        public static LLColliderAuthoringMode InferColliderModeFromCategory(LLModuleCategory category)
        {
            switch (category)
            {
                case LLModuleCategory.Wall:
                case LLModuleCategory.Floor:
                case LLModuleCategory.Roof:
                    return LLColliderAuthoringMode.SingleBox;
                default:
                    return LLColliderAuthoringMode.MeshPerSubMesh;
            }
        }

        public static LLColliderAuthoringMode InferColliderModeFromName(string sourceName)
        {
            return InferColliderModeFromCategory(InferCategoryFromName(sourceName));
        }

        private void ApplyRendererSettings(Renderer[] renderers, bool castShadows, bool receiveShadows)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = receiveShadows;
            }
        }

        private static Renderer[] GetRenderers(Transform root)
        {
            return root == null ? Array.Empty<Renderer>() : root.GetComponentsInChildren<Renderer>(true);
        }

        private static bool TryGetCombinedBounds(Renderer[] renderers, out Bounds combined)
        {
            combined = default;
            bool foundAny = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!foundAny)
                {
                    combined = renderer.bounds;
                    foundAny = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            return foundAny;
        }

        private static void ApplyUniformScaleToRoot(Transform root, float multiplier)
        {
            if (root == null)
            {
                return;
            }

            Vector3 localScale = root.localScale;
            root.localScale = new Vector3(
                localScale.x * multiplier,
                localScale.y * multiplier,
                localScale.z * multiplier);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            float minLod1 = neverCullWhileVisible ? 0f : 0.001f;
            lod0ScreenRelativeHeight = Mathf.Clamp(lod0ScreenRelativeHeight, minLod1 + 0.01f, 1f);
            lod1ScreenRelativeHeight = Mathf.Clamp(lod1ScreenRelativeHeight, minLod1, Mathf.Max(minLod1, lod0ScreenRelativeHeight - 0.01f));
            cellSizeMeters = Mathf.Max(0.1f, cellSizeMeters);
            gridWidth = Mathf.Max(1, gridWidth);
            gridHeight = Mathf.Max(1, gridHeight);
            gridDepth = Mathf.Max(1, gridDepth);
            targetHorizontalSpanMeters = Mathf.Max(0.1f, targetHorizontalSpanMeters);
            appliedUniformScale = Mathf.Max(0.0001f, appliedUniformScale);
            overrideTargetHorizontalSpanMeters = Mathf.Max(0.1f, overrideTargetHorizontalSpanMeters);
            overrideGridWidth = Mathf.Max(1, overrideGridWidth);
            overrideGridDepth = Mathf.Max(1, overrideGridDepth);
        }
#endif
    }
}

#if UNITY_EDITOR
namespace LandAndLedgers.Buildings
{
    [CustomEditor(typeof(LLModularBuildingPiece))]
    internal sealed class LLModularBuildingPieceInlineEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

            LLModularBuildingPiece piece = (LLModularBuildingPiece)target;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply Size Override Now", GUILayout.Height(28f)))
                {
                    Undo.RecordObject(piece, "Apply Size Override Now");
                    piece.ApplySizeOverrideNow();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(piece);
                    EditorUtility.SetDirty(piece);
                }

                if (GUILayout.Button("Set 2m Default", GUILayout.Height(28f)))
                {
                    Undo.RecordObject(piece, "Set 2m Default");
                    piece.ResetOverrideToDefault2m();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(piece);
                    EditorUtility.SetDirty(piece);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply Shadow Settings", GUILayout.Height(24f)))
                {
                    Undo.RecordObject(piece, "Apply Shadow Settings");
                    piece.ApplyShadowSettingsNow();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(piece);
                    EditorUtility.SetDirty(piece);
                }

                if (GUILayout.Button("Shadows On", GUILayout.Height(24f)))
                {
                    Undo.RecordObject(piece, "Shadows On");
                    piece.SetShadowsOn();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(piece);
                    EditorUtility.SetDirty(piece);
                }

                if (GUILayout.Button("Shadows Off", GUILayout.Height(24f)))
                {
                    Undo.RecordObject(piece, "Shadows Off");
                    piece.SetShadowsOff();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(piece);
                    EditorUtility.SetDirty(piece);
                }
            }

            if (GUILayout.Button("Rebuild LOD Group", GUILayout.Height(24f)))
            {
                Undo.RecordObject(piece, "Rebuild LOD Group");
                piece.RebuildLODGroup();
                PrefabUtility.RecordPrefabInstancePropertyModifications(piece);
                EditorUtility.SetDirty(piece);
            }
        }
    }
}
#endif
