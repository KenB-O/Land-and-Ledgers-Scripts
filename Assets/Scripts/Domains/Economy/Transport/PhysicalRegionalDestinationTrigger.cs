using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    /// <summary>
    /// Runtime identity binding for one authored regional town building trigger.
    /// The arrival is accepted only by the active coupled freight team that
    /// names this exact journey location and business.
    /// </summary>
    public sealed class PhysicalRegionalDestinationTrigger : MonoBehaviour
    {
        public string LocationId { get; private set; } = string.Empty;
        public string BusinessId { get; private set; } = string.Empty;

        public void Bind(string locationId, string businessId)
        {
            LocationId = locationId ?? string.Empty;
            BusinessId = businessId ?? string.Empty;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null || string.IsNullOrWhiteSpace(LocationId)) return;
            CoupledTransportMovementController movement =
                other.GetComponentInParent<CoupledTransportMovementController>();
            movement?.OnRegionalDestinationTrigger(LocationId, BusinessId);
        }
    }
}
