using LandLedgers.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.UI
{
    public sealed class PropertyPreviewCameraRig
    {
        private const int MinimumPreviewWidth = 1280;
        private const int MinimumPreviewHeight = 960;
        private const int MaximumPreviewWidth = 2048;
        private const int MaximumPreviewHeight = 2048;

        private Camera previewCamera;
        private RenderTexture previewTexture;
        private PreviewFrame pendingFrame;
        private bool hasPendingFrame;

        public void BeginPreview(
            TownWorldController townWorld,
            int plotId,
            int buildingId,
            RawImage targetImage,
            TMP_Text fallbackText)
        {
            if (targetImage == null)
            {
                SetFallback(fallbackText, "Property preview unavailable.");
                return;
            }

            targetImage.texture = null;
            targetImage.color = new Color(0.18f, 0.2f, 0.18f, 1f);
            hasPendingFrame = false;
            pendingFrame = default;
            if (!TryBuildPreviewFrame(townWorld, plotId, buildingId, targetImage, out PreviewFrame frame))
            {
                SetFallback(fallbackText, plotId < 0 && buildingId < 0
                    ? "No site preview is needed for this review."
                    : "No framed site is available for preview.");
                return;
            }

            pendingFrame = frame;
            hasPendingFrame = true;
            SetFallback(fallbackText, "Preparing stable site preview...");
        }

        public bool TryRenderStablePreview(
            TownWorldController townWorld,
            int plotId,
            int buildingId,
            RawImage targetImage,
            TMP_Text fallbackText)
        {
            if (targetImage == null)
            {
                SetFallback(fallbackText, "Property preview unavailable.");
                return true;
            }

            if (!TryBuildPreviewFrame(townWorld, plotId, buildingId, targetImage, out PreviewFrame frame))
            {
                targetImage.texture = null;
                targetImage.color = new Color(0.18f, 0.2f, 0.18f, 1f);
                hasPendingFrame = false;
                pendingFrame = default;
                SetFallback(fallbackText, plotId < 0 && buildingId < 0
                    ? "No site preview is needed for this review."
                    : "No framed site is available for preview.");
                return true;
            }

            if (!hasPendingFrame || !pendingFrame.IsStableWith(frame))
            {
                pendingFrame = frame;
                hasPendingFrame = true;
                targetImage.texture = null;
                targetImage.color = new Color(0.18f, 0.2f, 0.18f, 1f);
                SetFallback(fallbackText, "Preparing stable site preview...");
                return false;
            }

            RenderFrame(frame, targetImage, fallbackText);
            hasPendingFrame = false;
            pendingFrame = default;
            return true;
        }

        private void RenderFrame(PreviewFrame frame, RawImage targetImage, TMP_Text fallbackText)
        {
            EnsureCamera();

            Vector2Int textureSize = frame.TextureSize;
            EnsureRenderTexture(textureSize.x, textureSize.y);

            previewCamera.targetTexture = previewTexture;
            previewCamera.aspect = textureSize.x / (float)textureSize.y;
            previewCamera.orthographic = true;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = frame.FarClipPlane;
            previewCamera.orthographicSize = frame.OrthographicSize;
            previewCamera.transform.position = frame.Position;
            previewCamera.transform.rotation = frame.Rotation;
            previewCamera.Render();

            targetImage.texture = previewTexture;
            targetImage.color = Color.white;
            SetFallback(fallbackText, string.Empty);
        }

        public void Dispose()
        {
            if (previewTexture != null)
            {
                previewTexture.Release();
                Object.Destroy(previewTexture);
                previewTexture = null;
            }

            if (previewCamera != null)
            {
                Object.Destroy(previewCamera.gameObject);
                previewCamera = null;
            }
        }

        private static bool TryBuildPreviewFrame(
            TownWorldController townWorld,
            int plotId,
            int buildingId,
            RawImage targetImage,
            out PreviewFrame frame)
        {
            frame = default;
            if (!TryResolveBounds(townWorld, plotId, buildingId, out Vector3 center, out float widthMeters, out float depthMeters, out float framingRadius))
            {
                return false;
            }

            Vector2Int textureSize = GetPreviewTextureSize(targetImage);
            float aspect = textureSize.x / (float)textureSize.y;
            float halfWidth = (widthMeters * 0.5f) + 3f;
            float halfDepth = (depthMeters * 0.5f) + 3f;
            float orthographicSize = Mathf.Max(10f, Mathf.Max(halfDepth, halfWidth / Mathf.Max(0.75f, aspect)));
            Vector3 offset = new(framingRadius * 0.92f, framingRadius * 1.28f, -framingRadius * 0.92f);
            Vector3 position = center + offset;
            Quaternion rotation = Quaternion.LookRotation((center - position).normalized, Vector3.up);
            frame = new PreviewFrame(
                center,
                position,
                rotation,
                orthographicSize,
                Mathf.Max(240f, framingRadius * 12f),
                textureSize);
            return true;
        }

        private readonly struct PreviewFrame
        {
            public PreviewFrame(
                Vector3 center,
                Vector3 position,
                Quaternion rotation,
                float orthographicSize,
                float farClipPlane,
                Vector2Int textureSize)
            {
                Center = center;
                Position = position;
                Rotation = rotation;
                OrthographicSize = orthographicSize;
                FarClipPlane = farClipPlane;
                TextureSize = textureSize;
            }

            public readonly Vector3 Center;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float OrthographicSize;
            public readonly float FarClipPlane;
            public readonly Vector2Int TextureSize;

            public bool IsStableWith(PreviewFrame other)
            {
                return TextureSize == other.TextureSize
                    && Vector3.SqrMagnitude(Center - other.Center) <= 0.0001f
                    && Vector3.SqrMagnitude(Position - other.Position) <= 0.0001f
                    && Quaternion.Angle(Rotation, other.Rotation) <= 0.05f
                    && Mathf.Abs(OrthographicSize - other.OrthographicSize) <= 0.001f
                    && Mathf.Abs(FarClipPlane - other.FarClipPlane) <= 0.01f;
            }
        }

        private void EnsureCamera()
        {
            if (previewCamera != null)
            {
                return;
            }

            GameObject cameraObject = new("Property Preview Camera");
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.enabled = false;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.17f, 0.19f, 0.17f, 1f);
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;
            previewCamera.useOcclusionCulling = false;
        }

        private void EnsureRenderTexture(int width, int height)
        {
            if (previewTexture != null && previewTexture.width == width && previewTexture.height == height)
            {
                return;
            }

            if (previewTexture != null)
            {
                previewTexture.Release();
                Object.Destroy(previewTexture);
                previewTexture = null;
            }

            previewTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Ownership Property Preview",
                antiAliasing = 4,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };

            previewTexture.Create();
        }

        private static Vector2Int GetPreviewTextureSize(RawImage targetImage)
        {
            Rect rect = targetImage.rectTransform != null ? targetImage.rectTransform.rect : default;
            float width = rect.width > 0f ? rect.width : 560f;
            float height = rect.height > 0f ? rect.height : 420f;

            int textureWidth = Mathf.Clamp(Mathf.CeilToInt(width * 2.4f), MinimumPreviewWidth, MaximumPreviewWidth);
            int textureHeight = Mathf.Clamp(Mathf.CeilToInt(height * 2.4f), MinimumPreviewHeight, MaximumPreviewHeight);
            return new Vector2Int(textureWidth, textureHeight);
        }

        private static bool TryResolveBounds(
            TownWorldController townWorld,
            int plotId,
            int buildingId,
            out Vector3 center,
            out float widthMeters,
            out float depthMeters,
            out float framingRadius)
        {
            center = default;
            widthMeters = 0f;
            depthMeters = 0f;
            framingRadius = 0f;

            if (townWorld == null)
            {
                return false;
            }

            GridRect rect = default;
            if (buildingId >= 0 && townWorld.TryGetBuildingById(buildingId, out PlacedBuilding building) && building != null)
            {
                rect = building.footprint;
            }
            else if (plotId >= 0 && townWorld.TryGetPlotById(plotId, out TownPlot plot) && plot != null)
            {
                rect = plot.bounds;
            }

            if (!rect.IsValid)
            {
                return false;
            }

            float cellSize = townWorld.Settings != null ? townWorld.Settings.cellSizeMeters : 4f;
            widthMeters = Mathf.Max(10f, rect.width * cellSize);
            depthMeters = Mathf.Max(10f, rect.depth * cellSize);
            framingRadius = Mathf.Max(widthMeters, depthMeters);
            center = townWorld.GetWorldCenterForRect(rect, 1f);
            return true;
        }

        private static void SetFallback(TMP_Text fallbackText, string value)
        {
            if (fallbackText == null)
            {
                return;
            }

            fallbackText.text = value ?? string.Empty;
            fallbackText.gameObject.SetActive(!string.IsNullOrWhiteSpace(value));
        }
    }
}
