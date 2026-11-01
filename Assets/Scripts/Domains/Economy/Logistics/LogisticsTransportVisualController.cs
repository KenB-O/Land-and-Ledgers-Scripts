using UnityEngine;

namespace LandLedgers.Economy
{
    [DisallowMultipleComponent]
    public sealed class LogisticsTransportVisualController : MonoBehaviour
    {
        [SerializeField] private GameObject cargoChild;
        [SerializeField] private GameObject horseChild;

        public void ApplyVisualState(LogisticsTransportVisualState state)
        {
            CacheChildren();
            bool cargoVisible = state == LogisticsTransportVisualState.LoadedWagon;
            bool horseVisible = true;

            if (cargoChild != null)
            {
                cargoChild.SetActive(cargoVisible);
            }

            if (horseChild != null)
            {
                horseChild.SetActive(horseVisible);
            }
        }

        private void CacheChildren()
        {
            cargoChild ??= FindNamedChild("Cargo", "cargo", "WagonCargo", "Load", "LoadedCargo");
            horseChild ??= FindNamedChild("Horse", "horse", "Team", "WorkHorse");
        }

        private GameObject FindNamedChild(params string[] names)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == transform)
                {
                    continue;
                }

                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (string.Equals(child.name, names[nameIndex], System.StringComparison.OrdinalIgnoreCase))
                    {
                        return child.gameObject;
                    }
                }
            }

            return null;
        }
    }
}
