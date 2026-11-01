using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Economy.Generic
{
    /// <summary>
    /// Pass IV (Worker 3): the production stage machine.
    ///
    /// Extends the existing ProductionProcessState / GenericProductionRuntime with
    /// ProductionStageDefinition / ProductionStageState (ProductionStageContracts.cs).
    /// Existing ProductionPhaseDefinitions map to stages 1:1:
    ///   RequiresPerson = true  -> ActiveLabor  (real WorkTask via GenericProductionTaskBridge)
    ///   RequiresPerson = false -> PassiveProcess (worker released, due events drive it)
    /// Slices author richer plans (PeriodicTending, BlockingDependency, tending grace
    /// windows, MissedDeadlineConsequence) per stage.
    ///
    /// Active/passive contract:
    /// - An ActiveLabor stage creates a real WorkTask (worked via TaskAuthority.RecordWorkProgress,
    ///   completed explicitly — never auto-completed, so the handoff can intervene).
    /// - When the NEXT stage is passive WITH a tending requirement, the finished active
    ///   task does NOT complete: it goes to WaitingOnPassiveProcess via
    ///   ProductionTaskHandoffDriver (worker released; equipment/workspace/material
    ///   reservations stay held) and due events are scheduled for tending/unload.
    /// - The next ActiveLabor stage adopts the waiting task (same worker responsibility
    ///   episode); the tending/unload work is recorded on it, then it completes.
    /// - A missed tending deadline applies the passive stage's configured
    ///   MissedDeadlineConsequence (quality/waste/batch-failed/maintenance) —
    ///   method-specific, never a universal "everything burns" rule.
    ///
    /// No second production engine: input consumption stays exactly-once via
    /// ProductionProcessState.ConsumeInputsOnce, output stays guarded by the
    /// OutputCreated flag, and all labor goes through TaskAuthority.
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part II §§2.1-2.8.
    /// </summary>
    public sealed class ProductionStagePlan
    {
        public List<ProductionStageDefinition> Stages = new List<ProductionStageDefinition>();

        /// <summary>
        /// Default mapping: one stage per phase. RequiresPerson -> ActiveLabor,
        /// otherwise PassiveProcess. Reservation keys follow the unified key-space
        /// (asset:{id}, workspace:{id}).
        /// </summary>
        public static ProductionStagePlan FromPhases(ProductionMethodDefinition method)
        {
            var plan = new ProductionStagePlan();
            if (method == null || method.Phases == null) return plan;
            foreach (ProductionPhaseDefinition phase in method.Phases)
            {
                var kind = phase != null && phase.RequiresPerson
                    ? ProductionStageKind.ActiveLabor
                    : ProductionStageKind.PassiveProcess;
                float seconds = (phase != null ? Math.Max(1, phase.ElapsedMinutes) : 1) * 60f;
                var def = new ProductionStageDefinition(kind, seconds);
                if (phase != null)
                {
                    if (phase.ReservedEquipmentIds != null)
                        foreach (string id in phase.ReservedEquipmentIds)
                            if (!string.IsNullOrWhiteSpace(id))
                                def.StageReservationKeys.Add(ReservationKeyConventions.Asset(id));
                    if (phase.ReservedWorkspaceIds != null)
                        foreach (string id in phase.ReservedWorkspaceIds)
                            if (!string.IsNullOrWhiteSpace(id))
                                def.StageReservationKeys.Add(ReservationKeyConventions.Workspace(id));
                }
                plan.Stages.Add(def);
            }
            return plan;
        }

        /// <summary>
        /// Whether entering this passive/tending stage requires the previous labor task
        /// to wait (worker released, recalled for tending/unload) instead of completing.
        /// </summary>
        public static bool StageRequiresTendingReturn(ProductionStageDefinition def)
        {
            if (def == null) return false;
            if (def.Kind == ProductionStageKind.PeriodicTending) return true;
            return def.Kind == ProductionStageKind.PassiveProcess
                && def.TendingGraceGameSeconds > 0f
                && def.MissedConsequence != MissedDeadlineConsequence.None;
        }
    }

    /// <summary>
    /// Pass IV (Worker 3, wired to Worker 1 at merge): the WaitingOnPassiveProcess handoff.
    ///
    /// Moves an InProgress labor task to passive waiting via
    /// TaskAuthority.MarkWaitingOnPassiveProcess: the worker is released, the task stays
    /// open against the passive stage, and equipment/workspace/materials stay reserved.
    /// Resume for tending goes through TaskAuthority.ResumeTask.
    ///
    /// Nothing here bypasses TaskAuthority: both paths go through its public API.
    /// </summary>
    public sealed class ProductionTaskHandoffDriver
    {
        /// <summary>
        /// Moves an InProgress labor task to passive waiting: the worker is released,
        /// the task stays open against the passive stage. Returns false with a loud
        /// reason when the transition is refused.
        /// </summary>
        public bool TryEnterPassiveWait(
            TaskAuthority authority, EntityId taskId, WorkTimeBudgetStore budgets, int dayIndex,
            string dueEventId, out string reason)
        {
            reason = string.Empty;
            if (authority == null) { reason = "ProductionTaskHandoffDriver: no TaskAuthority."; return false; }
            WorkTask task = authority.FindTask(taskId);
            if (task == null) { reason = "ProductionTaskHandoffDriver: unknown task " + taskId + "."; return false; }
            if (!TaskLifecyclePolicy.CanTransition(task.Status, TaskStatus.WaitingOnPassiveProcess))
            {
                reason = "ProductionTaskHandoffDriver: TaskLifecyclePolicy forbids "
                    + task.Status + " -> WaitingOnPassiveProcess.";
                return false;
            }

            return authority.MarkWaitingOnPassiveProcess(
                taskId,
                dueEventId ?? string.Empty,
                "ProductionTaskHandoffDriver: entering passive wait for stage.",
                out reason);
        }

        /// <summary>
        /// Returns a passively-waiting task to InProgress for tending/unload work.
        /// Delegates to TaskAuthority.ResumeTask.
        /// </summary>
        public bool TryResumeForTending(
            TaskAuthority authority, EntityId taskId, EntityId workerId,
            WorkTimeBudgetStore budgets, int dayIndex,
            out string reason)
        {
            reason = string.Empty;
            if (authority == null) { reason = "ProductionTaskHandoffDriver: no TaskAuthority."; return false; }
            WorkTask task = authority.FindTask(taskId);
            if (task == null) { reason = "ProductionTaskHandoffDriver: unknown task " + taskId + "."; return false; }

            if (task.Status != TaskStatus.WaitingOnPassiveProcess)
            {
                reason = "ProductionTaskHandoffDriver: cannot resume tending — task is " + task.Status
                    + " (expected WaitingOnPassiveProcess).";
                return false;
            }

            return authority.ResumeTask(
                taskId,
                "ProductionTaskHandoffDriver: resuming for tending/unload.",
                out reason);
        }
    }

    /// <summary>
    /// Pass IV (Worker 3): non-persisted runtime hooks for a staged process. The process
    /// state itself is plain data (persisted); these callbacks wire it to the live
    /// authorities. Re-supplied after save/load.
    /// </summary>
    public sealed class StagedProcessHooks
    {
        public ProductionMethodDefinition Method;
        public TaskAuthority Tasks;
        public WorkTimeBudgetStore Budgets;
        public GenericProductionTaskBridge TaskBridge;
        public IDueEventScheduler DueEvents;
        /// <summary>Optional; when null, stage reservation keys are recorded but not enforced (noted loudly).</summary>
        public IUnifiedReservationAuthority Reservations;
        public Func<float> ClockGameSeconds;
        public int DayIndex;
        public EntityId BusinessId;
        /// <summary>Creates the process output exactly once (guarded by OutputCreated).</summary>
        public Func<StagedProductionProcess, bool> CreateOutput;
        /// <summary>Overrides the default missed-deadline consequence handling (optional).</summary>
        public Func<StagedProductionProcess, ProductionStageState, MissedDeadlineConsequence, List<string>, bool> ApplyConsequence;
        /// <summary>Called for EquipmentMaintenanceRequired (equipment reservation key).</summary>
        public Action<string> MarkEquipmentNeedsMaintenance;
        /// <summary>Worker who performs tending check-ins (PeriodicTending).</summary>
        public EntityId TendingWorkerId = EntityId.Invalid;
        /// <summary>Minutes of labor one tending check-in takes.</summary>
        public int TendingWorkMinutes = 5;
        public List<string> Diagnostics;

        public float Now() => ClockGameSeconds != null ? ClockGameSeconds() : 0f;

        public void Log(string message)
        {
            if (Diagnostics != null) Diagnostics.Add(message);
        }
    }

    /// <summary>
    /// Pass IV (Worker 3): one waste record from a missed deadline (PartialWaste).
    /// Plain data; persisted with the process.
    /// </summary>
    [Serializable]
    public sealed class StageWasteRecord
    {
        public int StageIndex;
        public float WasteFraction01;
        public string Note = string.Empty;
    }

    /// <summary>
    /// Pass IV (Worker 3): baseline worked-minutes for a stage whose labor task was
    /// carried in from an earlier stage (tending/unload episode). Lets the machine
    /// measure the CURRENT stage's work on a shared task. Plain data; persisted.
    /// </summary>
    [Serializable]
    public sealed class StageLaborBaseline
    {
        public int StageIndex;
        public int BaselineWorkedMinutes;
    }

    /// <summary>
    /// Pass IV (Worker 3): the staged production process. Wraps the existing
    /// ProductionProcessState (phase machine kept 1:1 with stages) and owns the
    /// ProductionStageState list. Plain data + behavior; persisted via the nested
    /// save DTO. Save-DTO methods live on this class (owning runtime).
    /// </summary>
    [Serializable]
    public sealed class StagedProductionProcess
    {
        public string ProcessId = string.Empty;
        public ProductionProcessState PhaseState = new ProductionProcessState();
        public ProductionStagePlan StagePlan = new ProductionStagePlan();
        public List<ProductionStageState> StageStates = new List<ProductionStageState>();
        public int CurrentStageIndex;
        public bool Cancelled;
        public bool Failed;
        public string FailedReason = string.Empty;
        /// <summary>Accumulated quality penalty 0..1 from QualityDegraded consequences.</summary>
        public float QualityPenalty01;
        public List<StageWasteRecord> WasteRecords = new List<StageWasteRecord>();
        public List<StageLaborBaseline> LaborBaselines = new List<StageLaborBaseline>();
        /// <summary>
        /// Definition id of the labor task currently waiting through a passive stage
        /// (empty when none). The task is rebound read-only via FindOpenTask — never
        /// a second task for the same definition.
        /// </summary>
        public string WaitingLaborTaskDefinitionId = string.Empty;
        /// <summary>Stage index whose tending deadline is pending (-1 when none).</summary>
        public int DeadlineStageIndex = -1;
        /// <summary>Why the process is parked waiting for a stage entry (empty when not parked).</summary>
        public string ParkedReason = string.Empty;
        public List<string> EventLog = new List<string>();

        [NonSerialized]
        private ProductionTaskHandoffDriver handoffDriver = new ProductionTaskHandoffDriver();

        [NonSerialized]
        private readonly Dictionary<string, EntityId> boundTaskIds =
            new Dictionary<string, EntityId>(StringComparer.Ordinal);

        public bool IsComplete => PhaseState != null && PhaseState.Completed;
        public bool IsActive => !Cancelled && !Failed && !IsComplete;

        public ProductionStageDefinition CurrentStageDefinition =>
            StagePlan != null && CurrentStageIndex >= 0 && CurrentStageIndex < StagePlan.Stages.Count
                ? StagePlan.Stages[CurrentStageIndex]
                : null;

        public ProductionStageState CurrentStageState =>
            CurrentStageIndex >= 0 && CurrentStageIndex < StageStates.Count
                ? StageStates[CurrentStageIndex]
                : null;

        private void Note(string message)
        {
            EventLog.Add(message);
            if (EventLog.Count > 200) EventLog.RemoveAt(0);
        }

        public string DuePayloadRef() => "stageproc:" + (ProcessId ?? string.Empty);

        /// <summary>
        /// Stable due-event id for this process + kind, per the foundation convention
        /// ("due-" + kind + "-" + payloadRef): rescheduling replaces, never duplicates.
        /// </summary>
        public string DueEventIdFor(DueEventKind kind) => "due-" + kind + "-" + DuePayloadRef();

        /// <summary>Deterministic labor-task definition id for a stage (bridge convention).</summary>
        public string StageTaskDefinitionId(int stageIndex) =>
            "generic-production:" + (ProcessId ?? string.Empty) + ":phase:" + stageIndex + ":definition";

        // ---------- lifecycle ----------

        /// <summary>
        /// Begins the process: validates the plan against the method's phases (1:1),
        /// starts the phase machine, and enters stage 0.
        /// </summary>
        public bool Begin(StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            if (hooks == null) { reason = "StagedProductionProcess.Begin: no hooks."; return false; }
            if (hooks.Method == null) { reason = "StagedProductionProcess.Begin: no method."; return false; }
            if (StagePlan == null || StagePlan.Stages.Count == 0)
            { reason = "StagedProductionProcess.Begin: no stage plan."; return false; }
            if (hooks.Method.Phases == null || hooks.Method.Phases.Count != StagePlan.Stages.Count)
            {
                reason = "StagedProductionProcess.Begin: stage plan has " + (StagePlan.Stages?.Count ?? 0)
                    + " stages but the method has " + (hooks.Method.Phases?.Count ?? 0)
                    + " phases — stages map 1:1 to phases.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(ProcessId)) ProcessId = Guid.NewGuid().ToString("N");
            PhaseState.ProcessId = ProcessId;
            PhaseState.MethodId = hooks.Method.MethodId ?? string.Empty;
            PhaseState.AssignedPersonId = workerId.IsValid ? workerId.ToString() : string.Empty;
            PhaseState.Start(hooks.Method);

            StageStates.Clear();
            for (int i = 0; i < StagePlan.Stages.Count; i++) StageStates.Add(new ProductionStageState());
            CurrentStageIndex = 0;
            WaitingLaborTaskDefinitionId = string.Empty;
            DeadlineStageIndex = -1;
            ParkedReason = string.Empty;
            Note("process " + ProcessId + " began (" + hooks.Method.MethodId + ", "
                + StagePlan.Stages.Count + " stages).");
            return EnterStage(0, hooks, workerId, out reason);
        }

        /// <summary>
        /// Enters a stage: begins its state, reserves stage resources, creates/starts
        /// the labor task for ActiveLabor (adopting a carried waiting task when one is
        /// pending), or suspends into passive/tending with due events scheduled.
        /// </summary>
        public bool EnterStage(int stageIndex, StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            if (!IsActive) { reason = "Process " + ProcessId + " is not active."; return false; }
            ProductionStageDefinition def = stageIndex >= 0 && stageIndex < StagePlan.Stages.Count
                ? StagePlan.Stages[stageIndex] : null;
            if (def == null) { reason = "No stage definition at index " + stageIndex + "."; return false; }
            ProductionStageState state = StageStates[stageIndex];
            float now = hooks.Now();
            int savedStageIndex = CurrentStageIndex;
            CurrentStageIndex = stageIndex;

            // Reserve stage resources (atomic, all-or-nothing).
            string reservationId = string.Empty;
            if (def.StageReservationKeys != null && def.StageReservationKeys.Count > 0)
            {
                if (hooks.Reservations != null)
                {
                    var request = new ReservationRequest(
                        TaskIdentityConventions.ReservationOwnerKey("stageproc:" + ProcessId),
                        "stage " + stageIndex + " of " + ProcessId);
                    float end = now + def.DurationGameSeconds + def.TendingGraceGameSeconds;
                    foreach (string key in def.StageReservationKeys)
                    {
                        if (string.IsNullOrWhiteSpace(key)) continue;
                        request.Items.Add(new ReservationItem(key, 1f, now, Math.Max(now + 1f, end)));
                    }
                    string refusal = hooks.Reservations.TryReserveAll(request, out ReservationHandle handle);
                    if (refusal != null)
                    {
                        reason = "Stage " + stageIndex + " refused: resource reservation failed — " + refusal;
                        Note(reason);
                        // The stage was not entered — restore the index before refusing.
                        CurrentStageIndex = savedStageIndex;
                        return false;
                    }
                    reservationId = handle != null ? handle.ReservationId : string.Empty;
                }
                else
                {
                    string note = "Stage " + stageIndex + ": no reservation authority — keys ["
                        + string.Join(",", def.StageReservationKeys) + "] recorded but NOT enforced.";
                    Note(note);
                    hooks.Log("StagedProductionProcess: " + note);
                }
            }

            state.Begin(stageIndex, def, now);
            state.SetReservation(reservationId);

            bool ok;
            switch (def.Kind)
            {
                case ProductionStageKind.ActiveLabor:
                    ok = EnterActiveStage(stageIndex, hooks, workerId, out reason);
                    break;
                case ProductionStageKind.PassiveProcess:
                case ProductionStageKind.PeriodicTending:
                    ok = EnterPassiveStage(stageIndex, hooks, out reason);
                    break;
                case ProductionStageKind.BlockingDependency:
                    Note("stage " + stageIndex + " (" + def.Kind + "): waiting on external dependency — no due event.");
                    ok = true;
                    break;
                default:
                    reason = "Stage " + stageIndex + " has unspecified kind — refusing loudly.";
                    ok = false;
                    break;
            }
            if (!ok)
            {
                // Atomic entry: the stage was not entered — restore the index and release.
                CurrentStageIndex = savedStageIndex;
                ReleaseStageReservation(stageIndex, hooks);
            }
            return ok;
        }

        private WorkTask ResolveTaskByDefinition(string definitionId, StagedProcessHooks hooks)
        {
            if (string.IsNullOrWhiteSpace(definitionId) || hooks.Tasks == null) return null;
            if (boundTaskIds.TryGetValue(definitionId, out EntityId cached))
            {
                WorkTask hit = hooks.Tasks.FindTask(cached);
                if (hit != null) return hit;
                boundTaskIds.Remove(definitionId);
            }
            // Read-only rebind after save/load — never a second task for the same definition.
            WorkTask found = hooks.Tasks.FindOpenTask(definitionId, hooks.BusinessId, EntityId.Invalid);
            if (found != null) boundTaskIds[definitionId] = found.TaskId;
            return found;
        }

        private bool EnterActiveStage(int stageIndex, StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            ProductionStageState state = StageStates[stageIndex];
            if (hooks.Tasks == null) { reason = "Active stage needs TaskAuthority."; return false; }

            // Adopt a carried waiting task (tending/unload continues the episode).
            WorkTask task = null;
            bool carried = false;
            if (!string.IsNullOrWhiteSpace(WaitingLaborTaskDefinitionId))
            {
                task = ResolveTaskByDefinition(WaitingLaborTaskDefinitionId, hooks);
                if (task != null && task.Status != TaskStatus.Complete
                    && task.Status != TaskStatus.Failed && task.Status != TaskStatus.Cancelled)
                {
                    carried = true;
                    state.SetLaborTask(task.TaskId.ToString());
                    SetLaborBaseline(stageIndex, task.MinutesWorked);
                    Note("stage " + stageIndex + ": adopted waiting labor task " + task.TaskId
                        + " (" + task.Status + ") for tending/unload.");
                    EntityId tender = workerId.IsValid ? workerId : hooks.TendingWorkerId;
                    bool ok = handoffDriver.TryResumeForTending(
                        hooks.Tasks, task.TaskId, tender, hooks.Budgets, hooks.DayIndex,
                        out string resumeReason);
                    Note("stage " + stageIndex + ": tending resume — " + resumeReason);
                    if (!ok) { reason = resumeReason; return false; }
                    task = hooks.Tasks.FindTask(task.TaskId);
                }
                else
                {
                    Note("stage " + stageIndex + ": carried task " + WaitingLaborTaskDefinitionId
                        + " is gone (" + (task != null ? task.Status.ToString() : "not found")
                        + ") — refusing loudly; the episode cannot continue without its labor record.");
                    reason = "Stage " + stageIndex + ": the waiting labor task disappeared — refusing loudly.";
                    return false;
                }
            }

            if (task == null)
            {
                if (hooks.TaskBridge == null) { reason = "Active stage needs the production task bridge."; return false; }
                task = hooks.TaskBridge.EnsureCurrentPhaseTask(PhaseState, hooks.Method, workerId, out reason);
                if (task == null) return false;
                state.SetLaborTask(task.TaskId.ToString());
                boundTaskIds[StageTaskDefinitionId(stageIndex)] = task.TaskId;
                Note("stage " + stageIndex + ": labor task " + task.TaskId + " ensured.");
            }

            if (task.Status == TaskStatus.Queued)
            {
                if (!workerId.IsValid) { reason = "Stage " + stageIndex + ": no worker assigned."; return false; }
                if (!hooks.Tasks.AssignTask(task.TaskId, workerId, hooks.Budgets, out reason)) return false;
                task = hooks.Tasks.FindTask(task.TaskId);
            }
            if (task.Status == TaskStatus.Assigned)
            {
                if (!hooks.Tasks.StartTask(task.TaskId, hooks.DayIndex, out reason)) return false;
            }
            else if (task.Status != TaskStatus.InProgress)
            {
                reason = "Stage " + stageIndex + ": labor task is " + task.Status + ", cannot work.";
                return false;
            }
            if (carried)
                Note("stage " + stageIndex + ": tending/unload episode continues on " + task.TaskId + ".");
            else
                Note("stage " + stageIndex + ": labor task " + task.TaskId + " in progress.");
            return true;
        }

        private bool EnterPassiveStage(int stageIndex, StagedProcessHooks hooks, out string reason)
        {
            reason = string.Empty;
            ProductionStageState state = StageStates[stageIndex];
            ProductionStageDefinition def = StagePlan.Stages[stageIndex];

            if (hooks.DueEvents == null)
            {
                reason = "Stage " + stageIndex + ": passive stage needs the due-event scheduler.";
                return false;
            }
            string payload = DuePayloadRef();
            if (def.Kind == ProductionStageKind.PeriodicTending && def.TendingIntervalGameSeconds > 0f)
            {
                float first = state.StartGameSeconds + def.TendingIntervalGameSeconds;
                string problem = hooks.DueEvents.Schedule(DueEventKind.TendingDue, first, payload, out _);
                if (problem != null) { reason = "Tending scheduling failed: " + problem; return false; }
                Note("stage " + stageIndex + ": first tending due at +" + def.TendingIntervalGameSeconds + "s.");
            }
            {
                string problem = hooks.DueEvents.Schedule(
                    DueEventKind.PassiveProcessComplete, state.DueGameSeconds, payload, out _);
                if (problem != null) { reason = "Passive completion scheduling failed: " + problem; return false; }
            }
            // The tending deadline survives the passive stage's own completion: it guards
            // the tending/unload episode that follows. Cancelled when the tending
            // completes (carried task completes) or the process ends.
            if (def.TendingGraceGameSeconds > 0f && def.MissedConsequence != MissedDeadlineConsequence.None)
            {
                string problem = hooks.DueEvents.Schedule(
                    DueEventKind.TendingDeadlineMissed, state.DeadlineGameSeconds, payload, out _);
                if (problem != null) { reason = "Deadline scheduling failed: " + problem; return false; }
                DeadlineStageIndex = stageIndex;
                Note("stage " + stageIndex + ": tending deadline at +"
                    + (def.DurationGameSeconds + def.TendingGraceGameSeconds) + "s (" + def.MissedConsequence + ").");
            }
            Note("stage " + stageIndex + " (" + def.Kind + "): passive until +" + def.DurationGameSeconds + "s.");
            return true;
        }

        private void SetLaborBaseline(int stageIndex, int baselineWorkedMinutes)
        {
            for (int i = 0; i < LaborBaselines.Count; i++)
            {
                if (LaborBaselines[i] != null && LaborBaselines[i].StageIndex == stageIndex)
                {
                    LaborBaselines[i].BaselineWorkedMinutes = baselineWorkedMinutes;
                    return;
                }
            }
            LaborBaselines.Add(new StageLaborBaseline { StageIndex = stageIndex, BaselineWorkedMinutes = baselineWorkedMinutes });
        }

        private int LaborBaselineFor(int stageIndex)
        {
            foreach (StageLaborBaseline b in LaborBaselines)
                if (b != null && b.StageIndex == stageIndex) return b.BaselineWorkedMinutes;
            return 0;
        }

        /// <summary>
        /// Works the current ActiveLabor stage's task (real TaskAuthority.RecordWorkProgress —
        /// no auto-complete, so the passive handoff can intervene). When the stage's work
        /// is done the task either completes (next stage active/complete) or hands off to
        /// passive waiting (next stage passive with a tending requirement).
        /// </summary>
        public bool WorkActiveStage(StagedProcessHooks hooks, EntityId workerId, int minutes, out string reason)
        {
            reason = string.Empty;
            if (!IsActive) { reason = "Process is not active."; return false; }
            ProductionStageDefinition def = CurrentStageDefinition;
            if (def == null || def.Kind != ProductionStageKind.ActiveLabor)
            { reason = "Current stage is not ActiveLabor."; return false; }
            ProductionStageState state = CurrentStageState;
            // The labor task: a carried tending episode rebinds via the waiting definition
            // id; otherwise the stage's own deterministic definition id.
            WorkTask task = !string.IsNullOrWhiteSpace(WaitingLaborTaskDefinitionId)
                ? ResolveTaskByDefinition(WaitingLaborTaskDefinitionId, hooks)
                    ?? ResolveTaskByDefinition(StageTaskDefinitionId(CurrentStageIndex), hooks)
                : ResolveTaskByDefinition(StageTaskDefinitionId(CurrentStageIndex), hooks);
            if (task == null) { reason = "Active stage has no labor task."; return false; }
            if (task.Status == TaskStatus.Queued)
            {
                if (!workerId.IsValid) { reason = "No worker assigned."; return false; }
                if (!hooks.Tasks.AssignTask(task.TaskId, workerId, hooks.Budgets, out reason)) return false;
                task = hooks.Tasks.FindTask(task.TaskId);
            }
            if (task.Status == TaskStatus.Assigned)
            {
                if (!hooks.Tasks.StartTask(task.TaskId, hooks.DayIndex, out reason)) return false;
                task = hooks.Tasks.FindTask(task.TaskId);
            }
            if (task.Status != TaskStatus.InProgress)
            { reason = "Labor task is " + task.Status + ", not InProgress."; return false; }

            ProductionPhaseDefinition phase = hooks.Method.Phases[CurrentStageIndex];
            int stagePlanned = phase != null ? Math.Max(1, phase.ElapsedMinutes) : 1;
            int stageWorked = task.MinutesWorked - LaborBaselineFor(CurrentStageIndex);
            int stageRemaining = Math.Max(0, stagePlanned - stageWorked);
            int work = Math.Max(1, Math.Min(minutes, Math.Max(1, stageRemaining)));
            if (!hooks.Tasks.RecordWorkProgress(task.TaskId, work, hooks.Budgets, hooks.DayIndex,
                out _, out reason))
            {
                return false;
            }
            task = hooks.Tasks.FindTask(task.TaskId);
            Note("stage " + CurrentStageIndex + ": worked " + work + "m on " + task.TaskId
                + " (stage " + Math.Min(stagePlanned, task.MinutesWorked - LaborBaselineFor(CurrentStageIndex))
                + "/" + stagePlanned + "m).");

            if (task.MinutesWorked - LaborBaselineFor(CurrentStageIndex) < stagePlanned)
                return true; // stage work continues

            // Stage work done: complete the task, or hand it to passive waiting.
            ProductionStageDefinition next = CurrentStageIndex + 1 < StagePlan.Stages.Count
                ? StagePlan.Stages[CurrentStageIndex + 1] : null;
            bool carriedEpisode = !string.IsNullOrWhiteSpace(WaitingLaborTaskDefinitionId);
            if (!carriedEpisode && ProductionStagePlan.StageRequiresTendingReturn(next))
            {
                bool ok = handoffDriver.TryEnterPassiveWait(
                    hooks.Tasks, task.TaskId, hooks.Budgets, hooks.DayIndex,
                    DueEventIdFor(DueEventKind.TendingDue), out string handoffReason);
                Note("stage " + CurrentStageIndex + ": passive handoff — " + handoffReason);
                if (!ok) { reason = handoffReason; return false; }
                WaitingLaborTaskDefinitionId = StageTaskDefinitionId(CurrentStageIndex);
                return CompleteStage(CurrentStageIndex, hooks, workerId, out reason);
            }

            if (!hooks.Tasks.CompleteTask(task.TaskId, TaskOutcome.Success, hooks.DayIndex, hooks.Budgets))
            {
                reason = "Labor task could not be completed.";
                return false;
            }
            if (carriedEpisode)
            {
                // Tending/unload episode done: the deadline it was racing is cancelled.
                hooks.DueEvents?.Cancel(DueEventIdFor(DueEventKind.TendingDeadlineMissed));
                DeadlineStageIndex = -1;
                WaitingLaborTaskDefinitionId = string.Empty;
                Note("stage " + CurrentStageIndex + ": tending/unload complete — deadline cancelled.");
            }
            return CompleteStage(CurrentStageIndex, hooks, workerId, out reason);
        }

        /// <summary>
        /// Advances a passive stage by the given minutes of process time — the
        /// "work the order" semantic for stages that need no active labor.
        /// When the stage's duration elapses it completes through the normal
        /// stage path (reservation released, advance/parking as usual). Stages
        /// that need clock-driven tending or deadlines are refused; they must
        /// use the due-event path.
        /// </summary>
        public bool AdvancePassiveByMinutes(StagedProcessHooks hooks, EntityId workerId, int minutes, out string reason)
        {
            reason = string.Empty;
            if (!IsActive) { reason = "Process is not active."; return false; }
            ProductionStageDefinition def = CurrentStageDefinition;
            ProductionStageState state = CurrentStageState;
            if (def == null || state == null) { reason = "No current stage."; return false; }
            if (def.Kind == ProductionStageKind.ActiveLabor)
            { reason = "Current stage is ActiveLabor — use WorkActiveStage."; return false; }
            if (def.Kind == ProductionStageKind.BlockingDependency)
            { reason = "Current stage waits on an external dependency — cannot advance by minutes."; return false; }
            if (def.Kind == ProductionStageKind.PeriodicTending
                || def.TendingGraceGameSeconds > 0f
                || def.MissedConsequence != MissedDeadlineConsequence.None)
            {
                reason = "Current stage needs clock-driven tending/deadlines — cannot advance by minutes.";
                return false;
            }
            state.AdvancePassiveMinutes(Math.Max(0, minutes));
            float durationMinutes = def.DurationGameSeconds / 60f;
            if (state.PassiveMinutesElapsed + 0.001f < durationMinutes)
            {
                Note("stage " + CurrentStageIndex + ": passive +" + minutes + "m ("
                    + state.PassiveMinutesElapsed.ToString("0") + "/"
                    + durationMinutes.ToString("0") + "m elapsed).");
                return true;
            }
            // Duration elapsed: the minutes-driven path replaces the clock-driven
            // due event. Cancel this stage's pending completion event (stable id)
            // so it cannot fire stale after the stage advances.
            if (hooks != null && hooks.DueEvents != null)
                hooks.DueEvents.Cancel(DueEventIdFor(DueEventKind.PassiveProcessComplete));
            Note("stage " + CurrentStageIndex + ": passive duration elapsed — completing.");
            return CompleteStage(CurrentStageIndex, hooks, workerId, out reason);
        }

        /// <summary>
        /// Completes a stage: releases its reservation and advances to the next stage
        /// (or finishes). Stage entry is atomic with the phase-machine advance: if the
        /// next stage refuses entry (e.g. its resource reservation fails), the phase
        /// machine rolls back and the process PARKS after the completed stage
        /// (ParkedReason set) instead of desyncing. Retry with RetryParkedStage.
        /// Due events for the process are left alone — the tending deadline survives
        /// into the tending episode; process end cancels everything.
        /// </summary>
        public bool CompleteStage(int stageIndex, StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            if (!IsActive) { reason = "Process is not active."; return false; }

            ReleaseStageReservation(stageIndex, hooks);
            Note("stage " + stageIndex + " complete.");

            if (stageIndex + 1 >= StagePlan.Stages.Count)
            {
                // Terminal: advance the phase machine to completed.
                ProductionPhaseDefinition last = hooks.Method.Phases[stageIndex];
                PhaseState.Advance(hooks.Method, Math.Max(1, last != null ? last.ElapsedMinutes : 1));
                hooks.DueEvents?.CancelForPayload(DuePayloadRef());
                Note("process " + ProcessId + " complete — output pending its production event.");
                return true;
            }
            return TryAdvanceToStage(stageIndex, hooks, workerId, out reason);
        }

        /// <summary>
        /// Advances the phase machine 1:1 and enters the next stage, atomically. On
        /// entry refusal the phase machine rolls back and the process parks
        /// (ParkedReason). Returns true when the COMPLETION stood (entry deferred);
        /// the park reason is in ParkedReason and the out reason.
        /// </summary>
        private bool TryAdvanceToStage(int completedStageIndex, StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            int next = completedStageIndex + 1;
            // Snapshot the phase machine for rollback.
            int savedIndex = PhaseState.CurrentPhaseIndex;
            int savedRemaining = PhaseState.RemainingPhaseMinutes;
            bool savedCompleted = PhaseState.Completed;

            ProductionPhaseDefinition phase = hooks.Method.Phases[completedStageIndex];
            PhaseState.Advance(hooks.Method, Math.Max(1, phase != null ? phase.ElapsedMinutes : 1));
            if (!EnterStage(next, hooks, workerId, out reason))
            {
                PhaseState.CurrentPhaseIndex = savedIndex;
                PhaseState.RemainingPhaseMinutes = savedRemaining;
                PhaseState.Completed = savedCompleted;
                PhaseState.ApplyCurrentPhaseReservations(hooks.Method);
                // Atomic park: the stage was NOT entered — the process still sits
                // on the completed stage (EnterStage may have advanced the index
                // before refusing, e.g. on a reservation refusal).
                CurrentStageIndex = completedStageIndex;
                ParkedReason = "Stage " + next + " entry refused (" + reason + ") — parked after stage "
                    + completedStageIndex + ".";
                Note(ParkedReason);
                reason = ParkedReason;
                return true;
            }
            ParkedReason = string.Empty;
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Retries a parked stage entry (e.g. after the blocking resource freed up).
        /// No-op when not parked. Returns false only on a hard refusal.
        /// </summary>
        public bool RetryParkedStage(StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(ParkedReason)) return true;
            if (!IsActive) { reason = "Process is not active."; return false; }
            int completedStage = CurrentStageIndex;
            Note("retrying parked stage entry after stage " + completedStage + ".");
            return TryAdvanceToStage(completedStage, hooks, workerId, out reason);
        }

        /// <summary>
        /// Creates the process output exactly once, through the caller's authoritative
        /// callback. Guards on the existing OutputCreated flag — no double output.
        /// </summary>
        public bool TryCreateOutputOnce(StagedProcessHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (!PhaseState.Completed) { reason = "Process is not complete."; return false; }
            if (PhaseState.OutputCreated) { reason = "Output already created — refusing double output."; return false; }
            if (hooks.CreateOutput == null) { reason = "No output callback."; return false; }
            if (!hooks.CreateOutput(this))
            {
                reason = "Output callback refused — output NOT created.";
                return false;
            }
            PhaseState.OutputCreated = true;
            Note("output created (once).");
            return true;
        }

        // ---------- due-event handling ----------

        /// <summary>
        /// Routes one due event belonging to this process. Called by the
        /// ProductionDueEventDriver in due order.
        /// </summary>
        public bool OnDueEvent(DueEvent evt, StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            if (evt == null) { reason = "Null due event."; return false; }
            if (!IsActive)
            {
                Note("due event " + evt.Kind + " ignored — process not active.");
                return true;
            }
            switch (evt.Kind)
            {
                case DueEventKind.PassiveProcessComplete:
                    return OnPassiveComplete(hooks, workerId, out reason);
                case DueEventKind.TendingDue:
                    return OnTendingDue(hooks, workerId, out reason);
                case DueEventKind.TendingDeadlineMissed:
                    return OnDeadlineMissed(hooks, workerId, out reason);
                default:
                    reason = "Unhandled due event kind " + evt.Kind + " for staged process.";
                    return false;
            }
        }

        private bool OnPassiveComplete(StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            ProductionStageState state = CurrentStageState;
            ProductionStageDefinition def = CurrentStageDefinition;
            if (state == null || def == null
                || (def.Kind != ProductionStageKind.PassiveProcess && def.Kind != ProductionStageKind.PeriodicTending))
            {
                reason = "PassiveProcessComplete fired but current stage is not passive.";
                return false;
            }
            Note("stage " + CurrentStageIndex + ": passive process complete.");
            // The tending deadline (if any) is deliberately NOT cancelled here: it guards
            // the tending/unload episode that follows. It is cancelled when the tending
            // completes, or fires and applies its consequence.
            return CompleteStage(CurrentStageIndex, hooks, workerId, out reason);
        }

        private bool OnTendingDue(StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            ProductionStageState state = CurrentStageState;
            ProductionStageDefinition def = CurrentStageDefinition;
            if (state == null || def == null || def.Kind != ProductionStageKind.PeriodicTending)
            {
                reason = "TendingDue fired but current stage is not PeriodicTending.";
                return false;
            }

            // A person must return: resume the waiting labor task for the check-in.
            if (!string.IsNullOrWhiteSpace(WaitingLaborTaskDefinitionId))
            {
                WorkTask task = ResolveTaskByDefinition(WaitingLaborTaskDefinitionId, hooks);
                if (task == null)
                {
                    reason = "TendingDue: waiting labor task disappeared — refusing loudly.";
                    return false;
                }
                EntityId tender = workerId.IsValid ? workerId : hooks.TendingWorkerId;
                bool ok = handoffDriver.TryResumeForTending(
                    hooks.Tasks, task.TaskId, tender, hooks.Budgets, hooks.DayIndex,
                    out string resumeReason);
                Note("stage " + CurrentStageIndex + ": tending resume — " + resumeReason);
                if (ok)
                {
                    int minutes = Math.Max(1, hooks.TendingWorkMinutes);
                    hooks.Tasks.RecordWorkProgress(task.TaskId, minutes, hooks.Budgets, hooks.DayIndex,
                        out _, out string workReason);
                    if (!string.IsNullOrEmpty(workReason)) Note("tending work note: " + workReason);
                    state.MarkTendingDone();
                    // Back to waiting for the rest of the passive stretch.
                    handoffDriver.TryEnterPassiveWait(
                        hooks.Tasks, task.TaskId, hooks.Budgets, hooks.DayIndex,
                        DueEventIdFor(DueEventKind.TendingDue), out string waitReason);
                    Note("stage " + CurrentStageIndex + ": tending check-in done (" + minutes + "m) — " + waitReason);
                }
                else
                {
                    reason = resumeReason;
                    Note("stage " + CurrentStageIndex + ": tending MISSED — " + resumeReason);
                }
            }
            else
            {
                state.MarkTendingDone();
                Note("stage " + CurrentStageIndex + ": tending check (no labor task) done.");
            }

            // Schedule the next check-in while the passive stretch continues.
            float now = hooks.Now();
            float next = now + def.TendingIntervalGameSeconds;
            if (def.TendingIntervalGameSeconds > 0f && next < state.DueGameSeconds - 0.001f && hooks.DueEvents != null)
            {
                hooks.DueEvents.Schedule(DueEventKind.TendingDue, next, DuePayloadRef(), out _);
            }
            return true;
        }

        private bool OnDeadlineMissed(StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            if (DeadlineStageIndex < 0 || DeadlineStageIndex >= StagePlan.Stages.Count)
            {
                Note("deadline event: no pending tending deadline — stale, no-op.");
                return true;
            }
            ProductionStageDefinition def = StagePlan.Stages[DeadlineStageIndex];
            ProductionStageState state = StageStates[DeadlineStageIndex];
            // Tending already done (or consequence already applied): stale, no-op.
            if (state.TendingDone || state.ConsequenceApplied)
            {
                Note("deadline event: tending already done or consequence applied — no-op.");
                DeadlineStageIndex = -1;
                return true;
            }
            // The tending episode finished without the deadline being cancelled
            // (e.g. carried task completed): stale, no-op.
            if (string.IsNullOrWhiteSpace(WaitingLaborTaskDefinitionId))
            {
                WorkTask carried = ResolveTaskByDefinition(WaitingLaborTaskDefinitionId, hooks);
                if (carried == null)
                {
                    Note("deadline event: waiting task gone and episode over — stale, no-op.");
                    DeadlineStageIndex = -1;
                    return true;
                }
            }
            else
            {
                WorkTask carried = ResolveTaskByDefinition(WaitingLaborTaskDefinitionId, hooks);
                if (carried != null && (carried.Status == TaskStatus.Complete || carried.Status == TaskStatus.Cancelled))
                {
                    Note("deadline event: tending task already " + carried.Status + " — stale, no-op.");
                    DeadlineStageIndex = -1;
                    return true;
                }
            }

            Note("stage " + DeadlineStageIndex + ": tending DEADLINE missed (" + def.MissedConsequence + ").");
            state.MarkConsequenceApplied();
            DeadlineStageIndex = -1;

            if (hooks.ApplyConsequence != null)
            {
                var diag = new List<string>();
                bool handled = hooks.ApplyConsequence(this, state, def.MissedConsequence, diag);
                foreach (string d in diag) Note(d);
                if (!handled) { reason = "Consequence hook refused."; return false; }
                return true;
            }
            return ApplyDefaultConsequence(def.MissedConsequence, state, hooks, workerId, out reason);
        }

        /// <summary>
        /// Default method-specific consequence handling. Slices override via
        /// hooks.ApplyConsequence for domain-specific effects.
        /// </summary>
        public bool ApplyDefaultConsequence(
            MissedDeadlineConsequence consequence, ProductionStageState state,
            StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            switch (consequence)
            {
                case MissedDeadlineConsequence.None:
                    return true;
                case MissedDeadlineConsequence.QualityDegraded:
                    QualityPenalty01 = Math.Min(1f, QualityPenalty01 + 0.25f);
                    Note("consequence: quality degraded (penalty now " + QualityPenalty01.ToString("0.00") + ").");
                    return true;
                case MissedDeadlineConsequence.PartialWaste:
                    {
                        float fraction = CurrentStageDefinition != null
                            ? CurrentStageDefinition.WasteFraction01 : 0.25f;
                        WasteRecords.Add(new StageWasteRecord
                        {
                            StageIndex = state.StageIndex,
                            WasteFraction01 = fraction,
                            Note = "missed tending deadline at stage " + state.StageIndex,
                        });
                        Note("consequence: partial waste (" + fraction.ToString("0%") + " of batch).");
                        return true;
                    }
                case MissedDeadlineConsequence.BatchFailed:
                    return FailBatch("missed tending deadline at stage " + state.StageIndex, hooks, out reason);
                case MissedDeadlineConsequence.EquipmentMaintenanceRequired:
                    {
                        string equipment = CurrentStageDefinition != null
                            && CurrentStageDefinition.StageReservationKeys != null
                            && CurrentStageDefinition.StageReservationKeys.Count > 0
                            ? CurrentStageDefinition.StageReservationKeys[0]
                            : "unknown-equipment";
                        hooks.MarkEquipmentNeedsMaintenance?.Invoke(equipment);
                        Note("consequence: equipment maintenance required (" + equipment + ").");
                        // Release the stage reservation so the equipment can be serviced.
                        ReleaseStageReservation(state.StageIndex, hooks);
                        return true;
                    }
                default:
                    reason = "Unknown consequence " + consequence + ".";
                    return false;
            }
        }

        /// <summary>
        /// Fails the batch: cancels due events, releases reservations, fails the
        /// waiting labor task where the public API allows, and opens a corrective
        /// rework task (Failed -> ReworkRequired linkage via follow-ups).
        /// </summary>
        public bool FailBatch(string why, StagedProcessHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (Failed) { reason = "Already failed."; return false; }
            Failed = true;
            FailedReason = why ?? string.Empty;
            DeadlineStageIndex = -1;
            hooks.DueEvents?.CancelForPayload(DuePayloadRef());
            for (int i = 0; i < StageStates.Count; i++) ReleaseStageReservation(i, hooks);
            Note("BATCH FAILED: " + FailedReason);

            // Fail the waiting labor task. The real WaitingOnPassiveProcess -> Failed
            // transition belongs to Worker-1; the emulated path's task is Queued and
            // fails through the public API here.
            if (!string.IsNullOrWhiteSpace(WaitingLaborTaskDefinitionId) && hooks.Tasks != null)
            {
                WorkTask task = ResolveTaskByDefinition(WaitingLaborTaskDefinitionId, hooks);
                if (task != null && task.Status == TaskStatus.WaitingOnPassiveProcess)
                {
                    Note("waiting labor task " + task.TaskId + " is WaitingOnPassiveProcess — "
                        + "its Failed transition belongs to Worker-1's API; left open loudly.");
                }
                else if (task != null && task.Status == TaskStatus.Queued)
                {
                    hooks.Tasks.CompleteTask(task.TaskId, TaskOutcome.SeriousFailure, hooks.DayIndex, hooks.Budgets);
                    Note("emulated waiting task " + task.TaskId + " failed (was Queued).");
                }
                WaitingLaborTaskDefinitionId = string.Empty;
            }

            // A corrective rework task becomes necessary — a real WorkTask, queued.
            if (hooks.Tasks != null)
            {
                string defId = "rework:stageproc:" + ProcessId;
                if (hooks.Tasks.GetDefinition(defId) == null)
                {
                    var def = new TaskDefinition(defId, "Rework failed batch " + ProcessId, 60);
                    hooks.Tasks.RegisterDefinition(def, out _);
                }
                WorkTask rework = hooks.Tasks.CreateTask(defId, hooks.BusinessId, hooks.DayIndex);
                Note("rework task " + rework.TaskId + " queued (" + FailedReason + ").");
            }
            return true;
        }

        /// <summary>
        /// Notifies a BlockingDependency stage that its external dependency resolved.
        /// Completes the stage and advances. Refuses loudly when the current stage is
        /// not a blocking dependency.
        /// </summary>
        public bool NotifyDependencyResolved(StagedProcessHooks hooks, EntityId workerId, out string reason)
        {
            reason = string.Empty;
            ProductionStageDefinition def = CurrentStageDefinition;
            if (def == null || def.Kind != ProductionStageKind.BlockingDependency)
            {
                reason = "Current stage is not a BlockingDependency.";
                return false;
            }
            Note("stage " + CurrentStageIndex + ": blocking dependency resolved.");
            CurrentStageState.MarkTendingDone();
            return CompleteStage(CurrentStageIndex, hooks, workerId, out reason);
        }

        /// <summary>
        /// Cancels the process: due events cancelled, reservations released, open labor
        /// task cancelled. Consumed inputs stay consumed. Never produces output.
        /// </summary>
        public bool Cancel(StagedProcessHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (Cancelled) { reason = "Already cancelled."; return true; }
            if (IsComplete) { reason = "Process already complete — cannot cancel."; return false; }
            Cancelled = true;
            DeadlineStageIndex = -1;
            hooks.DueEvents?.CancelForPayload(DuePayloadRef());
            for (int i = 0; i < StageStates.Count; i++) ReleaseStageReservation(i, hooks);
            if (!string.IsNullOrWhiteSpace(WaitingLaborTaskDefinitionId) && hooks.Tasks != null)
            {
                WorkTask task = ResolveTaskByDefinition(WaitingLaborTaskDefinitionId, hooks);
                if (task != null && (task.Status == TaskStatus.Queued || task.Status == TaskStatus.Assigned
                    || task.Status == TaskStatus.InProgress))
                    hooks.Tasks.CancelTask(task.TaskId, hooks.Budgets);
                WaitingLaborTaskDefinitionId = string.Empty;
            }
            ProductionStageState state = CurrentStageState;
            if (state != null && hooks.Tasks != null)
            {
                WorkTask task = ResolveTaskByDefinition(StageTaskDefinitionId(CurrentStageIndex), hooks);
                if (task != null && (task.Status == TaskStatus.Queued || task.Status == TaskStatus.Assigned
                    || task.Status == TaskStatus.InProgress))
                    hooks.Tasks.CancelTask(task.TaskId, hooks.Budgets);
            }
            Note("process cancelled — consumed inputs stay consumed, reservations released, no output.");
            return true;
        }

        private void ReleaseStageReservation(int stageIndex, StagedProcessHooks hooks)
        {
            if (stageIndex < 0 || stageIndex >= StageStates.Count) return;
            ProductionStageState state = StageStates[stageIndex];
            if (!string.IsNullOrWhiteSpace(state.ReservationId) && hooks.Reservations != null)
            {
                hooks.Reservations.Release(state.ReservationId);
                Note("stage " + stageIndex + ": reservation " + state.ReservationId + " released.");
            }
            state.SetReservation(string.Empty);
        }

        // ---------- save/load (owning runtime: DTO methods live here) ----------

        [Serializable]
        public sealed class StagedProductionProcessSaveDto
        {
            public string ProcessId = string.Empty;
            public ProductionProcessState PhaseState = new ProductionProcessState();
            public List<ProductionStageDefinition> StageDefinitions = new List<ProductionStageDefinition>();
            public List<ProductionStageState> StageStates = new List<ProductionStageState>();
            public int CurrentStageIndex;
            public bool Cancelled;
            public bool Failed;
            public string FailedReason = string.Empty;
            public float QualityPenalty01;
            public List<StageWasteRecord> WasteRecords = new List<StageWasteRecord>();
            public List<StageLaborBaseline> LaborBaselines = new List<StageLaborBaseline>();
            public string WaitingLaborTaskDefinitionId = string.Empty;
            public int DeadlineStageIndex = -1;
            public string ParkedReason = string.Empty;
            public List<string> EventLog = new List<string>();
        }

        /// <summary>
        /// Captures the save DTO. Due events and live reservations are NOT in the DTO:
        /// on load the caller re-registers the process and calls ReacquireAfterLoad,
        /// which re-schedules pending events with STABLE ids (replace, never duplicate)
        /// and re-acquires stage reservations.
        /// </summary>
        public StagedProductionProcessSaveDto CaptureSaveDto()
        {
            return new StagedProductionProcessSaveDto
            {
                ProcessId = ProcessId ?? string.Empty,
                PhaseState = PhaseState,
                StageDefinitions = StagePlan != null
                    ? new List<ProductionStageDefinition>(StagePlan.Stages)
                    : new List<ProductionStageDefinition>(),
                StageStates = new List<ProductionStageState>(StageStates),
                CurrentStageIndex = CurrentStageIndex,
                Cancelled = Cancelled,
                Failed = Failed,
                FailedReason = FailedReason ?? string.Empty,
                QualityPenalty01 = QualityPenalty01,
                WasteRecords = new List<StageWasteRecord>(WasteRecords),
                LaborBaselines = new List<StageLaborBaseline>(LaborBaselines),
                WaitingLaborTaskDefinitionId = WaitingLaborTaskDefinitionId ?? string.Empty,
                DeadlineStageIndex = DeadlineStageIndex,
                ParkedReason = ParkedReason ?? string.Empty,
                EventLog = new List<string>(EventLog),
            };
        }

        public void LoadFromSaveDto(StagedProductionProcessSaveDto dto)
        {
            StageStates.Clear();
            WasteRecords.Clear();
            LaborBaselines.Clear();
            EventLog.Clear();
            boundTaskIds.Clear();
            if (dto == null) return;
            ProcessId = dto.ProcessId ?? string.Empty;
            PhaseState = dto.PhaseState ?? new ProductionProcessState();
            StagePlan = new ProductionStagePlan();
            if (dto.StageDefinitions != null) StagePlan.Stages.AddRange(dto.StageDefinitions);
            if (dto.StageStates != null) StageStates.AddRange(dto.StageStates);
            CurrentStageIndex = dto.CurrentStageIndex;
            Cancelled = dto.Cancelled;
            Failed = dto.Failed;
            FailedReason = dto.FailedReason ?? string.Empty;
            QualityPenalty01 = dto.QualityPenalty01;
            if (dto.WasteRecords != null) WasteRecords.AddRange(dto.WasteRecords);
            if (dto.LaborBaselines != null) LaborBaselines.AddRange(dto.LaborBaselines);
            WaitingLaborTaskDefinitionId = dto.WaitingLaborTaskDefinitionId ?? string.Empty;
            DeadlineStageIndex = dto.DeadlineStageIndex;
            ParkedReason = dto.ParkedReason ?? string.Empty;
            if (dto.EventLog != null) EventLog.AddRange(dto.EventLog);
            Note("process restored from save (stage " + CurrentStageIndex + ").");
        }

        /// <summary>
        /// Re-registers a loaded process: re-schedules its pending due events with
        /// stable ids (replace, never duplicate), re-acquires the live stage
        /// reservation, and rebinds labor tasks read-only (never a second task).
        /// Called once after LoadFromSaveDto.
        /// </summary>
        public bool ReacquireAfterLoad(StagedProcessHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (!IsActive)
            {
                Note("reacquire: process not active — nothing to re-schedule.");
                return true;
            }
            ProductionStageState state = CurrentStageState;
            ProductionStageDefinition def = CurrentStageDefinition;
            if (state == null || def == null) { reason = "No current stage."; return false; }

            if (def.StageReservationKeys != null && def.StageReservationKeys.Count > 0
                && hooks.Reservations != null && string.IsNullOrWhiteSpace(state.ReservationId))
            {
                var request = new ReservationRequest(
                    TaskIdentityConventions.ReservationOwnerKey("stageproc:" + ProcessId),
                    "reacquire stage " + CurrentStageIndex + " of " + ProcessId);
                float now = hooks.Now();
                foreach (string key in def.StageReservationKeys)
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    request.Items.Add(new ReservationItem(key, 1f, now,
                        Math.Max(now + 1f, state.DueGameSeconds + def.TendingGraceGameSeconds)));
                }
                string refusal = hooks.Reservations.TryReserveAll(request, out ReservationHandle handle);
                if (refusal != null)
                {
                    reason = "Reacquire refused: " + refusal;
                    return false;
                }
                state.SetReservation(handle != null ? handle.ReservationId : string.Empty);
                Note("reacquire: stage reservation " + state.ReservationId + " re-acquired.");
            }

            if (hooks.DueEvents != null && (def.Kind == ProductionStageKind.PassiveProcess
                || def.Kind == ProductionStageKind.PeriodicTending))
            {
                string payload = DuePayloadRef();
                if (IsFuture(state.DueGameSeconds, hooks))
                    hooks.DueEvents.Schedule(DueEventKind.PassiveProcessComplete, state.DueGameSeconds, payload, out _);
                if (def.Kind == ProductionStageKind.PeriodicTending && def.TendingIntervalGameSeconds > 0f
                    && !state.TendingDone && IsFuture(state.StartGameSeconds + def.TendingIntervalGameSeconds, hooks))
                    hooks.DueEvents.Schedule(DueEventKind.TendingDue,
                        state.StartGameSeconds + def.TendingIntervalGameSeconds, payload, out _);
                if (DeadlineStageIndex == CurrentStageIndex
                    && def.TendingGraceGameSeconds > 0f
                    && def.MissedConsequence != MissedDeadlineConsequence.None
                    && IsFuture(state.DeadlineGameSeconds, hooks))
                    hooks.DueEvents.Schedule(DueEventKind.TendingDeadlineMissed, state.DeadlineGameSeconds, payload, out _);
                Note("reacquire: due events re-scheduled with stable ids (replace, never duplicate).");
            }

            // Rebind labor tasks read-only — never a second task for the same definition.
            WorkTask current = ResolveTaskByDefinition(StageTaskDefinitionId(CurrentStageIndex), hooks);
            Note("reacquire: current labor task "
                + (current != null ? current.TaskId + " rebound (" + current.Status + ")." : "none — noted."));
            if (!string.IsNullOrWhiteSpace(WaitingLaborTaskDefinitionId))
            {
                WorkTask waiting = ResolveTaskByDefinition(WaitingLaborTaskDefinitionId, hooks);
                Note("reacquire: waiting labor task "
                    + (waiting != null ? waiting.TaskId + " rebound (" + waiting.Status + ")." : "NOT FOUND — noted loudly."));
            }
            return true;
        }

        private static bool IsFuture(float dueGameSeconds, StagedProcessHooks hooks)
        {
            return dueGameSeconds > hooks.Now() + 0.001f;
        }
    }

    /// <summary>
    /// Pass IV (Worker 3): dispatches already-popped due events to registered staged
    /// processes. The global driver loop (Worker 1) owns popping; this class scopes the
    /// staged-production subset by payloadRef. Register live processes; unregister on
    /// completion, failure, or cancellation.
    /// </summary>
    public sealed class ProductionDueEventDriver
    {
        private sealed class Registration
        {
            public StagedProductionProcess Process;
            public StagedProcessHooks Hooks;
            public EntityId WorkerId;
        }

        private readonly Dictionary<string, Registration> registrations =
            new Dictionary<string, Registration>(StringComparer.Ordinal);

        public int RegisteredCount => registrations.Count;

        public void Register(StagedProductionProcess process, StagedProcessHooks hooks, EntityId workerId)
        {
            if (process == null || string.IsNullOrWhiteSpace(process.ProcessId)) return;
            registrations[process.DuePayloadRef()] = new Registration
            {
                Process = process,
                Hooks = hooks,
                WorkerId = workerId,
            };
        }

        public void Unregister(StagedProductionProcess process)
        {
            if (process == null) return;
            registrations.Remove(process.DuePayloadRef());
        }

        /// <summary>
        /// Dispatches popped due events to staged processes in the given order (the
        /// caller pops in due order). Events for unknown payloadRefs are reported
        /// loudly and skipped — they belong to other drivers.
        /// </summary>
        public int Dispatch(IEnumerable<DueEvent> poppedEvents, List<string> diagnostics)
        {
            if (poppedEvents == null) return 0;
            int handled = 0;
            foreach (DueEvent evt in poppedEvents)
            {
                if (evt == null) continue;
                if (!registrations.TryGetValue(evt.PayloadRef ?? string.Empty, out Registration reg)
                    || reg.Process == null)
                {
                    diagnostics?.Add("ProductionDueEventDriver: no staged process for event "
                        + evt.EventId + " (" + evt.Kind + ") — skipped loudly.");
                    continue;
                }
                if (!reg.Process.OnDueEvent(evt, reg.Hooks, reg.WorkerId, out string reason))
                {
                    diagnostics?.Add("ProductionDueEventDriver: event " + evt.EventId + " failed — " + reason);
                    continue;
                }
                handled++;
                if (!reg.Process.IsActive) registrations.Remove(evt.PayloadRef);
            }
            return handled;
        }
    }
}
