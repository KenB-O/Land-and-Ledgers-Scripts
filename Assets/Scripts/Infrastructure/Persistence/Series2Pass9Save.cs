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

        public FuelYardByproductIntake.FuelYardByproductIntakeSaveDto FuelIntake;
        public FreightShipmentService.FreightServiceSaveDto Freight;
        public Pass9FuelSourceParamsDto FuelSourceParams;

        /// <summary>
        /// Slice S2: per-business equipment registers (ovens' component
        /// assets + tool kits). Additive; old saves leave this empty and
        /// load with empty registers — the kit/oven gates then refuse
        /// honestly until the scenario endowment runs on a NewGame.
        /// </summary>
        public List<BusinessEquipmentRegister> EquipmentRegisters = new List<BusinessEquipmentRegister>();

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

        public FuelYardByproductIntake FuelIntake;
        public FreightProductionDriver FreightDriver;
        public Series2CarrierSettlement CarrierSettlement;
        public Series2DealerBakeryFuelSource FuelSource;

        /// <summary>
        /// Slice S2: live per-business equipment registers, held here so
        /// the Pass 9 seam persists them. The hub owns the live
        /// dictionary; the bootstrap registers these instances into it
        /// on NewGame seeding and re-registers restored ones after a load.
        /// </summary>
        public List<BusinessEquipmentRegister> EquipmentRegisters = new List<BusinessEquipmentRegister>();

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

        /// <summary>Diagnostics from the most recent Capture, for the caller to log (Capture itself returns only the DTO, following the Pass XL coordinator pattern).</summary>
        public List<string> LastCaptureDiagnostics = new List<string>();

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

            if (FuelIntake != null)
            {
                dto.FuelIntake = FuelIntake.CaptureSaveDto();
            }
            else if (diag != null)
            {
                diag.Add("Series2Pass9Save: no fuel-yard intake supplied; fuel lots saved empty (the intake already had a DTO — it only needed this wiring).");
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

            // Pass 8 fuel intake lots (DTO already existed).
            if (FuelIntake != null)
            {
                FuelIntake.LoadFromSaveDto(dto.FuelIntake);
            }
            else if (dto.FuelIntake != null && dto.FuelIntake.Lots != null && dto.FuelIntake.Lots.Count > 0)
            {
                diag.Add("Series2Pass9Save: fuel intake lots present in save but no intake supplied; fuel lots NOT restored.");
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
            return new Pass9CatalogLineDto
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
    }
}
