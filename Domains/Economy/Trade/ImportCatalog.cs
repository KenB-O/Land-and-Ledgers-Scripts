using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Trade
{
    /// <summary>
    /// EQU-1: one importable raw material. Off-map origins are DECLARED trade
    /// partners — named, with an honest distance and transit time — never a vague
    /// "imported" flag. Canon: "Off-map counterparties may remain coarse, but the
    /// source class/reason persists" and "Off-map transitions preserve travel time,
    /// route, cargo, identity and cost."
    ///
    /// Prices are calibration, not canon (Canon Part XV holds).
    /// </summary>
    [Serializable]
    public sealed class ImportMaterial
    {
        public string MaterialId => materialId ?? string.Empty;
        public string DisplayName => displayName ?? MaterialId;
        public string OriginName => originName ?? string.Empty;
        public int DistanceMiles => Mathf.Max(0, distanceMiles);
        public int TransitDays => Mathf.Max(1, transitDays);
        public int PricePerUnitCents => Mathf.Max(0, pricePerUnitCents);

        [SerializeField]
        private string materialId = string.Empty;
        [SerializeField]
        private string displayName = string.Empty;
        [SerializeField]
        private string originName = string.Empty;
        [SerializeField, Min(0)]
        private int distanceMiles;
        [SerializeField, Min(1)]
        private int transitDays = 7;
        [SerializeField, Min(0)]
        private int pricePerUnitCents;

        public ImportMaterial() { }

        public ImportMaterial(string materialId, string displayName, string originName,
            int distanceMiles, int transitDays, int pricePerUnitCents)
        {
            this.materialId = materialId ?? string.Empty;
            this.displayName = displayName ?? materialId ?? string.Empty;
            this.originName = originName ?? string.Empty;
            this.distanceMiles = Mathf.Max(0, distanceMiles);
            this.transitDays = Mathf.Max(1, transitDays);
            this.pricePerUnitCents = Mathf.Max(0, pricePerUnitCents);
        }
    }

    /// <summary>
    /// EQU-1: the import catalog. Built-in materials cover the blacksmith's needs
    /// (iron, steel, forge coal) plus lumber as the honest stand-in while the
    /// timber/logging chain below the sawmill is still deferred. New materials
    /// register through <see cref="RegisterMaterial"/> — no code changes needed.
    /// </summary>
    public static class ImportCatalog
    {
        public const string IronStockId = "iron-stock";
        public const string SteelStockId = "steel-stock";
        public const string ForgeCoalId = "forge-coal";
        public const string LumberId = "lumber";
        /// <summary>
        /// EQP-5: hemlock/oak tanbark for the tannery. Historical: tanbark was a
        /// real traded commodity in the 1800s US (hemlock dominant; bark peelers
        /// and teamsters supplied the tanyards). Local bark supply waits on the
        /// timber chain; until then the honest path is the named off-map origin.
        /// </summary>
        public const string TanbarkId = "tanbark";

        private static readonly Dictionary<string, ImportMaterial> materials =
            new Dictionary<string, ImportMaterial>(StringComparer.Ordinal);

        static ImportCatalog()
        {
            // Kennedy-authorized stand-ins: a full mine is a later tier item.
            // Origins are named and priced; transit is real days, not instant.
            RegisterMaterial(new ImportMaterial(IronStockId, "Iron stock (bars)",
                "Pittsburgh ironworks, via railhead", 220, 14, 45));
            RegisterMaterial(new ImportMaterial(SteelStockId, "Steel stock (bars)",
                "Pittsburgh ironworks, via railhead", 220, 14, 120));
            RegisterMaterial(new ImportMaterial(ForgeCoalId, "Forge coal",
                "Off-map coal dealer, via railhead", 180, 12, 18));
            RegisterMaterial(new ImportMaterial(LumberId, "Lumber (boards)",
                "Off-map timber country, via wagon road", 60, 6, 25));
            RegisterMaterial(new ImportMaterial(TanbarkId, "Tanbark (hemlock/oak)",
                "Off-map bark country, via wagon road", 90, 8, 12));
        }

        /// <summary>Registers (or replaces) a material. Returns a diagnostic on rejection.</summary>
        public static string RegisterMaterial(ImportMaterial material)
        {
            if (material == null) return "ImportCatalog: cannot register a null material.";
            if (string.IsNullOrWhiteSpace(material.MaterialId))
                return "ImportCatalog: material needs an id.";
            if (string.IsNullOrWhiteSpace(material.OriginName))
                return $"ImportCatalog: material '{material.MaterialId}' needs a named origin — no anonymous sources.";
            materials[material.MaterialId] = material;
            return null;
        }

        public static ImportMaterial Get(string materialId)
        {
            if (string.IsNullOrWhiteSpace(materialId)) return null;
            materials.TryGetValue(materialId, out ImportMaterial material);
            return material;
        }

        public static IReadOnlyCollection<ImportMaterial> All => materials.Values;
    }
}
