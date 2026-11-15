using UnityEngine;

namespace LandLedgers.World
{
    [DisallowMultipleComponent]
    public sealed class BuildingAnchorMarker : MonoBehaviour
    {
        [SerializeField]
        private AnchorType type = AnchorType.FrontDoor;

        [SerializeField, Min(0.05f)]
        private float gizmoRadius = 0.35f;

        [SerializeField]
        [Tooltip("When enabled, placement systems may use this marker as the road/frontage access point that should be pinned to the nearest road edge. FrontDoor markers are treated as road-access candidates by default for backward compatibility.")]
        private bool useAsRoadAccessAnchor = false;

        public AnchorType Type => type;
        public bool UseAsRoadAccessAnchor => useAsRoadAccessAnchor || type == AnchorType.FrontDoor;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (Application.isPlaying || type == AnchorType.Service) return;

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.color = GetColor(type);
            Gizmos.matrix = transform.localToWorldMatrix;

            if (type == AnchorType.DropOff)
            {
                // Compact parcel/crate symbol: visible for authoring without obscuring the scene.
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.34f, 0.28f, 0.26f));
                Gizmos.DrawLine(new Vector3(-0.17f, 0.14f, 0f), new Vector3(0.17f, 0.14f, 0f));
                Gizmos.DrawLine(new Vector3(0f, 0.14f, -0.13f), new Vector3(0f, 0.14f, 0.13f));
            }
            else
            {
                float radius = Mathf.Min(gizmoRadius, 0.16f);
                Gizmos.DrawWireSphere(Vector3.zero, radius);
                Gizmos.DrawLine(Vector3.zero, Vector3.forward * radius * 2f);
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
#endif

        private static Color GetColor(AnchorType anchorType)
        {
            return anchorType switch
            {
                AnchorType.FrontDoor => new Color(0.2f, 0.85f, 1f, 1f),
                AnchorType.Service => new Color(0.5f, 0.9f, 0.35f, 1f),
                AnchorType.DropOff => new Color(1f, 0.75f, 0.2f, 1f),
                _ => Color.white
            };
        }
    }
}
