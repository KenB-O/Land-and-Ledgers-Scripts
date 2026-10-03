using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;

namespace LandLedgers.Orchestration.HouseholdSlice
{
    /// <summary>
    /// HF-5: scripted purchase executor for the vertical slice. A TEST DOUBLE standing in
    /// for the full journey/transaction simulation: it honors the embodied-execution
    /// contract (an acting Person executes; a real counterparty is named; the household
    /// ledger books the outflow with provenance) without pretending the travel simulation
    /// exists yet. SUPERSEDED by T1A's <see cref="EmbodiedPurchaseExecutor"/> — new code
    /// must use the real chain (Person -> journey -> transaction). Kept for the slice's
    /// legacy test only.
    /// </summary>
    [Obsolete("Use EmbodiedPurchaseExecutor (T1A): the real Person -> journey -> supplier -> transaction chain.")]
    public sealed class ScriptedPurchaseExecutor : IEmbodiedPurchaseExecutor
    {
        private readonly HouseholdLedgerRegistry ledgers;
        private readonly Dictionary<string, int> priceCentsByCategory;
        private readonly List<string> log = new List<string>();

        public ScriptedPurchaseExecutor(
            HouseholdLedgerRegistry ledgers,
            Dictionary<string, int> priceCentsByCategory)
        {
            this.ledgers = ledgers ?? throw new ArgumentNullException(nameof(ledgers));
            this.priceCentsByCategory = priceCentsByCategory ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string> Log => log;

        public PurchaseExecutionResult Execute(ProcurementNeed need, int actingPersonId, int dayIndex)
        {
            var result = new PurchaseExecutionResult
            {
                NeedSequence = need != null ? need.NeedSequence : -1,
                ActingPersonId = actingPersonId,
            };

            if (need == null || actingPersonId < 0)
            {
                result.Notes = "Rejected: need or acting person missing. Only an acting Person transacts (Canon 13.4).";
                log.Add(result.Notes);
                return result;
            }

            if (!priceCentsByCategory.TryGetValue(need.CategoryId, out int unitPriceCents) || unitPriceCents <= 0)
            {
                result.Notes = $"No legitimate supplier/channel for '{need.CategoryId}': need unmet, no revenue posted.";
                log.Add($"Day {dayIndex}: P{actingPersonId} could not source '{need.CategoryId}' — no sale faked.");
                return result;
            }

            int cost = unitPriceCents * need.UnitsNeeded;
            HouseholdLedger ledger = ledgers.GetOrCreate(need.HouseholdId);
            string rejection = ledger.RecordOutflow(
                dayIndex, cost, $"embodied purchase: {need.UnitsNeeded}u {need.CategoryId}", "general store");

            if (rejection != null)
            {
                result.Notes = $"Purchase failed ledger validation: {rejection}";
                log.Add(result.Notes);
                return result;
            }

            result.Success = true;
            result.UnitsAcquired = need.UnitsNeeded;
            result.AmountPaidCents = cost;
            result.Counterparty = "general store";
            result.Notes = $"P{actingPersonId} purchased {need.UnitsNeeded}u {need.CategoryId} for {cost}c from general store.";
            log.Add($"Day {dayIndex}: {result.Notes}");
            return result;
        }
    }

    /// <summary>
    /// HF-5: the household vertical slice — a small, runnable, demoable scenario proving
    /// households work end to end. Beats:
    /// 1. A founding player household settles (HF-3 formation + GHOST-DEF-006 bootstrap).
    /// 2. Members take roles across the work taxonomy: operator + family labor + one wage
    ///    employee hired through EmploymentRelationship (PKG-6).
    /// 3. The household economy runs: wage inflow with provenance, owner draw, family-labor
    ///    contribution (no payroll), and consumption through EMBODIED purchasing
    ///    (Canon 13.4) — never synthetic demand.
    /// 4. A membership change occurs mid-slice (a birth).
    /// 5. A save/load round-trip keeps every entity ID stable (HF-1).
    ///
    /// Deterministic and EditMode-runnable; Kennedy can also drive it from a
    /// MonoBehaviour later. All simulation-day indexes are explicit.
    /// </summary>
    public sealed class HouseholdVerticalSlice
    {
        public sealed class SliceWorld
        {
            public PopulationState Population = new PopulationState();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public KinshipRegistry Kinship = new KinshipRegistry();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public FamilyLaborLedger FamilyLabor = new FamilyLaborLedger();
            public AnimalRegistry Animals;
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public List<WorkRelationship> WorkRelationships = new List<WorkRelationship>();

            public HouseholdLifecycleManager Lifecycle;
            public HouseholdConsumptionPlanner Planner = new HouseholdConsumptionPlanner();

            public SliceWorld()
            {
                Animals = new AnimalRegistry(Ids);
                Lifecycle = new HouseholdLifecycleManager(Population, Memberships, Kinship, Ids);
            }
        }

        private readonly SliceWorld world = new SliceWorld();
        private readonly List<string> beats = new List<string>();

        public SliceWorld World => world;
        public IReadOnlyList<string> Beats => beats;

        public int PlayerHouseholdId { get; private set; } = -1;
        public int FounderId { get; private set; } = -1;
        public int SpouseId { get; private set; } = -1;
        public int HiredHandId { get; private set; } = -1;
        public string EmploymentId { get; private set; } = string.Empty;

        /// <summary>
        /// T1A: the slice's general store as a real <see cref="IGoodsSupplier"/> — finite
        /// stock at real prices, at a real journey location. Replaces the price-list
        /// fiction the scripted executor used.
        /// </summary>
        private sealed class SliceStoreSupplier : IGoodsSupplier
        {
            public string SupplierBusinessId => "B-general-store-1";
            public string SupplierName => "general store";
            public string LocationId => "slice-general-store";
            private readonly Dictionary<string, int> stock;
            private readonly Dictionary<string, int> prices;

            public SliceStoreSupplier(Dictionary<string, int> prices, int stockUnits)
            {
                this.prices = prices;
                stock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in prices) stock[kvp.Key] = stockUnits;
            }

            public bool HasCategory(string categoryId) => prices.ContainsKey(categoryId);
            public int StockUnits(string categoryId) => stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int PricePerUnitCents(string categoryId) => prices.TryGetValue(categoryId, out int p) ? p : 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
            {
                int sold = Math.Min(requestedUnits, StockUnits(categoryId));
                if (sold > 0) stock[categoryId] -= sold;
                return sold;
            }
        }

        private void Beat(string text)
        {
            beats.Add(text);
        }

        private PersonState AddPerson(string first, string last, int age)
        {
            var person = new PersonState
            {
                id = world.Population.AllocateNextPersonId(),
                firstName = first,
                lastName = last,
                age = age,
                ageBand = HouseholdLifecycleManager.AgeBandForAge(age),
            };
            world.Population.people.Add(person);
            world.Ids.SeedKind(EntityKind.Person, person.id + 1);
            return person;
        }

        /// <summary>Beat 1: the founding player household settles.</summary>
        public HouseholdState RunFounding(int dayIndex)
        {
            PersonState founder = AddPerson("Tomas", "Morrow", 34);
            PersonState spouse = AddPerson("Anna", "Morrow", 31);
            FounderId = founder.id;
            SpouseId = spouse.id;

            HouseholdState household = world.Lifecycle.DeclarePlayerHousehold(
                "household-slice", "Morrow Household",
                new List<int> { founder.id, spouse.id }, dayIndex);
            PlayerHouseholdId = household.id;

            // Two hens for the poultry beat (Tech X §2.3 hooks get exercised in-slice).
            AnimalState hen1 = world.Animals.RegisterAnimal(
                AnimalSpecies.Chicken, "Leghorn", AnimalSex.Female,
                AnimalOwnerKind.Household, household.id.ToString(), dayIndex, "founding flock");
            world.Animals.RegisterAnimal(
                AnimalSpecies.Chicken, "Leghorn", AnimalSex.Female,
                AnimalOwnerKind.Household, household.id.ToString(), dayIndex, "founding flock");

            Beat($"Day {dayIndex}: player household H{household.id} ({household.entityId}) settled " +
                 $"with P{founder.id} and P{spouse.id}; flock started ({hen1.AnimalId}).");
            return household;
        }

        /// <summary>Beat 2: members take roles across the work taxonomy (Canon IV 4.1).</summary>
        public void RunWorkRoles(int dayIndex)
        {
            // Founder: farm/business operator (profit/draw, never payroll).
            world.WorkRelationships.Add(new WorkRelationship
            {
                Kind = WorkRelationshipKind.FarmOrBusinessOperator,
                PersonId = FounderId,
                Counterparty = WorkRelationshipCounterparty.ForBusiness("B-farm-1"),
                RoleDisplayName = "Farm operator",
                StartDayIndex = dayIndex,
                EndDayIndex = -1,
            });

            // Spouse: family enterprise labor (task time/output, no payroll).
            world.WorkRelationships.Add(new WorkRelationship
            {
                Kind = WorkRelationshipKind.FamilyEnterpriseLabor,
                PersonId = SpouseId,
                Counterparty = WorkRelationshipCounterparty.ForBusiness("B-farm-1"),
                RoleDisplayName = "Poultry and garden",
                StartDayIndex = dayIndex,
                EndDayIndex = -1,
            });
            world.FamilyLabor.RecordContribution(new FamilyLaborContribution
            {
                PersonId = SpouseId,
                HouseholdId = PlayerHouseholdId,
                BusinessId = "B-farm-1",
                TaskCategory = "poultry",
                DayIndex = dayIndex,
                MinutesWorked = 300,
                OutputUnits = 22,
                OutputUnitLabel = "eggs collected",
                Notes = "morning flock round",
            }, agreedWageCents: 0);

            // A hired hand: wage employment through EmploymentRelationship (PKG-6 authority).
            PersonState hand = AddPerson("Petr", "Novak", 24);
            HiredHandId = hand.id;
            var employment = new EmploymentRelationship
            {
                Id = world.Ids.Allocate(EntityKind.EmploymentRelationship).ToString(),
                EmployeePersonId = hand.id,
                EmployerBusinessId = "B-farm-1",
                RoleDisplayName = "Field hand",
                Kind = EmploymentKind.SeasonalOrCasual,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(900, "harvest season rate"),
                StartDayIndex = dayIndex,
                Source = EmploymentSource.Authored,
            };
            world.Employments.Register(employment);
            EmploymentId = employment.Id;
            world.Lifecycle.AddMember(PlayerHouseholdId, hand.id, dayIndex, HouseholdMembershipSource.Manual, "hired hand boarding with household");
            world.WorkRelationships.Add(new WorkRelationship
            {
                Kind = employment.ToWorkRelationshipKind(),
                PersonId = hand.id,
                Counterparty = WorkRelationshipCounterparty.ForBusiness("B-farm-1"),
                RoleDisplayName = "Field hand",
                StartDayIndex = dayIndex,
                EndDayIndex = -1,
            });

            Beat($"Day {dayIndex}: P{FounderId} operates the farm; P{SpouseId} works family labor " +
                 $"(300 min, 22 eggs, no payroll); P{hand.id} hired as seasonal hand ({employment.Id}, 900c/week).");
        }

        /// <summary>Beat 3: the household economy runs — every inflow provenanced (Canon 13.2).</summary>
        public void RunEconomy(int dayIndex)
        {
            HouseholdLedger ledger = world.Ledgers.GetOrCreate(PlayerHouseholdId);

            // Wage inflow, provenanced to the EmploymentRelationship.
            world.Employments.GetAgreedWeeklyWageCents(EmploymentId, out int weeklyWage);
            ledger.RecordWagePayment(dayIndex, EmploymentId, HiredHandId, weeklyWage, "week 1");

            // Owner draw from farm profit (documented, not payroll).
            ledger.RecordInflow(dayIndex, 4500, HouseholdIncomeSource.OwnerDraw, "B-farm-1",
                "weekly farm profit draw", "farm");

            // Consumption: plan needs, execute through an acting Person (Canon 13.4).
            // T1A: the real embodied chain — the founder walks a real journey to a
            // real stocked supplier; no sale is faked.
            HouseholdState household = world.Population.GetHousehold(PlayerHouseholdId);
            household.reserves = new List<HouseholdReserveState>
            {
                new HouseholdReserveState { categoryId = "staple_food", displayName = "Staple food", currentUnits = 4, lowThresholdUnits = 14, targetUnits = 20 },
            };
            List<ProcurementNeed> needs = world.Planner.BuildPlan(household, FounderId, dayIndex);
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("slice-farm", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("slice-general-store", JourneyLocationKind.Store, "General Store", 2f, 0f));
            journeys.AddEdge("slice-farm", "slice-general-store", 2.0f, "town road");
            var directory = new SupplierDirectory();
            directory.Register(new SliceStoreSupplier(
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "staple_food", 120 } },
                stockUnits: 1000));
            var executor = new EmbodiedPurchaseExecutor(
                world.Population, world.Ledgers, directory, journeys, pid => "slice-farm");
            int executed = 0;
            foreach (ProcurementNeed need in needs)
            {
                PurchaseExecutionResult result = executor.Execute(need, FounderId, dayIndex);
                if (result.Success)
                {
                    executed++;
                }
            }

            Beat($"Day {dayIndex}: wage {weeklyWage}c via {EmploymentId}, owner draw 4500c, " +
                 $"{executed}/{needs.Count} procurement needs executed by P{FounderId}; " +
                 $"ledger balance {ledger.GetBalanceCents()}c.");
        }

        /// <summary>Beat 4: a membership change mid-slice — a birth.</summary>
        public PersonState RunBirth(int dayIndex)
        {
            PersonState child = world.Lifecycle.RecordBirth(
                PlayerHouseholdId, SpouseId, FounderId, "Petra", "Morrow", dayIndex);

            Beat($"Day {dayIndex}: P{child.id} born to P{SpouseId} and P{FounderId}; " +
                 $"kinship edges recorded separately from membership; household now has " +
                 $"{world.Lifecycle.GetMembers(PlayerHouseholdId).Count} members.");
            return child;
        }

        /// <summary>
        /// Beat 5: save/load round-trip. Exports population-adjacent state, ledgers, animals
        /// and ID cursors, reimports into a fresh world, and verifies every ID is stable
        /// and sequences continue (HF-1).
        /// </summary>
        public List<string> RunSaveLoadRoundTrip()
        {
            var report = new List<string>();

            // Export.
            var dto = new LandLedgersSaveGameDto();
            foreach (PersonState person in world.Population.people)
            {
                dto.population.people.Add(ToPersonDto(person));
            }
            // Household/person int ids are preserved by the legacy fields; typed cursors by HF-1.
            dto.population.nextPersonId = world.Population.NextPersonId;
            dto.population.nextHouseholdId = world.Population.NextHouseholdId;
            var ledgerStates = new List<HouseholdLedgerState>();
            world.Ledgers.ExportState(ledgerStates);
            dto.population.householdLedgers.AddRange(ledgerStates);
            var animalStates = new List<AnimalState>();
            var historicalStates = new List<HistoricalAnimalRecord>();
            var cohortStates = new List<LivestockCohort>();
            var batchStates = new List<EggBatchState>();
            world.Animals.ExportState(animalStates, historicalStates, cohortStates, batchStates);
            dto.population.animals.AddRange(animalStates);
            EntityIdSaveAdapter.WriteCursors(dto, world.Ids, dto.population.nextPersonId, dto.population.nextHouseholdId, 0);

            // Import into a fresh world.
            var fresh = new SliceWorld();
            foreach (PersonSaveDto personDto in dto.population.people)
            {
                fresh.Population.people.Add(FromPersonDto(personDto));
            }
            fresh.Population.RestorePersonIdAllocator(dto.population.nextPersonId);
            fresh.Population.RestoreHouseholdIdAllocator(dto.population.nextHouseholdId);
            fresh.Ledgers.ImportState(dto.population.householdLedgers);
            fresh.Animals.ImportState(dto.population.animals, null, null, null);
            List<string> cursorDiagnostics = EntityIdSaveAdapter.ReadCursors(
                dto, fresh.Ids, dto.population.nextPersonId, dto.population.nextHouseholdId, 0);

            // Verify.
            bool peopleStable = fresh.Population.people.Count == world.Population.people.Count;
            bool founderStable = fresh.Population.GetPerson(FounderId) != null;
            bool ledgerStable = fresh.Ledgers.Get(PlayerHouseholdId) != null
                && fresh.Ledgers.Get(PlayerHouseholdId).GetBalanceCents()
                    == world.Ledgers.Get(PlayerHouseholdId).GetBalanceCents();
            bool animalsStable = true;
            foreach (AnimalState animal in animalStates)
            {
                if (fresh.Animals.GetAnimal(animal.AnimalId) == null)
                {
                    animalsStable = false;
                    break;
                }
            }
            EntityId nextPerson = fresh.Ids.Allocate(EntityKind.Person);
            bool sequencesContinue = nextPerson.Id == world.Population.NextPersonId;

            report.Add($"people stable: {peopleStable} (founder P{FounderId} present: {founderStable})");
            report.Add($"ledger stable: {ledgerStable}");
            report.Add($"animals stable: {animalsStable}");
            report.Add($"sequences continue: {sequencesContinue} (next person {nextPerson})");
            report.Add($"cursor diagnostics: {(cursorDiagnostics.Count == 0 ? "none" : string.Join("; ", cursorDiagnostics))}");

            Beat("Save/load round-trip: " + string.Join(" | ", report));
            return report;
        }

        /// <summary>Runs every beat in order and returns the human-readable report.</summary>
        public string RunAll()
        {
            RunFounding(0);
            RunWorkRoles(1);
            RunEconomy(7);
            RunBirth(60);
            RunSaveLoadRoundTrip();

            var builder = new StringBuilder();
            builder.AppendLine("HOUSEHOLD VERTICAL SLICE — run report");
            foreach (string beat in beats)
            {
                builder.AppendLine("- " + beat);
            }

            return builder.ToString();
        }

        // Minimal person DTO mapping for the slice round-trip (the full save pipeline maps
        // the complete PersonState elsewhere; the slice only needs identity stability).
        private static PersonSaveDto ToPersonDto(PersonState person)
        {
            return new PersonSaveDto
            {
                id = person.id,
                firstName = person.firstName,
                lastName = person.lastName,
                age = person.age,
            };
        }

        private static PersonState FromPersonDto(PersonSaveDto dto)
        {
            return new PersonState
            {
                id = dto.id,
                firstName = dto.firstName,
                lastName = dto.lastName,
                age = dto.age,
                ageBand = HouseholdLifecycleManager.AgeBandForAge(dto.age),
            };
        }
    }
}
