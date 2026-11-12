using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>One complete, reusable, development-only town startup dataset.</summary>
    [CreateAssetMenu(fileName = "DevelopmentTownConfiguration", menuName = "Land & Ledgers/Development/Town Configuration", order = 980)]
    public sealed class DevelopmentTownConfiguration : ScriptableObject
    {
        [Serializable]
        public sealed class PersonDefinition
        {
            public string key = string.Empty;
            public string firstName = string.Empty;
            public string lastName = string.Empty;
            [Min(18)] public int age = 30;
            public bool ageIsProvisionalCalibration;
            public bool isFemale;
            public string occupation = "Unemployed";
            [Tooltip("Ordinal of a real residential building sorted by TownWorld building ID.")]
            public int homeBuildingOrdinal;
        }

        [Serializable]
        public sealed class HouseholdDefinition
        {
            public string key = string.Empty;
            public string displayName = string.Empty;
            public string surname = string.Empty;
            public string[] memberPersonKeys = Array.Empty<string>();
            [Tooltip("Ordinal of a real residential building sorted by TownWorld building ID.")]
            public int homeBuildingOrdinal;
            [Min(0)] public int openingCashCents;
        }

        public enum RelationshipKind { Spouse, Sibling }

        [Serializable]
        public sealed class RelationshipDefinition
        {
            public string firstPersonKey = string.Empty;
            public string secondPersonKey = string.Empty;
            public RelationshipKind relationship;
        }

        [Serializable]
        public sealed class BusinessDefinition
        {
            public string key = string.Empty;
            [Tooltip("Existing runtime operation ID, such as general-store, bakery, fuel-dealer, or livery-freight. More than one business may use an operation.")]
            public string operationId = string.Empty;
            public string ownerPersonKey = string.Empty;
            public string businessId = string.Empty;
            public string premisesId = string.Empty;
            public bool premisesRequired = true;
            [Min(0)] public int openingCashCents;
            [Tooltip("Optional freight quote calibration for this business. Values are development data, not canonical prices.")]
            public FreightServiceRateCard freightRateCard;
        }

        [Serializable]
        public sealed class FreightServiceRateCard
        {
            [Min(0)] public int baseServiceFeeCents;
            [Min(0)] public int ratePerMileCents;
            [Tooltip("One-time development tariff: charge the expected round trip from pickup to delivery and back.")]
            public bool chargeRoundTrip = true;
        }

        [Serializable]
        public sealed class BusinessCashTarget
        {
            public string businessKey = string.Empty;
            [Min(0)] public int targetCashCents;
        }

        [Serializable]
        public sealed class HouseholdCashTarget
        {
            [Tooltip("Person key whose legitimate shared household ledger receives the allocation.")]
            public string anchorPersonKey = string.Empty;
            [Min(0)] public int targetCashCents;
        }

        [Serializable]
        public sealed class OpeningInventoryItem
        {
            public string itemId = string.Empty;
            [Min(0)] public int minimumUnits;
            [Min(0)] public int maximumUnits;
        }

        [Serializable]
        public sealed class BusinessOpeningInventory
        {
            public string businessKey = string.Empty;
            public OpeningInventoryItem[] items = Array.Empty<OpeningInventoryItem>();
        }

        /// <summary>
        /// Slice S2 (2026-11-05, plan 02 §4): one bake oven's component
        /// asset ids, in BakeOven definition order (oven-chamber,
        /// kneading-table, proofing-rack, bake-peels). The ids are
        /// scenario-authored; the kinds come from the definition.
        /// </summary>
        [Serializable]
        public sealed class OvenEquipment
        {
            public string[] componentAssetIds = Array.Empty<string>();
        }

        /// <summary>
        /// Slice S2: scenario-owned opening equipment for one business —
        /// the equipment analogue of BusinessOpeningInventory. Zero by
        /// default: a business with no entry here gets no equipment.
        /// </summary>
        [Serializable]
        public sealed class BusinessOpeningEquipment
        {
            public string businessKey = string.Empty;
            public OvenEquipment[] ovens = Array.Empty<OvenEquipment>();
            public string[] kitIds = Array.Empty<string>();
        }

        /// <summary>
        /// Plan 01 (2026-11-06): one scenario-authored wholesale supply
        /// agreement for a bakery seller — the account analogue of
        /// BusinessOpeningEquipment. Zero by default: a seller with no
        /// entry here gets no account from the config (the applicator
        /// books the demand-derived fallback only when the config lists
        /// no wholesale accounts at all). The units per delivery and
        /// delivery cadence are scenario-authored here; D-1 stays open
        /// until Kennedy sets standing terms.
        /// </summary>
        [Serializable]
        public sealed class BakeryWholesaleAccountDefinition
        {
            public string sellerBusinessKey = string.Empty;
            public string buyerBusinessKey = string.Empty;
            public string productId = string.Empty;
            [Min(0)] public int unitsPerDelivery;
            [Min(0)] public int deliveryCadenceDays = 1;
            [Min(0)] public int pricePerUnitCents;
            public BakeryWholesaleFreshness freshness = BakeryWholesaleFreshness.FreshOrDayOld;
            public int firstDeliveryDayIndex;
        }

        [SerializeField] private string configurationId = "millbrook-development-town";
        [SerializeField, Min(0), Tooltip("Deterministic development fixture calibration; not canonical world-generation policy.")]
        private int seedOffset = 9217;
        [SerializeField, Tooltip("Uses the standard deterministic population generator with the authored population settings below instead of the personally authored Small-town roster.")]
        private bool generatePopulationFromSettings;
        [SerializeField] private PopulationGenerationSettings generatedPopulationSettings;
        [SerializeField] private PersonDefinition[] people = Array.Empty<PersonDefinition>();
        [SerializeField] private HouseholdDefinition[] households = Array.Empty<HouseholdDefinition>();
        [SerializeField] private RelationshipDefinition[] relationships = Array.Empty<RelationshipDefinition>();
        [SerializeField] private BusinessDefinition[] businesses = Array.Empty<BusinessDefinition>();
        [SerializeField] private BusinessCashTarget[] businessCashTargets = Array.Empty<BusinessCashTarget>();
        [SerializeField] private HouseholdCashTarget[] householdCashTargets = Array.Empty<HouseholdCashTarget>();
        [SerializeField] private BusinessOpeningInventory[] openingInventory = Array.Empty<BusinessOpeningInventory>();
        [SerializeField] private BusinessOpeningEquipment[] openingEquipment = Array.Empty<BusinessOpeningEquipment>();
        [SerializeField] private BakeryWholesaleAccountDefinition[] wholesaleAccounts = Array.Empty<BakeryWholesaleAccountDefinition>();
        [SerializeField] private string freightBusinessKey = "freight-company";
        [SerializeField, Min(0), Tooltip("Finite development-town opening fuel endowment for the configured Bakery. This is scenario stock, applied once and never replenished automatically.")]
        private int openingBakeryFuelUnits;
        [SerializeField, Min(0), Tooltip("Finite development-town opening flour in pounds, received into the Bakery production bin on New Game only.")]
        private int openingBakeryFlourPounds;
        [SerializeField, Min(0), Tooltip("Finite development-town opening baker's notions in units, received into the Bakery production bin on New Game only.")]
        private int openingBakeryNotionsUnits;
        /// <summary>
        /// Plan 10 E4 (Obj 8; D-2): the owner-supplied fuel-dealer
        /// conversion factor — intake units per bakery fuel unit
        /// (IntakeUnitsPerBakeryUnit for Series2DealerBakeryFuelSource).
        /// Zero/unset by default: unset means "no dealer conversion —
        /// fuel cannot be supplied from the dealer". The value itself is
        /// Kennedy's (D-2); no asset edit by Muse, and the asset entry
        /// naming it is Codex's when he supplies it. A factor of 1 would
        /// be an invented conversion — never default to 1, and a factor
        /// of 0 is treated as unset everywhere it is read.
        /// </summary>
        [SerializeField, Min(0)] private int fuelDealerIntakeUnitsPerBakeryUnit;
        [SerializeField] private string openingHorseId = "A0";
        [SerializeField] private string openingWagonId = "opening-freight-wagon";
        [SerializeField, Tooltip("Overnight 2026-11-04: development-town mortality switch. FALSE by default, so the current development town runs with population turnover removed as a testing variable. A prevented death is logged once per person as a suppressed death (see MortalityDriver) and leaves the person, their employment, and their ownership intact. This is a development-town setting only: scenarios that do not use this configuration keep normal mortality.")]
        private bool enableMortality;
        [SerializeField, Tooltip("Overnight 2026-11-04: development-town fertility switch. FALSE by default, so the current development town starts no new pregnancies and delivers no births while disabled. Pregnancies already underway are deferred, not deleted, and deliver once fertility is re-enabled. This is a development-town setting only: scenarios that do not use this configuration keep normal fertility.")]
        private bool enableFertility;

        public string ConfigurationId => configurationId ?? string.Empty;
        public int SeedOffset => seedOffset;
        public bool GeneratePopulationFromSettings => generatePopulationFromSettings;
        public PopulationGenerationSettings GeneratedPopulationSettings => generatedPopulationSettings;
        public IReadOnlyList<PersonDefinition> People => people ?? Array.Empty<PersonDefinition>();
        public IReadOnlyList<HouseholdDefinition> Households => households ?? Array.Empty<HouseholdDefinition>();
        public IReadOnlyList<RelationshipDefinition> Relationships => relationships ?? Array.Empty<RelationshipDefinition>();
        public IReadOnlyList<BusinessDefinition> Businesses => businesses ?? Array.Empty<BusinessDefinition>();
        public IReadOnlyList<BusinessCashTarget> BusinessCashTargets => businessCashTargets ?? Array.Empty<BusinessCashTarget>();
        public IReadOnlyList<HouseholdCashTarget> HouseholdCashTargets => householdCashTargets ?? Array.Empty<HouseholdCashTarget>();
        public IReadOnlyList<BusinessOpeningInventory> OpeningInventory => openingInventory ?? Array.Empty<BusinessOpeningInventory>();
        public IReadOnlyList<BusinessOpeningEquipment> OpeningEquipment => openingEquipment ?? Array.Empty<BusinessOpeningEquipment>();
        public IReadOnlyList<BakeryWholesaleAccountDefinition> WholesaleAccounts => wholesaleAccounts ?? Array.Empty<BakeryWholesaleAccountDefinition>();
        public string FreightBusinessKey => freightBusinessKey ?? string.Empty;
        public int OpeningBakeryFuelUnits => openingBakeryFuelUnits;
        public int OpeningBakeryFlourPounds => openingBakeryFlourPounds;
        public int OpeningBakeryNotionsUnits => openingBakeryNotionsUnits;
        /// <summary>Plan 10 E4: the D-2 fuel-dealer conversion factor; 0 = unset (no dealer conversion).</summary>
        public int FuelDealerIntakeUnitsPerBakeryUnit => fuelDealerIntakeUnitsPerBakeryUnit;
        public string OpeningHorseId => openingHorseId ?? string.Empty;
        public string OpeningWagonId => openingWagonId ?? string.Empty;
        public bool EnableMortality => enableMortality;
        public bool EnableFertility => enableFertility;

        public bool TryGetPerson(string personKey, out PersonDefinition result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(personKey)) return false;
            foreach (PersonDefinition person in People)
                if (person != null && string.Equals(person.key, personKey.Trim(), StringComparison.OrdinalIgnoreCase))
                { result = person; return true; }
            return false;
        }

        public int ResolveRuntimePersonId(string personKey)
        {
            if (string.IsNullOrWhiteSpace(personKey)) return -1;
            if (generatePopulationFromSettings
                && personKey.StartsWith("generated-person-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(personKey.Substring("generated-person-".Length), out int generatedPersonId)
                && generatedPersonId >= 0)
                return generatedPersonId;
            int id = 1;
            foreach (HouseholdDefinition household in Households)
                foreach (string memberKey in household.memberPersonKeys ?? Array.Empty<string>())
                {
                    if (string.Equals(memberKey, personKey, StringComparison.OrdinalIgnoreCase)) return id;
                    id++;
                }
            return -1;
        }

        public bool TryGetBusiness(string businessKey, out BusinessDefinition result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(businessKey)) return false;
            foreach (BusinessDefinition candidate in Businesses)
                if (candidate != null && string.Equals(candidate.key, businessKey.Trim(), StringComparison.OrdinalIgnoreCase))
                { result = candidate; return true; }
            return false;
        }

        public bool TryResolveOpeningInventory(string businessKey, string itemId, int scenarioSeed, out int quantity)
        {
            quantity = 0;
            if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(businessKey)) return false;
            foreach (BusinessOpeningInventory entry in OpeningInventory)
            {
                if (entry == null || !string.Equals(entry.businessKey, businessKey, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (OpeningInventoryItem item in entry.items ?? Array.Empty<OpeningInventoryItem>())
                {
                    if (item == null || !string.Equals(item.itemId, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                    int min = Mathf.Max(0, item.minimumUnits);
                    int max = Mathf.Max(min, item.maximumUnits);
                    uint hash = 2166136261u ^ unchecked((uint)scenarioSeed);
                    string key = ConfigurationId + ":" + businessKey.Trim().ToLowerInvariant() + ":" + itemId.Trim().ToLowerInvariant();
                    for (int i = 0; i < key.Length; i++) { hash ^= key[i]; hash *= 16777619u; }
                    hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15;
                    quantity = min + (int)(hash % (uint)(max - min + 1));
                    return true;
                }
            }
            return false;
        }

        public bool Validate(out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(ConfigurationId)) { error = "Development town configuration needs a stable ID."; return false; }
            if (generatePopulationFromSettings)
            {
                if (generatedPopulationSettings == null)
                { error = "Generated development presets require PopulationGenerationSettings."; return false; }
                if (People.Count != 0 || Households.Count != 0)
                { error = "Generated development presets must not duplicate an authored person or household roster."; return false; }
                var generatedBusinessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var generatedPersonOwners = new HashSet<int>();
                foreach (BusinessDefinition business in Businesses)
                {
                    int ownerId = business != null ? ResolveRuntimePersonId(business.ownerPersonKey) : -1;
                    if (business == null || string.IsNullOrWhiteSpace(business.key) || string.IsNullOrWhiteSpace(business.operationId)
                        || ownerId <= 0 || ownerId > generatedPopulationSettings.maximumStartingResidents
                        || business.openingCashCents < 0 || !generatedBusinessKeys.Add(business.key)
                        || !generatedPersonOwners.Add(ownerId))
                    { error = "Generated opening businesses need unique keys and distinct owners within the generated starting population."; return false; }
                    if (business.freightRateCard != null
                        && (business.freightRateCard.baseServiceFeeCents < 0 || business.freightRateCard.ratePerMileCents < 0))
                    { error = $"Development business '{business.key}' has a freight rate card with a negative fee."; return false; }
                }
                var generatedCashBusinessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (BusinessCashTarget target in BusinessCashTargets)
                    if (target == null || !TryGetBusiness(target.businessKey, out _)
                        || target.targetCashCents < 0 || !generatedCashBusinessKeys.Add(target.businessKey))
                    { error = "Generated business cash targets must reference unique configured businesses and non-negative balances."; return false; }
                var generatedCashHouseholdKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (HouseholdCashTarget target in HouseholdCashTargets)
                {
                    int anchorId = target != null ? ResolveRuntimePersonId(target.anchorPersonKey) : -1;
                    if (target == null || anchorId < 0 || anchorId >= generatedPopulationSettings.maximumStartingResidents
                        || target.targetCashCents < 0 || !generatedCashHouseholdKeys.Add(target.anchorPersonKey))
                    { error = "Generated household cash targets must reference distinct starting residents and non-negative balances."; return false; }
                }
                if (Businesses.Count == 0 || !TryGetBusiness(FreightBusinessKey, out _)
                    || string.IsNullOrWhiteSpace(OpeningHorseId) || string.IsNullOrWhiteSpace(OpeningWagonId))
                { error = "Generated development presets must keep the configured freight business, horse, and wagon identities."; return false; }
                return true;
            }
            if (People.Count == 0 || Households.Count == 0 || Businesses.Count == 0) { error = "Development town must define people, households, and businesses directly."; return false; }
            var personKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PersonDefinition person in People)
                if (person == null || string.IsNullOrWhiteSpace(person.key) || string.IsNullOrWhiteSpace(person.firstName)
                    || string.IsNullOrWhiteSpace(person.lastName) || person.age < 18 || person.homeBuildingOrdinal < 0
                    || !personKeys.Add(person.key)) { error = "Development people need unique keys, names, adult ages, and valid home ordinals."; return false; }
            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var householdKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var homeOrdinals = new HashSet<int>();
            foreach (HouseholdDefinition household in Households)
            {
                if (household == null || string.IsNullOrWhiteSpace(household.key) || string.IsNullOrWhiteSpace(household.surname)
                    || household.homeBuildingOrdinal < 0 || household.openingCashCents < 0 || !householdKeys.Add(household.key)
                    || !homeOrdinals.Add(household.homeBuildingOrdinal) || household.memberPersonKeys == null || household.memberPersonKeys.Length == 0)
                { error = "Development households need unique keys, distinct home ordinals, non-negative opening cash, and members."; return false; }
                foreach (string memberKey in household.memberPersonKeys)
                    if (!personKeys.Contains(memberKey) || !assigned.Add(memberKey)) { error = "Each development resident must reference exactly one configured household."; return false; }
            }
            if (assigned.Count != personKeys.Count) { error = "Every configured resident must belong to a household."; return false; }
            foreach (RelationshipDefinition relation in Relationships)
                if (relation == null || !personKeys.Contains(relation.firstPersonKey) || !personKeys.Contains(relation.secondPersonKey)
                    || string.Equals(relation.firstPersonKey, relation.secondPersonKey, StringComparison.OrdinalIgnoreCase))
                { error = "Development relationships must reference two distinct configured residents."; return false; }
            var businessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BusinessDefinition business in Businesses)
            {
                if (business == null || string.IsNullOrWhiteSpace(business.key) || string.IsNullOrWhiteSpace(business.operationId)
                    || !personKeys.Contains(business.ownerPersonKey) || business.openingCashCents < 0 || !businessKeys.Add(business.key))
                { error = "Development businesses need unique keys, an operation ID, a configured owner, and non-negative opening cash."; return false; }
                if (business.freightRateCard != null
                    && (business.freightRateCard.baseServiceFeeCents < 0 || business.freightRateCard.ratePerMileCents < 0))
                { error = $"Development business '{business.key}' has a freight rate card with a negative fee."; return false; }
            }
            if (!TryGetBusiness(FreightBusinessKey, out _) || string.IsNullOrWhiteSpace(OpeningHorseId) || string.IsNullOrWhiteSpace(OpeningWagonId))
            { error = "Development transport startup must identify its configured freight business, horse, and wagon."; return false; }
            var cashBusinessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BusinessCashTarget target in BusinessCashTargets)
                if (target == null || !TryGetBusiness(target.businessKey, out _) || target.targetCashCents < 0 || !cashBusinessKeys.Add(target.businessKey))
                { error = "Business cash targets must reference unique configured businesses and non-negative balances."; return false; }
            var cashHouseholdKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HouseholdCashTarget target in HouseholdCashTargets)
                if (target == null || !TryGetPerson(target.anchorPersonKey, out _) || target.targetCashCents < 0 || !cashHouseholdKeys.Add(target.anchorPersonKey))
                { error = "Household cash targets must reference unique configured residents and non-negative balances."; return false; }
            var inventoryBusinessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BusinessOpeningInventory entry in OpeningInventory)
            {
                if (entry == null || !TryGetBusiness(entry.businessKey, out _) || !inventoryBusinessKeys.Add(entry.businessKey))
                { error = "Opening inventory entries must reference unique configured businesses."; return false; }
                var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (OpeningInventoryItem item in entry.items ?? Array.Empty<OpeningInventoryItem>())
                    if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.minimumUnits < 0 || item.maximumUnits < item.minimumUnits || !items.Add(item.itemId))
                    { error = $"Opening inventory for '{entry.businessKey}' contains an invalid or duplicate item range."; return false; }
            }
            // Slice S2: opening equipment is optional and zero by default.
            // When present, every entry must name a configured business, every
            // kit id must resolve in the ToolKitCatalog, and every oven entry
            // must list exactly the BakeOven definition's component count.
            foreach (BusinessOpeningEquipment entry in OpeningEquipment)
            {
                if (entry == null || !TryGetBusiness(entry.businessKey, out _))
                { error = "Opening equipment entries must reference configured businesses."; return false; }
                foreach (string kitId in entry.kitIds ?? Array.Empty<string>())
                    if (string.IsNullOrWhiteSpace(kitId) || ToolKitCatalog.Get(kitId.Trim()) == null)
                    { error = $"Opening equipment for '{entry.businessKey}' names an unknown tool kit '{kitId}'."; return false; }
                foreach (OvenEquipment oven in entry.ovens ?? Array.Empty<OvenEquipment>())
                    if (oven == null || oven.componentAssetIds == null
                        || oven.componentAssetIds.Length != WorkstationCatalog.BakeOven.Components.Count)
                    { error = $"Opening equipment for '{entry.businessKey}' lists an oven whose component count must equal the BakeOven definition ({WorkstationCatalog.BakeOven.Components.Count})."; return false; }
            }
            return true;
        }

    }
}
