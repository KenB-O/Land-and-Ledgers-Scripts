// Review 2 vertical slice Pass D1: bakery -> General Store wholesale,
// the caller-side bridge the second read-only review found missing.
//
// The pieces already existed, in different representations, and no
// single caller chained them: the bakery bakes into BakeryShopRuntime
// shelf lots (BakeryCode id bake.bread-loaf), Pass 3 wholesale intake
// receives a lot into a Pass3StoreCatalog BACK pool only, Pass 7
// settlement moves business-to-business cash, the Pass 7 shelf move
// moves catalog stock BACK -> shelf as a named Pass 2 task, and the
// live store sells from BusinessRuntimeState category stock through
// IGeneralStoreTradingPort. Until this driver, a baked loaf could be
// sold through the bakery adapter (Pass 10) or sit in a Pass 3 catalog
// (Pass 7), but it could not travel bakery shelf -> store shelf ->
// household purchase on production classes, because nobody connected
// the Pass 3 shelf to the live category pool.
//
// This driver is that connection, in two explicit steps, so each
// failure mode has a named, recoverable state instead of a partial
// stock or cash move:
//
//   Step 1, TransferToShelf: bakery shelf lot -> Pass 3 BACK ->
//   cash settlement -> retail-price decision -> Pass 3 shelf. The
//   returned task descriptor (gs.move-back-to-shelf, 2 min/lot) is
//   what Codex issues through TaskAuthority; this driver performs
//   the stock move, not the person-time, exactly as Pass 7 states.
//   Step 2, StockShelfToLive: Pass 3 shelf units -> live store
//   category pool through IGeneralStoreTradingPort. Only on a
//   fully accepted ReceiveStock are Pass 3 shelf units consumed,
//   so a refused stocking leaves the units on the Pass 3 shelf,
//   never lost and never double-counted.
//
// Pre-checks run BEFORE any stock or cash moves, in this order:
// map translation, shelf availability, baked-day age (Pass 3 rule:
// transfer day at most one day past baked day, never stale),
// payer balance against the wholesale amount, and the retail-price
// guard (retail must be positive and different from wholesale
// cost). A refusal before intake moves nothing.
//
// One failure mode is deliberately NOT hidden: if cash settlement
// fails after intake (possible only with a port that can refuse
// credit; the Pass 9 production port cannot fail for a non-null
// runtime and a non-negative amount), the intake stands in BACK
// with its settlement record unsettled, and no shelf or live
// movement happens. The lot cannot be re-intaken (the Pass 3
// duplicate guard refuses it), so bread is never double-counted;
// the caller settles the returned record and then calls
// MoveSettledLotToShelf to continue. That recovery path is tested.
//
// Prices, conversions, and the category are caller parameters,
// never canon: wholesale and retail prices are test parameters
// supplied by the caller (the store definition's bread baseline
// 5c and wholesale 2c are the evidence a caller may pass in, not
// defaults of this driver), the bakery->store id translation comes
// from Review2ItemSkuMap (Direct, loaf to loaf), and the live
// category id is supplied by the caller (staple_food in the
// harness, because bread is a staple_food item in the definition).
// Freight charges are NOT settled here: this driver moves goods
// and goods cash only. If a carrier carried the leg, freight
// revenue is settled separately by Series2CarrierSettlement on
// delivery acceptance, exactly as Pass 8/10 prove; this driver is
// the ReceiveShipment callback a carrier leg calls, not a second
// freight settlement.
//
// Plain C#, no UnityEngine references, Roslyn-testable. It does
// NOT change any existing adapter, catalog, settlement, or shelf
// behaviour; it only calls them, in the order above.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.Economy.Ordering
{
    /// <summary>Caller inputs for one bakery -> store wholesale transfer. All prices and ids are caller data.</summary>
    public sealed class Review2WholesaleTransferOptions
    {
        public BakeryShopRuntime Bakery;
        public string BakeryProductId = string.Empty;
        public int Units;
        public int WholesalePriceCents;
        public int RetailPriceCents;
        public int TransferDayIndex;
        public Pass3WholesaleIntake Intake;
        public Pass3StoreCatalog StoreCatalog;
        public IBusinessCashPort StoreCashPort;
        public IBusinessCashPort BakeryCashPort;
        public string StoreBusinessId = string.Empty;
        public string BakeryBusinessId = string.Empty;
    }

    /// <summary>Result of a wholesale transfer step. A failed step moves nothing unless IntakeCompleted is true, which names the one recoverable partial state (see the file header).</summary>
    public sealed class Review2WholesaleTransferResult
    {
        public bool Transferred;
        public bool IntakeCompleted;
        public bool Settled;
        public bool OnShelf;
        public int Units;
        public string BakeryLotId = string.Empty;
        public string StoreProductId = string.Empty;
        public string Condition = string.Empty;
        public int AmountCents;
        public Pass3WholesaleSettlement Settlement;
        public string TaskActionId = string.Empty;
        public int TaskMinutesPerLot;
        public string Refusal = string.Empty;
    }

    /// <summary>Result of stocking Pass 3 shelf units into the live store category pool.</summary>
    public sealed class Review2StockingResult
    {
        public bool Stocked;
        public int Units;
        public int LivePoolUnitsAfter;
        public string Refusal = string.Empty;
    }

    /// <summary>Review 2 Pass D1: bakery shelf -> store shelf -> live store pool, on production classes only.</summary>
    public static class Review2BakeryToStoreWholesaleDriver
    {
        /// <summary>
        /// Step 1: moves a baked lot from the bakery shelf into the
        /// store's Pass 3 shelf, settling goods cash on the way.
        /// Pre-checks (map, shelf, age, payer balance, retail guard)
        /// run before any mutation; a refusal there moves nothing.
        /// </summary>
        public static Review2WholesaleTransferResult TransferToShelf(
            Review2WholesaleTransferOptions options, List<string> diag)
        {
            var result = new Review2WholesaleTransferResult();
            string refusal = Validate(options);
            if (refusal != null) return Refuse(result, refusal, diag);
            if (options.RetailPriceCents <= 0 || options.RetailPriceCents == options.WholesalePriceCents)
            {
                return Refuse(result,
                    $"Review2Wholesale: retail price {options.RetailPriceCents}c must be positive and different from wholesale cost {options.WholesalePriceCents}c — the owner must set a retail price before any shelf sale; nothing moved.",
                    diag);
            }

            Review2ItemTranslation translation = Review2ItemSkuMap.TryTranslate(
                Review2ItemSpace.BakeryCode, options.BakeryProductId, Review2ItemSpace.Store, diag);
            if (translation == null)
            {
                return Refuse(result,
                    $"Review2Wholesale: bakery product '{options.BakeryProductId}' has no store translation in Review2ItemSkuMap — nothing moved, never guessed.",
                    diag);
            }

            BakeryBreadLot lot = OldestSellableLot(options.Bakery, options.BakeryProductId);
            if (lot == null)
            {
                return Refuse(result,
                    $"Review2Wholesale: bakery has no sellable shelf lot of '{options.BakeryProductId}' — nothing moved.",
                    diag);
            }
            if (lot.UnitCount < options.Units)
            {
                return Refuse(result,
                    $"Review2Wholesale: bakery shelf lot holds {lot.UnitCount}u, asked {options.Units}u — nothing moved, never partial.",
                    diag);
            }
            int ageDays = options.TransferDayIndex - lot.BakedDayIndex;
            if (ageDays < 0)
            {
                return Refuse(result,
                    $"Review2Wholesale: transfer day {options.TransferDayIndex} is before baked day {lot.BakedDayIndex} — nothing moved.",
                    diag);
            }
            if (ageDays > 1)
            {
                return Refuse(result,
                    $"Review2Wholesale: bakery lot is stale ({ageDays} days past baked day) — refused, not converted, nothing moved.",
                    diag);
            }

            int amount = options.Units * options.WholesalePriceCents;
            if (options.StoreCashPort.BalanceCents < amount)
            {
                return Refuse(result,
                    $"Review2Wholesale: store holds {options.StoreCashPort.BalanceCents}c, cannot settle {amount}c wholesale — nothing moved.",
                    diag);
            }

            // All pre-checks passed: take from the bakery shelf first,
            // so a later refusal can restore exactly these units.
            int taken = lot.TakeUnits(options.Units);
            if (taken != options.Units)
            {
                return Refuse(result,
                    $"Review2Wholesale: bakery shelf gave {taken}u, asked {options.Units}u — nothing settled.",
                    diag);
            }

            string lotId = lot.LotId.ToString();
            Pass3WholesaleIntakeResult intake = options.Intake.Receive(
                lotId, translation.ToId, options.Units, options.WholesalePriceCents,
                lot.BakedDayIndex, options.TransferDayIndex, options.StoreCatalog,
                options.StoreBusinessId, options.BakeryBusinessId, diag);
            if (intake == null)
            {
                lot.UnitCount += taken;
                return Refuse(result,
                    $"Review2Wholesale: wholesale intake refused for lot '{lotId}' — bakery shelf restored, nothing settled.",
                    diag);
            }

            // Pass 3 labels the settlement generically; the caller
            // supplies the real business ids for the record.
            intake.Settlement.PayerBusinessId = string.IsNullOrWhiteSpace(options.StoreBusinessId)
                ? intake.Settlement.PayerBusinessId : options.StoreBusinessId;
            intake.Settlement.PayeeBusinessId = string.IsNullOrWhiteSpace(options.BakeryBusinessId)
                ? intake.Settlement.PayeeBusinessId : options.BakeryBusinessId;

            result.IntakeCompleted = true;
            result.BakeryLotId = lotId;
            result.StoreProductId = translation.ToId;
            result.Condition = intake.Condition;
            result.Units = options.Units;
            result.AmountCents = amount;
            result.Settlement = intake.Settlement;

            Series2WholesaleSettlementResult settled = Series2WholesaleSettlement.SettleCash(
                intake.Settlement,
                (IBusinessIdentityCashPort)options.StoreCashPort,
                (IBusinessIdentityCashPort)options.BakeryCashPort,
                diag);
            if (!settled.Settled)
            {
                result.Refusal = "Review2Wholesale: intake stands in BACK, settlement unsettled (" + settled.Refusal +
                    ") — no shelf or live movement happened; settle the returned record, then call MoveSettledLotToShelf. The lot cannot be re-intaken (duplicate guard).";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }
            result.Settled = true;

            Review2WholesaleTransferResult moved = MoveSettledLotToShelf(
                options.StoreCatalog, translation.ToId, options.Units, options.RetailPriceCents, diag);
            result.OnShelf = moved.OnShelf;
            result.TaskActionId = moved.TaskActionId;
            result.TaskMinutesPerLot = moved.TaskMinutesPerLot;
            if (!moved.OnShelf)
            {
                result.Refusal = "Review2Wholesale: settled in cash, but the shelf move failed (" + moved.Refusal +
                    ") — stock stands in BACK and can be moved by retrying MoveSettledLotToShelf.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }

            result.Transferred = true;
            if (diag != null) diag.Add($"Review2Wholesale: lot '{lotId}' ({options.Units}u {translation.ToId}, {intake.Condition}) settled {amount}c and moved to the store shelf (task {result.TaskActionId}, {result.TaskMinutesPerLot} min/lot to be issued through TaskAuthority).");
            return result;
        }

        /// <summary>
        /// Continues a transfer whose intake and settlement are done
        /// (including a transfer recovered from a settlement failure):
        /// sets the retail price and moves catalog stock BACK -> shelf.
        /// Refuses when the line is missing, the retail guard fails,
        /// or back stock is short; moves nothing on refusal.
        /// </summary>
        public static Review2WholesaleTransferResult MoveSettledLotToShelf(
            Pass3StoreCatalog catalog, string productId, int units, int retailPriceCents, List<string> diag)
        {
            var result = new Review2WholesaleTransferResult { StoreProductId = productId, Units = units };
            if (catalog == null || string.IsNullOrWhiteSpace(productId) || units <= 0)
            {
                return Refuse(result, "Review2Wholesale: shelf move needs a catalog, a product id, and a positive quantity.", diag);
            }
            Pass3ProductLine line = catalog.Find(productId);
            if (line == null)
            {
                return Refuse(result, $"Review2Wholesale: product '{productId}' has no catalog line — nothing moved.", diag);
            }
            if (!line.SetPrice(retailPriceCents, diag))
            {
                return Refuse(result, $"Review2Wholesale: retail price {retailPriceCents}c could not be set for '{productId}'.", diag);
            }
            string guard = Series2WholesaleShelfMove.RequireRetailPriceSet(line);
            if (guard != null)
            {
                return Refuse(result, "Review2Wholesale: " + guard, diag);
            }
            Series2ShelfMoveResult moved = Series2WholesaleShelfMove.MoveToShelf(catalog, productId, units, diag);
            if (!moved.Moved)
            {
                return Refuse(result, $"Review2Wholesale: shelf move refused for '{productId}' ({moved.Refusal}).", diag);
            }
            result.OnShelf = true;
            result.TaskActionId = moved.TaskActionId;
            result.TaskMinutesPerLot = moved.TaskMinutesPerLot;
            return result;
        }

        /// <summary>
        /// Step 2: stocks Pass 3 shelf units into the live store
        /// category pool through the production trading port. The
        /// category receivable is checked first; shelf units are
        /// consumed only after a fully accepted ReceiveStock, and
        /// are restored if ReceiveStock refuses, so a refused
        /// stocking loses nothing and double-counts nothing.
        /// </summary>
        public static Review2StockingResult StockShelfToLive(
            Pass3StoreCatalog catalog, string productId, int units, string categoryId,
            IGeneralStoreTradingPort storePort, List<string> diag)
        {
            var result = new Review2StockingResult { Units = units };
            if (catalog == null || string.IsNullOrWhiteSpace(productId) || units <= 0
                || string.IsNullOrWhiteSpace(categoryId) || storePort == null)
            {
                result.Refusal = "Review2Wholesale: stocking needs a catalog, a product id, a positive quantity, a category id, and a store port.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }
            Pass3ProductLine line = catalog.Find(productId);
            if (line == null)
            {
                result.Refusal = $"Review2Wholesale: product '{productId}' has no catalog line — nothing stocked.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }
            if (line.ShelfUnits < units)
            {
                result.Refusal = $"Review2Wholesale: catalog shelf holds {line.ShelfUnits}u '{productId}', asked {units}u — nothing stocked, never partial.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }
            if (storePort.CategoryReceivableUnits(categoryId) < units)
            {
                result.Refusal = $"Review2Wholesale: live category '{categoryId}' can receive {storePort.CategoryReceivableUnits(categoryId)}u, asked {units}u — nothing stocked; shelf units remain for a later stocking.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }
            if (!line.ConsumeShelfUnits(units, diag))
            {
                result.Refusal = $"Review2Wholesale: could not consume {units}u '{productId}' from the catalog shelf — nothing stocked.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }
            string stockProblem = storePort.ReceiveStock(categoryId, units,
                $"wholesale bakery lot, {productId}");
            if (stockProblem != null)
            {
                line.ShelfUnits += units;
                result.Refusal = $"Review2Wholesale: live stocking refused ({stockProblem}) — catalog shelf restored, nothing stocked.";
                if (diag != null) diag.Add(result.Refusal);
                return result;
            }
            result.Stocked = true;
            result.LivePoolUnitsAfter = storePort.CategoryStockUnits(categoryId);
            if (diag != null) diag.Add($"Review2Wholesale: stocked {units}u '{productId}' into live category '{categoryId}' (pool now {result.LivePoolUnitsAfter}u); catalog shelf no longer holds them.");
            return result;
        }

        private static BakeryBreadLot OldestSellableLot(BakeryShopRuntime bakery, string productId)
        {
            if (bakery == null || bakery.BreadShelf == null) return null;
            BakeryBreadLot oldest = null;
            foreach (BakeryBreadLot lot in bakery.BreadShelf)
            {
                if (lot == null || !lot.CanSell) continue;
                if (!string.Equals(lot.ProductId, productId, StringComparison.OrdinalIgnoreCase)) continue;
                if (oldest == null || lot.BakedDayIndex < oldest.BakedDayIndex) oldest = lot;
            }
            return oldest;
        }

        private static string Validate(Review2WholesaleTransferOptions options)
        {
            if (options == null) return "Review2Wholesale: transfer options are required.";
            if (options.Bakery == null) return "Review2Wholesale: a bakery runtime is required.";
            if (string.IsNullOrWhiteSpace(options.BakeryProductId)) return "Review2Wholesale: a bakery product id is required.";
            if (options.Units <= 0) return "Review2Wholesale: transfer needs a positive quantity.";
            if (options.WholesalePriceCents < 0) return "Review2Wholesale: wholesale price cannot be negative.";
            if (options.Intake == null) return "Review2Wholesale: a wholesale intake instance is required (its delivered-lot guard must be reused and save-restored, never rebuilt per call).";
            if (options.StoreCatalog == null) return "Review2Wholesale: a store catalog is required.";
            if (options.StoreCashPort is not IBusinessIdentityCashPort storeCash
                || options.BakeryCashPort is not IBusinessIdentityCashPort bakeryCash)
                return "Review2Wholesale: store and bakery cash ports must identify their owning Businesses before intake.";
            if (string.IsNullOrWhiteSpace(options.StoreBusinessId)
                || string.IsNullOrWhiteSpace(options.BakeryBusinessId)
                || string.Equals(options.StoreBusinessId.Trim(), options.BakeryBusinessId.Trim(), StringComparison.OrdinalIgnoreCase))
                return "Review2Wholesale: distinct store and bakery Business ids are required before intake.";
            if (!string.Equals(storeCash.BusinessId, options.StoreBusinessId.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(bakeryCash.BusinessId, options.BakeryBusinessId.Trim(), StringComparison.OrdinalIgnoreCase))
                return "Review2Wholesale: cash-port Business identities must match the named store and bakery before intake.";
            return null;
        }

        private static Review2WholesaleTransferResult Refuse(
            Review2WholesaleTransferResult result, string refusal, List<string> diag)
        {
            result.Refusal = refusal;
            if (diag != null) diag.Add(refusal);
            return result;
        }
    }
}
