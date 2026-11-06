using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Ordering;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.BreadDrivers
{
    /// <summary>
    /// Caller inputs for one bakery wholesale day. All inputs are
    /// explicit — the driver finds nothing in the scene and resolves
    /// no business by itself. The store inputs name the General Store
    /// buyer; the cash ports must identify their owning businesses
    /// (the transfer driver enforces that before any intake).
    /// </summary>
    public sealed class BakeryWholesaleDailyInputs
    {
        public BakeryShopRuntime Bakery;
        public Series2BakeryProductionDriver ProductionDriver;
        public int DayIndex;
        public bool BakerKitUsable;
        public WorkstationDefinition OvenDefinition;
        public Func<string, WorkstationComponentView?> FindOvenComponent;
        public EntityIdRegistry IdRegistry;
        public IGeneralStoreTradingPort StorePort;
        public IBusinessIdentityCashPort StoreCashPort;
        public IBusinessIdentityCashPort BakeryCashPort;
        public string StoreBusinessId = string.Empty;
        public string BakeryBusinessId = string.Empty;
        public Pass3WholesaleIntake Intake;
        public Pass3StoreCatalog StoreCatalog;
    }

    /// <summary>Outcome of one bakery wholesale day.</summary>
    public struct BakeryWholesaleDailyResult
    {
        /// <summary>Active wholesale accounts on the books at the start of the day.</summary>
        public int AccountsOnBooks;
        /// <summary>Batch ids PlanWholesaleBatches returned today (includes the one-day-ahead lead plan).</summary>
        public int BatchesPlanned;
        /// <summary>Batches actually baked today (-1 refusals count as 0).</summary>
        public int BatchesBaked;
        /// <summary>Due accounts whose transfer AND stocking both completed.</summary>
        public int TransfersCompleted;
        /// <summary>Units stocked into the live store category pool today (prior-day shelf retries included).</summary>
        public int UnitsStocked;
        /// <summary>
        /// Names the day's blocker, if any: no account on the books, or
        /// the production refusal that stopped the bake. A transfer of
        /// previously baked stock can still complete on a day whose bake
        /// was refused; per-account transfer refusals are in the
        /// diagnostics, not here.
        /// </summary>
        public string Refusal;
    }

    /// <summary>
    /// Plan 01 (2026-11-06, slice 1B): the bakery's daily wholesale
    /// day — plan, bake, stock-then-transfer, record. Plain C#, no
    /// Unity dependency, Roslyn-testable; the SimulationDrivers
    /// MonoBehaviour stays a thin forwarder over this class.
    ///
    /// Order, per the plan: (a) no accounts → the named blocker, plan
    /// nothing; (b) plan + bake through the existing calls, all guards
    /// applying (flour/notions/fuel/kit/oven loud refusals, batches stay
    /// planned, nothing bakes on refusal); (c) retry stocking any bread
    /// already sitting on the Pass 3 catalog shelf from a prior day
    /// before transferring new units, so an interrupted day never
    /// double-buys; (d) for each due General Store account, transfer
    /// through Review2BakeryToStoreWholesaleDriver and stock to live —
    /// a refusal leaves the account due (it retries tomorrow), moves no
    /// cash, and loses no bread; (e) on a successful transfer +
    /// stocking, record it on the account via
    /// BakeryShopRuntime.RecordWholesaleTransfer (schedule advance only;
    /// no shelf, no cash).
    ///
    /// The store path never calls FulfillWholesale: that would take the
    /// same units a second time (the pantry delivery-run path, which has
    /// no General Store intake). This path runs production through
    /// WorkDay directly, exactly as DriveBakeryWholesalePlanning does
    /// today — it does NOT reserve Glen's person-time through
    /// TaskAuthority (Obj 9's work). Labour is accounted inside the
    /// runtime's BakerMinutesPerDay budget only.
    /// </summary>
    public static class BakeryWholesaleDailyDriver
    {
        /// <summary>Live store category bread stocks into (staple_food in the store definition).</summary>
        public const string StoreCategoryId = "staple_food";

        public static BakeryWholesaleDailyResult RunDay(BakeryWholesaleDailyInputs inputs, List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new BakeryWholesaleDailyResult();
            if (inputs == null || inputs.Bakery == null || inputs.ProductionDriver == null)
            {
                result.Refusal = "BakeryWholesaleDailyDriver: a bakery runtime and production driver are required; nothing planned.";
                diag.Add(result.Refusal);
                return result;
            }

            BakeryShopRuntime bakery = inputs.Bakery;
            int dayIndex = inputs.DayIndex;

            int activeAccounts = 0;
            foreach (var account in bakery.WholesaleAccounts)
                if (account != null && account.IsActive) activeAccounts++;
            result.AccountsOnBooks = activeAccounts;
            if (activeAccounts == 0)
            {
                result.Refusal = "BakeryWholesaleDailyDriver: no wholesale account on the books — book one via scenario config; nothing planned.";
                diag.Add(result.Refusal);
                return result;
            }

            // (b) Plan + bake — existing calls, moved verbatim.
            List<string> planned = bakery.PlanWholesaleBatches(dayIndex, diag);
            result.BatchesPlanned = planned != null ? planned.Count : 0;
            inputs.ProductionDriver.AdoptPlannedBatches(dayIndex, planned, diag);
            int diagnosticsBeforeRun = diag.Count;
            int baked = inputs.ProductionDriver.RunScheduledDay(
                dayIndex, inputs.BakerKitUsable, inputs.OvenDefinition,
                inputs.FindOvenComponent, null, diag, inputs.IdRegistry);
            result.BatchesBaked = Math.Max(0, baked);
            if (baked < 0)
                result.Refusal = FirstRefusal(diag, diagnosticsBeforeRun, dayIndex);

            // The transfer steps need the store seam; without it the
            // baked bread stays on the bakery shelf — never lost, never
            // teleported — and the accounts stay due for tomorrow.
            if (inputs.StoreCatalog == null || inputs.Intake == null || inputs.StorePort == null
                || inputs.StoreCashPort == null || inputs.BakeryCashPort == null
                || string.IsNullOrWhiteSpace(inputs.StoreBusinessId)
                || string.IsNullOrWhiteSpace(inputs.BakeryBusinessId))
            {
                diag.Add("BakeryWholesaleDailyDriver: no live store seam (catalog, intake, store port, or cash ports) — transfer skipped, bread stays on the bakery shelf, accounts stay due.");
                return result;
            }

            // (c) Stock-then-transfer: retry any bread already sitting on
            // the Pass 3 catalog shelf from a prior day before moving
            // new units, so an interrupted day never double-buys.
            string storeProductId = StoreProductIdFor(BakeryBreadCatalog.BreadLoafId, diag);
            if (storeProductId != null)
            {
                Pass3ProductLine line = inputs.StoreCatalog.Find(storeProductId);
                if (line != null && line.ShelfUnits > 0)
                {
                    Review2StockingResult retry = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                        inputs.StoreCatalog, storeProductId, line.ShelfUnits, StoreCategoryId, inputs.StorePort, diag);
                    if (retry.Stocked) result.UnitsStocked += retry.Units;
                }
            }

            // (d) For each due General Store account, transfer + stock.
            foreach (var account in bakery.WholesaleAccounts)
            {
                if (account == null || !account.IsActive) continue;
                if (account.NextDeliveryDayIndex > dayIndex) continue;
                if (!string.Equals(account.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal)) continue;
                if (!string.Equals(account.BuyerBusinessId, inputs.StoreBusinessId, StringComparison.Ordinal)) continue;

                // Capture the source lot and its provenance BEFORE the
                // transfer takes the units, so the delivery record keeps
                // the full upstream chain. The transfer takes from this
                // same oldest sellable lot and never partials, so the
                // captured lot is the transferred lot.
                BakeryBreadLot sourceLot = OldestSellableLot(bakery, account.ProductId);
                string sourceLotId = sourceLot != null ? sourceLot.LotId.ToString() : string.Empty;
                var provenance = new List<string>();
                if (sourceLot?.InputProvenance != null) provenance.AddRange(sourceLot.InputProvenance);

                int retailPriceCents = inputs.StorePort.CategoryPricePerUnitCents(StoreCategoryId);
                var options = new Review2WholesaleTransferOptions
                {
                    Bakery = bakery,
                    BakeryProductId = account.ProductId,
                    Units = account.UnitsPerDelivery,
                    WholesalePriceCents = account.PricePerUnitCents,
                    RetailPriceCents = retailPriceCents,
                    TransferDayIndex = dayIndex,
                    Intake = inputs.Intake,
                    StoreCatalog = inputs.StoreCatalog,
                    StoreCashPort = inputs.StoreCashPort,
                    BakeryCashPort = inputs.BakeryCashPort,
                    StoreBusinessId = inputs.StoreBusinessId,
                    BakeryBusinessId = inputs.BakeryBusinessId,
                };
                Review2WholesaleTransferResult transfer =
                    Review2BakeryToStoreWholesaleDriver.TransferToShelf(options, diag);
                if (!transfer.Transferred)
                {
                    diag.Add($"BakeryWholesaleDailyDriver: account {account.AccountId} stays due — transfer refused, no cash moved, no bread lost.");
                    continue;
                }

                Review2StockingResult stocked = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                    inputs.StoreCatalog, transfer.StoreProductId, transfer.Units, StoreCategoryId, inputs.StorePort, diag);
                if (!stocked.Stocked)
                {
                    // Stocking refusal leaves the units on the catalog
                    // shelf for step (c) tomorrow; the account stays due.
                    diag.Add($"BakeryWholesaleDailyDriver: account {account.AccountId} stays due — stocking refused, units remain on the catalog shelf.");
                    continue;
                }
                result.UnitsStocked += stocked.Units;

                // (e) Record it on the account (schedule advance only).
                string recordRefusal = bakery.RecordWholesaleTransfer(
                    account.AccountId, sourceLotId, transfer.Units,
                    account.PricePerUnitCents, dayIndex, provenance, diag);
                if (!string.IsNullOrWhiteSpace(recordRefusal))
                {
                    diag.Add($"BakeryWholesaleDailyDriver: account {account.AccountId} transfer stocked but the delivery record refused — {recordRefusal}");
                    continue;
                }
                result.TransfersCompleted++;
            }

            return result;
        }

        private static string StoreProductIdFor(string bakeryProductId, List<string> diag)
        {
            Review2ItemTranslation translation = Review2ItemSkuMap.TryTranslate(
                Review2ItemSpace.BakeryCode, bakeryProductId, Review2ItemSpace.Store, diag);
            if (translation == null)
            {
                diag.Add($"BakeryWholesaleDailyDriver: bakery product '{bakeryProductId}' has no store translation — prior-day shelf stock cannot be retried.");
                return null;
            }
            return translation.ToId;
        }

        private static BakeryBreadLot OldestSellableLot(BakeryShopRuntime bakery, string productId)
        {
            if (bakery?.BreadShelf == null) return null;
            BakeryBreadLot oldest = null;
            foreach (var lot in bakery.BreadShelf)
            {
                if (lot == null || !lot.CanSell) continue;
                if (!string.Equals(lot.ProductId, productId, StringComparison.Ordinal)) continue;
                if (oldest == null || lot.BakedDayIndex < oldest.BakedDayIndex) oldest = lot;
            }
            return oldest;
        }

        private static string FirstRefusal(List<string> diag, int fromIndex, int dayIndex)
        {
            for (int i = Math.Max(0, fromIndex); i < diag.Count; i++)
            {
                string line = diag[i];
                if (!string.IsNullOrEmpty(line)
                    && line.IndexOf("refus", StringComparison.OrdinalIgnoreCase) >= 0)
                    return line;
            }
            return $"BakeryWholesaleDailyDriver: bakery production refused for day {dayIndex}; see [BakeryProduction] diagnostics.";
        }
    }
}
