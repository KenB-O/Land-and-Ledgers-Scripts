using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum BusinessCapacityBottleneck
    {
        None = 0,
        Storage = 1,
        Processing = 2,
        Service = 3
    }

    [Serializable]
    public sealed class BusinessCapacityState
    {
        [SerializeField, Min(0)]
        private int storageCapacityUnits;

        [SerializeField, Min(0)]
        private int currentStoredUnits;

        [SerializeField, Min(0)]
        private int processingCapacityUnitsPerWeek;

        [SerializeField, Min(0)]
        private int currentProcessingUnitsPerWeek;

        [SerializeField, Min(0)]
        private int serviceCapacityVisitsPerDay;

        [SerializeField, Min(0)]
        private int currentServiceVisitsPerDay;

        [SerializeField, Range(0f, 1f)]
        private float storageHealth01 = 1f;

        [SerializeField, Range(0f, 1f)]
        private float processingHealth01 = 1f;

        [SerializeField, Range(0f, 1f)]
        private float serviceHealth01 = 1f;

        [SerializeField]
        private BusinessCapacityBottleneck bottleneck = BusinessCapacityBottleneck.None;

        public int StorageCapacityUnits => Mathf.Max(0, storageCapacityUnits);
        public int CurrentStoredUnits => Mathf.Max(0, currentStoredUnits);
        public int ProcessingCapacityUnitsPerWeek => Mathf.Max(0, processingCapacityUnitsPerWeek);
        public int CurrentProcessingUnitsPerWeek => Mathf.Max(0, currentProcessingUnitsPerWeek);
        public int ServiceCapacityVisitsPerDay => Mathf.Max(0, serviceCapacityVisitsPerDay);
        public int CurrentServiceVisitsPerDay => Mathf.Max(0, currentServiceVisitsPerDay);
        public float StorageHealth01 => Mathf.Clamp01(storageHealth01);
        public float ProcessingHealth01 => Mathf.Clamp01(processingHealth01);
        public float ServiceHealth01 => Mathf.Clamp01(serviceHealth01);
        public BusinessCapacityBottleneck Bottleneck => bottleneck;
        public string BottleneckDisplayName => bottleneck switch
        {
            BusinessCapacityBottleneck.Storage => "storage",
            BusinessCapacityBottleneck.Processing => "processing",
            BusinessCapacityBottleneck.Service => "service",
            _ => "none"
        };

        public BusinessCapacitySaveDto CaptureSaveDto()
        {
            return new BusinessCapacitySaveDto
            {
                storageCapacityUnits = StorageCapacityUnits,
                currentStoredUnits = CurrentStoredUnits,
                processingCapacityUnitsPerWeek = ProcessingCapacityUnitsPerWeek,
                currentProcessingUnitsPerWeek = CurrentProcessingUnitsPerWeek,
                serviceCapacityVisitsPerDay = ServiceCapacityVisitsPerDay,
                currentServiceVisitsPerDay = CurrentServiceVisitsPerDay
            };
        }

        public void ApplySaveDto(BusinessCapacitySaveDto dto)
        {
            if (dto == null)
            {
                Refresh(0, 0, 0, 0, 0, 0);
                return;
            }

            Refresh(
                dto.currentStoredUnits,
                dto.storageCapacityUnits,
                dto.processingCapacityUnitsPerWeek,
                dto.currentProcessingUnitsPerWeek,
                dto.serviceCapacityVisitsPerDay,
                dto.currentServiceVisitsPerDay);
        }

        public void Refresh(
            int currentStoredUnits,
            int storageCapacityUnits,
            int processingCapacityUnitsPerWeek,
            int currentProcessingUnitsPerWeek,
            int serviceCapacityVisitsPerDay,
            int currentServiceVisitsPerDay)
        {
            this.currentStoredUnits = Mathf.Max(0, currentStoredUnits);
            this.storageCapacityUnits = Mathf.Max(0, storageCapacityUnits);
            this.processingCapacityUnitsPerWeek = Mathf.Max(0, processingCapacityUnitsPerWeek);
            this.currentProcessingUnitsPerWeek = Mathf.Clamp(currentProcessingUnitsPerWeek, 0, this.processingCapacityUnitsPerWeek);
            this.serviceCapacityVisitsPerDay = Mathf.Max(0, serviceCapacityVisitsPerDay);
            this.currentServiceVisitsPerDay = Mathf.Clamp(currentServiceVisitsPerDay, 0, this.serviceCapacityVisitsPerDay);

            storageHealth01 = this.storageCapacityUnits <= 0 ? 1f : Mathf.Clamp01((float)this.currentStoredUnits / this.storageCapacityUnits);
            processingHealth01 = this.processingCapacityUnitsPerWeek <= 0 ? 1f : Mathf.Clamp01((float)this.currentProcessingUnitsPerWeek / this.processingCapacityUnitsPerWeek);
            serviceHealth01 = this.serviceCapacityVisitsPerDay <= 0 ? 1f : Mathf.Clamp01((float)this.currentServiceVisitsPerDay / this.serviceCapacityVisitsPerDay);
            bottleneck = ResolveBottleneck();
        }

        private BusinessCapacityBottleneck ResolveBottleneck()
        {
            BusinessCapacityBottleneck resolved = BusinessCapacityBottleneck.None;
            float strongestPressure = 0.01f;

            Consider(storageCapacityUnits > 0, 1f - StorageHealth01, BusinessCapacityBottleneck.Storage, ref resolved, ref strongestPressure);
            Consider(processingCapacityUnitsPerWeek > 0, 1f - ProcessingHealth01, BusinessCapacityBottleneck.Processing, ref resolved, ref strongestPressure);
            Consider(serviceCapacityVisitsPerDay > 0, 1f - ServiceHealth01, BusinessCapacityBottleneck.Service, ref resolved, ref strongestPressure);
            return resolved;
        }

        private static void Consider(
            bool available,
            float pressure,
            BusinessCapacityBottleneck candidate,
            ref BusinessCapacityBottleneck resolved,
            ref float strongestPressure)
        {
            if (!available || pressure <= strongestPressure)
            {
                return;
            }

            strongestPressure = pressure;
            resolved = candidate;
        }
    }

    [Serializable]
    public sealed class CategoryStockState
    {
        [SerializeField]
        private string categoryId;

        [SerializeField, Min(0)]
        private int currentStockUnits;

        [SerializeField, Min(0)]
        private int targetStockUnits;

        [SerializeField, Min(0)]
        private int pendingReorderUnits;

        [SerializeField]
        private int lastDailyUnitsSold;

        [SerializeField]
        private int lastDailyRevenueCents;

        [SerializeField]
        private int weekToDateUnitsSold;

        [SerializeField]
        private int weekToDateRevenueCents;

        public string CategoryId => categoryId ?? string.Empty;
        public int CurrentStockUnits => Mathf.Max(0, currentStockUnits);
        public int TargetStockUnits => Mathf.Max(0, targetStockUnits);
        public int PendingReorderUnits => Mathf.Max(0, pendingReorderUnits);
        public int LastDailyUnitsSold => Mathf.Max(0, lastDailyUnitsSold);
        public int LastDailyRevenueCents => Mathf.Max(0, lastDailyRevenueCents);
        public int WeekToDateUnitsSold => Mathf.Max(0, weekToDateUnitsSold);
        public int WeekToDateRevenueCents => Mathf.Max(0, weekToDateRevenueCents);
        public float StockHealth01 => targetStockUnits <= 0 ? 1f : Mathf.Clamp01((float)currentStockUnits / targetStockUnits);
        public int MissingTargetStockUnits => Mathf.Max(0, TargetStockUnits - CurrentStockUnits);
        public bool HasPendingReorder => PendingReorderUnits > 0;
        public string StockPostureLabel => ResolveStockPostureLabel();
        public bool NeedsReorder(float threshold01) => StockHealth01 <= Mathf.Clamp01(threshold01);

        public string BuildStockReadinessLine()
        {
            string pending = HasPendingReorder ? $", {PendingReorderUnits} pending" : string.Empty;
            return $"{CategoryId}: {CurrentStockUnits}/{TargetStockUnits} units, {StockPostureLabel}, missing {MissingTargetStockUnits}{pending}";
        }

        public CategoryStockState()
        {
            categoryId = string.Empty;
        }

        public CategoryStockState(string categoryId, int startingStockUnits, int targetStockUnits)
        {
            this.categoryId = categoryId;
            currentStockUnits = Mathf.Max(0, startingStockUnits);
            this.targetStockUnits = Mathf.Max(0, targetStockUnits);
        }

        private string ResolveStockPostureLabel()
        {
            if (TargetStockUnits <= 0)
            {
                return CurrentStockUnits > 0 ? "untracked stock" : "no target";
            }

            if (CurrentStockUnits <= 0)
            {
                return "stockout";
            }

            if (StockHealth01 < 0.35f)
            {
                return "critical";
            }

            if (StockHealth01 < 0.7f)
            {
                return "low";
            }

            if (MissingTargetStockUnits <= 0)
            {
                return "full";
            }

            return "ready";
        }

        public CategoryStockSaveDto CaptureSaveDto()
        {
            return new CategoryStockSaveDto
            {
                categoryId = CategoryId,
                currentStockUnits = CurrentStockUnits,
                targetStockUnits = TargetStockUnits,
                pendingReorderUnits = PendingReorderUnits,
                lastDailyUnitsSold = LastDailyUnitsSold,
                lastDailyRevenueCents = LastDailyRevenueCents,
                weekToDateUnitsSold = WeekToDateUnitsSold,
                weekToDateRevenueCents = WeekToDateRevenueCents
            };
        }

        public static CategoryStockState FromSaveDto(CategoryStockSaveDto dto)
        {
            if (dto == null)
            {
                return new CategoryStockState();
            }

            return new CategoryStockState
            {
                categoryId = dto.categoryId ?? string.Empty,
                currentStockUnits = Mathf.Max(0, dto.currentStockUnits),
                targetStockUnits = Mathf.Max(0, dto.targetStockUnits),
                pendingReorderUnits = Mathf.Max(0, dto.pendingReorderUnits),
                lastDailyUnitsSold = Mathf.Max(0, dto.lastDailyUnitsSold),
                lastDailyRevenueCents = Mathf.Max(0, dto.lastDailyRevenueCents),
                weekToDateUnitsSold = Mathf.Max(0, dto.weekToDateUnitsSold),
                weekToDateRevenueCents = Mathf.Max(0, dto.weekToDateRevenueCents)
            };
        }

        public void RecordDailySales(int unitsSold, int revenueCents)
        {
            int clampedUnits = Mathf.Clamp(unitsSold, 0, currentStockUnits);
            currentStockUnits -= clampedUnits;
            lastDailyUnitsSold += clampedUnits;
            lastDailyRevenueCents += Mathf.Max(0, revenueCents);
            weekToDateUnitsSold += clampedUnits;
            weekToDateRevenueCents += Mathf.Max(0, revenueCents);
        }

        public void RecordNonStockDailySales(int unitsSold, int revenueCents)
        {
            int clampedUnits = Mathf.Max(0, unitsSold);
            lastDailyUnitsSold += clampedUnits;
            lastDailyRevenueCents += Mathf.Max(0, revenueCents);
            weekToDateUnitsSold += clampedUnits;
            weekToDateRevenueCents += Mathf.Max(0, revenueCents);
        }

        public void ResetDailySales()
        {
            lastDailyUnitsSold = 0;
            lastDailyRevenueCents = 0;
        }

        public void ResetWeekToDateSales()
        {
            weekToDateUnitsSold = 0;
            weekToDateRevenueCents = 0;
        }

        public void QueueReorderToTarget()
        {
            pendingReorderUnits = Mathf.Max(0, targetStockUnits - currentStockUnits);
        }

        public void SetPendingReorderUnits(int units)
        {
            pendingReorderUnits = Mathf.Clamp(units, 0, Mathf.Max(0, targetStockUnits - currentStockUnits));
        }

        public void RefreshPendingReorderToTarget(float threshold01)
        {
            pendingReorderUnits = NeedsReorder(threshold01)
                ? Mathf.Max(0, targetStockUnits - currentStockUnits)
                : 0;
        }

        public void ReceivePendingReorder()
        {
            currentStockUnits += pendingReorderUnits;
            pendingReorderUnits = 0;
        }

        public int ReceivePendingReorderUnits(int units)
        {
            int receivedUnits = Mathf.Clamp(units, 0, pendingReorderUnits);
            currentStockUnits += receivedUnits;
            pendingReorderUnits -= receivedUnits;
            return receivedUnits;
        }

        public bool TryConsumeStockUnits(int units)
        {
            int requestedUnits = Mathf.Max(0, units);
            if (requestedUnits > currentStockUnits)
            {
                return false;
            }

            currentStockUnits -= requestedUnits;
            pendingReorderUnits = Mathf.Min(pendingReorderUnits, Mathf.Max(0, targetStockUnits - currentStockUnits));
            return true;
        }

        public int AddStockUnits(int units)
        {
            int requestedUnits = Mathf.Max(0, units);
            if (requestedUnits <= 0)
            {
                return 0;
            }

            int capacityRemaining = targetStockUnits > 0
                ? Mathf.Max(0, targetStockUnits - currentStockUnits)
                : requestedUnits;
            int acceptedUnits = Mathf.Min(requestedUnits, capacityRemaining);
            currentStockUnits += acceptedUnits;
            pendingReorderUnits = Mathf.Min(pendingReorderUnits, Mathf.Max(0, targetStockUnits - currentStockUnits));
            return acceptedUnits;
        }

        public void SetCurrentStockForTests(int units)
        {
            SetCurrentStockUnits(units);
        }

        public void SetCurrentStockUnits(int units)
        {
            currentStockUnits = Mathf.Max(0, units);
            pendingReorderUnits = Mathf.Min(pendingReorderUnits, Mathf.Max(0, targetStockUnits - currentStockUnits));
        }

        public int NormalizeForStartup(CategoryStockState template, string fallbackCategoryId)
        {
            int repairs = 0;
            string resolvedCategoryId = !string.IsNullOrWhiteSpace(categoryId)
                ? categoryId.Trim()
                : !string.IsNullOrWhiteSpace(template?.CategoryId)
                    ? template.CategoryId.Trim()
                    : fallbackCategoryId ?? string.Empty;
            int resolvedCurrent = Mathf.Max(0, currentStockUnits);
            int templateTarget = template != null ? template.TargetStockUnits : 0;
            int resolvedTarget = Mathf.Max(Mathf.Max(0, targetStockUnits), templateTarget);
            if (resolvedTarget <= 0 && resolvedCurrent > 0)
            {
                resolvedTarget = resolvedCurrent;
            }

            int resolvedPending = Mathf.Clamp(Mathf.Max(0, pendingReorderUnits), 0, Mathf.Max(0, resolvedTarget - resolvedCurrent));
            int resolvedDailyUnits = Mathf.Max(0, lastDailyUnitsSold);
            int resolvedDailyRevenue = Mathf.Max(0, lastDailyRevenueCents);
            int resolvedWeekUnits = Mathf.Max(0, weekToDateUnitsSold);
            int resolvedWeekRevenue = Mathf.Max(0, weekToDateRevenueCents);

            if (!string.Equals(resolvedCategoryId, categoryId, StringComparison.Ordinal)
                || resolvedCurrent != currentStockUnits
                || resolvedTarget != targetStockUnits
                || resolvedPending != pendingReorderUnits
                || resolvedDailyUnits != lastDailyUnitsSold
                || resolvedDailyRevenue != lastDailyRevenueCents
                || resolvedWeekUnits != weekToDateUnitsSold
                || resolvedWeekRevenue != weekToDateRevenueCents)
            {
                repairs++;
            }

            categoryId = resolvedCategoryId;
            currentStockUnits = resolvedCurrent;
            targetStockUnits = resolvedTarget;
            pendingReorderUnits = resolvedPending;
            lastDailyUnitsSold = resolvedDailyUnits;
            lastDailyRevenueCents = resolvedDailyRevenue;
            weekToDateUnitsSold = resolvedWeekUnits;
            weekToDateRevenueCents = resolvedWeekRevenue;
            return repairs;
        }

        public int MergeFromDuplicate(CategoryStockState duplicate)
        {
            if (duplicate == null)
            {
                return 0;
            }

            currentStockUnits = Mathf.Max(0, currentStockUnits) + duplicate.CurrentStockUnits;
            targetStockUnits = Mathf.Max(TargetStockUnits, duplicate.TargetStockUnits);
            pendingReorderUnits = Mathf.Clamp(PendingReorderUnits + duplicate.PendingReorderUnits, 0, Mathf.Max(0, targetStockUnits - currentStockUnits));
            lastDailyUnitsSold = Mathf.Max(0, lastDailyUnitsSold) + duplicate.LastDailyUnitsSold;
            lastDailyRevenueCents = Mathf.Max(0, lastDailyRevenueCents) + duplicate.LastDailyRevenueCents;
            weekToDateUnitsSold = Mathf.Max(0, weekToDateUnitsSold) + duplicate.WeekToDateUnitsSold;
            weekToDateRevenueCents = Mathf.Max(0, weekToDateRevenueCents) + duplicate.WeekToDateRevenueCents;
            return 1;
        }

    }

    /// <summary>
    /// Legacy worker-slot record. Mixes staffing need (slot id, required-for-opening), worker
    /// identity (assigned worker), wage terms and missed-payroll state in one record.
    /// PKG-2 (TECH §4.3 / SD-05): slot COUNTS are no longer a causal staffing authority. FTE and
    /// headcount are derived read models (see WorkforceStaffingReadModel); slots must not
    /// fabricate Persons or suppress seasonal hiring. PKG-7 decomposes this into RoleDefinition +
    /// PositionState + EmploymentRelationship (3A-D19); until consumers migrate, this class is the
    /// compatibility projection surface.
    /// </summary>
    [Serializable]
    public sealed class WorkerSlotState
    {
        [SerializeField]
        private string slotId;

        [SerializeField]
        private string slotDisplayName;

        [SerializeField]
        private string assignedWorkerId;

        [SerializeField]
        private string assignedWorkerDisplayName;

        [SerializeField, Min(0)]
        private int weeklyWageCents;

        [SerializeField]
        private bool requiredForOpening;

        [SerializeField]
        private bool paidActive;

        [SerializeField]
        private bool suspendedForMissedPayroll;

        public string SlotId => slotId ?? string.Empty;
        public string SlotDisplayName => string.IsNullOrWhiteSpace(slotDisplayName) ? SlotId : slotDisplayName;
        public string AssignedWorkerId => assignedWorkerId ?? string.Empty;
        public string AssignedWorkerDisplayName => string.IsNullOrWhiteSpace(assignedWorkerDisplayName) ? AssignedWorkerId : assignedWorkerDisplayName;
        public int WeeklyWageCents => Mathf.Max(0, weeklyWageCents);
        /// <summary>
        /// PKG-2 (TECH §4.3): a template staffing-need flag, not a causal gate. Whether a business
        /// may operate is decided from actual worker coverage, not from slot definitions.
        /// </summary>
        public bool RequiredForOpening => requiredForOpening;
        public bool IsPaidActive => IsFilled && paidActive && !suspendedForMissedPayroll;
        public bool SuspendedForMissedPayroll => suspendedForMissedPayroll;
        public bool IsFilled => !string.IsNullOrWhiteSpace(assignedWorkerId) || !string.IsNullOrWhiteSpace(assignedWorkerDisplayName);

        /// <summary>
        /// PKG-2: a derived continuity diagnostic, not staffing authority. Counts must be read
        /// through WorkforceStaffingReadModel, never used to decide how many workers may be engaged.
        /// </summary>
        public int StartupContinuityScore
        {
            get
            {
                int score = 0;
                if (IsFilled)
                {
                    score += 100;
                }

                if (IsPaidActive)
                {
                    score += 40;
                }

                if (RequiredForOpening)
                {
                    score += 15;
                }

                score += Mathf.Clamp(WeeklyWageCents / 100, 0, 25);
                return score;
            }
        }

        public int NormalizeForStartup(WorkerSlotState template, string fallbackSlotId)
        {
            int repairs = 0;
            string resolvedSlotId = !string.IsNullOrWhiteSpace(slotId)
                ? slotId.Trim()
                : !string.IsNullOrWhiteSpace(template?.SlotId)
                    ? template.SlotId.Trim()
                    : fallbackSlotId ?? string.Empty;
            string resolvedDisplayName = !string.IsNullOrWhiteSpace(slotDisplayName)
                ? slotDisplayName.Trim()
                : !string.IsNullOrWhiteSpace(template?.SlotDisplayName)
                    ? template.SlotDisplayName.Trim()
                    : resolvedSlotId;
            string resolvedWorkerId = assignedWorkerId?.Trim() ?? string.Empty;
            string resolvedWorkerName = assignedWorkerDisplayName?.Trim() ?? string.Empty;
            int resolvedWage = Mathf.Max(0, weeklyWageCents > 0 ? weeklyWageCents : template?.WeeklyWageCents ?? 0);
            bool resolvedRequired = template != null ? template.RequiredForOpening : requiredForOpening;
            bool hasWorker = !string.IsNullOrWhiteSpace(resolvedWorkerId) || !string.IsNullOrWhiteSpace(resolvedWorkerName);
            bool resolvedSuspended = hasWorker && suspendedForMissedPayroll;
            bool resolvedPaidActive = hasWorker && paidActive && !resolvedSuspended;

            if (!string.Equals(resolvedSlotId, slotId, StringComparison.Ordinal)
                || !string.Equals(resolvedDisplayName, slotDisplayName, StringComparison.Ordinal)
                || !string.Equals(resolvedWorkerId, assignedWorkerId, StringComparison.Ordinal)
                || !string.Equals(resolvedWorkerName, assignedWorkerDisplayName, StringComparison.Ordinal)
                || resolvedWage != weeklyWageCents
                || resolvedRequired != requiredForOpening
                || resolvedPaidActive != paidActive
                || resolvedSuspended != suspendedForMissedPayroll)
            {
                repairs++;
            }

            slotId = resolvedSlotId;
            slotDisplayName = resolvedDisplayName;
            assignedWorkerId = resolvedWorkerId;
            assignedWorkerDisplayName = resolvedWorkerName;
            weeklyWageCents = resolvedWage;
            requiredForOpening = resolvedRequired;
            paidActive = resolvedPaidActive;
            suspendedForMissedPayroll = resolvedSuspended;
            return repairs;
        }

        public WorkerSlotState CloneAsTemplateCopy()
        {
            return new WorkerSlotState(SlotId, SlotDisplayName, WeeklyWageCents, RequiredForOpening);
        }


        public WorkerSlotState()
        {
            slotId = string.Empty;
            slotDisplayName = string.Empty;
            assignedWorkerId = string.Empty;
            assignedWorkerDisplayName = string.Empty;
            paidActive = false;
            suspendedForMissedPayroll = false;
        }

        public WorkerSlotState(string slotId, string slotDisplayName, int weeklyWageCents, bool requiredForOpening)
        {
            this.slotId = slotId;
            this.slotDisplayName = slotDisplayName;
            this.weeklyWageCents = Mathf.Max(0, weeklyWageCents);
            this.requiredForOpening = requiredForOpening;
            assignedWorkerId = string.Empty;
            assignedWorkerDisplayName = string.Empty;
            paidActive = false;
            suspendedForMissedPayroll = false;
        }

        public WorkerSlotSaveDto CaptureSaveDto()
        {
            return new WorkerSlotSaveDto
            {
                slotId = SlotId,
                slotDisplayName = SlotDisplayName,
                assignedWorkerId = AssignedWorkerId,
                assignedWorkerDisplayName = AssignedWorkerDisplayName,
                weeklyWageCents = WeeklyWageCents,
                requiredForOpening = RequiredForOpening,
                paidActive = IsPaidActive,
                suspendedForMissedPayroll = SuspendedForMissedPayroll
            };
        }

        public static WorkerSlotState FromSaveDto(WorkerSlotSaveDto dto)
        {
            if (dto == null)
            {
                return new WorkerSlotState();
            }

            return new WorkerSlotState
            {
                slotId = dto.slotId ?? string.Empty,
                slotDisplayName = dto.slotDisplayName ?? string.Empty,
                assignedWorkerId = dto.assignedWorkerId ?? string.Empty,
                assignedWorkerDisplayName = dto.assignedWorkerDisplayName ?? string.Empty,
                weeklyWageCents = Mathf.Max(0, dto.weeklyWageCents),
                requiredForOpening = dto.requiredForOpening,
                paidActive = dto.paidActive,
                suspendedForMissedPayroll = dto.suspendedForMissedPayroll
            };
        }

        /// <summary>
        /// Assigns a worker to this slot. PKG-2: assignment through a slot is wage employment by
        /// construction (the slot wage drives payroll); the relationship classifies as
        /// WorkRelationshipKind.PermanentEmployment via WorkRelationshipProjection. Family labor
        /// and operators must NOT be recorded here - they use the taxonomy's non-wage kinds
        /// (TECH §4.1, §4.5; PL-04).
        /// </summary>
        public void Assign(string workerId, int wageCents)
        {
            Assign(workerId, workerId, wageCents);
        }

        public void Assign(string workerId, string workerDisplayName, int wageCents)
        {
            assignedWorkerId = workerId ?? string.Empty;
            assignedWorkerDisplayName = workerDisplayName ?? string.Empty;
            weeklyWageCents = Mathf.Max(0, wageCents);
            paidActive = IsFilled;
            suspendedForMissedPayroll = false;
        }

        public string Unassign()
        {
            string releasedWorkerId = AssignedWorkerId;
            assignedWorkerId = string.Empty;
            assignedWorkerDisplayName = string.Empty;
            paidActive = false;
            suspendedForMissedPayroll = false;
            return releasedWorkerId;
        }

        public void MarkPaidActive()
        {
            if (!IsFilled)
            {
                paidActive = false;
                suspendedForMissedPayroll = false;
                return;
            }

            paidActive = true;
            suspendedForMissedPayroll = false;
        }

        public string SuspendForMissedPayroll()
        {
            string releasedWorkerId = AssignedWorkerId;
            paidActive = false;
            suspendedForMissedPayroll = true;
            return releasedWorkerId;
        }
    }

    internal readonly struct BusinessRuntimeTransactionSnapshot
    {
        public BusinessRuntimeTransactionSnapshot(
            int currentCashCents,
            int lastWeeklyLocalTransferRevenueCents,
            int lastWeeklyLocalTransferCostCents,
            int lastWeeklyCashAfterCents,
            string lastWeeklyTransferSummary,
            int lastDailyNetCents,
            int weekToDateNetCents,
            int monthToDateNetCents,
            int yearToDateNetCents,
            int allTimeNetCents)
        {
            CurrentCashCents = currentCashCents;
            LastWeeklyLocalTransferRevenueCents = lastWeeklyLocalTransferRevenueCents;
            LastWeeklyLocalTransferCostCents = lastWeeklyLocalTransferCostCents;
            LastWeeklyCashAfterCents = lastWeeklyCashAfterCents;
            LastWeeklyTransferSummary = lastWeeklyTransferSummary ?? string.Empty;
            LastDailyNetCents = lastDailyNetCents;
            WeekToDateNetCents = weekToDateNetCents;
            MonthToDateNetCents = monthToDateNetCents;
            YearToDateNetCents = yearToDateNetCents;
            AllTimeNetCents = allTimeNetCents;
        }

        public int CurrentCashCents { get; }
        public int LastWeeklyLocalTransferRevenueCents { get; }
        public int LastWeeklyLocalTransferCostCents { get; }
        public int LastWeeklyCashAfterCents { get; }
        public string LastWeeklyTransferSummary { get; }
        public int LastDailyNetCents { get; }
        public int WeekToDateNetCents { get; }
        public int MonthToDateNetCents { get; }
        public int YearToDateNetCents { get; }
        public int AllTimeNetCents { get; }
    }

    [Serializable]
    public sealed class BusinessRuntimeState
    {
        private const float RequiredStaffBaselineEfficiency01 = 0.8f;

        [SerializeField]
        private string businessId;

        /// <summary>
        /// T1B: the PKG-6 wage authority. UNITY WIRING STEP (needs verification on the
        /// dev machine): the scene bootstrap / SimulationSystemsHub AutoWire must set
        /// this from the hub's EmploymentRelationshipRegistry. Until wired, payroll
        /// reads and disbursement fall back to the legacy slot-wage loop.
        /// </summary>
        public EmploymentRelationshipRegistry EmploymentRegistry { get; set; }

        [SerializeField]
        private BusinessType businessType;

        [SerializeField, Min(0)]
        private int currentCashCents;

        [SerializeField]
        private int lastDailyRevenueCents;

        [SerializeField]
        private int lastWeeklyPayrollCents;

        [SerializeField]
        private int lastWeeklyReorderBudgetCents;

        [SerializeField]
        private int lastWeeklyInputUnitsConsumed;

        [SerializeField]
        private int lastWeeklyOutputUnitsProduced;

        [SerializeField]
        private int lastWeeklyInputProcurementSpendCents;

        [SerializeField]
        private int lastWeeklyLocalTransferRevenueCents;

        [SerializeField]
        private int lastWeeklyLocalTransferCostCents;

        [SerializeField]
        private int lastWeeklyCashTransferInCents;

        [SerializeField]
        private int lastWeeklyCashTransferOutCents;

        [SerializeField]
        private int lastWeeklyOwnerDistributionCents;

        [SerializeField]
        private int lastWeeklyCashBeforeCents;

        [SerializeField]
        private int lastWeeklyCashAfterCents;

        [SerializeField, Min(0)]
        private int accruedLiabilityCents;

        [SerializeField, Min(0)]
        private int lastWeeklyAccruedLiabilityCents;

        [SerializeField]
        private string lastWeeklyBlockedReason = string.Empty;

        [SerializeField, TextArea(1, 3)]
        private string lastWeeklyOperationSummary = string.Empty;

        [SerializeField, TextArea(1, 3)]
        private string lastWeeklyTransferSummary = string.Empty;

        [SerializeField]
        private int weekToDateRevenueCents;

        [SerializeField]
        private int weekToDateUnitsSold;

        [SerializeField]
        private int lastDailyNetCents;

        [SerializeField]
        private int weekToDateNetCents;

        [SerializeField]
        private int monthToDateNetCents;

        [SerializeField]
        private int yearToDateNetCents;

        [SerializeField]
        private int allTimeNetCents;

        [SerializeField, Range(0f, 1f)]
        private float stockHealth01 = 1f;

        [SerializeField, Range(0f, 1f)]
        private float reliability01 = 1f;

        [SerializeField, Range(0f, 1f)]
        private float competitionPressure01;

        [SerializeField]
        private List<CategoryStockState> categoryStock = new();

        [SerializeField]
        private List<WorkerSlotState> workerSlots = new();

        [SerializeField]
        private List<string> lastSuspendedPayrollWorkerIds = new();

        [SerializeField]
        private BusinessMainFocusState mainFocus = BusinessMainFocusState.FromDefinition(null, BusinessType.GeneralStore);

        [SerializeField]
        private BusinessCapacityState capacity = new();

        public string BusinessId => businessId ?? string.Empty;
        public BusinessType BusinessType => businessType;
        public int CurrentCashCents => Mathf.Max(0, currentCashCents);
        public int LastDailyRevenueCents => Mathf.Max(0, lastDailyRevenueCents);
        public int LastWeeklyPayrollCents => Mathf.Max(0, lastWeeklyPayrollCents);
        public int LastWeeklyReorderBudgetCents => Mathf.Max(0, lastWeeklyReorderBudgetCents);
        public int LastWeeklyInputUnitsConsumed => Mathf.Max(0, lastWeeklyInputUnitsConsumed);
        public int LastWeeklyOutputUnitsProduced => Mathf.Max(0, lastWeeklyOutputUnitsProduced);
        public int LastWeeklyInputProcurementSpendCents => Mathf.Max(0, lastWeeklyInputProcurementSpendCents);
        public int LastWeeklyLocalTransferRevenueCents => Mathf.Max(0, lastWeeklyLocalTransferRevenueCents);
        public int LastWeeklyLocalTransferCostCents => Mathf.Max(0, lastWeeklyLocalTransferCostCents);
        public int LastWeeklyCashTransferInCents => Mathf.Max(0, lastWeeklyCashTransferInCents);
        public int LastWeeklyCashTransferOutCents => Mathf.Max(0, lastWeeklyCashTransferOutCents);
        public int LastWeeklyOwnerDistributionCents => Mathf.Max(0, lastWeeklyOwnerDistributionCents);
        public int LastWeeklyCashBeforeCents => Mathf.Max(0, lastWeeklyCashBeforeCents);
        public int LastWeeklyCashAfterCents => Mathf.Max(0, lastWeeklyCashAfterCents);
        public int AccruedLiabilityCents => Mathf.Max(0, accruedLiabilityCents);
        public int LastWeeklyAccruedLiabilityCents => Mathf.Max(0, lastWeeklyAccruedLiabilityCents);
        public string LastWeeklyBlockedReason => lastWeeklyBlockedReason ?? string.Empty;
        public string LastWeeklyOperationSummary => lastWeeklyOperationSummary ?? string.Empty;
        public string LastWeeklyTransferSummary => lastWeeklyTransferSummary ?? string.Empty;
        public int WeekToDateRevenueCents => Mathf.Max(0, weekToDateRevenueCents);
        public int WeekToDateUnitsSold => Mathf.Max(0, weekToDateUnitsSold);
        public int LastDailyNetCents => lastDailyNetCents;
        public int WeekToDateNetCents => weekToDateNetCents;
        public int MonthToDateNetCents => monthToDateNetCents;
        public int YearToDateNetCents => yearToDateNetCents;
        public int AllTimeNetCents => allTimeNetCents;
        public float StockHealth01 => Mathf.Clamp01(stockHealth01);
        public float Reliability01 => Mathf.Clamp01(reliability01);
        public float CompetitionPressure01 => Mathf.Clamp01(competitionPressure01);
        public IReadOnlyList<CategoryStockState> CategoryStock => categoryStock;
        public IReadOnlyList<WorkerSlotState> WorkerSlots => workerSlots;
        public int TotalCurrentStockUnits => CalculateTotalCurrentStockUnits();
        public int TotalTargetStockUnits => CalculateTotalTargetStockUnits();
        public int TotalPendingReorderUnits => CalculateTotalPendingReorderUnits();
        public IReadOnlyList<string> LastSuspendedPayrollWorkerIds => lastSuspendedPayrollWorkerIds;
        public BusinessMainFocusState MainFocus => mainFocus ?? BusinessMainFocusState.FromDefinition(null, businessType);
        public BusinessCapacityState Capacity => capacity ??= new BusinessCapacityState();
        public int TargetWorkerCount => workerSlots.Count;
        public int FilledWeeklyPayrollCents
        {
            get
            {
                // T1B: the PKG-6 registry is the wage authority (Tech X §4.1–4.3). The
                // slot sum below is the legacy fallback for contexts where the registry
                // has not been wired yet (see EmploymentRegistry).
                if (EmploymentRegistry != null)
                {
                    return EmploymentRegistry.GetActiveWeeklyPayrollCents(BusinessId);
                }

                int payroll = 0;
                for (int i = 0; i < workerSlots.Count; i++)
                {
                    WorkerSlotState slot = workerSlots[i];
                    if (slot != null && slot.IsFilled)
                    {
                        payroll += slot.WeeklyWageCents;
                    }
                }

                return Mathf.Max(0, payroll);
            }
        }

        public int FilledWorkerCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < workerSlots.Count; i++)
                {
                    if (workerSlots[i].IsFilled)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int ActiveWorkerCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < workerSlots.Count; i++)
                {
                    if (workerSlots[i].IsPaidActive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int RequiredWorkerCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < workerSlots.Count; i++)
                {
                    if (workerSlots[i].RequiredForOpening)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int ActiveRequiredWorkerCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < workerSlots.Count; i++)
                {
                    WorkerSlotState slot = workerSlots[i];
                    if (slot.RequiredForOpening && slot.IsPaidActive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public float OperatingEfficiency01
        {
            get
            {
                if (TargetWorkerCount <= 0)
                {
                    return 1f;
                }

                if (RequiredWorkerCount > 0 && ActiveRequiredWorkerCount < RequiredWorkerCount)
                {
                    return 0f;
                }

                int required = RequiredWorkerCount;
                if (required <= 0)
                {
                    return Mathf.Clamp01((float)ActiveWorkerCount / TargetWorkerCount);
                }

                int optionalSlots = Mathf.Max(0, TargetWorkerCount - required);
                if (optionalSlots <= 0)
                {
                    return 1f;
                }

                int activeOptional = Mathf.Max(0, ActiveWorkerCount - ActiveRequiredWorkerCount);
                float optionalContribution = Mathf.Clamp01((float)activeOptional / optionalSlots) * (1f - RequiredStaffBaselineEfficiency01);
                return Mathf.Clamp01(RequiredStaffBaselineEfficiency01 + optionalContribution);
            }
        }

        public string BuildStockReadinessLine()
        {
            if (categoryStock == null || categoryStock.Count == 0)
            {
                return "Stock readiness: no tracked categories.";
            }

            int critical = 0;
            int low = 0;
            int pending = 0;
            CategoryStockState weakest = null;
            for (int i = 0; i < categoryStock.Count; i++)
            {
                CategoryStockState stock = categoryStock[i];
                if (stock == null)
                {
                    continue;
                }

                if (stock.HasPendingReorder)
                {
                    pending++;
                }

                if (stock.TargetStockUnits > 0 && stock.CurrentStockUnits <= 0)
                {
                    critical++;
                }
                else if (stock.TargetStockUnits > 0 && stock.StockHealth01 < 0.7f)
                {
                    low++;
                }

                if (weakest == null || stock.StockHealth01 < weakest.StockHealth01)
                {
                    weakest = stock;
                }
            }

            string weakestLine = weakest != null ? $" | weakest {weakest.BuildStockReadinessLine()}" : string.Empty;
            return $"Stock readiness: {TotalCurrentStockUnits}/{TotalTargetStockUnits} units, {TotalPendingReorderUnits} pending | critical {critical}, low {low}, pending categories {pending}{weakestLine}";
        }

        private int CalculateTotalCurrentStockUnits()
        {
            int total = 0;
            if (categoryStock == null)
            {
                return 0;
            }

            for (int i = 0; i < categoryStock.Count; i++)
            {
                total += categoryStock[i] != null ? categoryStock[i].CurrentStockUnits : 0;
            }

            return Mathf.Max(0, total);
        }

        private int CalculateTotalTargetStockUnits()
        {
            int total = 0;
            if (categoryStock == null)
            {
                return 0;
            }

            for (int i = 0; i < categoryStock.Count; i++)
            {
                total += categoryStock[i] != null ? categoryStock[i].TargetStockUnits : 0;
            }

            return Mathf.Max(0, total);
        }

        private int CalculateTotalPendingReorderUnits()
        {
            int total = 0;
            if (categoryStock == null)
            {
                return 0;
            }

            for (int i = 0; i < categoryStock.Count; i++)
            {
                total += categoryStock[i] != null ? categoryStock[i].PendingReorderUnits : 0;
            }

            return Mathf.Max(0, total);
        }

        public int NormalizeForStartup(
            BusinessRuntimeState profileTemplate,
            string fallbackBusinessId,
            BusinessType fallbackBusinessType,
            BusinessThroughputMode throughputMode,
            int baselineWeeklyThroughputUnits,
            int baselineDailyServiceCapacity)
        {
            int repairs = 0;
            string resolvedBusinessId = !string.IsNullOrWhiteSpace(businessId)
                ? businessId.Trim()
                : !string.IsNullOrWhiteSpace(profileTemplate?.BusinessId)
                    ? profileTemplate.BusinessId.Trim()
                    : fallbackBusinessId ?? string.Empty;
            BusinessType resolvedType = profileTemplate != null ? profileTemplate.BusinessType : fallbackBusinessType;

            if (!string.Equals(resolvedBusinessId, businessId, StringComparison.Ordinal) || resolvedType != businessType)
            {
                repairs++;
            }

            businessId = resolvedBusinessId;
            businessType = resolvedType;
            categoryStock ??= new List<CategoryStockState>();
            workerSlots ??= new List<WorkerSlotState>();
            lastSuspendedPayrollWorkerIds ??= new List<string>();
            mainFocus ??= BusinessMainFocusState.FromDefinition(null, businessType);
            capacity ??= new BusinessCapacityState();

            repairs += NormalizeScalarFieldsForStartup();
            repairs += NormalizeCategoryStockForStartup(profileTemplate);
            repairs += NormalizeWorkerSlotsForStartup(profileTemplate);
            repairs += NormalizeSuspendedPayrollIdsForStartup();
            RefreshStockHealth();
            RefreshCapacity(throughputMode, baselineWeeklyThroughputUnits, baselineDailyServiceCapacity, 0, 0);
            RefreshWeeklyCashAfter();
            return repairs;
        }

        private int NormalizeScalarFieldsForStartup()
        {
            int repairs = 0;
            int resolvedCurrentCash = Mathf.Max(0, currentCashCents);
            int resolvedDailyRevenue = Mathf.Max(0, lastDailyRevenueCents);
            int resolvedPayroll = Mathf.Max(0, lastWeeklyPayrollCents);
            int resolvedReorderBudget = Mathf.Max(0, lastWeeklyReorderBudgetCents);
            int resolvedInputUnits = Mathf.Max(0, lastWeeklyInputUnitsConsumed);
            int resolvedOutputUnits = Mathf.Max(0, lastWeeklyOutputUnitsProduced);
            int resolvedInputSpend = Mathf.Max(0, lastWeeklyInputProcurementSpendCents);
            int resolvedLocalRevenue = Mathf.Max(0, lastWeeklyLocalTransferRevenueCents);
            int resolvedLocalCost = Mathf.Max(0, lastWeeklyLocalTransferCostCents);
            int resolvedTransferIn = Mathf.Max(0, lastWeeklyCashTransferInCents);
            int resolvedTransferOut = Mathf.Max(0, lastWeeklyCashTransferOutCents);
            int resolvedDistribution = Mathf.Max(0, lastWeeklyOwnerDistributionCents);
            int resolvedCashBefore = Mathf.Max(0, lastWeeklyCashBeforeCents);
            int resolvedCashAfter = Mathf.Max(0, lastWeeklyCashAfterCents);
            int resolvedLiability = Mathf.Max(0, accruedLiabilityCents);
            int resolvedWeeklyLiability = Mathf.Max(0, lastWeeklyAccruedLiabilityCents);
            int resolvedWeekRevenue = Mathf.Max(0, weekToDateRevenueCents);
            int resolvedWeekUnits = Mathf.Max(0, weekToDateUnitsSold);
            float resolvedStockHealth = Mathf.Clamp01(stockHealth01);
            float resolvedReliability = Mathf.Clamp01(reliability01 <= 0f ? 1f : reliability01);
            float resolvedCompetition = Mathf.Clamp01(competitionPressure01);
            string resolvedBlocked = lastWeeklyBlockedReason?.Trim() ?? string.Empty;
            string resolvedOperation = lastWeeklyOperationSummary?.Trim() ?? string.Empty;
            string resolvedTransfer = lastWeeklyTransferSummary?.Trim() ?? string.Empty;

            if (resolvedCurrentCash != currentCashCents
                || resolvedDailyRevenue != lastDailyRevenueCents
                || resolvedPayroll != lastWeeklyPayrollCents
                || resolvedReorderBudget != lastWeeklyReorderBudgetCents
                || resolvedInputUnits != lastWeeklyInputUnitsConsumed
                || resolvedOutputUnits != lastWeeklyOutputUnitsProduced
                || resolvedInputSpend != lastWeeklyInputProcurementSpendCents
                || resolvedLocalRevenue != lastWeeklyLocalTransferRevenueCents
                || resolvedLocalCost != lastWeeklyLocalTransferCostCents
                || resolvedTransferIn != lastWeeklyCashTransferInCents
                || resolvedTransferOut != lastWeeklyCashTransferOutCents
                || resolvedDistribution != lastWeeklyOwnerDistributionCents
                || resolvedCashBefore != lastWeeklyCashBeforeCents
                || resolvedCashAfter != lastWeeklyCashAfterCents
                || resolvedLiability != accruedLiabilityCents
                || resolvedWeeklyLiability != lastWeeklyAccruedLiabilityCents
                || resolvedWeekRevenue != weekToDateRevenueCents
                || resolvedWeekUnits != weekToDateUnitsSold
                || !Mathf.Approximately(resolvedStockHealth, stockHealth01)
                || !Mathf.Approximately(resolvedReliability, reliability01)
                || !Mathf.Approximately(resolvedCompetition, competitionPressure01)
                || !string.Equals(resolvedBlocked, lastWeeklyBlockedReason, StringComparison.Ordinal)
                || !string.Equals(resolvedOperation, lastWeeklyOperationSummary, StringComparison.Ordinal)
                || !string.Equals(resolvedTransfer, lastWeeklyTransferSummary, StringComparison.Ordinal))
            {
                repairs++;
            }

            currentCashCents = resolvedCurrentCash;
            lastDailyRevenueCents = resolvedDailyRevenue;
            lastWeeklyPayrollCents = resolvedPayroll;
            lastWeeklyReorderBudgetCents = resolvedReorderBudget;
            lastWeeklyInputUnitsConsumed = resolvedInputUnits;
            lastWeeklyOutputUnitsProduced = resolvedOutputUnits;
            lastWeeklyInputProcurementSpendCents = resolvedInputSpend;
            lastWeeklyLocalTransferRevenueCents = resolvedLocalRevenue;
            lastWeeklyLocalTransferCostCents = resolvedLocalCost;
            lastWeeklyCashTransferInCents = resolvedTransferIn;
            lastWeeklyCashTransferOutCents = resolvedTransferOut;
            lastWeeklyOwnerDistributionCents = resolvedDistribution;
            lastWeeklyCashBeforeCents = resolvedCashBefore;
            lastWeeklyCashAfterCents = resolvedCashAfter;
            accruedLiabilityCents = resolvedLiability;
            lastWeeklyAccruedLiabilityCents = resolvedWeeklyLiability;
            weekToDateRevenueCents = resolvedWeekRevenue;
            weekToDateUnitsSold = resolvedWeekUnits;
            stockHealth01 = resolvedStockHealth;
            reliability01 = resolvedReliability;
            competitionPressure01 = resolvedCompetition;
            lastWeeklyBlockedReason = resolvedBlocked;
            lastWeeklyOperationSummary = resolvedOperation;
            lastWeeklyTransferSummary = resolvedTransfer;
            return repairs;
        }

        private int NormalizeCategoryStockForStartup(BusinessRuntimeState profileTemplate)
        {
            int repairs = 0;
            Dictionary<string, CategoryStockState> byCategory = new(StringComparer.OrdinalIgnoreCase);
            for (int i = categoryStock.Count - 1; i >= 0; i--)
            {
                CategoryStockState stock = categoryStock[i];
                if (stock == null)
                {
                    categoryStock.RemoveAt(i);
                    repairs++;
                    continue;
                }

                repairs += stock.NormalizeForStartup(FindTemplateCategory(profileTemplate, stock.CategoryId), $"category_{i:00}");
                string categoryId = stock.CategoryId;
                if (string.IsNullOrWhiteSpace(categoryId))
                {
                    categoryStock.RemoveAt(i);
                    repairs++;
                    continue;
                }

                if (byCategory.TryGetValue(categoryId, out CategoryStockState existing))
                {
                    repairs += existing.MergeFromDuplicate(stock);
                    categoryStock.RemoveAt(i);
                    repairs++;
                    continue;
                }

                byCategory[categoryId] = stock;
            }

            if (profileTemplate != null)
            {
                IReadOnlyList<CategoryStockState> templateStock = profileTemplate.CategoryStock;
                for (int i = 0; i < templateStock.Count; i++)
                {
                    CategoryStockState template = templateStock[i];
                    if (template == null || string.IsNullOrWhiteSpace(template.CategoryId) || byCategory.ContainsKey(template.CategoryId))
                    {
                        continue;
                    }

                    CategoryStockState restored = new(template.CategoryId, 0, template.TargetStockUnits);
                    categoryStock.Add(restored);
                    byCategory[restored.CategoryId] = restored;
                    repairs++;
                }
            }

            return repairs;
        }

        private int NormalizeWorkerSlotsForStartup(BusinessRuntimeState profileTemplate)
        {
            int repairs = 0;
            Dictionary<string, WorkerSlotState> bySlot = new(StringComparer.OrdinalIgnoreCase);
            for (int i = workerSlots.Count - 1; i >= 0; i--)
            {
                WorkerSlotState slot = workerSlots[i];
                if (slot == null)
                {
                    workerSlots.RemoveAt(i);
                    repairs++;
                    continue;
                }

                repairs += slot.NormalizeForStartup(FindTemplateSlot(profileTemplate, slot.SlotId), $"slot_{i:00}");
                string slotId = slot.SlotId;
                if (string.IsNullOrWhiteSpace(slotId))
                {
                    workerSlots.RemoveAt(i);
                    repairs++;
                    continue;
                }

                if (bySlot.TryGetValue(slotId, out WorkerSlotState existing))
                {
                    if (slot.StartupContinuityScore > existing.StartupContinuityScore)
                    {
                        int existingIndex = workerSlots.IndexOf(existing);
                        if (existingIndex >= 0)
                        {
                            workerSlots[existingIndex] = slot;
                        }

                        bySlot[slotId] = slot;
                    }

                    workerSlots.RemoveAt(i);
                    repairs++;
                    continue;
                }

                bySlot[slotId] = slot;
            }

            if (profileTemplate != null)
            {
                IReadOnlyList<WorkerSlotState> templateSlots = profileTemplate.WorkerSlots;
                for (int i = 0; i < templateSlots.Count; i++)
                {
                    WorkerSlotState template = templateSlots[i];
                    if (template == null || string.IsNullOrWhiteSpace(template.SlotId) || bySlot.ContainsKey(template.SlotId))
                    {
                        continue;
                    }

                    WorkerSlotState restored = template.CloneAsTemplateCopy();
                    workerSlots.Add(restored);
                    bySlot[restored.SlotId] = restored;
                    repairs++;
                }
            }

            return repairs;
        }

        private int NormalizeSuspendedPayrollIdsForStartup()
        {
            int repairs = 0;
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int i = lastSuspendedPayrollWorkerIds.Count - 1; i >= 0; i--)
            {
                string id = lastSuspendedPayrollWorkerIds[i]?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    lastSuspendedPayrollWorkerIds.RemoveAt(i);
                    repairs++;
                    continue;
                }

                if (!string.Equals(id, lastSuspendedPayrollWorkerIds[i], StringComparison.Ordinal))
                {
                    lastSuspendedPayrollWorkerIds[i] = id;
                    repairs++;
                }
            }

            return repairs;
        }

        private static CategoryStockState FindTemplateCategory(BusinessRuntimeState template, string categoryId)
        {
            if (template == null || string.IsNullOrWhiteSpace(categoryId))
            {
                return null;
            }

            IReadOnlyList<CategoryStockState> categories = template.CategoryStock;
            for (int i = 0; i < categories.Count; i++)
            {
                CategoryStockState stock = categories[i];
                if (stock != null && string.Equals(stock.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return stock;
                }
            }

            return null;
        }

        private static WorkerSlotState FindTemplateSlot(BusinessRuntimeState template, string slotId)
        {
            if (template == null || string.IsNullOrWhiteSpace(slotId))
            {
                return null;
            }

            IReadOnlyList<WorkerSlotState> slots = template.WorkerSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                WorkerSlotState slot = slots[i];
                if (slot != null && string.Equals(slot.SlotId, slotId, StringComparison.OrdinalIgnoreCase))
                {
                    return slot;
                }
            }

            return null;
        }


        public static BusinessRuntimeState CreateFrom(
            BusinessDefinition business,
            IReadOnlyList<ItemCategoryDefinition> categories,
            IReadOnlyList<ItemDefinition> items,
            BusinessMainFocusState mainFocus,
            BusinessThroughputMode throughputMode,
            int baselineWeeklyThroughputUnits,
            int baselineDailyServiceCapacity)
        {
            BusinessRuntimeState state = new()
            {
                businessId = business.BusinessId,
                businessType = business.BusinessType,
                currentCashCents = business.Economy.StartingCashCents,
                lastWeeklyReorderBudgetCents = business.Economy.WeeklyReorderReserveCents,
                reliability01 = 1f,
                competitionPressure01 = 0f,
                mainFocus = mainFocus ?? BusinessMainFocusState.FromDefinition(null, business.BusinessType)
            };

            for (int i = 0; i < business.WorkerSlots.Length; i++)
            {
                WorkerSlotDefinition slot = business.WorkerSlots[i];
                state.workerSlots.Add(new WorkerSlotState(slot.SlotId, slot.DisplayName, slot.BaselineWeeklyWageCents, slot.RequiredForOpening));
            }

            for (int i = 0; i < categories.Count; i++)
            {
                ItemCategoryDefinition category = categories[i];
                if (!business.OwnsCategory(category.CategoryId))
                {
                    continue;
                }

                int startingStock = 0;
                int targetStock = 0;
                for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                {
                    ItemDefinition item = items[itemIndex];
                    if (!string.Equals(item.CategoryId, category.CategoryId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    startingStock += item.StartingStockUnits;
                    targetStock += item.ReorderTargetUnits;
                }

                state.categoryStock.Add(new CategoryStockState(category.CategoryId, startingStock, targetStock));
            }

            state.RefreshStockHealth();
            state.RefreshCapacity(throughputMode, baselineWeeklyThroughputUnits, baselineDailyServiceCapacity, 0, 0);
            return state;
        }

        public BusinessRuntimeSaveDto CaptureSaveDto()
        {
            BusinessRuntimeSaveDto dto = new()
            {
                businessId = BusinessId,
                businessType = BusinessType,
                currentCashCents = CurrentCashCents,
                lastDailyRevenueCents = LastDailyRevenueCents,
                lastWeeklyPayrollCents = LastWeeklyPayrollCents,
                lastWeeklyReorderBudgetCents = LastWeeklyReorderBudgetCents,
                lastWeeklyInputUnitsConsumed = LastWeeklyInputUnitsConsumed,
                lastWeeklyOutputUnitsProduced = LastWeeklyOutputUnitsProduced,
                lastWeeklyInputProcurementSpendCents = LastWeeklyInputProcurementSpendCents,
                lastWeeklyLocalTransferRevenueCents = LastWeeklyLocalTransferRevenueCents,
                lastWeeklyLocalTransferCostCents = LastWeeklyLocalTransferCostCents,
                lastWeeklyCashTransferInCents = LastWeeklyCashTransferInCents,
                lastWeeklyCashTransferOutCents = LastWeeklyCashTransferOutCents,
                lastWeeklyOwnerDistributionCents = LastWeeklyOwnerDistributionCents,
                lastWeeklyCashBeforeCents = LastWeeklyCashBeforeCents,
                lastWeeklyCashAfterCents = LastWeeklyCashAfterCents,
                accruedLiabilityCents = AccruedLiabilityCents,
                lastWeeklyAccruedLiabilityCents = LastWeeklyAccruedLiabilityCents,
                lastWeeklyBlockedReason = LastWeeklyBlockedReason,
                lastWeeklyOperationSummary = LastWeeklyOperationSummary,
                lastWeeklyTransferSummary = LastWeeklyTransferSummary,
                weekToDateRevenueCents = WeekToDateRevenueCents,
                weekToDateUnitsSold = WeekToDateUnitsSold,
                lastDailyNetCents = LastDailyNetCents,
                weekToDateNetCents = WeekToDateNetCents,
                monthToDateNetCents = MonthToDateNetCents,
                yearToDateNetCents = YearToDateNetCents,
                allTimeNetCents = AllTimeNetCents,
                stockHealth01 = StockHealth01,
                reliability01 = Reliability01,
                competitionPressure01 = CompetitionPressure01,
                mainFocus = MainFocus.CaptureSaveDto(),
                capacity = Capacity.CaptureSaveDto()
            };

            for (int i = 0; i < categoryStock.Count; i++)
            {
                dto.categoryStock.Add(categoryStock[i].CaptureSaveDto());
            }

            for (int i = 0; i < workerSlots.Count; i++)
            {
                dto.workerSlots.Add(workerSlots[i].CaptureSaveDto());
            }

            for (int i = 0; i < lastSuspendedPayrollWorkerIds.Count; i++)
            {
                dto.lastSuspendedPayrollWorkerIds.Add(lastSuspendedPayrollWorkerIds[i]);
            }

            return dto;
        }

        public static BusinessRuntimeState FromSaveDto(BusinessRuntimeSaveDto dto, BusinessType fallbackBusinessType)
        {
            if (dto == null)
            {
                return null;
            }

            BusinessRuntimeState state = new()
            {
                businessId = dto.businessId ?? string.Empty,
                businessType = dto.businessType,
                currentCashCents = Mathf.Max(0, dto.currentCashCents),
                lastDailyRevenueCents = Mathf.Max(0, dto.lastDailyRevenueCents),
                lastWeeklyPayrollCents = Mathf.Max(0, dto.lastWeeklyPayrollCents),
                lastWeeklyReorderBudgetCents = Mathf.Max(0, dto.lastWeeklyReorderBudgetCents),
                lastWeeklyInputUnitsConsumed = Mathf.Max(0, dto.lastWeeklyInputUnitsConsumed),
                lastWeeklyOutputUnitsProduced = Mathf.Max(0, dto.lastWeeklyOutputUnitsProduced),
                lastWeeklyInputProcurementSpendCents = Mathf.Max(0, dto.lastWeeklyInputProcurementSpendCents),
                lastWeeklyLocalTransferRevenueCents = Mathf.Max(0, dto.lastWeeklyLocalTransferRevenueCents),
                lastWeeklyLocalTransferCostCents = Mathf.Max(0, dto.lastWeeklyLocalTransferCostCents),
                lastWeeklyCashTransferInCents = Mathf.Max(0, dto.lastWeeklyCashTransferInCents),
                lastWeeklyCashTransferOutCents = Mathf.Max(0, dto.lastWeeklyCashTransferOutCents),
                lastWeeklyOwnerDistributionCents = Mathf.Max(0, dto.lastWeeklyOwnerDistributionCents),
                lastWeeklyCashBeforeCents = Mathf.Max(0, dto.lastWeeklyCashBeforeCents),
                lastWeeklyCashAfterCents = Mathf.Max(0, dto.lastWeeklyCashAfterCents),
                accruedLiabilityCents = Mathf.Max(0, dto.accruedLiabilityCents),
                lastWeeklyAccruedLiabilityCents = Mathf.Max(0, dto.lastWeeklyAccruedLiabilityCents),
                lastWeeklyBlockedReason = dto.lastWeeklyBlockedReason ?? string.Empty,
                lastWeeklyOperationSummary = dto.lastWeeklyOperationSummary ?? string.Empty,
                lastWeeklyTransferSummary = dto.lastWeeklyTransferSummary ?? string.Empty,
                weekToDateRevenueCents = Mathf.Max(0, dto.weekToDateRevenueCents),
                weekToDateUnitsSold = Mathf.Max(0, dto.weekToDateUnitsSold),
                lastDailyNetCents = dto.lastDailyNetCents,
                weekToDateNetCents = dto.weekToDateNetCents,
                monthToDateNetCents = dto.monthToDateNetCents,
                yearToDateNetCents = dto.yearToDateNetCents,
                allTimeNetCents = dto.allTimeNetCents,
                stockHealth01 = Mathf.Clamp01(dto.stockHealth01),
                reliability01 = Mathf.Clamp01(dto.reliability01),
                competitionPressure01 = Mathf.Clamp01(dto.competitionPressure01),
                mainFocus = BusinessMainFocusState.FromSaveDto(dto.mainFocus, fallbackBusinessType),
                capacity = new BusinessCapacityState()
            };

            state.categoryStock.Clear();
            if (dto.categoryStock != null)
            {
                for (int i = 0; i < dto.categoryStock.Count; i++)
                {
                    state.categoryStock.Add(CategoryStockState.FromSaveDto(dto.categoryStock[i]));
                }
            }

            state.workerSlots.Clear();
            if (dto.workerSlots != null)
            {
                for (int i = 0; i < dto.workerSlots.Count; i++)
                {
                    state.workerSlots.Add(WorkerSlotState.FromSaveDto(dto.workerSlots[i]));
                }
            }

            state.lastSuspendedPayrollWorkerIds.Clear();
            if (dto.lastSuspendedPayrollWorkerIds != null)
            {
                for (int i = 0; i < dto.lastSuspendedPayrollWorkerIds.Count; i++)
                {
                    state.lastSuspendedPayrollWorkerIds.Add(dto.lastSuspendedPayrollWorkerIds[i] ?? string.Empty);
                }
            }

            state.capacity.ApplySaveDto(dto.capacity);
            state.RefreshStockHealth();
            return state;
        }

        public void RefreshCapacity(
            BusinessThroughputMode throughputMode,
            int baselineWeeklyThroughputUnits,
            int baselineDailyServiceCapacity,
            int lastWeeklyThroughputUnits,
            int lastDailyServiceVisits)
        {
            int storedUnits = 0;
            int storageCapacityUnits = 0;
            for (int i = 0; i < categoryStock.Count; i++)
            {
                storedUnits += categoryStock[i].CurrentStockUnits;
                storageCapacityUnits += categoryStock[i].TargetStockUnits;
            }

            int processingCapacity = throughputMode == BusinessThroughputMode.Service
                ? 0
                : Mathf.Max(0, baselineWeeklyThroughputUnits);
            int serviceCapacity = Mathf.Max(0, baselineDailyServiceCapacity);
            Capacity.Refresh(
                storedUnits,
                storageCapacityUnits,
                processingCapacity,
                Mathf.Max(0, lastWeeklyThroughputUnits),
                serviceCapacity,
                Mathf.Max(0, lastDailyServiceVisits));
        }

        public void ResolveDailySalesPlaceholder(string categoryId, int unitsSold, int revenueCents)
        {
            CategoryStockState stock = GetCategoryStock(categoryId);
            if (stock == null)
            {
                return;
            }

            stock.RecordDailySales(unitsSold, revenueCents);
            int revenue = Mathf.Max(0, revenueCents);
            currentCashCents += revenue;
            lastDailyRevenueCents += revenue;
            weekToDateRevenueCents += revenue;
            weekToDateUnitsSold += Mathf.Max(0, unitsSold);
            RecordNetDelta(revenue);
            RefreshStockHealth();
        }

        public void RecordDailyServiceRevenue(string categoryId, int visitsOrUnits, int revenueCents)
        {
            int units = Mathf.Max(0, visitsOrUnits);
            int revenue = Mathf.Max(0, revenueCents);
            currentCashCents += revenue;
            lastDailyRevenueCents += revenue;
            weekToDateRevenueCents += revenue;
            weekToDateUnitsSold += units;
            RecordNetDelta(revenue);

            CategoryStockState stock = GetCategoryStock(categoryId);
            stock?.RecordNonStockDailySales(units, revenue);
            RefreshStockHealth();
            RefreshWeeklyCashAfter();
        }

        public void BeginDailySalesCadence()
        {
            lastDailyRevenueCents = 0;
            lastDailyNetCents = 0;
            for (int i = 0; i < categoryStock.Count; i++)
            {
                categoryStock[i].ResetDailySales();
            }
        }

        public void ResetWeekToDateSales()
        {
            weekToDateRevenueCents = 0;
            weekToDateUnitsSold = 0;
            weekToDateNetCents = 0;
            for (int i = 0; i < categoryStock.Count; i++)
            {
                categoryStock[i].ResetWeekToDateSales();
            }
        }

        public void ResetMonthToDateNet()
        {
            monthToDateNetCents = 0;
        }

        public void ResetYearToDateNet()
        {
            yearToDateNetCents = 0;
        }

        public void BeginWeeklySettlementCadence(BusinessDefinition business, float reorderThreshold01)
        {
            BeginWeeklySettlementCadence(business, reorderThreshold01, true);
        }

        public void BeginWeeklySettlementCadence(BusinessDefinition business, float reorderThreshold01, bool resolvePayrollImmediately)
        {
            lastWeeklyInputUnitsConsumed = 0;
            lastWeeklyOutputUnitsProduced = 0;
            lastWeeklyInputProcurementSpendCents = 0;
            lastWeeklyLocalTransferRevenueCents = 0;
            lastWeeklyLocalTransferCostCents = 0;
            lastWeeklyCashTransferInCents = 0;
            lastWeeklyCashTransferOutCents = 0;
            lastWeeklyOwnerDistributionCents = 0;
            lastWeeklyCashBeforeCents = CurrentCashCents;
            lastWeeklyCashAfterCents = CurrentCashCents;
            lastWeeklyAccruedLiabilityCents = 0;
            lastWeeklyBlockedReason = string.Empty;
            lastWeeklyOperationSummary = string.Empty;
            lastWeeklyTransferSummary = string.Empty;
            lastWeeklyPayrollCents = 0;
            lastSuspendedPayrollWorkerIds.Clear();

            if (resolvePayrollImmediately)
            {
                ResolveWeeklyPayroll();
            }

            lastWeeklyReorderBudgetCents = business != null ? business.Economy.WeeklyReorderReserveCents : 0;

            for (int i = 0; i < categoryStock.Count; i++)
            {
                categoryStock[i].RefreshPendingReorderToTarget(reorderThreshold01);
            }

            RefreshWeeklyCashAfter();
        }

        public void ResolveWeeklyPayroll()
        {
            // T1B: when the PKG-6 registry is wired, every wage flows through an
            // employment record — nobody is paid who was never hired (Tech X §4.1–4.3).
            if (EmploymentRegistry != null)
            {
                ResolveWeeklyPayrollFromEmployments(EmploymentRegistry);
                return;
            }

            for (int i = 0; i < workerSlots.Count; i++)
            {
                WorkerSlotState slot = workerSlots[i];
                if (!slot.IsFilled)
                {
                    continue;
                }

                int wage = slot.WeeklyWageCents;
                if (currentCashCents >= wage)
                {
                    currentCashCents -= wage;
                    lastWeeklyPayrollCents += wage;
                    RecordNetDelta(-wage);
                    slot.MarkPaidActive();
                    continue;
                }

                string suspendedWorkerId = slot.SuspendForMissedPayroll();
                AccrueOperatingLiabilityCents(wage, $"missed payroll for {slot.AssignedWorkerDisplayName}");
                if (!string.IsNullOrWhiteSpace(suspendedWorkerId))
                {
                    lastSuspendedPayrollWorkerIds.Add(suspendedWorkerId);
                }
            }

            RefreshWeeklyCashAfter();
        }

        /// <summary>
        /// T1B: payroll through the employment authority. For each filled slot the
        /// worker's employment record is resolved — or projected once from the slot
        /// assignment evidence (Source=ProjectedFromWorkerSlot) when the legacy hire
        /// path never registered one. Wages paid are the AGREED registry wages, never
        /// the slot's template wage. Slot paid/suspended states are maintained as the
        /// legacy compatibility projection (PKG-7) so existing readouts keep working.
        /// </summary>
        private void ResolveWeeklyPayrollFromEmployments(EmploymentRelationshipRegistry employments)
        {
            EnsureEmploymentRecords(employments);

            foreach (EmploymentRelationship employment in employments.GetActiveByEmployer(BusinessId))
            {
                int wage = Mathf.Max(0, employment.Compensation.AgreedWeeklyWageCents);
                if (wage <= 0)
                {
                    continue;
                }

                if (currentCashCents >= wage)
                {
                    currentCashCents -= wage;
                    lastWeeklyPayrollCents += wage;
                    RecordNetDelta(-wage);
                    SyncSlotPaidState(employment.EmployeePersonId, paid: true);
                    continue;
                }

                employment.LifecycleState = EmploymentLifecycleState.Suspended;
                employment.SuspensionReason = EmploymentSuspensionReason.EmployerPaymentDefault;
                AccrueOperatingLiabilityCents(wage, $"missed payroll for {employment.RoleDisplayName} (employment {employment.Id})");
                SyncSlotPaidState(employment.EmployeePersonId, paid: false);
                string workerId = employment.EmployeePersonId.ToString();
                if (!lastSuspendedPayrollWorkerIds.Contains(workerId))
                {
                    lastSuspendedPayrollWorkerIds.Add(workerId);
                }
            }

            RefreshWeeklyCashAfter();
        }

        /// <summary>
        /// T1B migration-on-read: every filled slot's worker must hold an employment
        /// record before payroll runs. Records are projected from the slot assignment
        /// (the hiring evidence) exactly once — Register is first-wins, so existing
        /// records are never overwritten. Slot reassignments (new worker, same slot)
        /// get a person-specific id to avoid colliding with the previous worker's record.
        /// </summary>
        private void EnsureEmploymentRecords(EmploymentRelationshipRegistry employments)
        {
            foreach (WorkerSlotState slot in workerSlots)
            {
                if (slot == null || !slot.IsFilled)
                {
                    continue;
                }

                if (!int.TryParse(slot.AssignedWorkerId, out int personId))
                {
                    continue; // owner:xxx and unparsable ids — matches the projector's skip rule.
                }

                bool hasExistingRelationship = false;
                foreach (EmploymentRelationship existing in employments.GetByEmployee(personId))
                {
                    if (string.Equals(existing.EmployerBusinessId, BusinessId, StringComparison.Ordinal))
                    {
                        hasExistingRelationship = true;
                        break;
                    }
                }

                if (hasExistingRelationship)
                {
                    // A slot is transitional evidence only. An ended or suspended
                    // EmploymentRelationship remains authoritative and must not be
                    // silently replaced by a fresh active record during migration.
                    continue;
                }

                List<EmploymentRelationship> projected =
                    EmploymentRelationshipProjector.ProjectFromWorkerSlots(BusinessId, new[] { slot }, -1);
                foreach (EmploymentRelationship relationship in projected)
                {
                    if (employments.TryGetById(relationship.Id, out EmploymentRelationship clash)
                        && clash.EmployeePersonId != relationship.EmployeePersonId)
                    {
                        // Slot reassigned since the old record: keep the old record's
                        // history intact and give the new worker their own id.
                        relationship.Id = $"{relationship.Id}:p{relationship.EmployeePersonId}";
                    }

                    employments.Register(relationship);
                }
            }
        }

        /// <summary>
        /// T1B: keeps the legacy slot paid-state projection in sync with employment
        /// payroll outcomes, so slot-based UI readouts keep working (PKG-7).
        /// </summary>
        private void SyncSlotPaidState(int personId, bool paid)
        {
            string workerId = personId.ToString();
            foreach (WorkerSlotState slot in workerSlots)
            {
                if (slot == null || !slot.IsFilled)
                {
                    continue;
                }

                if (!string.Equals(slot.AssignedWorkerId, workerId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (paid)
                {
                    slot.MarkPaidActive();
                }
                else
                {
                    slot.SuspendForMissedPayroll();
                }
            }
        }

        public void BeginMonthlySummaryCadence(float reliability01, float competitionPressure01)
        {
            this.reliability01 = Mathf.Clamp01(reliability01);
            this.competitionPressure01 = Mathf.Clamp01(competitionPressure01);
        }

        public void AdjustReliability01(float delta)
        {
            reliability01 = Mathf.Clamp01(reliability01 + delta);
            RefreshWeeklyCashAfter();
        }

        public CategoryStockState GetCategoryStock(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return null;
            }

            for (int i = 0; i < categoryStock.Count; i++)
            {
                if (string.Equals(categoryStock[i].CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return categoryStock[i];
                }
            }

            return null;
        }

        public CategoryStockState EnsureCategoryStock(string categoryId, int currentStockUnits, int targetStockUnits)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return null;
            }

            CategoryStockState stock = GetCategoryStock(categoryId);
            if (stock != null)
            {
                return stock;
            }

            stock = new CategoryStockState(categoryId, currentStockUnits, targetStockUnits);
            categoryStock.Add(stock);
            RefreshStockHealth();
            RefreshStoredCapacityOnly();
            return stock;
        }

        public void SetCategoryStockUnits(string categoryId, int units)
        {
            CategoryStockState stock = GetCategoryStock(categoryId);
            if (stock == null)
            {
                return;
            }

            stock.SetCurrentStockUnits(units);
            RefreshStockHealth();
            RefreshStoredCapacityOnly();
        }

        public void SpendCents(int cents)
        {
            int spend = Mathf.Min(currentCashCents, Mathf.Max(0, cents));
            currentCashCents = Mathf.Max(0, currentCashCents - spend);
            RecordNetDelta(-spend);
            RefreshWeeklyCashAfter();
        }

        public void AddCashCents(int cents)
        {
            int amount = Mathf.Max(0, cents);
            currentCashCents += amount;
            RecordNetDelta(amount);
            RefreshWeeklyCashAfter();
        }

        public void SetCurrentCashCents(int cents, bool recordNetDelta = false)
        {
            int target = Mathf.Max(0, cents);
            int delta = target - CurrentCashCents;
            currentCashCents = target;
            if (recordNetDelta && delta != 0)
            {
                RecordNetDelta(delta);
            }

            RefreshWeeklyCashAfter();
        }

        public void AdjustCashCents(int deltaCents, bool recordNetDelta = false)
        {
            if (deltaCents == 0)
            {
                return;
            }

            SetCurrentCashCents(CurrentCashCents + deltaCents, recordNetDelta);
        }

        internal BusinessRuntimeTransactionSnapshot CaptureTransactionSnapshot()
        {
            return new BusinessRuntimeTransactionSnapshot(
                currentCashCents,
                lastWeeklyLocalTransferRevenueCents,
                lastWeeklyLocalTransferCostCents,
                lastWeeklyCashAfterCents,
                lastWeeklyTransferSummary,
                lastDailyNetCents,
                weekToDateNetCents,
                monthToDateNetCents,
                yearToDateNetCents,
                allTimeNetCents);
        }

        internal void RestoreTransactionSnapshot(BusinessRuntimeTransactionSnapshot snapshot)
        {
            currentCashCents = Mathf.Max(0, snapshot.CurrentCashCents);
            lastWeeklyLocalTransferRevenueCents = Mathf.Max(0, snapshot.LastWeeklyLocalTransferRevenueCents);
            lastWeeklyLocalTransferCostCents = Mathf.Max(0, snapshot.LastWeeklyLocalTransferCostCents);
            lastWeeklyCashAfterCents = Mathf.Max(0, snapshot.LastWeeklyCashAfterCents);
            lastWeeklyTransferSummary = snapshot.LastWeeklyTransferSummary ?? string.Empty;
            lastDailyNetCents = snapshot.LastDailyNetCents;
            weekToDateNetCents = snapshot.WeekToDateNetCents;
            monthToDateNetCents = snapshot.MonthToDateNetCents;
            yearToDateNetCents = snapshot.YearToDateNetCents;
            allTimeNetCents = snapshot.AllTimeNetCents;
        }

        public void RecordWeeklyCashTransferIn(int cents)
        {
            lastWeeklyCashTransferInCents += Mathf.Max(0, cents);
            RefreshWeeklyCashAfter();
        }

        public void RecordWeeklyCashTransferOut(int cents)
        {
            lastWeeklyCashTransferOutCents += Mathf.Max(0, cents);
            RefreshWeeklyCashAfter();
        }

        public void RecordWeeklyOwnerDistribution(int cents)
        {
            lastWeeklyOwnerDistributionCents += Mathf.Max(0, cents);
            RefreshWeeklyCashAfter();
        }

        public bool TrySpendCents(int cents)
        {
            int cost = Mathf.Max(0, cents);
            if (currentCashCents < cost)
            {
                return false;
            }

            currentCashCents -= cost;
            RecordNetDelta(-cost);
            RefreshWeeklyCashAfter();
            return true;
        }

        public void RecordWeeklyInputProcurement(int units, int spendCents)
        {
            lastWeeklyInputProcurementSpendCents += Mathf.Max(0, spendCents);
            RefreshWeeklyCashAfter();
        }

        public void RecordWeeklyOperation(int inputUnitsConsumed, int outputUnitsProduced, string summary, string blockedReason)
        {
            lastWeeklyInputUnitsConsumed = Mathf.Max(0, inputUnitsConsumed);
            lastWeeklyOutputUnitsProduced = Mathf.Max(0, outputUnitsProduced);
            lastWeeklyOperationSummary = summary ?? string.Empty;
            lastWeeklyBlockedReason = blockedReason ?? string.Empty;
            RefreshWeeklyCashAfter();
        }

        public void AddWeeklyLocalTransferRevenue(int cents, string summarySegment)
        {
            int revenue = Mathf.Max(0, cents);
            currentCashCents += revenue;
            lastWeeklyLocalTransferRevenueCents += revenue;
            RecordNetDelta(revenue);
            AppendWeeklyTransferSummary(summarySegment);
            RefreshWeeklyCashAfter();
        }

        public void AddWeeklyLocalTransferCost(int cents, string summarySegment)
        {
            int cost = Mathf.Max(0, cents);
            int spend = Mathf.Min(currentCashCents, cost);
            currentCashCents = Mathf.Max(0, currentCashCents - spend);
            lastWeeklyLocalTransferCostCents += cost;
            RecordNetDelta(-spend);
            int shortfall = Mathf.Max(0, cost - spend);
            if (shortfall > 0)
            {
                AccrueOperatingLiabilityCents(shortfall, summarySegment);
            }

            AppendWeeklyTransferSummary(summarySegment);
            RefreshWeeklyCashAfter();
        }

        public void AccrueOperatingLiabilityCents(int cents, string reason = null)
        {
            int amount = Mathf.Max(0, cents);
            if (amount <= 0)
            {
                return;
            }

            accruedLiabilityCents += amount;
            lastWeeklyAccruedLiabilityCents += amount;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                AppendWeeklyBlockedReason($"accrued liability: {reason.Trim()}");
            }
        }

        public void AppendWeeklyTransferSummary(string summarySegment)
        {
            if (string.IsNullOrWhiteSpace(summarySegment))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(lastWeeklyTransferSummary))
            {
                lastWeeklyTransferSummary += "; ";
            }

            lastWeeklyTransferSummary += summarySegment.Trim();
        }

        public void SetWeeklyBlockedReason(string blockedReason)
        {
            lastWeeklyBlockedReason = blockedReason ?? string.Empty;
        }

        public void AppendWeeklyBlockedReason(string blockedReason)
        {
            if (string.IsNullOrWhiteSpace(blockedReason))
            {
                return;
            }

            string reason = blockedReason.Trim();
            if (string.IsNullOrWhiteSpace(lastWeeklyBlockedReason))
            {
                lastWeeklyBlockedReason = reason;
                return;
            }

            string[] existing = lastWeeklyBlockedReason.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < existing.Length; i++)
            {
                if (string.Equals(existing[i].Trim(), reason, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            lastWeeklyBlockedReason += "; " + reason;
        }

        public void RefreshWeeklyCashAfter()
        {
            lastWeeklyCashAfterCents = CurrentCashCents;
        }

        private void RecordNetDelta(int deltaCents)
        {
            if (deltaCents == 0)
            {
                return;
            }

            lastDailyNetCents += deltaCents;
            weekToDateNetCents += deltaCents;
            monthToDateNetCents += deltaCents;
            yearToDateNetCents += deltaCents;
            allTimeNetCents += deltaCents;
        }

        public bool TryAssignOwnerOperator(BusinessOwnerIdentity owner)
        {
            if (owner == null || owner.OwnerKind == BusinessOwnerKind.Player || workerSlots.Count == 0 || FilledWorkerCount > 0)
            {
                return false;
            }

            int slotIndex = FindFirstOpenRequiredSlotIndex();
            if (slotIndex < 0)
            {
                slotIndex = FindFirstOpenSlotIndex();
            }

            if (slotIndex < 0)
            {
                return false;
            }

            WorkerSlotState slot = workerSlots[slotIndex];
            string ownerId = owner.PersonId >= 0
                ? $"owner:{owner.PersonId}"
                : $"owner:{owner.DisplayName}";
            slot.Assign(ownerId, owner.DisplayName, slot.WeeklyWageCents);
            return true;
        }

        public void ReceivePendingReorders()
        {
            for (int i = 0; i < categoryStock.Count; i++)
            {
                categoryStock[i].ReceivePendingReorder();
            }

            RefreshStockHealth();
        }

        public int ReceivePendingReorderUnits(string categoryId, int units)
        {
            CategoryStockState stock = GetCategoryStock(categoryId);
            if (stock == null)
            {
                return 0;
            }

            int receivedUnits = stock.ReceivePendingReorderUnits(units);
            RefreshStockHealth();
            return receivedUnits;
        }

        public int AllocatePendingReorderUnits(string categoryId, int units)
        {
            CategoryStockState stock = GetCategoryStock(categoryId);
            if (stock == null)
            {
                return 0;
            }

            int allocatedUnits = Mathf.Clamp(units, 0, stock.PendingReorderUnits);
            if (allocatedUnits <= 0)
            {
                return 0;
            }

            stock.SetPendingReorderUnits(stock.PendingReorderUnits - allocatedUnits);
            return allocatedUnits;
        }

        public bool TryConsumeCategoryStockUnits(string categoryId, int units, out int consumedUnits)
        {
            consumedUnits = 0;
            CategoryStockState stock = GetCategoryStock(categoryId);
            if (stock == null)
            {
                return false;
            }

            int requestedUnits = Mathf.Max(0, units);
            if (!stock.TryConsumeStockUnits(requestedUnits))
            {
                return false;
            }

            consumedUnits = requestedUnits;
            RefreshStockHealth();
            RefreshStoredCapacityOnly();
            return true;
        }

        public int AddCategoryStockUnits(string categoryId, int units)
        {
            CategoryStockState stock = GetCategoryStock(categoryId);
            if (stock == null)
            {
                return 0;
            }

            int acceptedUnits = stock.AddStockUnits(units);
            RefreshStockHealth();
            RefreshStoredCapacityOnly();
            return acceptedUnits;
        }

        public bool CanReceiveShipment(string categoryId, int units, out string blockedReason)
        {
            blockedReason = string.Empty;
            CategoryStockState stock = GetCategoryStock(categoryId);
            if (stock == null)
            {
                blockedReason = $"destination category missing: {categoryId}";
                return false;
            }

            int requestedUnits = Mathf.Max(0, units);
            if (requestedUnits <= 0)
            {
                blockedReason = "shipment had no cargo";
                return false;
            }

            int capacityRemaining = stock.TargetStockUnits > 0
                ? Mathf.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits)
                : requestedUnits;
            if (capacityRemaining <= 0)
            {
                blockedReason = $"category full: {categoryId}";
                return false;
            }

            return true;
        }

        public int ApplyDeliveredShipment(string categoryId, int units)
        {
            return AddCategoryStockUnits(categoryId, units);
        }

        public bool HasReorderNeed(float threshold01)
        {
            for (int i = 0; i < categoryStock.Count; i++)
            {
                if (categoryStock[i].NeedsReorder(threshold01))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshStockHealth()
        {
            if (categoryStock.Count == 0)
            {
                stockHealth01 = 1f;
                return;
            }

            float total = 0f;
            for (int i = 0; i < categoryStock.Count; i++)
            {
                total += categoryStock[i].StockHealth01;
            }

            stockHealth01 = Mathf.Clamp01(total / categoryStock.Count);
        }

        private void RefreshStoredCapacityOnly()
        {
            int storedUnits = 0;
            int storageCapacityUnits = 0;
            for (int i = 0; i < categoryStock.Count; i++)
            {
                storedUnits += categoryStock[i].CurrentStockUnits;
                storageCapacityUnits += categoryStock[i].TargetStockUnits;
            }

            Capacity.Refresh(
                storedUnits,
                storageCapacityUnits,
                Capacity.ProcessingCapacityUnitsPerWeek,
                Capacity.CurrentProcessingUnitsPerWeek,
                Capacity.ServiceCapacityVisitsPerDay,
                Capacity.CurrentServiceVisitsPerDay);
        }

        private int FindFirstOpenRequiredSlotIndex()
        {
            for (int i = 0; i < workerSlots.Count; i++)
            {
                if (workerSlots[i].RequiredForOpening && !workerSlots[i].IsFilled)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindFirstOpenSlotIndex()
        {
            for (int i = 0; i < workerSlots.Count; i++)
            {
                if (!workerSlots[i].IsFilled)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
