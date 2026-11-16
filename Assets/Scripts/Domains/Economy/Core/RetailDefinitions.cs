using System;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum RetailCategoryRole
    {
        Core = 0,
        Flex = 1
    }

    public enum RetailDemandGroup
    {
        DailyStaple = 0,
        FoodProtein = 1,
        Clothing = 2,
        Household = 3,
        Hardware = 4,
        Medicine = 5,

        // Agent F, slice F2 (expanded campaign 2026-11-16): furniture is
        // its own demand group, so furniture demand is never grouped with
        // household-goods demand. Append-only: existing values unchanged.
        // Both switches over this enum have default arms (store need
        // priority falls to 9, household reserve demand falls to
        // generalGoodsNeed), so no switch change is needed for the new
        // value; a durable good belongs at the lowest urgency in both.
        Furniture = 6
    }

    [Serializable]
    public struct PriceBand
    {
        [Min(0)]
        public int minCents;

        [Min(0)]
        public int maxCents;

        public bool HasBand => minCents > 0 || maxCents > 0;

        public int Clamp(int cents)
        {
            int min = Mathf.Max(0, minCents);
            int max = Mathf.Max(min, maxCents);
            return HasBand ? Mathf.Clamp(cents, min, max) : Mathf.Max(0, cents);
        }
    }

    [Serializable]
    public struct RetailPricingDefinition
    {
        private const float DefaultWholesaleCostShare = 0.7f;

        [Tooltip("Frontier baseline retail price, stored in cents for stable tuning.")]
        [Min(0)]
        public int baselineCents;

        [Tooltip("Store purchase cost before import/hauling. Leave 0 to derive a first-pass wholesale cost from baseline retail.")]
        [Min(0)]
        public int baseWholesaleCostCents;

        [Tooltip("First-pass import/hauling cost paid by the store per unit.")]
        [Min(0)]
        public int importCostCents;

        [Tooltip("Optional local market price floor/ceiling. Leave 0/0 if the item should float from baseline only.")]
        public PriceBand localMarketBand;

        [Tooltip("Store markup or markdown multiplier before future supply/demand pressure is applied.")]
        [Min(0f)]
        public float markupMultiplier;

        [Tooltip("Future hook for supply/demand systems. 0 means ignore pressure, 1 means full pressure.")]
        [Range(0f, 2f)]
        public float supplyDemandSensitivity;

        /// <summary>
        /// DEF-013 (Decision 9): supply/demand pressure from the category's
        /// live stock health (CategoryStockState.StockHealth01: 0 = empty,
        /// 1 = at or above target). Empty stock is the maximum scarcity
        /// premium (+1), half stock is neutral (0), full stock is the maximum
        /// surplus discount (-1). The per-item supplyDemandSensitivity scales
        /// it inside GetDisplayPriceCents / GetCostPlusPriceCents, and the
        /// item's localMarketBand clamps the result, so a sensitivity of 0
        /// means the price never moves. Out-of-range input clamps.
        /// </summary>
        public static float ResolveSupplyDemandPressure01(float stockHealth01)
        {
            return Mathf.Clamp(1f - 2f * stockHealth01, -1f, 1f);
        }

        /// <summary>
        /// DEF-013 follow-up (Decision 9): resolves the supply/demand
        /// pressure for one retail category from live runtime stock — the
        /// category's own stock health when it has a stock state, else the
        /// business-wide stock health (same source as the DEF-013
        /// transfer-price path). A null runtime means no signal and the
        /// price is unchanged (0f); a null or empty category falls back to
        /// the business-wide health.
        /// </summary>
        public static float ResolveCategorySupplyDemandPressure01(BusinessRuntimeState runtime, string categoryId)
        {
            if (runtime == null)
            {
                return 0f;
            }

            CategoryStockState stock = runtime.GetCategoryStock(categoryId);
            float stockHealth01 = stock != null ? stock.StockHealth01 : runtime.StockHealth01;
            return ResolveSupplyDemandPressure01(stockHealth01);
        }

        public int GetDisplayPriceCents(float categoryMarkupMultiplier = 1f, float supplyDemandPressure = 0f)
        {
            float markup = markupMultiplier <= 0f ? 1f : markupMultiplier;
            float demandAdjustment = 1f + supplyDemandPressure * Mathf.Max(0f, supplyDemandSensitivity);
            int pricedCents = Mathf.RoundToInt(Mathf.Max(0, baselineCents) * markup * Mathf.Max(0f, categoryMarkupMultiplier) * Mathf.Max(0f, demandAdjustment));
            return localMarketBand.Clamp(pricedCents);
        }

        public int GetLandedCostCents()
        {
            int wholesaleCost = baseWholesaleCostCents > 0
                ? baseWholesaleCostCents
                : Mathf.RoundToInt(Mathf.Max(0, baselineCents) * DefaultWholesaleCostShare);
            return Mathf.Max(0, wholesaleCost) + Mathf.Max(0, importCostCents);
        }

        public int GetCostPlusPriceCents(float categoryMarkupMultiplier = 1f, float supplyDemandPressure = 0f)
        {
            float markup = markupMultiplier <= 0f ? 1f : markupMultiplier;
            float demandAdjustment = 1f + supplyDemandPressure * Mathf.Max(0f, supplyDemandSensitivity);
            int pricedCents = Mathf.RoundToInt(GetLandedCostCents() * markup * Mathf.Max(0f, categoryMarkupMultiplier) * Mathf.Max(0f, demandAdjustment));
            return localMarketBand.Clamp(pricedCents);
        }

        public bool IsMarginCompressedByBand(float categoryMarkupMultiplier = 1f, float supplyDemandPressure = 0f)
        {
            if (!localMarketBand.HasBand)
            {
                return false;
            }

            float markup = markupMultiplier <= 0f ? 1f : markupMultiplier;
            float demandAdjustment = 1f + supplyDemandPressure * Mathf.Max(0f, supplyDemandSensitivity);
            int unclampedPrice = Mathf.RoundToInt(GetLandedCostCents() * markup * Mathf.Max(0f, categoryMarkupMultiplier) * Mathf.Max(0f, demandAdjustment));
            return localMarketBand.Clamp(unclampedPrice) < unclampedPrice;
        }
    }

    [Serializable]
    public sealed class ItemCategoryDefinition
    {
        [SerializeField]
        private string categoryId = "category";

        [SerializeField]
        private string displayName = "Category";

        [SerializeField]
        private RetailCategoryRole role = RetailCategoryRole.Core;

        [SerializeField]
        private RetailDemandGroup demandGroup;

        [SerializeField, Min(0)]
        private int sortOrder;

        [SerializeField, Min(0f)]
        private float defaultMarkupMultiplier = 1.15f;

        [SerializeField, Range(0f, 1f)]
        private float lowStockThreshold01 = 0.25f;

        [SerializeField]
        [TextArea(1, 3)]
        private string playerFacingSummary;

        [SerializeField]
        private string[] itemIds = Array.Empty<string>();

        public string CategoryId => string.IsNullOrWhiteSpace(categoryId) ? displayName : categoryId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? CategoryId : displayName;
        public RetailCategoryRole Role => role;
        public RetailDemandGroup DemandGroup => demandGroup;
        public int SortOrder => Mathf.Max(0, sortOrder);
        public float DefaultMarkupMultiplier => defaultMarkupMultiplier <= 0f ? 1f : defaultMarkupMultiplier;
        public float LowStockThreshold01 => Mathf.Clamp01(lowStockThreshold01);
        public string PlayerFacingSummary => playerFacingSummary ?? string.Empty;
        public ReadOnlySpan<string> ItemIds => itemIds ?? Array.Empty<string>();

        public bool ContainsItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || itemIds == null)
            {
                return false;
            }

            for (int i = 0; i < itemIds.Length; i++)
            {
                if (string.Equals(itemIds[i], itemId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class ItemDefinition
    {
        [SerializeField]
        private string itemId = "item";

        [SerializeField]
        private string displayName = "Item";

        [SerializeField]
        private string categoryId = "category";

        [SerializeField]
        private string unit = "each";

        [SerializeField]
        private RetailPricingDefinition pricing = new()
        {
            baselineCents = 100,
            markupMultiplier = 1f,
            supplyDemandSensitivity = 0.25f
        };

        [SerializeField, Min(0)]
        private int startingStockUnits;

        [SerializeField, Min(0)]
        private int reorderTargetUnits;

        [SerializeField, Min(1)]
        private int reorderLotSize = 1;

        [SerializeField]
        private bool coreStock = true;

        [SerializeField]
        private string[] producerTags = Array.Empty<string>();

        [SerializeField]
        private string[] consumerTags = Array.Empty<string>();

        public string ItemId => string.IsNullOrWhiteSpace(itemId) ? displayName : itemId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? ItemId : displayName;
        public string CategoryId => categoryId ?? string.Empty;
        public string Unit => string.IsNullOrWhiteSpace(unit) ? "each" : unit;
        public RetailPricingDefinition Pricing => pricing;
        public int StartingStockUnits => Mathf.Max(0, startingStockUnits);
        public int ReorderTargetUnits => Mathf.Max(0, reorderTargetUnits);
        public int ReorderLotSize => Mathf.Max(1, reorderLotSize);
        public bool CoreStock => coreStock;
        public ReadOnlySpan<string> ProducerTags => producerTags ?? Array.Empty<string>();
        public ReadOnlySpan<string> ConsumerTags => consumerTags ?? Array.Empty<string>();
    }
}
