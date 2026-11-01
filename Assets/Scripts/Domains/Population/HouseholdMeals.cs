using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>Phase B: the daily meal slots. Append-only.</summary>
    public enum MealSlot
    {
        Morning = 0,
        Midday = 1,
        Evening = 2,
    }

    /// <summary>
    /// Phase B: where a served meal came from. Every meal records a traceable
    /// record no matter the source; a household with no food records NO
    /// successful meal (hardship instead).
    /// </summary>
    public enum MealSourceKind
    {
        Unspecified = 0,
        HouseholdInventory = 1, // real lots consumed from the household inventory
        EmployerProvided = 2, // meals provided by an employer at work
        BoardingArrangement = 3, // board-included meals (boarding house / kin)
        PurchasedMeal = 4, // restaurant, hotel dining room, bought prepared food
        HouseholdProduction = 5, // prepared in-house from raw ingredients + labor
    }

    /// <summary>Phase B: the adequacy outcome of a served meal.</summary>
    public enum MealAdequacy
    {
        Substantial = 0, // a full meal
        Light = 1, // a light/flexible meal (midday)
    }

    /// <summary>Phase B: one lot consumption inside a meal.</summary>
    [Serializable]
    public sealed class MealItemConsumption
    {
        public string ItemId = string.Empty;
        public int LotId;
        public int QuantityUnits;
    }

    /// <summary>Phase B: resulting availability snapshot for one item after a meal.</summary>
    [Serializable]
    public sealed class MealInventorySnapshot
    {
        public string ItemId = string.Empty;
        public int AvailableUnitsAfter;
    }

    /// <summary>
    /// Phase B: one traceable meal record — person(s) fed, item/lot consumed,
    /// quantity, source, time, adequacy outcome, and resulting inventory.
    /// </summary>
    [Serializable]
    public sealed class HouseholdMealRecord
    {
        public int PersonId;
        public int HouseholdId;
        public int DayIndex;
        public MealSlot Slot;
        public MealSourceKind SourceKind;
        public List<MealItemConsumption> ItemsConsumed = new List<MealItemConsumption>();
        public string PreparationNote = string.Empty;
        public MealAdequacy Adequacy;
        public List<MealInventorySnapshot> ResultingInventory = new List<MealInventorySnapshot>();
    }

    /// <summary>
    /// Phase B: a meal that could not be served. Shortage causes hardship and
    /// responses — never invisible replenishment, never a faked meal record.
    /// </summary>
    [Serializable]
    public sealed class MissedMealRecord
    {
        public int PersonId;
        public int HouseholdId;
        public int DayIndex;
        public MealSlot Slot;
        public string Reason = string.Empty;
    }

    /// <summary>
    /// Phase B: one explicit raw-to-prepared conversion (e.g. flour to bread).
    /// Preparation consumes REAL ingredients and names its equipment and
    /// labor — raw is never silently converted to prepared for free.
    /// </summary>
    [Serializable]
    public sealed class MealPreparationRecord
    {
        public int HouseholdId;
        public int DayIndex;
        public string RawItemId = string.Empty;
        public int RawUnitsConsumed;
        public string PreparedItemId = string.Empty;
        public int PreparedUnitsProduced;
        public string EquipmentLabel = string.Empty;
        public int LaborMinutes;
        public string CookLabel = string.Empty;
    }

    /// <summary>
    /// Phase B: the meal scheduling policy. Two substantial meals daily plus
    /// an optional/flexible midday meal — CONFIGURABLE per scenario, never a
    /// universal constant. The midday meal is circumstance-sensitive: it is
    /// served only when staple days-of-supply meet the threshold.
    /// </summary>
    public sealed class MealSchedulingPolicy
    {
        /// <summary>Slots served as substantial meals (default: morning + evening).</summary>
        public List<MealSlot> SubstantialMealSlots { get; set; } = new List<MealSlot> { MealSlot.Morning, MealSlot.Evening };

        /// <summary>Whether the flexible midday meal is enabled at all.</summary>
        public bool MiddayMealEnabled { get; set; } = true;

        /// <summary>
        /// Circumstance sensitivity: the midday meal is served only when the
        /// household's staple days-of-supply is at or above this threshold.
        /// </summary>
        public float MiddayMealSupplyThresholdDays { get; set; } = 3f;

        /// <summary>Item priority for meal serving (first available wins).</summary>
        public List<string> MealItemPriority { get; set; } = new List<string>
        {
            HouseholdItemCatalog.BreadId,
            HouseholdItemCatalog.PreservedMeatId,
            HouseholdItemCatalog.PreservedFoodId,
            HouseholdItemCatalog.PotatoesId,
            HouseholdItemCatalog.FlourId,
            HouseholdItemCatalog.UnspecifiedStapleFoodId,
        };

        /// <summary>Per-item serving-size overrides (item id → units per serving).</summary>
        public Dictionary<string, int> ServingSizeOverride { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Expected daily meals per person under this policy (nutrition denominator).</summary>
        public int ExpectedMealsPerDay => (SubstantialMealSlots != null ? SubstantialMealSlots.Count : 0) + (MiddayMealEnabled ? 1 : 0);

        public static MealSchedulingPolicy Default => new MealSchedulingPolicy();

        public int GetServingUnits(string itemId)
        {
            if (!string.IsNullOrWhiteSpace(itemId)
                && ServingSizeOverride != null
                && ServingSizeOverride.TryGetValue(itemId, out int overridden)
                && overridden > 0)
            {
                return overridden;
            }

            HouseholdItemDefinition definition = HouseholdItemCatalog.Get(itemId);
            return definition != null ? Mathf.Max(1, definition.MealServingUnits) : 1;
        }

        /// <summary>Slots to serve today for a household with the given staple days-of-supply.</summary>
        public List<MealSlot> GetSlotsForDay(float stapleDaysOfSupply)
        {
            var slots = new List<MealSlot>();
            if (SubstantialMealSlots != null)
            {
                slots.AddRange(SubstantialMealSlots);
            }

            if (MiddayMealEnabled && stapleDaysOfSupply >= MiddayMealSupplyThresholdDays && !slots.Contains(MealSlot.Midday))
            {
                slots.Add(MealSlot.Midday);
            }

            return slots;
        }
    }

    /// <summary>Phase B: the result of serving one household for one day.</summary>
    public sealed class MealDayResult
    {
        public Dictionary<int, int> MealsServedPerPerson = new Dictionary<int, int>();
        public int MealsServed;
        public int MealsMissed;
    }

    /// <summary>
    /// Phase B (Real People): real food and meals. Persons eat regularly from
    /// real inventory lots; every meal records a traceable record (person fed,
    /// item/lot consumed, quantity, source, time, adequacy, resulting
    /// inventory). If the household has no food, NO successful meal is
    /// recorded — a missed-meal hardship record is written instead.
    /// </summary>
    public sealed class HouseholdMealService
    {
        private readonly HouseholdMealLogRegistry log;
        private readonly List<string> diagnostics = new List<string>();

        public HouseholdMealService(HouseholdMealLogRegistry log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Explicit raw-to-prepared conversion. Consumes real raw lots and
        /// books the prepared lot with preparation provenance (equipment +
        /// labor named). Never free, never silent.
        /// </summary>
        public string PrepareFromRaw(
            HouseholdInventory inventory,
            string rawItemId,
            string preparedItemId,
            int rawUnitsPerPreparedUnit,
            int preparedUnits,
            string equipmentLabel,
            int laborMinutes,
            int dayIndex,
            string cookLabel)
        {
            if (inventory == null)
            {
                return Reject("PrepareFromRaw: inventory is required.");
            }

            if (HouseholdItemCatalog.Get(rawItemId) == null)
            {
                return Reject($"PrepareFromRaw: unknown raw item '{rawItemId}'.");
            }

            if (HouseholdItemCatalog.Get(preparedItemId) == null)
            {
                return Reject($"PrepareFromRaw: unknown prepared item '{preparedItemId}'.");
            }

            if (rawUnitsPerPreparedUnit <= 0 || preparedUnits <= 0)
            {
                return Reject("PrepareFromRaw: raw units per prepared unit and prepared units must be positive.");
            }

            if (string.IsNullOrWhiteSpace(equipmentLabel))
            {
                return Reject("PrepareFromRaw: equipment is required (never convert for free).");
            }

            if (laborMinutes < 0)
            {
                return Reject("PrepareFromRaw: labor minutes cannot be negative.");
            }

            int rawNeeded = rawUnitsPerPreparedUnit * preparedUnits;
            var consumptionRecords = new List<HouseholdConsumptionRecord>();
            string consumeRejection = inventory.Consume(
                rawItemId, rawNeeded, dayIndex,
                $"meal preparation: {rawItemId} to {preparedItemId}",
                string.IsNullOrWhiteSpace(cookLabel) ? "household cook" : cookLabel,
                consumptionRecords);
            if (consumeRejection != null)
            {
                return Reject($"PrepareFromRaw: {consumeRejection}");
            }

            string addRejection = inventory.AddLot(
                preparedItemId, preparedUnits, dayIndex,
                $"meal preparation from {rawItemId} ({equipmentLabel}, {laborMinutes} min labor)");
            if (addRejection != null)
            {
                return Reject($"PrepareFromRaw: {addRejection}");
            }

            log.RecordPreparation(new MealPreparationRecord
            {
                HouseholdId = inventory.HouseholdId,
                DayIndex = Mathf.Max(0, dayIndex),
                RawItemId = rawItemId,
                RawUnitsConsumed = rawNeeded,
                PreparedItemId = preparedItemId,
                PreparedUnitsProduced = preparedUnits,
                EquipmentLabel = equipmentLabel,
                LaborMinutes = laborMinutes,
                CookLabel = cookLabel ?? string.Empty,
            });
            return null;
        }

        /// <summary>
        /// Serves one day of meals for a household's members.
        /// outOfHomeMeals maps person id → (meals already eaten out, source).
        /// Persons are served in order; each slot first honors out-of-home
        /// meals, then consumes real inventory lots. Empty larders produce
        /// missed-meal hardship records, never faked meals.
        /// </summary>
        public MealDayResult ServeHouseholdDay(
            HouseholdState household,
            IReadOnlyList<int> memberPersonIds,
            HouseholdInventory inventory,
            MealSchedulingPolicy policy,
            int dayIndex,
            Dictionary<int, OutOfHomeMeals> outOfHomeMeals)
        {
            var result = new MealDayResult();
            if (household == null || memberPersonIds == null || inventory == null)
            {
                diagnostics.Add("ServeHouseholdDay: household, members, or inventory missing — no meals served.");
                return result;
            }

            policy ??= MealSchedulingPolicy.Default;
            float stapleDays = EstimateStapleDaysOfSupply(inventory, memberPersonIds.Count, policy);
            List<MealSlot> slots = policy.GetSlotsForDay(stapleDays);

            for (int i = 0; i < memberPersonIds.Count; i++)
            {
                int personId = memberPersonIds[i];
                int outOfHome = 0;
                MealSourceKind outOfHomeSource = MealSourceKind.Unspecified;
                if (outOfHomeMeals != null && outOfHomeMeals.TryGetValue(personId, out OutOfHomeMeals ooh))
                {
                    outOfHome = Math.Max(0, ooh.MealsEaten);
                    outOfHomeSource = ooh.SourceKind;
                }

                int served = 0;
                int slotIndex = 0;
                foreach (MealSlot slot in slots)
                {
                    if (slotIndex < outOfHome)
                    {
                        // Already genuinely fed out of home: record it, consume nothing.
                        log.RecordMeal(new HouseholdMealRecord
                        {
                            PersonId = personId,
                            HouseholdId = household.id,
                            DayIndex = Mathf.Max(0, dayIndex),
                            Slot = slot,
                            SourceKind = outOfHomeSource != MealSourceKind.Unspecified
                                ? outOfHomeSource
                                : MealSourceKind.PurchasedMeal,
                            Adequacy = slot == MealSlot.Midday ? MealAdequacy.Light : MealAdequacy.Substantial,
                        });
                        served++;
                        slotIndex++;
                        continue;
                    }

                    if (TryServeFromInventory(household, personId, inventory, policy, slot, dayIndex))
                    {
                        served++;
                    }
                    else
                    {
                        log.RecordMissedMeal(new MissedMealRecord
                        {
                            PersonId = personId,
                            HouseholdId = household.id,
                            DayIndex = Mathf.Max(0, dayIndex),
                            Slot = slot,
                            Reason = "no food available in household inventory",
                        });
                        result.MealsMissed++;
                    }

                    slotIndex++;
                }

                result.MealsServedPerPerson[personId] = served;
                result.MealsServed += served;
            }

            return result;
        }

        private bool TryServeFromInventory(
            HouseholdState household,
            int personId,
            HouseholdInventory inventory,
            MealSchedulingPolicy policy,
            MealSlot slot,
            int dayIndex)
        {
            List<string> priority = policy.MealItemPriority;
            for (int i = 0; i < priority.Count; i++)
            {
                string itemId = priority[i];
                int servingUnits = policy.GetServingUnits(itemId);
                if (inventory.GetAvailableUnits(itemId) < servingUnits)
                {
                    continue;
                }

                var consumptionRecords = new List<HouseholdConsumptionRecord>();
                string rejection = inventory.Consume(
                    itemId, servingUnits, dayIndex,
                    $"meal ({slot})", $"P{personId}", consumptionRecords);
                if (rejection != null)
                {
                    diagnostics.Add($"ServeHouseholdDay: {rejection}");
                    continue;
                }

                var record = new HouseholdMealRecord
                {
                    PersonId = personId,
                    HouseholdId = household.id,
                    DayIndex = Mathf.Max(0, dayIndex),
                    Slot = slot,
                    SourceKind = MealSourceKind.HouseholdInventory,
                    Adequacy = slot == MealSlot.Midday ? MealAdequacy.Light : MealAdequacy.Substantial,
                };
                foreach (HouseholdConsumptionRecord consumption in consumptionRecords)
                {
                    record.ItemsConsumed.Add(new MealItemConsumption
                    {
                        ItemId = consumption.ItemId,
                        LotId = consumption.LotId,
                        QuantityUnits = consumption.QuantityUnits,
                    });
                }

                SnapshotResultingInventory(inventory, record);
                log.RecordMeal(record);
                HouseholdInventoryReserveBridge.MirrorLotConsumptionToReserve(household, itemId, servingUnits);
                return true;
            }

            return false;
        }

        private static void SnapshotResultingInventory(HouseholdInventory inventory, HouseholdMealRecord record)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < inventory.Lots.Count; i++)
            {
                HouseholdItemLot lot = inventory.Lots[i];
                if (lot == null || !seen.Add(lot.ItemId))
                {
                    continue;
                }

                record.ResultingInventory.Add(new MealInventorySnapshot
                {
                    ItemId = lot.ItemId,
                    AvailableUnitsAfter = inventory.GetAvailableUnits(lot.ItemId),
                });
            }
        }

        private static float EstimateStapleDaysOfSupply(HouseholdInventory inventory, int memberCount, MealSchedulingPolicy policy)
        {
            int stapleAvailable = 0;
            for (int i = 0; i < policy.MealItemPriority.Count; i++)
            {
                HouseholdItemDefinition definition = HouseholdItemCatalog.Get(policy.MealItemPriority[i]);
                if (definition != null && definition.IsFood)
                {
                    stapleAvailable += inventory.GetAvailableUnits(definition.ItemId);
                }
            }

            int members = Math.Max(1, memberCount);
            int dailyUse = members * policy.ExpectedMealsPerDay * 2; // ~2 staple units per meal
            if (dailyUse <= 0)
            {
                return float.PositiveInfinity;
            }

            return stapleAvailable / (float)dailyUse;
        }

        private string Reject(string diagnostic)
        {
            diagnostics.Add(diagnostic);
            return diagnostic;
        }
    }

    /// <summary>Phase B: meals a person genuinely ate outside the household.</summary>
    public struct OutOfHomeMeals
    {
        public int MealsEaten;
        public MealSourceKind SourceKind;
    }

    /// <summary>
    /// Phase B: per-household meal log — every served meal, every missed meal,
    /// every preparation. Save-persisted; records are append-only.
    /// </summary>
    public sealed class HouseholdMealLogRegistry
    {
        private readonly Dictionary<int, List<HouseholdMealRecord>> mealsByHousehold = new Dictionary<int, List<HouseholdMealRecord>>();
        private readonly Dictionary<int, List<MissedMealRecord>> missedByHousehold = new Dictionary<int, List<MissedMealRecord>>();
        private readonly Dictionary<int, List<MealPreparationRecord>> preparationsByHousehold = new Dictionary<int, List<MealPreparationRecord>>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public void RecordMeal(HouseholdMealRecord record)
        {
            if (record == null || record.HouseholdId < 0)
            {
                return;
            }

            if (!mealsByHousehold.TryGetValue(record.HouseholdId, out List<HouseholdMealRecord> list))
            {
                list = new List<HouseholdMealRecord>();
                mealsByHousehold[record.HouseholdId] = list;
            }

            list.Add(record);
        }

        public void RecordMissedMeal(MissedMealRecord record)
        {
            if (record == null || record.HouseholdId < 0)
            {
                return;
            }

            if (!missedByHousehold.TryGetValue(record.HouseholdId, out List<MissedMealRecord> list))
            {
                list = new List<MissedMealRecord>();
                missedByHousehold[record.HouseholdId] = list;
            }

            list.Add(record);
        }

        public void RecordPreparation(MealPreparationRecord record)
        {
            if (record == null || record.HouseholdId < 0)
            {
                return;
            }

            if (!preparationsByHousehold.TryGetValue(record.HouseholdId, out List<MealPreparationRecord> list))
            {
                list = new List<MealPreparationRecord>();
                preparationsByHousehold[record.HouseholdId] = list;
            }

            list.Add(record);
        }

        public IReadOnlyList<HouseholdMealRecord> GetMeals(int householdId)
        {
            return mealsByHousehold.TryGetValue(householdId, out List<HouseholdMealRecord> list)
                ? list
                : Array.Empty<HouseholdMealRecord>();
        }

        public IReadOnlyList<MissedMealRecord> GetMissedMeals(int householdId)
        {
            return missedByHousehold.TryGetValue(householdId, out List<MissedMealRecord> list)
                ? list
                : Array.Empty<MissedMealRecord>();
        }

        public IReadOnlyList<MealPreparationRecord> GetPreparations(int householdId)
        {
            return preparationsByHousehold.TryGetValue(householdId, out List<MealPreparationRecord> list)
                ? list
                : Array.Empty<MealPreparationRecord>();
        }

        public void ExportState(
            List<HouseholdMealRecord> outMeals,
            List<MissedMealRecord> outMissed,
            List<MealPreparationRecord> outPreparations)
        {
            if (outMeals != null)
            {
                foreach (List<HouseholdMealRecord> list in mealsByHousehold.Values)
                {
                    outMeals.AddRange(list);
                }
            }

            if (outMissed != null)
            {
                foreach (List<MissedMealRecord> list in missedByHousehold.Values)
                {
                    outMissed.AddRange(list);
                }
            }

            if (outPreparations != null)
            {
                foreach (List<MealPreparationRecord> list in preparationsByHousehold.Values)
                {
                    outPreparations.AddRange(list);
                }
            }
        }

        public void ImportState(
            IEnumerable<HouseholdMealRecord> meals,
            IEnumerable<MissedMealRecord> missed,
            IEnumerable<MealPreparationRecord> preparations)
        {
            if (meals != null)
            {
                foreach (HouseholdMealRecord record in meals)
                {
                    RecordMeal(record);
                }
            }

            if (missed != null)
            {
                foreach (MissedMealRecord record in missed)
                {
                    RecordMissedMeal(record);
                }
            }

            if (preparations != null)
            {
                foreach (MealPreparationRecord record in preparations)
                {
                    RecordPreparation(record);
                }
            }
        }
    }
}
