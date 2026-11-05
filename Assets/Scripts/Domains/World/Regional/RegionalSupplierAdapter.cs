using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.World.Regional
{
    /// <summary>
    /// Slice 14 (2026-11-05): regional businesses as
    /// <see cref="IUpstreamGoodsSupplier"/> implementations, so the Slice 11
    /// <see cref="StoreRestockDriver"/> (and through it the store's daily
    /// restock review) can buy real goods from Ashford and Redfield.
    ///
    /// The procurement chain speaks CATEGORIES; the registry speaks ITEMS.
    /// The bridge rules, stated plainly:
    /// <list type="bullet">
    /// <item>Quote for a category = the cheapest wholesale price among the
    /// business's offered items in that category that still have stock.
    /// The chain picks the cheapest supplier per category, so the quote is
    /// the business's best price for the category, never an average that
    /// hides the cheap goods.</item>
    /// <item>Available units = the sum on hand across the business's offered
    /// items in the category.</item>
    /// <item>Ship debits real per-item stock, cheapest item first, and names
    /// the per-item breakdown in the diagnostics. It never ships more than
    /// is on hand and never takes stock below zero. The store's category
    /// stock is undifferentiated, so the item mix does not corrupt the
    /// store side — but the supplier side stays item-exact.</item>
    /// <item>Lead time is distance, not a fixture: one-way miles at wagon
    /// speed (4 mph, 96 miles/day), rounded up. Ashford 50 mi = 1 day,
    /// Redfield 100 mi = 2 days. This matches the Millbrook horse-feed
    /// fixture (50 miles, 1 transit day).</item>
    /// <item>Weekly production adds the registry's WeeklyMinimumUnits per
    /// item, capped at the target reserve. The weekly MAXIMUM is the
    /// capacity the owner could authorize; the deterministic model
    /// guarantees only the minimum. A supplier that sells out stays out
    /// until its next weekly production — the buyer then chooses the other
    /// town or waits, exactly as the brief requires.</item>
    /// </list>
    /// Item-to-category mapping is copied from
    /// GeneralStoreBusinessDefinition.asset (the authority); it is a
    /// code-side table because the procurement chain has no asset access
    /// in the harness.
    /// </summary>
    public static class RegionalItemCategories
    {
        private static readonly Dictionary<string, string> ItemToCategory =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "flour", "staple_food" }, { "bread", "staple_food" },
                { "potatoes", "staple_food" }, { "salt", "staple_food" },
                { "sugar", "staple_food" }, { "coffee", "staple_food" },
                { "tea", "staple_food" },
                { "beef", "meat" }, { "bacon", "meat" }, { "cheese", "meat" },
                { "basic_cloth", "clothing" }, { "flannel", "clothing" },
                { "workwear", "clothing" }, { "boots", "clothing" },
                { "blanket", "clothing" }, { "hat", "clothing" },
                { "soap", "household_goods" }, { "candles", "household_goods" },
                { "lamp_oil", "household_goods" }, { "lantern", "household_goods" },
                { "thread", "household_goods" }, { "buttons", "household_goods" },
                { "nails_hardware_lot", "tools_hardware" },
                { "hand_tool", "tools_hardware" }, { "farm_tool", "tools_hardware" },
                { "patent_remedy", "medicine_remedies" },
                { "medicine_bottle", "medicine_remedies" },
            };

        public static string CategoryForItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return string.Empty;
            return ItemToCategory.TryGetValue(itemId, out string category) ? category : string.Empty;
        }
    }

    /// <summary>One regional business as a live upstream supplier.</summary>
    public sealed class RegionalSupplierAdapter : IUpstreamGoodsSupplier
    {
        /// <summary>Wagon miles per day: 4 mph x 24 h. Lead time is one-way miles / 96, rounded up.</summary>
        public const float WagonMilesPerDay = 96f;

        private readonly RegionalBusinessRegistry.RegionalBusinessDefinition definition;
        private readonly Dictionary<string, int> onHand =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int lastProductionWeek = -1;

        public RegionalSupplierAdapter(RegionalBusinessRegistry.RegionalBusinessDefinition definition)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            for (int i = 0; i < definition.Offers.Length; i++)
            {
                RegionalBusinessRegistry.RegionalOffer offer = definition.Offers[i];
                if (offer != null && !string.IsNullOrWhiteSpace(offer.ItemId))
                    onHand[offer.ItemId] = Math.Max(0, offer.StartingUnits);
            }
        }

        public string SupplierId => definition.BusinessId;
        public string SupplierName => definition.DisplayName;
        public string TownId => definition.TownId;

        /// <summary>Production week the adapter has processed through (dayIndex / 7). -1 before the first drive.</summary>
        public int LastProductionWeek => lastProductionWeek;

        /// <summary>Units of one item on hand right now.</summary>
        public int OnHandUnits(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return 0;
            return onHand.TryGetValue(itemId, out int units) ? units : 0;
        }

        /// <summary>
        /// Slice 17: appends this supplier's stock rows (one per offered
        /// item, plus the production week) to a save DTO. Call once per
        /// supplier per capture.
        /// </summary>
        public void CaptureInto(RegionalSupplySaveDto dto)
        {
            if (dto == null) return;
            for (int i = 0; i < definition.Offers.Length; i++)
            {
                RegionalBusinessRegistry.RegionalOffer offer = definition.Offers[i];
                if (offer == null || string.IsNullOrWhiteSpace(offer.ItemId)) continue;
                dto.SupplierStocks.Add(new RegionalSupplierStockRow
                {
                    SupplierId = definition.BusinessId,
                    ItemId = offer.ItemId,
                    Units = OnHandUnits(offer.ItemId),
                    LastProductionWeek = lastProductionWeek,
                });
            }
        }

        /// <summary>
        /// Slice 17: clear-then-load for this supplier's stock. Every
        /// offered item first resets to its registry starting units; rows
        /// for this supplier then overwrite. Rows for other suppliers are
        /// ignored; rows for items this supplier does not offer are skipped
        /// with a diagnostic; negative units are clamped to zero with a
        /// diagnostic. A missing row therefore restores starting stock,
        /// never a stale value.
        /// </summary>
        public void RestoreFrom(RegionalSupplySaveDto dto, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            for (int i = 0; i < definition.Offers.Length; i++)
            {
                RegionalBusinessRegistry.RegionalOffer offer = definition.Offers[i];
                if (offer != null && !string.IsNullOrWhiteSpace(offer.ItemId))
                    onHand[offer.ItemId] = Math.Max(0, offer.StartingUnits);
            }
            lastProductionWeek = -1;
            if (dto == null || dto.SupplierStocks == null) return;
            int restoredWeek = -1;
            for (int i = 0; i < dto.SupplierStocks.Count; i++)
            {
                RegionalSupplierStockRow row = dto.SupplierStocks[i];
                if (row == null) continue;
                if (!string.Equals(row.SupplierId, definition.BusinessId, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(row.ItemId) || !onHand.ContainsKey(row.ItemId))
                {
                    diagnostics.Add($"RegionalSupplier '{SupplierName}': save row for unknown item '{row.ItemId}' skipped.");
                    continue;
                }
                if (row.Units < 0)
                {
                    diagnostics.Add($"RegionalSupplier '{SupplierName}': save row for '{row.ItemId}' had negative units ({row.Units}); clamped to 0.");
                    onHand[row.ItemId] = 0;
                }
                else
                {
                    onHand[row.ItemId] = row.Units;
                }
                if (row.LastProductionWeek >= 0)
                    restoredWeek = Math.Max(restoredWeek, row.LastProductionWeek);
            }
            if (restoredWeek >= 0) lastProductionWeek = restoredWeek;
        }

        public int QuoteUnitCostCents(string categoryId)
        {
            int best = 0;
            for (int i = 0; i < definition.Offers.Length; i++)
            {
                RegionalBusinessRegistry.RegionalOffer offer = definition.Offers[i];
                if (offer == null) continue;
                if (!string.Equals(RegionalItemCategories.CategoryForItem(offer.ItemId), categoryId,
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (OnHandUnits(offer.ItemId) <= 0) continue;
                if (best == 0 || offer.WholesalePriceCents < best)
                    best = Math.Max(0, offer.WholesalePriceCents);
            }
            return best;
        }

        public int AvailableUnits(string categoryId)
        {
            int total = 0;
            for (int i = 0; i < definition.Offers.Length; i++)
            {
                RegionalBusinessRegistry.RegionalOffer offer = definition.Offers[i];
                if (offer == null) continue;
                if (!string.Equals(RegionalItemCategories.CategoryForItem(offer.ItemId), categoryId,
                    StringComparison.OrdinalIgnoreCase)) continue;
                total += OnHandUnits(offer.ItemId);
            }
            return total;
        }

        public int LeadTimeDays(string categoryId)
        {
            RegionalTownRegistry.RegionalTownDefinition town = TownForAdapter();
            if (town == null) return 0;
            float miles = RegionalTownRegistry.HomeToCentreMiles(town);
            return Math.Max(0, (int)Math.Ceiling(miles / WagonMilesPerDay));
        }

        public int Ship(string categoryId, int units, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(categoryId) || units <= 0) return 0;
            int remaining = units;
            int shipped = 0;
            var breakdown = new List<string>();

            // Cheapest first: the quote names the cheapest item, so the
            // cheapest stock is what actually leaves first.
            var offers = new List<RegionalBusinessRegistry.RegionalOffer>();
            for (int i = 0; i < definition.Offers.Length; i++)
            {
                RegionalBusinessRegistry.RegionalOffer offer = definition.Offers[i];
                if (offer != null && string.Equals(
                    RegionalItemCategories.CategoryForItem(offer.ItemId), categoryId,
                    StringComparison.OrdinalIgnoreCase))
                    offers.Add(offer);
            }
            offers.Sort((a, b) => a.WholesalePriceCents.CompareTo(b.WholesalePriceCents));

            for (int i = 0; i < offers.Count && remaining > 0; i++)
            {
                int have = OnHandUnits(offers[i].ItemId);
                if (have <= 0) continue;
                int take = Math.Min(have, remaining);
                onHand[offers[i].ItemId] = have - take;
                remaining -= take;
                shipped += take;
                breakdown.Add($"{take}x {offers[i].ItemId}");
            }

            if (shipped > 0)
            {
                diagnostics.Add($"RegionalSupplier '{SupplierName}' shipped {shipped}u {categoryId} " +
                    $"on day {Math.Max(0, dayIndex)} as {string.Join(", ", breakdown)}; " +
                    $"{AvailableUnits(categoryId)}u {categoryId} remain on hand.");
            }
            else
            {
                diagnostics.Add($"RegionalSupplier '{SupplierName}' had no {categoryId} to ship.");
            }
            return shipped;
        }

        /// <summary>
        /// Applies weekly production for every week boundary crossed since the
        /// last call (week = dayIndex / 7). Each week adds the registry's
        /// WeeklyMinimumUnits per item, capped at the target reserve. Call
        /// once per simulated week (or once per day — same-week calls are
        /// no-ops). Deterministic: no randomness in production.
        /// </summary>
        public void DriveWeeklyProduction(int dayIndex)
        {
            int week = Math.Max(0, dayIndex) / 7;
            if (lastProductionWeek < 0)
            {
                lastProductionWeek = week;
                return;
            }
            while (lastProductionWeek < week)
            {
                lastProductionWeek++;
                for (int i = 0; i < definition.Offers.Length; i++)
                {
                    RegionalBusinessRegistry.RegionalOffer offer = definition.Offers[i];
                    if (offer == null || string.IsNullOrWhiteSpace(offer.ItemId)) continue;
                    int have = OnHandUnits(offer.ItemId);
                    int produced = Math.Min(Math.Max(0, offer.WeeklyMinimumUnits),
                        Math.Max(0, offer.TargetReserveUnits - have));
                    if (produced > 0) onHand[offer.ItemId] = have + produced;
                }
            }
        }

        private RegionalTownRegistry.RegionalTownDefinition TownForAdapter()
        {
            for (int i = 0; i < RegionalTownRegistry.All.Length; i++)
                if (string.Equals(RegionalTownRegistry.All[i].TownId, definition.TownId,
                    StringComparison.OrdinalIgnoreCase))
                    return RegionalTownRegistry.All[i];
            return null;
        }
    }

    /// <summary>All twelve regional businesses as live suppliers.</summary>
    public static class RegionalSupplierSet
    {
        public static List<RegionalSupplierAdapter> CreateAll()
        {
            var adapters = new List<RegionalSupplierAdapter>();
            for (int i = 0; i < RegionalBusinessRegistry.All.Length; i++)
                adapters.Add(new RegionalSupplierAdapter(RegionalBusinessRegistry.All[i]));
            return adapters;
        }

        public static List<IUpstreamGoodsSupplier> AsSuppliers(List<RegionalSupplierAdapter> adapters)
        {
            var suppliers = new List<IUpstreamGoodsSupplier>();
            if (adapters == null) return suppliers;
            for (int i = 0; i < adapters.Count; i++)
                if (adapters[i] != null) suppliers.Add(adapters[i]);
            return suppliers;
        }

        public static void DriveWeeklyProduction(List<RegionalSupplierAdapter> adapters, int dayIndex)
        {
            if (adapters == null) return;
            for (int i = 0; i < adapters.Count; i++)
                adapters[i]?.DriveWeeklyProduction(dayIndex);
        }
    }
}
