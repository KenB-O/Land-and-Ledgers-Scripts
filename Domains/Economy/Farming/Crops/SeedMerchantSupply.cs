using System;
using System.Collections.Generic;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// W5C: the seed merchant supply link. Survey result: T1E gave the general
    /// store a seed-supplier interface, but the store's own stock still named a
    /// free-text upstream — the FVS-flagged hole (seed with no real supplier)
    /// stayed open below it. This closes it the EQU-1 way rather than by
    /// inventing a new business type: the general store already retails seed,
    /// so what was missing is the NAMED upstream — off-map seed houses via the
    /// import catalog, and local seed-grower farms selling held-back lots.
    ///
    /// No new <c>BusinessType</c> value: the merchant runtime below keys to a
    /// business id and can sit behind a general store or a standalone seed
    /// merchant alike.
    /// </summary>
    public static class SeedMerchantSupply
    {
        /// <summary>
        /// The era's archetypal Toronto seed house (Steele, Briggs founded 1873;
        /// mail-order seed catalogues each late winter were the norm). Named
        /// off-map origin per the EQU-1 doctrine — never a vague "imported".
        /// Distance/transit/price are calibration (Canon Part XV).
        /// </summary>
        public const string DefaultSeedHouseOrigin = "Steele, Briggs Seed Co., Toronto, via railhead";

        public static string SeedMaterialIdFor(CropKind crop)
        {
            return "seed-" + crop.ToString().ToLowerInvariant();
        }

        public static string MaterialDisplayNameFor(CropKind crop, string varietyId)
        {
            return $"Seed {crop.ToString().ToLowerInvariant()} ({CropVarietyCatalog.DisplayNameOf(varietyId)})";
        }

        /// <summary>
        /// Registers seed as importable materials (EQU-1 extension path, same
        /// pattern as BakeryFlourSupply.EnsureBakeryImportables). One material
        /// per crop kind; the variety rides on the lot, not the material.
        /// </summary>
        public static void EnsureSeedImportables(List<string> diagnostics)
        {
            Register("seed-wheat", CropKind.Wheat, "red-fife", 160, 12, 55, diagnostics);
            Register("seed-corn", CropKind.Corn, "yellow-dent", 160, 12, 40, diagnostics);
            Register("seed-oats", CropKind.Oats, "scotch-oats", 160, 12, 35, diagnostics);
            Register("seed-hay", CropKind.Hay, "timothy", 160, 12, 25, diagnostics);
        }

        private static void Register(string materialId, CropKind crop, string varietyId,
            int distanceMiles, int transitDays, int pricePerUnitCents, List<string> diagnostics)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                materialId,
                MaterialDisplayNameFor(crop, varietyId),
                DefaultSeedHouseOrigin,
                distanceMiles,
                transitDays,
                pricePerUnitCents));
            if (problem != null && diagnostics != null)
            {
                diagnostics.Add($"SeedMerchantSupply: seed import registration: {problem}");
            }
        }
    }

    /// <summary>
    /// W5C: the seed merchant runtime — a real supplier of seed with lot-level
    /// stock. Implements <see cref="ISeedSupplier"/> (T1E compat) and adds
    /// lot-level sale so the full provenance chain travels to the buying farm.
    ///
    /// Stock arrives only two honest ways: import deliveries (EQU-1, named seed
    /// house) or local seed-grower farms selling held-back lots. The merchant
    /// never conjures stock; short sales are refused loudly.
    /// </summary>
    [Serializable]
    public sealed class SeedMerchant : ISeedSupplier
    {
        public string SupplierBusinessId { get; private set; } = string.Empty;
        public string SupplierName { get; private set; } = string.Empty;

        private readonly List<SeedLot> lots = new List<SeedLot>();
        private readonly Dictionary<CropKind, int> prices = new Dictionary<CropKind, int>();

        public SeedMerchant() { }

        public SeedMerchant(string businessId, string businessName)
        {
            SupplierBusinessId = businessId ?? string.Empty;
            SupplierName = businessName ?? string.Empty;
        }

        public IReadOnlyList<SeedLot> Lots => lots;

        public void SetPrice(CropKind crop, int priceCentsPerUnit)
        {
            prices[crop] = Math.Max(0, priceCentsPerUnit);
        }

        public int PricePerUnitCents(CropKind crop) => prices.TryGetValue(crop, out int p) ? p : 0;

        public int SeedStockUnits(CropKind crop)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (lot != null && lot.Crop == crop) total += lot.Units;
            }
            return total;
        }

        public int SeedStockUnits(CropKind crop, string varietyId)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (lot != null && lot.Crop == crop && MatchesVariety(lot, varietyId)) total += lot.Units;
            }
            return total;
        }

        private static bool MatchesVariety(SeedLot lot, string varietyId)
        {
            if (string.IsNullOrWhiteSpace(varietyId)) return true; // any variety
            return string.Equals(lot.VarietyId, varietyId, StringComparison.OrdinalIgnoreCase);
        }

        private List<SeedLot> OrderedLots(CropKind crop, string varietyId)
        {
            var ordered = new List<SeedLot>();
            foreach (var lot in lots)
            {
                if (lot != null && lot.Crop == crop && lot.Units > 0 && MatchesVariety(lot, varietyId))
                {
                    ordered.Add(lot);
                }
            }
            // FIFO: oldest acquisition first, then lot id for stability.
            ordered.Sort((a, b) =>
            {
                int c = a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex);
                return c != 0 ? c : string.Compare(a.LotId.ToString(), b.LotId.ToString(), StringComparison.Ordinal);
            });
            return ordered;
        }

        /// <summary>
        /// Restocks from an EQU-1 import delivery. The named seed house enters
        /// the provenance chain as an off-map merchant hop (EQU-1: off-map
        /// counterparties stay coarse but named); the local merchant hop is
        /// appended at sale. Returns the created lot, or null with a diagnostic.
        /// </summary>
        public SeedLot RestockFromImport(
            EntityIdRegistry idRegistry,
            ImportLot importLot,
            CropKind crop,
            string varietyId,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null || importLot == null)
            {
                diagnostics.Add("SeedMerchant: restock needs a registry and an import lot.");
                return null;
            }
            if (importLot.Units <= 0)
            {
                diagnostics.Add("SeedMerchant: import lot carries no units.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(varietyId))
            {
                diagnostics.Add("SeedMerchant: imported seed must name its variety (or 'unknown-variety').");
                return null;
            }

            var lot = new SeedLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                Crop = crop,
                VarietyId = varietyId,
                Units = importLot.Units,
                AcquiredDayIndex = dayIndex,
            };
            lot.Provenance.AppendHop(
                SeedProvenanceRoles.Merchant,
                $"{importLot.OriginName} (import {importLot.OrderId})",
                string.Empty,
                importLot.ArrivalDayIndex);
            lot.SourceDescription = lot.RenderSource();
            lots.Add(lot);
            diagnostics.Add($"SeedMerchant: {SupplierName} stocked {lot.Units}u {crop} seed ({CropVarietyCatalog.DisplayNameOf(varietyId)}) from {importLot.OriginName}.");
            return lot;
        }

        /// <summary>
        /// Restocks from a local seed-grower farm's held-back lot. The grower's
        /// provenance (grower hop, ideally breeder → grower) is preserved, not
        /// rewritten. Returns the merchant lot, or null with a diagnostic.
        /// </summary>
        public SeedLot RestockFromGrower(
            EntityIdRegistry idRegistry,
            SeedLot growerLot,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null || growerLot == null)
            {
                diagnostics.Add("SeedMerchant: restock needs a registry and a grower lot.");
                return null;
            }
            if (growerLot.Units <= 0)
            {
                diagnostics.Add("SeedMerchant: grower lot carries no units.");
                return null;
            }
            if ((growerLot.Provenance == null || !growerLot.Provenance.HasHops)
                && string.IsNullOrWhiteSpace(growerLot.SourceDescription))
            {
                diagnostics.Add("SeedMerchant: grower lot names no provenance — no orphan inputs, even from growers.");
                return null;
            }

            var lot = new SeedLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                Crop = growerLot.Crop,
                VarietyId = string.IsNullOrWhiteSpace(growerLot.VarietyId)
                    ? CropVarietyCatalog.UnknownVarietyId
                    : growerLot.VarietyId,
                Units = growerLot.Units,
                AcquiredDayIndex = dayIndex,
                Provenance = growerLot.Provenance != null ? growerLot.Provenance.Copy() : new SeedProvenanceChain(),
            };
            lot.SourceDescription = lot.RenderSource();
            lots.Add(lot);
            diagnostics.Add($"SeedMerchant: {SupplierName} stocked {lot.Units}u {lot.Crop} seed from grower lot {growerLot.LotId}.");
            return lot;
        }

        /// <summary>
        /// T1E legacy restock path (no lot provenance supplied): records variety
        /// as unrecorded rather than inventing one.
        /// </summary>
        public string RestockSeed(CropKind crop, int units, string upstreamSource, int dayIndex)
        {
            if (units <= 0) return "SeedMerchant: no units to stock.";
            if (string.IsNullOrWhiteSpace(upstreamSource))
            {
                return "SeedMerchant: seed must name its upstream source (seed grower / seed house) — no orphan inputs.";
            }
            var lot = new SeedLot
            {
                Crop = crop,
                VarietyId = CropVarietyCatalog.UnknownVarietyId,
                Units = units,
                AcquiredDayIndex = dayIndex,
            };
            lot.Provenance.AppendHop(SeedProvenanceRoles.Grower, upstreamSource, string.Empty, dayIndex);
            lot.SourceDescription = lot.RenderSource();
            lots.Add(lot);
            return null;
        }

        /// <summary>
        /// Sells seed lots to a farm with the full provenance chain. The
        /// merchant hop is appended at sale, so the chain reads
        /// breeder/grower → merchant → farm. FIFO by acquisition day.
        /// Returns the sold lots (new lot ids, farm-bound), or null with a
        /// LOUD diagnostic on shortfall — nothing is sold, nothing conjured.
        /// </summary>
        public List<SeedLot> SellSeedLots(
            CropKind crop,
            string varietyId,
            int requestedUnits,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null)
            {
                diagnostics.Add("SeedMerchant: sale needs an id registry — lots are never re-used across owners.");
                return null;
            }
            if (requestedUnits <= 0)
            {
                diagnostics.Add("SeedMerchant: no units requested.");
                return null;
            }

            int available = SeedStockUnits(crop, varietyId);
            string varietyNote = string.IsNullOrWhiteSpace(varietyId)
                ? string.Empty
                : $" of variety '{CropVarietyCatalog.DisplayNameOf(varietyId)}'";
            if (available < requestedUnits)
            {
                diagnostics.Add(
                    $"SeedMerchant: {SupplierName} holds {available}u {crop} seed{varietyNote}, " +
                    $"cannot sell {requestedUnits}u — finite supply, no seed conjured.");
                return null;
            }

            var sold = new List<SeedLot>();
            int remaining = requestedUnits;
            foreach (var lot in OrderedLots(crop, varietyId))
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, lot.Units);
                lot.Units -= take;
                remaining -= take;

                var farmLot = new SeedLot
                {
                    LotId = idRegistry.Allocate(EntityKind.Lot),
                    Crop = lot.Crop,
                    VarietyId = lot.VarietyId,
                    Units = take,
                    AcquiredDayIndex = dayIndex,
                    Provenance = lot.Provenance != null ? lot.Provenance.Copy() : new SeedProvenanceChain(),
                };
                farmLot.Provenance.AppendHop(SeedProvenanceRoles.Merchant, SupplierName, SupplierBusinessId, dayIndex);
                farmLot.SourceDescription = farmLot.RenderSource();
                sold.Add(farmLot);
            }

            DropEmptiedLots();
            diagnostics.Add(
                $"SeedMerchant: {SupplierName} sold {requestedUnits}u {crop} seed{varietyNote} " +
                $"across {sold.Count} lot(s).");
            return sold;
        }

        /// <summary>
        /// T1E unit-count sale path (ISeedSupplier contract): returns units
        /// actually sold, or -1 with a diagnostic on refusal. Farms wanting
        /// provenance should use <see cref="SellSeedLots"/>.
        /// </summary>
        public int SellSeed(CropKind crop, int requestedUnits, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (requestedUnits <= 0)
            {
                diagnostics.Add("SeedMerchant: no units requested.");
                return -1;
            }

            var ordered = OrderedLots(crop, null);
            int sold = 0;
            foreach (var lot in ordered)
            {
                if (sold >= requestedUnits) break;
                int take = Math.Min(requestedUnits - sold, lot.Units);
                lot.Units -= take;
                sold += take;
            }
            DropEmptiedLots();

            if (sold <= 0)
            {
                diagnostics.Add($"SeedMerchant: {SupplierName} has no {crop} seed in stock — finite supply (Canon §9.2).");
                return -1;
            }
            return sold;
        }

        private void DropEmptiedLots()
        {
            for (int i = lots.Count - 1; i >= 0; i--)
            {
                if (lots[i] == null || lots[i].Units <= 0) lots.RemoveAt(i);
            }
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public SeedMerchantSaveDto CaptureSaveDto()
        {
            return new SeedMerchantSaveDto
            {
                businessId = SupplierBusinessId,
                businessName = SupplierName,
                prices = new Dictionary<CropKind, int>(prices),
                lots = new List<SeedLot>(lots),
            };
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(SeedMerchantSaveDto dto)
        {
            lots.Clear();
            prices.Clear();
            if (dto == null) return;
            SupplierBusinessId = dto.businessId ?? string.Empty;
            SupplierName = dto.businessName ?? string.Empty;
            if (dto.prices != null)
            {
                foreach (var kv in dto.prices) prices[kv.Key] = kv.Value;
            }
            if (dto.lots != null)
            {
                foreach (var lot in dto.lots)
                {
                    if (lot != null) lots.Add(lot);
                }
            }
        }
    }

    /// <summary>Save DTO for the seed merchant (CLN-1 pattern).</summary>
    [Serializable]
    public sealed class SeedMerchantSaveDto
    {
        public string businessId = string.Empty;
        public string businessName = string.Empty;
        public Dictionary<CropKind, int> prices = new Dictionary<CropKind, int>();
        public List<SeedLot> lots = new List<SeedLot>();
    }
}
