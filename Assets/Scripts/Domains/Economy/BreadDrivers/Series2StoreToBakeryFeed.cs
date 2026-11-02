using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.BreadDrivers
{
    /// <summary>
    /// Series 2 Pass 6 (bread drivers): the flour-unit conversion, made
    /// explicit and testable. The audit found the composed run used
    /// 1 lb per store unit while the store profile describes a flour
    /// unit as a 10 lb sack. Both numbers cannot be true at once, and
    /// neither is canon: the conversion is a caller-supplied parameter.
    /// StoreUnitDescription names what the caller believes a store unit
    /// IS (for example "10 lb sack" or "1 lb unit"); LbPerStoreUnit is
    /// the number actually applied; ExpectedLbPerNamedUnit, when the
    /// caller supplies it, is the number the description implies. A
    /// mismatch is diagnosed loudly as a warning, not silently fixed
    /// and not refused: the applied number always wins, and the warning
    /// is the record that the description and the number disagreed.
    /// </summary>
    public sealed class Series2FlourUnitConversion
    {
        public int LbPerStoreUnit;
        public string StoreUnitDescription = string.Empty;
        public int? ExpectedLbPerNamedUnit;

        public string Validate(List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (LbPerStoreUnit <= 0)
                return "Series2FlourUnitConversion: lb per store unit must be positive; no feed runs on a zero conversion.";
            if (ExpectedLbPerNamedUnit.HasValue && ExpectedLbPerNamedUnit.Value != LbPerStoreUnit)
                diag.Add($"Series2FlourUnitConversion WARNING: store unit described as '{StoreUnitDescription}' implies {ExpectedLbPerNamedUnit.Value} lb, but the applied conversion is {LbPerStoreUnit} lb per store unit; applied number wins, description must be corrected by the caller.");
            else
                diag.Add($"Series2FlourUnitConversion: 1 store unit = {LbPerStoreUnit} lb ({StoreUnitDescription}).");
            return null;
        }

        public int FlourLbFor(int storeUnits) => Math.Max(0, storeUnits) * LbPerStoreUnit;
    }

    /// <summary>
    /// Series 2 Pass 6 (bread drivers): the production store-to-bakery
    /// feed over real runtimes, with the same rollback semantics as
    /// the ThreeBusinessSlice orchestrator bridge (which stays as a
    /// regression reference only and is NOT the system under test here):
    /// consume store lots, then bakery-cash check (consumption undone
    /// on failure), then store cash, then the bakery bin; on a bin
    /// refusal, consumption is undone and both cash movements are
    /// restored. Cash moves only through BusinessRuntimeState (the
    /// business cash writer); no debt is created here; no lot unit can
    /// feed both a household sale and production, because ConsumeLots
    /// removes the units from sellable stock first. The flour feed and
    /// the notions feed share FeedToBin; they differ only in the bin
    /// item id and the conversion applied. Diagnostics never print fake
    /// zeros: an unknown supplier is reported as "unknown supplier",
    /// never as a guessed name.
    /// </summary>
    public static class Series2StoreToBakeryFeed
    {
        /// <summary>
        /// Feeds flour from the store into the bakery bin. Returns null
        /// on success, or a refusal diagnostic; on any failure after
        /// consumption, nothing has moved (see FeedToBin).
        /// </summary>
        public static string FeedFlour(
            OffMapNamedSkuProcurementService storeService,
            BusinessRuntimeState storeRuntime,
            BakeryShopRuntime bakery, BusinessRuntimeState bakeryRuntime,
            EntityIdRegistry ids,
            string skuId, int storeUnits, Series2FlourUnitConversion conversion,
            int unitPriceCents, int dayIndex, List<string> diag)
        {
            if (conversion == null) return "Series2StoreToBakeryFeed: no flour-unit conversion supplied; feed refused rather than guessing lb per store unit.";
            string conversionProblem = conversion.Validate(diag);
            if (conversionProblem != null) return conversionProblem;
            return FeedToBin(
                storeService, storeRuntime, bakery, bakeryRuntime, ids,
                skuId, storeUnits, BakeryBreadCatalog.FlourItemId,
                conversion.FlourLbFor(storeUnits), "bakery-feed",
                unitPriceCents, dayIndex, diag);
        }

        /// <summary>
        /// Generalised feed into any bakery bin item (flour, notions):
        /// store lots in, bin units out, at the caller's conversion.
        /// Returns null on success, or a refusal diagnostic with full
        /// rollback (consumption undone, cash restored).
        /// </summary>
        public static string FeedToBin(
            OffMapNamedSkuProcurementService storeService,
            BusinessRuntimeState storeRuntime,
            BakeryShopRuntime bakery, BusinessRuntimeState bakeryRuntime,
            EntityIdRegistry ids,
            string skuId, int storeUnits, string binItemId, int binUnits,
            string purpose, int unitPriceCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (storeService == null || storeRuntime == null || bakery == null || bakeryRuntime == null || ids == null)
                return "Series2StoreToBakeryFeed: store service, store runtime, bakery, bakery runtime, and id registry are required.";
            if (storeUnits <= 0 || binUnits <= 0 || unitPriceCents < 0 || string.IsNullOrWhiteSpace(binItemId))
                return "Series2StoreToBakeryFeed: positive store units, positive bin units, a bin item id, and a non-negative price are required.";

            NamedSkuLotConsumptionRecord consumption =
                storeService.ConsumeLots(skuId, storeUnits, purpose, dayIndex);
            if (consumption == null)
                return "Series2StoreToBakeryFeed: store lot consumption refused; see store diagnostics. Nothing moved.";

            int totalPrice = storeUnits * unitPriceCents;
            if (!bakeryRuntime.TrySpendCents(totalPrice))
            {
                storeService.UndoConsumption(consumption);
                return "Series2StoreToBakeryFeed: bakery cash cannot pay the feed price; consumption undone, nothing moved.";
            }

            storeRuntime.AddCashCents(totalPrice);
            string supplierName = "unknown supplier";
            for (int i = 0; i < storeService.Orders.Count; i++)
            {
                if (storeService.Orders[i] != null && !string.IsNullOrWhiteSpace(storeService.Orders[i].SupplierName))
                {
                    supplierName = storeService.Orders[i].SupplierName;
                    break;
                }
            }

            string problem = BakeryFlourSupply.ReceiveImportArrival(
                bakery.FlourStock, binItemId, binUnits, consumption.ConsumptionId,
                $"General Store {storeService.BusinessId} via {supplierName}",
                dayIndex, ids, diag);
            if (problem != null)
            {
                storeService.UndoConsumption(consumption);
                if (!storeRuntime.TrySpendCents(totalPrice))
                    diag.Add("Series2StoreToBakeryFeed ROLLBACK WARNING: store cash could not return the feed price; cash and stock now disagree and must be reconciled manually.");
                bakeryRuntime.AddCashCents(totalPrice);
                return $"Series2StoreToBakeryFeed: bakery bin refused the {binItemId} ({problem}); consumption undone, cash restored.";
            }

            diag.Add($"Series2StoreToBakeryFeed: {storeUnits}u '{skuId}' from {storeService.BusinessId} became {binUnits} unit(s) '{binItemId}' in the bakery bin for {totalPrice}c (consumption {consumption.ConsumptionId}).");
            return null;
        }
    }
}
