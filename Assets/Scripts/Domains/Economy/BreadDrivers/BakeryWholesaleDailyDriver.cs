using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
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

        /// <summary>Plan 02 slice 2A: flour procurement seam. The store and
        /// bakery runtime states prove stock and identity; the live store
        /// flour price comes from the forwarder (never hardcoded); the
        /// Millbrook quote carries the forwarder's observed freight state;
        /// the import figures come from ImportCatalog with
        /// ImportOrderPathExists = false in this slice (no live order path).
        /// MillbrookSupplier is the reorder authority (slice 2B).</summary>
        public BusinessRuntimeState StoreRuntimeState;
        public BusinessRuntimeState BakeryRuntimeState;
        public int StoreFlourUnitPriceCents;
        public MillbrookFlourQuote MillbrookQuote;
        public OffMapAidBusinessRuntime MillbrookSupplier;
        public int ImportFlourPricePerLbCents;
        public int ImportTransitDays;
        public bool ImportOrderPathExists;

        /// <summary>Plan 04 N-slice: notions procurement seam. The live
        /// notions supply (store procurement service wrapped by
        /// Series2NotionsSupply) and the caller-supplied counterparty
        /// registration (vendor/SKU/price from the catalog, via the
        /// forwarder). Null on either side means unwired: the driver
        /// step logs the blocker and buys nothing — never a second
        /// supply path, never conjured notions.</summary>
        public Series2NotionsSupply NotionsSupply;
        public Series2CounterpartyRegistration NotionsRegistration;
    }

    /// <summary>Millbrook flour quote as filled by the forwarder: sacks and
    /// goods price from the supplier runtime, miles from the definition, and
    /// the freight quote (or the observed unavailability reason) from the
    /// configured rate card. FreightAvailable is observable state — a
    /// Millbrook shipment has departed — never a guess.</summary>
    public sealed class MillbrookFlourQuote
    {
        public int SacksAvailable;
        public int? GoodsPricePerSackCents;
        public float OneWayMiles;
        public int? FreightQuoteCents;
        public bool FreightAvailable;
        public string FreightUnavailableReason = string.Empty;
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
        /// <summary>Whole flour sacks bought locally from the General Store today (Plan 02).</summary>
        public int FlourSacksPurchased;
        /// <summary>Pounds of flour bought locally today (sacks × 10 lb).</summary>
        public int FlourPoundsPurchased;
        /// <summary>Millbrook reorder lot id placed today, if any (Plan 02 slice 2B).</summary>
        public string MillbrookReorderLotId;
        /// <summary>Notion units fed to the bin from arrived store lots today (Plan 04 N-slice).</summary>
        public int NotionsUnitsFed;
        /// <summary>Notion units ordered today for the pipeline (Plan 04 N-slice).</summary>
        public int NotionsOrdered;
        /// <summary>Arrival day of today's notions order, or 0 when none was placed (Plan 04 N-slice).</summary>
        public int NotionsArrivalDayIndex;
        /// <summary>Plan 04 M4: remaining flour shortfall after today's local purchase, in pounds (0 when covered).</summary>
        public int FlourNeedPoundsRemaining;
        /// <summary>Plan 04 M4: remaining flour shortfall in whole sacks (ceil(lb/10)) — the registerable need.</summary>
        public int FlourNeedSacks;
        /// <summary>Plan 04 M4: remaining notions shortfall after today's feed (batches minus fed, 0 when covered).</summary>
        public int NotionsNeedUnits;
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

        /// <summary>
        /// Plan 04 M4 (Obj 42/43, D-S5 — wire branch): maps the driver's
        /// unmet needs to registered business input needs, in the shape
        /// the SimulationDrivers forwarder registers with SBRM after
        /// RunDay. Only needs with units &gt; 0 are returned. Flour is the
        /// remaining shortfall in whole sacks; notions are the remaining
        /// shortfall in units. Fuel is NOT mapped: the driver computes no
        /// fuel need (NB-3/D-2/D-7 — no fuel supply route, no
        /// IntakeUnitsPerBakeryUnit). MaxPrice is unset (0): the driver
        /// ranks sources by cost but states no max price — inventing a
        /// cap would be a new policy. Buyer is the bakery business id.
        /// </summary>
        public static List<BusinessInputNeed> BuildInputNeeds(BakeryWholesaleDailyResult result, string bakeryBusinessId)
        {
            var needs = new List<BusinessInputNeed>();
            if (string.IsNullOrWhiteSpace(bakeryBusinessId))
            {
                return needs;
            }

            if (result.FlourNeedSacks > 0)
            {
                needs.Add(new BusinessInputNeed
                {
                    BuyerBusinessInstanceId = bakeryBusinessId,
                    InputCategoryId = BakeryFlourLocalPurchase.FlourItemId,
                    UnitsNeeded = result.FlourNeedSacks,
                    MaxPriceCentsPerUnit = 0,
                    ReasonSummary = $"bakery wholesale flour for {result.BatchesPlanned} batch(es), short {result.FlourNeedPoundsRemaining} lb",
                });
            }

            if (result.NotionsNeedUnits > 0)
            {
                needs.Add(new BusinessInputNeed
                {
                    BuyerBusinessInstanceId = bakeryBusinessId,
                    InputCategoryId = BakeryFlourSupply.NotionsMaterialId,
                    UnitsNeeded = result.NotionsNeedUnits,
                    MaxPriceCentsPerUnit = 0,
                    ReasonSummary = $"bakery wholesale notions for {result.BatchesPlanned} batch(es), short {result.NotionsNeedUnits} unit(s)",
                });
            }

            return needs;
        }

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

            // (b½) Flour procurement — Plan 02 slice 2A: buy only today's
            // need, from the recommended executable source, then bake through
            // the existing calls. batchesToday is exactly the adopted set
            // Precheck will see. Slice 2A buys locally only; a remainder the
            // store cannot cover is named here and covered by the Millbrook
            // reorder in slice 2B — the bake proceeds on whatever flour
            // arrived, and a remaining shortfall is the loud Precheck refusal.
            int batchesToday = result.BatchesPlanned;
            int flourOnHandLb = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int needLb = batchesToday * BakeryBreadCatalog.BreadFlourPerBatch - flourOnHandLb;
            int? flourCostPerBatchCents = null;
            if (batchesToday > 0 && needLb > 0)
            {
                int sacksNeeded = (needLb + BakeryFlourLocalPurchase.LbPerSack - 1) / BakeryFlourLocalPurchase.LbPerSack;
                int storeSacks = inputs.StoreRuntimeState != null
                    ? inputs.StoreRuntimeState.GetRetailItemStockUnits(
                        BakeryFlourLocalPurchase.FlourItemId, BakeryFlourLocalPurchase.FlourCategoryId)
                    : 0;
                var comparisonInputs = new BakeryFlourSupplierInputs
                {
                    SacksNeeded = sacksNeeded,
                    StoreSacksAvailable = storeSacks,
                    StorePricePerSackCents = inputs.StoreFlourUnitPriceCents > 0
                        ? (int?)inputs.StoreFlourUnitPriceCents : null,
                    MillbrookSacksAvailable = inputs.MillbrookQuote != null ? inputs.MillbrookQuote.SacksAvailable : 0,
                    MillbrookPricePerSackCents = inputs.MillbrookQuote != null ? inputs.MillbrookQuote.GoodsPricePerSackCents : null,
                    MillbrookOneWayMiles = inputs.MillbrookQuote != null ? inputs.MillbrookQuote.OneWayMiles : 0f,
                    MillbrookFreightQuoteCents = inputs.MillbrookQuote != null ? inputs.MillbrookQuote.FreightQuoteCents : null,
                    MillbrookFreightAvailable = inputs.MillbrookQuote != null && inputs.MillbrookQuote.FreightAvailable,
                    MillbrookFreightUnavailableReason = inputs.MillbrookQuote != null ? inputs.MillbrookQuote.FreightUnavailableReason : string.Empty,
                    ImportPricePerLbCents = inputs.ImportFlourPricePerLbCents > 0
                        ? (int?)inputs.ImportFlourPricePerLbCents : null,
                    ImportTransitDays = inputs.ImportTransitDays,
                    ImportOrderPathExists = inputs.ImportOrderPathExists,
                };
                BakeryFlourSupplierComparisonResult comparison =
                    BakeryFlourSupplierComparison.Compare(comparisonInputs, diag);
                if (comparison.Recommended != null)
                    flourCostPerBatchCents = comparison.Recommended.CostPerBatchCents;

                if (comparison.Recommended != null
                    && string.Equals(comparison.Recommended.SupplierId, "general-store", StringComparison.Ordinal))
                {
                    if (inputs.StoreRuntimeState == null || inputs.BakeryRuntimeState == null
                        || inputs.BakeryCashPort == null || inputs.StoreCashPort == null || inputs.IdRegistry == null)
                    {
                        diag.Add("BakeryWholesaleDailyDriver: flour purchase skipped — the store seam (store/bakery runtimes, cash ports, id registry) is incomplete; nothing bought.");
                    }
                    else
                    {
                        int sacksToBuy = Math.Min(sacksNeeded, storeSacks);
                        if (sacksToBuy > 0)
                        {
                            string transactionId = BakeryFlourLocalPurchase.BuildTransactionId(
                                inputs.BakeryBusinessId, dayIndex,
                                BakeryFlourLocalPurchase.NextSequenceForDay(bakery, inputs.BakeryBusinessId, dayIndex));
                            var purchaseInputs = new BakeryFlourLocalPurchaseInputs
                            {
                                StoreRuntime = inputs.StoreRuntimeState,
                                BakeryRuntime = inputs.BakeryRuntimeState,
                                Bakery = bakery,
                                BakeryCashPort = inputs.BakeryCashPort,
                                StoreCashPort = inputs.StoreCashPort,
                                IdRegistry = inputs.IdRegistry,
                                BakeryBusinessId = inputs.BakeryBusinessId,
                                UnitPriceCents = inputs.StoreFlourUnitPriceCents,
                                SacksRequested = sacksToBuy,
                                DayIndex = dayIndex,
                                TransactionId = transactionId,
                            };
                            BakeryFlourLocalPurchaseResult purchase =
                                BakeryFlourLocalPurchase.PurchaseSacks(purchaseInputs, diag);
                            if (purchase.Succeeded && !purchase.Duplicate)
                            {
                                result.FlourSacksPurchased += purchase.SacksPurchased;
                                result.FlourPoundsPurchased += purchase.PoundsReceived;
                                needLb -= purchase.PoundsReceived;
                            }
                            else if (!purchase.Succeeded)
                            {
                                diag.Add($"BakeryWholesaleDailyDriver: flour purchase refused — {purchase.Refusal}");
                            }
                        }
                    }
                }
                else if (comparison.Recommended == null)
                {
                    diag.Add($"BakeryWholesaleDailyDriver: no flour purchase today — {comparison.Refusal}");
                }
                else
                {
                    diag.Add($"BakeryWholesaleDailyDriver: recommended source {comparison.Recommended.DisplayName} is not a local counter purchase — no flour bought locally today.");
                }

                if (needLb > 0)
                {
                    diag.Add($"BakeryWholesaleDailyDriver: flour still short {needLb} lb for today's {batchesToday} batch(es) after local purchase — the bake will refuse at Precheck unless a Millbrook reorder covers it (slice 2B).");
                }
            }
            else if (batchesToday > 0)
            {
                diag.Add($"BakeryWholesaleDailyDriver: flour sufficient — {flourOnHandLb} lb on hand for {batchesToday} batch(es); no purchase, no comparison.");
                if (inputs.StoreFlourUnitPriceCents > 0)
                    flourCostPerBatchCents = BakeryFlourSupplierComparison.SacksPerBatch * inputs.StoreFlourUnitPriceCents;
            }

            // M4 (Plan 04, D-S5): the remaining flour shortfall is the
            // registerable need — the driver's own computation, in whole
            // sacks. Zero when the bin + today's purchase cover the plan.
            result.FlourNeedPoundsRemaining = Math.Max(0, needLb);
            result.FlourNeedSacks = (result.FlourNeedPoundsRemaining + BakeryFlourLocalPurchase.LbPerSack - 1) / BakeryFlourLocalPurchase.LbPerSack;

            // (b¾) Notions procurement + feed — Plan 04 N-slice: buy only
            // today's need (1 notion/batch), receive due arrivals first,
            // feed the bin, then order the remaining shortfall for the
            // 14-day pipeline. This runs before RunScheduledDay, so a fed
            // bin passes Precheck on notions; an unfed bin refuses there
            // loudly, and the pipeline order is the honest path to the
            // next bake — never a bootstrap seed, never conjured stock.
            if (batchesToday > 0)
            {
                var notionsInputs = new BakeryNotionsDailySupplyInputs
                {
                    NotionsSupply = inputs.NotionsSupply,
                    StoreRuntime = inputs.StoreRuntimeState,
                    BakeryRuntime = inputs.BakeryRuntimeState,
                    Bakery = bakery,
                    IdRegistry = inputs.IdRegistry,
                    Registration = inputs.NotionsRegistration,
                    StoreBusinessId = inputs.StoreBusinessId,
                    BakeryBusinessId = inputs.BakeryBusinessId,
                    BatchesToday = batchesToday,
                    DayIndex = dayIndex,
                };
                BakeryNotionsDailySupplyResult notionsResult =
                    BakeryNotionsDailySupply.RunDay(notionsInputs, diag);
                result.NotionsUnitsFed += notionsResult.NotionsFed;
                result.NotionsOrdered += notionsResult.NotionsOrdered;
                result.NotionsArrivalDayIndex = notionsResult.NotionsArrivalDayIndex;
            }

            // M4 (Plan 04, D-S5): the remaining notions shortfall is the
            // registerable need — the day's batches minus what the bin
            // was fed. Zero when the bin covers the plan. (The pipeline
            // order above is the supply response, not a second need.)
            result.NotionsNeedUnits = Math.Max(0, batchesToday - result.NotionsUnitsFed);

            // Per-batch economics, logged whenever a batch is planned today:
            // flour cost vs wholesale revenue, margin visible, never hidden.
            // The missing B2B flour price term is recorded in Plan 02 §5.
            if (batchesToday > 0 && flourCostPerBatchCents.HasValue)
            {
                int accountPrice = AccountPricePerUnit(bakery, inputs.StoreBusinessId);
                if (accountPrice > 0)
                {
                    int revenuePerBatch = BakeryBreadCatalog.BreadYieldPerBatch * accountPrice;
                    int margin = revenuePerBatch - flourCostPerBatchCents.Value;
                    diag.Add($"BakeryWholesaleDailyDriver: per-batch economics — flour {flourCostPerBatchCents.Value}c/batch vs wholesale revenue {revenuePerBatch}c/batch (24 loaves x {accountPrice}c), margin {margin}c/batch; no negotiated B2B flour price term exists (Plan 02 §5) — buying at the live retail price.");
                }
            }

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

        private static int AccountPricePerUnit(BakeryShopRuntime bakery, string storeBusinessId)
        {
            if (bakery?.WholesaleAccounts == null) return 0;
            foreach (var account in bakery.WholesaleAccounts)
            {
                if (account != null && account.IsActive
                    && string.Equals(account.BuyerBusinessId, storeBusinessId, StringComparison.Ordinal)
                    && string.Equals(account.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal))
                    return account.PricePerUnitCents;
            }
            foreach (var account in bakery.WholesaleAccounts)
            {
                if (account != null && account.IsActive) return account.PricePerUnitCents;
            }
            return 0;
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
