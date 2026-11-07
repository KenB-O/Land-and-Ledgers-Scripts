using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    [CreateAssetMenu(menuName = "Land & Ledgers/Transport/Opening Presentation Assets")]
    public sealed class OpeningTransportPresentationAssets : ScriptableObject
    {
        [SerializeField] private GameObject horsePrefab;
        [SerializeField] private GameObject wagonPrefab;
        [SerializeField] private GameObject regionalTownPrefab;
        [SerializeField] private RuntimeAnimatorController horseIdleController;

        public GameObject HorsePrefab => horsePrefab;
        public GameObject WagonPrefab => wagonPrefab;
        public GameObject RegionalTownPrefab => regionalTownPrefab;
        public RuntimeAnimatorController HorseIdleController => horseIdleController;
    }
}
