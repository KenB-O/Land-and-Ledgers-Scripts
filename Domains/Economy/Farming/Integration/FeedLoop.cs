using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Integration
{
    /// <summary>
    /// FVS-4: a real supplier of feed. Finite stock — a supplier cannot sell
    /// what it does not have. The general store stocks feed as a trade good
    /// ("the store can buy/sell whatever"); the store's own stock must name its
    /// source (grain mill / crop farm) — suppliers need sources too (FVS
    /// upstream-provenance doctrine).
    /// </summary>
    public interface IFeedSupplier
    {
        string SupplierBusinessId { get; }
        string SupplierName { get; }
        int FeedStockUnits { get; }
        int PricePerUnitCents { get; }
        /// <summary>Returns units actually sold (finite stock), or -1 with a diagnostic on refusal.</summary>
        int SellFeed(int requestedUnits, int dayIndex, List<string> diagnostics);
        /// <summary>Restocks from a named upstream source (grain mill, crop farm).</summary>
        string RestockFeed(int units, string upstreamSource, int dayIndex);
    }

    /// <summary>
    /// FVS-4: the general store as feed supplier. Canon-grounded choice: the
    /// store is the rural supply hub (Tech X §3.1 produce purchasing shows the
    /// pattern) and Kennedy's standing doctrine is the store "can buy/sell
    /// whatever". Feed is a stocked trade good with a named upstream source.
    /// </summary>
    [Serializable]
    public sealed class GeneralStoreFeedSupplier : IFeedSupplier
    {
        public string SupplierBusinessId { get; private set; }
        public string SupplierName { get; private set; }
        public int FeedStockUnits { get; private set; }
        public int PricePerUnitCents { get; private set; }
        public string UpstreamSource { get; private set; } = string.Empty;

        public GeneralStoreFeedSupplier() { }

        public GeneralStoreFeedSupplier(string storeBusinessId, string storeName, int pricePerUnitCents)
        {
            SupplierBusinessId = storeBusinessId ?? string.Empty;
            SupplierName = storeName ?? string.Empty;
            PricePerUnitCents = Math.Max(0, pricePerUnitCents);
        }

        public string RestockFeed(int units, string upstreamSource, int dayIndex)
        {
            if (units <= 0) return "GeneralStoreFeedSupplier: no units to stock.";
            if (string.IsNullOrWhiteSpace(upstreamSource))
            {
                return "GeneralStoreFeedSupplier: feed must name its upstream source (grain mill / crop farm) — no orphan inputs.";
            }
            FeedStockUnits += units;
            UpstreamSource = upstreamSource;
            return null;
        }

        public int SellFeed(int requestedUnits, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (requestedUnits <= 0)
            {
                diagnostics.Add("GeneralStoreFeedSupplier: no units requested.");
                return -1;
            }
            int sold = Math.Min(requestedUnits, FeedStockUnits);
            if (sold <= 0)
            {
                diagnostics.Add($"GeneralStoreFeedSupplier: {SupplierName} has no feed in stock — finite supply (Canon §9.2).");
                return -1;
            }
            FeedStockUnits -= sold;
            if (sold < requestedUnits)
            {
                diagnostics.Add($"GeneralStoreFeedSupplier: only {sold}/{requestedUnits} units available — partial fill.");
            }
            return sold;
        }
    }

    /// <summary>
    /// FVS-4: feed purchasing. Money moves with provenance (Canon XIII §13.2);
    /// feed arrives as farm stock, never as a stat boost.
    /// </summary>
    public static class FeedMarket
    {
        /// <summary>
        /// Buys feed from a real supplier into the farm's feed stock. Returns
        /// units acquired, or -1 with diagnostics.
        /// </summary>
        public static int BuyFeed(
            FeedLoop farmFeed,
            IFeedSupplier supplier,
            int requestedUnits,
            int dayIndex,
            HouseholdLedger buyerLedger,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (farmFeed == null || supplier == null)
            {
                diagnostics.Add("FeedMarket: farm feed loop or supplier missing.");
                return -1;
            }
            if (buyerLedger == null)
            {
                diagnostics.Add("FeedMarket: no buyer ledger — feed purchases require provenance (Canon 13.2).");
                return -1;
            }

            int sold = supplier.SellFeed(requestedUnits, dayIndex, diagnostics);
            if (sold < 0) return -1;

            int costCents = sold * supplier.PricePerUnitCents;
            if (costCents > 0)
            {
                buyerLedger.RecordOutflow(dayIndex, costCents,
                    $"Feed purchase: {sold} units from {supplier.SupplierName} (via {supplier.GetType().Name})",
                    supplier.SupplierName);
            }
            farmFeed.AddFeed(sold, $"purchased from {supplier.SupplierName} (day {dayIndex})");
            diagnostics.Add($"FeedMarket: bought {sold} feed units from {supplier.SupplierName} for {costCents}c.");
            return sold;
        }
    }

    /// <summary>
    /// FVS-4: the farm's feed loop. Animals eat every day; feed comes from the
    /// farm's own hay field (harvest task), purchased feed (FeedMarket), or the
    /// bootstrap endowment (StartingFeedUnits — a one-time authored stock, never
    /// ongoing synthesis). Underfed animals lose condition: milk yields drop
    /// (DairyCowState.Condition01), hens stop laying. Feed is never free.
    /// </summary>
    [Serializable]
    public sealed class FeedLoop
    {
        /// <summary>Calibration: daily feed units per head.</summary>
        public const float DailyFeedPerCow = 1f;
        public const float DailyFeedPerChicken = 0.05f;
        public const string HarvestHayTaskId = "harvest-hay";

        public int FeedStockUnits { get; private set; }
        public string LastFeedSource = string.Empty; // provenance of the current stock

        public FeedLoop() { }

        public FeedLoop(int startingFeedUnits)
        {
            // FVS-1 bootstrap endowment: explicit one-time scenario-start stock.
            FeedStockUnits = Math.Max(0, startingFeedUnits);
            LastFeedSource = "bootstrap endowment (one-time; ongoing feed must be harvested or purchased)";
        }

        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;
            var harvest = new TaskDefinition(HarvestHayTaskId, "Harvest hay", 120);
            harvest.SetRequiredSkill(SkillIds.CropTending, new[] { "hay-harvest" });
            authority.RegisterDefinition(harvest, out _);
        }

        public void AddFeed(int units, string source)
        {
            if (units <= 0) return;
            FeedStockUnits += units;
            LastFeedSource = source ?? string.Empty;
        }

        /// <summary>
        /// One day of feeding. Returns shortfall units (0 = fully fed).
        /// Grazing covers part of cattle need in non-winter seasons
        /// (FarmSeasons.PastureGrazingShare); the rest must come from stock.
        /// </summary>
        public int ConsumeDay(
            int cattleHead,
            int chickenHead,
            FarmSeason season,
            DairyChain dairy,
            IEnumerable<EntityId> cowIds,
            PoultryChain flock,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();

            float grazingShare = FarmSeasons.PastureGrazingShare(season);
            float cowNeed = cattleHead * DailyFeedPerCow * (1f - grazingShare)
                * FarmSeasons.FeedMultiplierFor(season);
            float chickenNeed = chickenHead * DailyFeedPerChicken
                * FarmSeasons.FeedMultiplierFor(season);
            int needUnits = Mathf.CeilToInt(cowNeed + chickenNeed);

            int fedUnits = Math.Min(needUnits, FeedStockUnits);
            FeedStockUnits -= fedUnits;
            int shortfall = needUnits - fedUnits;

            if (shortfall > 0)
            {
                diagnostics.Add($"FeedLoop: shortfall of {shortfall} feed units on {season} day — animals underfed (need {needUnits}, had {fedUnits}).");
                // Underfed cows lose condition → milk yields drop (Tech X §8.1 nutrition).
                if (dairy != null && cowIds != null)
                {
                    foreach (var cowId in cowIds)
                    {
                        DairyCowState state = dairy.GetCowState(cowId);
                        if (state != null)
                        {
                            state.Condition01 = Mathf.Clamp01(state.Condition01 - 0.1f);
                        }
                    }
                }
            }
            else if (needUnits > 0)
            {
                // Full feeding slowly restores condition.
                if (dairy != null && cowIds != null)
                {
                    foreach (var cowId in cowIds)
                    {
                        DairyCowState state = dairy.GetCowState(cowId);
                        if (state != null && state.Condition01 < 1f)
                        {
                            state.Condition01 = Mathf.Clamp01(state.Condition01 + 0.02f);
                        }
                    }
                }
            }

            return shortfall;
        }
    }
}
