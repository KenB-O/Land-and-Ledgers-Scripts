using System;
using System.Collections.Generic;
using LandLedgers.Economy.Logistics;
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
    /// Lane 9a (A5): why a meal was missed, classified at close time
    /// from the evidence available then — so a missed meal can be told
    /// apart from legitimate poverty vs broken mechanics from the
    /// record alone. Only the kinds the evidence supports are ever
    /// written: the meal service classifies from inventory evidence
    /// (NoFoodInLarder / SchedulingWindowMissed / Unknown); cash,
    /// seller, stock-at-seller, shopper and counter reasons need the
    /// shopping loop's or shortage monitor's evidence and are stamped
    /// by the caller that holds it. Unknown is a valid outcome — never
    /// invent a reason. Unknown is 0 so records from old saves (which
    /// carry no kind) load as Unknown, never as an invented reason.
    /// </summary>
    public enum MissedMealReasonKind
    {
        Unknown = 0,
        NoFoodInLarder = 1,
        CannotAfford = 2,
        NoKnownSeller = 3,
        NoStockAtSeller = 4,
        NoShopperAvailable = 5,
        NoCounterService = 6,
        SchedulingWindowMissed = 7,
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
        /// <summary>Lane 9a (A5): the classified reason. Unknown is the default — old saves load Unknown, never an invented reason.</summary>
        public MissedMealReasonKind ReasonKind = MissedMealReasonKind.Unknown;
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
    /// Phase B: the meal scheduling policy. The ordinary household schedule
    /// provides breakfast, lunch and supper. The midday slot remains
    /// configurable for scenarios, but food scarcity does not erase the need
    /// from nutrition or missed-meal accounting.
    /// </summary>
    public sealed class MealSchedulingPolicy
    {
        /// <summary>Configured substantial meal slots (default: morning + evening).</summary>
        public List<MealSlot> SubstantialMealSlots { get; set; } = new List<MealSlot> { MealSlot.Morning, MealSlot.Evening };

        /// <summary>Whether the flexible midday meal is enabled at all.</summary>
        public bool MiddayMealEnabled { get; set; } = true;

        /// <summary>
        /// Legacy authoring field retained for save/source compatibility.
        /// Scarcity no longer removes the midday meal slot; it creates a
        /// traceable missed meal when the household lacks edible food.
        /// </summary>
        public float MiddayMealSupplyThresholdDays { get; set; } = 0f;

        /// <summary>
        /// Item priority for meal serving (first available wins). Lane 9a
        /// (A8): generic unspecified staple is NOT a meal item — a loaf
        /// is a loaf, and unspecified stock never satisfies a meal, a
        /// purchase, a shipment or a receipt as if it were a named
        /// product. Legacy unidentified stock cannot be served until
        /// identified (see HouseholdInventoryReserveBridge).
        /// </summary>
        public List<string> MealItemPriority { get; set; } = new List<string>
        {
            HouseholdItemCatalog.BreadId,
            "beef",
            "bacon",
            "cheese",
            HouseholdItemCatalog.PreservedMeatId,
            HouseholdItemCatalog.UnspecifiedMeatId,
            HouseholdItemCatalog.PreservedFoodId,
            HouseholdItemCatalog.PotatoesId,
        };

        /// <summary>Per-item serving-size overrides (item id → units per serving).</summary>
        public Dictionary<string, int> ServingSizeOverride { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Expected daily meals per person under this policy (nutrition denominator).</summary>
        public int ExpectedMealsPerDay => GetSlotsForDay(0f).Count;

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
                foreach (MealSlot slot in SubstantialMealSlots)
                {
                    if (!slots.Contains(slot)) slots.Add(slot);
                }
            }

            if (MiddayMealEnabled && !slots.Contains(MealSlot.Midday))
            {
                slots.Add(MealSlot.Midday);
            }

            slots.Sort();
            return slots;
        }
    }

    /// <summary>Phase B: the result of serving one household for one day.</summary>
    public sealed class MealDayResult
    {
        public Dictionary<int, int> MealsServedPerPerson = new Dictionary<int, int>();
        /// <summary>
        /// Number of meal slots actually scheduled per member for this day.
        /// Food scarcity does not remove a required slot; an unserved slot is
        /// recorded as a missed meal.
        /// </summary>
        public int MealsRequiredPerPerson;
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
        private readonly List<LotIdentity> lastMealLotIdentities = new List<LotIdentity>();

        public HouseholdMealService(HouseholdMealLogRegistry log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Priority V1: the F4 lot identity resolver this service uses
        /// to answer, at consumption time, which lot a meal consumed —
        /// by canonical identity, not by bare lot id. Null (the default)
        /// means today's behaviour: meals record the lot id only and
        /// LastMealLotIdentities stays empty. Set it to a resolver over
        /// the household inventory registry to resolve consumed lots.
        /// </summary>
        public LotIdentityResolver LotIdentityResolver { get; set; }

        /// <summary>
        /// Priority V1: the identities of the lots the most recent
        /// ServeHouseholdDay call consumed, resolved through
        /// <see cref="LotIdentityResolver"/> at consumption time.
        /// Runtime-only (never saved): a fresh list per served day,
        /// empty when no resolver is set. A lot that could not be
        /// resolved is named in <see cref="Diagnostics"/>, never
        /// invented.
        /// </summary>
        public IReadOnlyList<LotIdentity> LastMealLotIdentities => lastMealLotIdentities;

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

            // Priority V1: a fresh consumption-identity list per served day.
            lastMealLotIdentities.Clear();

            policy ??= MealSchedulingPolicy.Default;
            float stapleDays = EstimateStapleDaysOfSupply(inventory, memberPersonIds.Count, policy);
            List<MealSlot> slots = policy.GetSlotsForDay(stapleDays);
            result.MealsRequiredPerPerson = slots.Count;

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
                            Reason = DescribeMealInventoryFailure(inventory, policy),
                            ReasonKind = ClassifyMissedMealReason(inventory, policy),
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

        /// <summary>
        /// Resolve one real meal at its scheduled time. The meal log is the
        /// durable idempotency key: after Save/Continue, an already served or
        /// missed person/day/slot is not charged or consumed again. Flour is
        /// deliberately not directly edible; raw ingredients must first be
        /// converted by the domestic preparation authority.
        /// </summary>
        public bool ServeHouseholdMealSlot(
            HouseholdState household,
            int personId,
            HouseholdInventory inventory,
            MealSchedulingPolicy policy,
            int dayIndex,
            MealSlot slot,
            bool alreadyAteAwayFromHome = false,
            MealSourceKind awayMealSource = MealSourceKind.PurchasedMeal,
            string missedReasonOverride = null,
            MissedMealReasonKind missedReasonKind = MissedMealReasonKind.Unknown)
        {
            if (household == null || personId < 0 || inventory == null)
            {
                diagnostics.Add("ServeHouseholdMealSlot: household, person, or received household inventory is missing; no meal was created.");
                return false;
            }

            policy ??= MealSchedulingPolicy.Default;
            if (!policy.GetSlotsForDay(EstimateStapleDaysOfSupply(inventory, 1, policy)).Contains(slot))
                return false;
            if (log.HasResolution(household.id, personId, dayIndex, slot))
                return false;

            lastMealLotIdentities.Clear();
            if (alreadyAteAwayFromHome)
            {
                log.RecordMeal(new HouseholdMealRecord
                {
                    PersonId = personId,
                    HouseholdId = household.id,
                    DayIndex = Math.Max(0, dayIndex),
                    Slot = slot,
                    SourceKind = awayMealSource == MealSourceKind.Unspecified
                        ? MealSourceKind.PurchasedMeal : awayMealSource,
                    Adequacy = slot == MealSlot.Midday ? MealAdequacy.Light : MealAdequacy.Substantial,
                });
                return true;
            }

            if (string.IsNullOrWhiteSpace(missedReasonOverride)
                && TryServeFromInventory(household, personId, inventory, policy, slot, dayIndex))
                return true;

            // Lane 9a (A5): the late-close path checks inventory before
            // writing the generic reason, and never serves a meal
            // retroactively. Food in the larder means the window passed,
            // not the larder — SchedulingWindowMissed. No servable food
            // means the missed meal is NoFoodInLarder. A caller that
            // holds stronger evidence (cash, seller, shopper, counter)
            // stamps its kind explicitly; it wins over this
            // inventory-only classification.
            MissedMealReasonKind reasonKind;
            string reason;
            if (missedReasonKind != MissedMealReasonKind.Unknown)
            {
                reasonKind = missedReasonKind;
                reason = string.IsNullOrWhiteSpace(missedReasonOverride)
                    ? DescribeMealInventoryFailure(inventory, policy)
                    : missedReasonOverride;
            }
            else if (!string.IsNullOrWhiteSpace(missedReasonOverride))
            {
                if (HasServableMeal(inventory, policy))
                {
                    reasonKind = MissedMealReasonKind.SchedulingWindowMissed;
                    reason = "The scheduled meal opportunity passed with servable food in the larder; no later food was consumed retroactively.";
                }
                else
                {
                    reasonKind = MissedMealReasonKind.NoFoodInLarder;
                    reason = missedReasonOverride;
                }
            }
            else
            {
                reasonKind = ClassifyMissedMealReason(inventory, policy);
                reason = DescribeMealInventoryFailure(inventory, policy);
            }

            log.RecordMissedMeal(new MissedMealRecord
            {
                PersonId = personId,
                HouseholdId = household.id,
                DayIndex = Math.Max(0, dayIndex),
                Slot = slot,
                Reason = reason,
                ReasonKind = reasonKind,
            });
            return true;
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
                // Lane 9a (A8): generic unspecified staple is never a
                // servable meal item, even when a custom policy lists it
                // — a loaf is a loaf.
                if (IsUnspecifiedStaple(itemId)) continue;
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

                // Priority V1: at consumption time, resolve each consumed
                // lot through the F4 resolver (when one is set) so the
                // meal names its lots by canonical identity, not by bare
                // lot id. A lot that cannot be resolved is named in
                // diagnostics, never invented; an unset resolver means
                // today's behaviour (lot ids only, empty identity list).
                if (LotIdentityResolver != null)
                {
                    foreach (HouseholdConsumptionRecord consumption in consumptionRecords)
                    {
                        LotIdentity identity = LotIdentityResolver.Resolve(new LotKey(
                            CustodyTransferLog.HouseholdInventoryAuthority, consumption.LotId.ToString()));
                        if (identity != null)
                        {
                            lastMealLotIdentities.Add(identity);
                        }
                        else
                        {
                            diagnostics.Add($"ServeHouseholdDay: meal consumed lot {consumption.LotId} " +
                                "but the lot identity resolver could not resolve it — see the resolver diagnostics.");
                        }
                    }
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

        /// <summary>
        /// Lane 9a (A8): the generic unspecified staple item. Never a
        /// servable meal item, never counted toward supply, never
        /// reported as a meal item — whatever policy lists it.
        /// </summary>
        private static bool IsUnspecifiedStaple(string itemId) =>
            string.Equals(itemId, HouseholdItemCatalog.UnspecifiedStapleFoodId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Lane 9a (A5): whether the larder holds a full serving of an
        /// identified meal item right now. Generic unspecified stock
        /// never counts (A8) — it is not a servable meal.
        /// </summary>
        private static bool HasServableMeal(HouseholdInventory inventory, MealSchedulingPolicy policy)
        {
            if (inventory == null || policy?.MealItemPriority == null) return false;
            for (int i = 0; i < policy.MealItemPriority.Count; i++)
            {
                string itemId = policy.MealItemPriority[i];
                if (IsUnspecifiedStaple(itemId)) continue;
                HouseholdItemDefinition definition = HouseholdItemCatalog.Get(itemId);
                if (definition == null || !definition.IsFood) continue;
                if (inventory.GetAvailableUnits(itemId) >= policy.GetServingUnits(itemId)) return true;
            }
            return false;
        }

        /// <summary>
        /// Lane 9a (A5): classifies a missed meal from the inventory
        /// evidence available at close time. Inventory evidence can
        /// only prove NoFoodInLarder (no servable identified food) or,
        /// when food was present but the slot still missed, Unknown —
        /// cash, seller, stock-at-seller, shopper and counter evidence
        /// live in the shopping loop and the shortage monitor, not here,
        /// so this classifier never invents those reasons.
        /// </summary>
        public static MissedMealReasonKind ClassifyMissedMealReason(HouseholdInventory inventory, MealSchedulingPolicy policy) =>
            HasServableMeal(inventory, policy) ? MissedMealReasonKind.Unknown : MissedMealReasonKind.NoFoodInLarder;

        private static string DescribeMealInventoryFailure(HouseholdInventory inventory, MealSchedulingPolicy policy)
        {
            if (inventory == null || policy?.MealItemPriority == null)
                return "No household inventory or meal-item catalog was available at serving time; no food was consumed.";

            var shortLots = new List<string>();
            for (int i = 0; i < policy.MealItemPriority.Count; i++)
            {
                string itemId = policy.MealItemPriority[i];
                // Lane 9a (A8): generic unspecified staple is not a meal
                // item and is never reported as one.
                if (IsUnspecifiedStaple(itemId)) continue;
                HouseholdItemDefinition item = HouseholdItemCatalog.Get(itemId);
                if (item == null || !item.IsFood) continue;
                int available = inventory.GetAvailableUnits(itemId);
                int required = policy.GetServingUnits(itemId);
                if (available > 0 && available < required)
                    shortLots.Add($"{item.DisplayName} {available}/{required} {item.UnitLabel}");
            }
            if (shortLots.Count > 0)
                return "Food is received but no recognized item has a full serving: " + string.Join(", ", shortLots) + ". No food was consumed.";
            return "No meal-sized recognized food lot is currently received in household inventory. Check the household shopping and delivery diagnostics for cash, supplier stock, scheduling, travel, or in-transit causes; no food was consumed.";
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
                // Lane 9a (A8): generic unspecified staple never counts
                // toward days of supply — it is not servable food.
                if (IsUnspecifiedStaple(policy.MealItemPriority[i])) continue;
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

        public bool HasResolution(int householdId, int personId, int dayIndex, MealSlot slot)
        {
            if (mealsByHousehold.TryGetValue(householdId, out List<HouseholdMealRecord> meals)
                && meals.Exists(meal => meal != null && meal.PersonId == personId
                    && meal.DayIndex == dayIndex && meal.Slot == slot))
                return true;
            return missedByHousehold.TryGetValue(householdId, out List<MissedMealRecord> missed)
                && missed.Exists(meal => meal != null && meal.PersonId == personId
                    && meal.DayIndex == dayIndex && meal.Slot == slot);
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
            // Save/Continue restores a snapshot, not an append operation.
            // Loading another save (or the same save twice in one session)
            // must replace the current history instead of duplicating meals,
            // missed slots, and preparation records. A null section is
            // treated as empty when any import section is supplied; three
            // null arguments mean the caller has no meal-log data to import.
            if (meals == null && missed == null && preparations == null) return;
            mealsByHousehold.Clear();
            missedByHousehold.Clear();
            preparationsByHousehold.Clear();

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
