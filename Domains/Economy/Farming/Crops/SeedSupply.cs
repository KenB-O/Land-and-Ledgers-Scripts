using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// T1E: a seed lot with provenance. Seed is never fungible-without-history:
    /// every lot names the harvest (or supplier) it came from, because planting
    /// records SeedSource on the field (CropChain.PlantField).
    /// </summary>
    [Serializable]
    public sealed class SeedLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public CropKind Crop;
        public int Units;
        public string SourceDescription = string.Empty; // e.g. "saved from farm-1 wheat harvest 210" or "bought: general store <- seed farm"
        public int AcquiredDayIndex;

        /// <summary>
        /// W5C: the variety as data (<see cref="CropVarietyCatalog"/> id).
        /// <see cref="CropVarietyCatalog.UnknownVarietyId"/> when unrecorded —
        /// honesty, not a guess.
        /// </summary>
        public string VarietyId = string.Empty;

        /// <summary>
        /// W5C: the structured upstream chain (breeder/grower → merchant → farm).
        /// <see cref="SourceDescription"/> remains the human summary; the chain is
        /// the auditable record.
        /// </summary>
        public SeedProvenanceChain Provenance = new SeedProvenanceChain();

        /// <summary>
        /// W5C: explicit one-time bootstrap endowment (scenario start). Flagged,
        /// never a supplier — the honest fallback when no real supplier exists yet.
        /// </summary>
        public bool IsBootstrapEndowment;

        public SeedLot() { }

        /// <summary>
        /// W5C: the source string a planting records — the structured chain when
        /// present, the legacy description otherwise.
        /// </summary>
        public string RenderSource()
        {
            if (Provenance != null && Provenance.HasHops)
            {
                string variety = CropVarietyCatalog.DisplayNameOf(VarietyId);
                return $"{variety} seed: {Provenance.Render()}";
            }
            return SourceDescription ?? string.Empty;
        }
    }

    /// <summary>
    /// T1E: a real supplier of seed. Finite stock — a supplier cannot sell what it
    /// does not have. The general store stocks seed as a trade good ("the store can
    /// buy/sell whatever"); the store's own stock must name its upstream source
    /// (seed grower / crop farm) — suppliers need sources too.
    /// </summary>
    public interface ISeedSupplier
    {
        string SupplierBusinessId { get; }
        string SupplierName { get; }
        int SeedStockUnits(CropKind crop);
        int PricePerUnitCents(CropKind crop);
        /// <summary>Returns units actually sold (finite stock), or -1 with a diagnostic on refusal.</summary>
        int SellSeed(CropKind crop, int requestedUnits, int dayIndex, List<string> diagnostics);
        /// <summary>Restocks from a named upstream source (seed grower, crop farm).</summary>
        string RestockSeed(CropKind crop, int units, string upstreamSource, int dayIndex);
    }

    /// <summary>
    /// T1E: the general store as seed supplier. Seed sacks are canon equipment;
    /// the store retails them per crop kind with a named upstream grower.
    /// </summary>
    [Serializable]
    public sealed class GeneralStoreSeedSupplier : ISeedSupplier
    {
        public string SupplierBusinessId { get; private set; } = string.Empty;
        public string SupplierName { get; private set; } = string.Empty;

        private readonly Dictionary<CropKind, int> stock = new Dictionary<CropKind, int>();
        private readonly Dictionary<CropKind, int> prices = new Dictionary<CropKind, int>();
        private readonly Dictionary<CropKind, string> upstreamByCrop = new Dictionary<CropKind, string>();

        public GeneralStoreSeedSupplier() { }

        public GeneralStoreSeedSupplier(string storeBusinessId, string storeName)
        {
            SupplierBusinessId = storeBusinessId ?? string.Empty;
            SupplierName = storeName ?? string.Empty;
        }

        public void SetPrice(CropKind crop, int priceCentsPerUnit)
        {
            prices[crop] = Math.Max(0, priceCentsPerUnit);
        }

        public int SeedStockUnits(CropKind crop) => stock.TryGetValue(crop, out int s) ? s : 0;
        public int PricePerUnitCents(CropKind crop) => prices.TryGetValue(crop, out int p) ? p : 0;
        public string UpstreamSource(CropKind crop) => upstreamByCrop.TryGetValue(crop, out string u) ? u : string.Empty;

        public string RestockSeed(CropKind crop, int units, string upstreamSource, int dayIndex)
        {
            if (units <= 0) return "GeneralStoreSeedSupplier: no units to stock.";
            if (string.IsNullOrWhiteSpace(upstreamSource))
            {
                return "GeneralStoreSeedSupplier: seed must name its upstream source (seed grower / crop farm) — no orphan inputs.";
            }

            if (!stock.ContainsKey(crop)) stock[crop] = 0;
            stock[crop] += units;
            upstreamByCrop[crop] = upstreamSource;
            return null;
        }

        public int SellSeed(CropKind crop, int requestedUnits, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (requestedUnits <= 0)
            {
                diagnostics.Add("GeneralStoreSeedSupplier: no units requested.");
                return -1;
            }

            int sold = Math.Min(requestedUnits, SeedStockUnits(crop));
            if (sold <= 0)
            {
                diagnostics.Add($"GeneralStoreSeedSupplier: {SupplierName} has no {crop} seed in stock — finite supply (Canon §9.2).");
                return -1;
            }

            stock[crop] -= sold;
            return sold;
        }
    }

    /// <summary>
    /// T1E: seed saving — the historical norm. A farm holds back part of its own
    /// harvest as next season's seed. This is INTERNAL USE (like CRP-3's FeedOwnGrain):
    /// no sale, no revenue, no phantom seed — the held-back grain simply never leaves
    /// the farm, and its provenance is the farm's own harvest.
    /// </summary>
    public static class SeedSaving
    {
        /// <summary>
        /// Holds back seedUnits from a harvest lot for future planting. Returns the
        /// SeedLot, or null with a diagnostic when the harvest cannot cover it.
        /// </summary>
        public static SeedLot HoldBackSeed(
            EntityIdRegistry idRegistry,
            CropLot harvestLot,
            int seedUnits,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null || harvestLot == null)
            {
                diagnostics.Add("SeedSaving: need a registry and a harvest lot.");
                return null;
            }

            if (seedUnits <= 0)
            {
                diagnostics.Add("SeedSaving: no seed units to hold back.");
                return null;
            }

            if (harvestLot.QuantityUnits < seedUnits)
            {
                diagnostics.Add($"SeedSaving: harvest lot {harvestLot.LotId} holds {harvestLot.QuantityUnits}u — cannot hold back {seedUnits}u.");
                return null;
            }

            if (harvestLot.ProductKind != "grain")
            {
                diagnostics.Add($"SeedSaving: lot {harvestLot.LotId} is {harvestLot.ProductKind}, not threshed grain — seed comes from clean grain.");
                return null;
            }

            harvestLot.QuantityUnits -= seedUnits;
            return new SeedLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                Crop = harvestLot.Crop,
                Units = seedUnits,
                SourceDescription = $"saved from {harvestLot.FarmId} {harvestLot.Crop} harvest (lot {harvestLot.LotId}, day {harvestLot.HarvestDayIndex})",
                AcquiredDayIndex = dayIndex,
            };
        }

        /// <summary>
        /// The seed-source string a planting records when sowing saved seed.
        /// </summary>
        public static string SavedSeedSourceDescription(SeedLot seedLot)
        {
            if (seedLot == null) return string.Empty;
            return $"saved seed, lot {seedLot.LotId}: {seedLot.SourceDescription}";
        }
    }
}
