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

        private void OnDrawGizmos()
        {
            if (Application.isPlaying)
            {
                return;
            }

            Gizmos.color = GetColor(type);
            Gizmos.DrawSphere(transform.position, gizmoRadius);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * gizmoRadius * 2f);
        }

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
