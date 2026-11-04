using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    [Serializable]
    public sealed class OffMapAidInventoryRow
    {
        public string ItemId = string.Empty;
        public string DisplayName = string.Empty;
        public string InventoryUnit = "lb";
        [Min(0)] public int StartingUnits;
        public string OrderUnit = string.Empty;
        [Min(0)] public int InventoryUnitsPerOrderUnit;
        [Min(0)] public int BakeryUnitsPerOrderUnit;
        [Min(0)] public int OrderPriceCents;
    }

    [Serializable]
    public sealed class OffMapAidProductionRow
    {
        public string ItemId = string.Empty;
        [Min(0)] public int WeeklyMinimumUnits;
        [Min(0)] public int WeeklyMaximumUnits;
        [Min(0)] public int TargetReserveUnits;
    }

    /// <summary>
    /// Authored configuration for a game-run off-map aid supplier. This is not a
    /// town BusinessProfile: it has no owner, premises, workers, or autonomous AI.
    /// Mutable inventory, cash, orders, and freight lots live in the runtime authority.
    /// </summary>
    [CreateAssetMenu(menuName = "Land & Ledgers/Economy/Off-map Aid Business", fileName = "OffMapAidBusiness")]
    public sealed class OffMapAidBusinessDefinition : ScriptableObject
    {
        public string BusinessId = string.Empty;
        public string DisplayName = string.Empty;
        [Min(0f), Tooltip("Authored one-way road-equivalent mileage from the settlement to this supplier's pickup point. This is mileage data, not a map coordinate.")]
        public float PickupRouteMilesFromTown;
        public List<OffMapAidInventoryRow> Inventory = new List<OffMapAidInventoryRow>();
        public List<OffMapAidProductionRow> Production = new List<OffMapAidProductionRow>();

        public string ValidateDefinition()
        {
            if (string.IsNullOrWhiteSpace(BusinessId) || string.IsNullOrWhiteSpace(DisplayName)
                || PickupRouteMilesFromTown < 0f || float.IsNaN(PickupRouteMilesFromTown) || float.IsInfinity(PickupRouteMilesFromTown))
                return "Off-map aid business requires a stable id and display name.";
            var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OffMapAidInventoryRow row in Inventory ?? new List<OffMapAidInventoryRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.ItemId) || !itemIds.Add(row.ItemId)
                    || row.StartingUnits < 0 || row.InventoryUnitsPerOrderUnit < 0
                    || row.BakeryUnitsPerOrderUnit < 0 || row.OrderPriceCents < 0)
                    return "Off-map aid inventory rows need unique item ids and non-negative units and prices.";
                if (row.OrderPriceCents > 0 && (string.IsNullOrWhiteSpace(row.OrderUnit)
                    || row.InventoryUnitsPerOrderUnit <= 0))
                    return $"Orderable item '{row.ItemId}' needs an order unit and a positive inventory conversion.";
            }
            var productionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OffMapAidProductionRow row in Production ?? new List<OffMapAidProductionRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.ItemId) || !itemIds.Contains(row.ItemId)
                    || !productionIds.Add(row.ItemId) || row.WeeklyMinimumUnits < 0
                    || row.WeeklyMaximumUnits < row.WeeklyMinimumUnits || row.TargetReserveUnits < 0)
                    return "Production rows must uniquely reference inventory items and have valid replenishment bounds.";
            }
            return null;
        }
    }
}
