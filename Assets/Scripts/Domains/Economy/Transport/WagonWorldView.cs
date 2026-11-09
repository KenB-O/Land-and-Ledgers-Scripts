using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    /// <summary>Scene representation of a persistent Wagon EquipmentAsset.</summary>
    public sealed class WagonWorldView : MonoBehaviour
    {
        private const string CanvasMaterialResourceFolder = "WagonCanvasVariants";
        private static Material[] canvasMaterials;
        private readonly List<Renderer> canvasRenderers = new List<Renderer>();
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

        /// <summary>Restores or assigns the durable canvas finish for this wagon.</summary>
        public void ApplyCanvasMaterial(EquipmentAsset asset)
        {
            if (asset == null || !asset.IsWagon) return;
            Material[] variants = GetCanvasMaterials();
            if (variants.Length == 0) return;

            if (asset.CanvasMaterialVariantIndex < 0 || asset.CanvasMaterialVariantIndex >= variants.Length)
                asset.CanvasMaterialVariantIndex = Random.Range(0, variants.Length);

            Material selected = variants[asset.CanvasMaterialVariantIndex];
            if (selected == null) return;
            for (int i = 0; i < canvasRenderers.Count; i++)
                if (canvasRenderers[i] != null) canvasRenderers[i].sharedMaterial = selected;
        }

        private void Awake()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer candidate = renderers[i];
                if (candidate == null) continue;
                // CartCloth is slot zero on each canvas mesh in the authored wagon prefab.
                // Reading sharedMaterial avoids allocating a sharedMaterials array per renderer.
                if (candidate.sharedMaterial != null && candidate.sharedMaterial.name == "CartCloth")
                    canvasRenderers.Add(candidate);
            }
        }

        private static Material[] GetCanvasMaterials()
        {
            if (canvasMaterials == null)
            {
                canvasMaterials = new[]
                {
                    Resources.Load<Material>(CanvasMaterialResourceFolder + "/WagonCanvas_White"),
                    Resources.Load<Material>(CanvasMaterialResourceFolder + "/WagonCanvas_Tan"),
                    Resources.Load<Material>(CanvasMaterialResourceFolder + "/WagonCanvas_DarkTan"),
                    Resources.Load<Material>(CanvasMaterialResourceFolder + "/WagonCanvas_Weathered"),
                };
            }
            return canvasMaterials;
        }

        public void ConfigureAnchors(Transform driver, Transform hitch, Transform cargo, Transform bounds)
        {
            driverSeat = driver;
            hitchPoint = hitch;
            cargoRoot = cargo;
            loadBounds = bounds;
        }
    }
}
