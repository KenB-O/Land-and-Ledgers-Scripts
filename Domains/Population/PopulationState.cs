using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Population
{
    [Serializable]
    public sealed class PopulationState
    {
        private readonly SequentialIdAllocator personIdAllocator = new();
        private readonly SequentialIdAllocator householdIdAllocator = new();

        public int NextPersonId => personIdAllocator.NextId;
        public int NextHouseholdId => householdIdAllocator.NextId;

        public int AllocateNextPersonId() => personIdAllocator.AllocateNext();
        public int AllocateNextHouseholdId() => householdIdAllocator.AllocateNext();

        public void RestorePersonIdAllocator(int nextPersonId)
        {
            personIdAllocator.RestoreExact(nextPersonId);
        }

        public void RestoreHouseholdIdAllocator(int nextHouseholdId)
        {
            householdIdAllocator.RestoreExact(nextHouseholdId);
        }

        public List<PersonState> people = new();
        public List<HouseholdState> households = new();
        public List<string> validationMessages = new();
        public List<RentalPropertyState> rentalProperties = new();
        public List<RentalApplicantState> rentalApplicants = new();
        public int boardingCapacity;
        public int boardingUsed;
        public int boardingHouseCapacity;
        public int boardingHouseUsed;
        public int renterHouseholdCount;
        public int kinPlacementCount;
        public int transientPersonCount;
        public int unstableHouseholdCount;
        public int departedUnsettledCount;
        public int rentalVacancyCount;
        public int playerOwnedRentalVacancyCount;
        public int rentalApplicantCount;
        public int strongerHousingNeedCount;
        public int lastWeeklySettlementMoveCount;
        public int lastWeeklyPlayerOwnedRentalMoveCount;
        public int lastWeeklyPlayerOwnedBoardingMoveCount;
        public string lastWeeklySettlementActionSummary = string.Empty;
        public float housingPressure01;
        public float rentalPressure01;
        public float laborAbsorption01 = 1f;
        public string lastSettlementSummary = string.Empty;
        public string lastRentalSummary = string.Empty;
        public SettlementPressureSnapshot settlementPressureSnapshot = SettlementPressureSnapshot.Empty();
        public PopulationValidationSnapshot populationValidationSnapshot = PopulationValidationSnapshot.Clean();
        public TownReservePressureSnapshot townReservePressureSnapshot = TownReservePressureSnapshot.Empty();
        public string lastPopulationValidationSummary = string.Empty;
        public string lastTownReserveSummary = string.Empty;

        public void Clear()
        {
            people ??= new List<PersonState>();
            households ??= new List<HouseholdState>();
            validationMessages ??= new List<string>();
            rentalProperties ??= new List<RentalPropertyState>();
            rentalApplicants ??= new List<RentalApplicantState>();
            people.Clear();
            households.Clear();
            personIdAllocator.RestoreExact(0);
            householdIdAllocator.RestoreExact(0);
            validationMessages.Clear();
            rentalProperties.Clear();
            rentalApplicants.Clear();
            boardingCapacity = 0;
            boardingUsed = 0;
            boardingHouseCapacity = 0;
            boardingHouseUsed = 0;
            renterHouseholdCount = 0;
            kinPlacementCount = 0;
            transientPersonCount = 0;
            unstableHouseholdCount = 0;
            departedUnsettledCount = 0;
            rentalVacancyCount = 0;
            playerOwnedRentalVacancyCount = 0;
            rentalApplicantCount = 0;
            strongerHousingNeedCount = 0;
            lastWeeklySettlementMoveCount = 0;
            lastWeeklyPlayerOwnedRentalMoveCount = 0;
            lastWeeklyPlayerOwnedBoardingMoveCount = 0;
            lastWeeklySettlementActionSummary = string.Empty;
            housingPressure01 = 0f;
            rentalPressure01 = 0f;
            laborAbsorption01 = 1f;
            lastSettlementSummary = string.Empty;
            lastRentalSummary = string.Empty;
            settlementPressureSnapshot = SettlementPressureSnapshot.Empty();
            populationValidationSnapshot = PopulationValidationSnapshot.Clean();
            townReservePressureSnapshot = TownReservePressureSnapshot.Empty();
            lastPopulationValidationSummary = string.Empty;
            lastTownReserveSummary = string.Empty;
        }

        public PersonState GetPerson(int personId)
        {
            people ??= new List<PersonState>();
            for (int i = 0; i < people.Count; i++)
            {
                PersonState person = people[i];
                if (person != null && person.id == personId)
                {
                    return person;
                }
            }

            return null;
        }

        public HouseholdState GetHousehold(int householdId)
        {
            households ??= new List<HouseholdState>();
            for (int i = 0; i < households.Count; i++)
            {
                HouseholdState household = households[i];
                if (household != null && household.id == householdId)
                {
                    return household;
                }
            }

            return null;
        }

        public void EnsureSettlementMetricsInitialized()
        {
            people ??= new List<PersonState>();
            households ??= new List<HouseholdState>();
            validationMessages ??= new List<string>();
            rentalProperties ??= new List<RentalPropertyState>();
            rentalApplicants ??= new List<RentalApplicantState>();
            boardingCapacity = Math.Max(0, boardingCapacity);
            boardingUsed = Math.Max(0, boardingUsed);
            boardingHouseCapacity = Math.Max(0, boardingHouseCapacity);
            boardingHouseUsed = Math.Max(0, boardingHouseUsed);
            renterHouseholdCount = Math.Max(0, renterHouseholdCount);
            kinPlacementCount = Math.Max(0, kinPlacementCount);
            transientPersonCount = Math.Max(0, transientPersonCount);
            unstableHouseholdCount = Math.Max(0, unstableHouseholdCount);
            departedUnsettledCount = Math.Max(0, departedUnsettledCount);
            rentalVacancyCount = Math.Max(0, rentalVacancyCount);
            playerOwnedRentalVacancyCount = Math.Max(0, playerOwnedRentalVacancyCount);
            rentalApplicantCount = Math.Max(0, rentalApplicantCount);
            strongerHousingNeedCount = Math.Max(0, strongerHousingNeedCount);
            lastWeeklySettlementMoveCount = Math.Max(0, lastWeeklySettlementMoveCount);
            lastWeeklyPlayerOwnedRentalMoveCount = Math.Max(0, lastWeeklyPlayerOwnedRentalMoveCount);
            lastWeeklyPlayerOwnedBoardingMoveCount = Math.Max(0, lastWeeklyPlayerOwnedBoardingMoveCount);
            lastWeeklySettlementActionSummary ??= string.Empty;
            housingPressure01 = Math.Min(1f, Math.Max(0f, housingPressure01));
            rentalPressure01 = Math.Min(1f, Math.Max(0f, rentalPressure01));
            laborAbsorption01 = Math.Min(1f, Math.Max(0f, laborAbsorption01));
            lastSettlementSummary ??= string.Empty;
            lastRentalSummary ??= string.Empty;
            lastPopulationValidationSummary ??= string.Empty;
            lastTownReserveSummary ??= string.Empty;
            if (string.IsNullOrWhiteSpace(settlementPressureSnapshot.headline))
            {
                settlementPressureSnapshot = SettlementPressureSnapshot.Empty();
            }
            if (string.IsNullOrWhiteSpace(populationValidationSnapshot.headline))
            {
                populationValidationSnapshot = PopulationValidationSnapshot.Clean();
            }
            if (string.IsNullOrWhiteSpace(townReservePressureSnapshot.headline))
            {
                townReservePressureSnapshot = TownReservePressureSnapshot.Empty();
            }

            for (int i = rentalProperties.Count - 1; i >= 0; i--)
            {
                if (rentalProperties[i] == null)
                {
                    rentalProperties.RemoveAt(i);
                    continue;
                }

                rentalProperties[i].Sanitize();
            }

            for (int i = rentalApplicants.Count - 1; i >= 0; i--)
            {
                if (rentalApplicants[i] == null)
                {
                    rentalApplicants.RemoveAt(i);
                    continue;
                }

                rentalApplicants[i].Sanitize();
            }
        }
    }
}
