using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// W3B: the hotel dining room's daily menu as data. Canon §8.1B: "Menu
    /// detail should remain abstracted to meal quality, food cost, supply
    /// reliability and labor burden rather than becoming a cooking game."
    /// A board day is two meals — breakfast and dinner — matching
    /// DailyNeedsService's two meals per person per day. The ingredient
    /// lines follow the W2C boarding-house table; the labor minutes are the
    /// hotel's own (a dining room runs heavier than a boarding table).
    /// All quantities are calibration (Canon Part XV).
    /// </summary>
    [Serializable]
    public sealed class HotelMealSpec
    {
        public string MealId = string.Empty;
        public string DisplayName = string.Empty;
        /// <summary>Ingredient units per single meal (foodName -> units).</summary>
        public List<HotelIngredientLine> Ingredients = new List<HotelIngredientLine>();
        /// <summary>Kitchen labor minutes to prepare one meal.</summary>
        public int LaborMinutesPerMeal;

        public HotelMealSpec() { }

        public static HotelMealSpec Breakfast()
        {
            return new HotelMealSpec
            {
                MealId = "hotel-breakfast",
                DisplayName = "Hotel breakfast",
                LaborMinutesPerMeal = 10,
                Ingredients = new List<HotelIngredientLine>
                {
                    new HotelIngredientLine(HotelFoodSupply.BreadMaterialId, 2),
                    new HotelIngredientLine(HotelFoodSupply.DairyMaterialId, 1),
                },
            };
        }

        public static HotelMealSpec Dinner()
        {
            return new HotelMealSpec
            {
                MealId = "hotel-dinner",
                DisplayName = "Hotel dinner",
                LaborMinutesPerMeal = 15,
                Ingredients = new List<HotelIngredientLine>
                {
                    new HotelIngredientLine(HotelFoodSupply.MeatMaterialId, 1),
                    new HotelIngredientLine(HotelFoodSupply.BreadMaterialId, 1),
                    new HotelIngredientLine(HotelFoodSupply.ProduceMaterialId, 1),
                },
            };
        }
    }

    [Serializable]
    public sealed class HotelIngredientLine
    {
        public string FoodName = string.Empty;
        public int UnitsPerMeal;

        public HotelIngredientLine() { }

        public HotelIngredientLine(string foodName, int unitsPerMeal)
        {
            FoodName = foodName ?? string.Empty;
            UnitsPerMeal = unitsPerMeal;
        }
    }

    /// <summary>
    /// W3B: one batch of prepared dining-room meals sitting on the kitchen
    /// shelf. Hotel meals are day-scoped: prepared today, served today,
    /// spoiled tomorrow (waste — never served). Dining-room meals ride with
    /// the room-and-board package, not retailed, so there is no day-old
    /// discounting. The W2C meal-lot pattern, hotel-typed.
    /// </summary>
    [Serializable]
    public sealed class HotelMealLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string MealId = string.Empty;
        public int PreparedDayIndex;
        public int MealsRemaining;
        public bool Spoiled;

        /// <summary>Upstream provenance of the ingredients that went into this lot.</summary>
        public List<string> InputProvenance = new List<string>();

        public HotelMealLot() { }

        public void AgeToDay(int dayIndex)
        {
            Spoiled = dayIndex > PreparedDayIndex;
        }

        public bool CanServe => !Spoiled && MealsRemaining > 0;

        public int TakeMeals(int requested)
        {
            int taken = Math.Min(Math.Max(0, requested), CanServe ? MealsRemaining : 0);
            MealsRemaining -= taken;
            return taken;
        }
    }

    /// <summary>W3B: one dining-room meal actually served to a real guest.</summary>
    [Serializable]
    public sealed class HotelServedMealRecord
    {
        public string ServedId = string.Empty;
        public EntityId LotId = EntityId.Invalid;
        public string MealId = string.Empty;
        public int DayIndex;
        public int PersonId; // the guest — nutrition credit goes to a real person
        public string ProvenanceChain = string.Empty;

        public HotelServedMealRecord() { }
    }

    /// <summary>
    /// W3B: hotel-specific board policy — WHO the dining room feeds.
    /// Proprietor policy as data (not a hardcoded guess): a frontier hotel
    /// might feed every guest, only its transient nightly travelers, or run
    /// room-only and leave the dining room dark.
    /// </summary>
    public enum HotelBoardPolicy
    {
        /// <summary>Only nightly-stay guests (transient travelers) take board — the W3B demand driver.</summary>
        NightlyGuestsOnly = 0,
        /// <summary>Every current guest, nightly and weekly.</summary>
        AllGuests = 1,
        /// <summary>The dining room is dark — room-only house.</summary>
        NoBoard = 2,
    }

    /// <summary>
    /// W3B: the day-scoped ledger of dining-room meals actually served to
    /// real guests — the producer side of <see cref="IHotelMealDaySource"/>.
    /// Every served hotel meal lands here keyed by (day, person id);
    /// <see cref="DailyNeedsService"/> consults it so hotel meals genuinely
    /// satisfy NPC nutrition (Canon §2.5) instead of existing in a parallel
    /// food economy. The exact parallel of RestaurantMealDayLedger (W2B)
    /// and BoardingHouseMealDayLedger (W2C).
    /// </summary>
    public sealed class HotelMealDayLedger : IHotelMealDaySource
    {
        // dayIndex -> personId -> meals served that day.
        private readonly Dictionary<int, Dictionary<int, int>> servedByDay =
            new Dictionary<int, Dictionary<int, int>>();

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Records one served hotel meal for a real person on a real day. Person ids ≤ 0 are refused loudly.</summary>
        public string ReportServedMeal(int personId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "HotelMealDayLedger: refused — a served meal needs a real person id; anonymous diners are not recorded.";
            if (dayIndex < 0)
                return "HotelMealDayLedger: refused — a served meal needs a real day index.";

            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson))
            {
                byPerson = new Dictionary<int, int>();
                servedByDay[dayIndex] = byPerson;
            }

            byPerson.TryGetValue(personId, out int soFar);
            byPerson[personId] = soFar + 1;
            return null;
        }

        /// <summary>IHotelMealDaySource: meals the person actually ate at a hotel dining room on the given day.</summary>
        public int MealsEatenAtHotel(int personId, int dayIndex)
        {
            if (personId <= 0 || dayIndex < 0) return 0;
            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson)) return 0;
            return byPerson.TryGetValue(personId, out int count) ? Math.Max(0, count) : 0;
        }

        /// <summary>Total served hotel meals recorded for the day, across all guests.</summary>
        public int ServedMealsOnDay(int dayIndex)
        {
            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson)) return 0;
            int total = 0;
            foreach (int count in byPerson.Values) total += Math.Max(0, count);
            return total;
        }

        /// <summary>Drops days strictly before the given day (bounded memory; yesterday's hotel meals never feed tomorrow's nutrition).</summary>
        public void PruneBefore(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var stale = new List<int>();
            foreach (int day in servedByDay.Keys)
            {
                if (day < dayIndex) stale.Add(day);
            }

            foreach (int day in stale) servedByDay.Remove(day);
            if (stale.Count > 0)
            {
                diag.Add($"HotelMealDayLedger: pruned {stale.Count} day(s) before day {dayIndex} — served meals are day-scoped facts.");
            }
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelMealDayLedgerSaveDto
        {
            public List<HotelMealDayEntry> Entries = new List<HotelMealDayEntry>();
        }

        [Serializable]
        public sealed class HotelMealDayEntry
        {
            public int DayIndex;
            public int PersonId;
            public int MealsServed;
        }

        public HotelMealDayLedgerSaveDto CaptureSaveDto()
        {
            var dto = new HotelMealDayLedgerSaveDto();
            foreach (var dayKvp in servedByDay)
            {
                foreach (var personKvp in dayKvp.Value)
                {
                    dto.Entries.Add(new HotelMealDayEntry
                    {
                        DayIndex = dayKvp.Key,
                        PersonId = personKvp.Key,
                        MealsServed = personKvp.Value,
                    });
                }
            }
            return dto;
        }

        public void LoadFromSaveDto(HotelMealDayLedgerSaveDto dto)
        {
            servedByDay.Clear();
            if (dto?.Entries == null) return;
            foreach (HotelMealDayEntry entry in dto.Entries)
            {
                if (entry == null || entry.PersonId <= 0 || entry.DayIndex < 0 || entry.MealsServed <= 0) continue;
                if (!servedByDay.TryGetValue(entry.DayIndex, out Dictionary<int, int> byPerson))
                {
                    byPerson = new Dictionary<int, int>();
                    servedByDay[entry.DayIndex] = byPerson;
                }
                byPerson.TryGetValue(entry.PersonId, out int soFar);
                byPerson[entry.PersonId] = soFar + entry.MealsServed;
            }
        }
        #endregion
    }

    /// <summary>
    /// W3B: sums hotel-meal sources across hotels (the production parallel
    /// of CompositeRestaurantMealSource / CompositeBoardingHouseMealSource).
    /// A town with several hotels feeds the nutrition link from all of them.
    /// </summary>
    public sealed class CompositeHotelMealSource : IHotelMealDaySource
    {
        private readonly IReadOnlyList<IHotelMealDaySource> sources;

        public CompositeHotelMealSource(IReadOnlyList<IHotelMealDaySource> sources)
        {
            this.sources = sources;
        }

        public int MealsEatenAtHotel(int personId, int dayIndex)
        {
            if (sources == null || personId <= 0 || dayIndex < 0) return 0;
            int total = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                IHotelMealDaySource source = sources[i];
                if (source != null) total += Math.Max(0, source.MealsEatenAtHotel(personId, dayIndex));
            }
            return total;
        }
    }

    /// <summary>
    /// W3B: the hotel dining room — the per-hotel food-service side of
    /// room-and-board. Follows the W2C boarding-house kitchen pattern
    /// without duplicating its types: the kitchen prepares today's meals
    /// from the hotel pantry's real lots (with provenance) under a real
    /// labor budget and serves them to real guests per the hotel's board
    /// policy. Shortfalls are service failures (guests paid for board) —
    /// logged loudly, never papered over. Canon §8.1B: bed capacity and
    /// meal capacity are SEPARATE; a hotel can have empty beds but no
    /// practical kitchen capacity to feed more board guests.
    /// </summary>
    public sealed class HotelKitchen
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly HotelFoodStock foodStock = new HotelFoodStock();
        private readonly List<HotelMealLot> mealShelf = new List<HotelMealLot>();
        private readonly List<HotelServedMealRecord> servedMeals = new List<HotelServedMealRecord>();
        private readonly HotelMealDayLedger mealDayLedger = new HotelMealDayLedger();
        private readonly EntityIdRegistry idRegistry;
        private int servedSequence = 1;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public HotelFoodStock FoodStock => foodStock;
        public IReadOnlyList<HotelMealLot> MealShelf => mealShelf;
        public IReadOnlyList<HotelServedMealRecord> ServedMeals => servedMeals;
        public IHotelMealDaySource MealDaySource => mealDayLedger;
        public HotelMealDayLedger MealLedger => mealDayLedger;

        /// <summary>Proprietor policy: who takes board. Defaults to nightly (transient) guests.</summary>
        public HotelBoardPolicy BoardPolicy { get; set; } = HotelBoardPolicy.NightlyGuestsOnly;

        public HotelKitchen(EntityIdRegistry idRegistry)
        {
            this.idRegistry = idRegistry;
        }

        /// <summary>
        /// Guests the board policy actually feeds, in deterministic person-id
        /// order. Weekly long-stay guests are NOT transients — under the
        /// default policy they keep their own (household) meal arrangements.
        /// </summary>
        public List<int> BoardEligibleGuestIds(HotelGuestRegister register)
        {
            var eligible = new List<int>();
            if (register == null || BoardPolicy == HotelBoardPolicy.NoBoard) return eligible;
            foreach (HotelGuestRecord record in register.Guests)
            {
                if (record == null || record.PersonId <= 0) continue;
                if (BoardPolicy == HotelBoardPolicy.NightlyGuestsOnly && record.StayKind != HotelStayKind.Nightly)
                    continue;
                if (!eligible.Contains(record.PersonId)) eligible.Add(record.PersonId);
            }
            eligible.Sort();
            return eligible;
        }

        /// <summary>
        /// Runs one day of dining-room service: ages the shelf (yesterday's
        /// unserved meals become waste), prepares today's breakfasts and
        /// dinners for board-eligible guests, and serves them. Returns the
        /// number of guests fully served (2 meals).
        /// </summary>
        public int ExecuteDay(HotelGuestRegister register, int dayIndex, int kitchenLaborMinutes, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (dayIndex < 0)
            {
                diag.Add("HotelKitchen: refused — a kitchen day needs a real day index.");
                return 0;
            }
            if (register == null)
            {
                diag.Add("HotelKitchen: no guest register — no guests, no meals.");
                return 0;
            }

            AgeShelf(dayIndex, diag);

            List<int> guestIds = BoardEligibleGuestIds(register);
            if (guestIds.Count == 0)
            {
                diag.Add($"HotelKitchen: no board-eligible guests on day {dayIndex} — the dining room stays dark.");
                return 0;
            }
            if (kitchenLaborMinutes <= 0)
            {
                diag.Add($"HotelKitchen: no kitchen labor allocated on day {dayIndex} — the dining room stays dark (no service failure: the shift was never staffed).");
                return 0;
            }

            int laborRemaining = Math.Max(0, kitchenLaborMinutes);
            PrepareMeals(HotelMealSpec.Breakfast(), guestIds.Count, dayIndex, ref laborRemaining, diag);
            PrepareMeals(HotelMealSpec.Dinner(), guestIds.Count, dayIndex, ref laborRemaining, diag);

            int fullyServed = 0;
            foreach (int personId in guestIds)
            {
                int served = 0;
                served += ServeMeal("hotel-breakfast", personId, dayIndex, diag);
                served += ServeMeal("hotel-dinner", personId, dayIndex, diag);
                if (served >= 2) fullyServed++;
                else diag.Add($"HotelKitchen: guest {personId} was shorted {2 - served} board meal(s) on day {dayIndex} — a service failure under their room-and-board package.");
            }

            diag.Add($"HotelKitchen: day {dayIndex} — {fullyServed}/{guestIds.Count} board-eligible guest(s) fully served.");
            return fullyServed;
        }

        private void AgeShelf(int dayIndex, List<string> diag)
        {
            for (int i = mealShelf.Count - 1; i >= 0; i--)
            {
                HotelMealLot lot = mealShelf[i];
                if (lot == null) { mealShelf.RemoveAt(i); continue; }
                lot.AgeToDay(dayIndex);
                if (lot.Spoiled)
                {
                    if (lot.MealsRemaining > 0)
                    {
                        diag.Add($"HotelKitchen: lot {lot.LotId} ({lot.MealId}, {lot.MealsRemaining} meal(s), prepared day {lot.PreparedDayIndex}) went unserved — written off as waste (day {dayIndex}). Never served.");
                    }
                    mealShelf.RemoveAt(i);
                }
            }
        }

        private bool IngredientsAvailable(HotelMealSpec spec, out string shortFood)
        {
            shortFood = string.Empty;
            foreach (HotelIngredientLine ingredient in spec.Ingredients)
            {
                if (ingredient == null) continue;
                if (foodStock.UnitsOnHand(ingredient.FoodName) < ingredient.UnitsPerMeal)
                {
                    shortFood = ingredient.FoodName;
                    return false;
                }
            }
            return true;
        }

        private void PrepareMeals(HotelMealSpec spec, int mealCount, int dayIndex, ref int laborRemaining, List<string> diag)
        {
            int prepped = 0;
            for (int n = 0; n < mealCount; n++)
            {
                if (laborRemaining < spec.LaborMinutesPerMeal)
                {
                    diag.Add($"HotelKitchen: {spec.DisplayName} prep shorted by kitchen labor — {mealCount - n} meal(s) unprepared on day {dayIndex}.");
                    break;
                }

                // Availability is checked before ANY dispense so a shortfall
                // never strands partially-dispensed ingredients: the pantry
                // dispenses whole meals or nothing.
                if (!IngredientsAvailable(spec, out string shortFood))
                {
                    diag.Add($"HotelKitchen: {spec.DisplayName} prep refused — pantry shortfall on {shortFood} (day {dayIndex}). Guests go short, honestly.");
                    break;
                }

                var dispenseLines = new List<HotelFoodDispenseLine>();
                foreach (HotelIngredientLine ingredient in spec.Ingredients)
                {
                    if (ingredient == null) continue;
                    dispenseLines.AddRange(
                        foodStock.TryDispenseUnits(ingredient.FoodName, ingredient.UnitsPerMeal, dayIndex, diag));
                }

                laborRemaining -= spec.LaborMinutesPerMeal;
                var lot = new HotelMealLot
                {
                    LotId = idRegistry != null ? idRegistry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                    MealId = spec.MealId,
                    PreparedDayIndex = dayIndex,
                    MealsRemaining = 1,
                    Spoiled = false,
                };
                foreach (HotelFoodDispenseLine line in dispenseLines)
                    lot.InputProvenance.Add($"{line.UnitsTaken} × {line.FoodName} — {line.ProvenanceChain}");
                mealShelf.Add(lot);
                prepped++;
            }
            if (prepped > 0)
                diag.Add($"HotelKitchen: prepared {prepped} × {spec.DisplayName} (day {dayIndex}).");
        }

        private int ServeMeal(string mealId, int personId, int dayIndex, List<string> diag)
        {
            for (int i = 0; i < mealShelf.Count; i++)
            {
                HotelMealLot lot = mealShelf[i];
                if (lot == null || !string.Equals(lot.MealId, mealId, StringComparison.OrdinalIgnoreCase)) continue;
                if (lot.TakeMeals(1) <= 0) continue;

                servedMeals.Add(new HotelServedMealRecord
                {
                    ServedId = $"hotel-served-{servedSequence++}",
                    LotId = lot.LotId,
                    MealId = mealId,
                    DayIndex = dayIndex,
                    PersonId = personId,
                    ProvenanceChain = string.Join(" | ", lot.InputProvenance.ToArray()),
                });
                string refusal = mealDayLedger.ReportServedMeal(personId, dayIndex, diag);
                if (refusal != null) diag.Add($"HotelKitchen: {refusal}");
                if (lot.MealsRemaining <= 0) mealShelf.RemoveAt(i);
                return 1;
            }
            return 0;
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelKitchenSaveDto
        {
            public int ServedSequence = 1;
            public int BoardPolicy;
            public HotelFoodStock.HotelFoodStockSaveDto FoodStock = new HotelFoodStock.HotelFoodStockSaveDto();
            public List<HotelMealLot> MealShelf = new List<HotelMealLot>();
            public List<HotelServedMealRecord> ServedMeals = new List<HotelServedMealRecord>();
            public HotelMealDayLedger.HotelMealDayLedgerSaveDto MealLedger = new HotelMealDayLedger.HotelMealDayLedgerSaveDto();
        }

        public HotelKitchenSaveDto CaptureSaveDto()
        {
            var dto = new HotelKitchenSaveDto
            {
                ServedSequence = Math.Max(1, servedSequence),
                BoardPolicy = (int)BoardPolicy,
                FoodStock = foodStock.CaptureSaveDto(),
                MealLedger = mealDayLedger.CaptureSaveDto(),
            };
            foreach (HotelMealLot lot in mealShelf) if (lot != null) dto.MealShelf.Add(lot);
            foreach (HotelServedMealRecord record in servedMeals) if (record != null) dto.ServedMeals.Add(record);
            return dto;
        }

        public void LoadFromSaveDto(HotelKitchenSaveDto dto)
        {
            mealShelf.Clear();
            servedMeals.Clear();
            servedSequence = 1;
            if (dto == null) return;
            servedSequence = Math.Max(1, dto.ServedSequence);
            if (Enum.IsDefined(typeof(HotelBoardPolicy), dto.BoardPolicy))
                BoardPolicy = (HotelBoardPolicy)dto.BoardPolicy;
            foodStock.LoadFromSaveDto(dto.FoodStock);
            if (dto.MealShelf != null)
                foreach (HotelMealLot lot in dto.MealShelf)
                    if (lot != null && lot.LotId != EntityId.Invalid) mealShelf.Add(lot);
            if (dto.ServedMeals != null)
                foreach (HotelServedMealRecord record in dto.ServedMeals)
                    if (record != null && record.PersonId > 0) servedMeals.Add(record);
            mealDayLedger.LoadFromSaveDto(dto.MealLedger);
        }
        #endregion
    }
}
