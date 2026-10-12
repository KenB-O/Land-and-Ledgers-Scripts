using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V P1-1/P1-5 (Worker 1): the data-only draft of a generated starting
    /// world. Produced by a generation port from (preset, seed, streams) and
    /// consumed by the economy seeder. Read-only input to seeding — the seeder
    /// may read ids and authored values but must not mutate the draft.
    /// </summary>
    [Serializable]
    public sealed class ScenarioSeedPopulation
    {
        public List<ScenarioSeedHousehold> Households = new List<ScenarioSeedHousehold>();
        public List<ScenarioSeedBusiness> Businesses = new List<ScenarioSeedBusiness>();
        public List<ScenarioSeedPropertyGrant> PropertyGrants = new List<ScenarioSeedPropertyGrant>();
        public int PlayerHouseholdId = -1;

        public int ResidentCount
        {
            get
            {
                int total = 0;
                foreach (ScenarioSeedHousehold household in Households)
                {
                    total += household?.Members?.Count ?? 0;
                }

                return total;
            }
        }
    }

    [Serializable]
    public sealed class ScenarioSeedHousehold
    {
        public int HouseholdId;
        public string Surname = string.Empty;
        public int HomeBuildingId = -1;
        public bool IsPlayerHousehold;
        public List<ScenarioSeedPerson> Members = new List<ScenarioSeedPerson>();
    }

    [Serializable]
    public sealed class ScenarioSeedPerson
    {
        public int PersonId;
        public int HouseholdId;
        public string FirstName = string.Empty;
        public string LastName = string.Empty;
        public int Age;
        public bool IsWorkingAge;
        public string ProfessionId = string.Empty;
        public string ProfessionName = string.Empty;
        public int WeeklyWageCents;
    }

    [Serializable]
    public sealed class ScenarioSeedBusiness
    {
        public string BusinessId = string.Empty;
        public string DisplayName = string.Empty;
        public string BusinessType = string.Empty;
        public int CapitalCents;
        public string OwnerKind = "npc";
        public int BuildingId = -1;
        public List<ScenarioInventoryGrant> InventoryGrants = new List<ScenarioInventoryGrant>();
    }

    [Serializable]
    public sealed class ScenarioInventoryGrant
    {
        public string ProductId = string.Empty;
        public float Quantity;
        public int UnitCostCents;

        public ScenarioInventoryGrant() { }

        public ScenarioInventoryGrant(string productId, float quantity, int unitCostCents)
        {
            ProductId = productId ?? string.Empty;
            Quantity = Math.Max(0f, quantity);
            UnitCostCents = Math.Max(0, unitCostCents);
        }
    }

    [Serializable]
    public sealed class ScenarioSeedPropertyGrant
    {
        public string GrantId = string.Empty;
        public int BuildingId = -1;
        public int HouseholdId = -1;
        public string Notes = string.Empty;
    }
}
