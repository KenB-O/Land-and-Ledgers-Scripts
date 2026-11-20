// Series 2 Pass 9 (player binding + save adoption): save adoption
// for everything Series 1 and Series 2 built that had NO save
// coverage before this pass.
//
// What was missing, verified in files before writing: zero
// Pass2/Pass3/Pass4/Pass5 references in SaveLoadManager.cs or
// PassXlSliceSaveDto.cs; the slice coordinator captured only
// flags + off-map procurement. Pass 6 planned day/batch ids were
// in NO save DTO. Pass 7 wholesale delivered-lot ids lived only
// in a private per-instance set. Pass 8 dealer intake lots had a
// DTO but no production-save wiring, and carrier settlement's
// per-instance settled set had no persistence at all.
//
// What this file does, and does NOT do:
// - It defines ONE new save section (Series2Pass9SaveDto) and ONE
//   runtime holder (Series2Pass9SaveState) with Capture (deep
//   copy, never aliasing live lists) and Restore (clear-then-
//   load, idempotent: restoring the same DTO twice yields the
//   same state, never a duplicate).
// - It wires that section into the production save through the
//   existing Pass XL slice section: PassXlSliceSaveDto gains one
//   additive field (series2Pass9; old saves read it as null,
//   which restores as empty), and PassXlSliceSaveCoordinator
//   gains overloads that capture/restore it when a state is
//   supplied. SaveLoadManager gains one property
//   (Series2Pass9State, following the PassXlProcurementService
//   pattern) and calls those overloads. Codex must SET that
//   property before the first save, exactly as for procurement
//   (F19); a null state saves an empty section with a
//   diagnostic, never a crash.
// - It does NOT duplicate state that already has a save DTO:
//   bakery shelf/flour/fuel lots are already in
//   BakeryShopRuntimeSaveDto (Capture/Load in
//   BakeryShopRuntime.cs); business cash is already in
//   BusinessRuntimeState's own save; household cash is already
//   in HouseholdLedgerState; TaskAuthority has its own
//   TaskAuthoritySaveDto (the binding's WorkTasks persist
//   there, not here — this section persists the PLAYER ROSTER
//   that the binding mirrors, and a driver that restores the
//   roster without re-issuing through the binding has a roster
//   with no authority tasks, which the review doc says plainly).
// - Adapter DayIndex is deliberately NOT restored: Pass 7 says
//   the day driver sets it at day start, and restoring a stale
//   day would price at yesterday's condition. Bakery
//   PlannedDayIndex IS restored (it is driver plan state, not
//   a pricing day), and the driver re-validates via Precheck
//   before any bake after load.
// - Pass 5 leads/visits are deliberately NOT here: Pass 10
//   scope, stated, not claimed.
// - Fuel-source state is parameters only: conversion factor,
//   dealer id, and a flag that an off-map callback was wired.
//   The delegate itself cannot be serialized, and no order or
//   origin is invented here — the callback, when rewired by
//   Codex, names its own import order and origin at run time.
//   Restored params are exposed on the state holder
//   (RestoredFuelSourceParams) for Codex to construct a new
//   source from; they are never applied to a live source
//   behind its back.
// - Carrier settlement: NO second settled-set field is
//   persisted, by decision (see
//   Series2CarrierSettlement.RestoreFromShipments): the
//   shipment's freightRevenueCollected flag, which IS part of
//   the shipment save DTO, is the settlement authority across
//   saves. Restore rebuilds the in-memory set from those flags
//   so the in-process guard matches the persisted truth, and
//   SettleOnAcceptance refuses a flagged shipment even with an
//   empty set — a restored settlement can never double-collect.
// - Freight restore takes a FreightShipmentHooks (TaskAuthority
//   + WorkTimeBudgetStore + receiving callback + reservations).
//   The SaveLoadManager path passes null hooks: the service
//   restore handles null hooks (reservation rebinding is
//   skipped, task bindings re-resolve by definition id on the
//   next tick). Codex may pass fully-wired hooks through
//   State.Restore directly when it has them; the null-hooks
//   path is the documented default, not a silent loss —
//   shipments, cargo, deliveryApplied, and
//   freightRevenueCollected all round-trip in the DTO itself.
// - Old/empty saves: a null DTO, or a DTO with null sections,
//   restores as empty state for every section, with a
//   diagnostic per skipped holder — the same old-save
//   contract as the Pass XL coordinator, never data invented
//   to fill a gap.
using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.FreightFuel;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Economy.GenericBusiness;
using LandLedgers.Economy.Storage;
using LandLedgers.Orchestration.Player;
using LandLedgers.Population;
using LandLedgers.World.Journeys;

namespace LandLedgers.Persistence
{
    [Serializable]
    public sealed class Pass9PersonHandOccupancyDto
    {
        public int PersonId;
        public string LeftAuthorityId = string.Empty;
        public string LeftLotId = string.Empty;
        public string LeftBatchId = string.Empty;
        public int LeftRequirement;
        public string RightAuthorityId = string.Empty;
        public string RightLotId = string.Empty;
        public string RightBatchId = string.Empty;
        public int RightRequirement;
        public bool IsTwoHandedHold;
        public string LastRefusalReason = string.Empty;
        public string LastRefusalOperation = string.Empty;
    }

    [Serializable]
    public sealed class Pass9HandheldContainerEntryDto
    {
        public string AuthorityId = string.Empty;
        public string LotId = string.Empty;
        public string BatchId = string.Empty;
        public string ItemId = string.Empty;
        public int Units;
        public int Condition;
    }

    [Serializable]
    public sealed class Pass9HandheldContainerDto
    {
        public string ContainerId = string.Empty;
        public int Kind;
        public int HeldByPersonId = -1;
        public int HeldHand = -1;
        public string LocationId = string.Empty;
        public int MaxUnits = -1;
        public bool MassKnown;
        public float MassKg;
        public bool VolumeKnown;
        public float VolumeLiters;
        public bool LengthKnown;
        public float LengthMeters;
        public bool WidthKnown;
        public float WidthMeters;
        public bool HeightKnown;
        public float HeightMeters;
        public List<Pass9HandheldContainerEntryDto> Entries = new List<Pass9HandheldContainerEntryDto>();
    }

    // ---- Pass 2 ----

    [Serializable]
    public sealed class Pass9SequenceEntryDto
    {
        public string TaskDefinitionId = string.Empty;
        public int PlannedMinutes;
        public bool Completed;
        public string MissedReason = string.Empty;
    }

    [Serializable]
    public sealed class Pass9MissedItemDto
    {
        public string TaskDefinitionId = string.Empty;
        public string OwnerDescription = string.Empty;
        public string Reason = string.Empty;
        public bool WasDemandBalked;
    }

    // ---- Pass 3 ----

    [Serializable]
    public sealed class Pass9WholesaleShelfLotDto
    {
        public string BakeryLotId = string.Empty;
        public int BakedDayIndex = -1;
        public int ReceivedDayIndex = -1;
        public string Condition = string.Empty;
        public int UnitWholesaleCostCents;
        public int BackUnits;
        public int ShelfUnits;
    }

    [Serializable]
    public sealed class Pass9CatalogLineDto
    {
        public string ProductId = string.Empty;
        public string DisplayName = string.Empty;
        public int CostCents;
        public int PriceCents;
        public int ShelfUnits;
        public int BackUnits;
        public int ShelfCapacityUnits;
        public int BackCapacityUnits;
        public List<Pass9WholesaleShelfLotDto> WholesaleLots = new List<Pass9WholesaleShelfLotDto>();
    }

    [Serializable]
    public sealed class Pass9SkuOfferDto
    {
        public string SkuId = string.Empty;
        public int UnitPriceCents;
        public double UnitWeightLb;
    }

    [Serializable]
    public sealed class Pass9SupplierOfferDto
    {
        public string SupplierId = string.Empty;
        public string SupplierName = string.Empty;
        public int DistanceMiles;
        public bool IsPickupOnly;
        public int MinimumAmountCents;
        public int MinimumQuantityUnits;
        public double MinimumPoundageLb;
        public List<Pass9SkuOfferDto> SkuOffers = new List<Pass9SkuOfferDto>();
    }

    [Serializable]
    public sealed class Pass9OrderLineDto
    {
        public string SkuId = string.Empty;
        public int QuantityUnits;
        public int UnitPriceCents;
        public double UnitWeightLb;
    }

    [Serializable]
    public sealed class Pass9OrderDraftDto
    {
        public Pass9SupplierOfferDto Supplier;
        public List<Pass9OrderLineDto> Lines = new List<Pass9OrderLineDto>();
        public int FreightOption;
        public int FreightCentsPerMile;
        public int FreightBusinessCommissionCents;
        public int Actor;
        public bool IsAutomatic;
        public bool DelegationAllowed;
    }

    [Serializable]
    public sealed class Pass9OrderRequestDto
    {
        public string SupplierId = string.Empty;
        public string SkuId = string.Empty;
        public int QuantityUnits;
        public int GoodsCents;
        public int FreightAllocatedCents;
        public int TotalCents;
        public int FreightOption;
        public bool RequiresJourney;
        public string Status = string.Empty;
    }

    [Serializable]
    public sealed class Pass9FuelInputOrderDto
    {
        public string DealerBusinessId = string.Empty;
        public string InputSkuId = string.Empty;
        public Pass9OrderDraftDto Draft;
        public double ConversionRateFuelPerInputUnit;
        public bool InputReceived;
        public string InputLotId = string.Empty;
    }

    [Serializable]
    public sealed class Pass9WholesaleSettlementDto
    {
        public string PayerBusinessId = string.Empty;
        public string PayeeBusinessId = string.Empty;
        public string BakeryLotId = string.Empty;
        public string ProductId = string.Empty;
        public int Units;
        public int UnitWholesalePriceCents;
        public int AmountCents;
        public bool Settled;
    }

    // ---- Pass 4 ----

    [Serializable]
    public sealed class Pass9DutyTaskDto
    {
        public string TaskDefinitionId = string.Empty;
        public int PlannedMinutes;
        public string Owner = string.Empty;
        public bool Completed;
        public bool InProgress;
        public string MissedReason = string.Empty;
        public bool DutyAssigned;
    }

    [Serializable]
    public sealed class Pass9DutyDto
    {
        public string PlayerPersonId = string.Empty;
        public string CurrentLocationId = string.Empty;
        public int DailyBudgetMinutes;
        public bool DutyRequested;
        public bool TreatPlayerAsNpc;
        public string CurrentTaskId = string.Empty;
        public int CashCents;
        public string TravelDestination = string.Empty;
        public int TravelMinutesTotal;
        public int TravelMinutesElapsed;
        public List<Pass9DutyTaskDto> Tasks = new List<Pass9DutyTaskDto>();
        public List<Pass9DutyTaskDto> OffloadedTasks = new List<Pass9DutyTaskDto>();
    }

    [Serializable]
    public sealed class Pass9AutoEatDto
    {
        public bool ToggleOn;
        public int State;
        public string CurrentLocationId = string.Empty;
        public int CashCents;
        public string FailureReason = string.Empty;
        public string SellerLocationId = string.Empty;
        public int TravelMinutesTotal;
        public int TravelMinutesElapsed;
    }

    [Serializable]
    public sealed class Pass9AutoSleepDto
    {
        public bool ToggleOn;
        public int State;
        public string CurrentLocationId = string.Empty;
        public int CashCents;
        public string FailureReason = string.Empty;
        public string LodgingLocationId = string.Empty;
    }

    // ---- Pass 8 fuel-source parameters only (no delegate) ----

    [Serializable]
    public sealed class Pass9FuelSourceParamsDto
    {
        public string DealerBusinessId = string.Empty;
        public int IntakeUnitsPerBakeryUnit;
        public bool HadOffMapReceipt;
    }

    /// <summary>
    /// Series 2 Pass 9 save section. Every list is initialised so
    /// a partially-filled or old DTO (null lists are repaired on
    /// restore) never crashes a load. SectionVersion is additive
    /// bookkeeping for future passes, not a format-version claim.
    /// </summary>
    [Serializable]
    public sealed class Pass9FuelDealerCustomerQueueEntryDto
    {
        public string BusinessInstanceId = string.Empty;
        public LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto Queue;
    }

    [Serializable]
    public sealed class Pass9FurniturePurchaseLineDto
    {
        public string ItemId = string.Empty;
        public int Units;
        public string SourceLotId = string.Empty;
        public int UnitPriceCents;
        public int Stage;
        public List<string> AssetIds = new List<string>();
    }

    [Serializable]
    public sealed class Pass9FurniturePurchaseOrderDto
    {
        public string PurchaseId = string.Empty;
        public int Kind;
        public int HouseholdId = -1;
        public int SellerHouseholdId = -1;
        public string VendorId = string.Empty;
        public string VendorName = string.Empty;
        public int BuildingInstanceId = -1;
        public int DayIndex;
        public int FreightCents;
        public string ShipmentId = string.Empty;
        public string InstallationBlockedReason = string.Empty;
        public bool CancellationRequested;
        public string TerminalReason = string.Empty;
        public List<Pass9FurniturePurchaseLineDto> Lines = new List<Pass9FurniturePurchaseLineDto>();
        public List<string> Diagnostics = new List<string>();
    }

    [Serializable]
    public sealed class Series2Pass9SaveDto
    {
        public int SectionVersion = 1;

        public List<Pass9SequenceEntryDto> SequenceEntries = new List<Pass9SequenceEntryDto>();
        public List<Pass9MissedItemDto> MissedItems = new List<Pass9MissedItemDto>();
        public int IdleMinutes;

        public List<Pass9CatalogLineDto> CatalogLines = new List<Pass9CatalogLineDto>();
        public List<Pass9OrderDraftDto> Drafts = new List<Pass9OrderDraftDto>();
        public List<Pass9OrderRequestDto> PlacedRequests = new List<Pass9OrderRequestDto>();
        public List<Pass9FuelInputOrderDto> FuelInputOrders = new List<Pass9FuelInputOrderDto>();
        public List<Pass9WholesaleSettlementDto> WholesaleSettlements = new List<Pass9WholesaleSettlementDto>();
        public List<string> WholesaleDeliveredLotIds = new List<string>();

        public Pass9DutyDto Duty;
        public Pass9AutoEatDto AutoEat;
        public Pass9AutoSleepDto AutoSleep;

        public int BakeryPlannedDayIndex = -1;
        public List<string> BakeryPlannedBatchIds = new List<string>();
        // Additive live Bakery owner state. Older saves leave this null; the
        // bootstrap-owned runtime remains empty until the authored Bakery is
        // configured, while current saves round-trip its actual lots/batches.
        public LandLedgers.Economy.Businesses.Bakery.BakeryShopRuntime.BakeryShopRuntimeSaveDto BakeryRuntime;
        // L1 (Agent L1, universal business campaign 2026-11-17): live
        // blacksmith runtime state (material stock + lot ledger, forged
        // horseshoes/hardware, repair orders/quotes/pickups/liens, quarter
        // pools). Additive; old saves leave this null and load with an
        // empty smith — the honest old-save default.
        public LandLedgers.Economy.Blacksmith.BlacksmithRuntime.BlacksmithRuntimeSaveDto BlacksmithRuntime;

        /// <summary>
        /// Plan 07 (Slice 07): staged bakery chain state (chain DTO,
        /// including the private due-event scheduler's events). Additive;
        /// old saves leave this null and load with an empty chain, the
        /// honest old-save default.
        /// </summary>
        public LandLedgers.Economy.Businesses.Bakery.BakeryStagedProductionChain.BakeryChainSaveDto StagedBakeryChain;

        public FuelYardByproductIntake.FuelYardByproductIntakeSaveDto FuelIntake;

        /// <summary>B-18 (DEF-003): Fuel Dealer runtime state (suppliers, orders, cash book, policy). Additive; old saves leave it null and load with a fresh runtime — the honest old-save default.</summary>
        public LandLedgers.Economy.Businesses.FuelDealer.FuelDealerRuntime.FuelDealerRuntimeSaveDto FuelDealerRuntime;
        public FreightShipmentService.FreightServiceSaveDto Freight;
        public Pass9FuelSourceParamsDto FuelSourceParams;

        // QA-29: timber/fuel/queue sections. LogHaul has no separate slot:
        // it rides inside LoggingOperationSaveDto.Hauls and restores through
        // LogHaul.FromSaveDto. Each section is additive; old saves leave it
        // null and load with empty state — the honest old-save default.
        public LandLedgers.Economy.Businesses.Logging.LoggingStandRegistry.LoggingStandRegistrySaveDto StandRegistry;
        public LandLedgers.Economy.Businesses.Logging.LoggingOperationRuntime.LoggingOperationSaveDto LoggingOperation;
        public SawmillShopRuntime.SawmillShopRuntimeSaveDto Sawmill;
        public LandLedgers.Economy.Businesses.LumberYard.LumberYardShopRuntime.LumberYardShopRuntimeSaveDto LumberYard;
        public LandLedgers.Economy.Businesses.Logging.LoggingCampSite.LoggingCampSiteSaveDto LoggingCamp;
        public LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto CustomerQueue;
        /// <summary>Additive physical bakery counter line; old saves have no waiting customers.</summary>
        public LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto BakeryCustomerQueue;
        /// <summary>Additive, business-keyed fuel-dealer counter lines; old saves restore no waiting customers.</summary>
        public List<Pass9FuelDealerCustomerQueueEntryDto> FuelDealerCustomerQueues = new List<Pass9FuelDealerCustomerQueueEntryDto>();

        /// <summary>
        /// Slice S2: per-business equipment registers (ovens' component
        /// assets + tool kits). Additive; old saves leave this empty and
        /// load with empty registers — the kit/oven gates then refuse
        /// honestly until the scenario endowment runs on a NewGame.
        /// </summary>
        public List<BusinessEquipmentRegister> EquipmentRegisters = new List<BusinessEquipmentRegister>();

        /// <summary>
        /// B0 (Agent B, expanded campaign 2026-11-16): per-household
        /// furnishing registers (household-owned furniture/equipment
        /// assets + placement bindings). Additive; old saves leave this
        /// empty and load with empty registers — homes start empty
        /// unless the scenario explicitly authors an endowment (O-2).
        /// </summary>
        public List<HouseholdFurnishingRegister> HouseholdFurnishingRegisters = new List<HouseholdFurnishingRegister>();
        /// <summary>Durable furniture orders; freight remains in FreightServiceSaveDto and custody remains in household registers.</summary>
        public List<Pass9FurniturePurchaseOrderDto> FurniturePurchaseOrders = new List<Pass9FurniturePurchaseOrderDto>();

        /// <summary>
        /// B2 (Agent B, expanded campaign 2026-11-16): stove fuel
        /// stores — the fuel physically in each household stove
        /// (HouseholdStoveFuelStoreDto). Additive; old saves leave this
        /// empty and load with empty stores — the honest old-save
        /// default (fuel that was never tracked was never in a stove).
        /// The stove instances themselves are derived from the B0
        /// registers on load and need no saved section.
        /// </summary>
        public List<HouseholdStoveFuelStoreDto> HouseholdStoveFuelStores = new List<HouseholdStoveFuelStoreDto>();

        /// <summary>
        /// G-4 (Agent G): live storage bindings, held here so the Pass
        /// 9 seam persists them. The hub/state owns the per-household
        /// storage registries; the bootstrap restores the saved bindings
        /// into them after a load. A household with no entry holds no
        /// storage bindings.
        /// </summary>
        public List<FurnishingStorageBindingSaveDto> HouseholdStorageBindings = new List<FurnishingStorageBindingSaveDto>();

        /// <summary>Additive person-hand snapshots; older saves deserialize this as an empty list.</summary>
        public List<Pass9PersonHandOccupancyDto> PersonHands = new List<Pass9PersonHandOccupancyDto>();

        /// <summary>Additive physical handheld-container state; older saves deserialize this as an empty list.</summary>
        public List<Pass9HandheldContainerDto> HandheldContainers = new List<Pass9HandheldContainerDto>();

        /// <summary>
        /// Slice S9: off-map journeys recorded by the production
        /// OffMapJourneyAuthority. Additive; old saves leave this null
        /// and load with no journeys — the honest old-save default.
        /// </summary>
        public OffMapJourneyAuthoritySaveDto OffMapJourneys;
    }

    /// <summary>
    /// Runtime holder for the Pass 9 save section. Codex fills
    /// these fields with the live model instances and hands this
    /// holder to SaveLoadManager (Series2Pass9State) before the
    /// first save, and reads the restored instances back from it
    /// after a load. A null field in any section captures as an
    /// empty section and restores as a diagnostic, never a crash.
    /// Callers must read Catalog / SequenceDay / MissedReport back
    /// from this holder after Restore: those three are replaced
    /// with rebuilt instances (the old instances have no clear
    /// method, so mutating them could not be made idempotent).
    /// Duty and auto-care instances are restored IN PLACE via
    /// their RestoreSnapshot methods, so their identity is kept.
    /// </summary>
    public sealed class Series2Pass9SaveState
    {
        public Pass2TaskSequenceDay SequenceDay;
        public Pass2MissedWorkReport MissedReport;

        public Pass3StoreCatalog Catalog;
        public List<Pass3OrderDraft> Drafts = new List<Pass3OrderDraft>();
        public List<Pass3OrderRequest> PlacedRequests = new List<Pass3OrderRequest>();
        public List<Pass3FuelInputOrder> FuelInputOrders = new List<Pass3FuelInputOrder>();
        public Pass3WholesaleIntake WholesaleIntake;
        public List<Pass3WholesaleSettlement> WholesaleSettlements = new List<Pass3WholesaleSettlement>();

        public Pass4PlayerDuty Duty;
        public Pass4AutoEat AutoEat;
        public Pass4AutoSleep AutoSleep;

        public Series2BakeryProductionDriver BakeryDriver;
        public LandLedgers.Economy.Businesses.Bakery.BakeryShopRuntime BakeryRuntime;
        /// <summary>
        /// L1 (Agent L1, universal business campaign 2026-11-17): live
        /// blacksmith runtime, held here in the established Pass 9 holder
        /// pattern ("Codex fills these fields with the live model
        /// instances"). Null until a production driver owns a smith
        /// (D3 delivery driver follow-up F-L1-1); a null holder captures
        /// as an empty section, exactly like the bakery's.
        /// </summary>
        public LandLedgers.Economy.Blacksmith.BlacksmithRuntime BlacksmithRuntime;

        /// <summary>
        /// Plan 07 (Slice 07): live staged bakery chain and its PRIVATE
        /// due-event scheduler, held here in the established Pass 9
        /// holder pattern ("Codex fills these fields with the live model
        /// instances"). The chain is not a hub-owned authority; it rides
        /// the Pass 9 state exactly like BakeryDriver does.
        /// </summary>
        public LandLedgers.Economy.Businesses.Bakery.BakeryStagedProductionChain StagedBakeryChain;
        public LandLedgers.Time.DueEventScheduler StagedBakeryDueEvents;

        public FuelYardByproductIntake FuelIntake;

        /// <summary>
        /// B-18 (DEF-003, Decision 3): the live per-instance Fuel Dealer
        /// runtime (purchasing, supplier relationships, order book, cash
        /// book) for the first formed dealer. Created by
        /// SimulationDrivers at registration (or on first transfer) and
        /// reused after load — never replaced, so Restore is clear-then-load
        /// into the live holder. Null until a dealer is formed.
        /// </summary>
        public LandLedgers.Economy.Businesses.FuelDealer.FuelDealerRuntime FuelDealerRuntime;
        public FreightProductionDriver FreightDriver;
        public Series2CarrierSettlement CarrierSettlement;
        public Series2DealerBakeryFuelSource FuelSource;

        // QA-29: live timber/fuel/queue runtimes for the Pass 9 sections
        // above. Same holder pattern as FuelIntake: Codex fills these with
        // the live instances before the first save; a null field captures
        // as an empty section and restores as a diagnostic, never a crash.
        // A2 plan 27 (P10 live felling driver, note 110 C4): the
        // logging operation, stand registry, and logging camp ARE owned
        // in production now — the bootstrapper constructs them into
        // these slots in ConfigureSawmillProductionWiring, and
        // SimulationDrivers.DriveFelling drives the camp daily. The
        // sawmill, lumber yard, and customer queue slots keep the
        // original shape: null until their own wiring lands.
        public LandLedgers.Economy.Businesses.Logging.LoggingStandRegistry StandRegistry;
        public LandLedgers.Economy.Businesses.Logging.LoggingOperationRuntime LoggingOperation;
        public LandLedgers.Economy.Businesses.Sawmill.SawmillShopRuntime Sawmill;
        public LandLedgers.Economy.Businesses.LumberYard.LumberYardShopRuntime LumberYard;
        public LandLedgers.Economy.Businesses.Logging.LoggingCampSite LoggingCamp;

        /// <summary>
        /// QA-29: the live store waiting-customer queue. Bound by the
        /// bootstrapper before the Pass 9 restore
        /// (EnsurePass9SaveOwnerForRestore) and at loop build
        /// (SimulationDrivers.BuildHouseholdShoppingLoop).
        ///
        /// Campaign C (DEF-028): a queue section that arrives while no
        /// queue is bound is HELD in pendingCustomerQueue, not dropped,
        /// and the bind drains it into the queue (LoadFromSaveDto,
        /// clear-then-load). The drain runs before the first post-load
        /// tick, so the queue holds no live entries the drain could
        /// wipe; a second bind after the drain is a no-op (pending is
        /// cleared on drain). Follows the RestoredStagedBakeryChain
        /// precedent in this file (a DTO recovered by Restore, applied
        /// when its owner appears) — except the drain here is automatic
        /// at bind time, so no consumer call is needed.
        /// </summary>
        private LandLedgers.Economy.GeneralStore.CustomerQueue customerQueue;
        private LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto pendingCustomerQueue;
        private LandLedgers.Economy.GeneralStore.CustomerQueue bakeryCustomerQueue;
        private LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto pendingBakeryCustomerQueue;
        private readonly Dictionary<string, LandLedgers.Economy.GeneralStore.CustomerQueue> fuelDealerCustomerQueues =
            new Dictionary<string, LandLedgers.Economy.GeneralStore.CustomerQueue>(StringComparer.Ordinal);
        private readonly Dictionary<string, LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto> pendingFuelDealerCustomerQueues =
            new Dictionary<string, LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto>(StringComparer.Ordinal);

        /// <summary>Campaign C (DEF-028): true while a queue section recovered by Restore waits for a queue to bind.</summary>
        public bool HasPendingCustomerQueue => pendingCustomerQueue != null;
        public bool HasPendingBakeryCustomerQueue => pendingBakeryCustomerQueue != null;

        public LandLedgers.Economy.GeneralStore.CustomerQueue CustomerQueue
        {
            get => customerQueue;
            set
            {
                customerQueue = value;
                if (customerQueue != null && pendingCustomerQueue != null)
                {
                    LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto deferred = pendingCustomerQueue;
                    pendingCustomerQueue = null;
                    customerQueue.LoadFromSaveDto(deferred);
                }
            }
        }

        /// <summary>
        /// Live bakery waiting line. Kept distinct from the General Store
        /// queue so service ownership and customer patience cannot cross
        /// between merchants. A queue restored before bakery bootstrap is
        /// held pending and applied when its runtime binds.
        /// </summary>
        public LandLedgers.Economy.GeneralStore.CustomerQueue BakeryCustomerQueue
        {
            get => bakeryCustomerQueue;
            set
            {
                bakeryCustomerQueue = value;
                if (bakeryCustomerQueue != null && pendingBakeryCustomerQueue != null)
                {
                    var deferred = pendingBakeryCustomerQueue;
                    pendingBakeryCustomerQueue = null;
                    bakeryCustomerQueue.LoadFromSaveDto(deferred);
                }
            }
        }

        public int PendingFuelDealerCustomerQueueCount => pendingFuelDealerCustomerQueues.Count;

        /// <summary>
        /// Returns the live physical counter queue for one formed fuel dealer.
        /// A queue restored before the dealer's retail adapter exists is held
        /// by business identity and drained exactly once when this method binds it.
        /// </summary>
        public LandLedgers.Economy.GeneralStore.CustomerQueue GetOrCreateFuelDealerCustomerQueue(
            string businessInstanceId, string merchantName)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            if (!fuelDealerCustomerQueues.TryGetValue(businessInstanceId, out var queue) || queue == null)
            {
                queue = new LandLedgers.Economy.GeneralStore.CustomerQueue
                {
                    MerchantBusinessId = businessInstanceId,
                    MerchantName = merchantName ?? string.Empty,
                };
                fuelDealerCustomerQueues[businessInstanceId] = queue;
            }

            if (pendingFuelDealerCustomerQueues.TryGetValue(businessInstanceId, out var pending))
            {
                pendingFuelDealerCustomerQueues.Remove(businessInstanceId);
                queue.LoadFromSaveDto(pending);
            }
            else
            {
                queue.MerchantBusinessId = businessInstanceId;
                queue.MerchantName = merchantName ?? string.Empty;
            }
            return queue;
        }

        /// <summary>
        /// Slice S2: live per-business equipment registers, held here so
        /// the Pass 9 seam persists them. The hub owns the live
        /// dictionary; the bootstrap registers these instances into it
        /// on NewGame seeding and re-registers restored ones after a load.
        /// </summary>
        public List<BusinessEquipmentRegister> EquipmentRegisters = new List<BusinessEquipmentRegister>();
        public List<FurniturePurchaseOrder> FurniturePurchaseOrders = new List<FurniturePurchaseOrder>();

        /// <summary>
        /// B0 (Agent B): live per-household furnishing registers, held
        /// here so the Pass 9 seam persists them. The hub owns the live
        /// dictionary; the bootstrap re-registers restored ones after a
        /// load. A household with no register holds no furniture.
        /// </summary>
        public List<HouseholdFurnishingRegister> HouseholdFurnishingRegisters = new List<HouseholdFurnishingRegister>();

        /// <summary>
        /// B2 (Agent B): live stove fuel stores, held here so the Pass
        /// 9 seam persists them. The hub/state owns the per-household
        /// stove registries; the bootstrap restores the saved stores
        /// into them after a load. A household with no entry holds no
        /// fuel in any stove.
        /// </summary>
        public List<HouseholdStoveFuelStoreDto> HouseholdStoveFuelStores = new List<HouseholdStoveFuelStoreDto>();

        /// <summary>
        /// G-4 (Agent G, expanded campaign 2026-11-16): storage
        /// bindings — the bind parameters plus placed lots per binding
        /// (FurnishingStorageBindingSaveDto). Additive; old saves leave
        /// this empty and load with no bindings — the honest old-save
        /// default (bindings that were never tracked were never bound
        /// after a load). The B0 placement stamp (StorageZone
        /// capability) still persists through the furnishing registers;
        /// this list is what re-binds the fixtures from it.
        /// </summary>
        public List<FurnishingStorageBindingSaveDto> HouseholdStorageBindings = new List<FurnishingStorageBindingSaveDto>();

        /// <summary>Live person hand authority; bound by SimulationDrivers and restored in place.</summary>
        public PersonHandOccupancyRegistry PersonHands;

        /// <summary>Live handheld-container directory; its snapshots are restored in place.</summary>
        public HandheldContainerRegistry HandheldContainers;

        /// <summary>
        /// Slice S9: live snapshot holder for the off-map journeys,
        /// filled by SimulationDrivers on every journey mutation and
        /// restored into the authority by the bootstrapper after a load.
        /// Null on old saves (no journeys — the old-save default).
        /// </summary>
        public OffMapJourneyAuthoritySaveDto OffMapJourneys;

        /// <summary>
        /// Fuel-source parameters recovered by the last Restore,
        /// for Codex to construct a new fuel source from. Null
        /// when no source was saved. Never applied to a live
        /// source by this class: the off-map delegate must be
        /// rewired by the driver that constructs the source.
        /// </summary>
        public Pass9FuelSourceParamsDto RestoredFuelSourceParams;

        /// <summary>
        /// Plan 07 (Slice 07): staged bakery chain DTO recovered by the
        /// last Restore, for SimulationDrivers to apply on the first
        /// drive after load (the driver loads the private scheduler
        /// first, then the chain, which rebuilds hooks and re-acquires
        /// due events). Null when no chain was saved. Never applied to
        /// a live chain by this class: applying needs TaskAuthority,
        /// which this layer does not own.
        /// </summary>
        public LandLedgers.Economy.Businesses.Bakery.BakeryStagedProductionChain.BakeryChainSaveDto RestoredStagedBakeryChain;

        /// <summary>
        /// B-18 (DEF-003): fuel-intake DTO recovered by the last Restore
        /// when no live intake holder existed yet (fresh Continue: the
        /// driver creates the holder lazily). SimulationDrivers applies it
        /// on first use through ApplyPendingFuelIntake. Null when no intake
        /// was saved, or once applied. A null DTO on a later Restore clears
        /// a stale pending field from an earlier load.
        /// </summary>
        public FuelYardByproductIntake.FuelYardByproductIntakeSaveDto RestoredFuelIntake;

        /// <summary>
        /// B-18 (DEF-003): Fuel Dealer runtime DTO recovered by the last
        /// Restore when no live runtime holder existed yet (fresh Continue:
        /// the driver creates the holder lazily at registration). The driver
        /// applies it through ApplyPendingFuelDealerRuntime. Null when no
        /// runtime was saved, or once applied. A null DTO on a later Restore
        /// clears a stale pending field from an earlier load.
        /// </summary>
        public LandLedgers.Economy.Businesses.FuelDealer.FuelDealerRuntime.FuelDealerRuntimeSaveDto RestoredFuelDealerRuntime;

        /// <summary>Diagnostics from the most recent Capture, for the caller to log (Capture itself returns only the DTO, following the Pass XL coordinator pattern).</summary>
        public List<string> LastCaptureDiagnostics = new List<string>();

        /// <summary>
        /// B-18 (DEF-003): applies a pending fuel-intake DTO held by
        /// Restore, now that the live holder exists. No-op when nothing is
        /// pending or the holder is not created yet (the pending DTO stays
        /// for a later application). Clear-then-load, so applying twice is
        /// the same as applying once.
        /// </summary>
        public void ApplyPendingFuelIntake()
        {
            if (RestoredFuelIntake == null || FuelIntake == null) return;
            FuelIntake.LoadFromSaveDto(RestoredFuelIntake);
            RestoredFuelIntake = null;
        }

        /// <summary>
        /// B-18 (DEF-003): applies a pending Fuel Dealer runtime DTO held
        /// by Restore, now that the live holder exists. No-op when nothing
        /// is pending or the holder is not created yet (the pending DTO
        /// stays for a later application). Returns the refusal when the
        /// pending DTO belongs to another dealer (identity guard) — the
        /// pending DTO is dropped, never applied to the wrong business.
        /// </summary>
        public string ApplyPendingFuelDealerRuntime()
        {
            if (RestoredFuelDealerRuntime == null || FuelDealerRuntime == null) return null;
            LandLedgers.Economy.Businesses.FuelDealer.FuelDealerRuntime.FuelDealerRuntimeSaveDto dto = RestoredFuelDealerRuntime;
            RestoredFuelDealerRuntime = null;
            if (!FuelDealerRuntime.LoadFromSaveDto(dto))
                return "Series2Pass9Save: pending Fuel Dealer runtime state refused (dealer id mismatch); dealer state NOT restored.";
            return null;
        }

        /// <summary>
        /// W-G1 (Agent G, expanded campaign 2026-11-16): adopts
        /// hub-owned equipment/furnishing registers created mid-game
        /// into the Pass 9 state lists, so the next Capture persists
        /// them. The state lists are the ONLY persistence path for
        /// registers (the hub DTO does not include them) and the
        /// bootstrap seeds them only at NewGame, so a register created
        /// after NewGame — lumber yard, carpenter business, household
        /// furnishings — exists only in the hub dictionary until this
        /// sync runs. Called from SaveLoadManager.BuildSaveDto
        /// immediately before the coordinator Capture snapshots the
        /// state lists. Takes enumerables, not the hub, so it stays
        /// Unity-free and directly testable, the same pattern as the
        /// coordinator. Rules, per list independently: a hub register
        /// with no state entry of the same id is ADOPTED (the live hub
        /// object is added, so later hub mutations stay visible to
        /// Capture's deep copy) only when non-empty — business:
        /// Assets or Kits; furnishing: Assets or Bindings — so
        /// read-created empties never pollute saves; a hub register
        /// whose id already exists in the state list REPLACES it
        /// by id (live hub data is authoritative at SAVE time, the
        /// inverse of Restore, where save data is authoritative at
        /// LOAD time); a state entry missing from the hub is NEVER
        /// deleted — a missing hub entry could be a load-order
        /// artifact, and deleting would destroy saved data — it is
        /// kept and named in the diagnostic. Null-safe end to end:
        /// null enumerables or a null state list skip that list with
        /// a diagnostic; null register entries and blank business ids
        /// are skipped; never throws. Idempotent: adopt-missing plus
        /// replace-by-id, so calling sync twice before a save
        /// produces the same state, and syncing after Restore
        /// produces no duplicates. One summary line is appended to
        /// diag per call.
        /// </summary>
        public void SyncRegistersFromHub(IEnumerable<BusinessEquipmentRegister> equipment, IEnumerable<HouseholdFurnishingRegister> furnishings, List<string> diag)
        {
            diag = diag ?? new List<string>();
            int adoptedEquipment = 0;
            int replacedEquipment = 0;
            int skippedEmptyEquipment = 0;
            int keptStateOnlyEquipment = 0;
            var adoptedEquipmentIds = new List<string>();
            var keptStateOnlyEquipmentIds = new List<string>();
            int adoptedFurnishings = 0;
            int replacedFurnishings = 0;
            int skippedEmptyFurnishings = 0;
            int keptStateOnlyFurnishings = 0;
            var adoptedFurnishingIds = new List<string>();
            var keptStateOnlyFurnishingIds = new List<string>();

            if (EquipmentRegisters == null)
            {
                diag.Add("Series2Pass9SaveState.SyncRegistersFromHub: EquipmentRegisters list is null; equipment sync skipped.");
            }
            else if (equipment == null)
            {
                diag.Add("Series2Pass9SaveState.SyncRegistersFromHub: no hub equipment registers supplied; equipment sync skipped.");
            }
            else
            {
                var seenEquipmentIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (BusinessEquipmentRegister register in equipment)
                {
                    if (register == null) continue;
                    if (string.IsNullOrWhiteSpace(register.BusinessInstanceId)) continue;
                    seenEquipmentIds.Add(register.BusinessInstanceId);
                    int index = EquipmentRegisters.FindIndex(state =>
                        state != null && string.Equals(state.BusinessInstanceId, register.BusinessInstanceId, StringComparison.Ordinal));
                    if (index >= 0)
                    {
                        EquipmentRegisters[index] = register;
                        replacedEquipment++;
                    }
                    else if ((register.Assets != null && register.Assets.Count > 0)
                        || (register.Kits != null && register.Kits.Count > 0))
                    {
                        EquipmentRegisters.Add(register);
                        adoptedEquipment++;
                        adoptedEquipmentIds.Add(register.BusinessInstanceId);
                    }
                    else
                    {
                        skippedEmptyEquipment++;
                    }
                }
                foreach (BusinessEquipmentRegister stateRegister in EquipmentRegisters)
                {
                    if (stateRegister == null || string.IsNullOrWhiteSpace(stateRegister.BusinessInstanceId)) continue;
                    if (!seenEquipmentIds.Contains(stateRegister.BusinessInstanceId))
                    {
                        keptStateOnlyEquipment++;
                        keptStateOnlyEquipmentIds.Add(stateRegister.BusinessInstanceId);
                    }
                }
            }

            if (HouseholdFurnishingRegisters == null)
            {
                diag.Add("Series2Pass9SaveState.SyncRegistersFromHub: HouseholdFurnishingRegisters list is null; furnishing sync skipped.");
            }
            else if (furnishings == null)
            {
                diag.Add("Series2Pass9SaveState.SyncRegistersFromHub: no hub furnishing registers supplied; furnishing sync skipped.");
            }
            else
            {
                var seenFurnishingIds = new HashSet<int>();
                foreach (HouseholdFurnishingRegister register in furnishings)
                {
                    if (register == null) continue;
                    seenFurnishingIds.Add(register.HouseholdId);
                    int index = HouseholdFurnishingRegisters.FindIndex(state =>
                        state != null && state.HouseholdId == register.HouseholdId);
                    if (index >= 0)
                    {
                        HouseholdFurnishingRegisters[index] = register;
                        replacedFurnishings++;
                    }
                    else if ((register.Assets != null && register.Assets.Count > 0)
                        || (register.Bindings != null && register.Bindings.Count > 0))
                    {
                        HouseholdFurnishingRegisters.Add(register);
                        adoptedFurnishings++;
                        adoptedFurnishingIds.Add(register.HouseholdId.ToString());
                    }
                    else
                    {
                        skippedEmptyFurnishings++;
                    }
                }
                foreach (HouseholdFurnishingRegister stateRegister in HouseholdFurnishingRegisters)
                {
                    if (stateRegister == null) continue;
                    if (!seenFurnishingIds.Contains(stateRegister.HouseholdId))
                    {
                        keptStateOnlyFurnishings++;
                        keptStateOnlyFurnishingIds.Add(stateRegister.HouseholdId.ToString());
                    }
                }
            }

            diag.Add($"Series2Pass9SaveState.SyncRegistersFromHub: equipment adopted {adoptedEquipment} ({string.Join(", ", adoptedEquipmentIds)}), replaced {replacedEquipment}, skipped-empty {skippedEmptyEquipment}, state-only kept {keptStateOnlyEquipment} ({string.Join(", ", keptStateOnlyEquipmentIds)}); furnishings adopted {adoptedFurnishings} ({string.Join(", ", adoptedFurnishingIds)}), replaced {replacedFurnishings}, skipped-empty {skippedEmptyFurnishings}, state-only kept {keptStateOnlyFurnishings} ({string.Join(", ", keptStateOnlyFurnishingIds)}).");
        }

        /// <summary>
        /// G-4 (Agent G, expanded campaign 2026-11-16): sync the live
        /// stove fuel stores into the Pass 9 state list BEFORE the
        /// coordinator Capture snapshots them — otherwise a stove fed
        /// after NewGame is silently lost at save, because the state
        /// list is the only persistence path for fuel stores and
        /// CaptureFuelStores had no production caller (B2's item,
        /// W-G1 follow-up).
        ///
        /// Clear-then-load, deliberately NOT the adopt-only rule
        /// SyncRegistersFromHub applies to registers: fuel is fully
        /// determined by the live registries at save time, there is no
        /// authored state-only fuel (B2: old saves leave the list
        /// empty), and RestoreFuelStores is itself clear-then-load — a
        /// kept state-only entry would be fuel for a stove that no
        /// longer holds it. Null-safe end to end: a null enumerable or
        /// a null state list skips the sync with a diagnostic; null
        /// registry entries are skipped; never throws. Idempotent:
        /// syncing twice before a save produces the same state. One
        /// summary line is appended to diag per call.
        /// </summary>
        public void SyncFuelStoresFromHub(IEnumerable<HouseholdStoveRegistry> stoves, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (HouseholdStoveFuelStores == null)
            {
                diag.Add("Series2Pass9SaveState.SyncFuelStoresFromHub: HouseholdStoveFuelStores list is null; fuel sync skipped.");
                return;
            }
            if (stoves == null)
            {
                diag.Add("Series2Pass9SaveState.SyncFuelStoresFromHub: no hub stove registries supplied; fuel sync skipped.");
                return;
            }
            var captured = new List<HouseholdStoveFuelStoreDto>();
            var householdsWithFuel = new HashSet<int>();
            foreach (HouseholdStoveRegistry registry in stoves)
            {
                if (registry == null) continue;
                foreach (HouseholdStoveFuelStoreDto store in registry.CaptureFuelStores())
                {
                    if (store == null) continue;
                    captured.Add(store);
                    householdsWithFuel.Add(store.HouseholdId);
                }
            }
            HouseholdStoveFuelStores.Clear();
            HouseholdStoveFuelStores.AddRange(captured);
            diag.Add($"Series2Pass9SaveState.SyncFuelStoresFromHub: captured {captured.Count} fuel stores for {householdsWithFuel.Count} households.");
        }

        /// <summary>
        /// G-4 (Agent G, expanded campaign 2026-11-16): sync the live
        /// storage bindings into the Pass 9 state list BEFORE the
        /// coordinator Capture snapshots them — otherwise a binding
        /// made after NewGame is silently lost at save, because the
        /// state list is the only persistence path for bind parameters
        /// (B5's follow-up, correcting B5's claim that bindings
        /// re-derive after Continue).
        ///
        /// Clear-then-load, same reasoning as SyncFuelStoresFromHub:
        /// bindings are live-derived from the B0 registers, there is no
        /// authored state-only binding (TryBindFixture has no production
        /// caller yet), and the bootstrapper restore is clear-then-load
        /// per binding. Null-safe end to end; never throws. Idempotent.
        /// One summary line is appended to diag per call.
        /// </summary>
        public void SyncStorageBindingsFromHub(IEnumerable<FurnishingStorageRegistry> storages, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (HouseholdStorageBindings == null)
            {
                diag.Add("Series2Pass9SaveState.SyncStorageBindingsFromHub: HouseholdStorageBindings list is null; storage sync skipped.");
                return;
            }
            if (storages == null)
            {
                diag.Add("Series2Pass9SaveState.SyncStorageBindingsFromHub: no hub storage registries supplied; storage sync skipped.");
                return;
            }
            var captured = new List<FurnishingStorageBindingSaveDto>();
            var householdsWithBindings = new HashSet<int>();
            foreach (FurnishingStorageRegistry registry in storages)
            {
                if (registry == null) continue;
                foreach (FurnishingStorageBindingSaveDto binding in registry.CaptureBindings())
                {
                    if (binding == null) continue;
                    captured.Add(binding);
                    householdsWithBindings.Add(binding.HouseholdId);
                }
            }
            HouseholdStorageBindings.Clear();
            HouseholdStorageBindings.AddRange(captured);
            diag.Add($"Series2Pass9SaveState.SyncStorageBindingsFromHub: captured {captured.Count} storage bindings for {householdsWithBindings.Count} households.");
        }

        public Series2Pass9SaveDto Capture(List<string> diag)
        {
            diag = diag ?? new List<string>();
            LastCaptureDiagnostics = diag;
            var dto = new Series2Pass9SaveDto();

            if (SequenceDay != null)
            {
                foreach (Pass2TaskSequenceDay.SequenceEntry entry in SequenceDay.Entries)
                {
                    if (entry == null) continue;
                    dto.SequenceEntries.Add(new Pass9SequenceEntryDto
                    {
                        TaskDefinitionId = entry.TaskDefinitionId ?? string.Empty,
                        PlannedMinutes = entry.PlannedMinutes,
                        Completed = entry.Completed,
                        MissedReason = entry.MissedReason ?? string.Empty,
                    });
                }
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no Pass 2 sequence day supplied; sequence section saved empty.");
            }

            if (MissedReport != null)
            {
                dto.IdleMinutes = MissedReport.IdleMinutes;
                foreach (Pass2MissedWorkItem item in MissedReport.Items)
                {
                    if (item == null) continue;
                    dto.MissedItems.Add(new Pass9MissedItemDto
                    {
                        TaskDefinitionId = item.TaskDefinitionId ?? string.Empty,
                        OwnerDescription = item.OwnerDescription ?? string.Empty,
                        Reason = item.Reason ?? string.Empty,
                        WasDemandBalked = item.WasDemandBalked,
                    });
                }
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no Pass 2 missed-work report supplied; report section saved empty.");
            }

            if (Catalog != null)
            {
                foreach (Pass3ProductLine line in Catalog.Lines)
                {
                    if (line == null) continue;
                    dto.CatalogLines.Add(CopyLine(line));
                }
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no Pass 3 store catalog supplied; catalog section saved empty.");
            }

            foreach (Pass3OrderDraft draft in Drafts)
            {
                if (draft == null) continue;
                dto.Drafts.Add(CopyDraft(draft));
            }

            foreach (Pass3OrderRequest request in PlacedRequests)
            {
                if (request == null) continue;
                dto.PlacedRequests.Add(new Pass9OrderRequestDto
                {
                    SupplierId = request.SupplierId ?? string.Empty,
                    SkuId = request.SkuId ?? string.Empty,
                    QuantityUnits = request.QuantityUnits,
                    GoodsCents = request.GoodsCents,
                    FreightAllocatedCents = request.FreightAllocatedCents,
                    TotalCents = request.TotalCents,
                    FreightOption = (int)request.FreightOption,
                    RequiresJourney = request.RequiresJourney,
                    Status = request.Status ?? string.Empty,
                });
            }

            foreach (Pass3FuelInputOrder order in FuelInputOrders)
            {
                if (order == null) continue;
                dto.FuelInputOrders.Add(new Pass9FuelInputOrderDto
                {
                    DealerBusinessId = order.DealerBusinessId ?? string.Empty,
                    InputSkuId = order.InputSkuId ?? string.Empty,
                    Draft = order.Draft != null ? CopyDraft(order.Draft) : null,
                    ConversionRateFuelPerInputUnit = order.ConversionRateFuelPerInputUnit,
                    InputReceived = order.InputReceived,
                    InputLotId = order.InputLotId ?? string.Empty,
                });
            }

            foreach (Pass3WholesaleSettlement settlement in WholesaleSettlements)
            {
                if (settlement == null) continue;
                dto.WholesaleSettlements.Add(CopySettlement(settlement));
            }

            if (WholesaleIntake != null)
            {
                dto.WholesaleDeliveredLotIds = WholesaleIntake.DeliveredLotIds();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no wholesale intake supplied; delivered-lot guard saved empty. A production driver must supply the intake before saving, or a reload could re-intake a lot.");
            }

            if (Duty != null)
            {
                var dutyDto = new Pass9DutyDto
                {
                    PlayerPersonId = Duty.PlayerPersonId ?? string.Empty,
                    CurrentLocationId = Duty.CurrentLocationId ?? string.Empty,
                    DailyBudgetMinutes = Duty.DailyBudgetMinutes,
                    DutyRequested = Duty.DutyRequested,
                    TreatPlayerAsNpc = Duty.TreatPlayerAsNpc,
                    CurrentTaskId = Duty.CurrentTaskId ?? string.Empty,
                    CashCents = Duty.CashCents,
                    TravelDestination = Duty.TravelDestination ?? string.Empty,
                    TravelMinutesTotal = Duty.TravelMinutesTotal,
                    TravelMinutesElapsed = Duty.TravelMinutesElapsed,
                };
                foreach (Pass4DutyTask t in Duty.Tasks) dutyDto.Tasks.Add(CopyDutyTask(t));
                foreach (Pass4DutyTask t in Duty.OffloadedTasks) dutyDto.OffloadedTasks.Add(CopyDutyTask(t));
                dto.Duty = dutyDto;
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no Pass 4 duty supplied; duty section saved empty.");
            }

            if (AutoEat != null)
            {
                dto.AutoEat = new Pass9AutoEatDto
                {
                    ToggleOn = AutoEat.ToggleOn,
                    State = (int)AutoEat.State,
                    CurrentLocationId = AutoEat.CurrentLocationId ?? string.Empty,
                    CashCents = AutoEat.CashCents,
                    FailureReason = AutoEat.FailureReason ?? string.Empty,
                    SellerLocationId = AutoEat.SellerLocationId ?? string.Empty,
                    TravelMinutesTotal = AutoEat.TravelMinutesTotal,
                    TravelMinutesElapsed = AutoEat.TravelMinutesElapsed,
                };
            }

            if (AutoSleep != null)
            {
                dto.AutoSleep = new Pass9AutoSleepDto
                {
                    ToggleOn = AutoSleep.ToggleOn,
                    State = (int)AutoSleep.State,
                    CurrentLocationId = AutoSleep.CurrentLocationId ?? string.Empty,
                    CashCents = AutoSleep.CashCents,
                    FailureReason = AutoSleep.FailureReason ?? string.Empty,
                    LodgingLocationId = AutoSleep.LodgingLocationId ?? string.Empty,
                };
            }

            if (BakeryDriver != null)
            {
                dto.BakeryPlannedDayIndex = BakeryDriver.PlannedDayIndex;
                dto.BakeryPlannedBatchIds = new List<string>(BakeryDriver.PlannedBatchIds);
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no Pass 6 bakery driver supplied; planned day/batch section saved empty.");
            }

            LandLedgers.Economy.Businesses.Bakery.BakeryShopRuntime bakeryRuntime =
                BakeryRuntime ?? BakeryDriver?.Bakery;
            if (bakeryRuntime != null)
            {
                dto.BakeryRuntime = bakeryRuntime.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no live BakeryShopRuntime supplied; bakery stock and production state saved empty.");
            }

            // L1 (2026-11-17): blacksmith runtime (smith stock, horseshoes,
            // hardware, repair orders). Null holder captures as an empty
            // section, like the bakery's.
            if (BlacksmithRuntime != null)
            {
                dto.BlacksmithRuntime = BlacksmithRuntime.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no live BlacksmithRuntime supplied; smith stock, horseshoes, hardware and repair orders saved empty.");
            }

            // Plan 07 (Slice 07): staged bakery chain, with the private
            // scheduler's events folded into the chain DTO.
            if (StagedBakeryChain != null)
            {
                dto.StagedBakeryChain = StagedBakeryChain.CaptureSaveDto();
                if (StagedBakeryDueEvents != null)
                    dto.StagedBakeryChain.DueEvents = StagedBakeryDueEvents.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no live staged bakery chain supplied; staged production state saved empty.");
            }

            if (FuelIntake != null)
            {
                dto.FuelIntake = FuelIntake.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no fuel-yard intake supplied; fuel lots saved empty (the intake already had a DTO — it only needed this wiring).");
            }

            // B-18 (DEF-003): the Fuel Dealer runtime (suppliers, orders,
            // cash book, policy). A null holder captures as an empty section.
            if (FuelDealerRuntime != null)
            {
                dto.FuelDealerRuntime = FuelDealerRuntime.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no Fuel Dealer runtime supplied; dealer orders, suppliers and cash book saved empty.");
            }

            // QA-29: timber stand registry (stands + timber-rights grants).
            if (StandRegistry != null)
            {
                dto.StandRegistry = StandRegistry.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no timber stand registry supplied; stands and rights saved empty.");
            }

            // QA-29: logging operation (landing stock + in-flight log hauls;
            // hauls ride inside the operation DTO, no separate slot).
            if (LoggingOperation != null)
            {
                dto.LoggingOperation = LoggingOperation.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no logging operation supplied; landing stock and hauls saved empty.");
            }

            // QA-29: sawmill shop (log/lumber stock, byproducts, blade, tolls).
            if (Sawmill != null)
            {
                dto.Sawmill = Sawmill.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no sawmill supplied; mill stock saved empty.");
            }

            // QA-29: lumber yard shop (lumber/hardware stock, stockpile,
            // delivery register, contractor ledger, cull disposal, sales).
            if (LumberYard != null)
            {
                dto.LumberYard = LumberYard.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no lumber yard supplied; yard stock saved empty.");
            }

            // QA-29: logging camp (crew, work days, supply ledger).
            if (LoggingCamp != null)
            {
                dto.LoggingCamp = LoggingCamp.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no logging camp supplied; camp state saved empty.");
            }

            // QA-29: customer queue (waiting line + merchant identity; the
            // lost-sale log is diagnostics history and is not persisted).
            if (CustomerQueue != null)
            {
                dto.CustomerQueue = CustomerQueue.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no customer queue supplied; queue saved empty.");
            }

            if (BakeryCustomerQueue != null)
                dto.BakeryCustomerQueue = BakeryCustomerQueue.CaptureSaveDto();

            var fuelDealerQueueIds = new List<string>();
            foreach (string businessId in fuelDealerCustomerQueues.Keys)
                if (!string.IsNullOrWhiteSpace(businessId)) fuelDealerQueueIds.Add(businessId);
            foreach (string businessId in pendingFuelDealerCustomerQueues.Keys)
                if (!string.IsNullOrWhiteSpace(businessId) && !fuelDealerQueueIds.Contains(businessId))
                    fuelDealerQueueIds.Add(businessId);
            fuelDealerQueueIds.Sort(StringComparer.Ordinal);
            foreach (string businessId in fuelDealerQueueIds)
            {
                LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto queueDto = null;
                if (fuelDealerCustomerQueues.TryGetValue(businessId, out var liveQueue) && liveQueue != null)
                    queueDto = liveQueue.CaptureSaveDto();
                else if (pendingFuelDealerCustomerQueues.TryGetValue(businessId, out var pendingQueue))
                    queueDto = pendingQueue;
                if (queueDto == null) continue;
                dto.FuelDealerCustomerQueues.Add(new Pass9FuelDealerCustomerQueueEntryDto
                {
                    BusinessInstanceId = businessId,
                    Queue = queueDto,
                });
            }

            // Slice S2: equipment registers. Deep-copied, so later live
            // mutation cannot rewrite the saved section; a register with
            // no business id is skipped, never invented.
            if (EquipmentRegisters != null && EquipmentRegisters.Count > 0)
            {
                foreach (BusinessEquipmentRegister register in EquipmentRegisters)
                {
                    if (register == null || string.IsNullOrWhiteSpace(register.BusinessInstanceId)) continue;
                    dto.EquipmentRegisters.Add(CopyEquipmentRegister(register));
                }
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no business equipment registers supplied; equipment section saved empty.");
            }

            // B0: household furnishing registers. Deep-copied, so later
            // live mutation cannot rewrite the saved section.
            if (HouseholdFurnishingRegisters != null && HouseholdFurnishingRegisters.Count > 0)
            {
                foreach (HouseholdFurnishingRegister register in HouseholdFurnishingRegisters)
                {
                    if (register == null) continue;
                    dto.HouseholdFurnishingRegisters.Add(CopyHouseholdFurnishingRegister(register));
                }
            }

            foreach (FurniturePurchaseOrder order in FurniturePurchaseOrders ?? new List<FurniturePurchaseOrder>())
            {
                if (order == null) continue;
                dto.FurniturePurchaseOrders.Add(CaptureFurniturePurchase(order));
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no household furnishing registers supplied; furnishing section saved empty.");
            }

            // B2: stove fuel stores. Deep-copied, so later live mutation
            // cannot rewrite the saved section.
            if (HouseholdStoveFuelStores != null && HouseholdStoveFuelStores.Count > 0)
            {
                foreach (HouseholdStoveFuelStoreDto store in HouseholdStoveFuelStores)
                {
                    if (store == null) continue;
                    dto.HouseholdStoveFuelStores.Add(CopyHouseholdStoveFuelStore(store));
                }
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no stove fuel stores supplied; stove fuel section saved empty.");
            }

            // G-4: storage bindings. Deep-copied, so later live mutation
            // cannot rewrite the saved section.
            if (HouseholdStorageBindings != null && HouseholdStorageBindings.Count > 0)
            {
                foreach (FurnishingStorageBindingSaveDto binding in HouseholdStorageBindings)
                {
                    if (binding == null) continue;
                    dto.HouseholdStorageBindings.Add(CopyFurnishingStorageBinding(binding));
                }
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no storage bindings supplied; storage bindings section saved empty.");
            }

            if (PersonHands != null)
            {
                foreach (HandOccupancy hand in PersonHands.CaptureSnapshots())
                {
                    if (hand == null) continue;
                    dto.PersonHands.Add(new Pass9PersonHandOccupancyDto
                    {
                        PersonId = hand.PersonId,
                        LeftAuthorityId = hand.LeftLotKey.AuthorityId,
                        LeftLotId = hand.LeftLotKey.LotId,
                        LeftBatchId = hand.LeftBatchId,
                        LeftRequirement = (int)hand.LeftRequirement,
                        RightAuthorityId = hand.RightLotKey.AuthorityId,
                        RightLotId = hand.RightLotKey.LotId,
                        RightBatchId = hand.RightBatchId,
                        RightRequirement = (int)hand.RightRequirement,
                        IsTwoHandedHold = hand.IsTwoHandedHold,
                        LastRefusalReason = hand.LastRefusalReason,
                        LastRefusalOperation = hand.LastRefusalOperation,
                    });
                }
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no person hand authority supplied; person hand state saved empty.");
            }

            if (HandheldContainers != null)
            {
                foreach (HandheldContainerSnapshot container in HandheldContainers.CaptureSnapshots())
                {
                    if (container == null) continue;
                    WornContainerCapacity capacity = container.Capacity;
                    var saved = new Pass9HandheldContainerDto
                    {
                        ContainerId = container.ContainerId,
                        Kind = (int)container.Kind,
                        HeldByPersonId = container.HeldByPersonId,
                        HeldHand = container.HeldHand.HasValue ? (int)container.HeldHand.Value : -1,
                        LocationId = container.LocationId,
                        MaxUnits = capacity.MaxUnits ?? -1,
                        MassKnown = capacity.MaxMassKg.IsKnown,
                        MassKg = capacity.MaxMassKg.Value,
                        VolumeKnown = capacity.MaxVolumeLiters.IsKnown,
                        VolumeLiters = capacity.MaxVolumeLiters.Value,
                        LengthKnown = capacity.InteriorDimensions.Length.IsKnown,
                        LengthMeters = capacity.InteriorDimensions.Length.Value,
                        WidthKnown = capacity.InteriorDimensions.Width.IsKnown,
                        WidthMeters = capacity.InteriorDimensions.Width.Value,
                        HeightKnown = capacity.InteriorDimensions.Height.IsKnown,
                        HeightMeters = capacity.InteriorDimensions.Height.Value,
                    };
                    foreach (WornContainerEntry entry in container.Entries)
                    {
                        if (entry == null) continue;
                        saved.Entries.Add(new Pass9HandheldContainerEntryDto
                        {
                            AuthorityId = entry.LotKey.AuthorityId,
                            LotId = entry.LotKey.LotId,
                            BatchId = entry.BatchId,
                            ItemId = entry.ItemId,
                            Units = entry.Units,
                            Condition = (int)entry.Condition,
                        });
                    }
                    dto.HandheldContainers.Add(saved);
                }
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no handheld-container registry supplied; handheld-container section saved empty.");
            }

            // Slice S9: off-map journeys. No deep copy here — the DTO is
            // already a deep snapshot from OffMapJourneyAuthority.CaptureSaveDto.
            if (OffMapJourneys != null)
            {
                dto.OffMapJourneys = OffMapJourneys;
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no off-map journeys supplied; off-map section saved empty.");
            }

            if (FreightDriver != null)
            {
                dto.Freight = FreightDriver.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no freight production driver supplied; freight section saved empty.");
            }

            if (FuelSource != null)
            {
                dto.FuelSourceParams = new Pass9FuelSourceParamsDto
                {
                    DealerBusinessId = FuelSource.DealerBusinessId ?? string.Empty,
                    IntakeUnitsPerBakeryUnit = FuelSource.IntakeUnitsPerBakeryUnit,
                    HadOffMapReceipt = FuelSource.OffMapReceipt != null,
                };
            }

            return dto;
        }

        public void Restore(Series2Pass9SaveDto dto, FreightShipmentHooks freightHooks, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (dto == null)
            {
                diag.Add("Series2Pass9Save: no Pass 9 section in save (old save); all Pass 9 state restored as empty.");
                dto = new Series2Pass9SaveDto();
            }

            // Pass 2: rebuilt instances (no clear method exists on
            // either class, so replacing is the idempotent form).
            var day = new Pass2TaskSequenceDay();
            foreach (Pass9SequenceEntryDto entry in dto.SequenceEntries ?? new List<Pass9SequenceEntryDto>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.TaskDefinitionId)) continue;
                day.AddTask(entry.TaskDefinitionId, entry.PlannedMinutes);
                if (entry.Completed) day.MarkCompleted(entry.TaskDefinitionId);
                else if (!string.IsNullOrWhiteSpace(entry.MissedReason)) day.MarkMissed(entry.TaskDefinitionId, entry.MissedReason);
            }

            SequenceDay = day;

            var report = new Pass2MissedWorkReport();
            foreach (Pass9MissedItemDto item in dto.MissedItems ?? new List<Pass9MissedItemDto>())
            {
                if (item == null) continue;
                if (item.WasDemandBalked) report.AddBalkedDemand(item.TaskDefinitionId, item.OwnerDescription, item.Reason);
                else report.AddMissed(item.TaskDefinitionId, item.OwnerDescription, item.Reason);
            }

            if (dto.IdleMinutes > 0) report.RecordIdleMinutes(dto.IdleMinutes);
            MissedReport = report;

            // Pass 3 catalog: rebuilt instance; AddProduct zeroes
            // stock, so shelf/back are set on the stored line after.
            var catalog = new Pass3StoreCatalog();
            foreach (Pass9CatalogLineDto saved in dto.CatalogLines ?? new List<Pass9CatalogLineDto>())
            {
                if (saved == null || string.IsNullOrWhiteSpace(saved.ProductId)) continue;
                var line = new Pass3ProductLine
                {
                    ProductId = saved.ProductId,
                    DisplayName = saved.DisplayName ?? string.Empty,
                    CostCents = saved.CostCents,
                    PriceCents = saved.PriceCents,
                    ShelfCapacityUnits = saved.ShelfCapacityUnits,
                    BackCapacityUnits = saved.BackCapacityUnits,
                };
                if (!catalog.AddProduct(line, diag)) continue;
                Pass3ProductLine stored = catalog.Find(saved.ProductId);
                if (stored != null)
                {
                    stored.ShelfUnits = saved.ShelfUnits;
                    stored.BackUnits = saved.BackUnits;
                    stored.RestoreWholesaleLots((saved.WholesaleLots ?? new List<Pass9WholesaleShelfLotDto>())
                        .Where(l => l != null)
                        .Select(l => new Pass3WholesaleShelfLot
                        {
                            BakeryLotId = l.BakeryLotId ?? string.Empty,
                            BakedDayIndex = l.BakedDayIndex,
                            ReceivedDayIndex = l.ReceivedDayIndex,
                            Condition = l.Condition ?? string.Empty,
                            UnitWholesaleCostCents = l.UnitWholesaleCostCents,
                            BackUnits = l.BackUnits,
                            ShelfUnits = l.ShelfUnits,
                        }));
                }
            }

            Catalog = catalog;

            Drafts.Clear();
            foreach (Pass9OrderDraftDto saved in dto.Drafts ?? new List<Pass9OrderDraftDto>())
            {
                if (saved == null) continue;
                Drafts.Add(RebuildDraft(saved, diag));
            }

            PlacedRequests.Clear();
            foreach (Pass9OrderRequestDto saved in dto.PlacedRequests ?? new List<Pass9OrderRequestDto>())
            {
                if (saved == null) continue;
                PlacedRequests.Add(new Pass3OrderRequest
                {
                    SupplierId = saved.SupplierId ?? string.Empty,
                    SkuId = saved.SkuId ?? string.Empty,
                    QuantityUnits = saved.QuantityUnits,
                    GoodsCents = saved.GoodsCents,
                    FreightAllocatedCents = saved.FreightAllocatedCents,
                    TotalCents = saved.TotalCents,
                    FreightOption = (Pass3FreightOption)saved.FreightOption,
                    RequiresJourney = saved.RequiresJourney,
                    Status = saved.Status ?? string.Empty,
                });
            }

            FuelInputOrders.Clear();
            foreach (Pass9FuelInputOrderDto saved in dto.FuelInputOrders ?? new List<Pass9FuelInputOrderDto>())
            {
                if (saved == null) continue;
                FuelInputOrders.Add(new Pass3FuelInputOrder
                {
                    DealerBusinessId = saved.DealerBusinessId ?? string.Empty,
                    InputSkuId = saved.InputSkuId ?? string.Empty,
                    Draft = saved.Draft != null ? RebuildDraft(saved.Draft, diag) : null,
                    ConversionRateFuelPerInputUnit = saved.ConversionRateFuelPerInputUnit,
                    InputReceived = saved.InputReceived,
                    InputLotId = saved.InputLotId ?? string.Empty,
                });
            }

            WholesaleSettlements.Clear();
            foreach (Pass9WholesaleSettlementDto saved in dto.WholesaleSettlements ?? new List<Pass9WholesaleSettlementDto>())
            {
                if (saved == null) continue;
                WholesaleSettlements.Add(new Pass3WholesaleSettlement
                {
                    PayerBusinessId = saved.PayerBusinessId ?? string.Empty,
                    PayeeBusinessId = saved.PayeeBusinessId ?? string.Empty,
                    BakeryLotId = saved.BakeryLotId ?? string.Empty,
                    ProductId = saved.ProductId ?? string.Empty,
                    Units = saved.Units,
                    UnitWholesalePriceCents = saved.UnitWholesalePriceCents,
                    AmountCents = saved.AmountCents,
                    Settled = saved.Settled,
                });
            }

            if (WholesaleIntake != null)
            {
                WholesaleIntake.RestoreDeliveredLotIds(dto.WholesaleDeliveredLotIds, diag);
            }
            else
            {
                diag.Add("Series2Pass9Save: no wholesale intake supplied on restore; delivered-lot guard NOT restored. Supply the intake before restoring, or a fresh instance could re-intake a lot.");
            }

            // Pass 4: restored in place via RestoreSnapshot.
            if (Duty != null && dto.Duty != null)
            {
                var tasks = new List<Pass4DutyTask>();
                foreach (Pass9DutyTaskDto saved in dto.Duty.Tasks ?? new List<Pass9DutyTaskDto>())
                {
                    if (saved == null) continue;
                    tasks.Add(RebuildDutyTask(saved));
                }

                var offloaded = new List<Pass4DutyTask>();
                foreach (Pass9DutyTaskDto saved in dto.Duty.OffloadedTasks ?? new List<Pass9DutyTaskDto>())
                {
                    if (saved == null) continue;
                    offloaded.Add(RebuildDutyTask(saved));
                }

                Duty.RestoreSnapshot(
                    dto.Duty.CurrentLocationId, dto.Duty.DailyBudgetMinutes,
                    dto.Duty.DutyRequested, dto.Duty.TreatPlayerAsNpc,
                    dto.Duty.CurrentTaskId, dto.Duty.CashCents,
                    dto.Duty.TravelDestination, dto.Duty.TravelMinutesTotal, dto.Duty.TravelMinutesElapsed,
                    tasks, offloaded, diag);
            }
            else if (dto.Duty != null)
            {
                diag.Add("Series2Pass9Save: duty section present in save but no duty instance supplied; duty NOT restored.");
            }

            if (AutoEat != null && dto.AutoEat != null)
            {
                AutoEat.RestoreSnapshot(
                    dto.AutoEat.ToggleOn, (Pass4AutoEatState)dto.AutoEat.State,
                    dto.AutoEat.CurrentLocationId, dto.AutoEat.CashCents,
                    dto.AutoEat.FailureReason, dto.AutoEat.SellerLocationId,
                    dto.AutoEat.TravelMinutesTotal, dto.AutoEat.TravelMinutesElapsed, diag);
            }

            if (AutoSleep != null && dto.AutoSleep != null)
            {
                AutoSleep.RestoreSnapshot(
                    dto.AutoSleep.ToggleOn, (Pass4AutoSleepState)dto.AutoSleep.State,
                    dto.AutoSleep.CurrentLocationId, dto.AutoSleep.CashCents,
                    dto.AutoSleep.FailureReason, dto.AutoSleep.LodgingLocationId, diag);
            }

            // Pass 6 driver plan.
            if (BakeryDriver != null)
            {
                BakeryDriver.RestorePlannedState(dto.BakeryPlannedDayIndex, dto.BakeryPlannedBatchIds, diag);
            }
            else if (dto.BakeryPlannedBatchIds != null && dto.BakeryPlannedBatchIds.Count > 0)
            {
                diag.Add("Series2Pass9Save: bakery plan present in save but no bakery driver supplied; plan NOT restored.");
            }

            LandLedgers.Economy.Businesses.Bakery.BakeryShopRuntime bakeryRuntime =
                BakeryRuntime ?? BakeryDriver?.Bakery;
            if (bakeryRuntime != null && dto.BakeryRuntime != null)
            {
                if (!string.Equals(bakeryRuntime.BusinessInstanceId,
                        dto.BakeryRuntime.BusinessInstanceId ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    diag.Add($"Series2Pass9Save: Bakery runtime identity mismatch; live '{bakeryRuntime.BusinessInstanceId}' was not overwritten by saved '{dto.BakeryRuntime.BusinessInstanceId}'.");
                }
                else
                {
                    bakeryRuntime.LoadFromSaveDto(dto.BakeryRuntime);
                }
            }
            else if (dto.BakeryRuntime != null)
            {
                diag.Add("Series2Pass9Save: Bakery runtime data present in save but no live runtime owner was supplied; bakery state NOT restored.");
            }
            else if (bakeryRuntime != null)
            {
                // Slice 8 (Q04): the save carries no Bakery runtime
                // section (old save, or a section captured with no live
                // runtime) but a live runtime exists. Clear it instead
                // of leaving the newer state in place: LoadFromSaveDto
                // with a fresh DTO is clear-then-load
                // (BakeryShopRuntime.cs:1638 — every list is cleared,
                // flour/fuel stocks cleared, sequences reset), so the
                // live runtime restores as empty, matching the sibling
                // legs and the "all Pass 9 state restored as empty"
                // diagnostic above.
                bakeryRuntime.LoadFromSaveDto(
                    new LandLedgers.Economy.Businesses.Bakery.BakeryShopRuntime.BakeryShopRuntimeSaveDto
                    {
                        BusinessInstanceId = bakeryRuntime.BusinessInstanceId,
                    });
                diag.Add("Series2Pass9Save: no Bakery runtime section in save; live runtime cleared (old-save default).");
            }

            // L1 (2026-11-17): blacksmith runtime. Same identity-guard +
            // clear-on-missing-section rule as the bakery above: a section
            // for another smith never overwrites the live one, and a live
            // smith with no section in the save restores as empty.
            if (BlacksmithRuntime != null && dto.BlacksmithRuntime != null)
            {
                if (!string.Equals(BlacksmithRuntime.BusinessInstanceId,
                        dto.BlacksmithRuntime.BusinessInstanceId ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    diag.Add($"Series2Pass9Save: Blacksmith runtime identity mismatch; live '{BlacksmithRuntime.BusinessInstanceId}' was not overwritten by saved '{dto.BlacksmithRuntime.BusinessInstanceId}'.");
                }
                else
                {
                    BlacksmithRuntime.LoadFromSaveDto(dto.BlacksmithRuntime);
                }
            }
            else if (dto.BlacksmithRuntime != null)
            {
                diag.Add("Series2Pass9Save: Blacksmith runtime data present in save but no live runtime owner was supplied; smith state NOT restored.");
            }
            else if (BlacksmithRuntime != null)
            {
                BlacksmithRuntime.LoadFromSaveDto(
                    new LandLedgers.Economy.Blacksmith.BlacksmithRuntime.BlacksmithRuntimeSaveDto
                    {
                        BusinessInstanceId = BlacksmithRuntime.BusinessInstanceId,
                    });
                diag.Add("Series2Pass9Save: no Blacksmith runtime section in save; live runtime cleared (old-save default).");
            }

            // Plan 07 (Slice 07): do NOT rebuild hooks here (no
            // TaskAuthority in this layer). Write the DTO to the state
            // as pending; SimulationDrivers applies it on the first
            // drive after load. A null DTO clears a stale pending
            // field from an earlier load.
            RestoredStagedBakeryChain = dto.StagedBakeryChain;
            // Pass 8 fuel intake lots (DTO already existed). B-18 (DEF-003):
            // when the live holder does not exist yet (fresh Continue: the
            // driver creates it lazily), hold the DTO as pending instead of
            // dropping the lots — the driver applies it on first use through
            // ApplyPendingFuelIntake. A null DTO clears a stale pending
            // field from an earlier load.
            if (FuelIntake != null)
            {
                FuelIntake.LoadFromSaveDto(dto.FuelIntake);
                RestoredFuelIntake = null;
            }
            else if (dto.FuelIntake != null && dto.FuelIntake.Lots != null && dto.FuelIntake.Lots.Count > 0)
            {
                RestoredFuelIntake = dto.FuelIntake;
                diag.Add("Series2Pass9Save: fuel intake lots present in save but no intake supplied yet; held as pending for the driver to apply.");
            }
            else
            {
                RestoredFuelIntake = null;
            }

            // B-18 (DEF-003): the Fuel Dealer runtime. Same pending rule as
            // the intake above: restore in place when the holder exists
            // (clear-then-load, with the dealer-id identity guard), hold as
            // pending when it does not exist yet, clear a stale pending
            // field when the DTO is null.
            if (FuelDealerRuntime != null)
            {
                if (!FuelDealerRuntime.LoadFromSaveDto(dto.FuelDealerRuntime))
                    diag.Add("Series2Pass9Save: Fuel Dealer runtime state refused (dealer id mismatch); dealer state NOT restored.");
                RestoredFuelDealerRuntime = null;
            }
            else if (dto.FuelDealerRuntime != null)
            {
                RestoredFuelDealerRuntime = dto.FuelDealerRuntime;
                diag.Add("Series2Pass9Save: Fuel Dealer runtime state present in save but no runtime supplied yet; held as pending for the driver to apply.");
            }
            else
            {
                RestoredFuelDealerRuntime = null;
            }

            // QA-29: timber stand registry.
            if (StandRegistry != null)
            {
                StandRegistry.LoadFromSaveDto(dto.StandRegistry);
            }
            else if (dto.StandRegistry != null && ((dto.StandRegistry.Stands != null && dto.StandRegistry.Stands.Count > 0) || (dto.StandRegistry.RightsGrants != null && dto.StandRegistry.RightsGrants.Count > 0)))
            {
                diag.Add("Series2Pass9Save: timber stands present in save but no stand registry supplied; stands NOT restored.");
            }

            // QA-29: logging operation (landing stock + in-flight log hauls).
            if (LoggingOperation != null)
            {
                LoggingOperation.LoadFromSaveDto(dto.LoggingOperation);
            }
            else if (dto.LoggingOperation != null && ((dto.LoggingOperation.LandingStock != null && dto.LoggingOperation.LandingStock.Count > 0) || (dto.LoggingOperation.Hauls != null && dto.LoggingOperation.Hauls.Count > 0)))
            {
                diag.Add("Series2Pass9Save: logging operation state present in save but no logging operation supplied; landing stock and hauls NOT restored.");
            }

            // QA-29: sawmill shop.
            if (Sawmill != null)
            {
                Sawmill.LoadFromSaveDto(dto.Sawmill);
            }
            else if (dto.Sawmill != null && dto.Sawmill.LogStock != null && dto.Sawmill.LogStock.Lots != null && dto.Sawmill.LogStock.Lots.Count > 0)
            {
                diag.Add("Series2Pass9Save: sawmill log stock present in save but no sawmill supplied; mill stock NOT restored.");
            }

            // QA-29 / C2: lumber yard shop. Identity-gated like the bakery
            // above: a DTO for a different business id must not overwrite
            // the live yard.
            if (LumberYard != null && dto.LumberYard != null)
            {
                if (!string.Equals(LumberYard.BusinessInstanceId,
                        dto.LumberYard.BusinessInstanceId ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    diag.Add($"Series2Pass9Save: Lumber yard runtime identity mismatch; live '{LumberYard.BusinessInstanceId}' was not overwritten by saved '{dto.LumberYard.BusinessInstanceId}'.");
                }
                else
                {
                    LumberYard.LoadFromSaveDto(dto.LumberYard);
                }
            }
            else if (LumberYard != null)
            {
                LumberYard.LoadFromSaveDto(dto.LumberYard); // null dto clears honestly (old save).
            }
            else if (dto.LumberYard != null && dto.LumberYard.LumberStock != null && dto.LumberYard.LumberStock.Lots != null && dto.LumberYard.LumberStock.Lots.Count > 0)
            {
                diag.Add("Series2Pass9Save: lumber yard stock present in save but no lumber yard supplied; yard stock NOT restored.");
            }

            // QA-29: logging camp.
            if (LoggingCamp != null)
            {
                LoggingCamp.LoadFromSaveDto(dto.LoggingCamp);
            }
            else if (dto.LoggingCamp != null && ((dto.LoggingCamp.Crew != null && dto.LoggingCamp.Crew.Count > 0) || (dto.LoggingCamp.WorkDays != null && dto.LoggingCamp.WorkDays.Count > 0)))
            {
                diag.Add("Series2Pass9Save: logging camp state present in save but no logging camp supplied; camp state NOT restored.");
            }

            // QA-29: customer queue.
            if (CustomerQueue != null)
            {
                CustomerQueue.LoadFromSaveDto(dto.CustomerQueue);
            }
            else if (dto.CustomerQueue != null && dto.CustomerQueue.Waiting != null && dto.CustomerQueue.Waiting.Count > 0)
            {
                // Campaign C (DEF-028): defer, do not drop. The bind
                // (BuildHouseholdShoppingLoop, Rebind) drains this into
                // the live queue when it appears.
                pendingCustomerQueue = dto.CustomerQueue;
                diag.Add("Series2Pass9Save: customer queue present in save but no customer queue supplied yet; queue held pending the queue bind (DEF-028), not dropped.");
            }

            if (BakeryCustomerQueue != null)
                BakeryCustomerQueue.LoadFromSaveDto(dto.BakeryCustomerQueue);
            else if (dto.BakeryCustomerQueue != null
                && dto.BakeryCustomerQueue.Waiting != null
                && dto.BakeryCustomerQueue.Waiting.Count > 0)
            {
                pendingBakeryCustomerQueue = dto.BakeryCustomerQueue;
                diag.Add("Series2Pass9Save: bakery counter queue present before bakery service binding; waiting line held pending, not dropped.");
            }

            pendingFuelDealerCustomerQueues.Clear();
            var savedFuelDealerQueues = new Dictionary<string, LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto>(StringComparer.Ordinal);
            foreach (Pass9FuelDealerCustomerQueueEntryDto entry in dto.FuelDealerCustomerQueues
                ?? new List<Pass9FuelDealerCustomerQueueEntryDto>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.BusinessInstanceId) || entry.Queue == null)
                {
                    diag.Add("Series2Pass9Save: invalid fuel-dealer customer queue entry ignored; no business identity or queue data.");
                    continue;
                }
                if (savedFuelDealerQueues.ContainsKey(entry.BusinessInstanceId))
                {
                    diag.Add($"Series2Pass9Save: duplicate fuel-dealer customer queue for '{entry.BusinessInstanceId}' ignored; first entry retained.");
                    continue;
                }
                // The wrapper identity is the durable owner. A malformed or
                // older nested DTO must not bind this waiting line to a
                // different dealer after Continue. Normalize into a fresh
                // queue snapshot so restore never aliases the caller's DTO.
                var normalizedQueue = new LandLedgers.Economy.GeneralStore.CustomerQueue();
                normalizedQueue.LoadFromSaveDto(entry.Queue);
                if (!string.IsNullOrWhiteSpace(entry.Queue.MerchantBusinessId)
                    && !string.Equals(entry.BusinessInstanceId, entry.Queue.MerchantBusinessId, StringComparison.Ordinal))
                {
                    diag.Add($"Series2Pass9Save: fuel-dealer queue identity '{entry.Queue.MerchantBusinessId}' disagrees with owning entry '{entry.BusinessInstanceId}'; entry identity retained.");
                }
                normalizedQueue.MerchantBusinessId = entry.BusinessInstanceId;
                savedFuelDealerQueues.Add(entry.BusinessInstanceId, normalizedQueue.CaptureSaveDto());
            }

            foreach (KeyValuePair<string, LandLedgers.Economy.GeneralStore.CustomerQueue> pair in fuelDealerCustomerQueues)
            {
                if (pair.Value == null) continue;
                if (savedFuelDealerQueues.TryGetValue(pair.Key, out var savedQueue))
                {
                    pair.Value.LoadFromSaveDto(savedQueue);
                    savedFuelDealerQueues.Remove(pair.Key);
                }
                else
                {
                    pair.Value.LoadFromSaveDto(new LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto
                    {
                        MerchantBusinessId = pair.Key,
                        MerchantName = pair.Value.MerchantName ?? string.Empty,
                    });
                }
            }

            foreach (KeyValuePair<string, LandLedgers.Economy.GeneralStore.CustomerQueue.CustomerQueueSaveDto> pair in savedFuelDealerQueues)
            {
                pendingFuelDealerCustomerQueues[pair.Key] = pair.Value;
                diag.Add($"Series2Pass9Save: fuel-dealer queue for '{pair.Key}' restored before service binding; queue held pending, not dropped.");
            }

            // Slice S2: equipment registers — clear-then-load, idempotent.
            // Old saves carry no section and restore as empty registers;
            // restoring twice rebuilds the same registers, never duplicates.
            // The bootstrap re-registers these into the hub after a load.
            EquipmentRegisters.Clear();
            foreach (BusinessEquipmentRegister saved in dto.EquipmentRegisters ?? new List<BusinessEquipmentRegister>())
            {
                if (saved == null || string.IsNullOrWhiteSpace(saved.BusinessInstanceId)) continue;
                EquipmentRegisters.Add(CopyEquipmentRegister(saved));
            }

            // B0: household furnishing registers — clear-then-load,
            // idempotent. Old saves carry no section and restore as empty
            // registers; restoring twice rebuilds the same registers,
            // never duplicates. The bootstrap re-registers these into the
            // hub after a load.
            HouseholdFurnishingRegisters.Clear();
            foreach (HouseholdFurnishingRegister saved in dto.HouseholdFurnishingRegisters ?? new List<HouseholdFurnishingRegister>())
            {
                if (saved == null) continue;
                HouseholdFurnishingRegisters.Add(CopyHouseholdFurnishingRegister(saved));
            }

            FurniturePurchaseOrders.Clear();
            foreach (Pass9FurniturePurchaseOrderDto saved in dto.FurniturePurchaseOrders ?? new List<Pass9FurniturePurchaseOrderDto>())
            {
                if (saved == null || string.IsNullOrWhiteSpace(saved.PurchaseId)) continue;
                FurniturePurchaseOrder order = RestoreFurniturePurchase(saved);
                foreach (HouseholdFurnishingRegister register in HouseholdFurnishingRegisters)
                {
                    if (register == null || register.HouseholdId != order.HouseholdId) continue;
                    foreach (FurniturePurchaseLine line in order.Lines)
                    {
                        if (line?.AssetIds == null) continue;
                        foreach (string assetId in line.AssetIds)
                        {
                            EquipmentAsset asset = register.FindAsset(assetId);
                            if (asset != null && !order.ReceivedAssets.Any(existing => existing != null && existing.AssetId == asset.AssetId))
                                order.ReceivedAssets.Add(asset);
                        }
                    }
                }
                FurniturePurchaseOrders.Add(order);
            }

            // B2: stove fuel stores — clear-then-load, idempotent. Old
            // saves carry no section and restore as empty stores;
            // restoring twice rebuilds the same stores, never duplicates.
            HouseholdStoveFuelStores.Clear();
            foreach (HouseholdStoveFuelStoreDto saved in dto.HouseholdStoveFuelStores ?? new List<HouseholdStoveFuelStoreDto>())
            {
                if (saved == null) continue;
                HouseholdStoveFuelStores.Add(CopyHouseholdStoveFuelStore(saved));
            }

            // G-4: storage bindings — clear-then-load, idempotent. Old
            // saves carry no section and restore as no bindings;
            // restoring twice rebuilds the same bindings, never duplicates.
            HouseholdStorageBindings.Clear();
            foreach (FurnishingStorageBindingSaveDto saved in dto.HouseholdStorageBindings ?? new List<FurnishingStorageBindingSaveDto>())
            {
                if (saved == null) continue;
                HouseholdStorageBindings.Add(CopyFurnishingStorageBinding(saved));
            }

            if (PersonHands != null)
            {
                var restoredHands = new List<HandOccupancy>();
                foreach (Pass9PersonHandOccupancyDto saved in dto.PersonHands ?? new List<Pass9PersonHandOccupancyDto>())
                {
                    if (saved == null) continue;
                    if (!Enum.IsDefined(typeof(HandRequirement), saved.LeftRequirement)
                        || !Enum.IsDefined(typeof(HandRequirement), saved.RightRequirement))
                    {
                        diag.Add("Series2Pass9Save: invalid person hand requirement enum; affected person state was not restored.");
                        continue;
                    }
                    restoredHands.Add(new HandOccupancy(
                        saved.PersonId,
                        new LotKey(saved.LeftAuthorityId, saved.LeftLotId), saved.LeftBatchId,
                        new LotKey(saved.RightAuthorityId, saved.RightLotId), saved.RightBatchId,
                        saved.IsTwoHandedHold,
                        (HandRequirement)saved.LeftRequirement,
                        (HandRequirement)saved.RightRequirement,
                        saved.LastRefusalReason,
                        saved.LastRefusalOperation));
                }
                if (!PersonHands.RestoreSnapshots(restoredHands, out string handRestoreRefusal))
                    diag.Add(handRestoreRefusal);
            }
            else if (dto.PersonHands != null && dto.PersonHands.Count > 0)
            {
                diag.Add("Series2Pass9Save: person hand data exists but no live person hand authority was supplied; state was not restored.");
            }

            if (HandheldContainers != null)
            {
                var restoredContainers = new List<HandheldContainerSnapshot>();
                foreach (Pass9HandheldContainerDto saved in dto.HandheldContainers ?? new List<Pass9HandheldContainerDto>())
                {
                    if (saved == null || !Enum.IsDefined(typeof(HandheldContainerKind), saved.Kind)
                        || (saved.HeldHand != -1 && !Enum.IsDefined(typeof(HandSlot), saved.HeldHand)))
                    {
                        diag.Add("Series2Pass9Save: invalid handheld-container kind or hand enum; affected container was not restored.");
                        continue;
                    }

                    var entries = new List<WornContainerEntry>();
                    bool validEntries = true;
                    foreach (Pass9HandheldContainerEntryDto entry in saved.Entries ?? new List<Pass9HandheldContainerEntryDto>())
                    {
                        if (entry == null || !Enum.IsDefined(typeof(PersonItemCondition), entry.Condition))
                        {
                            diag.Add("Series2Pass9Save: invalid handheld-container entry condition; affected container was not restored.");
                            validEntries = false;
                            break;
                        }
                        entries.Add(new WornContainerEntry(
                            new LotKey(entry.AuthorityId, entry.LotId), entry.BatchId, entry.ItemId,
                            entry.Units, (PersonItemCondition)entry.Condition));
                    }
                    if (!validEntries) continue;

                    var capacity = new WornContainerCapacity(
                        saved.MaxUnits < 0 ? (int?)null : saved.MaxUnits,
                        RestoreMeasure(saved.MassKnown, saved.MassKg),
                        RestoreMeasure(saved.VolumeKnown, saved.VolumeLiters),
                        new PhysicalDimensions(
                            RestoreMeasure(saved.LengthKnown, saved.LengthMeters),
                            RestoreMeasure(saved.WidthKnown, saved.WidthMeters),
                            RestoreMeasure(saved.HeightKnown, saved.HeightMeters)));
                    restoredContainers.Add(new HandheldContainerSnapshot(
                        saved.ContainerId, (HandheldContainerKind)saved.Kind, saved.HeldByPersonId,
                        saved.HeldHand == -1 ? (HandSlot?)null : (HandSlot)saved.HeldHand,
                        capacity, entries, saved.LocationId));
                }

                if (!HandheldContainers.RestoreSnapshots(restoredContainers, out string containerRestoreRefusal))
                    diag.Add(containerRestoreRefusal);
            }
            else if (dto.HandheldContainers != null && dto.HandheldContainers.Count > 0)
            {
                diag.Add("Series2Pass9Save: handheld-container state exists but no live container registry was supplied; state was not restored.");
            }

            // Slice S9: off-map journeys. Assignment IS the clear-then-load
            // form for a single DTO; a null section restores as null
            // (no journeys — the old-save default).
            OffMapJourneys = dto.OffMapJourneys;

            // Pass 8 freight: shipments round-trip in the DTO
            // (including deliveryApplied and
            // freightRevenueCollected). The settlement set is
            // rebuilt from those flags, never from a second list.
            List<LogisticsShipmentState> restoredShipments = new List<LogisticsShipmentState>();
            if (FreightDriver != null)
            {
                var freightDiag = new List<string>();
                restoredShipments = FreightDriver.RestoreSaveDto(dto.Freight, freightHooks, freightDiag) ?? new List<LogisticsShipmentState>();
                diag.AddRange(freightDiag);
            }
            else if (dto.Freight != null && dto.Freight.Shipments != null && dto.Freight.Shipments.Count > 0)
            {
                diag.Add("Series2Pass9Save: freight shipments present in save but no freight driver supplied; freight NOT restored, and the carrier settlement set cannot be rebuilt from them.");
            }

            if (CarrierSettlement != null)
            {
                CarrierSettlement.RestoreFromShipments(restoredShipments);
            }

            RestoredFuelSourceParams = dto.FuelSourceParams != null
                ? new Pass9FuelSourceParamsDto
                {
                    DealerBusinessId = dto.FuelSourceParams.DealerBusinessId ?? string.Empty,
                    IntakeUnitsPerBakeryUnit = dto.FuelSourceParams.IntakeUnitsPerBakeryUnit,
                    HadOffMapReceipt = dto.FuelSourceParams.HadOffMapReceipt,
                }
                : null;
            if (RestoredFuelSourceParams != null && RestoredFuelSourceParams.HadOffMapReceipt)
            {
                diag.Add("Series2Pass9Save: fuel source had an off-map receipt at save time. The delegate itself is not serializable: Codex must rewire it when constructing the new fuel source from RestoredFuelSourceParams, or off-map fallback stays unwired after load.");
            }

            diag.Add("Series2Pass9Save: restore complete (clear-then-load, idempotent). Adapter DayIndex is NOT restored by design — the day driver sets it at day start. Pass 5 leads/visits are NOT in this section — Pass 10 scope.");
        }

        /// <summary>
        /// Slice S2: deep copy of one equipment register (assets + kits),
        /// so the saved section and the restored state never alias the
        /// live register. Every field is carried: a copy that dropped
        /// condition, ownership, or provenance would silently re-issue
        /// equipment the business had worn, sold, or never owned.
        /// </summary>
        private static PhysicalMeasure RestoreMeasure(bool known, float value)
        {
            return known ? PhysicalMeasure.Known(value) : PhysicalMeasure.Unknown;
        }

        private static BusinessEquipmentRegister CopyEquipmentRegister(BusinessEquipmentRegister source)
        {
            var copy = new BusinessEquipmentRegister
            {
                BusinessInstanceId = source.BusinessInstanceId ?? string.Empty,
            };
            if (source.Assets != null)
                foreach (EquipmentAsset asset in source.Assets)
                {
                    if (asset == null) continue;
                    copy.Assets.Add(CopyEquipmentAsset(asset));
                }
            if (source.Kits != null)
                foreach (ToolKitInstance kit in source.Kits)
                {
                    if (kit == null) continue;
                    copy.Kits.Add(new ToolKitInstance
                    {
                        InstanceId = kit.InstanceId ?? string.Empty,
                        KitId = kit.KitId ?? string.Empty,
                        Condition01 = kit.Condition01,
                        OwnerKind = kit.OwnerKind ?? string.Empty,
                        OwnerId = kit.OwnerId ?? string.Empty,
                        LocationId = kit.LocationId ?? string.Empty,
                        ReservedBy = kit.ReservedBy ?? string.Empty,
                        ReservedFor = kit.ReservedFor ?? string.Empty,
                        MaintenanceLog = new List<string>(kit.MaintenanceLog ?? new List<string>()),
                    });
                }
            return copy;
        }

        private static HouseholdStoveFuelStoreDto CopyHouseholdStoveFuelStore(HouseholdStoveFuelStoreDto source)
        {
            return new HouseholdStoveFuelStoreDto
            {
                HouseholdId = source.HouseholdId,
                StoveAssetId = source.StoveAssetId ?? string.Empty,
                FuelWoodUnits = source.FuelWoodUnits,
            };
        }

        private static FurnishingStorageBindingSaveDto CopyFurnishingStorageBinding(FurnishingStorageBindingSaveDto source)
        {
            var copy = new FurnishingStorageBindingSaveDto
            {
                HouseholdId = source.HouseholdId,
                AssetId = source.AssetId ?? string.Empty,
                ZoneId = source.ZoneId ?? string.Empty,
                BuildingInstanceId = source.BuildingInstanceId,
                PlacementId = source.PlacementId ?? string.Empty,
                Visibility = source.Visibility,
                DisplayMeshCap = source.DisplayMeshCap,
                AllowedReserveCategoryId = source.AllowedReserveCategoryId ?? string.Empty,
                AllowedItemIds = new List<string>(source.AllowedItemIds ?? new List<string>()),
                MassCapacity = source.MassCapacity,
                VolumeCapacity = source.VolumeCapacity,
            };
            if (source.PlacedLots != null)
                foreach (StoredLotReference placed in source.PlacedLots)
                {
                    if (placed == null) continue;
                    copy.PlacedLots.Add(new StoredLotReference(
                        placed.InventoryAuthorityId, placed.LotId, placed.Load));
                }
            return copy;
        }

        private static HouseholdFurnishingRegister CopyHouseholdFurnishingRegister(HouseholdFurnishingRegister source)
        {
            var copy = new HouseholdFurnishingRegister
            {
                HouseholdId = source.HouseholdId,
            };
            if (source.Assets != null)
                foreach (EquipmentAsset asset in source.Assets)
                {
                    if (asset == null) continue;
                    copy.Assets.Add(CopyEquipmentAsset(asset));
                }
            if (source.Bindings != null)
                foreach (FurnishingPlacementBinding binding in source.Bindings)
                {
                    if (binding == null) continue;
                    copy.Bindings.Add(CopyFurnishingPlacementBinding(binding));
                }
            return copy;
        }

        private static FurnishingPlacementBinding CopyFurnishingPlacementBinding(FurnishingPlacementBinding source)
        {
            return new FurnishingPlacementBinding
            {
                BindingId = source.BindingId ?? string.Empty,
                AssetId = source.AssetId ?? string.Empty,
                BuildingInstanceId = source.BuildingInstanceId,
                PlacementId = source.PlacementId ?? string.Empty,
                FloorIndex = source.FloorIndex,
                LocalPosition = source.LocalPosition,
                LocalRotation = source.LocalRotation,
                DefinitionId = source.DefinitionId ?? string.Empty,
                CapabilityKind = source.CapabilityKind,
                CapabilityReferenceId = source.CapabilityReferenceId ?? string.Empty,
                AcquisitionSource = source.AcquisitionSource,
                AcquisitionSourceId = source.AcquisitionSourceId ?? string.Empty,
                ReservationId = source.ReservationId ?? string.Empty,
            };
        }

        private static EquipmentAsset CopyEquipmentAsset(EquipmentAsset source)
        {
            var copy = new EquipmentAsset
            {
                AssetId = source.AssetId ?? string.Empty,
                Kind = source.Kind ?? string.Empty,
                DisplayName = source.DisplayName ?? string.Empty,
                Condition01 = source.Condition01,
                OwnerKind = source.OwnerKind ?? string.Empty,
                OwnerId = source.OwnerId ?? string.Empty,
                LocationId = source.LocationId ?? string.Empty,
                ReservedBy = source.ReservedBy ?? string.Empty,
                ReservedFor = source.ReservedFor ?? string.Empty,
                MadeByBusinessId = source.MadeByBusinessId ?? string.Empty,
                MadeByBusinessName = source.MadeByBusinessName ?? string.Empty,
                MaterialLotIds = new List<string>(source.MaterialLotIds ?? new List<string>()),
                MadeDayIndex = source.MadeDayIndex,
                MaintenanceLog = new List<string>(source.MaintenanceLog ?? new List<string>()),
                WagonSubtype = source.WagonSubtype ?? string.Empty,
                PayloadMassCapacityKg = source.PayloadMassCapacityKg,
                CargoVolumeCapacityM3 = source.CargoVolumeCapacityM3,
                StorageLocationId = source.StorageLocationId ?? string.Empty,
                DraftAnimalIds = new List<string>(source.DraftAnimalIds ?? new List<string>()),
                CurrentDriverPersonId = source.CurrentDriverPersonId ?? string.Empty,
                CurrentShipmentId = source.CurrentShipmentId ?? string.Empty,
                HitchingAnimalIds = new List<string>(source.HitchingAnimalIds ?? new List<string>()),
                HitchPreparationTaskId = source.HitchPreparationTaskId ?? string.Empty,
                HitchPreparationTravelTaskId = source.HitchPreparationTravelTaskId ?? string.Empty,
                HitchPreparationReservationId = source.HitchPreparationReservationId ?? string.Empty,
                HitchPreparationShipmentId = source.HitchPreparationShipmentId ?? string.Empty,
                HitchPreparationAttempted = source.HitchPreparationAttempted,
                CanvasMaterialVariantIndex = source.CanvasMaterialVariantIndex,
            };
            if (source.OwnershipHistory != null)
                foreach (EquipmentOwnershipRecord record in source.OwnershipHistory)
                {
                    if (record == null) continue;
                    copy.OwnershipHistory.Add(new EquipmentOwnershipRecord
                    {
                        FromOwnerKind = record.FromOwnerKind ?? string.Empty,
                        FromOwnerId = record.FromOwnerId ?? string.Empty,
                        ToOwnerKind = record.ToOwnerKind ?? string.Empty,
                        ToOwnerId = record.ToOwnerId ?? string.Empty,
                        DayIndex = record.DayIndex,
                        Reason = record.Reason ?? string.Empty,
                    });
                }
            return copy;
        }

        private static Pass9CatalogLineDto CopyLine(Pass3ProductLine line)
        {
            var dto = new Pass9CatalogLineDto
            {
                ProductId = line.ProductId ?? string.Empty,
                DisplayName = line.DisplayName ?? string.Empty,
                CostCents = line.CostCents,
                PriceCents = line.PriceCents,
                ShelfUnits = line.ShelfUnits,
                BackUnits = line.BackUnits,
                ShelfCapacityUnits = line.ShelfCapacityUnits,
                BackCapacityUnits = line.BackCapacityUnits,
            };
            foreach (Pass3WholesaleShelfLot lot in line.WholesaleLots ?? new List<Pass3WholesaleShelfLot>())
            {
                if (lot == null) continue;
                dto.WholesaleLots.Add(new Pass9WholesaleShelfLotDto
                {
                    BakeryLotId = lot.BakeryLotId ?? string.Empty,
                    BakedDayIndex = lot.BakedDayIndex,
                    ReceivedDayIndex = lot.ReceivedDayIndex,
                    Condition = lot.Condition ?? string.Empty,
                    UnitWholesaleCostCents = lot.UnitWholesaleCostCents,
                    BackUnits = lot.BackUnits,
                    ShelfUnits = lot.ShelfUnits,
                });
            }
            return dto;
        }

        private static Pass9SupplierOfferDto CopySupplier(Pass3SupplierOffer supplier)
        {
            var copy = new Pass9SupplierOfferDto
            {
                SupplierId = supplier.SupplierId ?? string.Empty,
                SupplierName = supplier.SupplierName ?? string.Empty,
                DistanceMiles = supplier.DistanceMiles,
                IsPickupOnly = supplier.IsPickupOnly,
                MinimumAmountCents = supplier.MinimumAmountCents,
                MinimumQuantityUnits = supplier.MinimumQuantityUnits,
                MinimumPoundageLb = supplier.MinimumPoundageLb,
            };
            if (supplier.SkuOffers != null)
            {
                foreach (Pass3SkuOffer offer in supplier.SkuOffers)
                {
                    if (offer == null) continue;
                    copy.SkuOffers.Add(new Pass9SkuOfferDto
                    {
                        SkuId = offer.SkuId ?? string.Empty,
                        UnitPriceCents = offer.UnitPriceCents,
                        UnitWeightLb = offer.UnitWeightLb,
                    });
                }
            }

            return copy;
        }

        private static Pass9OrderDraftDto CopyDraft(Pass3OrderDraft draft)
        {
            var copy = new Pass9OrderDraftDto
            {
                Supplier = draft.Supplier != null ? CopySupplier(draft.Supplier) : null,
                FreightOption = (int)draft.FreightOption,
                FreightCentsPerMile = draft.FreightCentsPerMile,
                FreightBusinessCommissionCents = draft.FreightBusinessCommissionCents,
                Actor = (int)draft.Actor,
                IsAutomatic = draft.IsAutomatic,
                DelegationAllowed = draft.DelegationAllowed,
            };
            foreach (Pass3OrderLine line in draft.Lines)
            {
                if (line == null) continue;
                copy.Lines.Add(new Pass9OrderLineDto
                {
                    SkuId = line.SkuId ?? string.Empty,
                    QuantityUnits = line.QuantityUnits,
                    UnitPriceCents = line.UnitPriceCents,
                    UnitWeightLb = line.UnitWeightLb,
                });
            }

            return copy;
        }

        private static Pass3OrderDraft RebuildDraft(Pass9OrderDraftDto saved, List<string> diag)
        {
            var draft = new Pass3OrderDraft
            {
                FreightOption = (Pass3FreightOption)saved.FreightOption,
                FreightCentsPerMile = saved.FreightCentsPerMile,
                FreightBusinessCommissionCents = saved.FreightBusinessCommissionCents,
                Actor = (Pass3ActorKind)saved.Actor,
                IsAutomatic = saved.IsAutomatic,
                DelegationAllowed = saved.DelegationAllowed,
            };
            if (saved.Supplier != null)
            {
                var supplier = new Pass3SupplierOffer
                {
                    SupplierId = saved.Supplier.SupplierId ?? string.Empty,
                    SupplierName = saved.Supplier.SupplierName ?? string.Empty,
                    DistanceMiles = saved.Supplier.DistanceMiles,
                    IsPickupOnly = saved.Supplier.IsPickupOnly,
                    MinimumAmountCents = saved.Supplier.MinimumAmountCents,
                    MinimumQuantityUnits = saved.Supplier.MinimumQuantityUnits,
                    MinimumPoundageLb = saved.Supplier.MinimumPoundageLb,
                };
                foreach (Pass9SkuOfferDto offer in saved.Supplier.SkuOffers ?? new List<Pass9SkuOfferDto>())
                {
                    if (offer == null) continue;
                    supplier.SkuOffers.Add(new Pass3SkuOffer
                    {
                        SkuId = offer.SkuId ?? string.Empty,
                        UnitPriceCents = offer.UnitPriceCents,
                        UnitWeightLb = offer.UnitWeightLb,
                    });
                }

                draft.Supplier = supplier;
            }

            foreach (Pass9OrderLineDto line in saved.Lines ?? new List<Pass9OrderLineDto>())
            {
                if (line == null) continue;
                draft.Lines.Add(new Pass3OrderLine
                {
                    SkuId = line.SkuId ?? string.Empty,
                    QuantityUnits = line.QuantityUnits,
                    UnitPriceCents = line.UnitPriceCents,
                    UnitWeightLb = line.UnitWeightLb,
                });
            }

            return draft;
        }

        private static Pass9WholesaleSettlementDto CopySettlement(Pass3WholesaleSettlement settlement)
        {
            return new Pass9WholesaleSettlementDto
            {
                PayerBusinessId = settlement.PayerBusinessId ?? string.Empty,
                PayeeBusinessId = settlement.PayeeBusinessId ?? string.Empty,
                BakeryLotId = settlement.BakeryLotId ?? string.Empty,
                ProductId = settlement.ProductId ?? string.Empty,
                Units = settlement.Units,
                UnitWholesalePriceCents = settlement.UnitWholesalePriceCents,
                AmountCents = settlement.AmountCents,
                Settled = settlement.Settled,
            };
        }

        private static Pass9DutyTaskDto CopyDutyTask(Pass4DutyTask task)
        {
            if (task == null) return new Pass9DutyTaskDto();
            return new Pass9DutyTaskDto
            {
                TaskDefinitionId = task.TaskDefinitionId ?? string.Empty,
                PlannedMinutes = task.PlannedMinutes,
                Owner = task.Owner ?? string.Empty,
                Completed = task.Completed,
                InProgress = task.InProgress,
                MissedReason = task.MissedReason ?? string.Empty,
                DutyAssigned = task.DutyAssigned,
            };
        }

        private static Pass4DutyTask RebuildDutyTask(Pass9DutyTaskDto saved)
        {
            return new Pass4DutyTask
            {
                TaskDefinitionId = saved.TaskDefinitionId ?? string.Empty,
                PlannedMinutes = saved.PlannedMinutes,
                Owner = saved.Owner ?? string.Empty,
                Completed = saved.Completed,
                InProgress = saved.InProgress,
                MissedReason = saved.MissedReason ?? string.Empty,
                DutyAssigned = saved.DutyAssigned,
            };
        }

        private static Pass9FurniturePurchaseOrderDto CaptureFurniturePurchase(FurniturePurchaseOrder order)
        {
            var saved = new Pass9FurniturePurchaseOrderDto
            {
                PurchaseId = order.PurchaseId ?? string.Empty,
                Kind = (int)order.Kind,
                HouseholdId = order.HouseholdId,
                SellerHouseholdId = order.SellerHouseholdId,
                VendorId = order.VendorId ?? string.Empty,
                VendorName = order.VendorName ?? string.Empty,
                BuildingInstanceId = order.BuildingInstanceId,
                DayIndex = order.DayIndex,
                FreightCents = order.FreightCents,
                ShipmentId = order.ShipmentId ?? string.Empty,
                InstallationBlockedReason = order.InstallationBlockedReason ?? string.Empty,
                CancellationRequested = order.CancellationRequested,
                TerminalReason = order.TerminalReason ?? string.Empty,
                Diagnostics = new List<string>(order.Diagnostics ?? new List<string>()),
            };
            foreach (FurniturePurchaseLine line in order.Lines ?? new List<FurniturePurchaseLine>())
            {
                if (line == null) continue;
                saved.Lines.Add(new Pass9FurniturePurchaseLineDto
                {
                    ItemId = line.ItemId ?? string.Empty,
                    Units = line.Units,
                    SourceLotId = line.SourceLotId ?? string.Empty,
                    UnitPriceCents = line.UnitPriceCents,
                    Stage = (int)line.Stage,
                    AssetIds = new List<string>(line.AssetIds ?? new List<string>()),
                });
            }
            return saved;
        }

        private static FurniturePurchaseOrder RestoreFurniturePurchase(Pass9FurniturePurchaseOrderDto saved)
        {
            var order = new FurniturePurchaseOrder
            {
                PurchaseId = saved.PurchaseId ?? string.Empty,
                Kind = Enum.IsDefined(typeof(FurniturePurchaseKind), saved.Kind)
                    ? (FurniturePurchaseKind)saved.Kind : FurniturePurchaseKind.New,
                HouseholdId = saved.HouseholdId,
                SellerHouseholdId = saved.SellerHouseholdId,
                VendorId = saved.VendorId ?? string.Empty,
                VendorName = saved.VendorName ?? string.Empty,
                BuildingInstanceId = saved.BuildingInstanceId,
                DayIndex = saved.DayIndex,
                FreightCents = Math.Max(0, saved.FreightCents),
                ShipmentId = saved.ShipmentId ?? string.Empty,
                InstallationBlockedReason = saved.InstallationBlockedReason ?? string.Empty,
                CancellationRequested = saved.CancellationRequested,
                TerminalReason = saved.TerminalReason ?? string.Empty,
                Diagnostics = new List<string>(saved.Diagnostics ?? new List<string>()),
            };
            foreach (Pass9FurniturePurchaseLineDto line in saved.Lines ?? new List<Pass9FurniturePurchaseLineDto>())
            {
                if (line == null) continue;
                int stage = line.Stage;
                if (!Enum.IsDefined(typeof(EquipmentPurchaseStage), stage)) stage = (int)EquipmentPurchaseStage.Quoted;
                order.Lines.Add(new FurniturePurchaseLine
                {
                    ItemId = line.ItemId ?? string.Empty,
                    Units = Math.Max(0, line.Units),
                    SourceLotId = line.SourceLotId ?? string.Empty,
                    UnitPriceCents = Math.Max(0, line.UnitPriceCents),
                    Stage = (EquipmentPurchaseStage)stage,
                    AssetIds = new List<string>(line.AssetIds ?? new List<string>()),
                });
            }
            return order;
        }
    }
}
