using System;
using System.Collections.Generic;
using System.Linq;
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
            [Min(1), Tooltip("Stable runtime identity. Once authored, this is not derived from household list order.")]
            public int runtimePersonId;
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
            [Min(1), Tooltip("Stable runtime identity. Once authored, this is not derived from household list order.")]
            public int runtimeHouseholdId;
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
            public string displayName = string.Empty;
            [Tooltip("Existing runtime operation ID, such as general-store, bakery, fuel-dealer, or livery-freight. More than one business may use an operation.")]
            public string operationId = string.Empty;
            public string ownerPersonKey = string.Empty;
            public string businessId = string.Empty;
            public string premisesId = string.Empty;
            public bool premisesRequired = true;
            [Min(0)] public int openingCashCents;
            [Tooltip("Optional freight quote calibration for this business. Values are development data, not canonical prices.")]
            public FreightServiceRateCard freightRateCard;
            [Header("Business opening resources and agreements")]
            public BusinessOpeningConfiguration opening = new();
        }

        [Serializable]
        public sealed class GeneralSettings
        {
            public string configurationId = "millbrook-development-town";
            [Min(0)] public int seedOffset = 9217;
            public bool generatePopulationFromSettings;
            public PopulationGenerationSettings generatedPopulationSettings;
            [Tooltip("Opening balances for generated households whose memberships are created from the population-generation asset.")]
            public HouseholdCashTarget[] generatedHouseholdOpeningCash = Array.Empty<HouseholdCashTarget>();
            public string freightBusinessKey = "freight-company";
            public bool enableMortality;
            public bool enableFertility;
        }

        /// <summary>Finite New Game resources and agreements owned by one configured business.</summary>
        [Serializable]
        public sealed class BusinessOpeningConfiguration
        {
            [Header("Inventory and equipment")]
            public OpeningInventoryItem[] inventory = Array.Empty<OpeningInventoryItem>();
            public BusinessOpeningEquipment equipment = new();
            [Header("Employees and production supplies")]
            public BusinessEmployeeDefinition[] employees = Array.Empty<BusinessEmployeeDefinition>();
            public BusinessSpareBladeDefinition[] spareBlades = Array.Empty<BusinessSpareBladeDefinition>();
            [Min(0)] public int loggingCampProvisionCoverDays;
            [Min(0)] public int bakeryFuelUnits;
            [Min(0)] public int bakeryFlourPounds;
            [Min(0)] public int bakeryNotionsUnits;
            [Min(0)] public int carpentryLumberUnits;
            [Min(0)] public int fuelDealerIntakeUnitsPerBakeryUnit;
            [Header("Transport and commercial agreements")]
            public string openingHorseId = string.Empty;
            public string openingWagonId = string.Empty;
            public BakeryWholesaleAccountDefinition[] wholesaleAgreements = Array.Empty<BakeryWholesaleAccountDefinition>();
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
            [NonSerialized] public string businessKey;
            public OvenEquipment[] ovens = Array.Empty<OvenEquipment>();
            public string[] kitIds = Array.Empty<string>();
            /// <summary>
            /// A2 plan 27 (P10 live felling driver): one scenario-authored
            /// opening asset per entry (asset id + equipment kind). The
            /// mill endowment installer registers each listed asset into
            /// the business equipment register. Zero by default: a
            /// business with no asset entries gets no assets. Asset ids
            /// are Kennedy-authored; the slice fixture id
            /// "slice-felling-axe" is refused in Validate.
            /// </summary>
            public OpeningAssetEntry[] assetEntries = Array.Empty<OpeningAssetEntry>();
        }

        /// <summary>
        /// A2 plan 27 (P10): one scenario-authored opening asset — an
        /// equipment asset id plus its equipment kind (for example
        /// "felling-axe"). The kind must match the equipment-gate code
        /// the asset is meant to satisfy.
        /// </summary>
        [Serializable]
        public sealed class OpeningAssetEntry
        {
            public string assetId = string.Empty;
            public string kind = string.Empty;
        }

        /// <summary>
        /// C-1 (expanded campaign, Phase 1 forestry): one scenario-authored
        /// opening employment — a named person employed by a named business
        /// from New Game, with a role and an agreed weekly wage. Zero by
        /// default: a business with no entry here hires nobody at opening.
        /// The employment is a real EmploymentRelationship in the hub-owned
        /// registry (see ScenarioOpeningEmploymentEndowment), so the business
        /// pays the agreed wage through the normal payroll path. The wage is
        /// provisional scenario calibration, not canon.
        /// </summary>
        [Serializable]
        public sealed class BusinessEmployeeDefinition
        {
            [NonSerialized] public string businessKey;
            public string personKey = string.Empty;
            public string role = string.Empty;
            [Min(0)] public int weeklyWageCents;
        }

        /// <summary>
        /// C-5 (expanded campaign, Phase 1 forestry): one scenario-authored
        /// opening spare saw blade — a blade id for a named business, received
        /// once at New Game into the mill's blade inventory with BOOTSTRAP
        /// provenance (see ScenarioMillOpeningSpareBladeEndowment). Zero by
        /// default: a business with no entry here stocks no spare. One spare
        /// is the natural number for a one-saw-line mill; Canon Part V says
        /// the filer keeps "replacement saws". Blade ids are Kennedy-authored.
        /// </summary>
        [Serializable]
        public sealed class BusinessSpareBladeDefinition
        {
            [NonSerialized] public string businessKey;
            public string bladeId = string.Empty;
        }

        /// <summary>
        /// A2 plan 27 (P10 live felling driver): one scenario-authored
        /// timber stand — the felling source the bootstrapper registers
        /// into the stand registry and grants timber rights over. Zero
        /// by default: a configuration with no stands registers none,
        /// and the felling driver refuses loudly. Stand entries are
        /// Kennedy-authored; no fixture stand is copied into production.
        /// </summary>
        [Serializable]
        public sealed class TimberStandEntry
        {
            public string standId = string.Empty;
            public string displayName = string.Empty;
            public string locationId = string.Empty;
            public string species = string.Empty;
            [Min(0)] public float kilogramsPerLog;
            [Min(0)] public int standingTimberUnits;
            // Plan 28 (note 113 C4): authored road miles from this stand's
            // landing to the mill; 0 = none authored, and the haul driver
            // refuses loudly until the authoring lands.
            [Min(0)] public float milesToMill;
            // DEF-072 (Decision 15): the mature stock this site can hold.
            // 0 = unset, and the registry defaults it to
            // standingTimberUnits at registration (an authored stand
            // starts at its capacity).
            [Min(0)] public int carryingCapacityUnits;
            // DEF-072 (Decision 15): 0..1 environmental conditions for
            // this site. 0 = unset, and the registry defaults it to 1
            // (productive site).
            [Range(0f, 1f)] public float siteQuality01;
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
            [NonSerialized] public string sellerBusinessKey;
            public string buyerBusinessKey = string.Empty;
            public string productId = string.Empty;
            [Min(0)] public int unitsPerDelivery;
            [Min(0)] public int deliveryCadenceDays = 1;
            [Min(0)] public int pricePerUnitCents;
            public BakeryWholesaleFreshness freshness = BakeryWholesaleFreshness.FreshOrDayOld;
            public int firstDeliveryDayIndex;
        }

        [Header("General settings")]
        [SerializeField] private GeneralSettings general = new();
        [Header("Residents")]
        [SerializeField] private PersonDefinition[] people = Array.Empty<PersonDefinition>();
        [Header("Households")]
        [SerializeField] private HouseholdDefinition[] households = Array.Empty<HouseholdDefinition>();
        [Header("Relationships")]
        [SerializeField] private RelationshipDefinition[] relationships = Array.Empty<RelationshipDefinition>();
        [Header("Businesses")]
        [SerializeField] private BusinessDefinition[] businesses = Array.Empty<BusinessDefinition>();
        [Header("World resources")]
        [SerializeField] private TimberStandEntry[] timberStands = Array.Empty<TimberStandEntry>();
        [Header("Development-only rules")]
        [SerializeField, Min(0)] private int schemaVersion;

        public GeneralSettings Settings => general ??= new GeneralSettings();
        public int SchemaVersion => schemaVersion;
        public string ConfigurationId => Settings.configurationId ?? string.Empty;
        public int SeedOffset => Settings.seedOffset;
        public bool GeneratePopulationFromSettings => Settings.generatePopulationFromSettings;
        public PopulationGenerationSettings GeneratedPopulationSettings => Settings.generatedPopulationSettings;
        public IReadOnlyList<PersonDefinition> People => people ?? Array.Empty<PersonDefinition>();
        public IReadOnlyList<HouseholdDefinition> Households => households ?? Array.Empty<HouseholdDefinition>();
        public IReadOnlyList<RelationshipDefinition> Relationships => relationships ?? Array.Empty<RelationshipDefinition>();
        public IReadOnlyList<BusinessDefinition> Businesses => businesses ?? Array.Empty<BusinessDefinition>();
        public IReadOnlyList<TimberStandEntry> TimberStands => timberStands ?? Array.Empty<TimberStandEntry>();
        public string FreightBusinessKey => Settings.freightBusinessKey ?? string.Empty;
        public bool EnableMortality => Settings.enableMortality;
        public bool EnableFertility => Settings.enableFertility;

        // K1 (2026-11-17): the five derived compatibility views
        // (OpeningInventory, BusinessCashTargets, HouseholdCashTargets,
        // OpeningEquipment, OpeningEmployees) are deleted. All consumers
        // now iterate the owning BusinessDefinition and HouseholdDefinition
        // records directly; schema-2 opening allocations are the single
        // authority. The nested DTO types stay.

        public BusinessDefinition FindBusinessByOperation(string operationId) => Businesses.FirstOrDefault(b =>
            b != null && string.Equals(b.operationId, operationId, StringComparison.OrdinalIgnoreCase));

        public BusinessDefinition FindBusiness(string businessKey) => TryGetBusiness(businessKey, out BusinessDefinition business) ? business : null;

        [Obsolete("Read spare blades from each owning BusinessDefinition.")]
        public IReadOnlyList<BusinessSpareBladeDefinition> OpeningSpareBlades => Businesses.SelectMany(b =>
            (b?.opening?.spareBlades ?? Array.Empty<BusinessSpareBladeDefinition>()).Select(x => { x.businessKey = b.key; return x; })).ToArray();
        public IReadOnlyList<BakeryWholesaleAccountDefinition> WholesaleAccounts => Businesses.SelectMany(b =>
            (b?.opening?.wholesaleAgreements ?? Array.Empty<BakeryWholesaleAccountDefinition>()).Select(x => { x.sellerBusinessKey = b.key; return x; })).ToArray();
        public int OpeningLoggingCampProvisionCoverDays => FindBusinessByOperation("sawmill")?.opening?.loggingCampProvisionCoverDays ?? 0;
        public int OpeningBakeryFuelUnits => FindBusinessByOperation("bakery")?.opening?.bakeryFuelUnits ?? 0;
        public int OpeningBakeryFlourPounds => FindBusinessByOperation("bakery")?.opening?.bakeryFlourPounds ?? 0;
        public int OpeningBakeryNotionsUnits => FindBusinessByOperation("bakery")?.opening?.bakeryNotionsUnits ?? 0;
        public int OpeningCarpentryLumberUnits => FindBusinessByOperation("carpentry-workshop")?.opening?.carpentryLumberUnits ?? 0;
        public int FuelDealerIntakeUnitsPerBakeryUnit => FindBusinessByOperation("fuel-dealer")?.opening?.fuelDealerIntakeUnitsPerBakeryUnit ?? 0;
        public string OpeningHorseId => FindBusiness(FreightBusinessKey)?.opening?.openingHorseId ?? string.Empty;
        public string OpeningWagonId => FindBusiness(FreightBusinessKey)?.opening?.openingWagonId ?? string.Empty;

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
            if (GeneratePopulationFromSettings
                && personKey.StartsWith("generated-person-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(personKey.Substring("generated-person-".Length), out int generatedPersonId)
                && generatedPersonId >= 0)
                return generatedPersonId;
            if (TryGetPerson(personKey, out PersonDefinition person) && person.runtimePersonId > 0)
                return person.runtimePersonId;
            // Transitional compatibility for generated fixtures without an
            // explicit stable id. Authored town assets migrate to explicit ids.
            int id = 1;
            foreach (HouseholdDefinition household in Households)
                foreach (string memberKey in household.memberPersonKeys ?? Array.Empty<string>())
                { if (string.Equals(memberKey, personKey, StringComparison.OrdinalIgnoreCase)) return id; id++; }
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
            // K1: direct owner-record iteration. The deleted OpeningInventory
            // view projected the same inventory arrays; the hash below is
            // untouched, so seeded quantities are identical.
            if (!TryGetBusiness(businessKey, out BusinessDefinition business)) return false;
            foreach (OpeningInventoryItem item in business.opening?.inventory ?? Array.Empty<OpeningInventoryItem>())
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
            return false;
        }

        public bool Validate(out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(ConfigurationId)) { error = "Development town configuration needs a stable ID."; return false; }
            if (GeneratePopulationFromSettings)
            {
                if (GeneratedPopulationSettings == null)
                { error = "Generated development presets require PopulationGenerationSettings."; return false; }
                if (People.Count != 0 || Households.Count != 0)
                { error = "Generated development presets must not duplicate an authored person or household roster."; return false; }
                var generatedBusinessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var generatedPersonOwners = new HashSet<int>();
                foreach (BusinessDefinition business in Businesses)
                {
                    int ownerId = business != null ? ResolveRuntimePersonId(business.ownerPersonKey) : -1;
                    if (business == null || string.IsNullOrWhiteSpace(business.key) || string.IsNullOrWhiteSpace(business.operationId)
                        || ownerId <= 0 || ownerId > GeneratedPopulationSettings.maximumStartingResidents
                        || business.openingCashCents < 0 || !generatedBusinessKeys.Add(business.key)
                        || !generatedPersonOwners.Add(ownerId))
                    { error = "Generated opening businesses need unique keys and distinct owners within the generated starting population."; return false; }
                    if (business.freightRateCard != null
                        && (business.freightRateCard.baseServiceFeeCents < 0 || business.freightRateCard.ratePerMileCents < 0))
                    { error = $"Development business '{business.key}' has a freight rate card with a negative fee."; return false; }
                }
                var generatedCashBusinessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // K1: direct owner-record iteration. BusinessCashTargets
                // projected one entry per business from openingCashCents;
                // the checks below are the same checks on the owning record.
                foreach (BusinessDefinition business in Businesses)
                    if (business == null || business.openingCashCents < 0 || !generatedCashBusinessKeys.Add(business.key))
                    { error = "Generated business cash targets must reference unique configured businesses and non-negative balances."; return false; }
                var generatedCashHouseholdKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // K1: the generated branch of the deleted HouseholdCashTargets
                // view returned the stored generated array unchanged; iterate
                // that owner record directly.
                foreach (HouseholdCashTarget target in Settings.generatedHouseholdOpeningCash ?? Array.Empty<HouseholdCashTarget>())
                {
                    int anchorId = target != null ? ResolveRuntimePersonId(target.anchorPersonKey) : -1;
                    if (target == null || anchorId < 0 || anchorId >= GeneratedPopulationSettings.maximumStartingResidents
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
            var runtimePersonIds = new HashSet<int>();
            bool hasStablePersonIds = People.Any(person => person != null && person.runtimePersonId > 0);
            foreach (PersonDefinition person in People)
                if (person == null || string.IsNullOrWhiteSpace(person.key) || string.IsNullOrWhiteSpace(person.firstName)
                    || string.IsNullOrWhiteSpace(person.lastName) || person.age < 18 || person.homeBuildingOrdinal < 0
                    || !personKeys.Add(person.key)
                    || (hasStablePersonIds && (person.runtimePersonId <= 0 || !runtimePersonIds.Add(person.runtimePersonId))))
                { error = "Development residents need unique readable identity keys, names, adult ages, valid home ordinals, and unique stable runtime IDs when IDs are authored."; return false; }
            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var householdKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var homeOrdinals = new HashSet<int>();
            var runtimeHouseholdIds = new HashSet<int>();
            bool hasStableHouseholdIds = Households.Any(household => household != null && household.runtimeHouseholdId > 0);
            foreach (HouseholdDefinition household in Households)
            {
                if (household == null || string.IsNullOrWhiteSpace(household.key) || string.IsNullOrWhiteSpace(household.surname)
                    || household.homeBuildingOrdinal < 0 || household.openingCashCents < 0 || !householdKeys.Add(household.key)
                    || (hasStableHouseholdIds && (household.runtimeHouseholdId <= 0 || !runtimeHouseholdIds.Add(household.runtimeHouseholdId)))
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
            var businessIssues = new List<string>();
            for (int i = 0; i < Businesses.Count; i++)
            {
                BusinessDefinition business = Businesses[i];
                if (business == null)
                {
                    businessIssues.Add($"businesses[{i}] is null");
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(business.key) ? $"businesses[{i}]" : $"business '{business.key}'";
                if (string.IsNullOrWhiteSpace(business.key))
                    businessIssues.Add($"{label} has an empty key");
                else if (!businessKeys.Add(business.key))
                    businessIssues.Add($"{label} duplicates a configured business key");

                if (string.IsNullOrWhiteSpace(business.operationId))
                    businessIssues.Add($"{label} has an empty operationId");
                if (string.IsNullOrWhiteSpace(business.ownerPersonKey))
                    businessIssues.Add($"{label} has an empty ownerPersonKey");
                else if (!personKeys.Contains(business.ownerPersonKey))
                    businessIssues.Add($"{label} ownerPersonKey '{business.ownerPersonKey}' does not match a configured person key");
                if (business.openingCashCents < 0)
                    businessIssues.Add($"{label} has negative openingCashCents ({business.openingCashCents})");
                if (business.freightRateCard != null
                    && (business.freightRateCard.baseServiceFeeCents < 0 || business.freightRateCard.ratePerMileCents < 0))
                    businessIssues.Add($"{label} has a freightRateCard with a negative fee");
            }
            if (businessIssues.Count > 0)
            {
                error = "Invalid development business configuration: " + string.Join("; ", businessIssues) + ".";
                return false;
            }
            if (!TryGetBusiness(FreightBusinessKey, out _) || string.IsNullOrWhiteSpace(OpeningHorseId) || string.IsNullOrWhiteSpace(OpeningWagonId))
            { error = "Development transport startup must identify its configured freight business, horse, and wagon."; return false; }
            var cashBusinessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // K1: direct owner-record iteration. The deleted
            // BusinessCashTargets view projected one entry per business from
            // openingCashCents; the checks below are the same checks on the
            // owning record.
            foreach (BusinessDefinition business in Businesses)
                if (business == null || business.openingCashCents < 0 || !cashBusinessKeys.Add(business.key))
                { error = "Business cash targets must reference unique configured businesses and non-negative balances."; return false; }
            var cashHouseholdKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // K1: direct owner-record iteration. The deleted
            // HouseholdCashTargets view anchored each household on its first
            // member with target openingCashCents; households with no
            // members were already refused above, so the skip below is
            // unreachable and only mirrors the old view filter.
            foreach (HouseholdDefinition household in Households)
            {
                if (household == null || household.memberPersonKeys == null || household.memberPersonKeys.Length == 0) continue;
                string anchorPersonKey = household.memberPersonKeys[0];
                if (!TryGetPerson(anchorPersonKey, out _) || household.openingCashCents < 0 || !cashHouseholdKeys.Add(anchorPersonKey))
                { error = "Household cash targets must reference unique configured residents and non-negative balances."; return false; }
            }
            var inventoryBusinessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // K1: direct owner-record iteration over each business's opening
            // inventory. The deleted OpeningInventory view projected the
            // same arrays for businesses with non-empty inventory.
            foreach (BusinessDefinition business in Businesses)
            {
                if (business == null || business.opening?.inventory == null || business.opening.inventory.Length == 0) continue;
                if (!inventoryBusinessKeys.Add(business.key))
                { error = "Opening inventory entries must reference unique configured businesses."; return false; }
                var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (OpeningInventoryItem item in business.opening.inventory)
                    if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.minimumUnits < 0 || item.maximumUnits < item.minimumUnits || !items.Add(item.itemId))
                    { error = $"Opening inventory for '{business.key}' contains an invalid or duplicate item range."; return false; }
            }
            // Slice S2: opening equipment is optional and zero by default.
            // When present, every entry must name a configured business, every
            // kit id must resolve in the ToolKitCatalog, and every oven entry
            // must list exactly the BakeOven definition's component count.
            // K1: direct owner-record iteration. The deleted OpeningEquipment
            // view returned each business's opening.equipment record; the
            // business key in messages is the owning business key.
            foreach (BusinessDefinition business in Businesses)
            {
                if (business == null || business.opening?.equipment == null) continue;
                BusinessOpeningEquipment entry = business.opening.equipment;
                foreach (string kitId in entry.kitIds ?? Array.Empty<string>())
                    if (string.IsNullOrWhiteSpace(kitId) || ToolKitCatalog.Get(kitId.Trim()) == null)
                    { error = $"Opening equipment for '{business.key}' names an unknown tool kit '{kitId}'."; return false; }
                foreach (OvenEquipment oven in entry.ovens ?? Array.Empty<OvenEquipment>())
                    if (oven == null || oven.componentAssetIds == null
                        || oven.componentAssetIds.Length != WorkstationCatalog.BakeOven.Components.Count)
                    { error = $"Opening equipment for '{business.key}' lists an oven whose component count must equal the BakeOven definition ({WorkstationCatalog.BakeOven.Components.Count})."; return false; }
                // A2 plan 27 (P10): asset entries need a non-blank id and
                // kind, no duplicates, and never the slice fixture id.
                var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (OpeningAssetEntry asset in entry.assetEntries ?? Array.Empty<OpeningAssetEntry>())
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.assetId) || string.IsNullOrWhiteSpace(asset.kind)
                        || !assetIds.Add(asset.assetId.Trim()))
                    { error = $"Opening equipment for '{business.key}' contains an invalid or duplicate asset entry."; return false; }
                    if (string.Equals(asset.assetId.Trim(), "slice-felling-axe", StringComparison.Ordinal))
                    { error = $"Opening equipment for '{business.key}' must not reuse the slice fixture id 'slice-felling-axe'."; return false; }
                }
            }
            // C-1: opening employments are optional and zero by default.
            // When present, every entry must name a configured business and
            // a configured person, a non-blank role, and a positive weekly
            // wage (the hiring path refuses a non-positive wage); one
            // person holds at most one opening job, and a business owner
            // is never their own opening employee (the owner is rostered
            // separately wherever a crew is built).
            var openingEmployeePersons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // K1: direct owner-record iteration. The deleted OpeningEmployees
            // view flattened each business's opening.employees; the owning
            // business is the employer, so the TryGetBusiness check the old
            // loop ran on the stamped businessKey is vacuous here.
            foreach (BusinessDefinition business in Businesses)
            {
                if (business == null) continue;
                foreach (BusinessEmployeeDefinition employee in business.opening?.employees ?? Array.Empty<BusinessEmployeeDefinition>())
                {
                    if (employee == null
                        || !TryGetPerson(employee.personKey, out _)
                        || string.IsNullOrWhiteSpace(employee.role) || employee.weeklyWageCents <= 0
                        || string.Equals(employee.personKey, business.ownerPersonKey, StringComparison.OrdinalIgnoreCase)
                        || !openingEmployeePersons.Add(employee.personKey))
                    { error = "Opening employees must reference a configured business and person, a non-blank role, a positive weekly wage, and a person who is not that business's owner and holds no other opening job."; return false; }
                }
            }
            // C-5: opening spare blades are optional and zero by default.
            // When present, every entry must name a configured business and
            // a non-blank unique blade id (case-insensitive).
            var openingSpareBladeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BusinessSpareBladeDefinition spare in OpeningSpareBlades)
            {
                if (spare == null || !TryGetBusiness(spare.businessKey, out _)
                    || string.IsNullOrWhiteSpace(spare.bladeId)
                    || !openingSpareBladeIds.Add(spare.bladeId.Trim()))
                { error = "Opening spare blades must reference a configured business and a non-blank unique blade id."; return false; }
            }
            // A2 plan 27 (P10): timber stands are optional and zero by
            // default. When present, every stand needs a unique non-blank
            // id, a non-blank location, and non-negative kilograms per
            // log and standing units.
            var standIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TimberStandEntry stand in TimberStands)
                if (stand == null || string.IsNullOrWhiteSpace(stand.standId) || string.IsNullOrWhiteSpace(stand.locationId)
                    || stand.kilogramsPerLog < 0f || stand.standingTimberUnits < 0 || !standIds.Add(stand.standId))
                { error = "Timber stand entries need unique non-blank ids, a location, and non-negative kilograms per log and standing units."; return false; }
            return true;
        }

    }
}
