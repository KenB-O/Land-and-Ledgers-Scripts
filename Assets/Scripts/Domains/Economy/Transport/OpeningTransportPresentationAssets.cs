using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    [CreateAssetMenu(menuName = "Land & Ledgers/Transport/Opening Presentation Assets")]
    public sealed class OpeningTransportPresentationAssets : ScriptableObject
    {
        [SerializeField] private GameObject horsePrefab;
        [SerializeField] private GameObject wagonPrefab;
        [SerializeField] private RuntimeAnimatorController horseIdleController;

        public GameObject HorsePrefab => horsePrefab;
        public GameObject WagonPrefab => wagonPrefab;
        public RuntimeAnimatorController HorseIdleController => horseIdleController;
    }
}
