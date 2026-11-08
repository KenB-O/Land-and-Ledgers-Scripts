// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice P6 — production planning foundation tests (directive
// P6). Pins: projected finished-goods demand connects to required
// batches through the recorded recipe only (12 lb flour plus 1 notion
// per 24 loaves — BakeryBreadCatalog.BreadFlourPerBatch,
// StandardNotionsPerBatch, BreadYieldPerBatch, constants at
// BakeryBreadCatalog.cs:103-121, GetSpec at 182-207 — never rounded down);
// committed ingredients are subtracted before available stock is
// computed; incoming supplies count only where confirmed (a P5 option
// is not a confirmed delivery); expected spoilage discounts ingredient
// stock the same way Slice 6 discounts usable stock (lots past useful
// life or Unsafe count zero); cash and F2 capacity constraints cap
// feasible batches and name the cap; shortfall is named per ingredient
// with the exact missing units; Unknown inputs are reported as Unknown
// with the plan marked incomplete on that axis rather than planned on
// an invented value; and acceptance legs Q (the plan changes nothing
// in any live authority) and R (16-sack haul, 125c settlement, 12 lb
// to 24 loaves, all unchanged).
//
// The plan recommends batches; it orders nothing, reserves nothing in
// any live authority, and changes no Bakery policy.

using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Economy.Storage;
using LandLedgers.Economy.Transport;
using LandLedgers.Orchestration.Player;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class ProductionPlanTests
    {
        private const int Day = 12;
        private const int EthanId = 7;
        private const string CarrierId = "livery_freight_003";
        private const string BakeryId = "bakery_001";
        private const string StoreInstanceId = "general-store-1";
        private const string WagonId = "opening-freight-wagon";
        private const string ShipmentId = "freight-shipment:request-1";
        private const string AgreementId = "freight-agreement:request-1";
        private const string LotId = "aidlot-millbrook_grain_co:chain-flour-16";
        private const string OrderRequestId = "chain-flour-16";
        private const int Sacks = 16;
        private const int FreightChargeCents = 125;
        private const int HouseholdId = 7;
        private const int ShopperPersonId = 1;

        private const string FlourItem = "bakery_flour_sack";
        private const string MillbrookId = "millbrook_grain_co";
        private const string FlourIngredient = BakeryBreadCatalog.FlourItemId;
        private const string NotionsIngredient = BakeryBreadCatalog.NotionsItemId;
        private const string BreadItem = BakeryBreadCatalog.BreadLoafId;

        private static string Reasoning(ProductionPlan plan) =>
            string.Join(" | ", plan.Reasoning);

        private static string Axes(ProductionPlan plan) =>
            string.Join(", ", plan.IncompleteAxes);

        // The recorded recipe, read back from the catalog itself: 12 lb
        // flour plus 1 notion per batch, 24 loaves per batch
        // (BakeryBreadCatalog.cs:103-121 constants, GetSpec 182-207).
        private static ProductionRecipe BakeryRecipe() => ProductionRecipe.BakeryBreadLoaf();

        private static ProductionInputs BaseInputs(
            int demandLoaves = 48,
            int flourStock = 100,
            int notionsStock = 10,
            int flourCommitted = 0,
            int notionsCommitted = 0,
            int availableCashCents = 20000,
            int costPerBatchCents = 100,
            int maxBatches = 10)
        {
            return new ProductionInputs
            {
                ProjectedDemand = demandLoaves >= 0
                    ? ReserveQuantity.Known(demandLoaves) : ReserveQuantity.Unknown,
                Recipe = BakeryRecipe(),
                Stock = new List<IngredientStockEntry>
                {
                    new IngredientStockEntry(FlourIngredient, ReserveQuantity.Known(flourStock), null),
                    new IngredientStockEntry(NotionsIngredient, ReserveQuantity.Known(notionsStock), null),
                },
                Committed = new List<IngredientCommitment>
                {
                    new IngredientCommitment(FlourIngredient, ReserveQuantity.Known(flourCommitted)),
                    new IngredientCommitment(NotionsIngredient, ReserveQuantity.Known(notionsCommitted)),
                },
                Incoming = new List<IncomingSupply>(),
                LeadTimes = new ProductionLeadTimes(LeadTimeDays.Known(2.0), LeadTimeDays.Known(1.0)),
                ExpectedSpoilage = new List<ExpectedSpoilageEntry>(),
                Cash = new ProductionCashConstraint(
                    CentsAmount.Known(availableCashCents), CentsAmount.Known(costPerBatchCents)),
                Capacity = new ProductionCapacityConstraint(
                    ReserveQuantity.Known(maxBatches), ReserveQuantity.Unknown, ReserveQuantity.Unknown),
                AsOfDay = Day,
            };
        }

        private static int TotalFor(ProductionPlan plan, string itemId)
        {
            foreach (IngredientQuantity entry in plan.RecipeInputsTotal)
            {
                if (string.Equals(entry.ItemId, itemId, StringComparison.Ordinal)) return entry.Units.Units;
            }
            Assert.Fail("no recipe total for " + itemId);
            return 0;
        }

        private static int StockFor(ProductionPlan plan, string itemId)
        {
            foreach (IngredientQuantity entry in plan.StockOnHand)
            {
                if (string.Equals(entry.ItemId, itemId, StringComparison.Ordinal)) return entry.Units.Units;
            }
            Assert.Fail("no stock entry for " + itemId);
            return 0;
        }

        private static bool StockIsKnown(ProductionPlan plan, string itemId)
        {
            foreach (IngredientQuantity entry in plan.StockOnHand)
            {
                if (string.Equals(entry.ItemId, itemId, StringComparison.Ordinal)) return entry.Units.IsKnown;
            }
            Assert.Fail("no stock entry for " + itemId);
            return false;
        }

        private static IngredientBalance BalanceFor(ProductionPlan plan, string itemId)
        {
            foreach (IngredientBalance entry in plan.ShortfallOrSurplus)
            {
                if (string.Equals(entry.ItemId, itemId, StringComparison.Ordinal)) return entry;
            }
            Assert.Fail("no balance for " + itemId);
            return default(IngredientBalance);
        }

        private static PerishableLotState Lot(string lotId, string itemId, int units,
            PerishablePhysicalCondition condition = PerishablePhysicalCondition.Fresh,
            PerishableSafetyState safety = PerishableSafetyState.Safe,
            UsefulLifeEstimate remainingLife = default(UsefulLifeEstimate))
        {
            var state = new PerishableLotState(
                new LotKey("bakery:bakery_001", lotId), itemId, units, 0);
            state = state.WithConditions(condition, condition);
            state = state.WithSafety(safety,
                safety == PerishableSafetyState.Unsafe ? "test evidence: unsafe" : string.Empty);
            if (remainingLife.IsKnown)
            {
                // Remaining life is set at construction only through the
                // DTO round-trip: capture, edit the DTO field, restore.
                PerishableLotState.SaveDto dto = state.CaptureSaveDto();
                dto.RemainingLifeKnown = true;
                dto.RemainingLifeDays = remainingLife.Days;
                state = PerishableLotState.FromSaveDto(dto);
            }
            return state;
        }

        private static SupplierOption FlourIngredientOption(int quantityUnits, int unitPriceCents = 48)
        {
            return new SupplierOption(
                supplierId: MillbrookId,
                itemId: FlourIngredient,
                quantityUnits: ReserveQuantity.Known(quantityUnits),
                quantityAvailable: ReserveQuantity.Unknown,
                unitPriceCents: CentsAmount.Known(unitPriceCents),
                unitPriceSource: "test option: caller-supplied price, source named per test",
                freightCostCents: CentsAmount.Known(FreightChargeCents),
                freightSource: "test option: caller-supplied freight, source named per test",
                consolidatedShipmentQuoted: false,
                consolidatedShipment: false,
                leadTimeDays: LeadTimeDays.Known(2.0),
                quotedArrivalDay: ReserveQuantity.Unknown,
                reliability: SupplierReliability.Unknown,
                storageFit: SupplierStorageFit.Unknown("test option: no storage check supplied"),
                expectedSpoilageUnits: ReserveQuantity.Unknown,
                revenuePerUnitCents: CentsAmount.Known(10),
                revenueBasis: "test revenue basis: caller-supplied 10c per lb",
                availableCashCents: CentsAmount.Known(20000),
                isSelfCollect: false);
        }

        [Test]
        public void Demand48_RequiresTwoBatches_24LbFlour2Notions()
        {
            // The recorded recipe, read from the catalog, not the brief:
            // 12 lb flour + 1 notion per batch, 24 loaves per batch.
            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(BreadItem);
            Assert.AreEqual(12, spec.FlourUnitsPerBatch, "recorded flour per batch");
            Assert.AreEqual(1, spec.NotionUnitsPerBatch, "recorded notions per batch");
            Assert.AreEqual(24, spec.YieldUnitsPerBatch, "recorded loaves per batch");

            var book = new DemandObservation();
            Assert.IsTrue(book.Record(new DemandAttemptRecord(
                Day, BakeryId, BreadItem, HouseholdId,
                DemandObservation.NeedKey(HouseholdId, 1), "attempt-1",
                48, 48, false, "test: 48 loaves observed"), out string diagnostic), diagnostic);
            ProductionInputs inputs = ProductionInputs.FromObservation(
                book, BakeryId, BreadItem, 7, Day, out diagnostic);
            Assert.IsTrue(string.IsNullOrEmpty(diagnostic), diagnostic);
            Assert.IsTrue(inputs.ProjectedDemand.IsKnown);
            Assert.AreEqual(48, inputs.ProjectedDemand.Units, "the bridge carries the observed 48-loaf demand");
            inputs.Stock = BaseInputs().Stock;
            inputs.Committed = BaseInputs().Committed;
            inputs.Incoming = BaseInputs().Incoming;
            inputs.LeadTimes = BaseInputs().LeadTimes;
            inputs.ExpectedSpoilage = BaseInputs().ExpectedSpoilage;
            inputs.Cash = BaseInputs().Cash;
            inputs.Capacity = BaseInputs().Capacity;

            ProductionPlan plan = ProductionPlan.Compute(inputs);
            Assert.IsTrue(plan.ProjectedDemand.IsKnown);
            Assert.AreEqual(48, plan.ProjectedDemand.Units);
            Assert.IsTrue(plan.RequiredBatches.IsKnown);
            Assert.AreEqual(2, plan.RequiredBatches.Units, "48 loaves / 24 per batch = 2 batches");
            Assert.AreEqual(24, TotalFor(plan, FlourIngredient), "2 batches x 12 lb flour");
            Assert.AreEqual(2, TotalFor(plan, NotionsIngredient), "2 batches x 1 notion");
            Assert.IsTrue(plan.FeasibleBatches.IsKnown);
            Assert.AreEqual(2, plan.FeasibleBatches.Units, "stock, cash, and capacity all allow 2 batches");
            Assert.AreEqual(2, plan.RecommendedBatches.Units);
            Assert.IsTrue(plan.IsComplete, "every input Known: " + Axes(plan));
            Assert.IsTrue(Reasoning(plan).Contains("48"), "the reasoning names the demand: " + Reasoning(plan));
            Assert.IsTrue(Reasoning(plan).Contains("24"), "the reasoning names the per-batch yield: " + Reasoning(plan));
        }

        [Test]
        public void Demand25_RoundsUp_NeverDown()
        {
            ProductionPlan plan = ProductionPlan.Compute(BaseInputs(demandLoaves: 25));
            Assert.AreEqual(2, plan.RequiredBatches.Units,
                "25 loaves need 2 batches — a shortfall is never rounded down to 1");
            Assert.AreEqual(24, TotalFor(plan, FlourIngredient));
            Assert.AreEqual(2, TotalFor(plan, NotionsIngredient));

            ProductionPlan exact = ProductionPlan.Compute(BaseInputs(demandLoaves: 24));
            Assert.AreEqual(1, exact.RequiredBatches.Units, "24 loaves need exactly 1 batch");
            Assert.AreEqual(12, TotalFor(exact, FlourIngredient));
        }

        [Test]
        public void CommittedIngredients_SubtractedBeforeAvailableStock()
        {
            // 30 lb on hand, 12 lb committed to a planned batch: the
            // second batch sees 18 lb, not 30.
            ProductionPlan plan = ProductionPlan.Compute(BaseInputs(
                demandLoaves: 48, flourStock: 30, notionsStock: 2, flourCommitted: 12));
            Assert.AreEqual(30, StockFor(plan, FlourIngredient), "stock on hand is the usable 30 lb");
            IngredientQuantity committedFlour = default(IngredientQuantity);
            foreach (IngredientQuantity entry in plan.Committed)
            {
                if (string.Equals(entry.ItemId, FlourIngredient, StringComparison.Ordinal)) committedFlour = entry;
            }
            Assert.AreEqual(12, committedFlour.Units.Units, "12 lb are committed to a planned batch");
            IngredientBalance flour = BalanceFor(plan, FlourIngredient);
            Assert.AreEqual(24, flour.NeededUnits.Units);
            Assert.AreEqual(18, flour.AvailableUnits.Units,
                "30 lb on hand - 12 lb committed = 18 lb available for another batch");
            Assert.AreEqual(6, flour.ShortfallUnits.Units, "24 lb needed - 18 lb available = 6 lb short");
            Assert.AreEqual(0, BalanceFor(plan, NotionsIngredient).ShortfallUnits.Units);
            Assert.AreEqual(1, plan.FeasibleBatches.Units,
                "18 lb allows 1 batch of 12 lb; the second batch sees the reduced stock");
            Assert.AreEqual(1, plan.RecommendedBatches.Units);
            Assert.IsTrue(Reasoning(plan).Contains("committed"),
                "the reasoning names the commitment: " + Reasoning(plan));
        }

        [Test]
        public void IncomingSupplies_CountOnlyWhereConfirmed()
        {
            // 12 lb on hand, 12 lb incoming: unconfirmed adds nothing,
            // confirmed adds all 12.
            ProductionInputs unconfirmed = BaseInputs(demandLoaves: 48, flourStock: 12, notionsStock: 2);
            unconfirmed.Incoming = new List<IncomingSupply>
            {
                IncomingSupply.FromSupplierOption(FlourIngredientOption(12), false,
                    "test: a quoted P5 option, not a confirmed delivery"),
            };
            ProductionPlan planUnconfirmed = ProductionPlan.Compute(unconfirmed);
            Assert.AreEqual(12, BalanceFor(planUnconfirmed, FlourIngredient).AvailableUnits.Units,
                "an unconfirmed P5 option adds nothing to available stock");
            Assert.AreEqual(12, BalanceFor(planUnconfirmed, FlourIngredient).ShortfallUnits.Units,
                "24 lb needed - 12 lb available = 12 lb short");
            Assert.AreEqual(1, planUnconfirmed.FeasibleBatches.Units, "only 1 batch is stock-feasible");
            Assert.IsTrue(Reasoning(planUnconfirmed).Contains("not confirmed"),
                "the reasoning names the unconfirmed supply: " + Reasoning(planUnconfirmed));

            ProductionInputs confirmed = BaseInputs(demandLoaves: 48, flourStock: 12, notionsStock: 2);
            confirmed.Incoming = new List<IncomingSupply>
            {
                IncomingSupply.FromSupplierOption(FlourIngredientOption(12), true,
                    "test: a confirmed incoming delivery"),
            };
            ProductionPlan planConfirmed = ProductionPlan.Compute(confirmed);
            Assert.AreEqual(24, BalanceFor(planConfirmed, FlourIngredient).AvailableUnits.Units,
                "a confirmed incoming delivery adds its units");
            Assert.AreEqual(0, BalanceFor(planConfirmed, FlourIngredient).ShortfallUnits.Units);
            Assert.AreEqual(2, planConfirmed.FeasibleBatches.Units);
            Assert.AreEqual(2, planConfirmed.RecommendedBatches.Units);
        }

        [Test]
        public void ExpectedSpoilage_LotsPastLifeOrUnsafeCountZero()
        {
            // The Slice 6 discount rule, applied to ingredient stock: a
            // lot past its useful life or Unsafe counts zero usable.
            var lots = new List<PerishableLotState>
            {
                Lot("lot-fresh", FlourIngredient, 24),
                Lot("lot-aged", FlourIngredient, 12,
                    PerishablePhysicalCondition.Stale, PerishableSafetyState.Safe,
                    UsefulLifeEstimate.Known(0.0)),
                Lot("lot-unsafe", FlourIngredient, 6,
                    PerishablePhysicalCondition.Fresh, PerishableSafetyState.Unsafe),
            };
            ProductionInputs inputs = BaseInputs(demandLoaves: 48, notionsStock: 2);
            inputs.Stock = new List<IngredientStockEntry>
            {
                new IngredientStockEntry(FlourIngredient, ReserveQuantity.Unknown, lots),
                new IngredientStockEntry(NotionsIngredient, ReserveQuantity.Known(2), null),
            };
            ProductionPlan plan = ProductionPlan.Compute(inputs);
            Assert.AreEqual(24, StockFor(plan, FlourIngredient),
                "24 fresh lb count; 12 past-life lb and 6 unsafe lb count zero");
            Assert.AreEqual(0, BalanceFor(plan, FlourIngredient).ShortfallUnits.Units);
            Assert.AreEqual(2, plan.RecommendedBatches.Units);
            Assert.IsTrue(Reasoning(plan).Contains("past its useful life"),
                "the reasoning names the past-life discount: " + Reasoning(plan));
            Assert.IsTrue(Reasoning(plan).Contains("unsafe"),
                "the reasoning names the unsafe discount: " + Reasoning(plan));

            // Only a past-life lot: nothing usable, the whole need is short.
            ProductionInputs spoiledOnly = BaseInputs(demandLoaves: 48, notionsStock: 2);
            spoiledOnly.Stock = new List<IngredientStockEntry>
            {
                new IngredientStockEntry(FlourIngredient, ReserveQuantity.Unknown,
                    new List<PerishableLotState>
                    {
                        Lot("lot-aged-only", FlourIngredient, 12,
                            PerishablePhysicalCondition.Stale, PerishableSafetyState.Safe,
                            UsefulLifeEstimate.Known(0.0)),
                    }),
                new IngredientStockEntry(NotionsIngredient, ReserveQuantity.Known(2), null),
            };
            ProductionPlan spoiledPlan = ProductionPlan.Compute(spoiledOnly);
            Assert.AreEqual(0, StockFor(spoiledPlan, FlourIngredient));
            Assert.AreEqual(24, BalanceFor(spoiledPlan, FlourIngredient).ShortfallUnits.Units);
            Assert.AreEqual(0, spoiledPlan.FeasibleBatches.Units);
            Assert.AreEqual(0, spoiledPlan.RecommendedBatches.Units);
        }

        [Test]
        public void CashConstraint_CapsFeasibleBatchesAtOne()
        {
            // Stock, capacity, and demand all allow 2 batches; 150c of
            // cash against a caller-supplied 100c per batch allows 1.
            ProductionPlan plan = ProductionPlan.Compute(BaseInputs(
                demandLoaves: 48, availableCashCents: 150, costPerBatchCents: 100));
            Assert.AreEqual(2, plan.RequiredBatches.Units, "demand still requires 2 batches");
            Assert.IsTrue(plan.FeasibleBatches.IsKnown);
            Assert.AreEqual(1, plan.FeasibleBatches.Units,
                "150c / 100c per batch = 1 feasible batch");
            Assert.AreEqual(1, plan.RecommendedBatches.Units);
            Assert.IsTrue(Reasoning(plan).Contains("cash"),
                "the reasoning names the cash cap: " + Reasoning(plan));
            Assert.IsTrue(Reasoning(plan).Contains("150"),
                "the reasoning names the available cash: " + Reasoning(plan));
        }

        [Test]
        public void CapacityConstraint_F2Headroom_CapsFeasibleBatchesAtOne()
        {
            // The F2 BusinessStorage leg, read through the real
            // evaluator: Total 1000, Occupied 970, so 30 units of
            // headroom — below a second batch's 24-loaf footprint on top
            // of the first (48 loaves do not fit in 30).
            var storeRuntime = new BusinessRuntimeState();
            storeRuntime.BindBusinessIdentity(StoreInstanceId);
            storeRuntime.EnsureCategoryStock("staple_food", 970, 1000);
            var evaluator = new PhysicalCapacityEvaluator(
                null, id => string.Equals(id, StoreInstanceId, StringComparison.Ordinal) ? storeRuntime : null,
                null, null);
            PhysicalCapacityVerdict verdict = evaluator.Evaluate(
                new PhysicalCapacityQuery(PhysicalContainerKind.BusinessStorage, StoreInstanceId,
                    new PhysicalLoad(new[] { (HouseholdItemCatalog.BreadId, 16) }), Day),
                new PhysicalCapacityEvaluationContext { BusinessCategoryId = "staple_food" });
            Assert.IsTrue(verdict.StorageAxes.TotalUnits.HasValue, "the F2 leg answers Total");
            Assert.IsTrue(verdict.StorageAxes.OccupiedUnits.HasValue, "the F2 leg answers Occupied");
            Assert.AreEqual(1000, verdict.StorageAxes.TotalUnits.Value);
            Assert.AreEqual(970, verdict.StorageAxes.OccupiedUnits.Value);
            int headroom = verdict.StorageAxes.TotalUnits.Value - verdict.StorageAxes.OccupiedUnits.Value;
            Assert.AreEqual(30, headroom, "1000 total - 970 occupied");

            ProductionCapacityConstraint capacity = ProductionCapacityConstraint.FromStorageHeadroom(
                ReserveQuantity.Known(headroom), ReserveQuantity.Known(24));
            Assert.IsTrue(capacity.MaxBatches.IsKnown);
            Assert.AreEqual(1, capacity.MaxBatches.Units, "30 units of headroom / 24 per batch = 1 batch");

            ProductionInputs inputs = BaseInputs(demandLoaves: 48);
            inputs.Capacity = capacity;
            ProductionPlan plan = ProductionPlan.Compute(inputs);
            Assert.AreEqual(2, plan.RequiredBatches.Units, "demand still requires 2 batches");
            Assert.AreEqual(1, plan.FeasibleBatches.Units, "capacity caps feasible batches at 1");
            Assert.AreEqual(1, plan.RecommendedBatches.Units);
            Assert.IsTrue(Reasoning(plan).Contains("capacity"),
                "the reasoning names the capacity cap: " + Reasoning(plan));
        }

        [Test]
        public void Shortfall_NamedPerIngredient_ExactMissingUnits()
        {
            ProductionPlan plan = ProductionPlan.Compute(BaseInputs(
                demandLoaves: 48, flourStock: 10, notionsStock: 0));
            IngredientBalance flour = BalanceFor(plan, FlourIngredient);
            Assert.AreEqual(24, flour.NeededUnits.Units);
            Assert.AreEqual(10, flour.AvailableUnits.Units);
            Assert.AreEqual(14, flour.ShortfallUnits.Units, "24 lb needed - 10 lb available = 14 lb short");
            Assert.AreEqual(0, flour.SurplusUnits.Units);
            IngredientBalance notions = BalanceFor(plan, NotionsIngredient);
            Assert.AreEqual(2, notions.NeededUnits.Units);
            Assert.AreEqual(0, notions.AvailableUnits.Units);
            Assert.AreEqual(2, notions.ShortfallUnits.Units, "2 notions needed - 0 on hand = 2 short");
            string reasoning = Reasoning(plan);
            Assert.IsTrue(reasoning.Contains("shortfall"),
                "the reasoning names the shortfall: " + reasoning);
            Assert.IsTrue(reasoning.Contains("14"),
                "the reasoning names the 14 lb flour shortfall: " + reasoning);
        }

        [Test]
        public void UnknownLeadTime_PlanIncompleteOnLeadTimeAxis()
        {
            ProductionInputs inputs = BaseInputs(demandLoaves: 48);
            inputs.LeadTimes = new ProductionLeadTimes(LeadTimeDays.Unknown, LeadTimeDays.Unknown);
            ProductionPlan plan = ProductionPlan.Compute(inputs);
            Assert.IsTrue(!plan.IsComplete, "no authored lead time was supplied");
            Assert.IsTrue(Axes(plan).Contains("lead time"),
                "the plan is marked incomplete on the lead-time axis: " + Axes(plan));
            Assert.IsTrue(plan.RequiredBatches.IsKnown);
            Assert.AreEqual(2, plan.RequiredBatches.Units,
                "batches are still planned from the known axes — no lead time is invented");
            Assert.AreEqual(2, plan.RecommendedBatches.Units);
            Assert.IsTrue(!plan.EarliestCompletionDay.IsKnown,
                "no completion day is invented without a lead time");
            Assert.IsTrue(Reasoning(plan).Contains("lead time unknown"),
                "the reasoning names the missing lead time: " + Reasoning(plan));
        }

        [Test]
        public void UnknownDemand_RequiredAndRecommendedUnknown()
        {
            ProductionInputs inputs = BaseInputs(demandLoaves: -1);
            ProductionPlan plan = ProductionPlan.Compute(inputs);
            Assert.IsTrue(!plan.ProjectedDemand.IsKnown);
            Assert.IsTrue(!plan.RequiredBatches.IsKnown, "no demand, no required batches");
            Assert.IsTrue(!plan.RecommendedBatches.IsKnown, "no demand, no recommendation");
            Assert.IsTrue(!plan.IsComplete);
            Assert.IsTrue(Axes(plan).Contains("projected demand"),
                "the incomplete axis names the demand: " + Axes(plan));
            Assert.IsTrue(Reasoning(plan).Contains("demand unknown"),
                "the reasoning names the missing demand: " + Reasoning(plan));
        }

        [Test]
        public void UnknownCash_NamedNotInvented_RecommendedFromKnownAxes()
        {
            ProductionInputs inputs = BaseInputs(demandLoaves: 48);
            inputs.Cash = new ProductionCashConstraint(CentsAmount.Unknown, CentsAmount.Known(100));
            ProductionPlan plan = ProductionPlan.Compute(inputs);
            Assert.IsTrue(!plan.IsComplete, "cash is Unknown, so the plan is incomplete on that axis");
            Assert.IsTrue(Axes(plan).Contains("cash"), "the incomplete axis names cash: " + Axes(plan));
            Assert.AreEqual(2, plan.RecommendedBatches.Units,
                "the recommendation still follows the known axes — no cash figure is invented");
            Assert.IsTrue(Reasoning(plan).Contains("cash unknown"),
                "the reasoning names the missing cash: " + Reasoning(plan));
        }

        [Test]
        public void UnknownStock_FeasibleUnknown()
        {
            ProductionInputs inputs = BaseInputs(demandLoaves: 48);
            inputs.Stock = new List<IngredientStockEntry>
            {
                new IngredientStockEntry(FlourIngredient, ReserveQuantity.Unknown, null),
                new IngredientStockEntry(NotionsIngredient, ReserveQuantity.Known(10), null),
            };
            ProductionPlan plan = ProductionPlan.Compute(inputs);
            Assert.IsTrue(!StockIsKnown(plan, FlourIngredient),
                "flour stock is Unknown, never a stored zero");
            Assert.IsTrue(!BalanceFor(plan, FlourIngredient).AvailableUnits.IsKnown,
                "available flour is Unknown when stock is Unknown");
            Assert.IsTrue(!BalanceFor(plan, FlourIngredient).ShortfallUnits.IsKnown,
                "the flour shortfall is Unknown, never an invented number");
            Assert.IsTrue(!plan.FeasibleBatches.IsKnown,
                "feasible batches are Unknown without a known flour stock");
            Assert.IsTrue(!plan.RecommendedBatches.IsKnown);
            Assert.IsTrue(!plan.IsComplete);
            Assert.IsTrue(Axes(plan).Contains("stock"), "the incomplete axis names stock: " + Axes(plan));
        }

        [Test]
        public void FromObservation_NoHistory_UnknownDemand()
        {
            var book = new DemandObservation();
            ProductionInputs inputs = ProductionInputs.FromObservation(
                book, BakeryId, BreadItem, 7, Day, out string diagnostic);
            Assert.IsTrue(!inputs.ProjectedDemand.IsKnown,
                "no observed demand is Unknown demand, never zero demand");
            Assert.IsTrue(!string.IsNullOrEmpty(diagnostic), "the gap is named");
            Assert.IsTrue(diagnostic.Contains("no demand history"),
                "the diagnostic names the gap: " + diagnostic);
        }

        [Test]
        public void Compute_Deterministic_InputsUnmutated()
        {
            ProductionInputs inputs = BaseInputs(demandLoaves: 48);
            ProductionPlan first = ProductionPlan.Compute(inputs);
            ProductionPlan second = ProductionPlan.Compute(inputs);
            Assert.AreEqual(first.RequiredBatches.Units, second.RequiredBatches.Units);
            Assert.AreEqual(first.RecommendedBatches.Units, second.RecommendedBatches.Units);
            Assert.AreEqual(Reasoning(first), Reasoning(second), "same inputs, same reasoning");
            Assert.AreEqual(48, inputs.ProjectedDemand.Units, "Compute never mutates its inputs");
            Assert.AreEqual(100, StockFor(first, FlourIngredient));
        }

        [Test]
        public void Chain_Q_PlanChangesNothing()
        {
            var log = new CustodyTransferLog();
            Chain s = DriveToStockedStore(log);
            (int supplierCash, int supplierStock, int bakeryCash, int payerCash, int carrierCash,
                int flourLb, int storeStock, int lotCount) snapshot = Snapshot(s);

            // The plan is computed from the chain's own figures: 148 lb
            // flour on hand, 1 notion left (2 seeded, 1 baked), demand
            // 48 loaves.
            ProductionInputs inputs = BaseInputs(demandLoaves: 48,
                flourStock: s.Bakery.FlourStock.UnitsOnHand(FlourIngredient),
                notionsStock: s.Bakery.FlourStock.UnitsOnHand(NotionsIngredient));
            int logCountBefore = log.Count;
            ProductionPlan plan = ProductionPlan.Compute(inputs);
            Assert.AreEqual(1, plan.RecommendedBatches.Units,
                "148 lb flour allows 2 batches but 1 notion allows only 1: " + Reasoning(plan));

            (int supplierCash, int supplierStock, int bakeryCash, int payerCash, int carrierCash,
                int flourLb, int storeStock, int lotCount) after = Snapshot(s);
            Assert.AreEqual(snapshot, after,
                "planning changes nothing in any live authority — quantities and settlements identical before and after");
            Assert.AreEqual(logCountBefore, log.Count, "no new custody record either: the plan orders nothing");
        }

        [Test]
        public void Chain_R_FiguresUnchanged()
        {
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);

            PlaceFreightOrder(s);
            Assert.AreEqual(Sacks, s.Lot.PackageUnits, "16-sack haul");
            Assert.AreEqual(Sacks * 10, s.Lot.InventoryUnits, "16 sacks x 10 lb");
            Assert.AreEqual(Sacks * 48, s.Lot.TotalCostCents, "16 sacks x 48c");

            BuildFreightShipment(s);
            int payerBefore = s.PayerRuntime.CurrentCashCents;
            int carrierBefore = s.CarrierRuntime.CurrentCashCents;
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            Assert.AreEqual(payerBefore - FreightChargeCents, s.PayerRuntime.CurrentCashCents,
                "the 125c freight charge settles exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, s.CarrierRuntime.CurrentCashCents,
                "the 125c freight charge is credited exactly once");
            Assert.AreEqual(160, FlourLot(s).Units, "the delivered lot holds 160 lb");

            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked, "12 lb of flour bakes 24 loaves");
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "flour 160 -> 148 lb: 12 dispensed, 148 remaining");

            // The plan reads the same chain figures and changes none of them.
            ProductionInputs inputs = BaseInputs(demandLoaves: 24,
                flourStock: s.Bakery.FlourStock.UnitsOnHand(FlourIngredient),
                notionsStock: s.Bakery.FlourStock.UnitsOnHand(NotionsIngredient));
            ProductionPlan plan = ProductionPlan.Compute(inputs);
            Assert.AreEqual(1, plan.RequiredBatches.Units, "24 loaves need 1 batch");
            Assert.AreEqual(12, TotalFor(plan, FlourIngredient), "1 batch x 12 lb flour");
            Assert.AreEqual(1, TotalFor(plan, NotionsIngredient), "1 batch x 1 notion");
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "the plan moved no flour");
            Assert.AreEqual(payerBefore - FreightChargeCents, s.PayerRuntime.CurrentCashCents,
                "the plan settled no freight twice");
        }

        private static (int, int, int, int, int, int, int, int) Snapshot(Chain s) =>
            (s.Supplier.CashCents, s.Supplier.GetInventoryUnits(FlourItem),
                s.BakeryCash.BalanceCents, s.PayerRuntime.CurrentCashCents, s.CarrierRuntime.CurrentCashCents,
                s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                s.Supplier.Lots.Count + s.Bakery.FlourStock.Lots.Count);

        // ---- shared chain fixture: the real authorities, copied from
        // DemandObservationTests (itself merged from
        // FreightDeliveryPaymentAuditTests and
        // BakeryFoodChainEndToEndTests). Shopping helpers omitted: this
        // slice drives the chain only to a stocked store. ----

        // Drives the real authorities up to a stocked store: Millbrook
        // order, freight, bake — the same legs as the F3 chain test.
        private static Chain DriveToStockedStore(CustodyTransferLog log)
        {
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);
            PlaceFreightOrder(s);
            BuildFreightShipment(s);
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked, "24 loaves stocked to the live staple_food pool");
            return s;
        }

        private sealed class FakeBusinessCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }

            public FakeBusinessCashPort(int balanceCents) => BalanceCents = balanceCents;

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents <= 0 || amountCents > BalanceCents) return "test cash port refused debit";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (amountCents <= 0) return "test cash port refused credit";
                BalanceCents += amountCents;
                return null;
            }
        }

        // Production port over a real BusinessRuntimeState, copied from
        // BakeryFoodChainEndToEndTests: the production GeneralStoreTradingPort
        // wraps a MonoBehaviour manager and cannot be constructed in Roslyn.
        private sealed class RuntimeStorePort : IGeneralStoreTradingPort
        {
            public BusinessRuntimeState Runtime;
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int Cap = int.MaxValue;
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();

            public string StoreBusinessId => "general-store-1";
            public string StoreName => "General Store";
            public string LocationId => "general-store";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => Runtime != null ? Runtime.CurrentCashCents : 0;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }
            public int CategoryTargetStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.TargetStockUnits : 0;
            }
            public int CategoryPricePerUnitCents(string categoryId)
            {
                return Prices.TryGetValue(categoryId, out int p) ? p : 0;
            }
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return Runtime != null && Runtime.TryConsumeCategoryStockUnits(categoryId, units, out consumedUnits);
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                if (Runtime != null) Runtime.RecordGenericRetailSettlement(categoryId, unitsSold, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose)
            {
                if (Runtime == null) return "no runtime";
                if (Runtime.CurrentCashCents < amountCents) return "cannot afford";
                Runtime.SpendCents(amountCents);
                return null;
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (Runtime == null) return "no runtime";
                if (units > Cap) return $"capped port refuses {units}u (cap {Cap})";
                int accepted = Runtime.AddCategoryStockUnits(categoryId, units);
                if (accepted < units) return $"store accepted {accepted}/{units}u {categoryId}: category full.";
                return null;
            }
            public int CategoryReceivableUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                if (stock == null) return 0;
                if (Cap < int.MaxValue) return Math.Min(Cap, stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : Cap);
                return stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : int.MaxValue;
            }
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class Chain
        {
            public List<string> Diag = new List<string>();
            public CustodyTransferLog Log;
            public EntityIdRegistry Ids = new EntityIdRegistry();

            // Millbrook + freight.
            public OffMapAidBusinessRuntime Supplier;
            public FakeBusinessCashPort BakeryCash;
            public OffMapAidCargoLot Lot;
            public BakeryShopRuntime Bakery = new BakeryShopRuntime(BakeryId);
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public MillbrookFlourFreightCustodyAdapter Adapter;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public EquipmentReservationService Reservations;
            public FreightProductionDriver Freight;
            public FreightShipmentHooks Hooks;
            public AnimalState Horse;
            public EquipmentAsset Wagon;
            public LogisticsShipmentState Shipment;
            public FreightServiceAgreement Agreement;
            public BusinessRuntimeState CarrierRuntime;
            public BusinessRuntimeState PayerRuntime;
            public JourneyModel JourneyModel;
            public OffMapJourneyAuthority Journeys;

            // Bakery production + store.
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public RuntimeStorePort StorePort;
            public Pass3StoreCatalog Catalog = new Pass3StoreCatalog();
            public Pass3WholesaleIntake Intake = new Pass3WholesaleIntake();
            public Series2BakeryProductionDriver ProductionDriver;

            // Shopping.
            public PopulationState Population;
            public HouseholdMembershipRegistry Membership;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdNeedRegistry Needs;
            public SupplierDirectory Directory;
            public JourneyModel ShoppingJourneys;
            public PersonScheduleTracker Tracker;
            public ShopperSellerKnowledge Knowledge;
            public HouseholdShoppingLoop Loop;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
        }

        private static OffMapAidBusinessDefinition MillbrookDefinition()
        {
            // Row values copied verbatim from the authored Millbrook asset
            // (10 lb / 18 bakery units / 48c / 230 starting), as in
            // MillbrookFlourFreightCustodyTests.
            var definition = ScriptableObject.CreateInstance<OffMapAidBusinessDefinition>();
            definition.BusinessId = "millbrook_grain_co";
            definition.DisplayName = "Millbrook Grain Co.";
            definition.PickupRouteMilesFromTown = 50f;
            definition.Inventory = new List<OffMapAidInventoryRow>
            {
                new OffMapAidInventoryRow
                {
                    ItemId = "bakery_flour_sack", DisplayName = "Flour",
                    InventoryUnit = "lb", StartingUnits = 230,
                    OrderUnit = "10 lb sack", InventoryUnitsPerOrderUnit = 10,
                    BakeryUnitsPerOrderUnit = 18, OrderPriceCents = 48,
                },
            };
            return definition;
        }

        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static BusinessRuntimeState Runtime(string id, int cash)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cash);
            return runtime;
        }

        private static Chain NewChain(CustodyTransferLog log)
        {
            var s = new Chain { Log = log };
            s.Supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            s.Supplier.CustodyLog = log;
            s.BakeryCash = new FakeBusinessCashPort(20000);
            s.Bakery.CustodyLog = log;
            s.Adapter = new MillbrookFlourFreightCustodyAdapter(s.Supplier, s.Bakery, s.Ids, log);

            // Freight (mirrors FreightDeliveryPaymentAuditTests.SetUp).
            s.Tasks = new TaskAuthority();
            s.Budgets = new WorkTimeBudgetStore();
            s.Budgets.EnsureDay(Day);
            s.Budgets.EnsureDay(Day + 1);
            s.Budgets.EnsureDay(Day + 2);
            s.Reservations = new EquipmentReservationService();
            s.Horse = new AnimalRegistry(s.Ids).RegisterAnimal(AnimalSpecies.Horse, "Draft", AnimalSex.Male,
                AnimalOwnerKind.Business, CarrierId, Day, "Custody chain fixture");
            Assert.IsNotNull(s.Horse);
            Assert.IsNull(HorseAuthority.Configure(s.Horse, HorseUseCapabilities.GeneralPurpose,
                "ethan-staging", "freight-company-stable", Day));
            s.Horse.TransportState = HorseTransportState.AttachedToWagon;
            s.Horse.AttachedWagonAssetId = WagonId;
            s.Wagon = new EquipmentAsset
            {
                AssetId = WagonId, Kind = "freight-wagon", DisplayName = "Opening Freight Wagon",
                Condition01 = 1f, OwnerKind = "business", OwnerId = CarrierId,
                LocationId = "ethan-staging", HitchPreparationShipmentId = ShipmentId,
                HitchingAnimalIds = new List<string> { s.Horse.AnimalId.ToString() }
            };
            Assert.IsNull(WagonAuthority.Configure(s.Wagon, WagonSubtype.GeneralFarmFreight.ToString(),
                450f, 4f, "ethan-staging"));
            s.CarrierRuntime = Runtime(CarrierId, 0);
            s.PayerRuntime = Runtime(BakeryId, 20000);
            s.Freight = new FreightProductionDriver();
            s.Freight.EnsureCompany(CarrierId, s.CarrierRuntime);
            s.Freight.RegisterBusinessRuntime(BakeryId, s.PayerRuntime);
            s.Freight.Service.RegisterWagon(s.Wagon);
            s.Hooks = new FreightShipmentHooks
            {
                Tasks = s.Tasks,
                Budgets = s.Budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                Reservations = s.Reservations,
                NowGameSeconds = Day * 86400f + 8 * 3600f,
                CommitSourceLot = (LogisticsShipmentState shipment, int qty, out string reason) =>
                    s.Adapter.CommitSourceLot(shipment, qty, out reason),
                ReceiveCargoLot = (LogisticsShipmentState shipment, int qty, out string reason) =>
                    s.Adapter.ReceiveCargoLot(shipment, qty, Day, out reason),
            };
            s.JourneyModel = new JourneyModel();
            Assert.IsNull(s.JourneyModel.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f)));
            RegionalTownRegistry.ApplyAll(s.JourneyModel, new List<string>());
            s.Journeys = new OffMapJourneyAuthority(s.JourneyModel);

            // Bakery production + store (mirrors BakeryFoodChainEndToEndTests.NewSlice).
            s.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            s.StoreRuntime.AddCashCents(5000);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            s.StoreRuntime.EnsureCategoryStock("animal_feed", 0, 0);
            s.BakeryRuntime.BindBusinessIdentity(BakeryId);
            s.BakeryRuntime.AddCashCents(2000);
            s.StorePort = new RuntimeStorePort { Runtime = s.StoreRuntime };
            s.StorePort.Prices["staple_food"] = 5;
            s.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                StoreUnitsPerItemUnit = 1,
            });
            s.ProductionDriver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));

            // Shopping (mirrors BakeryFoodChainEndToEndTests.NewSlice).
            s.Population = new PopulationState();
            s.Population.people.Add(new PersonState
            {
                id = ShopperPersonId, firstName = "Glen", lastName = "Test", age = 34,
                ageBand = AgeBand.Adult18Plus, householdId = HouseholdId,
            });
            s.Population.households.Add(new HouseholdState { id = HouseholdId });
            s.Membership = new HouseholdMembershipRegistry();
            Assert.IsNull(s.Membership.Register(new HouseholdMembership
            {
                MembershipId = 1, PersonId = ShopperPersonId, HouseholdId = HouseholdId, StartDayIndex = 0,
            }));
            s.Ledgers = new HouseholdLedgerRegistry();
            s.Inventories = new HouseholdInventoryRegistry();
            s.Needs = new HouseholdNeedRegistry();
            s.ShoppingJourneys = new JourneyModel();
            s.ShoppingJourneys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Test Home", 0f, 0f));
            s.ShoppingJourneys.RegisterLocation(new JourneyLocation("general-store", JourneyLocationKind.Store, "General Store", 2f, 0f));
            Assert.IsNull(s.ShoppingJourneys.AddEdge("home", "general-store", 2.0f, "river road"));
            // One shared TaskAuthority serves the freight service and the
            // shopping loop, as in production.
            HouseholdShoppingLoop.RegisterTaskDefinitions(s.Tasks);
            s.Tracker = new PersonScheduleTracker();
            s.Knowledge = new ShopperSellerKnowledge();
            s.Directory = new SupplierDirectory();
            GeneralStoreSupplierAdapter.RegisterIn(s.Directory, s.StorePort);
            s.Knowledge.LearnSupplier(ShopperPersonId, StoreInstanceId, "lives in the same town", 0);
            s.Loop = new HouseholdShoppingLoop(
                s.Population, s.Membership, s.Ledgers, s.Inventories, s.Needs,
                s.Directory, s.ShoppingJourneys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                new FinancialObligationAuthority(), s.Ids);
            s.Loop.CustodyLog = log;
            s.PersonLocations[ShopperPersonId] = "home";
            return s;
        }

        private static void PlaceFreightOrder(Chain s)
        {
            Assert.IsNull(s.Supplier.TryPlaceOrder(
                OrderRequestId, BakeryId, "Glen Pierce", Sacks, s.BakeryCash, 0, out OffMapAidCargoLot lot),
                "fixture order must place");
            s.Lot = lot;
            Assert.AreEqual(LotId, lot.LotId);
            Assert.AreEqual(Sacks, lot.PackageUnits);
            Assert.AreEqual(Sacks * 10, lot.InventoryUnits, "16 sacks x 10 lb");
            Assert.AreEqual(Sacks * 48, lot.TotalCostCents, "16 sacks x 48c");
        }

        private static void BuildFreightShipment(Chain s)
        {
            FreightServiceAgreementBook book = s.Freight.ServiceAgreements;
            var request = new FreightServiceRequest
            {
                RequestId = "request-1", RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = LotId, Quantity = Sacks, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetQuote(request.RequestId, new FreightServiceQuote
            {
                QuoteId = "quote-1", OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = FreightChargeCents, PayerBusinessId = BakeryId, PaymentTiming = "After successful delivery"
            }));
            Assert.IsNull(book.Accept(request.RequestId, EthanId, Day, 40000f, out s.Agreement));
            s.Shipment = s.Freight.CreateShipment(CarrierId, ShipmentId, "millbrook_grain_co", BakeryId, Sacks,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                s.Hooks, EntityId.For(EntityKind.Person, EthanId), new List<EntityId> { s.Horse.AnimalId },
                FreightChargeCents, BakeryId, s.Diag, out string shipmentRefusal,
                AgreementId, LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(s.Shipment, shipmentRefusal);
            Assert.IsNull(book.LinkShipment(s.Agreement.AgreementId, ShipmentId));
            Assert.IsNull(book.MarkPreparedForDispatch(s.Agreement.AgreementId, ShipmentId, 50000f));
            s.Freight.SetDispatchPlan(ShipmentId, EntityId.For(EntityKind.Person, EthanId),
                new List<EntityId> { s.Horse.AnimalId });
        }

        private static void BindAndAuthorize(Chain s)
        {
            string bind = WagonAuthority.TryAssignDraftTeam(s.Wagon,
                new List<AnimalState> { s.Horse },
                EntityId.For(EntityKind.Person, EthanId).ToString(), ShipmentId,
                new AnimalAvailabilityAuthority(s.Reservations), Day * 86400f + 8 * 3600f, float.MaxValue);
            Assert.IsNull(bind, "fixture bind must succeed");
            Assert.IsTrue(s.Freight.AuthorizeShipmentDispatch(ShipmentId, out _), "fixture authorization must succeed");
        }

        private static List<LogisticsShipmentStatus> TickToCompleted(Chain s)
        {
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && s.Shipment.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                s.Freight.AdvanceTick(86400f, s.Hooks, EntityId.For(EntityKind.Person, EthanId), s.Diag);
                trace.Add(s.Shipment.Status);
            }
            return trace;
        }

        // The normal production path up to the return-day tick loop: journey
        // departed day 10, lot committed at the supplier on D+1 (day 11)
        // through the adapter — the same call the pickup drive makes —
        // team bound and authorized, journey returned on its due day, the
        // off-map hold lifted.
        private static void DriveFreightToReturnDay(Chain s)
        {
            OffMapJourneyRecord record = s.Journeys.DepartVehicle(
                WagonId, "town-center", "Millbrook Grain Co.", 0,
                "Millbrook flour pickup", null,
                new List<string> { AgreementId },
                10, null, null, s.Diag);
            Assert.IsNotNull(record, "Millbrook departure should succeed: " + string.Join("; ", s.Diag));
            record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId] = Sacks;

            // The D+1 pickup's first half, as the drive performs it: commit
            // the lot through the adapter (day 11), then apply the loading
            // writes — exactly WorkLoading's post-commit writes, no task.
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, s.Shipment.PlannedQuantityUnits, out string reason, 11),
                reason);
            s.Shipment.sourceLotDebitApplied = true;
            s.Shipment.loadApplied = true;
            s.Shipment.sourceCommittedAtSchedule = true;
            s.Shipment.remainingQuantityUnits = s.Shipment.PlannedQuantityUnits;
            s.Shipment.physicalLocationId = "wagon:" + WagonId;

            BindAndAuthorize(s);
            List<OffMapJourneyRecord> returned = s.Journeys.CompleteDueJourneys(Day, null, s.Diag);
            Assert.AreEqual(1, returned.Count, "the journey must complete exactly once on its due day");
            Assert.AreEqual(OffMapJourneyState.Returned, record.State);
            s.Hooks.IsVehicleOffMap = _ => false;
        }

        private static void InstallOven(Chain s)
        {
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
        }

        private static void SeedNotionsAndFuel(Chain s)
        {
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag));
        }

        private static void BookAccount(Chain s, int dueDay)
        {
            var account = new BakeryWholesaleAccount
            {
                BuyerBusinessId = StoreInstanceId,
                BuyerType = BusinessType.GeneralStore,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = 24,
                DeliveryCadenceDays = 1,
                PricePerUnitCents = 2,
                Freshness = BakeryWholesaleFreshness.FreshOrDayOld,
                NextDeliveryDayIndex = dueDay,
            };
            Assert.IsNull(s.Bakery.AddWholesaleAccount(account, s.Diag), "account booking should succeed");
        }

        private static BakeryWholesaleDailyInputs InputsFor(Chain s, int dayIndex) =>
            new BakeryWholesaleDailyInputs
            {
                Bakery = s.Bakery,
                ProductionDriver = s.ProductionDriver,
                DayIndex = dayIndex,
                BakerKitUsable = true,
                OvenDefinition = WorkstationCatalog.BakeOven,
                FindOvenComponent = OvenFinder(),
                IdRegistry = s.Ids,
                StorePort = s.StorePort,
                StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                BakeryCashPort = new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                StoreBusinessId = StoreInstanceId,
                BakeryBusinessId = BakeryId,
                Intake = s.Intake,
                StoreCatalog = s.Catalog,
                StoreRuntimeState = s.StoreRuntime,
                BakeryRuntimeState = s.BakeryRuntime,
                StoreFlourUnitPriceCents = 29,
                MillbrookQuote = new MillbrookFlourQuote
                {
                    SacksAvailable = 0,
                    GoodsPricePerSackCents = null,
                    OneWayMiles = 50f,
                    FreightQuoteCents = 125,
                    FreightAvailable = false,
                    FreightUnavailableReason = "freight blocked: no Millbrook shipment has departed",
                },
                MillbrookSupplier = null,
                ImportFlourPricePerLbCents = 3,
                ImportTransitDays = 14,
                ImportOrderPathExists = false,
            };

        // The flour bin holds flour AND notions lots: find the flour lot by
        // name instead of indexing Lots[0].
        private static BakeryFlourLot FlourLot(Chain s)
        {
            BakeryFlourLot found = null;
            int flourCount = 0;
            foreach (BakeryFlourLot lot in s.Bakery.FlourStock.Lots)
            {
                if (lot != null && string.Equals(lot.FlourName, BakeryBreadCatalog.FlourItemId, StringComparison.Ordinal))
                {
                    flourCount++;
                    found = lot;
                }
            }
            Assert.AreEqual(1, flourCount, "exactly one flour lot is expected in the bin");
            Assert.IsNotNull(found);
            return found;
        }

    }
}
