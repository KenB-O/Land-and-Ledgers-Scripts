// Slice 11 (2026-11-05): the daily production drive for the store restock
// chain. The chain itself (StoreRestockService: order from real suppliers,
// ship from real stock, pay, receive with provenance) already existed but
// had no production caller and no production suppliers. This driver is the
// plug-in point: it owns one service plus a supplier list, and DriveDaily
// runs the daily review + advance for the day. The manager wires it into
// OnDayChanged with the supplier list EMPTY for now, so the daily review
// places no orders and says so once per day, while the weekly off-map
// reorder keeps working unchanged. P3's regional businesses register
// themselves here; nothing about the drive changes when they do.
//
// The driver is deliberately boring: one drive per day (a second call the
// same day is a no-op), the reorder threshold is caller-supplied, and the
// diagnostics are the service's own lines plus the no-supplier notice.
// Open orders live in memory in the service; save/load for them is not
// built yet, and the weekly pending-reorder queue remains the durable
// route until it is.
using System;
using System.Collections.Generic;
using LandLedgers.World.Regional;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    public sealed class StoreRestockDriver
    {
        private readonly StoreRestockService restock = new StoreRestockService();
        private readonly List<IUpstreamGoodsSupplier> suppliers = new List<IUpstreamGoodsSupplier>();
        private readonly List<string> diagnostics = new List<string>();
        private int lastDrivenDayIndex = -1;

        public StoreRestockService Restock => restock;
        public IReadOnlyList<IUpstreamGoodsSupplier> Suppliers => suppliers;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string LastDailySummary { get; private set; } = "No daily restock review has run yet.";

        /// <summary>
        /// Slice 15: optional delivered-cost function (supplier, category,
        /// item, order units) -&gt; delivered cents per unit. The item is
        /// empty for a category order and names the item for an item-level
        /// order (Slice 18). Null (the default) keeps the historical
        /// goods-quote-only choice. The manager sets this from the
        /// regional freight quotes when regional suppliers are
        /// registered; the chain itself never invents a freight rate.
        /// </summary>
        public Func<IUpstreamGoodsSupplier, string, string, int, int> DeliveredCostPerUnitCents { get; set; }

        public void RegisterSupplier(IUpstreamGoodsSupplier supplier)
        {
            if (supplier == null || string.IsNullOrWhiteSpace(supplier.SupplierId)) return;
            for (int i = 0; i < suppliers.Count; i++)
                if (string.Equals(suppliers[i].SupplierId, supplier.SupplierId, StringComparison.OrdinalIgnoreCase)) return;
            suppliers.Add(supplier);
        }

        /// <summary>
        /// Slice 23: save/load seam for the driver. Slice 17's manager wiring
        /// (GeneralStoreRuntimeManager CaptureRegionalSupplyDto /
        /// RestoreRegionalSupply) calls these on the driver; they delegate to
        /// the owned restock service, which owns the orders, deliveries, and
        /// sequences. No driver-owned state is persisted: lastDrivenDayIndex
        /// is deliberately NOT saved — DriveDailyRestock runs only from
        /// OnDayChanged, and the manager (hence the driver) lives across
        /// LoadFromSaveDto in the same session, so no same-day double-drive
        /// path exists through a load.
        /// </summary>
        public void CaptureInto(RegionalSupplySaveDto dto) => restock.CaptureInto(dto);
        public void RestoreFrom(RegionalSupplySaveDto dto, List<string> diagnostics) => restock.RestoreFrom(dto, diagnostics);

        /// <summary>
        /// Runs the daily restock review and advance for one day. Returns
        /// false when there is no store to drive. A second call on the same
        /// day is a no-op and returns true.
        /// </summary>
        public bool DriveDaily(IGeneralStoreTradingPort store, int dayIndex, float reorderThreshold01)
        {
            if (store == null) return false;
            int day = Math.Max(0, dayIndex);
            if (day == lastDrivenDayIndex) return true;
            lastDrivenDayIndex = day;

            int diagBefore = restock.Diagnostics.Count;
            restock.EvaluateRestock(store, suppliers, day, reorderThreshold01, DeliveredCostPerUnitCents);
            restock.AdvanceDay(store, suppliers, day, diagnostics);

            var fresh = new List<string>();
            for (int i = diagBefore; i < restock.Diagnostics.Count; i++)
                fresh.Add(restock.Diagnostics[i]);
            foreach (string line in diagnostics)
                if (!fresh.Contains(line)) fresh.Add(line);
            diagnostics.Clear();

            if (suppliers.Count == 0)
            {
                LastDailySummary = $"Day {day}: no upstream suppliers registered — daily restock review placed no orders; " +
                    "the weekly off-map reorder remains the only supply.";
            }
            else if (fresh.Count == 0)
            {
                LastDailySummary = $"Day {day}: daily restock review ran; no new orders, shipments, or receipts.";
            }
            else
            {
                LastDailySummary = $"Day {day}: " + string.Join(" ", fresh);
            }

            return true;
        }
    }
}
