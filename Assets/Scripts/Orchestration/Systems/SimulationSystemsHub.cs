using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Civic;
using LandLedgers.Economy;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Transport;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Farming.Risk;
using LandLedgers.Economy.Postal;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Storage;
using LandLedgers.Economy.Delegation;
using LandLedgers.Persistence;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.Barber;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Economy.Businesses.GrainMill;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Economy.Businesses.Newspaper;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Businesses.Tailor;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Farming.Livestock;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.FirstLedger;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.World.Journeys;
using LandLedgers.World.Property;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Orchestration.Systems
{
    /// <summary>
    /// CLN-1: the scene-level home for the standalone simulation authorities accumulated
    /// across HF/TTS/DEV/BIZ/FRM. These authorities own real game state but had no
    /// MonoBehaviour holder and no save-pipeline wiring; this hub gives them both.
    /// Kennedy attaches this to the one scene (one instance); the SaveLoadManager
    /// finds it via FindAnyObjectByType and persists its section.
    /// The hub owns the authorities and the save boundary — it contains no simulation
    /// logic itself (drivers live in CLN-2).
    /// </summary>
    public sealed class SimulationSystemsHub : MonoBehaviour
    {
        [SerializeField]
        private EntityIdRegistry idRegistry = new EntityIdRegistry();

        // Pass XI WS1 (A-21): NOT a SerializeField. AnimalRegistry has
        // no Serializable attribute and holds Dictionary fields Unity
        // cannot scene-persist; the lazy Animals property owns the
        // runtime instance and the DTO save path is unaffected.
        private AnimalRegistry animalRegistry;

        [SerializeField]
        private HouseholdLedgerRegistry householdLedgers = new HouseholdLedgerRegistry();

        /// <summary>Phase B: per-household real item-lot inventories.</summary>
        private HouseholdInventoryRegistry householdInventories;

        /// <summary>Phase B: per-household meal logs (served, missed, preparations).</summary>
        private HouseholdMealLogRegistry mealLog;

        /// <summary>Phase B: household purchasing needs (demand, never sales).</summary>
        private HouseholdNeedRegistry purchasingNeeds;
        /// <summary>Authoritative person-to-hand state shared by task gating and inventory queries.</summary>
        private PersonHandOccupancyRegistry personHands;
        /// <summary>Runtime identity and location directory for instantiated handheld containers.</summary>
        private HandheldContainerRegistry handheldContainers;
        [SerializeField]
        private TaskAuthority taskAuthority = new TaskAuthority();

        /// <summary>
        /// Priority 4 Slice 4 — the installed task-duration diagnostic.
        /// NOT serialized: its records are ephemeral sidecar state (plan
        /// §2.5), rebuilt from the authority after a load. Null until
        /// InstallTaskDurationDiagnostics runs.
        /// </summary>
        private TaskDurationDiagnostics taskDurationDiagnostics;

        [SerializeField]
        private SkillService skillService = new SkillService();

        [SerializeField]
        private WorkTimeBudgetStore workTimeBudgets = new WorkTimeBudgetStore();

        private GenericProductionRuntimeRegistry genericProductionRuntime;

        [SerializeField]
        private EnterpriseValuationReadModel valuation = new EnterpriseValuationReadModel();

        [SerializeField]
        private FreightResourcePool freightPool = new FreightResourcePool();

        [SerializeField]
        private TransportAssetRegistry transportAssets = new TransportAssetRegistry();

        [SerializeField]
        private List<ButcherRuntime> butcherRuntimes = new List<ButcherRuntime>();

        [SerializeField]
        private FarmFlowLedger farmFlows = new FarmFlowLedger();

        // Pass XI WS1 (A-21): NOT a SerializeField. BusinessCapabilityRegistry
        // has no Serializable attribute and holds a Dictionary Unity cannot
        // scene-persist; the lazy Capabilities property owns the runtime
        // instance and the DTO save path is unaffected.
        private BusinessCapabilityRegistry capabilityRegistry = new BusinessCapabilityRegistry();

        [SerializeField]
        private BusinessOperatingLedger operatingLedger = new BusinessOperatingLedger();

        // Pass XI WS1 (A-21): NOT a SerializeField. EmploymentRelationshipRegistry
        // has no Serializable attribute and holds a Dictionary Unity cannot
        // scene-persist; the lazy Employments property owns the runtime
        // instance and the DTO save path is unaffected.
        private EmploymentRelationshipRegistry employments = new EmploymentRelationshipRegistry();

        [SerializeField]
        private FarmSliceSystems farmSlice = new FarmSliceSystems();

        /// <summary>SWN-3: business liabilities (loans, payables) — honest BIZ-5 inputs.</summary>
        private BusinessLiabilityLedger liabilityLedger = new BusinessLiabilityLedger();
        private FinancialObligationAuthority financialObligationAuthority = new FinancialObligationAuthority();
        private CreditOfferWorkflow creditOfferWorkflow = new CreditOfferWorkflow();
        private CreditRegistry creditRegistry = new CreditRegistry();

        public EntityIdRegistry Ids => idRegistry ??= new EntityIdRegistry();
        public AnimalRegistry Animals => animalRegistry ??= new AnimalRegistry(Ids);
        public HouseholdLedgerRegistry HouseholdLedgers => householdLedgers ??= new HouseholdLedgerRegistry();
        /// <summary>Phase B: per-household real item-lot inventories.</summary>
        public HouseholdInventoryRegistry HouseholdInventories => householdInventories ??= new HouseholdInventoryRegistry();
        /// <summary>Phase B: per-household meal logs.</summary>
        public HouseholdMealLogRegistry MealLog => mealLog ??= new HouseholdMealLogRegistry();
        /// <summary>Phase B: household purchasing needs.</summary>
        public HouseholdNeedRegistry PurchasingNeeds => purchasingNeeds ??= new HouseholdNeedRegistry();
        public PersonHandOccupancyRegistry PersonHands => personHands ??= new PersonHandOccupancyRegistry();
        public HandheldContainerRegistry HandheldContainers => handheldContainers ??= new HandheldContainerRegistry();
        public TaskAuthority Tasks => taskAuthority ??= new TaskAuthority();

        /// <summary>Priority 4 Slice 4 — the installed task-duration diagnostic, or null when not installed.</summary>
        public TaskDurationDiagnostics DurationDiagnostics => taskDurationDiagnostics;
        public SkillService Skills => skillService ??= new SkillService();
        public WorkTimeBudgetStore WorkTimeBudgets => workTimeBudgets ??= new WorkTimeBudgetStore();
        public GenericProductionRuntimeRegistry GenericProduction =>
            genericProductionRuntime ??= new GenericProductionRuntimeRegistry(Tasks, WorkTimeBudgets)
            {
                // Pass IX review R-11: the registry's provenance seam is the
                // hub's own persisted stock ledger — without this assignment
                // production output provenance was silently never recorded.
                ProvenanceLedger = StockProvenance,
                ProvenanceIds = Ids,
            };
        public EnterpriseValuationReadModel Valuation => valuation ??= new EnterpriseValuationReadModel();
        public FreightResourcePool FreightPool => freightPool ??= new FreightResourcePool();
        public TransportAssetRegistry TransportAssets => transportAssets ??= new TransportAssetRegistry();

        // Slice S2 (2026-11-05, plan 02 §4): hub-owned per-business
        // equipment registers (ovens' component assets + tool kits),
        // keyed by business instance id. The registers are the live
        // holders; the Pass 9 save seam persists them and the bootstrap
        // re-registers restored ones after a load. A business with no
        // register holds no equipment — the gate still refuses loudly.
        private Dictionary<string, BusinessEquipmentRegister> equipmentRegisters;

        // B8 (Agent B, expanded campaign 2026-11-16): hub-owned
        // per-household stove and storage registries, keyed by household
        // id, parallel to the furnishing registers below. Both derive
        // from the furnishing register (stoves and storage fixtures are
        // installed furniture), so they need no Pass 9 list of their
        // own: the furnishing register persists via W-G1 and these
        // rebuild from it (stove instances via RebuildFromRegister;
        // storage bindings re-bind from the install path — see the
        // B8 record in the B plan). These fields sit BEFORE the
        // furnishing-registers field on purpose: W-G2's
        // householdSleepingPlaceRegistries field sits directly AFTER it,
        // and keeping the two additions on opposite sides of the field
        // keeps the B8 and W-G2 hub edits on disjoint anchors.
        private Dictionary<int, HouseholdStoveRegistry> householdStoveRegistries;
        private Dictionary<int, FurnishingStorageRegistry> householdStorageRegistries;

        // B0 (Agent B, expanded campaign 2026-11-16): hub-owned
        // per-household furnishing registers, keyed by household id
        // (the int id from HouseholdState / HouseholdLedger). The
        // registers are the live holders; the Pass 9 save seam persists
        // them and the bootstrap re-registers restored ones after a
        // load. A household with no register holds no furniture.
        private Dictionary<int, HouseholdFurnishingRegister> householdFurnishingRegisters;
        private Dictionary<int, SleepingPlaceRegistry> householdSleepingPlaceRegistries;
        private Dictionary<int, DiningPlaceRegistry> householdDiningPlaceRegistries;

        // C-3 (expanded campaign): hub-owned per-business workstation
        // registries, keyed by holder id (not just business — the shape
        // serves future household workstations too, e.g. stoves).
        // Mirrors equipmentRegisters above. The registries are the live
        // holders; the bootstrapper re-derives the mill's saw-line station
        // from the equipment register on every boot, so no Pass 9 save
        // seam exists for them in this slice — if workstation instances
        // ever gain mutable runtime state, add the seam (and an adopt
        // method) then. A holder with no registry holds no workstation —
        // the gate still refuses loudly.
        private Dictionary<string, BusinessWorkstations> workstationRegisters;

        // C-3: hub-owned per-business support contexts (Canon 5.2: fuel
        // and consumable stocks, trained operator skills, repair
        // capabilities, compatible infrastructure), keyed by business
        // instance id. The gate's SupportSupplier is holder-blind, so the
        // hub merges every registered context into one (CollectGateSupport
        // below); only the mill has authored support state today, so the
        // merge cannot leak one business's stock into another's check yet
        // — a holder-parameterized supplier is the follow-up if a second
        // business ever needs a conflicting support requirement.
        private Dictionary<string, SupportContext> businessSupport;

        // C2: one live lumber yard runtime per LumberYard business,
        // mirroring the per-business equipment registers above. The Pass 9
        // save seam persists the first yard only (single holder field);
        // see Series2Pass9Save.cs. A business with no runtime holds no
        // yard stock.
        private Dictionary<string, LumberYardShopRuntime> lumberYardRuntimes;

        // D1 (C2 mirror): one live sawmill runtime per Sawmill business.
        // The Pass 9 save seam persists the first mill only (single
        // holder field); see Series2Pass9Save.cs. A business with no
        // runtime holds no mill stock.
        private Dictionary<string, SawmillShopRuntime> sawmillRuntimes;

        // P3 (F6 production wiring): one shared sawmill production driver,
        // one labour counters, one custody ledger. Single instances, not
        // lists — each is one shared authority, wired by
        // FirstLedgerSliceBootstrapper.ConfigureSawmillProductionWiring.
        private SawmillProductionDriver sawmillProductionDriver;
        private SawmillLabourCounters sawmillLabourCounters;
        private LogLotCustodyLedger logLotCustodyLedger;

        // F7 yard-transfer wiring: one shared sawmill-to-yard transfer
        // driver. Single instance, not a list — one shared authority,
        // wired by FirstLedgerSliceBootstrapper.ConfigureSawmillYardTransferWiring.
        private SawmillToYardTransferDriver sawmillToYardTransferDriver;

        // D3 (F-D2-1): one live blacksmith runtime per Blacksmith
        // business. No Pass 9 holder exists for the smith (the smith's
        // shop stock is not a Pass 9 section yet — a future save pass
        // owns that, like the D1 quarter pools); the hub is the live
        // holder, and the yard lots a delivery creates persist through
        // the Pass 9 yard section. A business with no runtime holds no
        // smith stock.
        private Dictionary<string, BlacksmithRuntime> blacksmithRuntimes;

        // D3 (F-D2-1, F-D2-2): one shared smith-to-yard hardware
        // transfer driver and one shared nails import driver. Single
        // instances, not lists — one shared authority each, wired by
        // FirstLedgerSliceBootstrapper.ConfigureBlacksmithYardHardwareWiring
        // and ConfigureLumberYardHardwareImportWiring.
        private BlacksmithToYardHardwareTransferDriver blacksmithToYardHardwareTransferDriver;
        private LumberYardHardwareImportDriver lumberYardHardwareImportDriver;

        /// <summary>Slice S2: the equipment register for one business, created on first use (empty).</summary>
        public BusinessEquipmentRegister EquipmentRegisterFor(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            equipmentRegisters ??= new Dictionary<string, BusinessEquipmentRegister>(StringComparer.Ordinal);
            if (!equipmentRegisters.TryGetValue(businessInstanceId, out BusinessEquipmentRegister register) || register == null)
            {
                register = new BusinessEquipmentRegister { BusinessInstanceId = businessInstanceId };
                equipmentRegisters[businessInstanceId] = register;
            }
            return register;
        }

        /// <summary>
        /// W-G1 (Agent G, expanded campaign 2026-11-16): read-only
        /// snapshot of the hub's live equipment registers, for the
        /// Pass 9 save sync (Series2Pass9SaveState.
        /// SyncRegistersFromHub). A new list each call, never the live
        /// dictionary, so callers cannot add or remove hub holders.
        /// Empty when no register has been created yet.
        /// </summary>
        public IReadOnlyList<BusinessEquipmentRegister> AllEquipmentRegisters =>
            equipmentRegisters != null
                ? new List<BusinessEquipmentRegister>(equipmentRegisters.Values)
                : new List<BusinessEquipmentRegister>();

        /// <summary>C2: the lumber yard runtime for one business, created on first use (empty).</summary>
        public LumberYardShopRuntime LumberYardRuntimeFor(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            lumberYardRuntimes ??= new Dictionary<string, LumberYardShopRuntime>(StringComparer.Ordinal);
            if (!lumberYardRuntimes.TryGetValue(businessInstanceId, out LumberYardShopRuntime yard) || yard == null)
            {
                yard = new LumberYardShopRuntime(businessInstanceId, Ids);
                lumberYardRuntimes[businessInstanceId] = yard;
            }
            return yard;
        }

        /// <summary>
        /// Slice S2: adopts a register (e.g. one restored from the Pass 9
        /// save seam) as the hub's live holder for its business. Replaces
        /// by business id, never merges — the save data is authoritative,
        /// and re-registering the same register is idempotent.
        /// </summary>
        public void RegisterBusinessEquipment(BusinessEquipmentRegister register)
        {
            if (register == null || string.IsNullOrWhiteSpace(register.BusinessInstanceId)) return;
            equipmentRegisters ??= new Dictionary<string, BusinessEquipmentRegister>(StringComparer.Ordinal);
            equipmentRegisters[register.BusinessInstanceId] = register;
        }

        /// <summary>B0: the furnishing register for one household, created on first use (empty).</summary>
        public HouseholdFurnishingRegister FurnishingRegisterFor(int householdId)
        {
            householdFurnishingRegisters ??= new Dictionary<int, HouseholdFurnishingRegister>();
            if (!householdFurnishingRegisters.TryGetValue(householdId, out HouseholdFurnishingRegister register) || register == null)
            {
                register = new HouseholdFurnishingRegister { HouseholdId = householdId };
                householdFurnishingRegisters[householdId] = register;
            }
            return register;
        }

        /// <summary>
        /// W-G1 (Agent G, expanded campaign 2026-11-16): read-only
        /// snapshot of the hub's live furnishing registers, for the
        /// Pass 9 save sync (Series2Pass9SaveState.
        /// SyncRegistersFromHub). A new list each call, never the live
        /// dictionary, so callers cannot add or remove hub holders.
        /// Empty when no register has been created yet.
        /// </summary>
        public IReadOnlyList<HouseholdFurnishingRegister> AllFurnishingRegisters =>
            householdFurnishingRegisters != null
                ? new List<HouseholdFurnishingRegister>(householdFurnishingRegisters.Values)
                : new List<HouseholdFurnishingRegister>();

        /// <summary>
        /// B0: adopts a register (e.g. one restored from the Pass 9 save
        /// seam) as the hub's live holder for its household. Replaces by
        /// household id, never merges — the save data is authoritative,
        /// and re-registering the same register is idempotent.
        /// </summary>
        public void RegisterHouseholdFurnishings(HouseholdFurnishingRegister register)
        {
            if (register == null) return;
            householdFurnishingRegisters ??= new Dictionary<int, HouseholdFurnishingRegister>();
            householdFurnishingRegisters[register.HouseholdId] = register;
        }

        /// <summary>
        /// C-3: the workstation registry for one holder, created on first use
        /// (empty). Mirrors EquipmentRegisterFor. A holder with no registry
        /// holds no workstation — the gate refuses "not established".
        /// </summary>
        public BusinessWorkstations WorkstationRegisterFor(string holderId)
        {
            if (string.IsNullOrWhiteSpace(holderId)) return null;
            workstationRegisters ??= new Dictionary<string, BusinessWorkstations>(StringComparer.Ordinal);
            if (!workstationRegisters.TryGetValue(holderId, out BusinessWorkstations registry) || registry == null)
            {
                registry = new BusinessWorkstations(holderId);
                workstationRegisters[holderId] = registry;
            }
            return registry;
        }

        /// <summary>
        /// W-G2 (Agent G, expanded campaign 2026-11-16): the
        /// sleeping-place registry for one household, created on first use
        /// and derived from the household's furnishing register (beds and
        /// bedrolls come from it; the floor fallback needs no assets).
        /// Hub-owned, one per household, parallel to the furnishing
        /// registers. The registry is NOT a Pass 9 list of its own: the
        /// furnishing register is the persisted truth, and the sleep
        /// registry re-mirrors from it after Continue (see
        /// FirstLedgerSliceBootstrapper, W-G2 hook). Rotation history is
        /// deliberately not persisted (E checkpoint 70 ruled it
        /// acceptable for B1); only fairness, never correctness, resets
        /// on load, because the asset holds still prevent double-booking.
        /// </summary>
        public SleepingPlaceRegistry SleepingPlaceRegistryFor(int householdId)
        {
            householdSleepingPlaceRegistries ??= new Dictionary<int, SleepingPlaceRegistry>();
            if (!householdSleepingPlaceRegistries.TryGetValue(householdId, out SleepingPlaceRegistry registry) || registry == null)
            {
                registry = new SleepingPlaceRegistry
                {
                    HouseholdId = householdId,
                    Furnishings = FurnishingRegisterFor(householdId),
                };
                householdSleepingPlaceRegistries[householdId] = registry;
            }
            return registry;
        }

        /// <summary>
        /// C-3: the support context for one business, created on first use
        /// (empty). Mirrors EquipmentRegisterFor. The bootstrapper registers
        /// the mill's authored support (saw-filing capability, lubrication
        /// store) into the context it returns here.
        /// </summary>
        public SupportContext SupportContextFor(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            businessSupport ??= new Dictionary<string, SupportContext>(StringComparer.Ordinal);
            if (!businessSupport.TryGetValue(businessInstanceId, out SupportContext context) || context == null)
            {
                context = new SupportContext();
                businessSupport[businessInstanceId] = context;
            }
            return context;
        }

        /// <summary>
        /// C-3: adopts a support context (e.g. one restored from a save seam)
        /// as the hub's live holder for its business. Replaces by business
        /// id, never merges — the adopted data is authoritative, and
        /// re-registering the same context is idempotent.
        /// </summary>
        public void RegisterBusinessSupport(string businessInstanceId, SupportContext support)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId) || support == null) return;
            businessSupport ??= new Dictionary<string, SupportContext>(StringComparer.Ordinal);
            businessSupport[businessInstanceId] = support;
        }

        /// <summary>
        /// W-G2: read-only snapshot of the hub's live sleeping-place
        /// registries, for the sleep tick driver. A new list each call,
        /// never the live dictionary, so callers cannot add or remove hub
        /// holders. Empty when no registry has been created yet.
        /// </summary>
        public IReadOnlyList<SleepingPlaceRegistry> AllSleepingPlaceRegistries =>
            householdSleepingPlaceRegistries != null
                ? new List<SleepingPlaceRegistry>(householdSleepingPlaceRegistries.Values)
                : new List<SleepingPlaceRegistry>();

        /// <summary>
        /// G-4 (Agent G, expanded campaign 2026-11-16): the dining-place
        /// registry for one household, created on first use (empty).
        /// Derived from the household's B0 furnishing register (chairs
        /// and tables are derived from it), parallel to
        /// SleepingPlaceRegistryFor. The registry is NOT a Pass 9 list
        /// of its own: reservations are intraday working state,
        /// re-derived by the next day's planning, and the meal driver
        /// releases past-day reservations on its next tick — so no
        /// Continue re-mirror is needed (unlike sleep's asset holds).
        /// </summary>
        public DiningPlaceRegistry DiningPlaceRegistryFor(int householdId)
        {
            householdDiningPlaceRegistries ??= new Dictionary<int, DiningPlaceRegistry>();
            if (!householdDiningPlaceRegistries.TryGetValue(householdId, out DiningPlaceRegistry registry) || registry == null)
            {
                registry = new DiningPlaceRegistry
                {
                    HouseholdId = householdId,
                    Furnishings = FurnishingRegisterFor(householdId),
                };
                householdDiningPlaceRegistries[householdId] = registry;
            }
            return registry;
        }

        /// <summary>
        /// G-4: read-only snapshot of the hub's live dining-place
        /// registries, for the meal tick driver. A new list each call,
        /// never the live dictionary, so callers cannot add or remove hub
        /// holders. Empty when no registry has been created yet.
        /// </summary>
        public IReadOnlyList<DiningPlaceRegistry> AllDiningPlaceRegistries =>
            householdDiningPlaceRegistries != null
                ? new List<DiningPlaceRegistry>(householdDiningPlaceRegistries.Values)
                : new List<DiningPlaceRegistry>();

        /// <summary>
        /// W-G2: adopts a registry (e.g. one rebuilt from the Pass 9
        /// furnishing registers after Continue) as the hub's live holder
        /// for its household. Replaces by household id, never merges —
        /// the re-mirrored data is authoritative, and re-registering the
        /// same registry is idempotent.
        /// </summary>
        public void RegisterSleepingPlaces(SleepingPlaceRegistry registry)
        {
            if (registry == null) return;
            householdSleepingPlaceRegistries ??= new Dictionary<int, SleepingPlaceRegistry>();
            householdSleepingPlaceRegistries[registry.HouseholdId] = registry;
        }

        /// <summary>
        /// C2: adopts a runtime (e.g. one restored from the Pass 9 save
        /// seam) as the hub's live holder for its business. Replaces by
        /// business id, never merges — the save data is authoritative,
        /// and re-registering the same runtime is idempotent.
        /// </summary>
        public void RegisterLumberYardRuntime(LumberYardShopRuntime runtime)
        {
            if (runtime == null || string.IsNullOrWhiteSpace(runtime.BusinessInstanceId)) return;
            lumberYardRuntimes ??= new Dictionary<string, LumberYardShopRuntime>(StringComparer.Ordinal);
            lumberYardRuntimes[runtime.BusinessInstanceId] = runtime;
        }

        /// <summary>D1 (C2 mirror): the sawmill runtime for one business, created on first use (empty).</summary>
        public SawmillShopRuntime SawmillRuntimeFor(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            sawmillRuntimes ??= new Dictionary<string, SawmillShopRuntime>(StringComparer.Ordinal);
            if (!sawmillRuntimes.TryGetValue(businessInstanceId, out SawmillShopRuntime mill) || mill == null)
            {
                mill = new SawmillShopRuntime(businessInstanceId, Ids);
                sawmillRuntimes[businessInstanceId] = mill;
            }
            return mill;
        }

        /// <summary>
        /// D1 (C2 mirror): adopts a runtime (e.g. one restored from the
        /// Pass 9 save seam) as the hub's live holder for its business.
        /// Replaces by business id, never merges — the save data is
        /// authoritative, and re-registering the same runtime is idempotent.
        /// </summary>
        public void RegisterSawmillRuntime(SawmillShopRuntime runtime)
        {
            if (runtime == null || string.IsNullOrWhiteSpace(runtime.BusinessInstanceId)) return;
            sawmillRuntimes ??= new Dictionary<string, SawmillShopRuntime>(StringComparer.Ordinal);
            sawmillRuntimes[runtime.BusinessInstanceId] = runtime;
        }

        /// <summary>
        /// D3 (F-D2-1, D1 mirror): the blacksmith runtime for one
        /// business, created on first use (empty — the forge station is
        /// not ready until the business formation path owns it, and the
        /// runtime name falls back to the business id). The smith cannot
        /// forge in production until it holds iron, so an empty holder
        /// simply has nothing to deliver.
        /// </summary>
        public BlacksmithRuntime BlacksmithRuntimeFor(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            blacksmithRuntimes ??= new Dictionary<string, BlacksmithRuntime>(StringComparer.Ordinal);
            if (!blacksmithRuntimes.TryGetValue(businessInstanceId, out BlacksmithRuntime smithy) || smithy == null)
            {
                smithy = new BlacksmithRuntime(businessInstanceId, businessInstanceId, false);
                blacksmithRuntimes[businessInstanceId] = smithy;
            }
            return smithy;
        }

        /// <summary>
        /// D3 (F-D2-1, D1 mirror): adopts a runtime (e.g. one restored
        /// from a save seam) as the hub's live holder for its business.
        /// Replaces by business id, never merges — the save data is
        /// authoritative, and re-registering the same runtime is idempotent.
        /// </summary>
        public void RegisterBlacksmithRuntime(BlacksmithRuntime runtime)
        {
            if (runtime == null || string.IsNullOrWhiteSpace(runtime.BusinessInstanceId)) return;
            blacksmithRuntimes ??= new Dictionary<string, BlacksmithRuntime>(StringComparer.Ordinal);
            blacksmithRuntimes[runtime.BusinessInstanceId] = runtime;
        }

        /// <summary>
        /// B8 (Agent B, expanded campaign 2026-11-16): the stove
        /// registry for one household, created on first use and derived
        /// from the household's furnishing register via
        /// HouseholdStoveRegistry.CreateDerived (installed stoves are
        /// re-derived from the B0 bindings at creation, so the registry
        /// never reads empty when a stove is installed). Hub-owned, one
        /// per household, parallel to the furnishing registers. The
        /// registry is NOT a Pass 9 list of its own: the furnishing
        /// register is the persisted truth, and the stove registry
        /// re-mirrors from it after Continue (see
        /// FirstLedgerSliceBootstrapper, B8 hook). Fuel stores persist
        /// through the Pass 9 HouseholdStoveFuelStores section once
        /// Agent G lands the fuel-store sync (B2's item, W-G1 follow-up);
        /// until then a save carries no stove fuel. This section sits
        /// after RegisterSawmillRuntime on purpose: W-G2's sleeping-place
        /// accessors sit directly after RegisterHouseholdFurnishings,
        /// and keeping the two additions on opposite sides of the
        /// lumber-yard/sawmill runtime methods keeps the B8 and W-G2
        /// hub edits on disjoint anchors.
        /// </summary>
        public HouseholdStoveRegistry HouseholdStoveRegistryFor(int householdId)
        {
            householdStoveRegistries ??= new Dictionary<int, HouseholdStoveRegistry>();
            if (!householdStoveRegistries.TryGetValue(householdId, out HouseholdStoveRegistry registry) || registry == null)
            {
                registry = HouseholdStoveRegistry.CreateDerived(householdId, FurnishingRegisterFor(householdId));
                householdStoveRegistries[householdId] = registry;
            }
            return registry;
        }

        /// <summary>
        /// B8: read-only snapshot of the hub's live stove registries, for
        /// the fuel-store sync (Agent G) and the daily drives. A new list
        /// each call, never the live dictionary, so callers cannot add or
        /// remove hub holders. Empty when no registry has been created yet.
        /// </summary>
        public IReadOnlyList<HouseholdStoveRegistry> AllStoveRegistries =>
            householdStoveRegistries != null
                ? new List<HouseholdStoveRegistry>(householdStoveRegistries.Values)
                : new List<HouseholdStoveRegistry>();

        /// <summary>
        /// B8: adopts a registry (e.g. one rebuilt from the Pass 9
        /// furnishing registers after Continue) as the hub's live holder
        /// for its household. Replaces by household id, never merges —
        /// the re-mirrored data is authoritative, and re-registering the
        /// same registry is idempotent.
        /// </summary>
        public void RegisterStoveRegistry(HouseholdStoveRegistry registry)
        {
            if (registry == null) return;
            householdStoveRegistries ??= new Dictionary<int, HouseholdStoveRegistry>();
            householdStoveRegistries[registry.HouseholdId] = registry;
        }

        /// <summary>
        /// B8: the storage registry for one household, created on first
        /// use and bound to the household's furnishing register and live
        /// inventory (both may be empty; a null inventory means the
        /// binding's lot lookups refuse loudly until the daily drive or
        /// the Continue hook re-points it via RefreshInventory). Hub-owned,
        /// one per household, parallel to the furnishing registers. The
        /// registry is NOT a Pass 9 list of its own: the bindings
        /// persist through the Pass 9 state list
        /// HouseholdStorageBindings (G-4 save seam) and the Continue
        /// hook restores them after RefreshInventory; the day-boundary
        /// poll recomputes live bindings.
        /// </summary>
        public FurnishingStorageRegistry FurnishingStorageRegistryFor(int householdId)
        {
            householdStorageRegistries ??= new Dictionary<int, FurnishingStorageRegistry>();
            if (!householdStorageRegistries.TryGetValue(householdId, out FurnishingStorageRegistry registry) || registry == null)
            {
                registry = new FurnishingStorageRegistry
                {
                    HouseholdId = householdId,
                    Furnishings = FurnishingRegisterFor(householdId),
                    Inventory = HouseholdInventories?.Get(householdId),
                };
                householdStorageRegistries[householdId] = registry;
            }
            return registry;
        }

        /// <summary>
        /// B8: read-only snapshot of the hub's live storage registries,
        /// for the day-boundary display poll. A new list each call, never
        /// the live dictionary. Empty when no registry has been created yet.
        /// </summary>
        public IReadOnlyList<FurnishingStorageRegistry> AllStorageRegistries =>
            householdStorageRegistries != null
                ? new List<FurnishingStorageRegistry>(householdStorageRegistries.Values)
                : new List<FurnishingStorageRegistry>();

        /// <summary>
        /// B8: adopts a registry as the hub's live holder for its
        /// household. Replaces by household id, never merges, and
        /// re-registering the same registry is idempotent.
        /// </summary>
        public void RegisterStorageRegistry(FurnishingStorageRegistry registry)
        {
            if (registry == null) return;
            householdStorageRegistries ??= new Dictionary<int, FurnishingStorageRegistry>();
            householdStorageRegistries[registry.HouseholdId] = registry;
        }

        /// <summary>
        /// P3 (F6 production wiring): adopts the sawmill production driver
        /// as the hub's live holder. Single instance; re-registering the
        /// same driver is idempotent.
        /// </summary>
        public void RegisterSawmillProductionDriver(SawmillProductionDriver driver)
        {
            if (driver == null) return;
            sawmillProductionDriver = driver;
        }

        /// <summary>P3 (F6 production wiring): the live sawmill production driver, or null when unwired.</summary>
        public SawmillProductionDriver SawmillProductionDriver => sawmillProductionDriver;

        /// <summary>
        /// P3 (F6 production wiring): adopts the sawmill labour counters as
        /// the hub's live holder. Single instance; re-registering the same
        /// counters is idempotent.
        /// </summary>
        public void RegisterSawmillLabourCounters(SawmillLabourCounters counters)
        {
            if (counters == null) return;
            sawmillLabourCounters = counters;
        }

        /// <summary>P3 (F6 production wiring): the live sawmill labour counters, or null when unwired.</summary>
        public SawmillLabourCounters SawmillLabourCounters => sawmillLabourCounters;

        /// <summary>
        /// P3 (F6 production wiring): adopts the log-lot custody ledger as
        /// the hub's live holder. Single instance; re-registering the same
        /// ledger is idempotent.
        /// </summary>
        public void RegisterLogLotCustodyLedger(LogLotCustodyLedger ledger)
        {
            if (ledger == null) return;
            logLotCustodyLedger = ledger;
        }

        /// <summary>P3 (F6 production wiring): the live log-lot custody ledger, or null when unwired.</summary>
        public LogLotCustodyLedger LogLotCustodyLedger => logLotCustodyLedger;

        /// <summary>
        /// F7 yard-transfer wiring: adopts the sawmill-to-yard transfer
        /// driver as the hub's live holder. Single instance;
        /// re-registering the same driver is idempotent.
        /// </summary>
        public void RegisterSawmillToYardTransferDriver(SawmillToYardTransferDriver driver)
        {
            if (driver == null) return;
            sawmillToYardTransferDriver = driver;
        }

        /// <summary>F7 yard-transfer wiring: the live sawmill-to-yard transfer driver, or null when unwired.</summary>
        public SawmillToYardTransferDriver SawmillToYardTransferDriver => sawmillToYardTransferDriver;

        /// <summary>
        /// D3 (F-D2-1): adopts the smith-to-yard hardware transfer
        /// driver as the hub's live holder. Single instance;
        /// re-registering the same driver is idempotent.
        /// </summary>
        public void RegisterBlacksmithToYardHardwareTransferDriver(BlacksmithToYardHardwareTransferDriver driver)
        {
            if (driver == null) return;
            blacksmithToYardHardwareTransferDriver = driver;
        }

        /// <summary>D3 (F-D2-1): the live smith-to-yard hardware transfer driver, or null when unwired.</summary>
        public BlacksmithToYardHardwareTransferDriver BlacksmithToYardHardwareTransferDriver => blacksmithToYardHardwareTransferDriver;

        /// <summary>
        /// D3 (F-D2-2): adopts the lumber yard nails import driver as
        /// the hub's live holder. Single instance; re-registering the
        /// same driver is idempotent.
        /// </summary>
        public void RegisterLumberYardHardwareImportDriver(LumberYardHardwareImportDriver driver)
        {
            if (driver == null) return;
            lumberYardHardwareImportDriver = driver;
        }

        /// <summary>D3 (F-D2-2): the live lumber yard nails import driver, or null when unwired.</summary>
        public LumberYardHardwareImportDriver LumberYardHardwareImportDriver => lumberYardHardwareImportDriver;

        // Pass IV Worker 2: the unified reservation authority (persisted),
        // the hub-owned draft-power service (reserves through the authority),
        // and the equipment capacity service (6 capacity models).
        private EquipmentReservationService reservations;
        private DraftPowerService draftPower;
        private EquipmentCapacityService equipmentCapacities;

        /// <summary>
        /// Pass IV: the ONE reservation authority — person time, equipment,
        /// workstations, workspaces, lots, motive power, animals, capacity.
        /// Persisted via the save pipeline.
        /// </summary>
        public EquipmentReservationService Reservations => reservations ??= new EquipmentReservationService();

        /// <summary>Pass IV: the unified authority as its contract interface.</summary>
        public IUnifiedReservationAuthority ReservationAuthority => Reservations;

        /// <summary>
        /// Pass IV: the hub-owned draft-power service. It reserves animals and
        /// drivers through <see cref="Reservations"/>; its hold index persists
        /// alongside the authority's holds.
        /// </summary>
        public DraftPowerService DraftPower => draftPower ??= new DraftPowerService(Reservations);

        /// <summary>
        /// Pass IV: equipment capacity semantics (6 models). Seeded with the
        /// authored kind profiles; instance lookups fall back through kinds.
        /// </summary>
        public EquipmentCapacityService EquipmentCapacities =>
            equipmentCapacities ??= CreateEquipmentCapacityService();

        private EquipmentCapacityService CreateEquipmentCapacityService()
        {
            var service = new EquipmentCapacityService(Reservations);
            EquipmentConditionProfiles.RegisterAll(service);
            service.KindLookup = ResolveEquipmentKind;
            return service;
        }

        /// <summary>Pass IV: resolves an equipment key to its kind for kind-level profile fallback.</summary>
        private string ResolveEquipmentKind(string equipmentKey)
        {
            if (string.IsNullOrWhiteSpace(equipmentKey)) return null;
            if (equipmentKey.StartsWith(ReservationKeyConventions.AssetPrefix, StringComparison.Ordinal))
            {
                string assetId = equipmentKey.Substring(ReservationKeyConventions.AssetPrefix.Length);
                var asset = TransportAssets.Get(assetId);
                return asset?.Kind;
            }
            return null;
        }

        // P3 (F6 production wiring): the installed equipment gate, retained
        // so the sawmill production driver receives the same instance the
        // TaskAuthority holds — never a second gate.
        private EquipmentTaskGate installedEquipmentTaskGate;

        /// <summary>P3 (F6 production wiring): the installed equipment task gate, or null when no gate has been installed yet.</summary>
        public EquipmentTaskGate InstalledEquipmentTaskGate => installedEquipmentTaskGate;

        /// <summary>
        /// Pass IV: installs the equipment execution gate (Canon 4.1) on the
        /// hub's TaskAuthority. Suppliers are wired to hub-owned truth — the
        /// unified reservation authority, the wagon asset registry, and the
        /// TimeManager timeline when a clock is supplied. No phantom equipment:
        /// kinds with no production register refuse loudly until provisioned.
        /// Called by SimulationDrivers in the production driver path.
        /// </summary>
        public void InstallEquipmentTaskGate(Func<float> nowGameSeconds = null)
        {
            // P3 (F6 production wiring): idempotent — the bootstrapper and
            // SimulationDrivers.Awake both call this, and the driver must
            // hold the same gate instance the TaskAuthority holds. A second
            // gate would diverge on reservations for the same saw.
            if (installedEquipmentTaskGate != null) return;
            var gate = new EquipmentTaskGate
            {
                AssetsSupplier = CollectGateAssets,
                KitsSupplier = CollectGateKits,
                AccessResolver = new EquipmentAccessResolver(),
                ReservationsSupplier = () => Reservations,
                NowGameSecondsSupplier = nowGameSeconds,
                HandOccupancySupplier = PersonHands.SnapshotFor,
                // C-3: the workstation suppliers. Before this slice the
                // production gate never set them, so every workstation-coded
                // check refused "no workstation registry supplied" — only
                // the ThreeBusinessSlice fixture set them, on its own local
                // gate. The registry is keyed by holder id (the holder kind
                // is ignored); definitions come from the catalog; components
                // resolve from the gate's own asset collection; support is
                // the merge of every registered per-business context.
                WorkstationsFor = (holderKind, holderId) => WorkstationRegisterFor(holderId),
                FindWorkstationDefinition = WorkstationCatalog.Find,
                FindComponent = FindGateComponent,
                SupportSupplier = CollectGateSupport,
            };
            installedEquipmentTaskGate = gate;
            Tasks.SetExecutionGate(gate);
        }

        /// <summary>
        /// Pass IV: production equipment assets visible to the task gate —
        /// the wagon asset registry plus every hub-owned per-business
        /// equipment register (Slice S2). Kinds with no production
        /// register still refuse loudly until provisioned.
        /// </summary>
        private List<EquipmentAsset> CollectGateAssets()
        {
            var assets = new List<EquipmentAsset>();
            foreach (var asset in TransportAssets.Assets)
                if (asset != null) assets.Add(asset);
            if (equipmentRegisters != null)
                foreach (var register in equipmentRegisters.Values)
                {
                    if (register?.Assets == null) continue;
                    foreach (var asset in register.Assets)
                        if (asset != null) assets.Add(asset);
                }
            return assets;
        }

        /// <summary>
        /// Pass IV: production toolkits visible to the task gate. Slice S2:
        /// every hub-owned per-business equipment register contributes its
        /// kits; a business with no register still holds no kit, and kit
        /// requirements refuse loudly until kits are provisioned.
        /// </summary>
        private List<ToolKitInstance> CollectGateKits()
        {
            var kits = new List<ToolKitInstance>();
            if (equipmentRegisters != null)
                foreach (var register in equipmentRegisters.Values)
                {
                    if (register?.Kits == null) continue;
                    foreach (var kit in register.Kits)
                        if (kit != null) kits.Add(kit);
                }
            return kits;
        }

        /// <summary>
        /// C-3: resolves a workstation component asset id to the lightweight
        /// view the gate evaluates against — the wagon asset registry plus
        /// every hub-owned per-business equipment register, mirroring
        /// CollectGateAssets. Null when no asset carries the id, so the
        /// station evaluation counts it as not present.
        /// </summary>
        private WorkstationComponentView? FindGateComponent(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId)) return null;
            var asset = TransportAssets.Get(assetId);
            if (asset == null && equipmentRegisters != null)
                foreach (var register in equipmentRegisters.Values)
                {
                    if (register?.Assets == null) continue;
                    foreach (var candidate in register.Assets)
                        if (candidate != null && candidate.AssetId == assetId) { asset = candidate; break; }
                    if (asset != null) break;
                }
            if (asset == null) return null;
            return new WorkstationComponentView
            {
                AssetId = asset.AssetId,
                Kind = asset.Kind,
                Condition01 = asset.Condition01,
                IsUsable = asset.IsUsable,
            };
        }

        /// <summary>
        /// C-3: merges every registered per-business support context into
        /// one — consumable units summed, skills/capabilities/infrastructure
        /// unioned — for the gate's holder-blind SupportSupplier. Returns a
        /// fresh context (never a registered one), so the gate cannot mutate
        /// a business's authored state. Empty when nothing is registered.
        /// </summary>
        private SupportContext CollectGateSupport()
        {
            var merged = new SupportContext();
            if (businessSupport == null) return merged;
            foreach (var context in businessSupport.Values)
            {
                if (context == null) continue;
                if (context.ConsumableUnits != null)
                    foreach (var pair in context.ConsumableUnits)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                        merged.ConsumableUnits.TryGetValue(pair.Key, out int onHand);
                        merged.ConsumableUnits[pair.Key] = onHand + Math.Max(0, pair.Value);
                    }
                if (context.AvailableOperatorSkills != null)
                    foreach (string skill in context.AvailableOperatorSkills)
                        if (!string.IsNullOrWhiteSpace(skill) && !merged.AvailableOperatorSkills.Contains(skill))
                            merged.AvailableOperatorSkills.Add(skill);
                if (context.AvailableRepairCapabilities != null)
                    foreach (string capability in context.AvailableRepairCapabilities)
                        if (!string.IsNullOrWhiteSpace(capability) && !merged.AvailableRepairCapabilities.Contains(capability))
                            merged.AvailableRepairCapabilities.Add(capability);
                if (context.AvailableInfrastructure != null)
                    foreach (string infrastructure in context.AvailableInfrastructure)
                        if (!string.IsNullOrWhiteSpace(infrastructure) && !merged.AvailableInfrastructure.Contains(infrastructure))
                            merged.AvailableInfrastructure.Add(infrastructure);
            }
            return merged;
        }
        public FarmFlowLedger FarmFlows => farmFlows ??= new FarmFlowLedger();
        public BusinessCapabilityRegistry Capabilities => capabilityRegistry ??= new BusinessCapabilityRegistry();
        public BusinessOperatingLedger OperatingLedger => operatingLedger ??= new BusinessOperatingLedger();
        public EmploymentRelationshipRegistry Employments => employments ??= new EmploymentRelationshipRegistry();
        public FarmSliceSystems FarmSlice => farmSlice ??= new FarmSliceSystems();
        /// <summary>SWN-3: the business liability ledger (valuation-attached).</summary>
        public BusinessLiabilityLedger Liabilities
        {
            get
            {
                liabilityLedger ??= new BusinessLiabilityLedger();
                liabilityLedger.AttachValuation(Valuation);
                liabilityLedger.AttachFinancialAuthority(FinancialObligations);
                return liabilityLedger;
            }
        }

        /// <summary>
        /// Shared finance authority. Compatibility ledgers and instrument registries
        /// should project into this authority rather than own a second balance.
        /// </summary>
        public FinancialObligationAuthority FinancialObligations =>
            financialObligationAuthority ??= new FinancialObligationAuthority();

        /// <summary>Shared request/offer negotiation workflow over the finance authority.</summary>
        public CreditOfferWorkflow CreditOffers => creditOfferWorkflow ??= new CreditOfferWorkflow();

        /// <summary>Document/instrument index bound to the shared obligation authority.</summary>
        public CreditRegistry CreditInstruments
        {
            get
            {
                creditRegistry ??= new CreditRegistry();
                creditRegistry.AttachFinancialAuthority(FinancialObligations);
                return creditRegistry;
            }
        }

        private PostalService postalService;

        /// <summary>NX-2A: the postal network authority (offices, mail, contracts).</summary>
        public PostalService Postal => postalService ??= new PostalService();

        // P6: travel & communication authorities. The hub owns them so the
        // drivers (CLN-2) can advance them daily and the save pipeline (CLN-1)
        // can round-trip them. Scene bootstrap registers the actual content
        // (journey locations/edges, post offices, weather seed) — the hub
        // never invents geography.
        private JourneyModel journeyModel;
        private RouteConditionService routeConditions;
        private MoneyOrderService moneyOrderService;
        private RegisteredMailService registeredMailService;
        private RecruitmentService recruitmentService;
        private int weatherSeed;

        // Real People / shared-credit authorities introduced by the Muse
        // integration. They live here so runtime wiring and persistence use
        // the same instances; none of these services owns a second cash,
        // inventory, or debt ledger.
        private PersonScheduleTracker personScheduleTracker;
        private KnowledgeTransmissionStore knowledgeTransmissions;
        private WeeklyAttendanceAccumulator weeklyAttendance;
        private RentCollectionService rentCollectionService;
        private NpcFormationAssetRegister npcFormationAssetRegister;
        private NpcBusinessLifecycleService npcBusinessLifecycle;
        private NpcBusinessCreationAdapter npcBusinessCreation;
        private readonly List<NpcBusinessLifeRecord> npcBusinessLifeRecords = new List<NpcBusinessLifeRecord>();
        private NpcBusinessInvestigation npcBusinessInvestigation;
        private NpcOpportunityObservationLog npcOpportunityObservation;
        private NpcEntrepreneurshipDriver npcEntrepreneurshipDriver;
        private NpcBusinessEventLog npcBusinessEvents;
        private NpcPropertyLifeService npcPropertyLife;
        private ConstructionProjectExecutor constructionProjectExecutor;
        private readonly List<ConstructionProject> constructionProjects = new List<ConstructionProject>();
        private NpcConstructionCommissioner npcConstructionCommissioner;
        private ConstructionContractBook constructionContractBook;
        private ConstructionProgressBillingService constructionBilling;
        private TradeCreditBook tradeCreditBook;
        private CreditEventLog creditEventLog;
        private CreditCashBridge creditCashBridge;

        // Autonomous Labor pass: labor-market authorities. Plain C# services
        // owned by the hub; none owns a second cash, inventory, or debt ledger.
        // DelegationService is an orphan rescue — it existed with tests but was
        // never registered or driven.
        private ContactGraphService contactGraphService;
        private EmploymentNegotiationService employmentNegotiationService;
        private LaborDemandAssessor laborDemandAssessor;
        private WorkScheduler workScheduler;
        private WageSettlementService wageSettlementService;
        private ManagerBrain managerBrain;
        private ManagerDecisionExecutionService managerDecisionExecution;
        private LaborMigrationLinker laborMigrationLinker;
        private DelegationService delegationService;
        private TenancyPaymentService tenancyPaymentService;
        private LodgingRegistry lodgingRegistry;

        /// <summary>
        /// Pass IV (Worker 1): deterministic due-event scheduler (task completions,
        /// passive processes, deadlines). Absolute game seconds on the TimeManager
        /// timeline; no second clock. Save-persisted.
        /// </summary>
        private DueEventScheduler dueEventScheduler;

        // Pass III land core (Worker A): legal parcel/holding identity, the single
        // non-title rights store, and the atomic subdivision coordinator. None owns
        // a second cash, debt, or title ledger — title stays in TitleAuthority.
        private ParcelService parcelService;
        private PropertyInterestStore propertyInterestStore;
        private ParcelSubdivisionService parcelSubdivisionService;
        private HouseholdDecisionService householdDecisionService;
        private HouseholdDecisionDriver householdDecisionDriver;
        private HouseholdLenderDirectory householdLenderDirectory;
        private BusinessLenderService businessLenderService;
        private BankLedgerBindingRegistry bankLedgerBindings;
        private DomesticLaborService domesticLaborService;
        private HouseholdAutonomyDriver householdAutonomyDriver;
        private DomesticLaborDriver domesticLaborDriver;
        private SleepTickDriver sleepTickDriver;
        private MealTickDriver mealTickDriver;
        private HouseholdErrandCoordinator householdErrandCoordinator;
        private HousingSearchNeedRegistry housingSearchNeedRegistry;
        private HousingAuthority housingAuthority;
        private readonly List<BoardingHouseShopRuntime> boardingHouseRuntimes = new List<BoardingHouseShopRuntime>();

        /// <summary>P6: the JRN-1 journey model — the one routed distance/time authority.</summary>
        public JourneyModel Journeys
        {
            get
            {
                journeyModel ??= new JourneyModel();
                // Awake is not guaranteed to have run when an authority is first
                // accessed from EditMode tests, early scene bootstrap, or a lazy
                // save/load path. Keep the non-serialized route policy attached at
                // the ownership boundary every time the model is obtained.
                journeyModel.ConditionProvider = RouteConditions;
                return journeyModel;
            }
        }

        /// <summary>
        /// P6: NX-2C weather/route conditions. Wired as the journey model's
        /// condition provider on Awake and after every load (the provider is
        /// not serialized — re-attachment is the hub's job).
        /// </summary>
        public RouteConditionService RouteConditions => routeConditions ??= new RouteConditionService();

        /// <summary>P6: D4E postal money-order books (bound to the hub's postal service).</summary>
        public MoneyOrderService MoneyOrders => moneyOrderService ??= new MoneyOrderService(Postal);

        /// <summary>P6: D4F registered-mail custody chain (subscribes to the hub's postal handoffs).</summary>
        public RegisteredMailService RegisteredMail => registeredMailService ??= new RegisteredMailService(Postal);

        /// <summary>P6: T2B recruitment workflows (efforts, inquiries, letters).</summary>
        public RecruitmentService Recruiting => recruitmentService ??= new RecruitmentService();

        private CivicTreasuryService civicTreasury;
        private SharedCategoryStockLedger stockProvenance;

        /// <summary>
        /// Pass IX WS6 (A1-11): the ONE civic cash store — receives property
        /// taxes (player and NPC legs), pays the schoolhouse teacher. Owned
        /// here like every other authority; persisted via the systems DTO.
        /// </summary>
        public CivicTreasuryService CivicTreasury => civicTreasury ??= new CivicTreasuryService();

        /// <summary>Pass IX WS6: restores the civic treasury from its save section (clear-then-load inside the service).</summary>
        public void LoadCivicTreasuryFromSaveDto(CivicTreasuryService.CivicTreasurySaveDto dto)
        {
            CivicTreasury.LoadFromSaveDto(dto);
        }

        /// <summary>
        /// Pass IX WS6 (IDENT-001): boundary provenance for shared category
        /// stock — production outputs and external arrivals recorded here;
        /// interior category movements remain bare counts (phased remainder,
        /// stated on SharedCategoryStockLedger).
        /// </summary>
        public SharedCategoryStockLedger StockProvenance => stockProvenance ??= new SharedCategoryStockLedger();

        private ScenarioObjectiveService scenarioObjectives;

        /// <summary>
        /// Pass IX WS6 (A1-03): scenario objective/failure state — dated
        /// qualifiers fail the run exactly once into the persisted run
        /// state (SystemsSaveDto.scenarioRunState).
        /// </summary>
        public ScenarioObjectiveService ScenarioObjectives => scenarioObjectives ??= new ScenarioObjectiveService();

        public PersonScheduleTracker PersonSchedules => personScheduleTracker ??= new PersonScheduleTracker();

        /// <summary>
        /// Pass VII Workstream E: the minimal person belief/knowledge store.
        /// Only transmitted packets are knowable; rumors stay uncertain.
        /// Save-persisted.
        /// </summary>
        public KnowledgeTransmissionStore KnowledgeTransmissions =>
            knowledgeTransmissions ??= new KnowledgeTransmissionStore();

        /// <summary>
        /// Pass IX (A2-05): the production weekly attendance accumulator.
        /// Hub-owned so it rides the systems save DTO; SimulationDrivers
        /// feeds it daily and builds payroll's attendance provider from it.
        /// </summary>
        public WeeklyAttendanceAccumulator WeeklyAttendance =>
            weeklyAttendance ??= new WeeklyAttendanceAccumulator();

        public RentCollectionService RentCollection => rentCollectionService ??= new RentCollectionService();
        public NpcFormationAssetRegister NpcFormationAssets => npcFormationAssetRegister ??= new NpcFormationAssetRegister();
        public NpcBusinessLifecycleService NpcBusinessLifecycle => npcBusinessLifecycle ??= new NpcBusinessLifecycleService();
        public NpcBusinessCreationAdapter NpcBusinessCreation => npcBusinessCreation ??= new NpcBusinessCreationAdapter(Ids, Capabilities);
        public IList<NpcBusinessLifeRecord> NpcBusinessLifeRecords => npcBusinessLifeRecords;
        public NpcBusinessInvestigation NpcBusinessInvestigation => npcBusinessInvestigation ??= new NpcBusinessInvestigation();
        public NpcOpportunityObservationLog NpcOpportunityObservation => npcOpportunityObservation ??= new NpcOpportunityObservationLog();

        /// <summary>
        /// Pass V P2-3: the autonomous opportunity-evaluation driver —
        /// observation -> recognition -> investigation -> formation, no quotas.
        /// Owns the per-person observation logs and investigations.
        /// </summary>
        public NpcEntrepreneurshipDriver NpcEntrepreneurship =>
            npcEntrepreneurshipDriver ??= new NpcEntrepreneurshipDriver();
        public NpcBusinessEventLog NpcBusinessEvents => npcBusinessEvents ??= new NpcBusinessEventLog();
        public NpcPropertyLifeService NpcPropertyLife => npcPropertyLife ??= new NpcPropertyLifeService();
        public ConstructionProjectExecutor ConstructionExecutor => constructionProjectExecutor ??= new ConstructionProjectExecutor();
        public IList<ConstructionProject> ConstructionProjects => constructionProjects;
        public NpcConstructionCommissioner NpcConstructionCommissioner => npcConstructionCommissioner ??= new NpcConstructionCommissioner();
        /// <summary>Plan 26 (Q18): the construction contract book, hub-held.</summary>
        public ConstructionContractBook ConstructionContractBook =>
            constructionContractBook ??= new ConstructionContractBook();
        /// <summary>Plan 26 (Q18, note 102 C6): the progress-billing service, hub-held with a null cash port until Q17 wires a real production port; the service refuses loudly on a null port, never throws.</summary>
        public ConstructionProgressBillingService ConstructionBilling =>
            constructionBilling ??= new ConstructionProgressBillingService(null);
        public TradeCreditBook TradeCredit => tradeCreditBook ??= new TradeCreditBook();
        public CreditEventLog CreditEvents => creditEventLog ??= new CreditEventLog();
        /// <summary>
        /// Transient binding layer for finance workflows. The bridge never owns
        /// balances; it is repopulated from the authoritative cash stores after
        /// boot/load.
        /// </summary>
        public CreditCashBridge CreditCash => creditCashBridge ??= new CreditCashBridge();

        /// <summary>
        /// Autonomous Labor: known-contacts authority for recruitment.
        /// Only documented links yield candidates — never an omniscient list.
        /// </summary>
        public ContactGraphService ContactGraph => contactGraphService ??= new ContactGraphService();

        /// <summary>
        /// Autonomous Labor: negotiated employment offers (offer/counter/
        /// accept/refuse/withdraw/expire). Authority predicate = business owner
        /// (via <see cref="IsBusinessOwner"/>) or HireStaff delegation.
        /// </summary>
        public EmploymentNegotiationService EmploymentNegotiation =>
            employmentNegotiationService ??= new EmploymentNegotiationService(OfferAuthorityPredicate);

        /// <summary>Autonomous Labor: workload-derived labor demand (read-only).</summary>
        public LaborDemandAssessor LaborDemand => laborDemandAssessor ??= new LaborDemandAssessor();

        /// <summary>Autonomous Labor: assigns queued work to employed persons.</summary>
        public WorkScheduler WorkScheduling => workScheduler ??= new WorkScheduler();

        /// <summary>Autonomous Labor: idempotency-guarded wage settlement.</summary>
        public WageSettlementService WageSettlement => wageSettlementService ??= new WageSettlementService();

        /// <summary>Autonomous Labor: delegated-manager decision loop.</summary>
        public ManagerBrain Managers => managerBrain ??= new ManagerBrain();

        /// <summary>
        /// Pass VII Workstream D: executes manager decisions through real
        /// production authorities. Configured by the driver with the hub's
        /// authorities; the ledger is save-persisted.
        /// </summary>
        public ManagerDecisionExecutionService ManagerDecisionExecution =>
            managerDecisionExecution ??= new ManagerDecisionExecutionService();

        /// <summary>Autonomous Labor: bounded employment-linked migration evaluation.</summary>
        public LaborMigrationLinker MigrationLinker => laborMigrationLinker ??= new LaborMigrationLinker();

        /// <summary>
        /// Autonomous Labor: orphan rescue. DelegationService shipped with tests
        /// but was never registered or driven; it is owned here now.
        /// </summary>
        public DelegationService Delegation => delegationService ??= new DelegationService();

        /// <summary>
        /// Pass III Worker A: legal parcel/holding identity + plot linkage
        /// (ParcelRecord/HoldingRecord). Ownership truth stays in TitleAuthority;
        /// this service never answers "who owns it".
        /// </summary>
        public ParcelService Parcels => parcelService ??= new ParcelService();

        /// <summary>Attaches the existing world grid and title authority to land services.</summary>
        public void AttachParcelWorld(TownWorldController world, TitleAuthority titles)
        {
            if (world == null) return;
            Parcels.Grid = world.Grid;
            ParcelSubdivisions.BuildingIndex = new TownWorldParcelBuildingIndex(world);
            if (titles != null)
                ParcelSubdivisions.TitleHolderLookup = titles.CurrentHolder;
        }

        /// <summary>
        /// Pass III Worker A: the single non-title rights store
        /// (lease/easement/charge/entry/ROW/restriction/dispute/lien).
        /// </summary>
        public PropertyInterestStore PropertyInterests => propertyInterestStore ??= new PropertyInterestStore();

        /// <summary>
        /// Pass III Worker A: atomic subdivision coordinator, wired to the parcel
        /// and interest authorities. Game wiring (Codex) sets BuildingIndex from
        /// PlacedBuilding footprints and TitleHolderLookup from the title chain.
        /// </summary>
        public ParcelSubdivisionService ParcelSubdivisions =>
            parcelSubdivisionService ??= new ParcelSubdivisionService(Parcels, PropertyInterests);

        /// <summary>
        /// Pass III Worker B: the general rent/board payment loop. Owned by
        /// the hub like the other Pass II/III plain-C# services; game wiring
        /// (Codex) drives SettleDueRents once per simulated day and supplies
        /// the ITenancyPayeeDirectory.
        /// </summary>
        public TenancyPaymentService TenancyPayments => tenancyPaymentService ??= new TenancyPaymentService();

        /// <summary>
        /// Pass VIII Worker 2: hub-owned lodging registry — which business
        /// rooms are registered as real accommodation. Boarding check-in
        /// refuses unregistered rooms; closure ends registrations. Owned by
        /// the hub so registration survives with the save.
        /// </summary>
        public LodgingRegistry LodgingRegistry => lodgingRegistry ??= new LodgingRegistry();

        /// <summary>
        /// Pass IV (Worker 1): the deterministic due-event scheduler. Drivers pop due
        /// events in due order before advancing; the schedule is save-persisted.
        /// </summary>
        public DueEventScheduler DueEvents => dueEventScheduler ??= new DueEventScheduler();
        /// Pass III Worker C: household financial pressure -> bounded decision
        /// (pure evaluator; the autonomy driver maps real state onto its input).
        /// </summary>
        public HouseholdDecisionService HouseholdDecisions =>
            householdDecisionService ??= new HouseholdDecisionService();

        /// <summary>
        /// Pass III Worker C: turns household decisions into real operations
        /// (opportunity discovery, housing search, obligations, negotiation
        /// requests, spending policies). Owns the opportunity log.
        /// </summary>
        public HouseholdDecisionDriver HouseholdDecisionDriver =>
            householdDecisionDriver ??= new HouseholdDecisionDriver(
                ContactGraph, Employments, HousingSearchNeeds, FinancialObligations, Ids, CreditCash);

        /// <summary>
        /// Pass V P2-2: durable registry of private individuals willing to
        /// lend to households. World generation registers wealthy NPCs here
        /// with documented capital; the autonomy driver resolves per-household
        /// facilities from it. Empty by default: no guaranteed credit.
        /// </summary>
        public HouseholdLenderDirectory HouseholdLenders
        {
            get
            {
                if (householdLenderDirectory == null)
                {
                    householdLenderDirectory = new HouseholdLenderDirectory();
                    householdLenderDirectory.BindHubServices(
                        CreditOffers, FinancialObligations, CreditCash, Ids, CreditEvents);
                }

                return householdLenderDirectory;
            }
        }

        /// <summary>
        /// Pass VIII (6.3): the live production business lender — the town
        /// bank as a real formal-bank credit participant. Implements
        /// IManagerBusinessLender so manager Borrow decisions reach a funded
        /// lender through the existing delegation path. Unchartered by
        /// default: no capital, no credit — the driver charters it once with
        /// documented capital.
        /// </summary>
        public BusinessLenderService BusinessLender
        {
            get
            {
                if (businessLenderService == null)
                {
                    businessLenderService = new BusinessLenderService();
                    businessLenderService.BindHubServices(
                        Ids, FinancialObligations, CreditOffers, CreditEvents, Valuation);
                }

                return businessLenderService;
            }
        }

        /// <summary>
        /// Pass XI WS2 (A-06): the single production home for the bank
        /// ledger binding registry. The registry holds no balances and no
        /// bindings by itself: a future scene step binds a player-owned
        /// bank's ledger through SimulationDrivers.BindBankLedger (one
        /// ledger per bank, fail-loud on a second). Until that step lands
        /// the registry is empty by design, not by omission.
        /// </summary>
        public BankLedgerBindingRegistry BankLedgerBindings
        {
            get
            {
                if (bankLedgerBindings == null)
                    bankLedgerBindings = new BankLedgerBindingRegistry();
                return bankLedgerBindings;
            }
        }

        /// <summary>
        /// Pass III Worker C: narrow domestic labor (meal prep, fuel, animal
        /// care, chores) on the existing TaskAuthority + person-time.
        /// </summary>
        public DomesticLaborService DomesticLabor =>
            domesticLaborService ??= new DomesticLaborService();

        /// <summary>
        /// Pass III Worker C: NPC autonomous household responses (brief §13) —
        /// declining reserves, rent due, job loss, housing change.
        /// </summary>
        public HouseholdAutonomyDriver HouseholdAutonomy =>
            householdAutonomyDriver ??= new HouseholdAutonomyDriver();

        /// <summary>
        /// Pass V drive-by: the domestic labor day driver (meal prep, fuel,
        /// animal care, chores) — previously tests-only. Hub-owned and
        /// persisted; SimulationDrivers runs it daily.
        /// </summary>
        public DomesticLaborDriver DomesticLaborDriver =>
            domesticLaborDriver ??= new DomesticLaborDriver();

        /// <summary>
        /// W-G2 (Agent G, expanded campaign 2026-11-16): the sleep tick
        /// driver (22:00 movement to the reserved place, hold through the
        /// night, 07:00 release with rest quality recorded). Hub-owned
        /// and transient — its per-night phase re-derives from the clock
        /// and the live reservations, so nothing about it is persisted;
        /// SimulationDrivers runs it on the short tick.
        /// </summary>
        public SleepTickDriver SleepTickDriver =>
            sleepTickDriver ??= new SleepTickDriver();

        /// <summary>
        /// G-4 (Agent G, expanded campaign 2026-11-16): the meal tick
        /// driver (meal-window movement to the reserved seat, hold
        /// through the meal, release at the window end; Fallback meals
        /// eat in place). Hub-owned and transient — its per-meal phase
        /// re-derives from the clock and the live reservations, so
        /// nothing about it is persisted; SimulationDrivers runs it on
        /// the short tick.
        /// </summary>
        public MealTickDriver MealTickDriver =>
            mealTickDriver ??= new MealTickDriver();

        /// <summary>
        /// Pass V P2-1: the household errand coordinator — batched shopping
        /// plans (HouseholdErrandPlan) with claims, reservations, and
        /// reassignment. Hub-owned and persisted.
        /// </summary>
        public HouseholdErrandCoordinator HouseholdErrands =>
            householdErrandCoordinator ??= new HouseholdErrandCoordinator();

        /// <summary>
        /// Priority U-3: the production shopping loop, owned by
        /// SimulationDrivers and attached here (see
        /// BuildHouseholdShoppingLoop) so the hub's save pipeline can carry
        /// the loop's in-transit custody. Null until attached: a scene
        /// without a shopping loop saves/loads empty custody, never an
        /// error. The hub never constructs the loop — the loop needs the
        /// population manager, memberships, and supplier directory the
        /// drivers own.
        /// </summary>
        public HouseholdShoppingLoop ShoppingLoop { get; set; }

        /// <summary>
        /// Slice 25: the production shopping driver, owned by
        /// SimulationDrivers (see BuildHouseholdShoppingLoop). The hub
        /// never constructs the driver; it only holds the reference for
        /// save/load, exactly like ShoppingLoop.
        ///
        /// Campaign C (DEF-028): the driver is attached by
        /// BuildHouseholdShoppingLoop, which runs after the hub load on a
        /// fresh-scene Continue — so the hub load cannot assume the
        /// driver is present. A trip list that arrives while no driver
        /// is attached is HELD in pendingShoppingTrips, not dropped, and
        /// the attach drains it into the driver (LoadFromSaveDto,
        /// clear-then-load; the driver is freshly constructed by the
        /// same BuildHouseholdShoppingLoop call, so it holds no live
        /// trips the drain could wipe). The drain runs before
        /// SimulationDrivers.Rebind rehydrates, so a deferred trip is
        /// rehydrated exactly like a directly loaded one.
        /// </summary>
        private ShoppingTripDriver shoppingTripDriver;
        private List<PlannedShoppingTripSaveDto> pendingShoppingTrips;

        /// <summary>Campaign C (DEF-028): trips held for a driver that has not attached yet; 0 when none are pending.</summary>
        public int PendingShoppingTripCount => pendingShoppingTrips != null ? pendingShoppingTrips.Count : 0;

        public ShoppingTripDriver ShoppingTripDriver
        {
            get => shoppingTripDriver;
            set
            {
                shoppingTripDriver = value;
                if (shoppingTripDriver != null && pendingShoppingTrips != null && pendingShoppingTrips.Count > 0)
                {
                    List<PlannedShoppingTripSaveDto> deferred = pendingShoppingTrips;
                    pendingShoppingTrips = null;
                    shoppingTripDriver.LoadFromSaveDto(deferred);
                }
            }
        }

        /// <summary>
        /// Pass III Worker C: hub-owned housing-search registry used by the
        /// RelocateCheaper decision path.
        /// </summary>
        public HousingSearchNeedRegistry HousingSearchNeeds =>
            housingSearchNeedRegistry ??= new HousingSearchNeedRegistry();

        /// <summary>
        /// Pass III property authority owned by the hub. Housing agreements,
        /// occupancies and functional spaces are real persisted state; the
        /// daily tenancy payment service consumes this authority rather than
        /// maintaining a parallel rent register.
        /// </summary>
        public HousingAuthority Housing => housingAuthority ??= new HousingAuthority();
        public IList<BoardingHouseShopRuntime> BoardingHouseRuntimes => boardingHouseRuntimes;

        public void RegisterBoardingHouseRuntime(BoardingHouseShopRuntime runtime)
        {
            if (runtime == null || boardingHouseRuntimes.Contains(runtime)) return;
            boardingHouseRuntimes.Add(runtime);
        }

        /// <summary>
        /// Autonomous Labor: owner lookup hook for the offer authority predicate.
        /// Game wiring (Codex) sets this from the business registry. When unset,
        /// offers require HireStaff delegation.
        /// </summary>
        [NonSerialized]
        public Func<string, int, bool> IsBusinessOwner;

        /// <summary>
        /// Autonomous Labor: current absolute day index for authority checks.
        /// Set daily by SimulationDrivers; defaults to 0 (delegations with
        /// day bounds should not be evaluated before the first drive).
        /// </summary>
        public int LaborDayIndex { get; set; }

        private bool OfferAuthorityPredicate(string businessInstanceId, int personId, string act)
        {
            if (IsBusinessOwner != null)
            {
                try
                {
                    if (IsBusinessOwner(businessInstanceId, personId)) return true;
                }
                catch (Exception)
                {
                    // Owner hook failed — fall through to delegation check.
                }
            }
            if (!string.Equals(act, "HireStaff", StringComparison.OrdinalIgnoreCase)) return false;
            var diag = new List<string>();
            return Delegation.CheckAct(
                businessInstanceId, personId, DelegatedAct.HireStaff, 0, LaborDayIndex, diag)
                == AuthorityVerdict.Permitted;
        }

        /// <summary>
        /// P6: weather seed for the route-condition service. Bootstrap-assigned
        /// from the world's canonical seed (deterministic weather); persisted
        /// in the save DTO so a loaded game continues the same weather stream.
        /// </summary>
        public int WeatherSeed
        {
            get => weatherSeed;
            set => weatherSeed = Math.Max(0, value);
        }

        private AgriculturalRiskService riskService;
        private LivestockDiseaseService diseaseService;

        /// <summary>NX-2B: agricultural disasters (grasshoppers, prairie fire, drought).</summary>
        public AgriculturalRiskService Risk => riskService ??= new AgriculturalRiskService();

        /// <summary>NX-2B: ranch-localized livestock disease outbreaks.</summary>
        public LivestockDiseaseService Disease => diseaseService ??= new LivestockDiseaseService();

        public IReadOnlyList<ButcherRuntime> ButcherRuntimes => butcherRuntimes;

        /// <summary>
        /// P2: W2B/W2C/W3B nutrition-link registries. Restaurant, boarding-house
        /// and hotel shop runtimes (or their meal-day ledgers) register their
        /// served-meal sources here so the daily-needs driver can count real
        /// prepared meals (Canon §2.5) instead of double-feeding households
        /// whose members genuinely ate out. Transient: the ledgers persist via
        /// their owning runtimes' own save DTOs; owners re-register after load.
        /// Empty registries mean no meals are counted — never estimates.
        /// </summary>
        private readonly List<IRestaurantMealDaySource> restaurantMealSources = new List<IRestaurantMealDaySource>();
        private readonly List<IBoardingHouseMealDaySource> boardingHouseMealSources = new List<IBoardingHouseMealDaySource>();
        private readonly List<IHotelMealDaySource> hotelMealSources = new List<IHotelMealDaySource>();

        public IReadOnlyList<IRestaurantMealDaySource> RestaurantMealSources => restaurantMealSources;
        public IReadOnlyList<IBoardingHouseMealDaySource> BoardingHouseMealSources => boardingHouseMealSources;
        public IReadOnlyList<IHotelMealDaySource> HotelMealSources => hotelMealSources;

        public void RegisterRestaurantMealSource(IRestaurantMealDaySource source)
        {
            if (source != null && !restaurantMealSources.Contains(source))
            {
                restaurantMealSources.Add(source);
            }
        }

        public void RegisterBoardingHouseMealSource(IBoardingHouseMealDaySource source)
        {
            if (source != null && !boardingHouseMealSources.Contains(source))
            {
                boardingHouseMealSources.Add(source);
            }
        }

        /// <summary>
        /// P2: builds the per-day meal-source composites the daily-needs
        /// service consumes. Empty registries yield empty composites (which
        /// report zero meals — identical to passing null). Public + static so
        /// EditMode tests can verify composition without a scene.
        /// </summary>
        public static void BuildMealSourceComposites(
            IReadOnlyList<IRestaurantMealDaySource> restaurantSources,
            IReadOnlyList<IBoardingHouseMealDaySource> boardingSources,
            IReadOnlyList<IHotelMealDaySource> hotelSources,
            out CompositeRestaurantMealSource restaurants,
            out CompositeBoardingHouseMealSource boardingHouses,
            out CompositeHotelMealSource hotels)
        {
            restaurants = new CompositeRestaurantMealSource(restaurantSources);
            boardingHouses = new CompositeBoardingHouseMealSource(boardingSources);
            hotels = new CompositeHotelMealSource(hotelSources);
        }

        private void Awake()
        {
            idRegistry ??= new EntityIdRegistry();
            animalRegistry ??= new AnimalRegistry(idRegistry);
            householdLedgers ??= new HouseholdLedgerRegistry();
            taskAuthority ??= new TaskAuthority();
            skillService ??= new SkillService();
            workTimeBudgets ??= new WorkTimeBudgetStore();
            valuation ??= new EnterpriseValuationReadModel();
            freightPool ??= new FreightResourcePool();
            transportAssets ??= new TransportAssetRegistry();
            // Pass IV Worker 2: hub-owned reservation + capacity authorities.
            // The freight pool shares the hub's draft-power service (one animal/
            // driver truth) and reconciles its wagons onto the asset registry
            // (one wagon condition/reservation truth).
            reservations ??= new EquipmentReservationService();
            draftPower ??= new DraftPowerService(reservations);
            equipmentCapacities ??= CreateEquipmentCapacityService();
            freightPool.AttachDraftPower(draftPower);
            freightPool.AttachWagonRegistry(transportAssets);
            var wagonDiag = new List<string>();
            freightPool.MigrateWagonsToRegistry(wagonDiag);
            butcherRuntimes ??= new List<ButcherRuntime>();
            farmFlows ??= new FarmFlowLedger();
            capabilityRegistry ??= new BusinessCapabilityRegistry();
            operatingLedger ??= new BusinessOperatingLedger();
            employments ??= new EmploymentRelationshipRegistry();
            farmSlice ??= new FarmSliceSystems();
            liabilityLedger ??= new BusinessLiabilityLedger();
            liabilityLedger.AttachValuation(valuation);
            financialObligationAuthority ??= new FinancialObligationAuthority();
            liabilityLedger.AttachFinancialAuthority(financialObligationAuthority);
            creditRegistry ??= new CreditRegistry();
            creditRegistry.AttachFinancialAuthority(financialObligationAuthority);
            PlayerDebtManager playerDebt = FindAnyObjectByType<PlayerDebtManager>();
            playerDebt?.AttachFinancialAuthority(financialObligationAuthority, idRegistry);
            postalService ??= new PostalService();
            // P6: travel & communication authorities. The condition provider is
            // not serialized — (re-)attachment is the hub's job, here and after
            // every load.
            journeyModel ??= new JourneyModel();
            routeConditions ??= new RouteConditionService();
            journeyModel.ConditionProvider = routeConditions;
            moneyOrderService ??= new MoneyOrderService(postalService);
            registeredMailService ??= new RegisteredMailService(postalService);
            recruitmentService ??= new RecruitmentService();
            riskService ??= new AgriculturalRiskService();
            diseaseService ??= new LivestockDiseaseService();
            personScheduleTracker ??= new PersonScheduleTracker();
            dueEventScheduler ??= new DueEventScheduler();
            rentCollectionService ??= new RentCollectionService();
            npcFormationAssetRegister ??= new NpcFormationAssetRegister();
            npcBusinessLifecycle ??= new NpcBusinessLifecycleService();
            npcBusinessCreation ??= new NpcBusinessCreationAdapter(Ids, Capabilities);
            npcBusinessInvestigation ??= new NpcBusinessInvestigation();
            npcOpportunityObservation ??= new NpcOpportunityObservationLog();
            npcBusinessEvents ??= new NpcBusinessEventLog();
            npcPropertyLife ??= new NpcPropertyLifeService();
            constructionProjectExecutor ??= new ConstructionProjectExecutor();
            npcConstructionCommissioner ??= new NpcConstructionCommissioner();
            constructionContractBook ??= new ConstructionContractBook();
            constructionBilling ??= new ConstructionProgressBillingService(null);
            tradeCreditBook ??= new TradeCreditBook();
            creditEventLog ??= new CreditEventLog();
            creditCashBridge ??= new CreditCashBridge();
            liabilityLedger.SyncAllToValuation(null);

            // P1: boot-time task-definition registration. Every static task catalog
            // registers its definitions here — before this pass, NO production caller
            // ever registered any catalog, so the first CreateTask for freight,
            // travel, farm, or shop work threw "Unknown TaskDefinitionId".
            // Registration is idempotent (the authority rejects duplicates).
            RegisterTaskCatalogs(taskAuthority, skillService, postalService, null);
        }

        /// <summary>
        /// Priority 4 Slice 4 — installs the task-duration diagnostic on
        /// the hub's TaskAuthority (plan §5 Slice 4 item 1). The clock is
        /// supplied by the caller (production: SimulationDrivers passes
        /// <c>() =&gt; timeManager.AbsoluteGameSeconds</c> — the hub owns no
        /// clock). Installing twice returns the existing service and
        /// re-points its clock at the latest caller's clock (the delegate
        /// reads the driver's live timeManager field, so it stays current);
        /// it never installs a second observer.
        /// The service's log sink defaults to Debug.LogWarning. Evidence
        /// lookups fed here: L5 due times from the hub-owned
        /// DueEventScheduler (read-only, TryGetDueGameSeconds); L3
        /// sleep/rest windows from the hub-owned PersonScheduleTracker
        /// (ActiveFor, Sleeping/Resting only — the two reservations
        /// DailyActivityPlanner writes for a wrapping sleep). L1 supplier
        /// arrivals are NOT fed: no single hub-held seam holds the order
        /// lists (notions orders live inside BakeryWholesaleDailyDriver's
        /// per-day inputs, StoreProcurement orders inside the procurement
        /// drivers), so SupplierArrivalLookup stays null and L1 evidence
        /// comes from DependencyDescription text alone (plan §7 C-8).
        /// A freshly installed service rebuilds its records from the
        /// authority's open tasks (§2.5; a no-op on first boot, the
        /// restore path for a fresh hub after a load).
        /// AutonomousSocietyDriver's own TaskAuthority is NOT installed:
        /// it is legacy — nothing in production constructs it (plan §7
        /// C-1), so installing there would observe nothing live.
        /// </summary>
        public TaskDurationDiagnostics InstallTaskDurationDiagnostics(Func<float> nowSeconds)
        {
            if (taskDurationDiagnostics == null)
            {
                taskDurationDiagnostics = TaskDurationDiagnosticsWiring.Install(Tasks, nowSeconds);
                if (taskDurationDiagnostics == null)
                {
                    return null;
                }

                taskDurationDiagnostics.DueEventDueTimeLookup = eventId =>
                {
                    float due;
                    return DueEvents.TryGetDueGameSeconds(eventId, out due) ? (float?)due : null;
                };
                taskDurationDiagnostics.ScheduleLookup = (personId, dayIndex) =>
                {
                    var windows = new List<TaskDurationSleepWindow>();
                    foreach (PersonActivityReservation reservation in PersonSchedules.ActiveFor(personId, dayIndex))
                    {
                        if (reservation != null
                            && (reservation.Activity == PersonActivityKind.Sleeping
                                || reservation.Activity == PersonActivityKind.Resting))
                        {
                            windows.Add(new TaskDurationSleepWindow
                            {
                                StartMinuteOfDay = reservation.StartMinuteOfDay,
                                DurationMinutes = reservation.DurationMinutes,
                            });
                        }
                    }

                    return windows;
                };
                taskDurationDiagnostics.RebuildFromAuthority(Tasks);
            }
            else if (nowSeconds != null)
            {
                Tasks.SetTimingClock(nowSeconds);
            }

            return taskDurationDiagnostics;
        }

        /// <summary>
        /// P1: registers every static task catalog with the hub's TaskAuthority.
        /// Public + static so EditMode tests can verify coverage without a scene.
        /// </summary>
        public static void RegisterTaskCatalogs(
            TaskAuthority tasks,
            SkillService skills,
            PostalService postal,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (tasks == null)
            {
                diagnostics.Add("RegisterTaskCatalogs: no TaskAuthority — task definitions not registered.");
                return;
            }

            // Shop/service catalogs.
            GeneralStoreTaskCatalog.RegisterAll(tasks);
            Pass2TaskCatalogs.RegisterWholesaleShelfMove(tasks);
            BakeryBreadCatalog.RegisterAll(tasks);
            BarberServiceCatalog.RegisterAll(tasks);
            DoctorTreatmentCatalog.RegisterAll(tasks);
            NewspaperTaskCatalog.RegisterAll(tasks);
            RestaurantMealCatalog.RegisterAll(tasks);
            TailorGarmentCatalog.RegisterAll(tasks);

            // Mine + mill catalogs.
            MineAssayTaskCatalog.Register(tasks, diagnostics);
            MineHoistingTaskCatalog.Register(tasks, diagnostics);
            MineLaborRegister.MineLaborTaskCatalog.Register(tasks, diagnostics);
            MineShaftTaskCatalog.Register(tasks, diagnostics);
            GrainMillMaintenanceTasks.RegisterTaskDefinitions(tasks, diagnostics);
            SawmillMaintenanceTasks.RegisterTaskDefinitions(tasks, diagnostics);
            LoggingTaskDefinitions.RegisterTaskDefinitions(tasks, skills, diagnostics);

            // Farming chains.
            CropChain.RegisterTaskDefinitions(tasks);
            Miller.RegisterTaskDefinitions(tasks);
            CheeseChain.RegisterTaskDefinitions(tasks);
            CreameryLabor.RegisterTaskDefinitions(tasks);
            DairyChain.RegisterTaskDefinitions(tasks);
            FarmConstruction.RegisterTaskDefinitions(tasks);
            FeedLoop.RegisterTaskDefinitions(tasks);
            PoultryChain.RegisterTaskDefinitions(tasks);
            PigSheepChain.RegisterTaskDefinitions(tasks);
            TimberHarvest.RegisterTaskDefinitions(tasks);
            Sawmill.RegisterTaskDefinitions(tasks);

            // Travel + freight + post.
            JourneyTravel.RegisterTaskDefinitions(tasks);
            FreightCompanyRuntime.RegisterTaskDefinitions(tasks);
            postal?.RegisterTaskDefinitions(tasks);
            DomesticTaskCatalog.RegisterAll(tasks, diagnostics);

            diagnostics.Add("RegisterTaskCatalogs: all static task catalogs registered (duplicates rejected).");
        }

        /// <summary>
        /// CLN-1: captures every owned authority's state for the save pipeline.
        /// </summary>
        public SystemsSaveDto CaptureSaveDto()
        {
            var dto = new SystemsSaveDto
            {
                tasks = Tasks.CaptureSaveDto(),
                skills = Skills.CaptureSaveDto(),
                valuation = Valuation.CaptureSaveDto(),
                freightPool = FreightPool.CaptureSaveDto(),
                transportAssets = TransportAssets.CaptureSaveDto(),
                farmFlows = FarmFlows.CaptureSaveDto(),
                capabilities = Capabilities.CaptureSaveDto(),
                operatingLedger = OperatingLedger.CaptureSaveDto(),
                employments = Employments.CaptureSaveDto(),
                weeklyAttendance = WeeklyAttendance.CaptureSaveDto(),
                farmSlice = FarmSlice.CaptureSaveDto(),
                liabilities = Liabilities.CaptureSaveDto(),
                financialObligations = FinancialObligations.CaptureSaveDto(),
                creditOffers = CreditOffers.CaptureSaveDto(),
                creditInstruments = CreditInstruments.CaptureSaveDto(),
                postal = Postal.CaptureSaveDto(),
                risk = Risk.CaptureSaveDto(),
                disease = Disease.CaptureSaveDto(),
                journeys = Journeys.CaptureSaveDto(),
                routeConditions = RouteConditions.CaptureSaveDto(),
                moneyOrders = MoneyOrders.CaptureSaveDto(),
                registeredMail = RegisteredMail.CaptureSaveDto(),
                recruiting = Recruiting.CaptureSaveDto(),
                weatherSeed = weatherSeed,
                personSchedule = PersonSchedules.CaptureSaveDto(),
                rentCollection = RentCollection.CaptureSaveDto(),
                npcFormationAssets = NpcFormationAssets.CaptureSaveDto(),
                npcBusinessLifecycle = CaptureNpcBusinessLifecycle(),
                npcBusinessInvestigation = NpcBusinessInvestigation.CaptureSaveDto(),
                npcOpportunityObservation = NpcOpportunityObservation.CaptureSaveDto(),
                npcEntrepreneurship = NpcEntrepreneurship.CaptureSaveDto(),
                npcBusinessEvents = NpcBusinessEvents.CaptureSaveDto(),
                npcPropertyLife = NpcPropertyLife.CaptureSaveDto(),
                constructionProjects = ConstructionExecutor.CaptureSaveDto(new List<ConstructionProject>(ConstructionProjects)),
                npcConstructionCommissioner = NpcConstructionCommissioner.CaptureSaveDto(),
                constructionContractBook = ConstructionContractBook.CaptureSaveDto(),
                constructionBilling = ConstructionBilling.CaptureSaveDto(),
                tradeCredit = TradeCredit.CaptureSaveDto(),
                creditEvents = CreditEvents.CaptureSaveDto(),
                contactGraph = ContactGraph.CaptureSaveDto(),
                knowledgeTransmissions = KnowledgeTransmissions.CaptureSaveDto(),
                employmentNegotiation = EmploymentNegotiation.CaptureSaveDto(),
                wageSettlement = WageSettlement.CaptureSaveDto(),
                managerBrain = Managers.CaptureSaveDto(),
                managerDecisionExecution = ManagerDecisionExecution.CaptureSaveDto(),
                delegation = Delegation.CaptureSaveDto(),
                parcels = Parcels.CaptureSaveDto(),
                propertyInterests = PropertyInterests.CaptureSaveDto(),
                tenancyPayments = TenancyPayments.CaptureSaveDto(),
                householdDecisions = HouseholdDecisions.CaptureSaveDto(),
                householdDecisionDriver = HouseholdDecisionDriver.CaptureSaveDto(),
                householdLenders = HouseholdLenders.CaptureSaveDto(),
                businessLender = BusinessLender.CaptureSaveDto(),
                domesticLabor = DomesticLabor.CaptureSaveDto(),
                domesticLaborDriver = DomesticLaborDriver.CaptureSaveDto(),
                householdErrandPlans = HouseholdErrands.Plans.CaptureSaveDto(),
                // Priority U-3: the loop's in-transit custody (null loop = empty).
                shoppingCustody = ShoppingLoop != null ? ShoppingLoop.CaptureCustodySaveDto() : new HouseholdShoppingCustodySaveDto(),
                // Slice 25: the driver's active trips (null driver = empty).
                shoppingTrips = ShoppingTripDriver != null ? ShoppingTripDriver.CaptureSaveDto() : new List<PlannedShoppingTripSaveDto>(),
                householdAutonomy = HouseholdAutonomy.CaptureSaveDto(),
                housing = Housing.CaptureSaveDto(),
                lodgingRegistry = LodgingRegistry.CaptureSaveDto(),
                boardingHouseRuntimes = CaptureBoardingHouseRuntimes(),
                // P3 (F6 production wiring): one section per shared sawmill
                // authority. Null at capture means unwired (old save, degraded
                // path) — the section stays an empty DTO, never null.
                sawmillProductionDriver = sawmillProductionDriver != null ? sawmillProductionDriver.ToSaveDto() : new SawmillProductionDriver.SawmillProductionDriverSaveDto(),
                sawmillLabourCounters = sawmillLabourCounters != null ? sawmillLabourCounters.ToSaveDto() : new SawmillLabourCounters.SawmillLabourCountersDto(),
                logLotCustodyLedger = logLotCustodyLedger != null ? logLotCustodyLedger.CaptureSaveDto() : new LogLotCustodyLedger.LogLotCustodyLedgerSaveDto(),
                // F7 yard-transfer wiring: one section for the shared
                // transfer driver. Null at capture means unwired — the
                // section stays an empty DTO, never null.
                sawmillToYardTransferDriver = sawmillToYardTransferDriver != null ? sawmillToYardTransferDriver.ToSaveDto() : new SawmillToYardTransferDriver.SawmillToYardTransferDriverSaveDto(),
                // D3 (F-D2-1, F-D2-2): one section per shared hardware
                // driver. Null at capture means unwired — the sections
                // stay empty DTOs, never null.
                blacksmithToYardHardwareTransferDriver = blacksmithToYardHardwareTransferDriver != null ? blacksmithToYardHardwareTransferDriver.ToSaveDto() : new BlacksmithToYardHardwareTransferDriver.BlacksmithToYardHardwareTransferDriverSaveDto(),
                lumberYardHardwareImportDriver = lumberYardHardwareImportDriver != null ? lumberYardHardwareImportDriver.ToSaveDto() : new LumberYardHardwareImportDriver.LumberYardHardwareImportDriverSaveDto(),
                // Pass IV (Worker 1): deterministic due-event schedule.
                dueEvents = DueEvents.CaptureSaveDto(),
                // Pass IV Worker 2: unified reservations, draft holds, capacity registrations.
                reservations = Reservations.CaptureSaveDto(),
                draftPower = DraftPower.CaptureSaveDto(),
                equipmentCapacities = EquipmentCapacities.CaptureSaveDto(),
                civicTreasury = CivicTreasury.CaptureSaveDto(),
                scenarioRunState = ScenarioObjectives.CaptureRunState(),
                stockProvenance = StockProvenance.CaptureSaveDto(),
            };

            dto.workTimeBudgets = WorkTimeBudgets.CaptureSaveDto();

            butcherRuntimes ??= new List<ButcherRuntime>();
            foreach (ButcherRuntime runtime in butcherRuntimes)
            {
                if (runtime != null)
                {
                    dto.butcherRuntimes.Add(runtime.CaptureSaveDto());
                }
            }

            return dto;
        }

        /// <summary>
        /// CLN-1: restores every owned authority's state from the save pipeline.
        /// Returns diagnostics for anything that could not be restored.
        /// </summary>
        public List<string> LoadFromSaveDto(SystemsSaveDto dto, int absoluteDayIndex)
        {
            var diagnostics = new List<string>();
            if (dto == null)
            {
                return diagnostics;
            }

            Tasks.LoadFromSaveDto(dto.tasks);
            // Priority 4 Slice 4 (§2.5): re-observe the restored open
            // tasks at day resolution. Records the service already holds
            // (same task ids, observed before the load) are left alone by
            // RebuildFromAuthority; tasks it never saw get day-resolution
            // records flagged RebuiltAfterLoad.
            taskDurationDiagnostics?.RebuildFromAuthority(Tasks);
            Skills.LoadFromSaveDto(dto.skills);
            WorkTimeBudgets.LoadFromSaveDto(dto.workTimeBudgets, absoluteDayIndex);
            Valuation.LoadFromSaveDto(dto.valuation);
            // Pass IV Worker 2: restore the reservation authority BEFORE the
            // draft-power hold index (the index verifies against the authority).
            Reservations.LoadFromSaveDto(dto.reservations);
            FreightPool.LoadFromSaveDto(dto.freightPool);
            TransportAssets.LoadFromSaveDto(dto.transportAssets);
            FarmFlows.LoadFromSaveDto(dto.farmFlows);
            Capabilities.LoadFromSaveDto(dto.capabilities, diagnostics);
            OperatingLedger.LoadFromSaveDto(dto.operatingLedger);
            Employments.LoadFromSaveDto(dto.employments);
            WeeklyAttendance.LoadFromSaveDto(dto.weeklyAttendance);
            FarmSlice.LoadFromSaveDto(dto.farmSlice);
            FinancialObligations.LoadFromSaveDto(dto.financialObligations);
            CreditOffers.LoadFromSaveDto(dto.creditOffers);
            CreditInstruments.LoadFromSaveDto(dto.creditInstruments);
            diagnostics.AddRange(FinancialObligations.Reconcile());
            Liabilities.LoadFromSaveDto(dto.liabilities);
            Postal.LoadFromSaveDto(dto.postal);
            Risk.LoadFromSaveDto(dto.risk);
            Disease.LoadFromSaveDto(dto.disease);
            Journeys.LoadFromSaveDto(dto.journeys);
            RouteConditions.LoadFromSaveDto(dto.routeConditions);
            MoneyOrders.LoadFromSaveDto(dto.moneyOrders);
            RegisteredMail.LoadFromSaveDto(dto.registeredMail);
            Recruiting.LoadFromSaveDto(dto.recruiting);
            PersonSchedules.LoadFromSaveDto(dto.personSchedule);
            RentCollection.LoadFromSaveDto(dto.rentCollection);
            NpcFormationAssets.LoadFromSaveDto(dto.npcFormationAssets);
            npcBusinessLifeRecords.Clear();
            if (dto.npcBusinessLifecycle != null)
            {
                foreach (NpcBusinessLifeRecordSaveDto recordDto in dto.npcBusinessLifecycle)
                {
                    if (recordDto == null || string.IsNullOrWhiteSpace(recordDto.BusinessInstanceId)) continue;
                    var record = new NpcBusinessLifeRecord();
                    record.LoadFromSaveDto(recordDto);
                    npcBusinessLifeRecords.Add(record);
                }
            }
            NpcBusinessInvestigation.LoadFromSaveDto(dto.npcBusinessInvestigation);
            NpcOpportunityObservation.LoadFromSaveDto(dto.npcOpportunityObservation);
            NpcEntrepreneurship.LoadFromSaveDto(dto.npcEntrepreneurship);
            NpcBusinessEvents.LoadFromSaveDto(dto.npcBusinessEvents);
            NpcPropertyLife.LoadFromSaveDto(dto.npcPropertyLife, diagnostics);
            ConstructionProjects.Clear();
            foreach (ConstructionProject project in ConstructionExecutor.LoadFromSaveDto(dto.constructionProjects, diagnostics))
            {
                if (project != null) ConstructionProjects.Add(project);
            }
            NpcConstructionCommissioner.LoadFromSaveDto(dto.npcConstructionCommissioner, diagnostics);
            ConstructionContractBook.LoadFromSaveDto(dto.constructionContractBook);
            ConstructionBilling.LoadFromSaveDto(dto.constructionBilling);
            TradeCredit.LoadFromSaveDto(dto.tradeCredit);
            CreditEvents.LoadFromSaveDto(dto.creditEvents);
            ContactGraph.LoadFromSaveDto(dto.contactGraph);
            KnowledgeTransmissions.LoadFromSaveDto(dto.knowledgeTransmissions);
            EmploymentNegotiation.LoadFromSaveDto(dto.employmentNegotiation);
            WageSettlement.LoadFromSaveDto(dto.wageSettlement);
            Managers.LoadFromSaveDto(dto.managerBrain);
            ManagerDecisionExecution.LoadFromSaveDto(dto.managerDecisionExecution);
            Delegation.LoadFromSaveDto(dto.delegation);
            Parcels.LoadFromSaveDto(dto.parcels);
            PropertyInterests.LoadFromSaveDto(dto.propertyInterests);
            Housing.LoadFromSaveDto(dto.housing);
            LodgingRegistry.LoadFromSaveDto(dto.lodgingRegistry);
            LoadBoardingHouseRuntimes(dto.boardingHouseRuntimes);
            // Sawmill sections (driver, counters, ledger, transfer driver) and the D3
            // hardware driver sections (smith-to-yard transfer driver, nails import
            // driver) are NOT restored here. They load after the Pass 9 mill restore via
            // FirstLedgerSliceBootstrapper.ApplySawmillRestoreAfterPass9 — loading
            // them here would run before the yard is restored and drop valid jobs
            // (plan 25).
            TenancyPayments.LoadFromSaveDto(dto.tenancyPayments);
            HouseholdDecisions.LoadFromSaveDto(dto.householdDecisions);
            HouseholdDecisionDriver.LoadFromSaveDto(dto.householdDecisionDriver);
            HouseholdLenders.LoadFromSaveDto(dto.householdLenders);
            BusinessLender.LoadFromSaveDto(dto.businessLender);
            DomesticLabor.LoadFromSaveDto(dto.domesticLabor);
            DomesticLaborDriver.LoadFromSaveDto(dto.domesticLaborDriver);
            HouseholdErrands.Plans.LoadFromSaveDto(dto.householdErrandPlans);
            // Priority U-3: the loop's in-transit custody (null section on
            // old saves, or a null loop, loads as empty — never an error).
            if (dto.shoppingCustody != null && ShoppingLoop != null) ShoppingLoop.LoadCustodyFromSaveDto(dto.shoppingCustody);
            HouseholdAutonomy.LoadFromSaveDto(dto.householdAutonomy);
            // Pass IV (Worker 1): due-event schedule (clear-then-load, idempotent).
            DueEvents.LoadFromSaveDto(dto.dueEvents);
            // Slice 25: the driver's trip list. Placed after the DueEvents
            // restore so every authority the driver loads against (tasks,
            // loop custody, dues) is restored above this line. The trips
            // are loaded here but NOT rehydrated here — the supplier
            // directory is not re-registered until SaveLoadManager's
            // EnsureNotionsProcurementForRestore; rehydrate runs in
            // SimulationDrivers.Rebind (see ApplyRestoredShoppingTrips).
            if (ShoppingTripDriver != null)
            {
                ShoppingTripDriver.LoadFromSaveDto(dto.shoppingTrips);
            }
            else if (dto.shoppingTrips != null && dto.shoppingTrips.Count > 0)
            {
                // Campaign C (DEF-028): defer, do not drop. On a
                // fresh-scene Continue no BuildHouseholdShoppingLoop call
                // has run yet, so the driver is never attached at hub-load
                // time; the attach (BuildHouseholdShoppingLoop) drains
                // these into the driver before Rebind rehydrates.
                pendingShoppingTrips = new List<PlannedShoppingTripSaveDto>(dto.shoppingTrips);
                diagnostics.Add("ShoppingTripDriver: save holds " + dto.shoppingTrips.Count + " shopping trips but no driver is attached to the hub yet — trips held pending the driver attach (DEF-028), not dropped.");
            }
            // Pass IV Worker 2: reservations restore before draft holds (draft
            // holds reference authority keys); capacity registrations restore in
            // step with authority capacity limits. The freight pool re-attaches to
            // the hub-owned services and migrates legacy wagons (idempotent).
            Reservations.LoadFromSaveDto(dto.reservations);
            DraftPower.LoadFromSaveDto(dto.draftPower);
            EquipmentCapacities.LoadFromSaveDto(dto.equipmentCapacities);
            // Pass IX WS6: civic treasury (additive section; absent in old saves = empty).
            LoadCivicTreasuryFromSaveDto(dto.civicTreasury);
            // Pass IX WS6 (A1-03): scenario run/failure state (additive section).
            ScenarioObjectives.LoadRunState(dto.scenarioRunState);
            // Pass IX WS6 (IDENT-001): stock provenance (additive section).
            StockProvenance.LoadFromSaveDto(dto.stockProvenance);
            FreightPool.AttachDraftPower(DraftPower);
            FreightPool.AttachWagonRegistry(TransportAssets);
            FreightPool.MigrateWagonsToRegistry(diagnostics);            weatherSeed = Math.Max(0, dto.weatherSeed);
            // The condition provider is not serialized — re-attach after load.
            Journeys.ConditionProvider = RouteConditions;

            butcherRuntimes ??= new List<ButcherRuntime>();
            butcherRuntimes.Clear();
            if (dto.butcherRuntimes != null)
            {
                foreach (ButcherRuntimeSaveDto runtimeDto in dto.butcherRuntimes)
                {
                    if (runtimeDto == null || string.IsNullOrWhiteSpace(runtimeDto.businessInstanceId))
                    {
                        continue;
                    }

                    var runtime = new ButcherRuntime(
                        runtimeDto.businessInstanceId,
                        runtimeDto.productionSiteId,
                        runtimeDto.retailSiteId);
                    runtime.LoadFromSaveDto(runtimeDto);
                    butcherRuntimes.Add(runtime);
                }
            }

            return diagnostics;
        }

        /// <summary>
        /// Deferred sawmill restore. Called ONLY from FirstLedgerSliceBootstrapper
        /// .ApplySawmillRestoreAfterPass9 after the Pass 9 mill restore — never
        /// from LoadFromSaveDto — because the driver loads re-attach against
        /// yard lots that do not exist until the Pass 9 restore runs.
        /// </summary>
        public List<string> LoadSawmillSectionsFromSaveDto(SystemsSaveDto dto)
        {
            var diagnostics = new List<string>();
            if (dto == null)
            {
                return diagnostics;
            }

            if (sawmillLabourCounters != null) sawmillLabourCounters.LoadFromSaveDto(dto.sawmillLabourCounters);
            else diagnostics.Add("SimulationSystemsHub: sawmill labour counters section skipped — no counters constructed yet; the bootstrapper owns construction.");
            if (sawmillProductionDriver != null) sawmillProductionDriver.LoadFromSaveDto(dto.sawmillProductionDriver);
            else diagnostics.Add("SimulationSystemsHub: sawmill production driver section skipped — no driver constructed yet; the bootstrapper owns construction.");
            if (logLotCustodyLedger != null) logLotCustodyLedger.LoadFromSaveDto(dto.logLotCustodyLedger);
            else diagnostics.Add("SimulationSystemsHub: log-lot custody ledger section skipped — no ledger constructed yet; the bootstrapper owns construction.");
            if (sawmillToYardTransferDriver != null) sawmillToYardTransferDriver.LoadFromSaveDto(dto.sawmillToYardTransferDriver);
            else diagnostics.Add("SimulationSystemsHub: sawmill-to-yard transfer driver section skipped — no driver constructed yet; the bootstrapper owns construction.");
            return diagnostics;
        }

        /// <summary>
        /// D3 (F-D2-1, F-D2-2): deferred hardware-driver restore.
        /// Called ONLY from FirstLedgerSliceBootstrapper
        /// .ApplySawmillRestoreAfterPass9 after the Pass 9 yard restore
        /// — never from LoadFromSaveDto — because the transfer driver
        /// loads re-attach against yard lots that do not exist until
        /// the Pass 9 restore runs. The import driver's order book has
        /// no yard-stock dependency, but it restores on the same
        /// deferred path so both hardware drivers share one restore
        /// point and one construction owner (the bootstrapper).
        /// </summary>
        public List<string> LoadHardwareDriverSectionsFromSaveDto(SystemsSaveDto dto)
        {
            var diagnostics = new List<string>();
            if (dto == null)
            {
                return diagnostics;
            }

            if (blacksmithToYardHardwareTransferDriver != null) blacksmithToYardHardwareTransferDriver.LoadFromSaveDto(dto.blacksmithToYardHardwareTransferDriver);
            else diagnostics.Add("SimulationSystemsHub: smith-to-yard hardware transfer driver section skipped — no driver constructed yet; the bootstrapper owns construction.");
            if (lumberYardHardwareImportDriver != null) lumberYardHardwareImportDriver.LoadFromSaveDto(dto.lumberYardHardwareImportDriver);
            else diagnostics.Add("SimulationSystemsHub: lumber yard nails import driver section skipped — no driver constructed yet; the bootstrapper owns construction.");
            return diagnostics;
        }

        private List<BoardingHouseShopRuntime.BoardingHouseShopRuntimeSaveDto> CaptureBoardingHouseRuntimes()
        {
            var result = new List<BoardingHouseShopRuntime.BoardingHouseShopRuntimeSaveDto>();
            foreach (BoardingHouseShopRuntime runtime in boardingHouseRuntimes)
                if (runtime != null) result.Add(runtime.CaptureSaveDto());
            return result;
        }

        private void LoadBoardingHouseRuntimes(List<BoardingHouseShopRuntime.BoardingHouseShopRuntimeSaveDto> saved)
        {
            if (saved == null || saved.Count == 0) return;
            foreach (BoardingHouseShopRuntime.BoardingHouseShopRuntimeSaveDto dto in saved)
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.BusinessInstanceId)) continue;
                BoardingHouseShopRuntime runtime = null;
                foreach (BoardingHouseShopRuntime existing in boardingHouseRuntimes)
                {
                    if (existing != null && string.Equals(existing.BusinessInstanceId, dto.BusinessInstanceId, StringComparison.Ordinal))
                    {
                        runtime = existing;
                        break;
                    }
                }
                if (runtime == null)
                {
                    runtime = new BoardingHouseShopRuntime(dto.BusinessInstanceId, Ids);
                    boardingHouseRuntimes.Add(runtime);
                }
                runtime.LoadFromSaveDto(dto);
            }
        }

        private List<NpcBusinessLifeRecordSaveDto> CaptureNpcBusinessLifecycle()
        {
            var saved = new List<NpcBusinessLifeRecordSaveDto>();
            foreach (NpcBusinessLifeRecord record in npcBusinessLifeRecords)
            {
                if (record != null && !string.IsNullOrWhiteSpace(record.BusinessInstanceId))
                {
                    saved.Add(record.CaptureSaveDto());
                }
            }
            return saved;
        }

        /// <summary>
        /// CLN-1: exports animal state for the PopulationSaveDto section.
        /// </summary>
        public void ExportAnimalState(
            List<AnimalState> outAnimals,
            List<HistoricalAnimalRecord> outHistorical,
            List<LivestockCohort> outCohorts,
            List<EggBatchState> outEggBatches)
        {
            Animals.ExportState(outAnimals, outHistorical, outCohorts, outEggBatches);
        }

        /// <summary>CLN-1: imports animal state from the PopulationSaveDto section.</summary>
        public void ImportAnimalState(
            IEnumerable<AnimalState> inAnimals,
            IEnumerable<HistoricalAnimalRecord> inHistorical,
            IEnumerable<LivestockCohort> inCohorts,
            IEnumerable<EggBatchState> inEggBatches)
        {
            Animals.RestoreState(inAnimals, inHistorical, inCohorts, inEggBatches);
        }

        /// <summary>CLN-1: exports household ledgers for the PopulationSaveDto section.</summary>
        public void ExportHouseholdLedgers(List<HouseholdLedgerState> outStates)
        {
            HouseholdLedgers.ExportState(outStates);
        }

        /// <summary>CLN-1: imports household ledgers from the PopulationSaveDto section.</summary>
        public void ImportHouseholdLedgers(IEnumerable<HouseholdLedgerState> inStates)
        {
            HouseholdLedgers.ImportState(inStates);
        }

        /// <summary>Phase B: exports household inventories for the PopulationSaveDto section.</summary>
        public void ExportHouseholdInventories(List<HouseholdInventoryState> outStates)
        {
            HouseholdInventories.ExportState(outStates);
        }

        /// <summary>Phase B: imports household inventories from the PopulationSaveDto section.</summary>
        public void ImportHouseholdInventories(IEnumerable<HouseholdInventoryState> inStates)
        {
            HouseholdInventories.ImportState(inStates);
        }

        /// <summary>Phase B: exports meal records for the PopulationSaveDto section.</summary>
        public void ExportMealLog(
            List<HouseholdMealRecord> outMeals,
            List<MissedMealRecord> outMissed,
            List<MealPreparationRecord> outPreparations)
        {
            MealLog.ExportState(outMeals, outMissed, outPreparations);
        }

        /// <summary>Phase B: imports meal records from the PopulationSaveDto section.</summary>
        public void ImportMealLog(
            IEnumerable<HouseholdMealRecord> meals,
            IEnumerable<MissedMealRecord> missed,
            IEnumerable<MealPreparationRecord> preparations)
        {
            MealLog.ImportState(meals, missed, preparations);
        }

        /// <summary>Phase B: exports purchasing needs for the PopulationSaveDto section.</summary>
        public void ExportPurchasingNeeds(List<HouseholdPurchasingNeed> outNeeds)
        {
            PurchasingNeeds.ExportState(outNeeds);
        }

        /// <summary>Phase B: imports purchasing needs from the PopulationSaveDto section.</summary>
        public void ImportPurchasingNeeds(IEnumerable<HouseholdPurchasingNeed> inNeeds)
        {
            PurchasingNeeds.ImportState(inNeeds);
        }
    }
}
