using System;
using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Population
{
    /// <summary>
    /// QA/player-inspection read model derived from the real population and business
    /// authorities. Rural and reachable counts are supplied by the route/market
    /// authority; this class never invents them from town population or spawns firms.
    /// </summary>
    public sealed class SettlementEconomyReadModel
    {
        public string SettlementName { get; }
        public int BuiltUpResidents { get; }
        public int RuralServicePopulation { get; }
        public int EffectiveMarketPopulation { get; }
        public int TemporaryPresentPopulation { get; }
        public int PersonCount { get; }
        public int AdultCount { get; }
        public int EconomicallyActivePersons { get; }
        public int WageEmployees { get; }
        public int ProprietorOrOperatorCount { get; }
        public int FamilyEnterpriseWorkers { get; }
        public int HouseholdCount { get; }
        public int OccupiedAccommodationCount { get; }
        public int OwnerOccupiedHouseholds { get; }
        public int RentingHouseholds { get; }
        public int BoardingHouseholds { get; }
        public int BusinessCount { get; }
        public int OperatingBusinessCount { get; }

        private SettlementEconomyReadModel(
            string settlementName,
            int builtUpResidents,
            int ruralServicePopulation,
            int effectiveMarketPopulation,
            int temporaryPresentPopulation,
            int personCount,
            int adultCount,
            int economicallyActivePersons,
            int wageEmployees,
            int proprietorOrOperatorCount,
            int familyEnterpriseWorkers,
            int householdCount,
            int occupiedAccommodationCount,
            int ownerOccupiedHouseholds,
            int rentingHouseholds,
            int boardingHouseholds,
            int businessCount,
            int operatingBusinessCount)
        {
            SettlementName = settlementName ?? string.Empty;
            BuiltUpResidents = builtUpResidents;
            RuralServicePopulation = ruralServicePopulation;
            EffectiveMarketPopulation = effectiveMarketPopulation;
            TemporaryPresentPopulation = temporaryPresentPopulation;
            PersonCount = personCount;
            AdultCount = adultCount;
            EconomicallyActivePersons = economicallyActivePersons;
            WageEmployees = wageEmployees;
            ProprietorOrOperatorCount = proprietorOrOperatorCount;
            FamilyEnterpriseWorkers = familyEnterpriseWorkers;
            HouseholdCount = householdCount;
            OccupiedAccommodationCount = occupiedAccommodationCount;
            OwnerOccupiedHouseholds = ownerOccupiedHouseholds;
            RentingHouseholds = rentingHouseholds;
            BoardingHouseholds = boardingHouseholds;
            BusinessCount = businessCount;
            OperatingBusinessCount = operatingBusinessCount;
        }

        public static SettlementEconomyReadModel Build(
            string settlementName,
            PopulationState population,
            IReadOnlyCollection<int> ruralServicePersonIds,
            IReadOnlyCollection<int> reachableMarketPersonIds,
            IReadOnlyCollection<int> temporaryPersonIds,
            IReadOnlyList<BusinessInstanceState> businesses,
            EmploymentRelationshipRegistry employments)
        {
            population ??= new PopulationState();
            var rural = new HashSet<int>(ruralServicePersonIds ?? Array.Empty<int>());
            var reachable = new HashSet<int>(reachableMarketPersonIds ?? Array.Empty<int>());
            var temporary = new HashSet<int>(temporaryPersonIds ?? Array.Empty<int>());
            var occupiedBuildings = new HashSet<int>();
            int builtUp = 0;
            int adults = 0;
            int active = 0;
            var proprietorIds = new HashSet<int>();

            foreach (PersonState person in population.people ?? new List<PersonState>())
            {
                if (person == null || person.deathDayIndex >= 0) continue;
                if (person.homeBuildingId >= 0 && !rural.Contains(person.id)) builtUp++;
                if (person.ageBand == AgeBand.Adult18Plus) adults++;
                if (person.laborAccessLevel != LaborAccessLevel.None) active++;
                if (person.homeBuildingId >= 0) occupiedBuildings.Add(person.homeBuildingId);
            }

            int ownerOccupied = 0;
            int renting = 0;
            int boarding = 0;
            foreach (HouseholdState household in population.households ?? new List<HouseholdState>())
            {
                if (household == null) continue;
                if (household.homeBuildingId >= 0) occupiedBuildings.Add(household.homeBuildingId);
                if (household.dwellingKind == HouseholdDwellingKind.RentedHomeOrRoom
                    || household.isRenterHousehold) renting++;
                else if (household.hostsBoarders || (household.boarderPersonIds != null && household.boarderPersonIds.Count > 0)) boarding++;
                else ownerOccupied++;
            }

            int businessCount = 0;
            int operatingBusinesses = 0;
            int wageEmployees = 0;
            foreach (BusinessInstanceState business in businesses ?? Array.Empty<BusinessInstanceState>())
            {
                if (business == null) continue;
                businessCount++;
                if (business.OperatingEfficiency01 > 0f) operatingBusinesses++;
                if (business.Owner != null && business.Owner.OwnerKind == BusinessOwnerKind.Npc
                    && business.Owner.PersonId >= 0)
                    proprietorIds.Add(business.Owner.PersonId);
                if (employments != null)
                    wageEmployees += employments.GetByEmployer(business.InstanceId).Count;
            }

            return new SettlementEconomyReadModel(
                settlementName,
                builtUp,
                rural.Count,
                reachable.Count,
                temporary.Count,
                population.people?.Count ?? 0,
                adults,
                active,
                wageEmployees,
                proprietorIds.Count,
                0,
                population.households?.Count ?? 0,
                occupiedBuildings.Count,
                ownerOccupied,
                renting,
                boarding,
                businessCount,
                operatingBusinesses);
        }
    }
}
