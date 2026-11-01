using UnityEngine;

namespace LandLedgers.World
{
    /// <summary>
    /// Marks runtime/editor-generated world objects that TownWorldController owns and may safely rebuild.
    /// Authored scene content must never carry this marker.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedWorldContentMarker : MonoBehaviour
    {
    }
}
