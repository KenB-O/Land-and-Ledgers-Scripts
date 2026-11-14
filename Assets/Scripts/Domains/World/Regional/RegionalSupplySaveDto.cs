using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.World.Regional
{
    /// <summary>
    /// Slice 17 (2026-11-05): the save shape for the regional supply chain.
    /// Pure data, no Unity dependency, so the round trip is harness-testable.
    /// One DTO covers both halves of the gap the manager names: the
    /// suppliers' per-item on-hand stock (plus each adapter's production
    /// week, so a reload neither double-produces nor skips a week) and the
    /// restock service's open orders, in-transit deliveries, and sequences.
    /// The store's own stock and cash live in GeneralStoreSaveDto already;
    /// this is the upstream half only. Restoring is clear-then-load with
    /// validation: unknown suppliers/items and negative units are skipped
    /// or clamped with a diagnostic, never trusted blindly. A missing or
    /// null DTO restores to the registry's starting position (fresh
    /// suppliers, no open orders) — an old save simply predates the region.
    /// R39: the DTO also carries one cash row per supplier, so each
    /// supplier's live cash ledger survives save/load on this same surface;
    /// a DTO with no cash rows restores every ledger to its authored
    /// opening cash.
    /// </summary>
    [Serializable]
    public sealed class RegionalSupplierStockRow
    {
        public string SupplierId = string.Empty;
        public string ItemId = string.Empty;
        public int Units;
        public int LastProductionWeek = -1;
    }

    /// <summary>R39: one supplier's live cash ledger balance in a save.</summary>
    [Serializable]
    public sealed class RegionalSupplierCashRow
    {
        public string SupplierId = string.Empty;
        public int CashCents;
    }

    [Serializable]
    public sealed class RegionalSupplySaveDto
    {
        public List<RegionalSupplierStockRow> SupplierStocks = new List<RegionalSupplierStockRow>();
        public List<RegionalSupplierCashRow> SupplierCash = new List<RegionalSupplierCashRow>();
        public List<StoreProcurementOrder> Orders = new List<StoreProcurementOrder>();
        public List<StoreDelivery> Deliveries = new List<StoreDelivery>();
        public int NextOrderSequence;
        public int NextDeliverySequence;
        public int NextShipmentGroupSequence;
    }
}
