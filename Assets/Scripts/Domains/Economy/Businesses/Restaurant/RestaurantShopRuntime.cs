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

        /// <summary>D1E: the declared service this batch was planned for ("dinner", "supper", ...) — planning intent, may be empty.</summary>
        public string TargetServiceWindowId = string.Empty;

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

        /// <summary>D1E: the quality band of this lot — whose hands made it (Canon §8.1B meal quality).</summary>
        public RestaurantMealQualityBand QualityBand = RestaurantMealQualityBand.House;

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

        /// <summary>D1E: the quality band of the meal served (Canon §8.1B; feeds the quality-aware nutrition link).</summary>
        public RestaurantMealQualityBand QualityBand = RestaurantMealQualityBand.House;

        /// <summary>D1E: the declared service this meal was eaten at ("dinner", "supper", ...) — may be empty.</summary>
        public string ServiceWindowId = string.Empty;

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
    ///
    /// D1E depth: cook roster (staffed cooks set capacity, quality and prep
    /// efficiency; empty roster = proprietor cooks), stove-fuel store (batch
    /// cooking burns fuel), meal quality bands (price + nutrition weight),
    /// declared mealtime services, the regulars book (visits, never credit),
    /// and standing-order / reorder-signal procurement.
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

        /// <summary>D1E: the stove-fuel store (Canon §8.1B: food service demands fuel).</summary>
        private readonly RestaurantFuelStock fuelStock = new RestaurantFuelStock();

        /// <summary>D1E: the cook roster (Canon §8.1E). Empty roster = the proprietor cooks (W2B fallback).</summary>
        private readonly RestaurantCookStaff cookStaff = new RestaurantCookStaff();

        /// <summary>D1E: the house's quality policy (Canon §8.1B meal quality).</summary>
        private readonly RestaurantMealQualityPolicy qualityPolicy = new RestaurantMealQualityPolicy();

        /// <summary>D1E: declared mealtime services (Canon §2.3: meals are day events).</summary>
        private readonly RestaurantServiceSchedule serviceSchedule = new RestaurantServiceSchedule();

        /// <summary>D1E: the regulars book — visit facts only, no credit (fork boundary).</summary>
        private readonly RestaurantRegulars regulars = new RestaurantRegulars();

        /// <summary>D1E: standing ingredient orders — the buyer side of supply agreements (Canon §8.1B).</summary>
        private readonly List<RestaurantStandingOrder> standingOrders = new List<RestaurantStandingOrder>();

        /// <summary>D1E: the pantry's reorder policy (threshold signals).</summary>
        private readonly RestaurantResupplyPolicy resupplyPolicy = new RestaurantResupplyPolicy();

        private RestaurantPriceSchedule prices = new RestaurantPriceSchedule();
        private int batchSeq;
        private int serveSeq;

        /// <summary>D1E: when true (default), batch cooking burns stove fuel; without fuel the batch stays prepped, loudly.</summary>
        public bool RequireKitchenFuel = true;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public RestaurantFoodStock FoodStock => foodStock;
        public IReadOnlyList<RestaurantKitchen> Kitchens => kitchens;
        public IReadOnlyList<RestaurantMealBatch> Batches => batches;
        public IReadOnlyList<RestaurantMealLot> MealShelf => mealShelf;
        public IReadOnlyList<RestaurantServedMealRecord> ServedMeals => servedMeals;
        public IReadOnlyList<RestaurantWasteRecord> Waste => waste;
        public RestaurantPriceSchedule Prices => prices;

        /// <summary>D1E: the stove-fuel store.</summary>
        public RestaurantFuelStock FuelStock => fuelStock;

        /// <summary>D1E: the cook roster.</summary>
        public RestaurantCookStaff CookStaff => cookStaff;

        /// <summary>D1E: the house's quality policy.</summary>
        public RestaurantMealQualityPolicy QualityPolicy => qualityPolicy;

        /// <summary>D1E: declared mealtime services.</summary>
        public RestaurantServiceSchedule ServiceSchedule => serviceSchedule;

        /// <summary>D1E: the regulars book (visit facts only — no credit).</summary>
        public RestaurantRegulars Regulars => regulars;

        /// <summary>D1E: standing ingredient orders.</summary>
        public IReadOnlyList<RestaurantStandingOrder> StandingOrders => standingOrders;

        /// <summary>D1E: the pantry's reorder policy.</summary>
        public RestaurantResupplyPolicy ResupplyPolicy => resupplyPolicy;

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
        /// D1E: names a cook to the roster (Canon §8.1E). Returns a rejection
        /// string, or null on success.
        /// </summary>
        public string AssignCook(int personId, int cookingSkillLevel, int dayIndex, List<string> diag, int minutesPerDay = 0)
        {
            return cookStaff.AssignCook(personId, cookingSkillLevel, dayIndex, diag ?? diagnostics, minutesPerDay);
        }

        /// <summary>
        /// D1E: a cook leaves the roster. Returns a rejection string, or null
        /// on success. When the last cook leaves, the kitchen stops loudly.
        /// </summary>
        public string RemoveCook(int personId, int dayIndex, List<string> diag)
        {
            return cookStaff.RemoveCook(personId, dayIndex, diag ?? diagnostics);
        }

        /// <summary>
        /// D1E: declares a mealtime service for a day (Canon §2.3). Returns a
        /// rejection string, or null on success.
        /// </summary>
        public string DeclareServiceWindow(string serviceId, string displayName, int dayIndex, int plannedMeals, List<string> diag)
        {
            return serviceSchedule.DeclareServiceWindow(serviceId, displayName, dayIndex, plannedMeals, diag ?? diagnostics);
        }

        /// <summary>
        /// D1E: records a standing ingredient order — the buyer side of a
        /// supply agreement (Canon §8.1B). Returns a rejection string, or
        /// null on success. The order is policy, not fulfillment.
        /// </summary>
        public string AddStandingOrder(RestaurantStandingOrder order, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (order == null) return "RestaurantShopRuntime: null standing order refused.";
            if (string.IsNullOrWhiteSpace(order.OrderId))
                return "RestaurantShopRuntime: a standing order needs an id.";
            if (string.IsNullOrWhiteSpace(order.FoodName))
                return "RestaurantShopRuntime: a standing order needs a food name (restaurant-meat/bread/produce/dairy).";
            if (string.IsNullOrWhiteSpace(order.SupplierBusinessId))
                return "RestaurantShopRuntime: a standing order must name its supplier — no orphan orders.";
            if (order.UnitsPerDelivery <= 0 || order.CadenceDays <= 0)
                return $"RestaurantShopRuntime: standing order '{order.OrderId}' needs positive units and cadence.";

            foreach (var existing in standingOrders)
            {
                if (existing != null && string.Equals(existing.OrderId, order.OrderId, StringComparison.OrdinalIgnoreCase))
                    return $"RestaurantShopRuntime: standing order '{order.OrderId}' is already on the books — not duplicated.";
            }

            standingOrders.Add(order);
            diag.Add($"RestaurantShopRuntime: standing order '{order.OrderId}' recorded — {order.UnitsPerDelivery} × {order.FoodName} " +
                $"every {order.CadenceDays} day(s) from {order.SupplierBusinessId} ({order.SupplierKind}), next due day {order.NextDueDayIndex}.");
            return null;
        }

        /// <summary>D1E: standing orders due on the day — data for the caller to place real orders.</summary>
        public List<RestaurantStandingOrder> DueStandingOrders(int dayIndex, List<string> diag)
        {
            return RestaurantResupplyEvaluator.DueStandingOrders(standingOrders, dayIndex, diag ?? diagnostics);
        }

        /// <summary>D1E: advances a standing order's schedule after the caller placed the real order.</summary>
        public string MarkStandingOrderPlaced(string orderId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (var order in standingOrders)
            {
                if (order != null && string.Equals(order.OrderId, orderId, StringComparison.OrdinalIgnoreCase))
                    return RestaurantResupplyEvaluator.MarkOrderPlaced(order, dayIndex, diag);
            }

            return $"RestaurantShopRuntime: no standing order '{orderId}' on the books — nothing advanced.";
        }

        /// <summary>D1E: pantry reorder signals — data, never orders.</summary>
        public List<RestaurantResupplySignal> EvaluateResupplySignals(int dayIndex, List<string> diag)
        {
            return RestaurantResupplyEvaluator.EvaluateSignals(foodStock, resupplyPolicy, dayIndex, diag ?? diagnostics);
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
        /// D1E: the optional service window id records which declared service
        /// the batch is planned for (planning intent only — it never cooks
        /// anything by itself).
        /// </summary>
        public List<string> PlanMealBatch(string mealId, int batchCount, int dayIndex, List<string> diag, string serviceWindowId = null)
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
                    TargetServiceWindowId = serviceWindowId ?? string.Empty,
                };
                batch.StageLog.Add($"day {dayIndex}: batch planned ({spec.DisplayName}, {spec.MealsYieldPerBatch} meals)" +
                    (string.IsNullOrWhiteSpace(serviceWindowId) ? string.Empty : $" for service '{serviceWindowId}'"));
                batches.Add(batch);
                ids.Add(batch.BatchId);
            }

            diag.Add($"RestaurantShopRuntime: planned {batchCount} meal batch(es) of {spec.DisplayName} (day {dayIndex})" +
                (string.IsNullOrWhiteSpace(serviceWindowId) ? "." : $" for service '{serviceWindowId}'."));
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
        /// D1E: the labor budget comes from the cook roster when the house
        /// has ever named a cook (empty roster = the proprietor cooks, the
        /// W2B fallback); a roster with no cook on duty stops the kitchen
        /// loudly. Batch cooking burns stove fuel (RequireKitchenFuel); the
        /// cooked lot's quality band follows the leading cook's skill.
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

            int laborRemaining;
            int leadingCookSkill;
            if (!cookStaff.HasRoster)
            {
                // W2B fallback: no named cook — the proprietor cooks (Canon §8.1E).
                laborRemaining = CookMinutesPerDay;
                leadingCookSkill = 0;
            }
            else if (!cookStaff.HasActiveCook(dayIndex))
            {
                diag.Add($"RestaurantShopRuntime: the cook roster is empty today — nobody is at the stove. {CountActiveBatches()} batch(es) stay parked (Canon §8.1E: loss of the cook damages the house).");
                return 0;
            }
            else
            {
                laborRemaining = cookStaff.TotalMinutesToday(dayIndex);
                leadingCookSkill = cookStaff.LeadingCookSkill(dayIndex);
                diag.Add($"RestaurantShopRuntime: {laborRemaining} cook-minute(s) on duty today (leading skill {leadingCookSkill}).");
            }

            int cooked = 0;
            var snapshot = new List<RestaurantMealBatch>(batches);

            foreach (var batch in snapshot)
            {
                if (!batch.IsActive) continue;

                while (TryAdvanceOneBatch(batch, dayIndex, leadingCookSkill, ref laborRemaining, kitchenBatchesLeft, diag, idRegistry))
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
        /// D1E: prep minutes are skill-adjusted (a strong cook is faster);
        /// the cook stage first checks stove fuel — a cold stove leaves the
        /// batch prepped, loudly.
        /// </summary>
        private bool TryAdvanceOneBatch(
            RestaurantMealBatch batch, int dayIndex, int leadingCookSkill, ref int laborRemaining,
            Dictionary<int, int> kitchenBatchesLeft, List<string> diag,
            EntityIdRegistry idRegistry)
        {
            RestaurantMealSpec spec = RestaurantMealCatalog.GetSpec(batch.MealId);
            if (string.IsNullOrEmpty(spec.MealId))
            {
                diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} names unknown menu item '{batch.MealId}' — stays parked, never guessed.");
                return false;
            }

            int prepMinutes = RestaurantCookStaff.EffectivePrepMinutes(spec.PrepMinutes, leadingCookSkill);

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

                    if (!SpendLabor(ref laborRemaining, prepMinutes, diag, batch, "prepping"))
                    {
                        ReturnCustodyToPantry(batch, taken, dayIndex, diag, idRegistry);
                        diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} prep needs {prepMinutes}m — stays planned for tomorrow.");
                        return false;
                    }

                    batch.IngredientsInCustody.AddRange(taken);
                    SetStage(batch, RestaurantMealBatchStage.Prepped, dayIndex,
                        $"prepped ({prepMinutes}m, ingredients from {taken.Count} lot line(s))");
                    // Prepped — cascade into cooking below (falls through by
                    // recursion, not fallthrough; stage is now Prepped).
                    break;

                case RestaurantMealBatchStage.Prepped:
                    if (!CheckStoveFuel(batch, spec, dayIndex, diag))
                    {
                        // Cold stove: the batch waits, prepped, for fuel.
                        return false;
                    }

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

                    CookBatch(batch, spec, kitchenIndex, leadingCookSkill, dayIndex, diag, idRegistry);
                    return true;

                default:
                    return false;
            }

            // A batch that just prepped cascades into the cook stage immediately.
            return TryAdvanceOneBatch(batch, dayIndex, leadingCookSkill, ref laborRemaining, kitchenBatchesLeft, diag, idRegistry);
        }

        /// <summary>
        /// D1E: the stove-fuel precondition for cooking. Checks availability
        /// WITHOUT dispensing (the dispense happens at the actual cooking, so
        /// a labor shortfall never strands burned fuel). No fuel → the batch
        /// stays prepped, loudly. Skipped entirely when RequireKitchenFuel is
        /// false (contexts where stove fuel is not modeled).
        /// </summary>
        private bool CheckStoveFuel(RestaurantMealBatch batch, RestaurantMealSpec spec, int dayIndex, List<string> diag)
        {
            if (!RequireKitchenFuel) return true;
            int need = RestaurantMealCatalog.FuelUnitsPerBatchCooking;
            int onHand = fuelStock.UnitsOnHand(RestaurantMealCatalog.FuelItemId);
            if (onHand < need)
            {
                diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} ({spec.DisplayName}) waits on stove fuel — " +
                    $"only {onHand} unit(s) of '{RestaurantMealCatalog.FuelItemId}' on hand, need {need}. " +
                    "The stove stays cold; the batch stays prepped. Reorder through the fuel dealer or an import order.");
                return false;
            }

            return true;
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

        /// <summary>
        /// Cooks a prepped batch: one kitchen batch slot → one prepared-meal
        /// lot with full input provenance. D1E: burns the stove fuel (the
        /// availability check already passed, so the dispense cannot fail
        /// here) and stamps the lot's quality band from the leading cook's
        /// skill (Canon §8.1B meal quality).
        /// </summary>
        private void CookBatch(
            RestaurantMealBatch batch, RestaurantMealSpec spec, int kitchenIndex, int leadingCookSkill, int dayIndex,
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
                QualityBand = qualityPolicy.DeriveBand(leadingCookSkill),
            };
            foreach (var line in batch.IngredientsInCustody)
            {
                if (line != null) lot.InputProvenance.Add($"{line.UnitsTaken}× {line.FoodName} from {line.ProvenanceChain}");
            }

            if (RequireKitchenFuel)
            {
                var fuelLines = fuelStock.TryDispenseUnits(
                    RestaurantMealCatalog.FuelItemId, RestaurantMealCatalog.FuelUnitsPerBatchCooking, dayIndex, diag);
                if (fuelLines != null)
                {
                    foreach (var fuelLine in fuelLines)
                    {
                        if (fuelLine != null) lot.InputProvenance.Add($"{fuelLine.UnitsTaken}× {fuelLine.FuelName} (stove fuel) from {fuelLine.ProvenanceChain}");
                    }
                }
            }

            mealShelf.Add(lot);
            // The ingredients are now IN the meals: custody lines move to the
            // lot, so the batch never double-counts and the pantry never sees
            // them again.
            batch.IngredientsInCustody.Clear();

            SetStage(batch, RestaurantMealBatchStage.Cooked, dayIndex,
                $"cooked in kitchen {kitchenIndex} ({spec.CookMinutes}m) → {spec.MealsYieldPerBatch} meal(s), quality {lot.QualityBand}");
            diag.Add($"RestaurantShopRuntime: batch {batch.BatchId} cooked — {spec.MealsYieldPerBatch} meal(s) of {spec.DisplayName} (day {dayIndex}, kitchen {kitchenIndex}, quality {lot.QualityBand}).");
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
        /// D1E: the optional service window id names the declared mealtime
        /// service the meal was eaten at; the served price is quality-aware
        /// (Canon §8.1B); the diner's visit is noted in the regulars book
        /// (visits only — never credit); the meal's quality weight rides the
        /// day ledger for the quality-aware nutrition link.
        /// </summary>
        public RestaurantServedMealRecord ServeMeal(string mealId, int personId, int dayIndex, List<string> diag, string serviceWindowId = null)
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

                // The service is claimed only when it was declared: an
                // undeclared service id is served anyway (the meal is real)
                // but is not recorded as a service, loudly.
                bool serviceClaimed = serviceSchedule.NoteServedCover(serviceWindowId, dayIndex, diag);

                var record = new RestaurantServedMealRecord
                {
                    ServedId = $"{businessInstanceId}-served-{serveSeq++}",
                    LotId = lot.LotId,
                    MealId = lot.MealId,
                    DayIndex = dayIndex,
                    PersonId = personId,
                    PriceCents = price,
                    ConditionServedAt = lot.Condition,
                    QualityBand = lot.QualityBand,
                    ServiceWindowId = serviceClaimed ? (serviceWindowId ?? string.Empty) : string.Empty,
                    ProvenanceChain = lot.InputProvenance != null
                        ? string.Join(" | ", lot.InputProvenance)
                        : string.Empty,
                };
                servedMeals.Add(record);

                string ledgerRefusal = mealDayLedger.ReportServedMeal(
                    personId, dayIndex, RestaurantMealQualityPolicy.QualityWeight01(lot.QualityBand), diag);
                if (ledgerRefusal != null)
                {
                    diag.Add($"RestaurantShopRuntime: {ledgerRefusal}");
                }

                string visitRefusal = regulars.NoteVisit(personId, mealId, dayIndex, diag);
                if (visitRefusal != null)
                {
                    diag.Add($"RestaurantShopRuntime: {visitRefusal}");
                }

                diag.Add($"RestaurantShopRuntime: served 1 {lot.MealId} to person {personId} (day {dayIndex}, {lot.Condition}, quality {lot.QualityBand}, {price}¢" +
                    (string.IsNullOrWhiteSpace(serviceWindowId) ? ")." : $", service '{serviceWindowId}')."));
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

            // D1E depth state.
            public RestaurantFuelStock.RestaurantFuelStockSaveDto FuelStock = new RestaurantFuelStock.RestaurantFuelStockSaveDto();
            public RestaurantCookStaff.RestaurantCookStaffSaveDto CookStaff = new RestaurantCookStaff.RestaurantCookStaffSaveDto();
            public RestaurantMealQualityPolicy QualityPolicy = new RestaurantMealQualityPolicy();
            public RestaurantServiceSchedule.RestaurantServiceScheduleSaveDto ServiceSchedule = new RestaurantServiceSchedule.RestaurantServiceScheduleSaveDto();
            public RestaurantRegulars.RestaurantRegularsSaveDto Regulars = new RestaurantRegulars.RestaurantRegularsSaveDto();
            public List<RestaurantStandingOrder> StandingOrders = new List<RestaurantStandingOrder>();
            public RestaurantResupplyPolicy ResupplyPolicy = new RestaurantResupplyPolicy();
            public bool RequireKitchenFuel = true;
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
                FuelStock = fuelStock.CaptureSaveDto(),
                CookStaff = cookStaff.CaptureSaveDto(),
                QualityPolicy = qualityPolicy,
                ServiceSchedule = serviceSchedule.CaptureSaveDto(),
                Regulars = regulars.CaptureSaveDto(),
                ResupplyPolicy = resupplyPolicy,
                RequireKitchenFuel = RequireKitchenFuel,
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
            if (standingOrders != null)
            {
                foreach (var order in standingOrders)
                {
                    if (order != null) dto.StandingOrders.Add(order);
                }
            }

            return dto;
        }

        public void LoadFromSaveDto(RestaurantShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            foodStock.LoadFromSaveDto(dto.FoodStock);
            mealDayLedger.LoadFromSaveDto(dto.MealDayLedger);
            fuelStock.LoadFromSaveDto(dto.FuelStock);
            cookStaff.LoadFromSaveDto(dto.CookStaff);
            serviceSchedule.LoadFromSaveDto(dto.ServiceSchedule);
            regulars.LoadFromSaveDto(dto.Regulars);
            if (dto.QualityPolicy != null)
            {
                qualityPolicy.FineMinCookSkillLevel = dto.QualityPolicy.FineMinCookSkillLevel;
                qualityPolicy.HouseMinCookSkillLevel = dto.QualityPolicy.HouseMinCookSkillLevel;
            }

            if (dto.ResupplyPolicy != null) CopyResupplyPolicy(dto.ResupplyPolicy);
            RequireKitchenFuel = dto.RequireKitchenFuel;
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

            standingOrders.Clear();
            if (dto.StandingOrders != null)
            {
                foreach (var order in dto.StandingOrders)
                {
                    if (order != null) standingOrders.Add(order);
                }
            }
        }

        /// <summary>D1E: copies a resupply policy's fields (the policy object itself is never aliased).</summary>
        private void CopyResupplyPolicy(RestaurantResupplyPolicy source)
        {
            resupplyPolicy.MeatThresholdUnits = source.MeatThresholdUnits;
            resupplyPolicy.MeatOrderUnits = source.MeatOrderUnits;
            resupplyPolicy.BreadThresholdUnits = source.BreadThresholdUnits;
            resupplyPolicy.BreadOrderUnits = source.BreadOrderUnits;
            resupplyPolicy.ProduceThresholdUnits = source.ProduceThresholdUnits;
            resupplyPolicy.ProduceOrderUnits = source.ProduceOrderUnits;
            resupplyPolicy.DairyThresholdUnits = source.DairyThresholdUnits;
            resupplyPolicy.DairyOrderUnits = source.DairyOrderUnits;
        }
        #endregion
    }

    /// <summary>
    /// W2B: condition-aware pricing helper for prepared-meal lots. Keeps the
    /// aging price logic next to the lot type.
    /// D1E: prices are quality-aware (Canon §8.1B meal quality) — the
    /// quality premium/discount applies to the fresh price, and the day-old
    /// condition discount applies on top of the quality price.
    /// </summary>
    public static class RestaurantMealPricing
    {
        /// <summary>
        /// D1E: the fresh price of a menu item at a quality band — the
        /// schedule price, moved by the band's premium/discount. Unspecified
        /// and House price at the schedule price exactly (W2B behavior).
        /// </summary>
        public static int QualityAdjustedFreshPriceCents(string mealId, RestaurantMealQualityBand band, RestaurantPriceSchedule prices)
        {
            int fresh = RestaurantMealCatalog.GetFreshPriceCents(mealId, prices);
            prices = prices ?? new RestaurantPriceSchedule();
            switch (band)
            {
                case RestaurantMealQualityBand.Fine:
                    return fresh * (100 + prices.FinePremiumPct) / 100;
                case RestaurantMealQualityBand.Rough:
                    return fresh * (100 - prices.RoughDiscountPct) / 100;
                default:
                    return fresh;
            }
        }

        /// <summary>
        /// The price a prepared-meal lot serves for at its CURRENT condition
        /// and quality: fresh → quality-adjusted fresh price; day-old →
        /// the schedule's day-old discount applied to the quality price;
        /// spoiled → zero (spoiled lots never serve anyway — this is the
        /// guard, not the plan).
        /// </summary>
        public static int ConditionServedAtPrice(this RestaurantMealLot lot, RestaurantPriceSchedule prices)
        {
            if (lot == null) return 0;
            switch (lot.Condition)
            {
                case RestaurantMealCondition.Fresh:
                    return QualityAdjustedFreshPriceCents(lot.MealId, lot.QualityBand, prices);
                case RestaurantMealCondition.DayOld:
                    int qualityFresh = QualityAdjustedFreshPriceCents(lot.MealId, lot.QualityBand, prices);
                    prices = prices ?? new RestaurantPriceSchedule();
                    return qualityFresh * prices.DayOldDiscountPct / 100;
                default:
                    return 0;
            }
        }
    }
}
