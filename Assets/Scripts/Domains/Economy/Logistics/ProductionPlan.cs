// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice P6 — production planning foundation (directive P6).
// Reusable planning logic for a producer such as Glen Pierce Bakery:
// projected finished-goods demand connects to required production
// batches through the recorded recipe only, then to recipe inputs,
// existing ingredient stock, ingredients committed to batches, incoming
// supplies, production lead times, expected spoilage, and cash and
// capacity constraints. A planning and query layer only: the plan
// recommends batches; it orders nothing, reserves nothing in any live
// authority, and changes no Bakery policy.
//
// WHERE EVERY FIGURE COMES FROM (no figure is invented):
//   - Demand: the caller's projected finished-goods demand, or the
//     Slice 6 bridge (ProductionInputs.FromObservation), which carries
//     the total observed demand over the window — recorded history,
//     never a forecast beyond it.
//   - Recipe: ProductionRecipe.BakeryBreadLoaf, read from
//     BakeryBreadCatalog (BakeryBreadCatalog.cs:103-121 for the
//     per-batch constants BreadFlourPerBatch 12, BreadYieldPerBatch
//     24, StandardNotionsPerBatch 1; GetSpec at lines 182-207):
//     12 lb flour plus 1 notion per batch, 24 loaves per batch. Any
//     other recipe is caller-supplied; this type invents no recipe.
//   - Stock: caller-supplied per ingredient. Where Slice 5 lot states
//     are supplied, usable stock is the Slice 6 discount
//     (ReserveProjection.UsableStockFromLots): a lot past its useful
//     life (remaining life Known and zero or less) or in an Unsafe
//     safety state counts zero; Suspect and Stale lots count, because
//     no authored rule discounts them (see the ReserveProjection
//     header). Where no lot states are supplied, the caller's plain
//     stock figure is used and named as caller-supplied.
//   - Committed: caller-supplied per ingredient, subtracted BEFORE
//     available stock is computed — an ingredient committed to a
//     planned batch is not available for another.
//   - Incoming: caller-supplied per ingredient, counted ONLY where
//     confirmed. A P5 SupplierOption is not a confirmed delivery:
//     IncomingSupply.FromSupplierOption takes the confirmation from
//     the caller, and an unconfirmed option adds nothing. Millbrook
//     and catalog quantity-available are Unknown pre-order (P5), so a
//     confirmed delivery is a caller fact, never derived from a quote.
//   - Spoilage: caller-supplied expected spoilage per ingredient, in
//     addition to the lot discount above (the forecast covers stock
//     that will spoil before use; the lot discount covers stock
//     already past life or unsafe — the two are not the same units,
//     and neither is rescaled or double-counted: spoilage discounts
//     usable stock, incoming supplies are fresh and are not
//     discounted by it).
//   - Cash: the business's own cash records, caller-supplied
//     (available cash and cost per batch). No authored per-batch cost
//     exists — the plan never derives one from a sack price, because
//     no authored conversion turns a 10 lb sack price into a per-lb
//     bakery-flour cost — so an Unknown cost means cash caps nothing
//     and the axis is named incomplete.
//   - Capacity: caller-supplied, computed from the F2 evaluator
//     (PhysicalCapacityContracts.cs BusinessStorage leg: Total minus
//     Occupied is the headroom, in the same units as the batch
//     footprint). ProductionCapacityConstraint.FromStorageHeadroom
//     turns headroom and footprint per batch into a maximum batch
//     count (floor — only whole batches count). This type references
//     no F2 type itself; the caller keeps units coherent.
//   - Lead times: caller-supplied (no authored lead time exists
//     anywhere in v3.1). An Unknown lead time makes the plan
//     incomplete on the lead-time axis and leaves the completion and
//     order days Unknown — never an invented day.
//
// RULES, stated once:
//   - Required batches = ceil(demand / units per batch) where the
//     recipe states units per batch; a shortfall is never rounded
//     down (a fractional batch cannot be baked, and one batch short
//     is still short).
//   - Available per ingredient = max(0, usable stock - expected
//     spoilage - committed) + confirmed incoming. Shortfall =
//     max(0, needed - available); surplus = max(0, available -
//     needed); both are named per ingredient with exact units.
//   - Feasible batches = min(required, stock-feasible per
//     ingredient, cash-feasible, capacity-feasible), where
//     stock-feasible is floor(available / per-batch units) and
//     cash-feasible is floor(cash / cost per batch). Recommended
//     batches = feasible batches.
//   - Unknown handling, following the Slice 6 exclusion pattern: a
//     core Unknown (demand, recipe, or an ingredient's stock) makes
//     the computation that depends on it Unknown; a bounding Unknown
//     (cash, capacity, lead time, commitments, spoilage) is EXCLUDED
//     from the figure (counted as no cap, or zero) and NAMED in the
//     reasoning, and the plan is marked incomplete on that axis
//     (IncompleteAxes) rather than planned on an invented value.
//     IsComplete is true only when every axis is Known and every
//     computed figure is Known.
//
// GAPS, recorded not papered over:
//   - Oven fuel is a firing input, not part of the dough recipe
//     the runtime dispenses at prep: BakeryBreadCatalog
//     .FuelUnitsPerFiring is 1 cordwood unit per firing
//     (BakeryBreadCatalog.cs:165), and the staged chain's
//     InputRequirements list flour, notions, AND fuel
//     (BakeryStagedProductionChain.cs:492-494) — but the runtime's
//     prep step dispenses flour and notions only
//     (BakeryShopRuntime.cs PrepBatchToProofing: "Prep: flour and
//     notions into the batch's custody"). The recipe here is the
//     prep recipe (flour plus notions); fuel planning is a
//     downstream step, not an invented third recipe input.
//   - No authored rule maps Stale condition alone to zero usable
//     stock (Slice 6); a Stale lot with Unknown remaining life is
//     counted, and the per-lot reason says so.
//   - The plan knows nothing about which supplier to order from —
//     that is the P5 ReplenishmentPlan's job, from a Slice 6 reserve.
//     This plan states the shortfall; it never selects a supplier.
//
// UNWIRED BY DESIGN: plain data plus a pure function. No flow
// consults this type, no driver is activated, no shopping loop,
// Bakery driver, or live flow is touched. It reads the Slice 5
// states and the Slice 6 structs only from inside its own owning
// directory (Domains/Economy/Logistics), so no earlier
// UnwiredSystems.md row is voided; it names BakeryBreadCatalog only
// for the recorded recipe, the same dependency Slice 3's read model
// already carries.

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;

namespace LandLedgers.Economy.Logistics
{
    /// <summary>
    /// One recipe input: an ingredient item id and the units of it
    /// one batch consumes, in the ingredient's own unit (lb of flour,
    /// notions). A struct, like the other small record types in this
    /// layer.
    /// </summary>
    public struct ProductionRecipeIngredient
    {
        public ProductionRecipeIngredient(string itemId, int unitsPerBatch)
        {
            ItemId = itemId ?? string.Empty;
            UnitsPerBatch = unitsPerBatch;
        }

        public string ItemId { get; }
        public int UnitsPerBatch { get; }
    }

    /// <summary>
    /// A production recipe: the finished product, the finished units
    /// one batch yields, and the inputs one batch consumes. A struct:
    /// a default instance is invalid (no yield, no inputs), which is
    /// the honest default — no recipe is ever assumed.
    /// </summary>
    public struct ProductionRecipe
    {
        public ProductionRecipe(string productId, int yieldUnitsPerBatch,
            IReadOnlyList<ProductionRecipeIngredient> ingredients)
        {
            ProductId = productId ?? string.Empty;
            YieldUnitsPerBatch = yieldUnitsPerBatch;
            Ingredients = ingredients ?? new List<ProductionRecipeIngredient>().AsReadOnly();
        }

        public string ProductId { get; }
        public int YieldUnitsPerBatch { get; }
        public IReadOnlyList<ProductionRecipeIngredient> Ingredients { get; }

        /// <summary>
        /// Whether the recipe can plan batches: a positive yield and
        /// at least one ingredient with a positive per-batch quantity.
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (YieldUnitsPerBatch <= 0) return false;
                if (Ingredients == null) return false;
                foreach (ProductionRecipeIngredient ingredient in Ingredients)
                {
                    if (!string.IsNullOrWhiteSpace(ingredient.ItemId) && ingredient.UnitsPerBatch > 0)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// The recorded Bakery recipe, read from the catalog
        /// (BakeryBreadCatalog.GetSpec): 12 lb flour plus 1 notion per
        /// batch, 24 loaves per batch.
        /// </summary>
        public static ProductionRecipe BakeryBreadLoaf()
        {
            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(BakeryBreadCatalog.BreadLoafId);
            return new ProductionRecipe(
                BakeryBreadCatalog.BreadLoafId,
                spec.YieldUnitsPerBatch,
                new List<ProductionRecipeIngredient>
                {
                    new ProductionRecipeIngredient(BakeryBreadCatalog.FlourItemId, spec.FlourUnitsPerBatch),
                    new ProductionRecipeIngredient(BakeryBreadCatalog.NotionsItemId, spec.NotionUnitsPerBatch),
                }.AsReadOnly());
        }
    }

    /// <summary>
    /// One ingredient's stock on hand. Where Slice 5 lot states are
    /// supplied they govern usable stock (the Slice 6 discount); where
    /// none are supplied, Units is the caller-supplied stock figure.
    /// A struct, like the other small record types in this layer.
    /// </summary>
    public struct IngredientStockEntry
    {
        public IngredientStockEntry(string itemId, ReserveQuantity units,
            IReadOnlyList<PerishableLotState> lots)
        {
            ItemId = itemId ?? string.Empty;
            Units = units;
            Lots = lots;
        }

        public string ItemId { get; }
        public ReserveQuantity Units { get; }
        public IReadOnlyList<PerishableLotState> Lots { get; }
    }

    /// <summary>Units of one ingredient already committed to a planned batch.</summary>
    public struct IngredientCommitment
    {
        public IngredientCommitment(string itemId, ReserveQuantity units)
        {
            ItemId = itemId ?? string.Empty;
            Units = units;
        }

        public string ItemId { get; }
        public ReserveQuantity Units { get; }
    }

    /// <summary>
    /// One incoming supply of one ingredient. IsConfirmed is a caller
    /// fact: a P5 supplier option is not a confirmed delivery, and an
    /// unconfirmed supply adds nothing to available stock.
    /// </summary>
    public struct IncomingSupply
    {
        public IncomingSupply(string itemId, ReserveQuantity units, bool isConfirmed,
            string sourceSupplierId, string sourceDescription)
        {
            ItemId = itemId ?? string.Empty;
            Units = units;
            IsConfirmed = isConfirmed;
            SourceSupplierId = sourceSupplierId ?? string.Empty;
            SourceDescription = sourceDescription ?? string.Empty;
        }

        public string ItemId { get; }
        public ReserveQuantity Units { get; }
        public bool IsConfirmed { get; }
        public string SourceSupplierId { get; }
        public string SourceDescription { get; }

        /// <summary>
        /// Builds an incoming supply from a P5 supplier option. The
        /// confirmation is supplied by the caller — the option itself
        /// carries no confirmation, and a quoted option is not a
        /// confirmed delivery until the caller says it is.
        /// </summary>
        public static IncomingSupply FromSupplierOption(SupplierOption option,
            bool isConfirmed, string sourceDescription) =>
            new IncomingSupply(option.ItemId, option.QuantityUnits, isConfirmed,
                option.SupplierId, sourceDescription ?? string.Empty);
    }

    /// <summary>Units of one ingredient expected to spoil before use.</summary>
    public struct ExpectedSpoilageEntry
    {
        public ExpectedSpoilageEntry(string itemId, ReserveQuantity spoiledUnits)
        {
            ItemId = itemId ?? string.Empty;
            SpoiledUnits = spoiledUnits;
        }

        public string ItemId { get; }
        public ReserveQuantity SpoiledUnits { get; }
    }

    /// <summary>
    /// Production lead times in days, caller-supplied — no authored
    /// lead time exists anywhere in v3.1, so Unknown is the normal
    /// case until a caller supplies one. The ingredient lead time is
    /// how long supplies take to arrive; the production lead time is
    /// how long a batch takes once its inputs are on hand.
    /// </summary>
    public struct ProductionLeadTimes
    {
        public ProductionLeadTimes(LeadTimeDays ingredientLeadTimeDays,
            LeadTimeDays productionLeadTimeDays)
        {
            IngredientLeadTimeDays = ingredientLeadTimeDays;
            ProductionLeadTimeDays = productionLeadTimeDays;
        }

        public LeadTimeDays IngredientLeadTimeDays { get; }
        public LeadTimeDays ProductionLeadTimeDays { get; }
    }

    /// <summary>
    /// The cash constraint, from the business's own cash records:
    /// available cash and the caller-supplied cost of one batch. No
    /// authored per-batch cost exists, so an Unknown cost means cash
    /// caps nothing and the axis is named incomplete.
    /// </summary>
    public struct ProductionCashConstraint
    {
        public ProductionCashConstraint(CentsAmount availableCashCents, CentsAmount costPerBatchCents)
        {
            AvailableCashCents = availableCashCents;
            CostPerBatchCents = costPerBatchCents;
        }

        public CentsAmount AvailableCashCents { get; }
        public CentsAmount CostPerBatchCents { get; }
    }

    /// <summary>
    /// The capacity constraint, computed by the caller from the F2
    /// evaluator (BusinessStorage leg: Total minus Occupied is the
    /// headroom, in the same units as the batch footprint). MaxBatches
    /// is the whole-batch count that fits; Unknown means capacity
    /// caps nothing and the axis is named incomplete.
    /// </summary>
    public struct ProductionCapacityConstraint
    {
        public ProductionCapacityConstraint(ReserveQuantity maxBatches,
            ReserveQuantity availableStorageUnits, ReserveQuantity footprintPerBatchUnits)
        {
            MaxBatches = maxBatches;
            AvailableStorageUnits = availableStorageUnits;
            FootprintPerBatchUnits = footprintPerBatchUnits;
        }

        public ReserveQuantity MaxBatches { get; }
        public ReserveQuantity AvailableStorageUnits { get; }
        public ReserveQuantity FootprintPerBatchUnits { get; }

        /// <summary>
        /// Turns F2 headroom and a per-batch footprint (both in the
        /// same units) into a maximum batch count: floor, because only
        /// whole batches count. Unknown where either input is Unknown
        /// or the footprint is not positive — never an assumed fit.
        /// </summary>
        public static ProductionCapacityConstraint FromStorageHeadroom(
            ReserveQuantity availableStorageUnits, ReserveQuantity footprintPerBatchUnits)
        {
            if (!availableStorageUnits.IsKnown || !footprintPerBatchUnits.IsKnown
                || footprintPerBatchUnits.Units <= 0)
            {
                return new ProductionCapacityConstraint(
                    ReserveQuantity.Unknown, availableStorageUnits, footprintPerBatchUnits);
            }
            return new ProductionCapacityConstraint(
                ReserveQuantity.Known(availableStorageUnits.Units / footprintPerBatchUnits.Units),
                availableStorageUnits, footprintPerBatchUnits);
        }
    }

    /// <summary>One ingredient's quantity, in the ingredient's own unit.</summary>
    public struct IngredientQuantity
    {
        public IngredientQuantity(string itemId, ReserveQuantity units)
        {
            ItemId = itemId ?? string.Empty;
            Units = units;
        }

        public string ItemId { get; }
        public ReserveQuantity Units { get; }
    }

    /// <summary>
    /// One ingredient's balance: what the required batches need, what
    /// is available, and the shortfall or surplus between them — both
    /// named, never netted into a single signed number, so a shortfall
    /// is never hidden by a surplus elsewhere.
    /// </summary>
    public struct IngredientBalance
    {
        public IngredientBalance(string itemId, ReserveQuantity neededUnits,
            ReserveQuantity availableUnits, ReserveQuantity shortfallUnits,
            ReserveQuantity surplusUnits)
        {
            ItemId = itemId ?? string.Empty;
            NeededUnits = neededUnits;
            AvailableUnits = availableUnits;
            ShortfallUnits = shortfallUnits;
            SurplusUnits = surplusUnits;
        }

        public string ItemId { get; }
        public ReserveQuantity NeededUnits { get; }
        public ReserveQuantity AvailableUnits { get; }
        public ReserveQuantity ShortfallUnits { get; }
        public ReserveQuantity SurplusUnits { get; }
    }

    /// <summary>
    /// Every input to a production plan, passed in by the caller —
    /// nothing here is hardcoded, and nothing is read from a live
    /// authority by this type. A struct: a default instance is
    /// all-Unknown with an invalid recipe, which is the honest
    /// default — no input is ever assumed.
    /// </summary>
    public struct ProductionInputs
    {
        /// <summary>Projected finished-goods demand, in finished units (Unknown when unprojected).</summary>
        public ReserveQuantity ProjectedDemand;

        /// <summary>The recipe the batches follow (invalid until a caller supplies one).</summary>
        public ProductionRecipe Recipe;

        /// <summary>Existing ingredient stock, per ingredient.</summary>
        public IReadOnlyList<IngredientStockEntry> Stock;

        /// <summary>Ingredients already committed to planned batches, per ingredient.</summary>
        public IReadOnlyList<IngredientCommitment> Committed;

        /// <summary>Incoming supplies, per ingredient — counted only where confirmed.</summary>
        public IReadOnlyList<IncomingSupply> Incoming;

        /// <summary>Production lead times (Unknown when no authored value — the plan is then incomplete on that axis).</summary>
        public ProductionLeadTimes LeadTimes;

        /// <summary>Expected spoilage per ingredient, from the Slice 5 condition model.</summary>
        public IReadOnlyList<ExpectedSpoilageEntry> ExpectedSpoilage;

        /// <summary>The cash constraint, from the business's own cash records.</summary>
        public ProductionCashConstraint Cash;

        /// <summary>The capacity constraint, from the F2 evaluator.</summary>
        public ProductionCapacityConstraint Capacity;

        /// <summary>The day the plan is made, for lead-time day arithmetic (negative means unknown).</summary>
        public int AsOfDay;

        /// <summary>
        /// Bridges the observation half into inputs: projected demand
        /// is the total observed demand for the item over the window
        /// ending at asOfDay (recorded history, never a forecast
        /// beyond it), and the recipe is the recorded Bakery recipe.
        /// Unknown when the window holds no observed demand (the
        /// diagnostic names the gap), and every other input stays
        /// Unknown — the bridge derives demand only, and invents no
        /// lead time, stock, or capacity.
        /// </summary>
        public static ProductionInputs FromObservation(DemandObservation observation,
            string businessId, string itemId, int recentDays, int asOfDay, out string diagnostic)
        {
            diagnostic = string.Empty;
            var inputs = new ProductionInputs
            {
                Recipe = ProductionRecipe.BakeryBreadLoaf(),
                AsOfDay = asOfDay,
            };
            if (observation == null)
            {
                diagnostic = "ProductionInputs: no observation supplied — no demand can be derived.";
                return inputs;
            }
            if (recentDays <= 0)
            {
                diagnostic = "ProductionInputs: the recent window must be at least one day.";
                return inputs;
            }
            int start = asOfDay - recentDays + 1;
            int total = 0;
            foreach (DailyDemandPoint day in observation.DailyHistory(businessId, itemId))
            {
                if (day.DayIndex < start || day.DayIndex > asOfDay) continue;
                total += day.RequestedUnits;
            }
            if (total > 0)
            {
                inputs.ProjectedDemand = ReserveQuantity.Known(total);
            }
            else
            {
                diagnostic = $"ProductionInputs: no demand history for {businessId}/{itemId} in the last {recentDays} day(s) ending day {asOfDay} — no demand can be derived.";
            }
            return inputs;
        }
    }

    /// <summary>
    /// A production plan: the batches a projected demand requires,
    /// the inputs those batches consume, the stock, commitments, and
    /// confirmed incoming supplies behind them, the shortfall or
    /// surplus per ingredient, and the batches that are feasible under
    /// the cash and capacity constraints. A recommendation only —
    /// nothing orders, nothing reserves stock in any live authority.
    /// The reasoning names every input that moved the number and
    /// every Unknown input that was left out of it.
    /// </summary>
    public sealed class ProductionPlan
    {
        private ProductionPlan(ReserveQuantity projectedDemand, ReserveQuantity requiredBatches,
            List<IngredientQuantity> recipeInputsTotal, List<IngredientQuantity> stockOnHand,
            List<IngredientQuantity> committed, List<IngredientQuantity> incoming,
            List<IngredientBalance> shortfallOrSurplus, ReserveQuantity feasibleBatches,
            ReserveQuantity recommendedBatches, ReserveQuantity earliestCompletionDay,
            ReserveQuantity latestIngredientOrderDay, bool isComplete,
            List<string> incompleteAxes, List<string> reasoning)
        {
            ProjectedDemand = projectedDemand;
            RequiredBatches = requiredBatches;
            RecipeInputsTotal = recipeInputsTotal.AsReadOnly();
            StockOnHand = stockOnHand.AsReadOnly();
            Committed = committed.AsReadOnly();
            Incoming = incoming.AsReadOnly();
            ShortfallOrSurplus = shortfallOrSurplus.AsReadOnly();
            FeasibleBatches = feasibleBatches;
            RecommendedBatches = recommendedBatches;
            EarliestCompletionDay = earliestCompletionDay;
            LatestIngredientOrderDay = latestIngredientOrderDay;
            IsComplete = isComplete;
            IncompleteAxes = incompleteAxes.AsReadOnly();
            Reasoning = reasoning.AsReadOnly();
        }

        /// <summary>The projected finished-goods demand the plan starts from, in finished units.</summary>
        public ReserveQuantity ProjectedDemand { get; }

        /// <summary>Batches required: ceil(demand / units per batch) — never rounded down.</summary>
        public ReserveQuantity RequiredBatches { get; }

        /// <summary>Total recipe inputs the required batches consume, per ingredient.</summary>
        public IReadOnlyList<IngredientQuantity> RecipeInputsTotal { get; }

        /// <summary>Usable stock on hand per ingredient, spoilage-discounted where lot states were supplied.</summary>
        public IReadOnlyList<IngredientQuantity> StockOnHand { get; }

        /// <summary>Ingredients committed to planned batches, per ingredient.</summary>
        public IReadOnlyList<IngredientQuantity> Committed { get; }

        /// <summary>Confirmed incoming supplies, per ingredient (unconfirmed supplies add nothing).</summary>
        public IReadOnlyList<IngredientQuantity> Incoming { get; }

        /// <summary>Shortfall or surplus per ingredient, with exact units.</summary>
        public IReadOnlyList<IngredientBalance> ShortfallOrSurplus { get; }

        /// <summary>Batches feasible under the stock, cash, and capacity constraints.</summary>
        public ReserveQuantity FeasibleBatches { get; }

        /// <summary>Batches recommended: the feasible batches (the plan recommends; it orders nothing).</summary>
        public ReserveQuantity RecommendedBatches { get; }

        /// <summary>The earliest day a batch completes (as-of day plus the production lead time), or Unknown.</summary>
        public ReserveQuantity EarliestCompletionDay { get; }

        /// <summary>The latest day ingredients must be ordered by (as-of day minus the ingredient lead time), or Unknown.</summary>
        public ReserveQuantity LatestIngredientOrderDay { get; }

        /// <summary>True only when every axis is Known and every computed figure is Known.</summary>
        public bool IsComplete { get; }

        /// <summary>Axes the plan is incomplete on ("projected demand", "recipe", "stock", "commitments", "cash", "capacity", "lead time", "expected spoilage").</summary>
        public IReadOnlyList<string> IncompleteAxes { get; }

        /// <summary>One line per input that moved the number, and one per Unknown input left out.</summary>
        public IReadOnlyList<string> Reasoning { get; }

        private static IngredientStockEntry FindStock(IReadOnlyList<IngredientStockEntry> stock, string itemId)
        {
            if (stock != null)
            {
                foreach (IngredientStockEntry entry in stock)
                {
                    if (string.Equals(entry.ItemId, itemId, StringComparison.Ordinal)) return entry;
                }
            }
            return new IngredientStockEntry(itemId, ReserveQuantity.Unknown, null);
        }

        /// <summary>
        /// Computes the plan from the supplied inputs. Pure: the
        /// inputs are never mutated, and no authority is read or
        /// written.
        /// </summary>
        public static ProductionPlan Compute(ProductionInputs inputs)
        {
            var reasoning = new List<string>();
            var incomplete = new List<string>();
            var totals = new List<IngredientQuantity>();
            var stockOnHand = new List<IngredientQuantity>();
            var committedOut = new List<IngredientQuantity>();
            var incomingOut = new List<IngredientQuantity>();
            var balances = new List<IngredientBalance>();

            if (!inputs.ProjectedDemand.IsKnown)
            {
                reasoning.Add("projected demand unknown — no demand figure was supplied, so no batches can be required.");
                incomplete.Add("projected demand");
                return new ProductionPlan(inputs.ProjectedDemand, ReserveQuantity.Unknown,
                    totals, stockOnHand, committedOut, incomingOut, balances,
                    ReserveQuantity.Unknown, ReserveQuantity.Unknown,
                    ReserveQuantity.Unknown, ReserveQuantity.Unknown,
                    false, incomplete, reasoning);
            }
            if (!inputs.Recipe.IsValid)
            {
                reasoning.Add($"recipe unknown — no usable recipe was supplied for '{inputs.Recipe.ProductId}', so demand cannot be turned into batches.");
                incomplete.Add("recipe");
                return new ProductionPlan(inputs.ProjectedDemand, ReserveQuantity.Unknown,
                    totals, stockOnHand, committedOut, incomingOut, balances,
                    ReserveQuantity.Unknown, ReserveQuantity.Unknown,
                    ReserveQuantity.Unknown, ReserveQuantity.Unknown,
                    false, incomplete, reasoning);
            }

            int demand = inputs.ProjectedDemand.Units;
            int yieldPerBatch = inputs.Recipe.YieldUnitsPerBatch;
            int required = (demand + yieldPerBatch - 1) / yieldPerBatch;
            reasoning.Add($"projected demand: {demand} unit(s) of '{inputs.Recipe.ProductId}' / {yieldPerBatch} unit(s) per batch = {required} batch(es) (rounded up — a shortfall is never rounded down).");

            bool stockFeasibleKnown = true;
            int stockFeasible = required;

            foreach (ProductionRecipeIngredient ingredient in inputs.Recipe.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(ingredient.ItemId) || ingredient.UnitsPerBatch <= 0)
                {
                    reasoning.Add($"recipe ingredient '{ingredient.ItemId}' consumes {ingredient.UnitsPerBatch} unit(s) per batch and is skipped — a batch input must be a positive quantity.");
                    continue;
                }
                string itemId = ingredient.ItemId;
                int needed = required * ingredient.UnitsPerBatch;
                totals.Add(new IngredientQuantity(itemId, ReserveQuantity.Known(needed)));
                reasoning.Add($"recipe inputs: {required} batch(es) x {ingredient.UnitsPerBatch} unit(s) of '{itemId}' = {needed} unit(s).");

                IngredientStockEntry stockEntry = FindStock(inputs.Stock, itemId);
                ReserveQuantity usable;
                if (stockEntry.Lots != null && stockEntry.Lots.Count > 0)
                {
                    var mine = new List<PerishableLotState>();
                    foreach (PerishableLotState lot in stockEntry.Lots)
                    {
                        if (lot != null && string.Equals(lot.ItemId, itemId, StringComparison.Ordinal))
                        {
                            mine.Add(lot);
                        }
                    }
                    UsableStockAssessment assessment = ReserveProjection.UsableStockFromLots(mine);
                    usable = ReserveQuantity.Known(assessment.UsableUnits);
                    stockOnHand.Add(new IngredientQuantity(itemId, usable));
                    reasoning.Add($"usable stock of '{itemId}': {assessment.UsableUnits} unit(s) from {mine.Count} lot(s), discounted by the Slice 6 rule (lots past useful life or Unsafe count zero).");
                    foreach (string lotReason in assessment.PerLotReasons)
                    {
                        reasoning.Add($"stock lot: {lotReason}");
                    }
                }
                else if (stockEntry.Units.IsKnown)
                {
                    usable = stockEntry.Units;
                    stockOnHand.Add(new IngredientQuantity(itemId, usable));
                    reasoning.Add($"stock on hand of '{itemId}': {usable.Units} unit(s) (caller-supplied, no lot states).");
                }
                else
                {
                    usable = ReserveQuantity.Unknown;
                    stockOnHand.Add(new IngredientQuantity(itemId, ReserveQuantity.Unknown));
                    incomplete.Add("stock");
                    reasoning.Add($"stock of '{itemId}' unknown — no stock figure or lot states were supplied, so the shortfall for '{itemId}' cannot be computed.");
                }

                int committed = 0;
                bool committedKnown = true;
                if (inputs.Committed != null)
                {
                    foreach (IngredientCommitment commitment in inputs.Committed)
                    {
                        if (!string.Equals(commitment.ItemId, itemId, StringComparison.Ordinal)) continue;
                        if (commitment.Units.IsKnown) committed += commitment.Units.Units;
                        else committedKnown = false;
                    }
                }
                committedOut.Add(new IngredientQuantity(itemId, ReserveQuantity.Known(committed)));
                if (committedKnown)
                {
                    reasoning.Add($"committed '{itemId}': {committed} unit(s) already committed to planned batches.");
                }
                else
                {
                    incomplete.Add("commitments");
                    reasoning.Add($"committed '{itemId}' unknown — counted as zero, never invented; the available figure may overstate what is free.");
                }

                int incoming = 0;
                if (inputs.Incoming != null)
                {
                    foreach (IncomingSupply supply in inputs.Incoming)
                    {
                        if (!string.Equals(supply.ItemId, itemId, StringComparison.Ordinal)) continue;
                        if (!supply.IsConfirmed)
                        {
                            reasoning.Add($"incoming supply of '{itemId}' from '{supply.SourceSupplierId}' is not confirmed — adds nothing (a P5 option is not a confirmed delivery).");
                            continue;
                        }
                        if (supply.Units.IsKnown) incoming += supply.Units.Units;
                        else reasoning.Add($"confirmed incoming supply of '{itemId}' from '{supply.SourceSupplierId}' carries an unknown quantity — adds nothing.");
                    }
                }
                incomingOut.Add(new IngredientQuantity(itemId, ReserveQuantity.Known(incoming)));
                if (incoming > 0)
                {
                    reasoning.Add($"incoming '{itemId}': {incoming} confirmed unit(s) add to available stock.");
                }

                int spoilage = 0;
                bool spoilageKnown = true;
                if (inputs.ExpectedSpoilage != null)
                {
                    foreach (ExpectedSpoilageEntry entry in inputs.ExpectedSpoilage)
                    {
                        if (!string.Equals(entry.ItemId, itemId, StringComparison.Ordinal)) continue;
                        if (entry.SpoiledUnits.IsKnown) spoilage += entry.SpoiledUnits.Units;
                        else spoilageKnown = false;
                    }
                }
                if (spoilageKnown)
                {
                    if (spoilage > 0)
                    {
                        reasoning.Add($"expected spoilage of '{itemId}': {spoilage} unit(s) expected to spoil before use.");
                    }
                }
                else
                {
                    incomplete.Add("expected spoilage");
                    reasoning.Add($"expected spoilage of '{itemId}' unknown — counted as zero, never invented; the available figure may overstate what survives.");
                }

                ReserveQuantity available;
                if (usable.IsKnown)
                {
                    int value = Math.Max(0, usable.Units - spoilage - committed) + incoming;
                    available = ReserveQuantity.Known(value);
                    reasoning.Add($"available '{itemId}': {usable.Units} usable - {spoilage} expected spoilage - {committed} committed + {incoming} confirmed incoming = {value} unit(s).");
                }
                else
                {
                    available = ReserveQuantity.Unknown;
                }

                if (available.IsKnown)
                {
                    int shortfall = Math.Max(0, needed - available.Units);
                    int surplus = Math.Max(0, available.Units - needed);
                    balances.Add(new IngredientBalance(itemId, ReserveQuantity.Known(needed), available,
                        ReserveQuantity.Known(shortfall), ReserveQuantity.Known(surplus)));
                    if (shortfall > 0)
                    {
                        reasoning.Add($"shortfall of '{itemId}': {needed} needed - {available.Units} available = {shortfall} unit(s) short.");
                    }
                    else
                    {
                        reasoning.Add($"surplus of '{itemId}': {available.Units} available - {needed} needed = {surplus} unit(s) surplus.");
                    }
                    int ingredientFeasible = available.Units / ingredient.UnitsPerBatch;
                    if (ingredientFeasible < stockFeasible) stockFeasible = ingredientFeasible;
                }
                else
                {
                    balances.Add(new IngredientBalance(itemId, ReserveQuantity.Known(needed),
                        ReserveQuantity.Unknown, ReserveQuantity.Unknown, ReserveQuantity.Unknown));
                    stockFeasibleKnown = false;
                    reasoning.Add($"shortfall of '{itemId}' unknown — available stock is unknown, so the shortfall cannot be computed.");
                }
            }

            int? cashFeasible = null;
            if (inputs.Cash.AvailableCashCents.IsKnown && inputs.Cash.CostPerBatchCents.IsKnown)
            {
                int cost = inputs.Cash.CostPerBatchCents.Cents;
                if (cost > 0)
                {
                    cashFeasible = inputs.Cash.AvailableCashCents.Cents / cost;
                    reasoning.Add($"cash: {inputs.Cash.AvailableCashCents.Cents}c available / {cost}c per batch = {cashFeasible.Value} batch(es) affordable.");
                }
                else
                {
                    reasoning.Add("cash: the caller-supplied cost per batch is zero, so cash caps nothing.");
                }
            }
            else
            {
                incomplete.Add("cash");
                reasoning.Add("cash unknown — no available-cash or per-batch cost figure was supplied, so cash caps nothing in this plan and no cash figure is invented.");
            }

            int? capacityFeasible = null;
            if (inputs.Capacity.MaxBatches.IsKnown)
            {
                capacityFeasible = inputs.Capacity.MaxBatches.Units;
                if (inputs.Capacity.AvailableStorageUnits.IsKnown && inputs.Capacity.FootprintPerBatchUnits.IsKnown)
                {
                    reasoning.Add($"capacity: {inputs.Capacity.AvailableStorageUnits.Units} unit(s) of headroom / {inputs.Capacity.FootprintPerBatchUnits.Units} unit(s) per batch = at most {capacityFeasible.Value} batch(es) fit.");
                }
                else
                {
                    reasoning.Add($"capacity: at most {capacityFeasible.Value} batch(es) fit (caller-supplied maximum).");
                }
            }
            else
            {
                incomplete.Add("capacity");
                reasoning.Add("capacity unknown — no capacity figure was supplied, so capacity caps nothing in this plan and no capacity is invented.");
            }

            ReserveQuantity earliestCompletion = ReserveQuantity.Unknown;
            ReserveQuantity latestOrder = ReserveQuantity.Unknown;
            bool leadKnown = inputs.LeadTimes.IngredientLeadTimeDays.IsKnown
                && inputs.LeadTimes.ProductionLeadTimeDays.IsKnown;
            if (!leadKnown)
            {
                incomplete.Add("lead time");
                reasoning.Add("lead time unknown — no authored lead time was supplied, so no completion or order day is computed.");
            }
            if (inputs.AsOfDay >= 0)
            {
                if (inputs.LeadTimes.ProductionLeadTimeDays.IsKnown)
                {
                    earliestCompletion = ReserveQuantity.Known(
                        inputs.AsOfDay + (int)Math.Ceiling(inputs.LeadTimes.ProductionLeadTimeDays.Days));
                    reasoning.Add($"production lead time: {inputs.LeadTimes.ProductionLeadTimeDays.Days} day(s) — the earliest completion day is {earliestCompletion.Units} (day {inputs.AsOfDay} + {inputs.LeadTimes.ProductionLeadTimeDays.Days} day(s)).");
                }
                if (inputs.LeadTimes.IngredientLeadTimeDays.IsKnown)
                {
                    latestOrder = ReserveQuantity.Known(
                        inputs.AsOfDay - (int)Math.Ceiling(inputs.LeadTimes.IngredientLeadTimeDays.Days));
                    reasoning.Add($"ingredient lead time: {inputs.LeadTimes.IngredientLeadTimeDays.Days} day(s) — ingredients must be ordered by day {latestOrder.Units} (day {inputs.AsOfDay} - {inputs.LeadTimes.IngredientLeadTimeDays.Days} day(s)).");
                }
            }
            else
            {
                reasoning.Add("as-of day unknown — no lead-time day arithmetic is computed.");
            }

            ReserveQuantity feasible;
            if (!stockFeasibleKnown)
            {
                feasible = ReserveQuantity.Unknown;
                reasoning.Add("feasible batches unknown — an ingredient's available stock is unknown, so no batch count can be bounded by stock.");
            }
            else
            {
                int value = Math.Min(required, stockFeasible);
                string cap = $"feasible batches: {required} required, capped by stock at {stockFeasible}";
                if (cashFeasible.HasValue)
                {
                    value = Math.Min(value, cashFeasible.Value);
                    cap += $", by cash at {cashFeasible.Value}";
                }
                if (capacityFeasible.HasValue)
                {
                    value = Math.Min(value, capacityFeasible.Value);
                    cap += $", by capacity at {capacityFeasible.Value}";
                }
                feasible = ReserveQuantity.Known(value);
                reasoning.Add(cap + $" = {value} batch(es).");
            }

            bool isComplete = incomplete.Count == 0 && feasible.IsKnown;
            if (isComplete)
            {
                reasoning.Add("plan complete: every input was Known.");
            }
            else
            {
                reasoning.Add("plan incomplete on: " + string.Join(", ", incomplete) + " — the figures above use the Known inputs only.");
            }
            return new ProductionPlan(inputs.ProjectedDemand, ReserveQuantity.Known(required),
                totals, stockOnHand, committedOut, incomingOut, balances,
                feasible, feasible, earliestCompletion, latestOrder,
                isComplete, incomplete, reasoning);
        }
    }
}
