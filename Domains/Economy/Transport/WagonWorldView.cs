using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    /// <summary>Scene representation of a persistent Wagon EquipmentAsset.</summary>
    public sealed class WagonWorldView : MonoBehaviour
    {
        [SerializeField] private string assetId = string.Empty;
        [SerializeField] private Transform driverSeat;
        [SerializeField] private Transform hitchPoint;
        [SerializeField] private Transform cargoRoot;
        [SerializeField] private Transform loadBounds;

        public string AssetId => assetId ?? string.Empty;
        public Transform DriverSeat => driverSeat;
        public Transform HitchPoint => hitchPoint;
        public Transform CargoRoot => cargoRoot;
        public Transform LoadBounds => loadBounds;

        public void BindAsset(string id) => assetId = id ?? string.Empty;

        public void ConfigureAnchors(Transform driver, Transform hitch, Transform cargo, Transform bounds)
        {
            driverSeat = driver;
            hitchPoint = hitch;
            cargoRoot = cargo;
            loadBounds = bounds;
        }
    }
}
