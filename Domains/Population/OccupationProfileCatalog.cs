using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Data-driven occupation ontology for the first settlement layer. Historical
    /// titles resolve to these profiles plus an assignment/specialization; they do
    /// not create parallel occupations or class gates.
    /// </summary>
    public sealed class OccupationProfile
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int Tier { get; }
        public IReadOnlyList<string> TaskFamilies { get; }
        public IReadOnlyList<string> CapabilityFamilies { get; }
        public IReadOnlyList<string> EquipmentRequirements { get; }
        public IReadOnlyList<string> WorkplaceTypes { get; }
        public IReadOnlyList<string> HistoricalTitles { get; }
        public float BaselineWorkerWeight { get; }

        public OccupationProfile(
            string id,
            string displayName,
            int tier,
            float baselineWorkerWeight,
            IEnumerable<string> taskFamilies,
            IEnumerable<string> capabilityFamilies,
            IEnumerable<string> equipmentRequirements,
            IEnumerable<string> workplaceTypes,
            IEnumerable<string> historicalTitles)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Tier = Math.Max(1, tier);
            BaselineWorkerWeight = Math.Max(0f, baselineWorkerWeight);
            TaskFamilies = Copy(taskFamilies);
            CapabilityFamilies = Copy(capabilityFamilies);
            EquipmentRequirements = Copy(equipmentRequirements);
            WorkplaceTypes = Copy(workplaceTypes);
            HistoricalTitles = Copy(historicalTitles);
        }

        private static IReadOnlyList<string> Copy(IEnumerable<string> values)
        {
            return new List<string>(values ?? Array.Empty<string>());
        }
    }

    public static class OccupationProfileCatalog
    {
        private static readonly IReadOnlyList<OccupationProfile> tierOne = BuildTierOne();
        private static readonly Dictionary<string, OccupationProfile> byKey = BuildIndex(tierOne);

        public static IReadOnlyList<OccupationProfile> TierOne => tierOne;

        public static OccupationProfile Find(string idOrTitle)
        {
            if (string.IsNullOrWhiteSpace(idOrTitle)) return null;
            byKey.TryGetValue(Normalize(idOrTitle), out OccupationProfile profile);
            return profile;
        }

        public static OccupationProfile Resolve(PersonState person)
        {
            if (person == null) return null;
            return Find(person.professionId) ?? Find(person.professionName);
        }

        private static Dictionary<string, OccupationProfile> BuildIndex(IReadOnlyList<OccupationProfile> profiles)
        {
            var index = new Dictionary<string, OccupationProfile>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < profiles.Count; i++)
            {
                OccupationProfile profile = profiles[i];
                Add(index, profile.Id, profile);
                Add(index, profile.DisplayName, profile);
                for (int j = 0; j < profile.HistoricalTitles.Count; j++)
                    Add(index, profile.HistoricalTitles[j], profile);
            }
            return index;
        }

        private static void Add(Dictionary<string, OccupationProfile> index, string value, OccupationProfile profile)
        {
            string key = Normalize(value);
            if (!string.IsNullOrWhiteSpace(key) && !index.ContainsKey(key)) index.Add(key, profile);
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', ' ').Replace('/', ' ');
        }

        private static IReadOnlyList<OccupationProfile> BuildTierOne()
        {
            return new List<OccupationProfile>
            {
                Profile("farmer", "Farmer", 1, 0.40120f,
                    new[] { "planting", "animal care", "harvest", "farm management" },
                    new[] { "Agricultural", "Animal Handling", "Organization / Management" },
                    new[] { "land", "farm implements", "storage" }, new[] { "farm", "rural holding" },
                    new[] { "Dairy Farmer", "Wheat Farmer", "Tenant Farmer" }),
                Profile("farm_hand", "Farm Hand", 1, 0.19500f,
                    new[] { "field work", "feeding", "milking", "harvest" },
                    new[] { "Agricultural", "Animal Handling", "Physical Work" },
                    new[] { "farm tools" }, new[] { "farm", "rural holding" },
                    new[] { "Harvest Hand", "Hay Hand", "Seasonal Farm Laborer" }),
                Profile("laborer", "Laborer", 1, 0.03819f,
                    new[] { "loading", "excavation", "road work", "construction support" },
                    new[] { "Physical Work", "Transport" }, new[] { "hand tools" },
                    new[] { "yard", "road", "construction site" }, new[] { "Common Laborer" }),
                Profile("teamster", "Teamster", 1, 0.02763f,
                    new[] { "loading", "driving", "animal handling", "route travel", "unloading" },
                    new[] { "Transport", "Animal Handling", "Mechanical / Repair" },
                    new[] { "wagon", "draft team" }, new[] { "route", "yard", "stable" },
                    new[] { "Drayman", "Freight Driver", "Deliveryman" }),
                Profile("blacksmith_farrier", "Blacksmith / Farrier", 1, 0.00976f,
                    new[] { "forge work", "repair", "horseshoeing", "material handling" },
                    new[] { "Mechanical / Repair", "Construction" }, new[] { "forge", "anvil", "iron stock" },
                    new[] { "smithy", "workshop" }, new[] { "Railroad Blacksmith", "Military Blacksmith" }),
                Profile("carpenter", "Carpenter", 1, 0.01952f,
                    new[] { "framing", "finish carpentry", "roofing", "bridge work" },
                    new[] { "Construction", "Mechanical / Repair" }, new[] { "saw", "carpentry tools", "lumber" },
                    new[] { "building site", "workshop" }, new[] { "Railroad Carpenter", "Mine Carpenter" }),
                Profile("wagonmaker_wheelwright", "Wagonmaker / Wheelwright", 1, 0.00319f,
                    new[] { "wagon building", "wheel repair", "fitting" },
                    new[] { "Mechanical / Repair", "Construction" }, new[] { "wheel tools", "timber", "iron" },
                    new[] { "wagon shop", "workshop" }, new[] { "Wheelwright", "Carriage Maker" }),
                Profile("saddler_harness_maker", "Saddler / Harness Maker", 1, 0.00319f,
                    new[] { "saddlery", "harness repair", "leather work" },
                    new[] { "Mechanical / Repair", "Commercial Interaction" }, new[] { "leather", "sewing tools" },
                    new[] { "saddlery", "workshop" }, new[] { "Saddler", "Harness Maker" }),
                Profile("dressmaker_seamstress", "Dressmaker / Seamstress", 1, 0.01153f,
                    new[] { "cutting", "fitting", "sewing", "alteration" },
                    new[] { "Construction", "Commercial Interaction" }, new[] { "sewing tools", "cloth" },
                    new[] { "home workshop", "dressmaking shop" }, new[] { "Dressmaker", "Seamstress", "Milliner" }),
                Profile("merchant", "Merchant", 1, 0.02438f,
                    new[] { "buying", "pricing", "customer service", "inventory", "credit", "records" },
                    new[] { "Commercial Interaction", "Numerical / Clerical", "Organization / Management" },
                    new[] { "stock", "ledger", "premises" }, new[] { "storefront", "office", "yard" },
                    new[] { "Hardware Merchant", "Grocer", "Lumber Dealer", "Feed Merchant" }),
                Profile("hospitality_keeper", "Hospitality Keeper", 1, 0.00700f,
                    new[] { "guest service", "rooms", "meals", "supplies", "account management" },
                    new[] { "Commercial Interaction", "Organization / Management", "Food Processing" },
                    new[] { "lodging space", "household supplies" }, new[] { "hotel", "boarding house", "eating house" },
                    new[] { "Hotel Keeper", "Innkeeper", "Boarding-House Keeper", "Eating-House Keeper" }),
                Profile("teacher", "Teacher", 1, 0.03553f,
                    new[] { "instruction", "attendance", "records", "preparation" },
                    new[] { "Organization / Management", "Numerical / Clerical" }, new[] { "schoolhouse", "books" },
                    new[] { "school", "home school" }, new[] { "Schoolteacher", "Teacher" })
            };
        }

        private static OccupationProfile Profile(string id, string name, int tier, float weight,
            string[] tasks, string[] capabilities, string[] equipment, string[] workplaces, string[] titles)
        {
            return new OccupationProfile(id, name, tier, weight, tasks, capabilities, equipment, workplaces, titles);
        }
    }
}
