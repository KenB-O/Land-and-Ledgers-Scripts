using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    public enum TransportVisualState
    {
        Idle = 0,
        MountedHorse = 1,
        DrivingWagon = 2,
    }

    /// <summary>Scene representation only; economic identity remains in runtime authorities.</summary>
    public sealed class HorseWorldView : MonoBehaviour
    {
        [SerializeField] private string animalId = string.Empty;
        [SerializeField] private Transform riderMount;
        [SerializeField] private Transform dismountLeft;
        [SerializeField] private Transform dismountRight;
        [SerializeField] private Transform hitchAnchor;

        public string AnimalId => animalId ?? string.Empty;
        public Transform RiderMount => riderMount;
        public Transform DismountLeft => dismountLeft;
        public Transform DismountRight => dismountRight;
        public Transform HitchAnchor => hitchAnchor;

        public void BindAnimal(string id) => animalId = id ?? string.Empty;
        public void ConfigureAnchors(Transform rider, Transform left, Transform right, Transform hitch)
        {
            riderMount = rider;
            dismountLeft = left;
            dismountRight = right;
            hitchAnchor = hitch;
        }
    }

}
