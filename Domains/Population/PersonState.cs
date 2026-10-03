using System;

namespace LandLedgers.Population
{
    [Serializable]
    public sealed class PersonState
    {
        public int id;
        public string firstName;
        public string lastName;
        public int age;
        public AgeBand ageBand;
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
