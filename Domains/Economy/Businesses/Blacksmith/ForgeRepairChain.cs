// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Pass IV (Worker 3) — Slice D: forge repair chain.
//
// ForgeRepairChain runs the 6-stage repair (receive -> inspect -> prepare ->
// repair -> cool/finish -> return) on the ProductionStageMachine. Materials
// (iron) and forge fuel (coal) are dispensed exactly once at begin. The repair
// stage's duration and outcome come from the CausalTaskOutcomeResolver (skill
// effects: capability, difficulty, equipment condition, fatigue) — a failed
// repair queues a real rework WorkTask instead of advancing. The forge is an
// exclusive reservation; competing nail-production work holds it, and a
// repair whose forge is busy parks (no phantom forge).

using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Skills;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Generic;

namespace LandLedgers.Economy.Businesses.Blacksmith
{
    /// <summary>One repair order moving through the 6-stage chain.</summary>
    [Serializable]
    public sealed class ForgeRepairOrder
    {
        public string OrderId = string.Empty;
        public string AssetId = string.Empty;
        public string AssetDescription = string.Empty;
        public EntityId SmithId = EntityId.Invalid;
        public double SmithCapability01;
        public StagedProductionProcess Process;
        public ProductionMethodDefinition Method;
        public StagedProcessHooks Hooks;
        public float IronUnitsDispensed;
        public float CoalUnitsDispensed;
        // Pre-resolved causal outcome for the repair stage (deterministic).
        public double ResolvedRepairMinutes;
        public double ResolvedQualityDelta;
        public double ResolvedWasteFactor;
        public double ResolvedFailureRisk01;
        public TaskOutcome ResolvedOutcome = TaskOutcome.Success;
        public EntityId ReworkTaskId = EntityId.Invalid;
        public bool ReworkNeeded;
        public bool IsComplete;
        public string FailureNote = string.Empty;
    }

    /// <summary>A nail-production run competing for the same forge.</summary>
    [Serializable]
    public sealed class NailProductionRun
    {
        public string RunId = string.Empty;
        public EntityId TaskId = EntityId.Invalid;
        public string ForgeReservationId = string.Empty;
        public int NailsTarget;
        public int NailsMade;
        public bool IsComplete;
    }

    public sealed class ForgeChainContext
    {
        public TaskAuthority Tasks;
        public WorkTimeBudgetStore Budgets;
        public IDueEventScheduler DueEvents;
        public IUnifiedReservationAuthority Reservations;
        public Func<float> ClockGameSeconds;
        public int DayIndex;
        public EntityId BusinessId;
        public string ForgeId = "forge-1";
        public List<string> Diagnostics;
        public float Now() => ClockGameSeconds != null ? ClockGameSeconds() : 0f;
        public void Log(string message)
        {
            if (Diagnostics != null) Diagnostics.Add("ForgeRepairChain: " + message);
        }
    }

    /// <summary>
    /// The 6-stage forge repair chain. Materials + fuel dispensed once at begin;
    /// the repair stage honors the causal skill outcome; failures queue rework.
    /// </summary>
    public sealed class ForgeRepairChain
    {
        public const int StageReceive = 0;
        public const int StageInspect = 1;
        public const int StagePrepare = 2;
        public const int StageRepair = 3;
        public const int StageCool = 4;
        public const int StageReturn = 5;

        // Forge fuel and materials on hand (real stocks, dispensed at begin).
        public float CoalUnitsOnHand = 100f;
        public float IronUnitsOnHand = 100f;
        public float CoalPerRepair = 5f;
        public float IronPerRepair = 2f;

        private readonly List<ForgeRepairOrder> orders = new List<ForgeRepairOrder>();
        private readonly List<NailProductionRun> nailRuns = new List<NailProductionRun>();
        private readonly CausalTaskOutcomeResolver outcomeResolver = new CausalTaskOutcomeResolver();
        private readonly ProductionDueEventDriver dueDriver = new ProductionDueEventDriver();
        private int sequence;

        public IReadOnlyList<ForgeRepairOrder> Orders => orders;
        public IReadOnlyList<NailProductionRun> NailRuns => nailRuns;

        private string ForgeReservationKey(ForgeChainContext ctx) => "forge:" + (ctx != null ? ctx.ForgeId : "forge-1");
        private EntityId BusinessEntityId(ForgeChainContext ctx) =>
            ctx != null && ctx.BusinessId.IsValid ? ctx.BusinessId : EntityId.For(EntityKind.Business, 2);

        /// <summary>
        /// Begins a repair: dispenses iron + coal exactly once, builds the 6-stage
        /// method/plan, and pre-resolves the causal repair outcome (deterministic
        /// from smith capability). Refuses loudly when fuel/materials are short.
        /// </summary>
        public ForgeRepairOrder BeginRepair(EquipmentAsset asset, EntityId smithId, double capability01,
            ForgeChainContext ctx, out string reason)
        {
            reason = string.Empty;
            if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId))
            {
                reason = "ForgeRepairChain: repair needs a real asset — refusing phantom repair.";
                return null;
            }
            if (smithId.Kind != EntityKind.Person || !smithId.IsValid)
            {
                reason = "ForgeRepairChain: repair needs a real smith — refusing.";
                return null;
            }
            if (CoalUnitsOnHand < CoalPerRepair - 0.001f)
            {
                reason = "ForgeRepairChain: forge has no coal (" + CoalUnitsOnHand + " < " + CoalPerRepair + ") — repair refused, stoke the forge first.";
                return null;
            }
            if (IronUnitsOnHand < IronPerRepair - 0.001f)
            {
                reason = "ForgeRepairChain: no iron for the repair (" + IronUnitsOnHand + " < " + IronPerRepair + ") — repair refused.";
                return null;
            }

            var order = new ForgeRepairOrder
            {
                OrderId = "repair-" + asset.AssetId + "-" + (sequence++),
                AssetId = asset.AssetId,
                AssetDescription = asset.DisplayName,
                SmithId = smithId,
                SmithCapability01 = Mathf.Clamp01((float)capability01),
            };

            // Exactly-once input consumption: coal + iron into the order's custody.
            CoalUnitsOnHand -= CoalPerRepair;
            IronUnitsOnHand -= IronPerRepair;
            order.CoalUnitsDispensed = CoalPerRepair;
            order.IronUnitsDispensed = IronPerRepair;

            // Causal skill resolution for the repair stage (deterministic).
            // Difficulty 0.6: a real repair is skilled work, not a hard gate.
            var inputs = new CausalOutcomeInputs
            {
                BaseMinutes = 60.0,
                Capability01 = order.SmithCapability01,
                ExperienceRelevance01 = 0.7,
                Difficulty01 = 0.6,
                EquipmentCondition01 = 0.85, // forge condition
                Fatigue01 = 0.1,
            };
            CausalOutcomeResult resolved = outcomeResolver.Resolve(inputs);
            order.ResolvedRepairMinutes = Math.Max(1.0, resolved.DurationMinutes);
            order.ResolvedQualityDelta = resolved.QualityDelta;
            order.ResolvedWasteFactor = resolved.WasteFactor;
            order.ResolvedFailureRisk01 = resolved.FailureRisk01;
            string taskIdSeed = "generic-production:forge-" + order.OrderId + ":phase:3:definition";
            order.ResolvedOutcome = TaskOutcomeDetermination.DetermineOutcome(
                resolved, taskIdSeed, smithId.ToString(), ctx != null ? ctx.DayIndex : 0);

            ProductionMethodDefinition method = BuildSixStepMethod(order);
            ProductionStagePlan plan = BuildSixStepPlan(ctx);

            var process = new StagedProductionProcess
            {
                ProcessId = "forge-" + order.OrderId,
                StagePlan = plan,
            };
            order.Method = method;
            order.Process = process;
            order.Hooks = BuildHooks(order, ctx);

            if (!process.Begin(order.Hooks, smithId, out reason))
            {
                // Begin failed: return the dispensed inputs.
                CoalUnitsOnHand += CoalPerRepair;
                IronUnitsOnHand += IronPerRepair;
                reason = "ForgeRepairChain: repair begin failed — " + reason;
                return null;
            }

            orders.Add(order);
            dueDriver.Register(process, order.Hooks, smithId);
            ctx.Log("order " + order.OrderId + " began for '" + asset.AssetId + "' (" + asset.DisplayName
                + "): smith " + smithId + " capability " + order.SmithCapability01.ToString("0.00")
                + ", repair " + order.ResolvedRepairMinutes.ToString("0") + " min, failure risk "
                + order.ResolvedFailureRisk01.ToString("0.00") + ", outcome " + order.ResolvedOutcome
                + ". Inputs dispensed once (" + CoalPerRepair + " coal, " + IronPerRepair + " iron).");
            return order;
        }

        /// <summary>
        /// Works an order: stages 0-2, 4-5 via the stage machine; the repair
        /// stage (3) is driven manually so the causal outcome (incl. failure ->
        /// rework) is honored instead of a hardcoded Success.
        /// </summary>
        public bool WorkOrder(ForgeRepairOrder order, int minutes, ForgeChainContext ctx, out string reason)
        {
            reason = string.Empty;
            if (order == null || order.Process == null) { reason = "No order."; return false; }
            if (order.IsComplete) { reason = "Order " + order.OrderId + " is complete."; return false; }
            if (!order.Process.IsActive) { reason = "Order " + order.OrderId + " is not active."; return false; }

            // Rework in flight: work it; on completion the repair stage advances.
            if (order.ReworkTaskId.IsValid)
                return WorkRework(order, minutes, ctx, out reason);

            if (order.Process.CurrentStageIndex == StageRepair)
                return WorkRepairStage(order, minutes, ctx, out reason);

            if (!order.Process.WorkActiveStage(order.Hooks, order.SmithId, minutes, out reason))
                return false;
            // After non-repair stages, check for process completion (return stage done).
            if (order.Process.IsComplete && !order.IsComplete)
            {
                order.IsComplete = true;
                ctx.Log("order " + order.OrderId + " complete — '" + order.AssetId + "' repaired"
                    + " (quality " + order.ResolvedQualityDelta.ToString("+0.00;-0.00") + ") and returned.");
            }
            return true;
        }

        /// <summary>Pumps due events for all orders (cooling completion, etc.).</summary>
        public int PumpDueEvents(ForgeChainContext ctx)
        {
            if (ctx == null || ctx.DueEvents == null) return 0;
            List<DueEvent> popped = ctx.DueEvents.PopDue(ctx.Now());
            int handled = dueDriver.Dispatch(popped, ctx.Diagnostics);
            foreach (ForgeRepairOrder order in orders)
            {
                if (order == null || order.Process == null || order.IsComplete) continue;
                if (order.Process.IsComplete)
                {
                    order.IsComplete = true;
                    ctx.Log("order " + order.OrderId + " complete — '" + order.AssetId + "' repaired and returned.");
                }
            }
            return handled;
        }

        /// <summary>Retries parked stage entries (e.g. after the forge freed up).</summary>
        public void RetryParked(ForgeChainContext ctx)
        {
            foreach (ForgeRepairOrder order in orders)
            {
                if (order == null || order.Process == null || order.IsComplete) continue;
                if (string.IsNullOrWhiteSpace(order.Process.ParkedReason)) continue;
                order.Process.RetryParkedStage(order.Hooks, order.SmithId, out string reason);
                if (!string.IsNullOrWhiteSpace(reason)) ctx.Log("ForgeRepairChain: " + reason);
            }
        }

        /// <summary>
        /// Starts a competing nail-production run that holds the forge exclusively.
        /// Real work: a WorkTask, worked through TaskAuthority.
        /// </summary>
        public NailProductionRun StartNailProduction(int nailsTarget, EntityId smithId, ForgeChainContext ctx, out string reason)
        {
            reason = string.Empty;
            var run = new NailProductionRun
            {
                RunId = "nails-" + (sequence++),
                NailsTarget = Math.Max(1, nailsTarget),
            };
            // Exclusive forge hold for the run's duration.
            if (ctx.Reservations != null)
            {
                var request = new ReservationRequest("task:forge:" + run.RunId, "nail production " + run.RunId);
                float now = ctx.Now();
                request.Items.Add(new ReservationItem(ForgeReservationKey(ctx), 1f, now, now + 3600f));
                string refusal = ctx.Reservations.TryReserveAll(request, out ReservationHandle handle);
                if (refusal != null)
                {
                    reason = "ForgeRepairChain: forge busy — nail run refused: " + refusal;
                    return null;
                }
                run.ForgeReservationId = handle != null ? handle.ReservationId : string.Empty;
            }
            string defId = "forge:nails:" + run.RunId;
            if (ctx.Tasks.GetDefinition(defId) == null)
            {
                ctx.Tasks.RegisterDefinition(new TaskDefinition(defId, "Forge nails x" + run.NailsTarget, 120), out _);
            }
            WorkTask task = ctx.Tasks.CreateTask(defId, BusinessEntityId(ctx), ctx.DayIndex);
            if (!ctx.Tasks.AssignTask(task.TaskId, smithId, ctx.Budgets, out reason)
                || !ctx.Tasks.StartTask(task.TaskId, ctx.DayIndex, out reason))
            {
                if (ctx.Reservations != null && !string.IsNullOrWhiteSpace(run.ForgeReservationId))
                    ctx.Reservations.Release(run.ForgeReservationId);
                reason = "ForgeRepairChain: nail run could not start: " + reason;
                return null;
            }
            run.TaskId = task.TaskId;
            nailRuns.Add(run);
            ctx.Log("nail run " + run.RunId + " started — forge held exclusively.");
            return run;
        }

        public bool WorkNailRun(NailProductionRun run, int minutes, ForgeChainContext ctx, out string reason)
        {
            reason = string.Empty;
            if (run == null || run.IsComplete) { reason = "No run."; return false; }
            if (!ctx.Tasks.RecordWorkProgress(run.TaskId, minutes, ctx.Budgets, ctx.DayIndex,
                out bool done, out reason)) return false;
            if (!done) return true;
            ctx.Tasks.CompleteTask(run.TaskId, TaskOutcome.Success, ctx.DayIndex, ctx.Budgets);
            run.NailsMade = run.NailsTarget;
            run.IsComplete = true;
            if (ctx.Reservations != null && !string.IsNullOrWhiteSpace(run.ForgeReservationId))
                ctx.Reservations.Release(run.ForgeReservationId);
            ctx.Log("nail run " + run.RunId + " complete — " + run.NailsMade + " nails, forge released.");
            return true;
        }

        // ---------- internals ----------

        /// <summary>
        /// Drives the repair stage manually: works the task, then honors the
        /// pre-resolved causal outcome. Failure queues a rework task instead of
        /// advancing; success advances to cooling.
        /// </summary>
        private bool WorkRepairStage(ForgeRepairOrder order, int minutes, ForgeChainContext ctx, out string reason)
        {
            reason = string.Empty;
            StagedProductionProcess process = order.Process;
            StagedProcessHooks hooks = order.Hooks;
            string defId = process.StageTaskDefinitionId(StageRepair);
            WorkTask task = hooks.Tasks.FindOpenTask(defId, BusinessEntityId(ctx), order.SmithId);
            if (task == null)
            {
                reason = "ForgeRepairChain: repair stage has no labor task (" + defId + ") — refusing loudly.";
                return false;
            }
            if (task.Status == TaskStatus.Queued)
            {
                if (!hooks.Tasks.AssignTask(task.TaskId, order.SmithId, hooks.Budgets, out reason)) return false;
                task = hooks.Tasks.FindTask(task.TaskId);
            }
            if (task.Status == TaskStatus.Assigned)
            {
                if (!hooks.Tasks.StartTask(task.TaskId, hooks.DayIndex, out reason)) return false;
                task = hooks.Tasks.FindTask(task.TaskId);
            }
            if (task.Status != TaskStatus.InProgress)
            {
                reason = "ForgeRepairChain: repair task is " + task.Status + ", not InProgress.";
                return false;
            }
            int planned = hooks.Method.Phases[StageRepair].ElapsedMinutes;
            int work = Math.Max(1, Math.Min(minutes, Math.Max(1, planned - task.MinutesWorked)));
            if (!hooks.Tasks.RecordWorkProgress(task.TaskId, work, hooks.Budgets, hooks.DayIndex,
                out _, out reason)) return false;
            task = hooks.Tasks.FindTask(task.TaskId);
            if (task.MinutesWorked < planned) return true; // repair continues

            // Repair work done: honor the causal outcome (not a hardcoded Success).
            hooks.Tasks.CompleteTask(task.TaskId, order.ResolvedOutcome, hooks.DayIndex, hooks.Budgets);
            if (order.ResolvedOutcome == TaskOutcome.Success
                || order.ResolvedOutcome == TaskOutcome.AcceptableWithVariation)
            {
                ctx.Log("order " + order.OrderId + ": repair " + order.ResolvedOutcome
                    + " (quality " + order.ResolvedQualityDelta.ToString("+0.00;-0.00")
                    + ", waste x" + order.ResolvedWasteFactor.ToString("0.00") + ") — advancing to cooling.");
                return process.CompleteStage(StageRepair, hooks, order.SmithId, out reason);
            }

            // Failure: queue a real rework task; the stage does NOT advance.
            order.ReworkNeeded = true;
            order.FailureNote = "repair " + order.ResolvedOutcome + " (failure risk was "
                + order.ResolvedFailureRisk01.ToString("0.00") + ") — rework queued.";
            string reworkDefId = "forge:rework:" + order.OrderId;
            if (hooks.Tasks.GetDefinition(reworkDefId) == null)
            {
                hooks.Tasks.RegisterDefinition(new TaskDefinition(reworkDefId,
                    "Rework repair " + order.OrderId, 45), out _);
            }
            WorkTask rework = hooks.Tasks.CreateTask(reworkDefId, BusinessEntityId(ctx), hooks.DayIndex);
            if (!hooks.Tasks.AssignTask(rework.TaskId, order.SmithId, hooks.Budgets, out reason)
                || !hooks.Tasks.StartTask(rework.TaskId, hooks.DayIndex, out reason))
            {
                reason = "ForgeRepairChain: rework task could not start: " + reason;
                return false;
            }
            order.ReworkTaskId = rework.TaskId;
            ctx.Log("order " + order.OrderId + ": " + order.FailureNote);
            return true;
        }

        private bool WorkRework(ForgeRepairOrder order, int minutes, ForgeChainContext ctx, out string reason)
        {
            reason = string.Empty;
            WorkTask rework = ctx.Tasks.FindTask(order.ReworkTaskId);
            if (rework == null) { reason = "ForgeRepairChain: rework task vanished."; return false; }
            if (!ctx.Tasks.RecordWorkProgress(order.ReworkTaskId, minutes, ctx.Budgets, ctx.DayIndex,
                out bool done, out reason)) return false;
            if (!done) return true;
            ctx.Tasks.CompleteTask(order.ReworkTaskId, TaskOutcome.Success, ctx.DayIndex, ctx.Budgets);
            order.ReworkTaskId = EntityId.Invalid;
            order.ReworkNeeded = false;
            ctx.Log("order " + order.OrderId + ": rework complete — repair accepted, advancing to cooling.");
            return order.Process.CompleteStage(StageRepair, order.Hooks, order.SmithId, out reason);
        }

        private ProductionMethodDefinition BuildSixStepMethod(ForgeRepairOrder order)
        {
            var method = new ProductionMethodDefinition
            {
                MethodId = "forge.repair." + order.OrderId,
            };
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "receive", ElapsedMinutes = 10, RequiresPerson = true });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "inspect", ElapsedMinutes = 15, RequiresPerson = true });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "prepare", ElapsedMinutes = 20, RequiresPerson = true });
            method.Phases.Add(new ProductionPhaseDefinition
            {
                PhaseId = "repair",
                ElapsedMinutes = Math.Max(1, (int)Math.Ceiling(order.ResolvedRepairMinutes)),
                RequiresPerson = true,
            });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "cool", ElapsedMinutes = 30, RequiresPerson = false });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "return", ElapsedMinutes = 10, RequiresPerson = true });
            return method;
        }

        private ProductionStagePlan BuildSixStepPlan(ForgeChainContext ctx)
        {
            var plan = new ProductionStagePlan();
            string forgeKey = ForgeReservationKey(ctx);
            // Stages 0-2, 5: plain active labor. Stage 3 (repair) holds the forge.
            // Stage 4 (cool): passive, worker released.
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, 10f * 60f));
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, 15f * 60f));
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, 20f * 60f,
                0f, 0f, MissedDeadlineConsequence.None, 0f, "",
                new List<string> { forgeKey }));
            // Repair: duration set per-order below; placeholder replaced at begin.
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, 60f * 60f,
                0f, 0f, MissedDeadlineConsequence.None, 0f, "",
                new List<string> { forgeKey }));
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.PassiveProcess, 30f * 60f));
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, 10f * 60f));
            return plan;
        }

        private StagedProcessHooks BuildHooks(ForgeRepairOrder order, ForgeChainContext ctx)
        {
            return new StagedProcessHooks
            {
                Method = order.Method,
                Tasks = ctx.Tasks,
                Budgets = ctx.Budgets,
                DueEvents = ctx.DueEvents,
                Reservations = ctx.Reservations,
                ClockGameSeconds = ctx.ClockGameSeconds,
                DayIndex = ctx.DayIndex,
                BusinessId = BusinessEntityId(ctx),
                TaskBridge = new GenericProductionTaskBridge(ctx.Tasks, ctx.Budgets, BusinessEntityId(ctx), ctx.DayIndex),
                TendingWorkerId = order.SmithId,
                Diagnostics = ctx.Diagnostics,
            };
        }

        /// <summary>Save/load: mid-repair persistence. Materials are dispensed
        /// at begin and recorded on the order; a restore never re-dispenses.</summary>
        [Serializable]
        public sealed class ForgeRepairChainSaveDto
        {
            public int Sequence;
            public float CoalUnitsOnHand;
            public float IronUnitsOnHand;
            public List<ForgeRepairOrderSaveDto> Orders = new List<ForgeRepairOrderSaveDto>();
        }

        [Serializable]
        public sealed class ForgeRepairOrderSaveDto
        {
            public string OrderId = string.Empty;
            public string AssetId = string.Empty;
            public string AssetDescription = string.Empty;
            public EntityId SmithId = EntityId.Invalid;
            public double SmithCapability01;
            public float IronUnitsDispensed;
            public float CoalUnitsDispensed;
            public double ResolvedRepairMinutes;
            public double ResolvedQualityDelta;
            public double ResolvedWasteFactor;
            public double ResolvedFailureRisk01;
            public TaskOutcome ResolvedOutcome = TaskOutcome.Success;
            public EntityId ReworkTaskId = EntityId.Invalid;
            public bool ReworkNeeded;
            public bool IsComplete;
            public string FailureNote = string.Empty;
            public StagedProductionProcessSaveDto Process;
        }

        public ForgeRepairChainSaveDto CaptureSaveDto()
        {
            var dto = new ForgeRepairChainSaveDto
            {
                Sequence = sequence,
                CoalUnitsOnHand = CoalUnitsOnHand,
                IronUnitsOnHand = IronUnitsOnHand,
            };
            foreach (ForgeRepairOrder order in orders)
            {
                if (order == null) continue;
                dto.Orders.Add(new ForgeRepairOrderSaveDto
                {
                    OrderId = order.OrderId,
                    AssetId = order.AssetId,
                    AssetDescription = order.AssetDescription,
                    SmithId = order.SmithId,
                    SmithCapability01 = order.SmithCapability01,
                    IronUnitsDispensed = order.IronUnitsDispensed,
                    CoalUnitsDispensed = order.CoalUnitsDispensed,
                    ResolvedRepairMinutes = order.ResolvedRepairMinutes,
                    ResolvedQualityDelta = order.ResolvedQualityDelta,
                    ResolvedWasteFactor = order.ResolvedWasteFactor,
                    ResolvedFailureRisk01 = order.ResolvedFailureRisk01,
                    ResolvedOutcome = order.ResolvedOutcome,
                    ReworkTaskId = order.ReworkTaskId,
                    ReworkNeeded = order.ReworkNeeded,
                    IsComplete = order.IsComplete,
                    FailureNote = order.FailureNote,
                    Process = order.Process != null ? order.Process.CaptureSaveDto() : null,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(ForgeRepairChainSaveDto dto, ForgeChainContext ctx)
        {
            orders.Clear();
            nailRuns.Clear();
            if (dto == null) return;
            sequence = dto.Sequence;
            CoalUnitsOnHand = dto.CoalUnitsOnHand;
            IronUnitsOnHand = dto.IronUnitsOnHand;
            if (dto.Orders == null) return;
            foreach (ForgeRepairOrderSaveDto od in dto.Orders)
            {
                if (od == null) continue;
                var order = new ForgeRepairOrder
                {
                    OrderId = od.OrderId,
                    AssetId = od.AssetId,
                    AssetDescription = od.AssetDescription,
                    SmithId = od.SmithId,
                    SmithCapability01 = od.SmithCapability01,
                    Method = BuildRepairMethod(),
                    IronUnitsDispensed = od.IronUnitsDispensed,
                    CoalUnitsDispensed = od.CoalUnitsDispensed,
                    ResolvedRepairMinutes = od.ResolvedRepairMinutes,
                    ResolvedQualityDelta = od.ResolvedQualityDelta,
                    ResolvedWasteFactor = od.ResolvedWasteFactor,
                    ResolvedFailureRisk01 = od.ResolvedFailureRisk01,
                    ResolvedOutcome = od.ResolvedOutcome,
                    ReworkTaskId = od.ReworkTaskId,
                    ReworkNeeded = od.ReworkNeeded,
                    IsComplete = od.IsComplete,
                    FailureNote = od.FailureNote ?? string.Empty,
                };
                var process = new StagedProductionProcess();
                process.LoadFromSaveDto(od.Process);
                order.Process = process;
                order.Hooks = BuildHooks(order, ctx);
                orders.Add(order);
            }
        }
    }
}
