using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>Phase B: purchasing-need lifecycle. Append-only.</summary>
    public enum PurchasingNeedStatus
    {
        Open = 0,
        Fulfilled = 1,
        Cancelled = 2,
    }

    /// <summary>
    /// Phase B: a REAL purchasing need. This is demand, not a sale: it carries
    /// no money, names no store, and posts no revenue anywhere. Phase C's
    /// shopping loop consumes open needs and executes them through an acting
    /// Person (Canon 13.4).
    /// </summary>
    [Serializable]
    public sealed class HouseholdPurchasingNeed
    {
        public int NeedSequence;
        public int HouseholdId;
        public string ItemId = string.Empty;
        public int UnitsNeeded;
        /// <summary>Necessities are not reduced by discretionary economizing.</summary>
        public bool IsNecessity = true;
        public float Urgency01;
        public string ReasonSummary = string.Empty;
        public int CreatedDayIndex;
        public int LastEvaluatedDayIndex;
        public PurchasingNeedStatus Status;
        public List<string> SuggestedResponses = new List<string>();
    }

    /// <summary>Phase B: what supply options a household actually has.</summary>
    public struct HouseholdSupplyAccess
    {
        public bool HasGeneralStore;
        public bool HasOffMapAccess;
        public bool CanProduce;
        public bool HasCash;
    }

    /// <summary>
    /// Phase B (Real People): shortage detection. Projects each household's
    /// needs from actual members, current inventory, expected consumption,
    /// income/resources, upcoming obligations, season, known supply access,
    /// and purchasing plans. When reserves go inadequate, it creates a REAL
    /// purchasing need object (consumable by Phase C's shopping loop).
    /// NEED IS NOT A SALE — demand never adds money to any store.
    /// </summary>
    public sealed class HouseholdShortageMonitor
    {
        /// <summary>Days of supply below which a need opens (configurable).</summary>
        public float LowDaysThreshold { get; set; } = 7f;

        /// <summary>Preferred reserve to buy toward once a shortage opens (configurable).</summary>
        public float TargetDaysThreshold { get; set; } = 7f;

        /// <summary>Days of supply at/above which an open need is fulfilled (configurable).</summary>
        public float FulfilledDaysThreshold { get; set; } = 14f;

        private readonly HouseholdNeedRegistry needs;
        private readonly List<string> diagnostics = new List<string>();

        public HouseholdShortageMonitor(HouseholdNeedRegistry needs)
        {
            this.needs = needs ?? throw new ArgumentNullException(nameof(needs));
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Evaluates one household for one day. expectedDailyUseByItem maps
        /// item id → expected units per day (from members, season, obligations).
        /// isColdSeason flags winter fuel pressure. upcomingObligationCents and
        /// expectedIncomeCents inform the suggested responses, never invent money.
        /// Plan 02 Slice 2: <paramref name="isRecognizedByKnownSeller"/>, when
        /// supplied, annotates a shortage with whether a legitimately known
        /// seller recognizes the item. Seller knowledge controls whether a
        /// shopping attempt can succeed; it must not erase an actual food or
        /// fuel shortage. Recognition is never inferred from current stock,
        /// so recognized-but-out-of-stock demand remains visible as well. A
        /// null predicate leaves seller availability unknown.
        /// I1-M1 (2026-11-17): <paramref name="mealPriorityOrder"/>, when
        /// supplied, allocates food needs in meal-priority order against one
        /// shared cover total, because meals consume one item per serving,
        /// first available in priority order -- foods are substitutes, not
        /// additions. The held total counts every priority food already in
        /// the larder, so a household stocked with a lower-priority food
        /// does not demand a full target of the top food on top of it.
        /// Without it each item's need was sized for the full
        /// target independently and one household demanded roughly six times
        /// the food it eats. <paramref name="isPurchasableNow"/>, when
        /// supplied, says whether the item can actually be bought today
        /// (recognized by a known seller and currently stocked); a
        /// higher-priority need counts toward the shared cover only when it
        /// is purchasable, so a stockout falls back to the next food instead
        /// of blocking it. A null purchasability predicate counts a need as
        /// cover. Both parameters are optional: callers that pass neither
        /// get exactly today's per-item behaviour.
        /// </summary>
        public void Evaluate(
            HouseholdState household,
            HouseholdInventory inventory,
            Dictionary<string, int> expectedDailyUseByItem,
            bool isColdSeason,
            HouseholdSupplyAccess supplyAccess,
            int upcomingObligationCents,
            int expectedIncomeCents,
            int dayIndex,
            Func<string, bool> isRecognizedByKnownSeller = null,
            IReadOnlyList<string> mealPriorityOrder = null,
            Func<string, bool> isPurchasableNow = null)
        {
            if (household == null || inventory == null)
            {
                diagnostics.Add("Evaluate: household or inventory missing — no needs evaluated.");
                return;
            }

            int day = Mathf.Max(0, dayIndex);
            if (expectedDailyUseByItem != null)
            {
                if (mealPriorityOrder == null)
                {
                    foreach (KeyValuePair<string, int> use in expectedDailyUseByItem)
                    {
                        EvaluateItem(household, inventory, use.Key, Mathf.Max(0, use.Value),
                            isColdSeason, supplyAccess, upcomingObligationCents, expectedIncomeCents, day,
                            isRecognizedByKnownSeller);
                    }
                }
                else
                {
                    // I1-M1: one shared cover total, allocated in meal-priority
                    // order. The held total counts every priority food the
                    // household already holds (meals eat any of them, first
                    // available in priority order); planned days accumulate
                    // only from higher-priority needs that are purchasable,
                    // so a stockout falls back to the next food instead of
                    // blocking it. Items outside the priority list keep
                    // today's independent evaluation; fuel is evaluated
                    // separately below and never shares food cover.
                    float totalHeldDays = 0f;
                    foreach (string itemId in mealPriorityOrder)
                    {
                        if (string.IsNullOrWhiteSpace(itemId)
                            || !expectedDailyUseByItem.TryGetValue(itemId, out int dailyUse)
                            || dailyUse <= 0)
                        {
                            continue;
                        }

                        totalHeldDays += inventory.EstimateDaysOfSupply(itemId, dailyUse);
                    }

                    float plannedDays = 0f;
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string itemId in mealPriorityOrder)
                    {
                        if (string.IsNullOrWhiteSpace(itemId)
                            || !expectedDailyUseByItem.TryGetValue(itemId, out int dailyUse))
                        {
                            continue;
                        }

                        seen.Add(itemId);
                        EvaluateAllocatedItem(household, inventory, itemId, Mathf.Max(0, dailyUse),
                            isColdSeason, supplyAccess, upcomingObligationCents, expectedIncomeCents, day,
                            isRecognizedByKnownSeller, isPurchasableNow, totalHeldDays, ref plannedDays);
                    }

                    foreach (KeyValuePair<string, int> use in expectedDailyUseByItem)
                    {
                        if (seen.Contains(use.Key))
                        {
                            continue;
                        }

                        EvaluateItem(household, inventory, use.Key, Mathf.Max(0, use.Value),
                            isColdSeason, supplyAccess, upcomingObligationCents, expectedIncomeCents, day,
                            isRecognizedByKnownSeller);
                    }
                }
            }

            // Fuel gets a cold-season urgency bump even when it has no
            // explicit daily-use entry (heating is evaluated separately).
            if (isColdSeason)
            {
                EvaluateItem(household, inventory, HouseholdItemCatalog.FuelWoodId, 1,
                    true, supplyAccess, upcomingObligationCents, expectedIncomeCents, day,
                    isRecognizedByKnownSeller);
            }
        }

        /// <summary>
        /// Plan 02 Slice 2: builds the recognition predicate the shortage
        /// drive passes into Evaluate — the same rule the shopping loop uses
        /// to find a known seller (FindKnownItemSuppliers): a supplier in
        /// the directory that a household member legitimately knows AND that
        /// recognizes the item (RecognizesItem / CategoryForItem non-empty).
        /// Current stock is never consulted: a recognized-but-out-of-stock
        /// item is real demand. With no directory, no knowledge, or no
        /// members, nothing is recognized and every need is suppressed until
        /// a seller is learned or registered.
        /// </summary>
        public static Func<string, bool> KnownSellerRecognitionPredicate(
            SupplierDirectory suppliers,
            ShopperSellerKnowledge knowledge,
            IEnumerable<int> memberIds)
        {
            var members = new HashSet<int>();
            if (memberIds != null)
            {
                foreach (int memberId in memberIds)
                {
                    members.Add(memberId);
                }
            }

            return itemId =>
            {
                if (string.IsNullOrWhiteSpace(itemId) || suppliers == null || knowledge == null || members.Count == 0)
                {
                    return false;
                }

                foreach (IGoodsSupplier supplier in suppliers.All)
                {
                    if (supplier == null || !(supplier is IItemResolvingSupplier resolving))
                    {
                        continue;
                    }

                    bool recognized = supplier is IItemCatalogSupplier catalog
                        ? catalog.RecognizesItem(itemId)
                        : !string.IsNullOrWhiteSpace(resolving.CategoryForItem(itemId));
                    if (!recognized)
                    {
                        continue;
                    }

                    foreach (int memberId in members)
                    {
                        if (knowledge.Knows(memberId, supplier.SupplierBusinessId))
                        {
                            return true;
                        }
                    }
                }

                return false;
            };
        }

        /// <summary>
        /// I1-M1 (2026-11-17): builds the purchasability predicate the
        /// shortage drive passes into Evaluate alongside the recognition
        /// predicate — the same known-seller rule, plus current stock:
        /// a supplier in the directory that a household member legitimately
        /// knows, that recognizes the item, and that currently holds at
        /// least one unit of it. Stock is consulted here only to decide
        /// whether a higher-priority need can actually be filled today (and
        /// therefore counts toward the shared cover); a recognized-but-
        /// out-of-stock item still opens its own need, exactly as the
        /// recognition predicate leaves it.
        /// </summary>
        public static Func<string, bool> KnownSellerPurchasabilityPredicate(
            SupplierDirectory suppliers,
            ShopperSellerKnowledge knowledge,
            IEnumerable<int> memberIds)
        {
            var members = new HashSet<int>();
            if (memberIds != null)
            {
                foreach (int memberId in memberIds)
                {
                    members.Add(memberId);
                }
            }

            return itemId =>
            {
                if (string.IsNullOrWhiteSpace(itemId) || suppliers == null || knowledge == null || members.Count == 0)
                {
                    return false;
                }

                foreach (IGoodsSupplier supplier in suppliers.All)
                {
                    if (supplier == null || !(supplier is IItemResolvingSupplier resolving))
                    {
                        continue;
                    }

                    bool recognized = supplier is IItemCatalogSupplier catalog
                        ? catalog.RecognizesItem(itemId)
                        : !string.IsNullOrWhiteSpace(resolving.CategoryForItem(itemId));
                    if (!recognized || resolving.ItemStockUnits(itemId) <= 0)
                    {
                        continue;
                    }

                    foreach (int memberId in members)
                    {
                        if (knowledge.Knows(memberId, supplier.SupplierBusinessId))
                        {
                            return true;
                        }
                    }
                }

                return false;
            };
        }

        /// <summary>
        /// I1-M1 (2026-11-17): one food item's share of the household's
        /// shared cover. <paramref name="totalHeldDays"/> is the days of
        /// food the household already holds across all priority foods --
        /// its own stock is already counted in that total, because meals
        /// eat any priority food, first available in priority order. The
        /// effective cover adds the days higher-priority needs will add
        /// when they are bought (<paramref name="plannedDays"/>), so the
        /// household demands the food it actually eats: the top food covers
        /// the shortfall first and lower foods open only for what remains.
        /// A created need counts toward the cover only when the item is
        /// purchasable today (null predicate counts as purchasable), so a
        /// stockout falls back to the next food instead of blocking it.
        /// Infinite cover (daily use at or below zero) fulfils the item,
        /// exactly as today's per-item evaluation does, and is never added
        /// to the planned total.
        /// </summary>
        private void EvaluateAllocatedItem(
            HouseholdState household,
            HouseholdInventory inventory,
            string itemId,
            int dailyUseUnits,
            bool isColdSeason,
            HouseholdSupplyAccess supplyAccess,
            int upcomingObligationCents,
            int expectedIncomeCents,
            int dayIndex,
            Func<string, bool> isRecognizedByKnownSeller,
            Func<string, bool> isPurchasableNow,
            float totalHeldDays,
            ref float plannedDays)
        {
            if (string.IsNullOrWhiteSpace(itemId) || HouseholdItemCatalog.Get(itemId) == null)
            {
                return;
            }

            // The item's own held stock is already inside totalHeldDays,
            // so the effective cover is that total plus what higher-
            // priority needs will add when bought.
            float effectiveDays = dailyUseUnits > 0
                ? totalHeldDays + plannedDays
                : float.PositiveInfinity;

            HouseholdPurchasingNeed openNeed = needs.GetOpenNeed(household.id, itemId);
            if (effectiveDays >= FulfilledDaysThreshold)
            {
                if (openNeed != null)
                {
                    openNeed.Status = PurchasingNeedStatus.Fulfilled;
                    openNeed.LastEvaluatedDayIndex = dayIndex;
                }

                return;
            }

            // Adequate for now, against the shared cover rather than this
            // item alone: keep the open need fresh but do not grow it.
            if (effectiveDays > LowDaysThreshold)
            {
                if (openNeed != null)
                {
                    openNeed.LastEvaluatedDayIndex = dayIndex;
                }

                return;
            }

            float targetDays = Mathf.Max(LowDaysThreshold, TargetDaysThreshold);
            int unitsNeeded = Mathf.CeilToInt(
                Mathf.Max(0f, targetDays - effectiveDays) * Mathf.Max(1, dailyUseUnits));

            bool hasKnownSeller = isRecognizedByKnownSeller == null
                || isRecognizedByKnownSeller(itemId);
            float urgency = Mathf.Clamp01(1f - effectiveDays / Mathf.Max(0.01f, LowDaysThreshold));
            if (isColdSeason && string.Equals(itemId, HouseholdItemCatalog.FuelWoodId, StringComparison.OrdinalIgnoreCase))
            {
                urgency = Mathf.Max(urgency, 0.7f);
            }

            string reason = $"{itemId}: {effectiveDays:0.0} days of shared food cover at {dailyUseUnits}/day " +
                $"(threshold {LowDaysThreshold:0.0}d)" +
                (isColdSeason ? ", cold season" : string.Empty) +
                (hasKnownSeller ? string.Empty : ", no known seller currently recognizes this item");

            if (!hasKnownSeller)
            {
                diagnostics.Add($"H{household.id} has an open '{itemId}' shortage, but no known seller currently offers it");
            }

            if (openNeed == null)
            {
                openNeed = needs.CreateNeed(household.id, itemId, dayIndex);
            }

            openNeed.UnitsNeeded = Math.Max(unitsNeeded, 1);
            openNeed.Urgency01 = urgency;
            openNeed.ReasonSummary = reason;
            openNeed.LastEvaluatedDayIndex = dayIndex;
            openNeed.Status = PurchasingNeedStatus.Open;
            openNeed.SuggestedResponses = BuildSuggestedResponses(
                itemId, supplyAccess, upcomingObligationCents, expectedIncomeCents, urgency);

            if (isPurchasableNow == null || isPurchasableNow(itemId))
            {
                plannedDays += (float)openNeed.UnitsNeeded / Mathf.Max(1, dailyUseUnits);
            }
        }

        private void EvaluateItem(
            HouseholdState household,
            HouseholdInventory inventory,
            string itemId,
            int dailyUseUnits,
            bool isColdSeason,
            HouseholdSupplyAccess supplyAccess,
            int upcomingObligationCents,
            int expectedIncomeCents,
            int dayIndex,
            Func<string, bool> isRecognizedByKnownSeller = null)
        {
            if (string.IsNullOrWhiteSpace(itemId) || HouseholdItemCatalog.Get(itemId) == null)
            {
                return;
            }

            float daysCovered = dailyUseUnits > 0
                ? inventory.EstimateDaysOfSupply(itemId, dailyUseUnits)
                : float.PositiveInfinity;

            HouseholdPurchasingNeed openNeed = needs.GetOpenNeed(household.id, itemId);
            if (daysCovered >= FulfilledDaysThreshold)
            {
                if (openNeed != null)
                {
                    openNeed.Status = PurchasingNeedStatus.Fulfilled;
                    openNeed.LastEvaluatedDayIndex = dayIndex;
                }

                return;
            }

            // A household at exactly the configured reserve boundary is
            // already in the low-stock band. This lets the production driver
            // open a shopping need while the final day's food is still held,
            // instead of waiting until consumption has driven it below zero.
            if (daysCovered > LowDaysThreshold)
            {
                // Adequate for now: keep the open need fresh but do not grow it.
                if (openNeed != null)
                {
                    openNeed.LastEvaluatedDayIndex = dayIndex;
                }

                return;
            }

            float targetDays = Mathf.Max(LowDaysThreshold, TargetDaysThreshold);
            int unitsNeeded = Mathf.CeilToInt(
                Mathf.Max(0f, targetDays - daysCovered) * Mathf.Max(1, dailyUseUnits));

            // Seller recognition determines whether the shopping loop has a
            // legitimate route to a seller; it does not determine whether
            // the household is short. Keep the need open so shortages remain
            // observable while supply is unavailable. HouseholdShoppingLoop
            // checks known suppliers before assigning a shopper or reserving
            // travel time, and therefore fails this need without creating a
            // fictitious trip or purchase.
            bool hasKnownSeller = isRecognizedByKnownSeller == null
                || isRecognizedByKnownSeller(itemId);
            float urgency = Mathf.Clamp01(1f - daysCovered / Mathf.Max(0.01f, LowDaysThreshold));
            if (isColdSeason && string.Equals(itemId, HouseholdItemCatalog.FuelWoodId, StringComparison.OrdinalIgnoreCase))
            {
                urgency = Mathf.Max(urgency, 0.7f);
            }

            string reason = $"{itemId}: {daysCovered:0.0} days of supply at {dailyUseUnits}/day " +
                $"(threshold {LowDaysThreshold:0.0}d)" +
                (isColdSeason ? ", cold season" : string.Empty) +
                (hasKnownSeller ? string.Empty : ", no known seller currently recognizes this item");

            if (!hasKnownSeller)
            {
                diagnostics.Add($"H{household.id} has an open '{itemId}' shortage, but no known seller currently offers it");
            }

            if (openNeed == null)
            {
                openNeed = needs.CreateNeed(household.id, itemId, dayIndex);
            }

            openNeed.UnitsNeeded = Math.Max(unitsNeeded, 1);
            openNeed.Urgency01 = urgency;
            openNeed.ReasonSummary = reason;
            openNeed.LastEvaluatedDayIndex = dayIndex;
            openNeed.Status = PurchasingNeedStatus.Open;
            openNeed.SuggestedResponses = BuildSuggestedResponses(
                itemId, supplyAccess, upcomingObligationCents, expectedIncomeCents, urgency);
        }

        /// <summary>
        /// Responses depend on the household's ACTUAL options: purchase (only
        /// with supply access and cash), produce (only when capable),
        /// substitute, legitimate credit, request help, delay consumption, or
        /// go without. Nothing here moves money or goods — Phase C executes.
        /// </summary>
        private static List<string> BuildSuggestedResponses(
            string itemId,
            HouseholdSupplyAccess supplyAccess,
            int upcomingObligationCents,
            int expectedIncomeCents,
            float urgency)
        {
            var responses = new List<string>();
            bool cashStrained = upcomingObligationCents > expectedIncomeCents;

            if ((supplyAccess.HasGeneralStore || supplyAccess.HasOffMapAccess) && supplyAccess.HasCash && !cashStrained)
            {
                responses.Add("purchase");
            }
            else if ((supplyAccess.HasGeneralStore || supplyAccess.HasOffMapAccess) && cashStrained)
            {
                responses.Add("purchase only if obligations allow");
            }

            if (supplyAccess.CanProduce)
            {
                responses.Add("produce");
            }

            string substitute = GetSubstitute(itemId);
            if (!string.IsNullOrEmpty(substitute))
            {
                responses.Add($"substitute:{substitute}");
            }

            if (urgency >= 0.8f)
            {
                responses.Add("request help");
            }

            if (cashStrained)
            {
                responses.Add("legitimate credit (documented obligation)");
            }

            responses.Add("delay consumption");
            responses.Add("go without");
            return responses;
        }

        private static string GetSubstitute(string itemId)
        {
            // Documented transitional substitutes (not canon): the obvious
            // in-catalog swaps a household would actually make.
            if (string.Equals(itemId, HouseholdItemCatalog.BreadId, StringComparison.OrdinalIgnoreCase))
            {
                return HouseholdItemCatalog.PotatoesId;
            }

            if (string.Equals(itemId, HouseholdItemCatalog.PotatoesId, StringComparison.OrdinalIgnoreCase))
            {
                return HouseholdItemCatalog.FlourId;
            }

            if (string.Equals(itemId, HouseholdItemCatalog.PreservedMeatId, StringComparison.OrdinalIgnoreCase))
            {
                return HouseholdItemCatalog.PreservedFoodId;
            }

            return null;
        }
    }

    /// <summary>
    /// Phase B: registry of purchasing needs. One open need per
    /// household+item (no duplicates); re-evaluation updates the open need in
    /// place. Save-persisted.
    /// </summary>
    public sealed class HouseholdNeedRegistry
    {
        private readonly Dictionary<string, HouseholdPurchasingNeed> openByKey = new Dictionary<string, HouseholdPurchasingNeed>(StringComparer.Ordinal);
        private readonly List<HouseholdPurchasingNeed> all = new List<HouseholdPurchasingNeed>();
        private readonly List<string> diagnostics = new List<string>();
        private int nextSequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        private static string Key(int householdId, string itemId) => $"{householdId}:{itemId}";

        public HouseholdPurchasingNeed GetOpenNeed(int householdId, string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            return openByKey.TryGetValue(Key(householdId, itemId), out HouseholdPurchasingNeed need)
                && need.Status == PurchasingNeedStatus.Open
                ? need
                : null;
        }

        public HouseholdPurchasingNeed CreateNeed(int householdId, string itemId, int dayIndex)
        {
            HouseholdPurchasingNeed existing = GetOpenNeed(householdId, itemId);
            if (existing != null)
            {
                return existing;
            }

            var need = new HouseholdPurchasingNeed
            {
                NeedSequence = nextSequence++,
                HouseholdId = householdId,
                ItemId = itemId,
                CreatedDayIndex = Mathf.Max(0, dayIndex),
                LastEvaluatedDayIndex = Mathf.Max(0, dayIndex),
                Status = PurchasingNeedStatus.Open,
            };
            openByKey[Key(householdId, itemId)] = need;
            all.Add(need);
            return need;
        }

        public List<HouseholdPurchasingNeed> GetOpenNeeds(int householdId)
        {
            var result = new List<HouseholdPurchasingNeed>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].HouseholdId == householdId && all[i].Status == PurchasingNeedStatus.Open)
                {
                    result.Add(all[i]);
                }
            }

            return result;
        }

        public void ExportState(List<HouseholdPurchasingNeed> outNeeds)
        {
            if (outNeeds == null)
            {
                return;
            }

            foreach (HouseholdPurchasingNeed need in all)
            {
                if (need != null) outNeeds.Add(CopyNeed(need));
            }
        }

        public void ImportState(IEnumerable<HouseholdPurchasingNeed> inNeeds)
        {
            if (inNeeds == null)
            {
                return;
            }

            // Save/Continue imports a snapshot, not an append operation.
            // Clear prior runtime state and copy each record so subsequent
            // shortage evaluation cannot mutate the caller's saved DTO.
            openByKey.Clear();
            all.Clear();
            nextSequence = 0;

            foreach (HouseholdPurchasingNeed need in inNeeds)
            {
                if (need == null || need.HouseholdId < 0 || string.IsNullOrWhiteSpace(need.ItemId))
                {
                    continue;
                }

                HouseholdPurchasingNeed restored = CopyNeed(need);
                nextSequence = Math.Max(nextSequence, restored.NeedSequence + 1);
                if (restored.Status == PurchasingNeedStatus.Open)
                {
                    string key = Key(restored.HouseholdId, restored.ItemId);
                    if (openByKey.ContainsKey(key))
                    {
                        diagnostics.Add($"ImportState: duplicate open need {key} skipped.");
                        continue;
                    }

                    openByKey[key] = restored;
                }

                all.Add(restored);
            }
        }

        private static HouseholdPurchasingNeed CopyNeed(HouseholdPurchasingNeed need)
        {
            return new HouseholdPurchasingNeed
            {
                NeedSequence = need.NeedSequence,
                HouseholdId = need.HouseholdId,
                ItemId = need.ItemId,
                UnitsNeeded = need.UnitsNeeded,
                IsNecessity = need.IsNecessity,
                Urgency01 = need.Urgency01,
                ReasonSummary = need.ReasonSummary,
                CreatedDayIndex = need.CreatedDayIndex,
                LastEvaluatedDayIndex = need.LastEvaluatedDayIndex,
                Status = need.Status,
                SuggestedResponses = need.SuggestedResponses != null
                    ? new List<string>(need.SuggestedResponses)
                    : new List<string>(),
            };
        }
    }
}
