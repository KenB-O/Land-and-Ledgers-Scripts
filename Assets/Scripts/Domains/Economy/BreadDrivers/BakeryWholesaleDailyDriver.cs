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

        /// <summary>Plan 10 E3 (Obj 9): Glen's person budget for the day
        /// in minutes (600 x capacity, resolved by the forwarder from the
        /// bound OwnerPersonId). Null = the gate is inactive (unbound
        /// owner or unresolvable budget) and Precheck runs on the runtime
        /// rules exactly as before — never defaulted.</summary>
        public int? CallerDailyBudgetMinutes;

        /// <summary>Plan 04 N-slice: notions procurement seam. The live
        /// notions supply (store procurement service wrapped by
        /// Series2NotionsSupply) and the caller-supplied counterparty
        /// registration (vendor/SKU/price from the catalog, via the
        /// forwarder). Null on either side means unwired: the driver
        /// step logs the blocker and buys nothing — never a second
        /// supply path, never conjured notions.</summary>
        public Series2NotionsSupply NotionsSupply;
        public Series2CounterpartyRegistration NotionsRegistration;

        /// <summary>Priority 3 Slice 4: caller-supplied pipeline depths
        /// in days, one per supply leg — how many future days of demand
        /// the pipeline should cover beyond today. Both default to 0
        /// (today's behaviour exactly); the production default stays 0
        /// until the owner names the policy (B-1). The forwarder does
        /// not set these fields, so no nonzero depth is wired in
        /// production by this slice. Parameters, not values: no depth
        /// is invented here.</summary>
        public int NotionsPipelineDepthDays;
        public int FlourPipelineDepthDays;

        /// <summary>Plan 04 Slice 4: cumulative-actuals carrier. The
        /// driver is stateless, so the caller threads the running totals
        /// in (the forwarder keeps them in member fields, diagnostics
        /// only — never persisted) and reads the updated totals back out
        /// on the result. All six default to 0. CumulativeActualsThreaded
        /// says the caller is threading: when true the driver logs the
        /// cumulative reconciliation line; when false it logs the honest
        /// per-day actuals line instead — never a cumulative label on
        /// per-day figures.</summary>
        public int PriorCumulativeBatchesBaked;
        public int PriorCumulativeFlourCashSpentCents;
        public int PriorCumulativeFlourSacksPurchased;
        public int PriorCumulativeFlourPoundsPurchased;
        public int PriorCumulativeNotionsCashSpentCents;
        public int PriorCumulativeWholesaleRevenueCents;
        public bool CumulativeActualsThreaded;
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
        /// <summary>Whole sacks the Millbrook reorder placed today covers (Plan 02 slice 2B / Plan 10 E1).
        /// The forwarder's M4 registration subtracts these from FlourNeedSacks, so flour already covered by
        /// a placed reorder is never registered a second time as an unmet need.</summary>
        public int MillbrookReorderSacks;
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
        /// <summary>Plan 04 Slice 4: cumulative actuals across the run
        /// (prior threaded totals + today). Diagnostics only — the
        /// forwarder carries them, nothing is persisted.</summary>
        public int CumulativeBatchesBaked;
        public int CumulativeFlourCashSpentCents;
        public int CumulativeFlourSacksPurchased;
        public int CumulativeFlourPoundsPurchased;
        public int CumulativeNotionsCashSpentCents;
        public int CumulativeWholesaleRevenueCents;
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
    /// a refusal before transfer leaves the account due. Once intake,
    /// settlement, and receipt into the store catalog's receiving shelf
    /// succeed, record the account even if live category stocking must wait
    /// for capacity. Step (c) retries that stocking tomorrow. Account
    /// recording advances schedule only; it moves no shelf units or cash.
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
        /// remaining shortfall in whole sacks, minus any sacks already
        /// covered by a Millbrook reorder placed today (E1: the order is
        /// placed, the lot waits on freight — registering it again would
        /// double-count the demand); notions are the remaining
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
                // Plan 10 E1 (M4): sacks already covered by a Millbrook
                // reorder placed today are not an unmet need — the order
                // is placed, the lot waits on the freight pipeline, and
                // registering it again would double-count the demand.
                int uncoveredSacks = Math.Max(0, result.FlourNeedSacks - result.MillbrookReorderSacks);
                if (uncoveredSacks > 0)
                {
                    needs.Add(new BusinessInputNeed
                    {
                        BuyerBusinessInstanceId = bakeryBusinessId,
                        InputCategoryId = BakeryFlourLocalPurchase.FlourItemId,
                        UnitsNeeded = uncoveredSacks,
                        MaxPriceCentsPerUnit = 0,
                        ReasonSummary = $"bakery wholesale flour for {result.BatchesPlanned} batch(es), short {result.FlourNeedPoundsRemaining} lb",
                    });
                }
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

        /// <summary>Priority 3 Slice 4: pounds of flour in unreceived
        /// Millbrook lots for this buyer — committed (in freight) and
        /// uncommitted (awaiting pickup) alike, since both are future
        /// flour the forward target must not order twice. Received lots
        /// are already in the bin (on-hand), never counted here. This
        /// is the depth computation's in-transit figure; it is wider
        /// than the single-outstanding-order guard's filter (which
        /// counts uncommitted lots only), and the two are not confused:
        /// the guard still refuses a duplicate reorder on its own
        /// terms inside PlaceReorder.</summary>
        private static int OutstandingMillbrookLb(BakeryWholesaleDailyInputs inputs)
        {
            int total = 0;
            if (inputs == null || inputs.MillbrookSupplier == null || inputs.MillbrookSupplier.Lots == null) return 0;
            foreach (OffMapAidCargoLot lot in inputs.MillbrookSupplier.Lots)
            {
                if (lot == null) continue;
                if (lot.Received) continue;
                if (!string.Equals(lot.BuyerBusinessId, inputs.BakeryBusinessId, StringComparison.Ordinal)) continue;
                total += Math.Max(0, lot.InventoryUnits);
            }
            return total;
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
            // PlanWholesaleBatches returns only newly created IDs. A prior
            // batch can remain active after a legitimate input refusal and
            // already cover the wholesale account, so today's new-plan count
            // is zero even though the execution driver has real work to retry.
            // Use the driver's adopted active set for input procurement and
            // Precheck; otherwise delayed notions arrivals are never received
            // or fed and the batch remains permanently blocked.
            int batchesToday = inputs.ProductionDriver.PlannedBatchIds.Count;
            int flourOnHandLb = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int needLb = batchesToday * BakeryBreadCatalog.BreadFlourPerBatch - flourOnHandLb;
            // Priority 3 Slice 4: forward cover. At depth 0 orderLb ==
            // needLb, so today's behaviour is unchanged. At depth D > 0
            // the procurement step seeks (D + 1) x one day's flour need
            // — today plus D future days — net of bin on-hand and
            // outstanding in-transit Millbrook lb. The shortfall itself
            // stays bin-only (Slice 2 scope note): flour already
            // dispensed into a batch's custody is NOT subtracted here —
            // driver-side custody awareness is a separate follow-up.
            // needLb keeps tracking today's shortfall (after the local
            // purchase) for FlourNeedPoundsRemaining and the "still
            // short" diagnostic; orderLb tracks the remaining forward
            // need for the purchase/reorder decisions.
            int flourDepthDays = Math.Max(0, inputs.FlourPipelineDepthDays);
            int inTransitFlourLb = flourDepthDays > 0 ? OutstandingMillbrookLb(inputs) : 0;
            int forwardTargetLb = (flourDepthDays + 1) * batchesToday * BakeryBreadCatalog.BreadFlourPerBatch;
            int orderLb = flourDepthDays > 0
                ? Math.Max(0, forwardTargetLb - flourOnHandLb - inTransitFlourLb)
                : needLb;
            int? flourCostPerBatchCents = null;
            if (batchesToday > 0 && (needLb > 0 || orderLb > 0))
            {
                int sacksNeeded = (orderLb + BakeryFlourLocalPurchase.LbPerSack - 1) / BakeryFlourLocalPurchase.LbPerSack;
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
                                orderLb -= purchase.PoundsReceived;
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

                // Plan 10 E1 (slice 2B remainder): the store could not
                // cover the need — order the remaining sacks from
                // Millbrook through the same paid-lot authority the
                // opening order uses. The lot enters the existing freight
                // pipeline (negotiation finds it via FindShippableLot);
                // the bake today proceeds on whatever flour arrived, and
                // any remaining shortfall is the loud Precheck refusal.
                // The driver has no owner name; the buyer business id is
                // the honest identifier on the cash transaction.
                if (orderLb > 0 && inputs.MillbrookSupplier != null && inputs.BakeryCashPort != null)
                {
                    int sacksRemaining = (orderLb + BakeryFlourLocalPurchase.LbPerSack - 1) / BakeryFlourLocalPurchase.LbPerSack;
                    string reorderRefusal = MillbrookFlourReorder.PlaceReorder(
                        inputs.MillbrookSupplier, inputs.BakeryCashPort, inputs.BakeryBusinessId,
                        inputs.BakeryBusinessId, sacksRemaining, dayIndex, diag, out OffMapAidCargoLot reorderLot);
                    if (reorderRefusal == null && reorderLot != null)
                    {
                        result.MillbrookReorderLotId = reorderLot.LotId;
                        result.MillbrookReorderSacks = reorderLot.PackageUnits;
                        // needLb is deliberately NOT decremented: the
                        // result keeps the raw shortfall after the local
                        // purchase, and BuildInputNeeds subtracts the
                        // covered sacks when registering the M4 need.
                        diag.Add($"BakeryWholesaleDailyDriver: Millbrook reorder placed — lot '{reorderLot.LotId}' " +
                            $"({reorderLot.PackageUnits} sack(s), {reorderLot.InventoryUnits} lb) enters the freight pipeline; " +
                            $"flour arrives after pickup + return, not today.");
                    }
                    else
                    {
                        diag.Add($"BakeryWholesaleDailyDriver: Millbrook reorder refused — {reorderRefusal}");
                    }
                }

                if (flourDepthDays > 0 && needLb > 0 && orderLb <= 0)
                {
                    diag.Add($"BakeryWholesaleDailyDriver: no new flour order — the forward target ({forwardTargetLb} lb) is already covered by {flourOnHandLb} lb on hand + {inTransitFlourLb} lb in transit.");
                }

                if (needLb > 0)
                {
                    diag.Add($"BakeryWholesaleDailyDriver: flour still short {needLb} lb for today's {batchesToday} batch(es) after local purchase" +
                        (string.IsNullOrWhiteSpace(result.MillbrookReorderLotId)
                            ? " — the bake will refuse at Precheck unless flour arrives."
                            : $" — Millbrook reorder '{result.MillbrookReorderLotId}' is outstanding and will cover it once the freight pipeline delivers; the bake will refuse at Precheck today."));
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
                    PipelineDepthDays = inputs.NotionsPipelineDepthDays,
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
            // the TRUE cost — flour + notions — vs wholesale revenue, margin
            // visible, never hidden. Plan 10 E5 (reporting only): the old
            // flour-only line understated the loss by the notions cost (58c
            // vs 48c = -10c); the true cost is 68c vs 48c = -20c/batch. No
            // price, cost, quantity, or decision changes here — the margin
            // investigation itself (B4) stays separate work. When the
            // notions registration is absent (unwired notions) the line
            // states the notions cost as unknown rather than 0, labels the
            // flour-only figure as such, and prints no margin; a
            // zero/negative notions price is treated as unknown, never as
            // free notions. The missing B2B flour price term is recorded
            // in Plan 02 §5.
            //
            // Plan 11 M1 (reporting only): the cost above is a
            // STANDALONE-BATCH standard cost — it charges two full sacks
            // of flour to every batch and never credits carryover — so
            // the line labels it as such and appends the day's actual
            // flour purchase from fields the result already carries
            // (sacks bought, pounds received, cash spent at the live
            // store price) plus the bin position after the purchase.
            // No new state, price, cost, or decision: procurement,
            // bake, transfer, and settlement are untouched.
            if (batchesToday > 0 && flourCostPerBatchCents.HasValue)
            {
                int accountPrice = AccountPricePerUnit(bakery, inputs.StoreBusinessId);
                if (accountPrice > 0)
                {
                    int revenuePerBatch = BakeryBreadCatalog.BreadYieldPerBatch * accountPrice;
                    int? notionsCostPerBatchCents = inputs.NotionsRegistration != null
                        && inputs.NotionsRegistration.UnitGoodsCostCents > 0
                        ? (int?)(BakeryBreadCatalog.StandardNotionsPerBatch * inputs.NotionsRegistration.UnitGoodsCostCents)
                        : null;
                    int actualFlourCashSpentCents = result.FlourSacksPurchased * inputs.StoreFlourUnitPriceCents;
                    int flourOnHandAfterPurchaseLb = flourOnHandLb + result.FlourPoundsPurchased;
                    string actualPurchaseClause = $"actual flour purchase today: {result.FlourSacksPurchased} sack(s), {result.FlourPoundsPurchased} lb, {actualFlourCashSpentCents}c spent; flour on hand after purchase: {flourOnHandAfterPurchaseLb} lb";
                    if (notionsCostPerBatchCents.HasValue)
                    {
                        int trueCostPerBatch = flourCostPerBatchCents.Value + notionsCostPerBatchCents.Value;
                        int margin = revenuePerBatch - trueCostPerBatch;
                        diag.Add($"BakeryWholesaleDailyDriver: per-batch economics (standalone-batch standard cost) — flour {flourCostPerBatchCents.Value}c + notions {notionsCostPerBatchCents.Value}c = {trueCostPerBatch}c/batch vs wholesale revenue {revenuePerBatch}c/batch (24 loaves x {accountPrice}c), margin {margin}c/batch; {actualPurchaseClause}; no negotiated B2B flour price term exists (Plan 02 §5) — buying at the live retail price.");
                    }
                    else
                    {
                        diag.Add($"BakeryWholesaleDailyDriver: per-batch economics (standalone-batch standard cost; flour only — notions cost unknown, no notions registration) — flour {flourCostPerBatchCents.Value}c/batch vs wholesale revenue {revenuePerBatch}c/batch (24 loaves x {accountPrice}c), no margin stated; {actualPurchaseClause}; no negotiated B2B flour price term exists (Plan 02 §5) — buying at the live retail price.");
                    }
                }
            }

            // Priority 3 Slice 3 (2026-11-08): one pipeline-coverage
            // line per day, after the notions step and before
            // RunScheduledDay — log-only, no state or ordering change.
            LogPipelineCoverage(inputs, dayIndex, diag);

            int diagnosticsBeforeRun = diag.Count;
            int baked = inputs.ProductionDriver.RunScheduledDay(
                dayIndex, inputs.BakerKitUsable, inputs.OvenDefinition,
                inputs.FindOvenComponent, inputs.CallerDailyBudgetMinutes, diag, inputs.IdRegistry);
            result.BatchesBaked = Math.Max(0, baked);
            if (baked < 0)
            {
                result.Refusal = FirstRefusal(diag, diagnosticsBeforeRun, dayIndex);
                // Plan 04 Slice 4: name the blocked batch once per day
                // with the refusal's own cause and the retry, so a stall is
                // visible in the log instead of silent. No state change.
                LogBlockedBatches(inputs, result, dayIndex, diag);
            }

            // The transfer steps need the store seam; without it the
            // baked bread stays on the bakery shelf — never lost, never
            // teleported — and the accounts stay due for tomorrow.
            if (inputs.StoreCatalog == null || inputs.Intake == null || inputs.StorePort == null
                || inputs.StoreCashPort == null || inputs.BakeryCashPort == null
                || string.IsNullOrWhiteSpace(inputs.StoreBusinessId)
                || string.IsNullOrWhiteSpace(inputs.BakeryBusinessId))
            {
                diag.Add("BakeryWholesaleDailyDriver: no live store seam (catalog, intake, store port, or cash ports) — transfer skipped, bread stays on the bakery shelf, accounts stay due.");
                // Plan 04 Slice 4: a batch that baked before the seam
                // refusal still reports its actuals (the bake happened;
                // the sale did not — revenue 0 today).
                return FinishWithActualsLine(inputs, result, 0, diag);
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
                        inputs.StoreCatalog, storeProductId, line.ShelfUnits, StoreCategoryId, inputs.StorePort, diag,
                        "loaf", null, dayIndex);
                    if (retry.Stocked) result.UnitsStocked += retry.Units;
                }
            }

            // (d) For each due General Store account, transfer + stock.
            // Plan 04 Slice 4: the wholesale cash the transfer settles
            // (units x account price) is today's revenue for the
            // cumulative-actuals line; the (c) retry above stocks prior-day
            // shelf bread and moves no wholesale cash, so it is not counted.
            int wholesaleRevenueTodayCents = 0;
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

                // TransferToShelf has completed intake, moved this bread
                // lot into the store's receiving catalog, and settled the
                // cash. That is the wholesale delivery boundary. A full
                // live category pool may defer stocking, but must not leave
                // this paid delivery due for another settlement attempt.
                string recordRefusal = bakery.RecordWholesaleTransfer(
                    account.AccountId, sourceLotId, transfer.Units,
                    account.PricePerUnitCents, dayIndex, provenance, diag);
                if (!string.IsNullOrWhiteSpace(recordRefusal))
                {
                    diag.Add($"BakeryWholesaleDailyDriver: transfer settled and received into catalog back stock, but the account record refused — {recordRefusal}");
                    continue;
                }
                result.TransfersCompleted++;
                wholesaleRevenueTodayCents += account.UnitsPerDelivery * account.PricePerUnitCents;

                Review2StockingResult stocked = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                    inputs.StoreCatalog, transfer.StoreProductId, transfer.Units, StoreCategoryId, inputs.StorePort, diag,
                    "loaf", transfer.BakeryLotId, dayIndex);
                if (!stocked.Stocked)
                {
                    // The settled delivery and schedule are already
                    // recorded. Units remain in store receiving stock and
                    // step (c) retries live stocking tomorrow.
                    diag.Add($"BakeryWholesaleDailyDriver: account {account.AccountId} delivery recorded; live stocking refused, units remain on the catalog receiving shelf.");
                    continue;
                }
                result.UnitsStocked += stocked.Units;
            }

            return FinishWithActualsLine(inputs, result, wholesaleRevenueTodayCents, diag);
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

        /// <summary>
        /// Priority 3 Slice 3 (2026-11-08): one pipeline-coverage line
        /// per day — the full pipeline position a reorder decision needs
        /// (bin, in-custody, in-transit with arrival days, days-of-cover
        /// at the account's daily rate, next arrival day) in a single
        /// line, so the investigation can separate H-A1 from H-A2/H-A3
        /// live. LOG-ONLY: no state, ordering, price, or quantity
        /// changes; the line is computed only from state the driver
        /// already holds (bin lots, batch custody lines, the day's
        /// Millbrook reorder via the supplier's outstanding lots, the
        /// notions service's order list with ExpectedArrivalDayIndex).
        /// One line per RunDay call — bounded by construction, the same
        /// discipline as the 12-per-day blocked-batch cap. The word
        /// "covered" never appears in this line: the planned yield is
        /// stated as planned yield, explicitly "not stock".
        ///
        /// Days-of-cover = min(flour batch-days, notions batch-days) at
        /// 1 batch/day, where flour batch-days = (bin + custody +
        /// in-transit lb) / 12 and notions batch-days = (on hand + in
        /// flight) / 1. Custody flour counts because it is committed to
        /// the active batch's bake, which is one of the covered daily
        /// bakes; the line states its inputs so the figure is auditable
        /// from the line itself. Fuel on hand is carried as a field (a
        /// fuel cap is visible there) but does not enter the cover
        /// figure, which is the flour/notions pipeline position.
        ///
        /// Producible is the Precheck verdict evaluated here on the
        /// same state Precheck will see inside RunScheduledDay — nothing
        /// mutates between the two calls, and Precheck is pure reads
        /// plus diagnostics (EvaluateReady included), so it is evaluated
        /// here against a scratch list and its own lines are not
        /// duplicated. First failing input is the Precheck refusal's
        /// cause word in check order (flour → notions → fuel → kit →
        /// oven → budget), via the same CauseFromRefusal helper the
        /// blocked-batch line uses.
        ///
        /// Millbrook arrival honesty: no Millbrook lot carries a
        /// scheduled arrival day (OffMapAidCargoLot has CreatedDayIndex
        /// only; no transit-days figure exists in the driver-held quote
        /// or freight path), so an outstanding lot is stated with its
        /// order day and "arrival unscheduled (freight pipeline)" —
        /// never an invented day. The next-arrival-day field is
        /// therefore driven by the notions order list
        /// (ExpectedArrivalDayIndex), the only scheduled arrivals the
        /// driver holds; when none are scheduled it reads "none in
        /// flight". A missing notions service reads "unwired (no
        /// service)" on the in-flight field, never a guessed zero.
        /// </summary>
        private static void LogPipelineCoverage(BakeryWholesaleDailyInputs inputs, int dayIndex, List<string> diag)
        {
            if (inputs?.Bakery == null || inputs.ProductionDriver == null) return;
            BakeryShopRuntime bakery = inputs.Bakery;

            int binFlourLb = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int notionsOnHand = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId);
            int fuelOnHand = bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);
            int custodyFlourLb = 0;
            int custodyNotions = 0;
            if (bakery.Batches != null)
            {
                foreach (BakeryDoughBatch batch in bakery.Batches)
                {
                    if (batch == null || !batch.IsActive) continue;
                    if (batch.FlourInCustody != null)
                        foreach (BakeryFlourDispenseLine line in batch.FlourInCustody)
                            if (line != null) custodyFlourLb += Math.Max(0, line.UnitsTaken);
                    if (batch.NotionsUsed != null)
                        foreach (BakeryFlourDispenseLine line in batch.NotionsUsed)
                            if (line != null) custodyNotions += Math.Max(0, line.UnitsTaken);
                }
            }

            // Outstanding Millbrook sacks: uncommitted, unreceived lots
            // for this buyer — the same filter MillbrookFlourReorder's
            // single-outstanding-order guard uses, so today's reorder
            // (placed earlier in this RunDay) is included with any prior
            // outstanding lot. No lot carries a scheduled arrival day.
            string millbrookText;
            int transitFlourLb = 0;
            if (inputs.MillbrookSupplier == null)
            {
                millbrookText = "none in flight";
            }
            else
            {
                var lotTexts = new List<string>();
                int totalSacks = 0;
                if (inputs.MillbrookSupplier.Lots != null)
                {
                    foreach (OffMapAidCargoLot lot in inputs.MillbrookSupplier.Lots)
                    {
                        if (lot == null) continue;
                        if (!string.Equals(lot.BuyerBusinessId, inputs.BakeryBusinessId, StringComparison.Ordinal)) continue;
                        if (lot.SourceCommitted || lot.Received) continue;
                        totalSacks += Math.Max(0, lot.PackageUnits);
                        transitFlourLb += Math.Max(0, lot.InventoryUnits);
                        lotTexts.Add($"lot '{lot.LotId}' ordered day {Math.Max(0, lot.CreatedDayIndex)}");
                    }
                }
                millbrookText = lotTexts.Count == 0
                    ? "none in flight"
                    : $"{totalSacks} sack(s) ({transitFlourLb} lb), {string.Join("; ", lotTexts)}, arrival unscheduled (freight pipeline)";
            }

            // Notions in flight by arrival day: live (unreceived,
            // uncancelled) orders for the configured notions SKU on the
            // store's procurement service, grouped by their scheduled
            // arrival day. The next arrival day is the earliest of them.
            string notionsSku = !string.IsNullOrWhiteSpace(inputs.NotionsRegistration?.SkuId)
                ? inputs.NotionsRegistration.SkuId
                : BakeryFlourSupply.NotionsMaterialId;
            OffMapNamedSkuProcurementService service = inputs.NotionsSupply != null && inputs.NotionsSupply.Procurement != null
                ? inputs.NotionsSupply.Procurement.Service
                : null;
            string notionsFlightText;
            int inFlightNotions = 0;
            int? nextArrivalDay = null;
            if (service == null)
            {
                notionsFlightText = "unwired (no service)";
            }
            else
            {
                var byArrival = new Dictionary<int, int>();
                if (service.Orders != null)
                {
                    foreach (NamedSkuOrder order in service.Orders)
                    {
                        if (order == null) continue;
                        if (!string.Equals(order.SkuId, notionsSku, StringComparison.Ordinal)) continue;
                        if (order.Stage == NamedSkuOrderStage.Received || order.Stage == NamedSkuOrderStage.Cancelled) continue;
                        int arrival = order.ExpectedArrivalDayIndex;
                        int existing = 0;
                        byArrival.TryGetValue(arrival, out existing);
                        byArrival[arrival] = existing + Math.Max(0, order.UnitsOrdered);
                    }
                }
                if (byArrival.Count == 0)
                {
                    notionsFlightText = "none in flight";
                }
                else
                {
                    var days = new List<int>(byArrival.Keys);
                    days.Sort();
                    var parts = new List<string>();
                    foreach (int arrival in days)
                    {
                        inFlightNotions += byArrival[arrival];
                        parts.Add($"day {arrival}: {byArrival[arrival]}u");
                        if (nextArrivalDay == null || arrival < nextArrivalDay.Value) nextArrivalDay = arrival;
                    }
                    notionsFlightText = $"{inFlightNotions}u ({string.Join(", ", parts)})";
                }
            }

            int flourDays = (binFlourLb + custodyFlourLb + transitFlourLb) / BakeryBreadCatalog.BreadFlourPerBatch;
            int notionsDays = (notionsOnHand + inFlightNotions) / BakeryBreadCatalog.StandardNotionsPerBatch;
            int daysOfCover = Math.Min(flourDays, notionsDays);
            string nextArrivalText = nextArrivalDay.HasValue ? nextArrivalDay.Value.ToString() : "none in flight";

            string firstFail;
            string producible;
            if (inputs.ProductionDriver.PlannedBatchIds == null || inputs.ProductionDriver.PlannedBatchIds.Count == 0)
            {
                firstFail = "nothing planned";
                producible = "no (nothing planned)";
            }
            else
            {
                var scratchDiag = new List<string>();
                string refusal = inputs.ProductionDriver.Precheck(
                    dayIndex, inputs.BakerKitUsable, inputs.OvenDefinition,
                    inputs.FindOvenComponent, inputs.CallerDailyBudgetMinutes, scratchDiag);
                if (refusal == null)
                {
                    firstFail = "none";
                    producible = "yes";
                }
                else
                {
                    firstFail = CoverageCauseFromRefusal(refusal);
                    producible = $"no ({firstFail})";
                }
            }

            diag.Add($"[BakeryProduction] pipeline coverage day {dayIndex}: bin flour {binFlourLb} lb, flour in custody {custodyFlourLb} lb, " +
                $"notions on hand {notionsOnHand}u, notions in custody {custodyNotions}u, fuel {fuelOnHand}u, " +
                $"Millbrook in transit: {millbrookText}, notions in flight: {notionsFlightText}, " +
                $"days-of-cover {daysOfCover} at 1 batch/day (planned yield {BakeryBreadCatalog.BreadYieldPerBatch}u/batch, not stock), " +
                $"next arrival day {nextArrivalText}, first failing input: {firstFail}, producible: {producible}");
        }

        /// <summary>Priority 3 Slice 3: the coverage line's
        /// first-failing-input word. The Precheck refusal's cause word
        /// in check order, via the same CauseFromRefusal helper the
        /// blocked-batch line uses — except the two degenerate Precheck
        /// refusals the helper cannot reduce (it returns the whole
        /// refusal text when no input keyword matches): a missing fuel
        /// source is not a fuel shortage, and a product with no catalog
        /// spec is not an input shortfall, so each gets its own short
        /// label here. The no-batches-planned refusal never reaches
        /// this helper (LogPipelineCoverage handles the empty plan
        /// before calling Precheck).</summary>
        private static string CoverageCauseFromRefusal(string refusal)
        {
            if (string.IsNullOrWhiteSpace(refusal)) return "unknown";
            string lower = refusal.ToLowerInvariant();
            if (lower.Contains("no fuel source")) return "fuel source missing";
            if (lower.Contains("no catalog spec")) return "no catalog spec";
            return CauseFromRefusal(refusal);
        }

        /// <summary>Plan 04 Slice 4: one blocked-batch line per active
        /// planned batch per RunDay call — the same 12-per-day
        /// diagnostics cap the forwarder applies in DriveDailyNeeds, so
        /// a many-batch stall stays bounded, with an "and N more" tail.
        /// The cause comes from the refusal text (CauseFromRefusal);
        /// "covered" is the yield of every active batch for the same
        /// product, i.e. the units the account is waiting on. The batch
        /// stays Planned and retries tomorrow with the same id — the line
        /// names that so the stall reads as a stall, not a loss.</summary>
        private static void LogBlockedBatches(BakeryWholesaleDailyInputs inputs, BakeryWholesaleDailyResult result, int dayIndex, List<string> diag)
        {
            if (inputs?.Bakery == null || inputs.ProductionDriver == null) return;
            string cause = CauseFromRefusal(result.Refusal);
            int logged = 0;
            int omitted = 0;
            foreach (string batchId in inputs.ProductionDriver.PlannedBatchIds)
            {
                if (string.IsNullOrWhiteSpace(batchId)) continue;
                BakeryDoughBatch batch = null;
                foreach (var candidate in inputs.Bakery.Batches)
                {
                    if (candidate != null && string.Equals(candidate.BatchId, batchId, StringComparison.Ordinal))
                    {
                        batch = candidate;
                        break;
                    }
                }
                if (batch == null || !batch.IsActive) continue;
                if (logged >= 12)
                {
                    omitted++;
                    continue;
                }
                int coveredUnits = 0;
                foreach (var candidate in inputs.Bakery.Batches)
                {
                    if (candidate == null || !candidate.IsActive) continue;
                    if (!string.Equals(candidate.ProductId, batch.ProductId, StringComparison.Ordinal)) continue;
                    coveredUnits += BakeryBreadCatalog.BreadYieldPerBatch;
                }
                string accountId = string.Empty;
                foreach (var account in inputs.Bakery.WholesaleAccounts)
                {
                    if (account == null || !account.IsActive) continue;
                    if (!string.Equals(account.ProductId, batch.ProductId, StringComparison.Ordinal)) continue;
                    accountId = account.AccountId;
                    break;
                }
                diag.Add($"[BakeryProduction] batch {batch.BatchId} blocked day {dayIndex} ({cause}), planned {batch.ScheduledDayIndex}, covered {coveredUnits}u for account {accountId}, producible: no ({cause}), retries tomorrow with the same batch id");
                logged++;
            }
            if (omitted > 0)
                diag.Add($"[BakeryProduction] ... {omitted} more blocked batch(es) omitted (12-per-day diagnostics cap)");
        }

        /// <summary>Plan 04 Slice 4: the blocked-batch cause is the
        /// Precheck refusal's own wording, reduced to its named input —
        /// never a new refusal reason invented here. Falls back to the
        /// refusal text itself when no input keyword matches.</summary>
        private static string CauseFromRefusal(string refusal)
        {
            if (string.IsNullOrWhiteSpace(refusal)) return "unknown";
            string lower = refusal.ToLowerInvariant();
            if (lower.Contains("person budget")) return "budget";
            if (lower.Contains("flour")) return "flour";
            if (lower.Contains("notions")) return "notions";
            if (lower.Contains("fuel")) return "fuel";
            if (lower.Contains("kit")) return "kit";
            if (lower.Contains("oven")) return "oven";
            return refusal;
        }

        /// <summary>Plan 04 Slice 4: cumulative actuals reconciliation.
        /// Fills the result's cumulative fields (threaded priors + today)
        /// and, on any day a batch baked, appends the reconciliation line
        /// after the per-batch economics line. When the caller threads
        /// totals the line is cumulative; otherwise it is the honest
        /// per-day actuals line — never a cumulative label on per-day
        /// figures. The standard figures come from the same inputs as
        /// the economics line (store flour price, notions registration,
        /// account price); when any standard component is unknown the
        /// line says so instead of inventing a standard. No price is
        /// changed; the line reports both definitions side by side so
        /// the -20c standard vs actual gap is explained by carryover in
        /// the open.</summary>
        private static BakeryWholesaleDailyResult FinishWithActualsLine(
            BakeryWholesaleDailyInputs inputs, BakeryWholesaleDailyResult result,
            int wholesaleRevenueTodayCents, List<string> diag)
        {
            int flourCashToday = result.FlourSacksPurchased * Math.Max(0, inputs.StoreFlourUnitPriceCents);
            int notionsPricePerUnit = inputs.NotionsRegistration != null
                ? Math.Max(0, inputs.NotionsRegistration.UnitGoodsCostCents) : 0;
            int notionsCashToday = result.NotionsUnitsFed * notionsPricePerUnit;
            result.CumulativeBatchesBaked = inputs.PriorCumulativeBatchesBaked + result.BatchesBaked;
            result.CumulativeFlourCashSpentCents = inputs.PriorCumulativeFlourCashSpentCents + flourCashToday;
            result.CumulativeFlourSacksPurchased = inputs.PriorCumulativeFlourSacksPurchased + result.FlourSacksPurchased;
            result.CumulativeFlourPoundsPurchased = inputs.PriorCumulativeFlourPoundsPurchased + result.FlourPoundsPurchased;
            result.CumulativeNotionsCashSpentCents = inputs.PriorCumulativeNotionsCashSpentCents + notionsCashToday;
            result.CumulativeWholesaleRevenueCents = inputs.PriorCumulativeWholesaleRevenueCents + wholesaleRevenueTodayCents;
            if (result.BatchesBaked <= 0) return result;

            int batches = inputs.CumulativeActualsThreaded ? result.CumulativeBatchesBaked : result.BatchesBaked;
            int flour = inputs.CumulativeActualsThreaded ? result.CumulativeFlourCashSpentCents : flourCashToday;
            int sacks = inputs.CumulativeActualsThreaded ? result.CumulativeFlourSacksPurchased : result.FlourSacksPurchased;
            int pounds = inputs.CumulativeActualsThreaded ? result.CumulativeFlourPoundsPurchased : result.FlourPoundsPurchased;
            int notions = inputs.CumulativeActualsThreaded ? result.CumulativeNotionsCashSpentCents : notionsCashToday;
            int revenue = inputs.CumulativeActualsThreaded ? result.CumulativeWholesaleRevenueCents : wholesaleRevenueTodayCents;
            string label = inputs.CumulativeActualsThreaded ? "cumulative actuals" : "day actuals";
            double actualPerBatch = batches > 0 ? (double)(flour + notions) / batches : 0.0;
            double revenuePerBatch = batches > 0 ? (double)revenue / batches : 0.0;
            double marginPerBatch = revenuePerBatch - actualPerBatch;
            string vsStandard;
            int standardCostPerBatch = -1;
            int standardMarginPerBatch = 0;
            if (inputs.Bakery != null
                && inputs.StoreFlourUnitPriceCents > 0
                && inputs.NotionsRegistration != null && inputs.NotionsRegistration.UnitGoodsCostCents > 0)
            {
                int standardRevenuePerBatch = AccountPricePerUnit(inputs.Bakery, inputs.StoreBusinessId)
                    * BakeryBreadCatalog.BreadYieldPerBatch;
                if (standardRevenuePerBatch > 0)
                {
                    standardCostPerBatch = BakeryFlourSupplierComparison.SacksPerBatch * inputs.StoreFlourUnitPriceCents
                        + BakeryBreadCatalog.StandardNotionsPerBatch * inputs.NotionsRegistration.UnitGoodsCostCents;
                    standardMarginPerBatch = standardRevenuePerBatch - standardCostPerBatch;
                }
            }
            vsStandard = standardCostPerBatch >= 0
                ? $"vs standard {standardCostPerBatch}c/batch (margin {marginPerBatch:0.0}c/batch actual vs {standardMarginPerBatch}c standard)"
                : "(standard unknown)";
            diag.Add($"[BakeryProduction] {label}: {batches} batch(es) baked, flour {flour}c ({sacks} sacks, {pounds} lb), notions {notions}c, revenue {revenue}c, actual {actualPerBatch:0.0}c/batch {vsStandard}");
            return result;
        }
    }
}
