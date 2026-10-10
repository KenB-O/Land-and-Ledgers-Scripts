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
        /// </summary>
        public void Evaluate(
            HouseholdState household,
            HouseholdInventory inventory,
            Dictionary<string, int> expectedDailyUseByItem,
            bool isColdSeason,
            HouseholdSupplyAccess supplyAccess,
            int upcomingObligationCents,
            int expectedIncomeCents,
            int dayIndex)
        {
            if (household == null || inventory == null)
            {
                diagnostics.Add("Evaluate: household or inventory missing — no needs evaluated.");
                return;
            }

            int day = Mathf.Max(0, dayIndex);
            if (expectedDailyUseByItem != null)
            {
                foreach (KeyValuePair<string, int> use in expectedDailyUseByItem)
                {
                    EvaluateItem(household, inventory, use.Key, Mathf.Max(0, use.Value),
                        isColdSeason, supplyAccess, upcomingObligationCents, expectedIncomeCents, day);
                }
            }

            // Fuel gets a cold-season urgency bump even when it has no
            // explicit daily-use entry (heating is evaluated separately).
            if (isColdSeason)
            {
                EvaluateItem(household, inventory, HouseholdItemCatalog.FuelWoodId, 1,
                    true, supplyAccess, upcomingObligationCents, expectedIncomeCents, day);
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
            int dayIndex)
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

            if (daysCovered >= LowDaysThreshold)
            {
                // Adequate for now: keep the open need fresh but do not grow it.
                if (openNeed != null)
                {
                    openNeed.LastEvaluatedDayIndex = dayIndex;
                }

                return;
            }

            float shortfallDays = Mathf.Max(0f, LowDaysThreshold - daysCovered);
            int unitsNeeded = Mathf.CeilToInt(shortfallDays * Mathf.Max(1, dailyUseUnits));
            float urgency = Mathf.Clamp01(1f - daysCovered / Mathf.Max(0.01f, LowDaysThreshold));
            if (isColdSeason && string.Equals(itemId, HouseholdItemCatalog.FuelWoodId, StringComparison.OrdinalIgnoreCase))
            {
                urgency = Mathf.Max(urgency, 0.7f);
            }

            string reason = $"{itemId}: {daysCovered:0.0} days of supply at {dailyUseUnits}/day " +
                $"(threshold {LowDaysThreshold:0.0}d)" +
                (isColdSeason ? ", cold season" : string.Empty);

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

            outNeeds.AddRange(all);
        }

        public void ImportState(IEnumerable<HouseholdPurchasingNeed> inNeeds)
        {
            if (inNeeds == null)
            {
                return;
            }

            foreach (HouseholdPurchasingNeed need in inNeeds)
            {
                if (need == null || need.HouseholdId < 0 || string.IsNullOrWhiteSpace(need.ItemId))
                {
                    continue;
                }

                nextSequence = Math.Max(nextSequence, need.NeedSequence + 1);
                if (need.Status == PurchasingNeedStatus.Open)
                {
                    string key = Key(need.HouseholdId, need.ItemId);
                    if (openByKey.ContainsKey(key))
                    {
                        diagnostics.Add($"ImportState: duplicate open need {key} skipped.");
                        continue;
                    }

                    openByKey[key] = need;
                }

                all.Add(need);
            }
        }
    }
}
