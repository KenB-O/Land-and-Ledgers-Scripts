using UnityEngine;

namespace LandLedgers.CameraSystem
{
    [DisallowMultipleComponent]
    public sealed class StrategyCameraBounds : MonoBehaviour
    {
        [Header("World Bounds")]
        [SerializeField]
        private bool boundsEnabled = true;

        [SerializeField]
        private bool useTransformPositionAsCenter = false;

        [SerializeField]
        private Vector2 center = Vector2.zero;

        [SerializeField, Min(1f)]
        private Vector2 size = new(320f, 320f);

        [Header("Soft Limit")]
        [SerializeField, Min(0f)]
        private float softMargin = 36f;

        [SerializeField, Min(0f)]
        private float returnStrength = 10f;

        [Header("Debug")]
        [SerializeField]
        private Color innerColor = new(1f, 0.75f, 0.2f, 0.85f);

        [SerializeField]
        private Color softColor = new(1f, 0.35f, 0.1f, 0.5f);

        public bool BoundsEnabled => boundsEnabled;
        public float SoftMargin => softMargin;
        public float ReturnStrength => returnStrength;

        public Vector2 Center
        {
            get
            {
                if (useTransformPositionAsCenter)
                {
                    Vector3 position = transform.position;
                    return new Vector2(position.x, position.z);
                }

                return center;
            }
        }

        public Vector2 Size => new(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));

        public Rect InnerRect
        {
            get
            {
                Vector2 safeSize = Size;
                return new Rect(Center - safeSize * 0.5f, safeSize);
            }
        }

        public Rect SoftRect
        {
            get
            {
                Rect inner = InnerRect;
                float margin = Mathf.Max(0f, softMargin);
                inner.xMin -= margin;
                inner.xMax += margin;
                inner.yMin -= margin;
                inner.yMax += margin;
                return inner;
            }
        }

        public void SetWorldBounds(Vector2 newCenter, Vector2 newSize, float newSoftMargin)
        {
            center = newCenter;
            size = new Vector2(Mathf.Max(1f, newSize.x), Mathf.Max(1f, newSize.y));
            softMargin = Mathf.Max(0f, newSoftMargin);
            useTransformPositionAsCenter = false;
        }

        private void OnValidate()
        {
            size.x = Mathf.Max(1f, size.x);
            size.y = Mathf.Max(1f, size.y);
            softMargin = Mathf.Max(0f, softMargin);
            returnStrength = Mathf.Max(0f, returnStrength);
        }

        private void OnDrawGizmosSelected()
        {
            if (!boundsEnabled)
            {
                return;
            }

            DrawRect(InnerRect, innerColor);

            if (softMargin > 0f)
            {
                DrawRect(SoftRect, softColor);
            }
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Gizmos.color = color;
            Vector3 a = new(rect.xMin, 0f, rect.yMin);
            Vector3 b = new(rect.xMax, 0f, rect.yMin);
            Vector3 c = new(rect.xMax, 0f, rect.yMax);
            Vector3 d = new(rect.xMin, 0f, rect.yMax);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
    }
}
