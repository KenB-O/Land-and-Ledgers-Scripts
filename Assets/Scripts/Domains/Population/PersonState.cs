using System;

namespace LandLedgers.Population
{
    [Serializable]
    public sealed class PersonState
    {
        public int id;
        public string firstName;
        public string lastName;
        /// <summary>
        /// Pass V P0-2: DERIVED CACHE, not authority. The birth date
        /// (<see cref="birthDate"/>) is the single age authority; this field is
        /// refreshed from it via <see cref="RefreshDerivedAge"/> and must never
        /// be incremented as a counter. Kept serialized so older saves keep
        /// their last computed value until migration re-derives it.
        /// </summary>
        public int age;
        public AgeBand ageBand;
        /// <summary>
        /// Pass V P0-2: the authoritative birth-date representation. Age is
        /// derived from this (completed calendar years via
        /// <see cref="AgeAuthority"/>), never stored independently.
        /// </summary>
        public BirthDate birthDate;
        /// <summary>
        /// Pass V P1-2: biological sex, needed by the demographic drivers
        /// (maternity, marriage pairing). Set at generation/birth; defaults false.
        /// </summary>
        public bool isFemale;
        /// <summary>
        /// Pass V P1-2: the person has migrated off-map. Off-map persons keep
        /// aging (birthday scheduling includes them); their record, kinship,
        /// ownership and debts are preserved.
        /// </summary>
        public bool isOffMap;
        public LaborAccessLevel laborAccessLevel;
        /// <summary>
        /// PKG-8 (3A-D14): legacy dual-write field. HouseholdMembershipRegistry is the sole
        /// household-membership authority; this pointer is kept for save compatibility and
        /// migration evidence only. Do not introduce new dual writes.
        /// </summary>
        public int householdId;
        public string professionId;
        public string professionName;
        /// <summary>
        /// PKG-6 (3A-D21): household-side wage MIRROR, not compensation authority. Agreed
        /// compensation lives only on EmploymentRelationship; this snapshot feeds household
        /// income summaries and must converge to the relationship terms, never override them.
        /// </summary>
        public WageSnapshot wage;
        public PersonHealthState health = new();
        /// <summary>
        /// NX-1B: nutrition derived from actual meal history (Tech X §2.9).
        /// Additive; defaults to fully nourished.
        /// </summary>
        public PersonNutritionState nutrition = new();
        public WorkerVisibleProfile visibleWorkerProfile = new();
        public WorkerHiddenTraits hiddenWorkerTraits = new();
        public WorkerTraitVisibility workerTraitVisibility = new();
        public WorkerApprenticeshipState apprenticeship = new();
        public int homeBuildingId;
        public int workplaceBuildingId;
        public PopulationScheduleState scheduleState;
        public int currentDestinationBuildingId;
        public NewcomerArrivalProfile arrivalProfile = NewcomerArrivalProfile.SettledResident;
        public SettlementArrangement settlementArrangement = SettlementArrangement.StableHousehold;
        public SettlementPressureState settlementPressure = SettlementPressureState.Create(
            SettlementArrangement.StableHousehold,
            1f,
            0.05f,
            0.88f,
            0,
            0.22f,
            "Stable household");
        public int startingCashCents;
        public float laborUrgency01 = 0.45f;
        public int laborReadinessModifier;
        public string preferredProfessionBias = string.Empty;
        public int settlementDifficulty;
        public int hostHouseholdId = -1;
        /// <summary>
        /// HF-3: day index of death, or -1 if living. The Person record and PersonId are
        /// retained after death — identity is never rewritten (Tech X §2.2).
        /// </summary>
        public int deathDayIndex = -1;

        public string DisplayName => $"{firstName} {lastName}";

        /// <summary>
        /// Pass V P0-2: true age in completed calendar years on the given
        /// Gregorian date, derived from <see cref="birthDate"/>. This is the
        /// only supported way to read a person's age.
        /// </summary>
        public int GetTrueAge(int currentYear, int currentMonth, int currentDay)
        {
            return AgeAuthority.CompletedYears(birthDate, currentYear, currentMonth, currentDay);
        }

        /// <summary>
        /// Pass V P0-2: refreshes the derived <see cref="age"/> cache and the
        /// age band from the authoritative birth date. Called after generation,
        /// after load/migration, and when a birthday due-event fires. Nothing
        /// else may write <see cref="age"/>.
        /// </summary>
        public void RefreshDerivedAge(int currentYear, int currentMonth, int currentDay)
        {
            age = GetTrueAge(currentYear, currentMonth, currentDay);
            ageBand = HouseholdLifecycleManager.AgeBandForAge(age);
            // Pass VI cohesion fix: labor access follows the age band, so sim-born children
            // gain real economic participation as they grow instead of keeping None forever.
            laborAccessLevel = HouseholdLifecycleManager.LaborAccessForAgeBand(ageBand);
        }

        public void InitializeWorkerTraits(System.Random random)
        {
            hiddenWorkerTraits = WorkerHiddenTraits.Generate(random, ageBand, laborAccessLevel);
            visibleWorkerProfile = WorkerVisibleProfile.Generate(
                hiddenWorkerTraits,
                age,
                ageBand,
                laborAccessLevel,
                wage,
                professionName,
                random);
            workerTraitVisibility = WorkerTraitVisibility.FirstPassDefault();
            EnsureSettlementStateInitialized();
        }

        public void EnsureWorkerTraitsInitialized(int seedSalt = 0)
        {
            EnsureSettlementStateInitialized();
            System.Random random = null;
            if (hiddenWorkerTraits == null || !hiddenWorkerTraits.IsInitialized)
            {
                random = new System.Random(BuildStableTraitSeed(seedSalt));
                hiddenWorkerTraits = WorkerHiddenTraits.Generate(random, ageBand, laborAccessLevel);
            }

            if (visibleWorkerProfile == null || !visibleWorkerProfile.IsInitialized)
            {
                random ??= new System.Random(BuildStableTraitSeed(seedSalt + 31));
                visibleWorkerProfile = WorkerVisibleProfile.Generate(
                    hiddenWorkerTraits,
                    age,
                    ageBand,
                    laborAccessLevel,
                    wage,
                    professionName,
                    random);
            }

            if (workerTraitVisibility == null || !workerTraitVisibility.IsInitialized)
            {
                workerTraitVisibility = WorkerTraitVisibility.FirstPassDefault();
            }

            EnsureApprenticeshipInitialized();
        }

        public void EnsureSettlementStateInitialized()
        {
            NewcomerSettlementEvaluator.EnsurePersonSettlementInitialized(this);
        }

        public void EnsureApprenticeshipInitialized()
        {
            if (apprenticeship == null || !apprenticeship.IsInitialized)
            {
                apprenticeship = WorkerApprenticeshipState.FirstPassDefault();
                return;
            }

            apprenticeship.EnsureInitialized();
        }

        public void RecordVisibleWorkerRole(string roleName, int wageCents)
        {
            EnsureWorkerTraitsInitialized();
            visibleWorkerProfile.RecordCurrentRole(roleName, wageCents);
        }

        public void RecordFormerVisibleWorkerRole(string roleName, int wageCents)
        {
            EnsureWorkerTraitsInitialized();
            visibleWorkerProfile.RecordFormerRole(roleName, wageCents);
        }

        private int BuildStableTraitSeed(int seedSalt)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + id;
                hash = hash * 31 + age;
                hash = hash * 31 + householdId;
                hash = hash * 31 + (firstName != null ? StableStringHash(firstName) : 0);
                hash = hash * 31 + (lastName != null ? StableStringHash(lastName) : 0);
                hash = hash * 31 + seedSalt;
                return hash;
            }
        }

        private static int StableStringHash(string value)
        {
            unchecked
            {
                int hash = 23;
                for (int i = 0; i < value.Length; i++)
                {
                    hash = hash * 31 + value[i];
                }

                return hash;
            }
        }
    }
}
