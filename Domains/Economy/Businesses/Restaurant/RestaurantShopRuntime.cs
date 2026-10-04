using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Restaurant
{
    /// <summary>
    /// W2B: the production stages of a meal batch. Batches flow
    /// Planned → Prepped → Cooked. A batch stays parked at its stage on any
    /// shortfall — never worked on conjured stock, never silently dropped.
    /// </summary>
    public enum RestaurantMealBatchStage
    {
        Planned = 0,
        Prepped = 1,
        Cooked = 2,
        Scrapped = 3,
    }

    /// <summary>
    /// W2B: one meal batch. The batch holds its ingredients in custody from
    /// the prep stage (dispensed from named lots with provenance lines), so
    /// cooking never re-dispenses. The custody lines move into the prepared
    /// meal lot's provenance at cooking.
    /// </summary>
    [Serializable]
    public sealed class RestaurantMealBatch
    {
        public string BatchId = string.Empty;
        public string MealId = string.Empty; // rest.beef-stew / rest.roast-plate / rest.creamed-stew
        public int ScheduledDayIndex;
        public RestaurantMealBatchStage Stage = RestaurantMealBatchStage.Planned;

        /// <summary>Ingredients dispensed into this batch's custody at prep, with provenance.</summary>
        public List<RestaurantFoodDispenseLine> IngredientsInCustody = new List<RestaurantFoodDispenseLine>();

        public List<string> StageLog = new List<string>();

        public RestaurantMealBatch() { }

        public bool IsActive => Stage == RestaurantMealBatchStage.Planned || Stage == RestaurantMealBatchStage.Prepped;
    }

    /// <summary>
    /// W2B: condition bands for prepared meals. Hot food does not wait: the
    /// day it is cooked it is fresh, the next day it is leftover (discounted),
    /// after that it is spoiled waste — never served (Canon §9.1: spoilage is
    /// a real outcome). Lots never mix, so new cooking never rejuvenates old
    /// leftovers.
    /// </summary>
    public enum RestaurantMealCondition
    {
        Unspecified = 0,
        Fresh = 1,   // Cooked today. Full price.
        DayOld = 2,  // Leftover. Discounted; serve it today.
        Spoiled = 3, // Waste. Never served.
    }

    /// <summary>
    /// W2B: one prepared-meal lot — a cooked batch's output with its own
    /// prepared day and condition. Aging is per-lot.
    /// </summary>
    [Serializable]
    public sealed class RestaurantMealLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string MealId = string.Empty;
        public int PreparedDayIndex;
        public int MealsRemaining;
        public RestaurantMealCondition Condition = RestaurantMealCondition.Fresh;

        /// <summary>Upstream ingredient chains cooked into this lot (full provenance).</summary>
        public List<string> InputProvenance = new List<string>();

        public RestaurantMealLot() { }

        /// <summary>
        /// Ages the lot to the given day. Prepared meals spoil fast: fresh
        /// the day they are cooked, leftover the next day, spoiled after.
        /// Spoiled lots stay on the books with provenance retained for audit —
        /// they are never served and never silently vanish.
        /// </summary>
        public void AgeToDay(int dayIndex)
        {
            int age = dayIndex - PreparedDayIndex;
            Condition = age <= 0 ? RestaurantMealCondition.Fresh
                : age == 1 ? RestaurantMealCondition.DayOld
                : RestaurantMealCondition.Spoiled;
        }

        public bool CanServe => Condition != RestaurantMealCondition.Spoiled && MealsRemaining > 0;

        public int TakeMeals(int requested)
        {
            int taken = Math.Min(Math.Max(0, requested), MealsRemaining);
            MealsRemaining -= taken;
            return taken;
        }
    }

    /// <summary>
    /// W2B: one served meal — the retail record. The caller settles these as
    /// ordinary ledger outflows; money moves only through ledger authorities.
    /// The diner's person id is recorded so served meals genuinely satisfy
    /// NPC nutrition (Canon §2.5) via the W2B nutrition-link seam.
    /// </summary>
    [Serializable]
    public sealed class RestaurantServedMealRecord
    {
        public string ServedId = string.Empty;
        public EntityId LotId = EntityId.Invalid;
        public string MealId = string.Empty;
        public int DayIndex;
        public int PersonId; // the diner — nutrition credit goes to a real person
        public int PriceCents;
        public RestaurantMealCondition ConditionServedAt = RestaurantMealCondition.Fresh;
        public string ProvenanceChain = string.Empty;

        public RestaurantServedMealRecord() { }
    }

    /// <summary>
    /// W2B: one spoiled-meal write-off. Spoiled lots stay on the books as
    /// waste (never served), and the write-off appears plainly for the ledger.
    /// </summary>
    [Serializable]
    public sealed class RestaurantWasteRecord
    {
        public EntityId LotId = EntityId.Invalid;
        public string MealId = string.Empty;
        public int Meals;
        public int PreparedDayIndex;
        public int WastedDayIndex;
        public string ProvenanceChain = string.Empty;

        public RestaurantWasteRecord() { }
    }

    /// <summary>
    /// W2B: one kitchen. A kitchen IS a workstation instance (WorkstationId
    /// "restaurant-kitchen": stove-range + cookware-set + pantry-bins):
    /// readiness derives from its component assets, never from a flag. Each
    /// ready kitchen offers <see cref="RestaurantMealCatalog.BatchesPerKitchenPerDay"/>
    /// batch cookings per day — the daily cook is capacity-gated by ready
    /// kitchens.
    /// </summary>
    [Serializable]
    public sealed class RestaurantKitchen
    {
        public int KitchenIndex;
        public WorkstationInstance Station = new WorkstationInstance();

        public RestaurantKitchen() { }
    }

    /// <summary>
    /// W2B: the per-instance restaurant runtime. Holds the kitchen pool
    /// (kitchens as workstations gating daily cooking capacity), the pantry
    /// (ingredient lots with provenance), the retail price schedule, the
    /// meal-batch queue, the prepared-meal shelf (aging lots), the served-meal
    /// records, and the served-meal day ledger that feeds the nutrition link.
    ///
    /// Boundary: this runtime owns the kitchen/pantry/batch/shelf/service
    /// layer only. Any generic resolution for BusinessType.Restaurant in
    /// SharedBusinessRuntimeManager is untouched — W2B routes around it the
    /// way W1C/W2A route around the tailor's and bakery's shared resolution.
    /// Seating/table throughput is out of scope (W2B: meal service + nutrition
    /// link; the kitchen is the production bottleneck analog).
    /// </summary>
    public sealed class RestaurantShopRuntime
    {
        /// <summary>TUNING: the cook's hands-on minutes per day (calibration, Canon Part XV; mirrors the baker's professional day).</summary>
        public const int CookMinutesPerDay = 600;

        private readonly string businessInstanceId;
        private readonly RestaurantFoodStock foodStock = new RestaurantFoodStock();
        private readonly List<RestaurantKitchen> kitchens = new List<RestaurantKitchen>();
        private readonly List<RestaurantMealBatch> batches = new List<RestaurantMealBatch>();
        private readonly List<RestaurantMealLot> mealShelf = new List<RestaurantMealLot>();
        private readonly List<RestaurantServedMealRecord> servedMeals = new List<RestaurantServedMealRecord>();
        private readonly List<RestaurantWasteRecord> waste = new List<RestaurantWasteRecord>();
        private readonly List<string> wasteNotedLotIds = new List<string>();
        private readonly RestaurantMealDayLedger mealDayLedger = new RestaurantMealDayLedger();
        private RestaurantPriceSchedule prices = new RestaurantPriceSchedule();
        private int batchSeq;
        private int serveSeq;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public RestaurantFoodStock FoodStock => foodStock;
        public IReadOnlyList<RestaurantKitchen> Kitchens => kitchens;
        public IReadOnlyList<RestaurantMealBatch> Batches => batches;
        public IReadOnlyList<RestaurantMealLot> MealShelf => mealShelf;
        public IReadOnlyList<RestaurantServedMealRecord> ServedMeals => servedMeals;
        public IReadOnlyList<RestaurantWasteRecord> Waste => waste;
        public RestaurantPriceSchedule Prices => prices;

        /// <summary>The served-meal day ledger — the producer side of the nutrition link (IRestaurantMealDaySource).</summary>
        public IRestaurantMealDaySource MealDaySource => mealDayLedger;

        public RestaurantShopRuntime(string businessInstanceId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
        }

        public void SetPriceSchedule(RestaurantPriceSchedule schedule)
        {
            prices = schedule ?? new RestaurantPriceSchedule();
        }

        /// <summary>
        /// Installs a kitchen: a workstation instance of "restaurant-kitchen"
        /// with its component assets named. Returns a rejection string, or
        /// null on success (the kitchen index is kitchens.Count - 1 afterwards).
        /// </summary>
        public string AddKitchen(string spaceId, List<string> componentAssetIds, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(spaceId))
                return "RestaurantShopRuntime: a kitchen needs a functional space — a room alone never grants the workstation.";

            var kitchen = new RestaurantKitchen
            {
                KitchenIndex = kitchens.Count,
                Station = new WorkstationInstance
                {
                    InstanceId = $"{businessInstanceId}-kitchen-{kitchens.Count}",
                    WorkstationId = RestaurantMealCatalog.RestaurantKitchenStationId,
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = spaceId,
                },
            };
            if (componentAssetIds != null)
            {
                foreach (string assetId in componentAssetIds)
                {
                    kitchen.Station.InstallComponent(assetId);
                }
            }

            kitchens.Add(kitchen);
            diag.Add($"RestaurantShopRuntime: kitchen {kitchen.KitchenIndex} installed in '{spaceId}' (workstation {kitchen.Station.InstanceId}).");
            return null;
        }

        /// <summary>
        /// Counts kitchens whose workstation instance evaluates ready against
        /// the catalog definition. Loud per-kitchen diagnostics — an unready
        /// kitchen is named, never silently skipped.
        /// </summary>
        public int CountReadyKitchens(
            WorkstationDefinition kitchenDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            int ready = 0;
            foreach (var kitchen in kitchens)
            {
                var reasons = new List<string>();
                string notReady = kitchen.Station.EvaluateReady(kitchenDefinition, findComponent, reasons);
                if (notReady == null)
                {
                    ready++;
                }
                else
                {
                    diag.Add($"RestaurantShopRuntime: kitchen {kitchen.KitchenIndex} not ready — {notReady}");
                }
            }

            return ready;
        }

        /// <summary>
        /// Plans meal batches of a menu item. Returns the batch ids, or null
        /// with a LOUD diagnostic — callers tell them apart (ids never contain
        /// the "RestaurantShopRuntime:" prefix). One batch = one pot's worth
        /// of meals; no ingredients are dispensed yet — that happens at prep.
        /// </summary>
        public List<string> PlanMealBatch(string mealId, int batchCount, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!RestaurantMealCatalog.IsKnownMeal(mealId))
                return PlanRefused(diag, $"unknown menu item '{mealId}' — only the house menu is cooked.");
            if (batchCount <= 0)
                return PlanRefused(diag, "batch count must be positive — nothing planned.");

            RestaurantMealSpec spec = RestaurantMealCatalog.GetSpec(mealId);
            var ids = new List<string>();
            for (int i = 0; i < batchCount; i++)
            {
                var batch = new RestaurantMealBatch
                {
                    BatchId = $"{businessInstanceId}-batch-{batchSeq++}",
                    MealId = mealId,
                    ScheduledDayIndex = dayIndex,
                    Stage = RestaurantMealBatchStage.Planned,
                };
                batch.StageLog.Add($"day {dayIndex}: batch planned ({spec.DisplayName}, {spec.MealsYieldPerBatch} meals)");
                batches.Add(batch);
                ids.Add(batch.BatchId);
            }

            diag.Add($"RestaurantShopRuntime: planned {batchCount} meal batch(es) of {spec.DisplayName} (day {dayIndex}).");
            return ids;
        }

        private List<string> PlanRefused(List<string> diag, string reason)
        {
            string message = $"RestaurantShopRuntime: meal planning refused — {reason}";
            diag.Add(message);
            return null;
        }

        /// <summary>
        /// Runs one restaurant day:
        /// 1. Ages the prepared-meal shelf — newly spoiled lots are written
        ///    off as waste, loudly, with provenance retained.
        /// 2. Advances planned batches FIFO: prep dispenses ingredients into
        ///    the batch's custody (loud refusal on shortfall — the batch
        ///    stays planned), then cooking claims one batch slot on a ready
        ///    kitchen. Stages cascade while the cook's labor minutes last.
        /// Each WorkDay call is one day: kitchen batch budgets reset at its
        /// start. Returns the number of batches cooked. The id registry is
        /// optional: when provided, meal lots take real HF-1 lot ids; without
        /// it the lot is still a real commercial lot but its id is honestly
        /// reported as unregistered (GrainDealer.SellGrain T1A precedent).
        /// </summary>
        public int WorkDay(
            int dayIndex,
            bool cookKitUsable,
            WorkstationDefinition kitchenDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag,
            EntityIdRegistry idRegistry = null)
        {
            diag = diag ?? diagnostics;

            AgeShelf(dayIndex, diag);
            mealDayLedger.PruneBefore(dayIndex, diag);

            if (!cookKitUsable)
            {
                diag.Add($"RestaurantShopRuntime: no usable cook's hand kit — the house cannot cook today (NX-1 teeth gate). {CountActiveBatches()} batch(es) still open.");
                return 0;
            }

            // Ready kitchens and their batch-cooking budgets for today.
            var kitchenBatchesLeft = new Dictionary<int, int>();
            foreach (var kitchen in kitchens)
            {
                var reasons = new List<string>();
                if (kitchen.Station.EvaluateReady(kitchenDefinition, findComponent, reasons) == null)
                {
                    kitchenBatchesLeft[kitchen.KitchenIndex] = RestaurantMealCatalog.BatchesPerKitchenPerDay;
                }
            }

            if (kitchenBatchesLeft.Count == 0)
            {
                diag.Add("RestaurantShopRuntime: no ready kitchen — cooking requires the stove; nothing cooks today.");
            }

            int laborRemaining = CookMinutesPerDay;
            int cooked = 0;
            var snapshot = new List<RestaurantMealBatch>(batches);

            foreach (var batch in snapshot)
            {
                if (!batch.IsActive) continue;

                while (TryAdvanceOneBatch(batch, dayIndex, ref laborRemaining, kitchenBatchesLeft, diag, idRegistry))
                {
                    cooked++;
                }
            }

            return cooked;
        }

        /// <summary>
        /// Ages every prepared-meal lot to the given day. Newly spoiled lots
        /// get one loud write-off each (deduped across days and save/load
        /// cycles); spoiled lots stay on the books as waste — never served,
        /// never silently removed, provenance retained.
        /// </summary>
        public void AgeShelf(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (var lot in mealShelf)
            {
                if (lot == null) continue;
                lot.AgeToDay(dayIndex);
                if (lot.Condition == RestaurantMealCondition.Spoiled
                    && lot.MealsRemaining > 0
                    && !wasteNotedLotIds.Contains(lot.LotId.ToString()))
                {
                    var chains = new List<string>();
                    if (lot.InputProvenance != null) chains.AddRange(lot.InputProvenance);
                    waste.Add(new RestaurantWasteRecord
                    {
                        LotId = lot.LotId,
                        MealId = lot.MealId,
                        Meals = lot.MealsRemaining,
                        PreparedDayIndex = lot.PreparedDayIndex,
                        WastedDayIndex = dayIndex,
                        ProvenanceChain = string.Join(" | ", chains),
                    });
                    wasteNotedLotIds.Add(lot.LotId.ToString());
                    diag.Add($"RestaurantShopRuntime: lot {lot.LotId} ({lot.MealId}, {lot.MealsRemaining} meal(s), prepared day {lot.PreparedDayIndex}) spoiled — written off as waste (day {dayIndex}). Never served.");
                }
            }
        }

        /// <summary>
        /// Advances one batch one stage if its preconditions hold. Returns
        /// true when the batch finished COOKING (the caller counts it);
        /// prep-only advances return false so a prep-followed-by-cook in one
        /// day counts exactly one cooked batch.
        /// </summary>
        private bool TryAdvanceOneBatch(
            RestaurantMealBatch batch, int dayIndex, ref int laborRemaining,
            Dictionary<int, int> kitchenBatchesLeft, List<string> diag,
            EntityIdRegistry idRegistry)
        {
            RestaurantMealSpec spec = RestaurantMealCatalog.GetSpec(batch.MealId);
            if (string.IsNullOrEmpty(spec.MealId))
            {
                diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} names unknown menu item '{batch.MealId}' — stays parked, never guessed.");
                return false;
            }

            switch (batch.Stage)
            {
                case RestaurantMealBatchStage.Planned:
                    // Prep: ingredients into the batch's custody, FIFO lots.
                    var taken = new List<RestaurantFoodDispenseLine>();
                    if (!DispenseIngredient(batch, spec, RestaurantMealCatalog.MeatItemId, spec.MeatLbsPerBatch, taken, dayIndex, diag)
                        || !DispenseIngredient(batch, spec, RestaurantMealCatalog.BreadItemId, spec.BreadLoavesPerBatch, taken, dayIndex, diag)
                        || !DispenseIngredient(batch, spec, RestaurantMealCatalog.ProduceItemId, spec.ProduceUnitsPerBatch, taken, dayIndex, diag)
                        || !DispenseIngredient(batch, spec, RestaurantMealCatalog.DairyItemId, spec.DairyUnitsPerBatch, taken, dayIndex, diag))
                    {
                        // Honesty on partial dispense: return what was taken
                        // as a fresh lot naming the reversal; never silently
                        // absorbed.
                        ReturnCustodyToPantry(batch, taken, dayIndex, diag, idRegistry);
                        diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} ({spec.DisplayName}) refused — pantry shortfall. Ingredients returned. Stays planned.");
                        return false;
                    }

                    if (!SpendLabor(ref laborRemaining, spec.PrepMinutes, diag, batch, "prepping"))
                    {
                        ReturnCustodyToPantry(batch, taken, dayIndex, diag, idRegistry);
                        diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} prep needs {spec.PrepMinutes}m — stays planned for tomorrow.");
                        return false;
                    }

                    batch.IngredientsInCustody.AddRange(taken);
                    SetStage(batch, RestaurantMealBatchStage.Prepped, dayIndex,
                        $"prepped ({spec.PrepMinutes}m, ingredients from {taken.Count} lot line(s))");
                    // Prepped — cascade into cooking below (falls through by
                    // recursion, not fallthrough; stage is now Prepped).
                    break;

                case RestaurantMealBatchStage.Prepped:
                    int kitchenIndex = ClaimBatchSlot(kitchenBatchesLeft);
                    if (kitchenIndex < 0)
                    {
                        diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} ({spec.DisplayName}) waits on a kitchen batch slot — all ready kitchens are fully booked today. Stays prepped.");
                        return false;
                    }

                    if (!SpendLabor(ref laborRemaining, spec.CookMinutes, diag, batch, "cooking"))
                    {
                        kitchenBatchesLeft[kitchenIndex]++;
                        diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} cook needs {spec.CookMinutes}m — stays prepped for tomorrow.");
                        return false;
                    }

                    CookBatch(batch, spec, kitchenIndex, dayIndex, diag, idRegistry);
                    return true;

                default:
                    return false;
            }

            // A batch that just prepped cascades into the cook stage immediately.
            return TryAdvanceOneBatch(batch, dayIndex, ref laborRemaining, kitchenBatchesLeft, diag, idRegistry);
        }

        private bool DispenseIngredient(
            RestaurantMealBatch batch, RestaurantMealSpec spec, string foodName, int units,
            List<RestaurantFoodDispenseLine> taken, int dayIndex, List<string> diag)
        {
            if (units <= 0) return true;
            var lines = foodStock.TryDispenseUnits(foodName, units, dayIndex, diag);
            if (lines == null)
            {
                diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} ({spec.DisplayName}) needs {units} × {foodName} — shortfall.");
                return false;
            }

            taken.AddRange(lines);
            return true;
        }

        /// <summary>
        /// Returns just-dispensed custody lines to the pantry as a fresh lot
        /// naming the reversal — provenance is preserved, never silently
        /// absorbed. The returned lot takes a real lot id when the caller
        /// provides the registry; without one its id is honestly reported as
        /// unregistered (GrainDealer.SellGrain T1A precedent).
        /// </summary>
        private void ReturnCustodyToPantry(
            RestaurantMealBatch batch, List<RestaurantFoodDispenseLine> lines, int dayIndex,
            List<string> diag, EntityIdRegistry idRegistry)
        {
            if (lines == null) return;
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var chains = new List<string>();
            foreach (var line in lines)
            {
                if (line == null) continue;
                if (!byName.TryGetValue(line.FoodName, out int soFar)) soFar = 0;
                byName[line.FoodName] = soFar + Math.Max(0, line.UnitsTaken);
                if (!string.IsNullOrWhiteSpace(line.ProvenanceChain)) chains.Add(line.ProvenanceChain);
            }

            foreach (var kvp in byName)
            {
                string rejection = foodStock.ReceiveLot(new RestaurantFoodLot
                {
                    LotId = idRegistry != null ? idRegistry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                    FoodName = kvp.Key,
                    Units = kvp.Value,
                    AcquiredDayIndex = dayIndex,
                    ImportOrderId = $"return-{batch.BatchId}",
                    OriginName = "Returned to pantry from failed batch prep",
                    SupplierNote = "Originally: " + string.Join(" | ", chains),
                    IsBootstrapEndowment = false,
                }, diag);
                if (rejection != null)
                {
                    diag.Add($"RestaurantShopRuntime: custody return for batch {batch.BatchId} refused — {rejection}");
                }
            }
        }

        /// <summary>Cooks a prepped batch: one kitchen batch slot → one prepared-meal lot with full input provenance.</summary>
        private void CookBatch(
            RestaurantMealBatch batch, RestaurantMealSpec spec, int kitchenIndex, int dayIndex,
            List<string> diag, EntityIdRegistry idRegistry)
        {
            var lot = new RestaurantMealLot
            {
                // A real HF-1 lot id when the caller provides the registry;
                // without one the lot is still a real commercial lot but its id
                // is honestly reported as unregistered (T1A precedent).
                LotId = idRegistry != null ? idRegistry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                MealId = batch.MealId,
                PreparedDayIndex = dayIndex,
                MealsRemaining = spec.MealsYieldPerBatch,
                Condition = RestaurantMealCondition.Fresh,
            };
            foreach (var line in batch.IngredientsInCustody)
            {
                if (line != null) lot.InputProvenance.Add($"{line.UnitsTaken}× {line.FoodName} from {line.ProvenanceChain}");
            }

            mealShelf.Add(lot);
            // The ingredients are now IN the meals: custody lines move to the
            // lot, so the batch never double-counts and the pantry never sees
            // them again.
            batch.IngredientsInCustody.Clear();

            SetStage(batch, RestaurantMealBatchStage.Cooked, dayIndex,
                $"cooked in kitchen {kitchenIndex} ({spec.CookMinutes}m) → {spec.MealsYieldPerBatch} meal(s)");
            diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} cooked — {spec.MealsYieldPerBatch} meal(s) of {spec.DisplayName} (day {dayIndex}, kitchen {kitchenIndex}).");
        }

        /// <summary>
        /// Serves one meal of a menu item to a real person. Oldest sellable
        /// lot first (leftover before fresh, so nothing good is wasted while
        /// leftovers sit). Leftover lots price at the schedule's day-old
        /// discount automatically; spoiled lots are never served. The served
        /// meal is reported to the day ledger so it genuinely satisfies the
        /// diner's nutrition (Canon §2.5). Returns the served-meal record —
        /// the caller settles it as an ordinary ledger outflow — or null with
        /// a LOUD diagnostic on shortfall.
        /// </summary>
        public RestaurantServedMealRecord ServeMeal(string mealId, int personId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!RestaurantMealCatalog.IsKnownMeal(mealId))
            {
                diag.Add($"RestaurantShopRuntime: service refused — unknown menu item '{mealId}'.");
                return null;
            }

            if (personId <= 0)
            {
                diag.Add("RestaurantShopRuntime: service refused — a served meal needs a real person id; the nutrition link credits real diners only.");
                return null;
            }

            AgeShelf(dayIndex, diag);

            var ordered = new List<RestaurantMealLot>();
            foreach (var lot in mealShelf)
            {
                if (lot != null
                    && lot.CanServe
                    && string.Equals(lot.MealId, mealId, StringComparison.Ordinal))
                {
                    ordered.Add(lot);
                }
            }

            ordered.Sort((a, b) => a.PreparedDayIndex.CompareTo(b.PreparedDayIndex));

            foreach (var lot in ordered)
            {
                if (lot.TakeMeals(1) <= 0) continue;

                int price = lot.ConditionServedAtPrice(prices);
                var record = new RestaurantServedMealRecord
                {
                    ServedId = $"{businessInstanceId}-served-{serveSeq++}",
                    LotId = lot.LotId,
                    MealId = lot.MealId,
                    DayIndex = dayIndex,
                    PersonId = personId,
                    PriceCents = price,
                    ConditionServedAt = lot.Condition,
                    ProvenanceChain = lot.InputProvenance != null
                        ? string.Join(" | ", lot.InputProvenance)
                        : string.Empty,
                };
                servedMeals.Add(record);

                string ledgerRefusal = mealDayLedger.ReportServedMeal(personId, dayIndex, diag);
                if (ledgerRefusal != null)
                {
                    diag.Add($"RestaurantShopRuntime: {ledgerRefusal}");
                }

                diag.Add($"RestaurantShopRuntime: served 1 {lot.MealId} to person {personId} (day {dayIndex}, {lot.Condition}, {price}¢).");
                return record;
            }

            diag.Add($"RestaurantShopRuntime: no {mealId} to serve person {personId} — the shelf is empty (finite supply, Canon §9.2).");
            return null;
        }

        private bool SpendLabor(ref int laborRemaining, int minutes, List<string> diag, RestaurantMealBatch batch, string stageName)
        {
            if (minutes <= 0) return true;
            if (laborRemaining < minutes)
            {
                diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} {stageName} needs {minutes}m, only {laborRemaining}m left today.");
                return false;
            }

            laborRemaining -= minutes;
            return true;
        }

        /// <summary>
        /// Claims one batch-cooking slot on the lowest-index ready kitchen
        /// with a free slot. Deterministic; returns -1 (with a diagnostic left
        /// to the caller) when every ready kitchen is fully booked today.
        /// </summary>
        private static int ClaimBatchSlot(Dictionary<int, int> kitchenBatchesLeft)
        {
            int best = int.MaxValue;
            foreach (var kvp in kitchenBatchesLeft)
            {
                if (kvp.Value > 0 && kvp.Key < best) best = kvp.Key;
            }

            if (best == int.MaxValue) return -1;
            kitchenBatchesLeft[best]--;
            return best;
        }

        private void SetStage(RestaurantMealBatch batch, RestaurantMealBatchStage stage, int dayIndex, string note)
        {
            batch.Stage = stage;
            batch.StageLog.Add($"day {dayIndex}: → {stage} ({note})");
        }

        private int CountActiveBatches()
        {
            int count = 0;
            foreach (var batch in batches)
            {
                if (batch.IsActive) count++;
            }

            return count;
        }

        /// <summary>
        /// Exposes the restaurant's kitchens to the NX-1 equipment gate: each
        /// kitchen is registered under "restaurant-kitchen-{n}", and the
        /// declared "restaurant-kitchen" id aliases the first READY kitchen at
        /// call time (point-in-time resolution — the runtime stays the
        /// authority for per-batch slot assignment). With no ready kitchen the
        /// alias is left unregistered so the gate refuses honestly.
        /// </summary>
        public void PopulateWorkstations(
            BusinessWorkstations registry,
            WorkstationDefinition kitchenDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (registry == null)
            {
                diag.Add("RestaurantShopRuntime: no workstation registry — kitchens not exposed to the equipment gate.");
                return;
            }

            RestaurantKitchen firstReady = null;
            foreach (var kitchen in kitchens)
            {
                registry.RegisterInstance(new WorkstationInstance
                {
                    InstanceId = $"{businessInstanceId}-kitchen-{kitchen.KitchenIndex}",
                    WorkstationId = $"{RestaurantMealCatalog.RestaurantKitchenStationId}-{kitchen.KitchenIndex}",
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = kitchen.Station.SpaceId,
                    ComponentAssetIds = new List<string>(kitchen.Station.ComponentAssetIds),
                });

                if (firstReady == null)
                {
                    var reasons = new List<string>();
                    if (kitchen.Station.EvaluateReady(kitchenDefinition, findComponent, reasons) == null)
                    {
                        firstReady = kitchen;
                    }
                }
            }

            if (firstReady != null)
            {
                registry.RegisterInstance(new WorkstationInstance
                {
                    InstanceId = firstReady.Station.InstanceId,
                    WorkstationId = RestaurantMealCatalog.RestaurantKitchenStationId,
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = firstReady.Station.SpaceId,
                    ComponentAssetIds = new List<string>(firstReady.Station.ComponentAssetIds),
                });
                diag.Add($"RestaurantShopRuntime: '{RestaurantMealCatalog.RestaurantKitchenStationId}' resolves to kitchen {firstReady.KitchenIndex} (first ready, point-in-time).");
            }
            else
            {
                diag.Add("RestaurantShopRuntime: no ready kitchen — the workstation alias is left unregistered; the gate refuses honestly.");
            }
        }

        /// <summary>Throughput readout: the kitchen-and-labor problem, plainly stated.</summary>
        public string CoverageSummary()
        {
            return $"Restaurant {businessInstanceId}: {kitchens.Count} kitchen(s), " +
                $"{CountActiveBatches()} open batch(es), {mealShelf.Count} meal lot(s) on the shelf, " +
                $"{foodStock.UnitsOnHand(RestaurantMealCatalog.MeatItemId)} lb meat on hand.";
        }

        public void CloseDay(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            diag.Add($"Restaurant {businessInstanceId}: day {dayIndex} — {CoverageSummary()}");
        }

        #region Save / Load
        [Serializable]
        public sealed class RestaurantShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public RestaurantPriceSchedule Prices = new RestaurantPriceSchedule();
            public RestaurantFoodStock.RestaurantFoodStockSaveDto FoodStock = new RestaurantFoodStock.RestaurantFoodStockSaveDto();
            public List<RestaurantKitchen> Kitchens = new List<RestaurantKitchen>();
            public List<RestaurantMealBatch> Batches = new List<RestaurantMealBatch>();
            public List<RestaurantMealLot> MealShelf = new List<RestaurantMealLot>();
            public List<RestaurantServedMealRecord> ServedMeals = new List<RestaurantServedMealRecord>();
            public List<RestaurantWasteRecord> Waste = new List<RestaurantWasteRecord>();
            public List<string> WasteNotedLotIds = new List<string>();
            public RestaurantMealDayLedger.RestaurantMealDayLedgerSaveDto MealDayLedger = new RestaurantMealDayLedger.RestaurantMealDayLedgerSaveDto();
            public int BatchSeq;
            public int ServeSeq;
        }

        public RestaurantShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new RestaurantShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                Prices = prices,
                FoodStock = foodStock.CaptureSaveDto(),
                MealDayLedger = mealDayLedger.CaptureSaveDto(),
                BatchSeq = batchSeq,
                ServeSeq = serveSeq,
            };
            dto.Kitchens.AddRange(kitchens);
            // Only live batches rehydrate; cooked batches are history and
            // their meal lots (below) are the persisted audit trail.
            foreach (var batch in batches)
            {
                if (batch != null && batch.IsActive) dto.Batches.Add(batch);
            }

            dto.MealShelf.AddRange(mealShelf);
            dto.ServedMeals.AddRange(servedMeals);
            dto.Waste.AddRange(waste);
            dto.WasteNotedLotIds.AddRange(wasteNotedLotIds);
            return dto;
        }

        public void LoadFromSaveDto(RestaurantShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            foodStock.LoadFromSaveDto(dto.FoodStock);
            mealDayLedger.LoadFromSaveDto(dto.MealDayLedger);
            if (dto.Prices != null) prices = dto.Prices;
            batchSeq = Math.Max(0, dto.BatchSeq);
            serveSeq = Math.Max(0, dto.ServeSeq);
            kitchens.Clear();
            if (dto.Kitchens != null)
            {
                foreach (var kitchen in dto.Kitchens)
                {
                    if (kitchen == null) continue;
                    if (kitchen.Station == null) kitchen.Station = new WorkstationInstance();
                    kitchens.Add(kitchen);
                }
            }

            batches.Clear();
            if (dto.Batches != null)
            {
                foreach (var batch in dto.Batches)
                {
                    if (batch != null) batches.Add(batch);
                }
            }

            mealShelf.Clear();
            if (dto.MealShelf != null)
            {
                foreach (var lot in dto.MealShelf)
                {
                    if (lot != null) mealShelf.Add(lot);
                }
            }

            servedMeals.Clear();
            if (dto.ServedMeals != null)
            {
                foreach (var record in dto.ServedMeals)
                {
                    if (record != null) servedMeals.Add(record);
                }
            }

            waste.Clear();
            if (dto.Waste != null)
            {
                foreach (var record in dto.Waste)
                {
                    if (record != null) waste.Add(record);
                }
            }

            wasteNotedLotIds.Clear();
            if (dto.WasteNotedLotIds != null)
            {
                wasteNotedLotIds.AddRange(dto.WasteNotedLotIds);
            }
        }
        #endregion
    }

    /// <summary>
    /// W2B: condition-aware pricing helper for prepared-meal lots. Keeps the
    /// aging price logic next to the lot type.
    /// </summary>
    public static class RestaurantMealPricing
    {
        /// <summary>
        /// The price a prepared-meal lot serves for at its CURRENT condition:
        /// fresh → fresh price; day-old → schedule discount; spoiled → zero
        /// (spoiled lots never serve anyway — this is the guard, not the plan).
        /// </summary>
        public static int ConditionServedAtPrice(this RestaurantMealLot lot, RestaurantPriceSchedule prices)
        {
            if (lot == null) return 0;
            switch (lot.Condition)
            {
                case RestaurantMealCondition.Fresh:
                    return RestaurantMealCatalog.GetFreshPriceCents(lot.MealId, prices);
                case RestaurantMealCondition.DayOld:
                    return RestaurantMealCatalog.GetDayOldPriceCents(lot.MealId, prices);
                default:
                    return 0;
            }
        }
    }
}
