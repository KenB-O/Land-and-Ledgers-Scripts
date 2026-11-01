using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// W2C: the board's daily menu as data. Canon §8.1B: "Menu detail should
    /// remain abstracted to meal quality, food cost, supply reliability and
    /// labor burden rather than becoming a cooking game." A board day is two
    /// meals — breakfast and dinner — matching DailyNeedsService's two
    /// meals per person per day. All quantities are calibration (Canon Part XV).
    /// </summary>
    [Serializable]
    public sealed class BoardingMealSpec
    {
        public string MealId = string.Empty;
        public string DisplayName = string.Empty;
        /// <summary>Ingredient units per single meal (foodName -> units).</summary>
        public List<BoardingIngredientLine> Ingredients = new List<BoardingIngredientLine>();
        /// <summary>Kitchen labor minutes to prepare one meal.</summary>
        public int LaborMinutesPerMeal;

        public BoardingMealSpec() { }

        public static BoardingMealSpec Breakfast()
        {
            return new BoardingMealSpec
            {
                MealId = "board-breakfast",
                DisplayName = "Board breakfast",
                LaborMinutesPerMeal = 8,
                Ingredients = new List<BoardingIngredientLine>
                {
                    new BoardingIngredientLine(BoardingHouseFoodSupply.BreadMaterialId, 2),
                    new BoardingIngredientLine(BoardingHouseFoodSupply.DairyMaterialId, 1),
                },
            };
        }

        public static BoardingMealSpec Dinner()
        {
            return new BoardingMealSpec
            {
                MealId = "board-dinner",
                DisplayName = "Board dinner",
                LaborMinutesPerMeal = 12,
                Ingredients = new List<BoardingIngredientLine>
                {
                    new BoardingIngredientLine(BoardingHouseFoodSupply.MeatMaterialId, 1),
                    new BoardingIngredientLine(BoardingHouseFoodSupply.BreadMaterialId, 1),
                    new BoardingIngredientLine(BoardingHouseFoodSupply.ProduceMaterialId, 1),
                },
            };
        }
    }

    [Serializable]
    public sealed class BoardingIngredientLine
    {
        public string FoodName = string.Empty;
        public int UnitsPerMeal;

        public BoardingIngredientLine() { }

        public BoardingIngredientLine(string foodName, int unitsPerMeal)
        {
            FoodName = foodName ?? string.Empty;
            UnitsPerMeal = unitsPerMeal;
        }
    }

    /// <summary>
    /// W2C: one batch of prepared board meals sitting on the kitchen shelf.
    /// Board meals are day-scoped: prepared today, served today, spoiled
    /// tomorrow (waste — never served). Board meals are included in the
    /// lodging agreement, not retailed, so there is no day-old discounting.
    /// </summary>
    [Serializable]
    public sealed class BoardingHouseMealLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string MealId = string.Empty;
        public int PreparedDayIndex;
        public int MealsRemaining;
        public bool Spoiled;

        /// <summary>Upstream provenance of the ingredients that went into this lot.</summary>
        public List<string> InputProvenance = new List<string>();

        public BoardingHouseMealLot() { }

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

    /// <summary>W2C: one board meal actually served to a real boarder.</summary>
    [Serializable]
    public sealed class BoardingHouseServedMealRecord
    {
        public string ServedId = string.Empty;
        public EntityId LotId = EntityId.Invalid;
        public string MealId = string.Empty;
        public int DayIndex;
        public int PersonId; // the boarder — nutrition credit goes to a real person
        public string ProvenanceChain = string.Empty;

        public BoardingHouseServedMealRecord() { }
    }

    /// <summary>
    /// W2C: the day-scoped ledger of board meals actually served to real
    /// boarders — the producer side of <see cref="IBoardingHouseMealDaySource"/>.
    /// Every served board meal lands here keyed by (day, person id);
    /// <see cref="DailyNeedsService"/> consults it so board meals genuinely
    /// satisfy NPC nutrition (Canon §2.5) instead of existing in a parallel
    /// food economy. The exact parallel of RestaurantMealDayLedger (W2B).
    /// </summary>
    public sealed class BoardingHouseMealDayLedger : IBoardingHouseMealDaySource
    {
        // dayIndex -> personId -> meals served that day.
        private readonly Dictionary<int, Dictionary<int, int>> servedByDay =
            new Dictionary<int, Dictionary<int, int>>();

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Records one served board meal for a real person on a real day. Person ids ≤ 0 are refused loudly.</summary>
        public string ReportServedMeal(int personId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "BoardingHouseMealDayLedger: refused — a served meal needs a real person id; anonymous diners are not recorded.";
            if (dayIndex < 0)
                return "BoardingHouseMealDayLedger: refused — a served meal needs a real day index.";

            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson))
            {
                byPerson = new Dictionary<int, int>();
                servedByDay[dayIndex] = byPerson;
            }

            byPerson.TryGetValue(personId, out int soFar);
            byPerson[personId] = soFar + 1;
            return null;
        }

        /// <summary>IBoardingHouseMealDaySource: meals the person actually ate as board at a boarding house on the given day.</summary>
        public int MealsEatenAtBoardingHouse(int personId, int dayIndex)
        {
            if (personId <= 0 || dayIndex < 0) return 0;
            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson)) return 0;
            return byPerson.TryGetValue(personId, out int count) ? Math.Max(0, count) : 0;
        }

        /// <summary>Total served board meals recorded for the day, across all boarders.</summary>
        public int ServedMealsOnDay(int dayIndex)
        {
            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson)) return 0;
            int total = 0;
            foreach (int count in byPerson.Values) total += Math.Max(0, count);
            return total;
        }

        /// <summary>Drops days strictly before the given day (bounded memory; yesterday's board meals never feed tomorrow's nutrition).</summary>
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
                diag.Add($"BoardingHouseMealDayLedger: pruned {stale.Count} day(s) before day {dayIndex} — served meals are day-scoped facts.");
            }
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingHouseMealDayLedgerSaveDto
        {
            public List<BoardingHouseMealDayEntry> Entries = new List<BoardingHouseMealDayEntry>();
        }

        [Serializable]
        public sealed class BoardingHouseMealDayEntry
        {
            public int DayIndex;
            public int PersonId;
            public int MealsServed;
        }

        public BoardingHouseMealDayLedgerSaveDto CaptureSaveDto()
        {
            var dto = new BoardingHouseMealDayLedgerSaveDto();
            foreach (var dayKvp in servedByDay)
            {
                foreach (var personKvp in dayKvp.Value)
                {
                    dto.Entries.Add(new BoardingHouseMealDayEntry
                    {
                        DayIndex = dayKvp.Key,
                        PersonId = personKvp.Key,
                        MealsServed = personKvp.Value,
                    });
                }
            }
            return dto;
        }

        public void LoadFromSaveDto(BoardingHouseMealDayLedgerSaveDto dto)
        {
            servedByDay.Clear();
            if (dto?.Entries == null) return;
            foreach (BoardingHouseMealDayEntry entry in dto.Entries)
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
    /// W2C: sums board-meal sources across boarding houses (the production
    /// parallel of CompositeRestaurantMealSource). A town with several
    /// houses feeds the nutrition link from all of them.
    /// </summary>
    public sealed class CompositeBoardingHouseMealSource : IBoardingHouseMealDaySource
    {
        private readonly IReadOnlyList<IBoardingHouseMealDaySource> sources;

        public CompositeBoardingHouseMealSource(IReadOnlyList<IBoardingHouseMealDaySource> sources)
        {
            this.sources = sources;
        }

        public int MealsEatenAtBoardingHouse(int personId, int dayIndex)
        {
            if (sources == null || personId <= 0 || dayIndex < 0) return 0;
            int total = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                IBoardingHouseMealDaySource source = sources[i];
                if (source != null) total += Math.Max(0, source.MealsEatenAtBoardingHouse(personId, dayIndex));
            }
            return total;
        }
    }

    /// <summary>
    /// W2C: the boarding-house kitchen — the per-house food-service side of
    /// "board". Canon §8.1B: bed capacity and meal capacity are SEPARATE;
    /// a house can have empty beds but no practical kitchen capacity to add
    /// full-board customers. The kitchen prepares today's board meals from
    /// the pantry's real lots (with provenance) under a real labor budget
    /// and serves them to real boarders. Shortfalls are service failures
    /// (boarders pay for these meals) — logged loudly, never papered over.
    /// </summary>
    public sealed class BoardingHouseKitchen
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly BoardingHouseFoodStock foodStock = new BoardingHouseFoodStock();
        private readonly List<BoardingHouseMealLot> mealShelf = new List<BoardingHouseMealLot>();
        private readonly List<BoardingHouseServedMealRecord> servedMeals = new List<BoardingHouseServedMealRecord>();
        private readonly BoardingHouseMealDayLedger mealDayLedger = new BoardingHouseMealDayLedger();
        private readonly EntityIdRegistry idRegistry;
        private int servedSequence = 1;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public BoardingHouseFoodStock FoodStock => foodStock;
        public IReadOnlyList<BoardingHouseMealLot> MealShelf => mealShelf;
        public IReadOnlyList<BoardingHouseServedMealRecord> ServedMeals => servedMeals;
        public IBoardingHouseMealDaySource MealDaySource => mealDayLedger;
        public BoardingHouseMealDayLedger MealLedger => mealDayLedger;

        /// <summary>
        /// D1F: the house's stove-fuel store, wired by the runtime (Canon
        /// §8.1B: food service creates real demand for fuel). Null = no fuel
        /// store attached.
        /// </summary>
        public BoardingHouseFuelStock FuelStock { get; set; }

        /// <summary>
        /// D1F: when true (default), preparing a board meal requires burning
        /// fuel — no fuel, no cooking, loudly. Set false only to route around
        /// unmodeled contexts (D1E precedent).
        /// </summary>
        public bool RequireFuel = true;

        public BoardingHouseKitchen(EntityIdRegistry idRegistry)
        {
            this.idRegistry = idRegistry;
        }

        /// <summary>
        /// Runs one day of board service: ages the shelf (yesterday's
        /// unserved meals become waste), prepares today's breakfasts and
        /// dinners for board-included boarders, and serves them. Returns the
        /// number of boarders fully served (2 meals).
        /// </summary>
        public int ExecuteDay(BoardingHouseBoarderRegister register, int dayIndex, int kitchenLaborMinutes, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (dayIndex < 0)
            {
                diag.Add("BoardingHouseKitchen: refused — a kitchen day needs a real day index.");
                return 0;
            }
            if (register == null)
            {
                diag.Add("BoardingHouseKitchen: no boarder register — no boarders, no meals.");
                return 0;
            }

            AgeShelf(dayIndex, diag);

            List<int> boarderIds = register.BoarderPersonIdsWithBoardIncluded();
            boarderIds.Sort();
            if (boarderIds.Count == 0)
            {
                diag.Add($"BoardingHouseKitchen: no board-included boarders on day {dayIndex} — the kitchen stays cold.");
                return 0;
            }

            int laborRemaining = Math.Max(0, kitchenLaborMinutes);
            PrepareMeals(BoardingMealSpec.Breakfast(), boarderIds.Count, dayIndex, ref laborRemaining, diag);
            PrepareMeals(BoardingMealSpec.Dinner(), boarderIds.Count, dayIndex, ref laborRemaining, diag);

            int fullyServed = 0;
            foreach (int personId in boarderIds)
            {
                int served = 0;
                served += ServeMeal("board-breakfast", personId, dayIndex, diag);
                served += ServeMeal("board-dinner", personId, dayIndex, diag);
                if (served >= 2) fullyServed++;
                else diag.Add($"BoardingHouseKitchen: boarder {personId} was shorted {2 - served} board meal(s) on day {dayIndex} — a service failure under their lodging agreement.");
            }

            diag.Add($"BoardingHouseKitchen: day {dayIndex} — {fullyServed}/{boarderIds.Count} board-included boarder(s) fully served.");
            return fullyServed;
        }

        private void AgeShelf(int dayIndex, List<string> diag)
        {
            for (int i = mealShelf.Count - 1; i >= 0; i--)
            {
                BoardingHouseMealLot lot = mealShelf[i];
                if (lot == null) { mealShelf.RemoveAt(i); continue; }
                lot.AgeToDay(dayIndex);
                if (lot.Spoiled)
                {
                    if (lot.MealsRemaining > 0)
                    {
                        diag.Add($"BoardingHouseKitchen: lot {lot.LotId} ({lot.MealId}, {lot.MealsRemaining} meal(s), prepared day {lot.PreparedDayIndex}) went unserved — written off as waste (day {dayIndex}). Never served.");
                    }
                    mealShelf.RemoveAt(i);
                }
            }
        }

        private bool IngredientsAvailable(BoardingMealSpec spec, out string shortFood)
        {
            shortFood = string.Empty;
            foreach (BoardingIngredientLine ingredient in spec.Ingredients)
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

        private void PrepareMeals(BoardingMealSpec spec, int mealCount, int dayIndex, ref int laborRemaining, List<string> diag)
        {
            int prepped = 0;
            for (int n = 0; n < mealCount; n++)
            {
                if (laborRemaining < spec.LaborMinutesPerMeal)
                {
                    diag.Add($"BoardingHouseKitchen: {spec.DisplayName} prep shorted by kitchen labor — {mealCount - n} meal(s) unprepared on day {dayIndex}.");
                    break;
                }

                // Availability is checked before ANY dispense so a shortfall
                // never strands partially-dispensed ingredients: the pantry
                // dispenses whole meals or nothing.
                if (!IngredientsAvailable(spec, out string shortFood))
                {
                    diag.Add($"BoardingHouseKitchen: {spec.DisplayName} prep refused — pantry shortfall on {shortFood} (day {dayIndex}). Boarders go short, honestly.");
                    break;
                }

                // D1F: the cooking fire (Canon §8.1B fuel demand). Fuel is
                // checked before anything dispenses, so a cold stove never
                // strands half-prepped ingredients.
                if (RequireFuel)
                {
                    if (FuelStock == null)
                    {
                        diag.Add($"BoardingHouseKitchen: {spec.DisplayName} prep refused — no fuel store wired; the stove stays cold (day {dayIndex}).");
                        break;
                    }
                    if (FuelStock.UnitsOnHand(BoardingHouseFuelSupply.FuelMaterialId) < BoardingHouseFuelSupply.FuelUnitsPerBoardMeal)
                    {
                        diag.Add($"BoardingHouseKitchen: {spec.DisplayName} prep refused — fuel store empty (day {dayIndex}). Boarders go short, honestly.");
                        break;
                    }
                }

                var dispenseLines = new List<BoardingHouseFoodDispenseLine>();
                foreach (BoardingIngredientLine ingredient in spec.Ingredients)
                {
                    if (ingredient == null) continue;
                    dispenseLines.AddRange(
                        foodStock.TryDispenseUnits(ingredient.FoodName, ingredient.UnitsPerMeal, dayIndex, diag));
                }

                List<BoardingHouseFuelDispenseLine> fuelLines = null;
                if (RequireFuel)
                {
                    // The pre-check above guarantees this burn succeeds; the
                    // null branch is pure defense (a burn never strands a
                    // half-prepped meal).
                    fuelLines = FuelStock.TryBurnUnits(BoardingHouseFuelSupply.FuelMaterialId,
                        BoardingHouseFuelSupply.FuelUnitsPerBoardMeal, dayIndex, diag);
                    if (fuelLines == null)
                    {
                        diag.Add($"BoardingHouseKitchen: {spec.DisplayName} prep refused — the fuel burn failed unexpectedly (day {dayIndex}).");
                        break;
                    }
                }

                laborRemaining -= spec.LaborMinutesPerMeal;
                var lot = new BoardingHouseMealLot
                {
                    LotId = idRegistry != null ? idRegistry.Allocate(EntityKind.Lot) : EntityId.Invalid,
                    MealId = spec.MealId,
                    PreparedDayIndex = dayIndex,
                    MealsRemaining = 1,
                    Spoiled = false,
                };
                foreach (BoardingHouseFoodDispenseLine line in dispenseLines)
                    lot.InputProvenance.Add($"{line.UnitsTaken} × {line.FoodName} — {line.ProvenanceChain}");
                if (fuelLines != null)
                    foreach (BoardingHouseFuelDispenseLine fuelLine in fuelLines)
                        lot.InputProvenance.Add($"fuel: {fuelLine.UnitsTaken} × {fuelLine.FuelName} — {fuelLine.ProvenanceChain}");
                mealShelf.Add(lot);
                prepped++;
            }
            if (prepped > 0)
                diag.Add($"BoardingHouseKitchen: prepared {prepped} × {spec.DisplayName} (day {dayIndex}).");
        }

        private int ServeMeal(string mealId, int personId, int dayIndex, List<string> diag)
        {
            for (int i = 0; i < mealShelf.Count; i++)
            {
                BoardingHouseMealLot lot = mealShelf[i];
                if (lot == null || !string.Equals(lot.MealId, mealId, StringComparison.OrdinalIgnoreCase)) continue;
                if (lot.TakeMeals(1) <= 0) continue;

                servedMeals.Add(new BoardingHouseServedMealRecord
                {
                    ServedId = $"bh-served-{servedSequence++}",
                    LotId = lot.LotId,
                    MealId = mealId,
                    DayIndex = dayIndex,
                    PersonId = personId,
                    ProvenanceChain = string.Join(" | ", lot.InputProvenance.ToArray()),
                });
                string refusal = mealDayLedger.ReportServedMeal(personId, dayIndex, diag);
                if (refusal != null) diag.Add($"BoardingHouseKitchen: {refusal}");
                if (lot.MealsRemaining <= 0) mealShelf.RemoveAt(i);
                return 1;
            }
            return 0;
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingHouseKitchenSaveDto
        {
            public int ServedSequence = 1;
            public BoardingHouseFoodStock.BoardingHouseFoodStockSaveDto FoodStock = new BoardingHouseFoodStock.BoardingHouseFoodStockSaveDto();
            public List<BoardingHouseMealLot> MealShelf = new List<BoardingHouseMealLot>();
            public List<BoardingHouseServedMealRecord> ServedMeals = new List<BoardingHouseServedMealRecord>();
            public BoardingHouseMealDayLedger.BoardingHouseMealDayLedgerSaveDto MealLedger = new BoardingHouseMealDayLedger.BoardingHouseMealDayLedgerSaveDto();
        }

        public BoardingHouseKitchenSaveDto CaptureSaveDto()
        {
            var dto = new BoardingHouseKitchenSaveDto
            {
                ServedSequence = Math.Max(1, servedSequence),
                FoodStock = foodStock.CaptureSaveDto(),
                MealLedger = mealDayLedger.CaptureSaveDto(),
            };
            foreach (BoardingHouseMealLot lot in mealShelf) if (lot != null) dto.MealShelf.Add(lot);
            foreach (BoardingHouseServedMealRecord record in servedMeals) if (record != null) dto.ServedMeals.Add(record);
            return dto;
        }

        public void LoadFromSaveDto(BoardingHouseKitchenSaveDto dto)
        {
            mealShelf.Clear();
            servedMeals.Clear();
            servedSequence = 1;
            if (dto == null) return;
            servedSequence = Math.Max(1, dto.ServedSequence);
            foodStock.LoadFromSaveDto(dto.FoodStock);
            if (dto.MealShelf != null)
                foreach (BoardingHouseMealLot lot in dto.MealShelf)
                    if (lot != null && lot.LotId != EntityId.Invalid) mealShelf.Add(lot);
            if (dto.ServedMeals != null)
                foreach (BoardingHouseServedMealRecord record in dto.ServedMeals)
                    if (record != null && record.PersonId > 0) servedMeals.Add(record);
            mealDayLedger.LoadFromSaveDto(dto.MealLedger);
        }
        #endregion
    }
}
